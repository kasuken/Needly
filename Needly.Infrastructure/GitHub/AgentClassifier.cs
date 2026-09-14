namespace Needly.Infrastructure.GitHub;

/// <summary>Identifies a specific agent or bot detected on GitHub activity.</summary>
/// <param name="Key">
/// The stable identity key (e.g. "dependabot"), or the configured fallback key for a recognized-but-
/// unmatched bot (e.g. "other-bot"). Persisted as <see cref="Needly.Domain.NeedlyAction.AgentAuthor"/>.
/// </param>
/// <param name="DisplayName">The human-readable display name, persisted as <see cref="Needly.Domain.NeedlyAction.AgentDisplayName"/>.</param>
public sealed record AgentIdentity(string Key, string DisplayName);

/// <summary>
/// Classifies the specific agent/bot behind a GitHub login, sender type and (where available) GitHub App
/// slug, using the data-driven ruleset in <see cref="AgentDetectionOptions"/> (issue #35). A new agent can
/// be recognized by editing configuration rather than shipping a code change.
/// </summary>
public sealed class AgentClassifier(AgentDetectionOptions options)
{
    private readonly AgentDetectionOptions options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Identifies the specific agent/bot behind the supplied facts, or <see langword="null"/> when there
    /// is no bot involvement at all. Activity that looks like a bot (see <see cref="IsBot"/>) but matches
    /// no specific rule returns the configured fallback identity, preserving today's
    /// <c>HasBotInvolvement</c>-only behavior for unrecognized bots.
    /// </summary>
    /// <param name="login">The GitHub login, when known.</param>
    /// <param name="type">The GitHub user "type" field (e.g. "Bot"), when known.</param>
    /// <param name="appSlug">The GitHub App slug associated with the activity, when known.</param>
    public AgentIdentity? Classify(string? login, string? type, string? appSlug)
    {
        foreach (var rule in options.Rules)
        {
            if (Matches(rule, login, appSlug))
            {
                return new AgentIdentity(rule.Key, rule.DisplayName);
            }
        }

        return IsBot(login, type)
            ? new AgentIdentity(options.OtherBotKey, options.OtherBotDisplayName)
            : null;
    }

    /// <summary>Determines whether the supplied login or sender type marks the activity as a bot.</summary>
    /// <remarks>
    /// Mirrors <c>GitHubActionEventHandler.IsBot</c>, which remains the source of truth for
    /// <see cref="Needly.Domain.NeedlyAction.HasBotInvolvement"/> and is left unchanged by issue #35.
    /// </remarks>
    public static bool IsBot(string? login, string? type) =>
        string.Equals(type, "Bot", StringComparison.OrdinalIgnoreCase) ||
        login?.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) == true;

    private static bool Matches(AgentDetectionRule rule, string? login, string? appSlug) =>
        (login is not null && rule.LoginEquals.Any(
            candidate => string.Equals(candidate, login, StringComparison.OrdinalIgnoreCase))) ||
        (login is not null && rule.LoginEndsWith.Any(
            suffix => login.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) ||
        (appSlug is not null && rule.AppSlugEquals.Any(
            candidate => string.Equals(candidate, appSlug, StringComparison.OrdinalIgnoreCase)));
}
