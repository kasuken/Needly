using Needly.Domain;

namespace Needly.Application.GitHub;

/// <summary>Persists and links a GitHub identity to a Needly account.</summary>
public interface IGitHubIdentityService
{
    /// <summary>Creates or updates the account represented by a verified GitHub profile.</summary>
    Task<SignedInNeedlyUser> UpsertAsync(
        GitHubIdentityProfile profile,
        CancellationToken cancellationToken);
}

/// <summary>Maintains the durable inventory of GitHub App installations and repositories.</summary>
public interface IInstallationInventoryService
{
    /// <summary>Applies an installation lifecycle webhook.</summary>
    Task HandleInstallationAsync(
        GitHubInstallationEvent installationEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    /// <summary>Applies an installation repository-selection webhook.</summary>
    Task HandleRepositoriesAsync(
        GitHubInstallationRepositoriesEvent repositoriesEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    /// <summary>Links an authenticated Needly user to a GitHub App installation.</summary>
    Task LinkUserAsync(
        Guid needlyUserId,
        long gitHubInstallationId,
        DateTimeOffset linkedAt,
        CancellationToken cancellationToken);
}

/// <summary>Reads the GitHub installation inventory shown in Settings.</summary>
public interface IGitHubSettingsService
{
    /// <summary>Gets installations linked to a Needly user.</summary>
    Task<GitHubSettings> GetAsync(Guid needlyUserId, CancellationToken cancellationToken);
}

/// <summary>Creates signed GitHub App JSON Web Tokens.</summary>
public interface IGitHubAppJwtProvider
{
    /// <summary>Creates a short-lived RS256 GitHub App token.</summary>
    string CreateToken();
}

/// <summary>Gets cached installation access tokens and refreshes them before expiration.</summary>
public interface IGitHubInstallationTokenProvider
{
    /// <summary>Gets a valid token for an active installation.</summary>
    Task<GitHubInstallationAccessToken> GetAsync(
        long gitHubInstallationId,
        CancellationToken cancellationToken);
}

/// <summary>Sends authenticated requests for one GitHub App installation.</summary>
public interface IGitHubApiClient
{
    /// <summary>Sends a request relative to the GitHub API base address.</summary>
    Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken cancellationToken);
}

/// <summary>Creates API clients authenticated for a specific active installation.</summary>
public interface IGitHubApiClientFactory
{
    /// <summary>Creates an authenticated client for an active installation.</summary>
    Task<IGitHubApiClient> CreateAsync(
        long gitHubInstallationId,
        CancellationToken cancellationToken);
}

    /// <summary>Loads authoritative ready-to-merge state for a pull request.</summary>
    public interface IGitHubPullRequestLookup
    {
        /// <summary>Gets the current pull request, review, check, and mergeability state.</summary>
        Task<GitHubPullRequestReadiness?> GetAsync(
        long gitHubInstallationId,
        string repositoryOwner,
        string repositoryName,
        int pullRequestNumber,
        CancellationToken cancellationToken);
    }

/// <summary>Authenticates and durably accepts GitHub webhook deliveries.</summary>
public interface IGitHubWebhookIngestionService
{
    /// <summary>Validates and persists one bounded webhook request.</summary>
    Task<GitHubWebhookReceipt> IngestAsync(
        GitHubWebhookRequest request,
        CancellationToken cancellationToken);
}

/// <summary>Provides bounded in-process signaling for durable webhook events.</summary>
public interface IGitHubWebhookQueue
{
    /// <summary>Queues a stored event identifier, waiting for bounded capacity.</summary>
    ValueTask EnqueueAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>Reads queued event identifiers until cancellation or completion.</summary>
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
}

/// <summary>Dispatches one durable GitHub webhook event.</summary>
public interface IGitHubWebhookDispatcher
{
    /// <summary>Processes one stored event and persists its resulting status.</summary>
    Task DispatchAsync(Guid eventId, CancellationToken cancellationToken);
}

/// <summary>Recovers durable webhook work after process restart.</summary>
public interface IGitHubWebhookRecoveryService
{
    /// <summary>Repairs interrupted state and queues all pending or retryable events.</summary>
    Task<int> RecoverAsync(CancellationToken cancellationToken);
}

/// <summary>Creates durable action events from current GitHub state for newly available repositories.</summary>
public interface IGitHubHistoricalBootstrapService
{
    /// <summary>Bootstraps the next bounded batch of eligible repositories.</summary>
    Task<int> BootstrapNextBatchAsync(CancellationToken cancellationToken);
}

/// <summary>Handles verified GitHub events that may produce inbox actions.</summary>
public interface IGitHubActionEventHandler
{
    /// <summary>Handles one known, durably stored action event.</summary>
    Task HandleAsync(GitHubStoredEvent storedEvent, CancellationToken cancellationToken);
}

/// <summary>Maintains installation-scoped organization and team membership inventory.</summary>
public interface IGitHubOrganizationMembershipService
{
    /// <summary>Synchronizes members, teams, and team members from the installation API.</summary>
    Task SyncAsync(long gitHubInstallationId, CancellationToken cancellationToken);

    /// <summary>Applies an organization member webhook.</summary>
    Task HandleMemberAsync(
        GitHubMemberEvent memberEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    /// <summary>Applies a team lifecycle webhook.</summary>
    Task HandleTeamAsync(
        GitHubTeamEvent teamEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    /// <summary>Applies a team membership webhook.</summary>
    Task HandleMembershipAsync(
        GitHubMembershipEvent membershipEvent,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}

/// <summary>Resolves installation-scoped teams and their active members.</summary>
public interface ITeamReviewResolver
{
    /// <summary>Resolves a GitHub team identifier for one installation.</summary>
    Task<TeamReviewTarget?> ResolveAsync(
        long gitHubInstallationId,
        long gitHubTeamId,
        CancellationToken cancellationToken);
}

/// <summary>Queries actions visible to one authenticated Needly user.</summary>
public interface IInboxVisibilityService
{
    /// <summary>Gets actions routed directly or through active teams in active memberships.</summary>
    Task<IReadOnlyList<VisibleAction>> GetVisibleAsync(
        Guid needlyUserId,
        CancellationToken cancellationToken);

    /// <summary>Gets visible actions that match the shared Saved View and Rule filter semantics.</summary>
    Task<IReadOnlyList<VisibleAction>> GetVisibleAsync(
        Guid needlyUserId,
        ActionFilter filter,
        CancellationToken cancellationToken);
}