namespace Needly.Domain;

/// <summary>
/// Represents a GitHub repository visible to an installation.
/// </summary>
public sealed class Repository
{
    private Repository()
    {
    }

    /// <summary>Gets the internal repository identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the owning installation identifier.</summary>
    public Guid InstallationId { get; private set; }

    /// <summary>Gets the GitHub repository identifier.</summary>
    public long GitHubRepositoryId { get; private set; }

    /// <summary>Gets the repository owner login.</summary>
    public string Owner { get; private set; } = string.Empty;

    /// <summary>Gets the repository name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets whether the installation can currently access the repository.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets when the repository record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when repository metadata was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates a repository.
    /// </summary>
    /// <param name="id">The internal identifier.</param>
    /// <param name="installationId">The owning installation identifier.</param>
    /// <param name="gitHubRepositoryId">The GitHub repository identifier.</param>
    /// <param name="owner">The repository owner login.</param>
    /// <param name="name">The repository name.</param>
    /// <param name="createdAt">The explicit creation timestamp.</param>
    /// <returns>A new repository.</returns>
    public static Repository Create(
        Guid id,
        Guid installationId,
        long gitHubRepositoryId,
        string owner,
        string name,
        DateTimeOffset createdAt)
    {
        var timestamp = DomainGuard.Timestamp(createdAt);
        return new Repository
        {
            Id = DomainGuard.Required(id, nameof(id)),
            InstallationId = DomainGuard.Required(installationId, nameof(installationId)),
            GitHubRepositoryId = DomainGuard.Positive(gitHubRepositoryId, nameof(gitHubRepositoryId)),
            Owner = DomainGuard.Required(owner, 100, nameof(owner)),
            Name = DomainGuard.Required(name, 100, nameof(name)),
            IsActive = true,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>
    /// Updates repository naming metadata.
    /// </summary>
    /// <param name="owner">The current owner login.</param>
    /// <param name="name">The current repository name.</param>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Update(string owner, string name, DateTimeOffset updatedAt)
    {
        Owner = DomainGuard.Required(owner, 100, nameof(owner));
        Name = DomainGuard.Required(name, 100, nameof(name));
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        IsActive = true;
    }

    /// <summary>Marks the repository unavailable while retaining its durable history.</summary>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Deactivate(DateTimeOffset updatedAt)
    {
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        IsActive = false;
    }
}