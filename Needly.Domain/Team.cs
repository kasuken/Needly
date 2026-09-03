namespace Needly.Domain;

/// <summary>
/// Represents a GitHub team that can own actions.
/// </summary>
public sealed class Team
{
    private Team()
    {
    }

    /// <summary>Gets the internal team identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning installation identifier.</summary>
    public Guid InstallationId { get; private set; }

    /// <summary>Gets the GitHub team identifier.</summary>
    public long GitHubTeamId { get; private set; }

    /// <summary>Gets the GitHub team slug.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Gets the GitHub team name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets whether the team currently exists for this installation.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets when the team record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when team metadata was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a team.
    /// </summary>
    /// <param name="id">The internal identifier.</param>
    /// <param name="installationId">The owning installation identifier.</param>
    /// <param name="gitHubTeamId">The GitHub team identifier.</param>
    /// <param name="slug">The GitHub team slug.</param>
    /// <param name="name">The GitHub team name.</param>
    /// <param name="createdAt">The explicit creation timestamp.</param>
    /// <returns>A new team.</returns>
    public static Team Create(
        Guid id,
        Guid installationId,
        long gitHubTeamId,
        string slug,
        string name,
        DateTimeOffset createdAt)
    {
        var timestamp = DomainGuard.Timestamp(createdAt);
        return new Team
        {
            Id = DomainGuard.Required(id, nameof(id)),
            InstallationId = DomainGuard.Required(installationId, nameof(installationId)),
            GitHubTeamId = DomainGuard.Positive(gitHubTeamId, nameof(gitHubTeamId)),
            Slug = DomainGuard.Required(slug, 100, nameof(slug)),
            Name = DomainGuard.Required(name, 200, nameof(name)),
            IsActive = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>
    /// Updates team naming metadata.
    /// </summary>
    /// <param name="slug">The current GitHub team slug.</param>
    /// <param name="name">The current GitHub team name.</param>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Update(string slug, string name, DateTimeOffset updatedAt)
    {
        Slug = DomainGuard.Required(slug, 100, nameof(slug));
        Name = DomainGuard.Required(name, 200, nameof(name));
        IsActive = true;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }

    /// <summary>Marks the team as no longer available from GitHub.</summary>
    public void Deactivate(DateTimeOffset updatedAt)
    {
        IsActive = false;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }
}