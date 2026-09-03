namespace Needly.Domain;

/// <summary>
/// Links a Needly user to a GitHub App installation selected during setup.
/// </summary>
public sealed class UserInstallation
{
    private UserInstallation()
    {
    }

    /// <summary>Gets the link identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the Needly user identifier.</summary>
    public Guid NeedlyUserId { get; private set; }

    /// <summary>Gets the GitHub installation identifier.</summary>
    public long GitHubInstallationId { get; private set; }

    /// <summary>Gets when the link was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a user-to-installation link.
    /// </summary>
    /// <param name="id">The link identifier.</param>
    /// <param name="needlyUserId">The Needly user identifier.</param>
    /// <param name="gitHubInstallationId">The GitHub installation identifier.</param>
    /// <param name="createdAt">The explicit creation timestamp.</param>
    /// <returns>A new user-to-installation link.</returns>
    public static UserInstallation Create(
        Guid id,
        Guid needlyUserId,
        long gitHubInstallationId,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = DomainGuard.Required(id, nameof(id)),
            NeedlyUserId = DomainGuard.Required(needlyUserId, nameof(needlyUserId)),
            GitHubInstallationId = DomainGuard.Positive(gitHubInstallationId, nameof(gitHubInstallationId)),
            CreatedAt = DomainGuard.Timestamp(createdAt)
        };
}