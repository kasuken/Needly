namespace Needly.Domain;

/// <summary>
/// Represents a validated HTTPS link to a GitHub issue or pull request.
/// </summary>
public readonly record struct GitHubDeepLink
{
    private const int MaximumLength = 2048;

    private GitHubDeepLink(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the absolute GitHub URL.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Parses a canonical GitHub issue or pull request deep link.
    /// </summary>
    /// <param name="value">The persisted absolute GitHub URL.</param>
    /// <returns>The parsed GitHub deep link.</returns>
    public static GitHubDeepLink Parse(string value)
    {
        var normalizedValue = DomainGuard.Required(value, MaximumLength, nameof(value));
        if (!Uri.TryCreate(normalizedValue, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new FormatException("A canonical HTTPS github.com deep link is required.");
        }

        var pathSegments = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length != 4 ||
            pathSegments[0].Length == 0 ||
            pathSegments[1].Length == 0 ||
            pathSegments[2] is not ("pull" or "issues") ||
            !int.TryParse(pathSegments[3], out var subjectNumber) ||
            subjectNumber <= 0)
        {
            throw new FormatException("The URL is not a GitHub issue or pull request deep link.");
        }

        return new GitHubDeepLink(uri.AbsoluteUri);
    }

    /// <summary>
    /// Creates and validates a deep link for a repository subject.
    /// </summary>
    /// <param name="value">The absolute GitHub URL.</param>
    /// <param name="repositoryOwner">The expected repository owner.</param>
    /// <param name="repositoryName">The expected repository name.</param>
    /// <param name="subjectType">The expected subject type.</param>
    /// <param name="subjectNumber">The expected subject number.</param>
    /// <returns>A validated GitHub deep link.</returns>
    public static GitHubDeepLink Create(
        string value,
        string repositoryOwner,
        string repositoryName,
        GitHubSubjectType subjectType,
        int subjectNumber)
    {
        var normalizedValue = DomainGuard.Required(value, MaximumLength, nameof(value));
        var expectedOwner = DomainGuard.Required(repositoryOwner, 100, nameof(repositoryOwner));
        var expectedRepository = DomainGuard.Required(repositoryName, 100, nameof(repositoryName));
        DomainGuard.Positive(subjectNumber, nameof(subjectNumber));

        GitHubDeepLink deepLink;
        try
        {
            deepLink = Parse(normalizedValue);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(exception.Message, nameof(value), exception);
        }

        var pathSegments = new Uri(deepLink.Value).GetComponents(UriComponents.Path, UriFormat.Unescaped)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var expectedSubjectSegment = subjectType == GitHubSubjectType.PullRequest ? "pull" : "issues";

        if (pathSegments.Length != 4 ||
            !string.Equals(pathSegments[0], expectedOwner, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(pathSegments[1], expectedRepository, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(pathSegments[2], expectedSubjectSegment, StringComparison.Ordinal) ||
            !int.TryParse(pathSegments[3], out var linkedSubjectNumber) ||
            linkedSubjectNumber != subjectNumber)
        {
            throw new ArgumentException("The GitHub deep link does not match the supplied subject.", nameof(value));
        }

        return deepLink;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}