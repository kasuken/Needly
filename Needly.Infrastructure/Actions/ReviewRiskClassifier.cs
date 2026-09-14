using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>Contains the outcome of a review risk classification (issue #34).</summary>
/// <param name="Level">The overall review risk level: the highest level among <paramref name="MatchedSignals"/>.</param>
/// <param name="MatchedSignals">
/// The matched risk signal names, ordered from the highest to the lowest contributing level. Never
/// reduced to just <paramref name="Level"/> without these reasons.
/// </param>
public sealed record ReviewRiskClassification(ReviewRiskLevel Level, string[] MatchedSignals);

/// <summary>Classifies pull request review risk from changed file paths and diff size.</summary>
public interface IReviewRiskClassifier
{
    /// <summary>
    /// Classifies review risk from a pull request's changed file paths and total changed line count.
    /// </summary>
    /// <param name="changedFilePaths">
    /// The pull request's changed file paths, or null when they could not be fetched (for example, a
    /// rate-limited or failed GitHub API call). A null list always degrades to
    /// <see cref="ReviewRiskLevel.Unknown"/>, never to <see cref="ReviewRiskLevel.Low"/>.
    /// </param>
    /// <param name="additions">The number of added lines, when known.</param>
    /// <param name="deletions">The number of deleted lines, when known.</param>
    ReviewRiskClassification Classify(IReadOnlyList<string>? changedFilePaths, int? additions, int? deletions);
}

/// <summary>
/// Classifies review risk using the configurable path-pattern and diff-size signals in
/// <see cref="ReviewRiskOptions"/>.
/// </summary>
public sealed class ReviewRiskClassifier(IOptions<ReviewRiskOptions> options) : IReviewRiskClassifier
{
    private static readonly ConcurrentDictionary<string, Regex> PatternCache = new();

    private readonly ReviewRiskOptions options = options.Value;

    /// <inheritdoc />
    public ReviewRiskClassification Classify(IReadOnlyList<string>? changedFilePaths, int? additions, int? deletions)
    {
        if (changedFilePaths is null)
        {
            return new ReviewRiskClassification(ReviewRiskLevel.Unknown, []);
        }

        var matched = new List<(string Name, ReviewRiskLevel Level)>();
        foreach (var signal in options.Signals)
        {
            if (changedFilePaths.Any(path => MatchesAny(signal.PathPatterns, path)))
            {
                matched.Add((signal.Name, signal.Level));
            }
        }

        if (changedFilePaths.Count > 0 && changedFilePaths.All(path => MatchesAny(options.TestPathPatterns, path)))
        {
            matched.Add(("tests only", ReviewRiskLevel.Low));
        }

        var changedLines = Math.Max(0, additions ?? 0) + Math.Max(0, deletions ?? 0);
        if (changedLines >= options.LargeDiffChangedLines)
        {
            matched.Add(("large diff", ReviewRiskLevel.Medium));
        }

        if (matched.Count == 0)
        {
            // No configured signal matched: treat as ordinary, low-attention review risk. This is
            // distinct from Unknown, which is reserved for "the changed-file list was unavailable".
            return new ReviewRiskClassification(ReviewRiskLevel.Low, []);
        }

        var level = matched.Max(signal => signal.Level);
        var signals = matched
            .OrderByDescending(signal => signal.Level)
            .Select(signal => signal.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ReviewRiskClassification(level, signals);
    }

    private static bool MatchesAny(IReadOnlyList<string> patterns, string path) =>
        patterns.Count > 0 && patterns.Any(pattern => GetRegex(pattern).IsMatch(NormalizePath(path)));

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static Regex GetRegex(string pattern) =>
        PatternCache.GetOrAdd(pattern, static value => new Regex(
            ConvertGlobToRegex(value),
            RegexOptions.IgnoreCase | RegexOptions.Compiled));

    /// <summary>
    /// Converts a glob path pattern (<c>**</c>, <c>*</c>, <c>?</c>) to an anchored, case-insensitive
    /// regular expression. Longer wildcard sequences are replaced before shorter ones so a trailing
    /// single <c>*</c> replacement never re-matches an already-converted <c>**</c> sequence.
    /// </summary>
    private static string ConvertGlobToRegex(string glob)
    {
        var escaped = Regex.Escape(glob)
            .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal);
        return $"^{escaped}$";
    }
}
