namespace Needly.Domain;

/// <summary>
/// Identifies whether a GitHub App installation can access GitHub APIs.
/// </summary>
public enum InstallationState
{
    /// <summary>The installation is available for API access.</summary>
    Active = 0,

    /// <summary>The installation was suspended by GitHub.</summary>
    Suspended = 1,

    /// <summary>The GitHub App was uninstalled.</summary>
    Deleted = 2
}

/// <summary>
/// Identifies the kind of GitHub account that owns an installation.
/// </summary>
public enum GitHubAccountType
{
    /// <summary>A personal GitHub account.</summary>
    User = 0,

    /// <summary>A GitHub organization.</summary>
    Organization = 1
}

/// <summary>
/// Represents a GitHub App installation that owns an isolated Needly workspace.
/// </summary>
public sealed class Installation
{
    private Installation()
    {
    }

    /// <summary>Gets the internal installation identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the GitHub installation identifier.</summary>
    public long GitHubInstallationId { get; private set; }

    /// <summary>Gets the GitHub account login that owns the installation.</summary>
    public string AccountLogin { get; private set; } = string.Empty;

    /// <summary>Gets the kind of GitHub account that owns the installation.</summary>
    public GitHubAccountType AccountType { get; private set; }

    /// <summary>Gets the installation lifecycle state.</summary>
    public InstallationState State { get; private set; }

    /// <summary>Gets whether GitHub API access is currently allowed.</summary>
    public bool IsActive => State == InstallationState.Active;

    /// <summary>Gets when the installation was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the installation was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Creates an installation.
    /// </summary>
    /// <param name="id">The internal identifier.</param>
    /// <param name="gitHubInstallationId">The GitHub installation identifier.</param>
    /// <param name="accountLogin">The owning account login.</param>
    /// <param name="createdAt">The explicit creation timestamp.</param>
    /// <returns>A new installation.</returns>
    public static Installation Create(
        Guid id,
        long gitHubInstallationId,
        string accountLogin,
        DateTimeOffset createdAt,
        GitHubAccountType accountType = GitHubAccountType.User)
    {
        var timestamp = DomainGuard.Timestamp(createdAt);
        return new Installation
        {
            Id = DomainGuard.Required(id, nameof(id)),
            GitHubInstallationId = DomainGuard.Positive(gitHubInstallationId, nameof(gitHubInstallationId)),
            AccountLogin = DomainGuard.Required(accountLogin, 100, nameof(accountLogin)),
            AccountType = accountType,
            State = InstallationState.Active,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    /// <summary>
    /// Updates mutable installation metadata.
    /// </summary>
    /// <param name="accountLogin">The current owning account login.</param>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Update(string accountLogin, DateTimeOffset updatedAt)
    {
        AccountLogin = DomainGuard.Required(accountLogin, 100, nameof(accountLogin));
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
    }

    /// <summary>
    /// Marks a suspended or previously deleted installation as active.
    /// </summary>
    /// <param name="accountLogin">The current owning account login.</param>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Activate(string accountLogin, DateTimeOffset updatedAt)
    {
        Activate(accountLogin, AccountType, updatedAt);
    }

    /// <summary>
    /// Marks a suspended or previously deleted installation as active and refreshes account metadata.
    /// </summary>
    /// <param name="accountLogin">The current owning account login.</param>
    /// <param name="accountType">The current owning account type.</param>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Activate(
        string accountLogin,
        GitHubAccountType accountType,
        DateTimeOffset updatedAt)
    {
        Update(accountLogin, updatedAt);
        AccountType = accountType;
        State = InstallationState.Active;
    }

    /// <summary>
    /// Marks the installation as suspended so API access is refused.
    /// </summary>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Suspend(DateTimeOffset updatedAt)
    {
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        State = InstallationState.Suspended;
    }

    /// <summary>
    /// Marks the installation as deleted after the GitHub App is uninstalled.
    /// </summary>
    /// <param name="updatedAt">The explicit update timestamp.</param>
    public void Delete(DateTimeOffset updatedAt)
    {
        UpdatedAt = DomainGuard.NotBefore(updatedAt, CreatedAt, nameof(updatedAt));
        State = InstallationState.Deleted;
    }
}