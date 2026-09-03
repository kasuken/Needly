namespace Needly.Domain;

/// <summary>Represents a GitHub user's membership in one installation-scoped team.</summary>
public sealed class TeamMember
{
    private TeamMember()
    {
    }

    /// <summary>Gets the membership identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the team identifier.</summary>
    public Guid TeamId { get; private set; }

    /// <summary>Gets the internal GitHub user identifier.</summary>
    public Guid GitHubUserId { get; private set; }

    /// <summary>Gets whether the membership is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets when the membership was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the membership was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an active team membership.</summary>
    public static TeamMember Create(Guid id, Guid teamId, Guid gitHubUserId, DateTimeOffset createdAt)
    {
        var timestamp = DomainGuard.Timestamp(createdAt);
        return new TeamMember
        {
            Id = DomainGuard.Required(id, nameof(id)),
            TeamId = DomainGuard.Required(teamId, nameof(teamId)),
            GitHubUserId = DomainGuard.Required(gitHubUserId, nameof(gitHubUserId)),
            IsActive = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>Marks the team membership as active.</summary>
    public void Activate(DateTimeOffset updatedAt)
    {
        IsActive = true;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }

    /// <summary>Marks the team membership as inactive.</summary>
    public void Deactivate(DateTimeOffset updatedAt)
    {
        IsActive = false;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }
}