namespace Needly.Domain;

/// <summary>
/// Represents a GitHub user known to Needly.
/// </summary>
public sealed class GitHubUser
{
    private GitHubUser()
    {
    }

    /// <summary>Gets the internal user identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the GitHub user identifier.</summary>
    public long GitHubUserId { get; private set; }

    /// <summary>Gets the GitHub login.</summary>
    public string Login { get; private set; } = string.Empty;

    /// <summary>Gets the optional display name.</summary>
    public string? DisplayName { get; private set; }

    /// <summary>Gets the optional avatar URL.</summary>
    public string? AvatarUrl { get; private set; }

    /// <summary>Gets when the GitHub user record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when GitHub user metadata was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a GitHub user.
    /// </summary>
    /// <param name="id">The internal identifier.</param>
    /// <param name="gitHubUserId">The GitHub user identifier.</param>
    /// <param name="login">The GitHub login.</param>
    /// <param name="displayName">The optional display name.</param>
    /// <param name="avatarUrl">The optional avatar URL.</param>
    /// <param name="createdAt">The explicit creation timestamp.</param>
    /// <returns>A new GitHub user.</returns>
    public static GitHubUser Create(
        Guid id,
        long gitHubUserId,
        string login,
        string? displayName,
        string? avatarUrl,
        DateTimeOffset createdAt)
    {
        var timestamp = DomainGuard.Timestamp(createdAt);
        return new GitHubUser
        {
            Id = DomainGuard.Required(id, nameof(id)),
            GitHubUserId = DomainGuard.Positive(gitHubUserId, nameof(gitHubUserId)),
            Login = DomainGuard.Required(login, 100, nameof(login)),
            DisplayName = DomainGuard.Optional(displayName, 200, nameof(displayName)),
            AvatarUrl = DomainGuard.Optional(avatarUrl, 2048, nameof(avatarUrl)),
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>
    /// Updates GitHub profile metadata.
    /// </summary>
    /// <param name="login">The current GitHub login.</param>
    /// <param name="displayName">The optional display name.</param>
    /// <param name="avatarUrl">The optional avatar URL.</param>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Update(string login, string? displayName, string? avatarUrl, DateTimeOffset updatedAt)
    {
        Login = DomainGuard.Required(login, 100, nameof(login));
        DisplayName = DomainGuard.Optional(displayName, 200, nameof(displayName));
        AvatarUrl = DomainGuard.Optional(avatarUrl, 2048, nameof(avatarUrl));
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }
}

/// <summary>
/// Represents a Needly account linked to a GitHub identity.
/// </summary>
public sealed class NeedlyUser
{
    private NeedlyUser()
    {
    }

    /// <summary>Gets the Needly user identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the linked GitHub user identifier.</summary>
    public Guid GitHubUserId { get; private set; }

    /// <summary>Gets the user's email address.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Gets the user's preferred display name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Gets when the Needly account was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the Needly account was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a Needly user.
    /// </summary>
    /// <param name="id">The Needly user identifier.</param>
    /// <param name="gitHubUserId">The linked GitHub user identifier.</param>
    /// <param name="email">The user's email address.</param>
    /// <param name="displayName">The preferred display name.</param>
    /// <param name="createdAt">The explicit creation timestamp.</param>
    /// <returns>A new Needly user.</returns>
    public static NeedlyUser Create(
        Guid id,
        Guid gitHubUserId,
        string email,
        string displayName,
        DateTimeOffset createdAt)
    {
        var timestamp = DomainGuard.Timestamp(createdAt);
        return new NeedlyUser
        {
            Id = DomainGuard.Required(id, nameof(id)),
            GitHubUserId = DomainGuard.Required(gitHubUserId, nameof(gitHubUserId)),
            Email = DomainGuard.Required(email, 320, nameof(email)),
            DisplayName = DomainGuard.Required(displayName, 200, nameof(displayName)),
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>
    /// Updates Needly account profile data.
    /// </summary>
    /// <param name="email">The current email address.</param>
    /// <param name="displayName">The preferred display name.</param>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Update(string email, string displayName, DateTimeOffset updatedAt)
    {
        Email = DomainGuard.Required(email, 320, nameof(email));
        DisplayName = DomainGuard.Required(displayName, 200, nameof(displayName));
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }
}