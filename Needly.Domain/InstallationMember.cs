namespace Needly.Domain;

/// <summary>Represents a GitHub user's membership in one App installation.</summary>
public sealed class InstallationMember
{
    private InstallationMember()
    {
    }

    /// <summary>Gets the membership identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the internal installation identifier.</summary>
    public Guid InstallationId { get; private set; }

    /// <summary>Gets the internal GitHub user identifier.</summary>
    public Guid GitHubUserId { get; private set; }

    /// <summary>Gets whether the membership is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets when the membership was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the membership was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an active installation membership.</summary>
    public static InstallationMember Create(
        Guid id,
        Guid installationId,
        Guid gitHubUserId,
        DateTimeOffset createdAt)
    {
        var timestamp = DomainGuard.Timestamp(createdAt);
        return new InstallationMember
        {
            Id = DomainGuard.Required(id, nameof(id)),
            InstallationId = DomainGuard.Required(installationId, nameof(installationId)),
            GitHubUserId = DomainGuard.Required(gitHubUserId, nameof(gitHubUserId)),
            IsActive = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>Marks the membership as active.</summary>
    public void Activate(DateTimeOffset updatedAt)
    {
        IsActive = true;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }

    /// <summary>Marks the membership as inactive.</summary>
    public void Deactivate(DateTimeOffset updatedAt)
    {
        IsActive = false;
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }
}