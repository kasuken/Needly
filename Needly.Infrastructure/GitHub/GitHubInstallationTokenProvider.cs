using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Returns cached tokens for active GitHub App installations.</summary>
public sealed class GitHubInstallationTokenProvider(
    NeedlyDbContext dbContext,
    GitHubInstallationTokenClient tokenClient,
    GitHubInstallationTokenCache tokenCache,
    TimeProvider timeProvider) : IGitHubInstallationTokenProvider
{
    private static readonly TimeSpan RefreshBeforeExpiration = TimeSpan.FromMinutes(1);
    private readonly NeedlyDbContext dbContext = dbContext
        ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly GitHubInstallationTokenClient tokenClient = tokenClient
        ?? throw new ArgumentNullException(nameof(tokenClient));
    private readonly GitHubInstallationTokenCache tokenCache = tokenCache
        ?? throw new ArgumentNullException(nameof(tokenCache));
    private readonly TimeProvider timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task<GitHubInstallationAccessToken> GetAsync(
        long gitHubInstallationId,
        CancellationToken cancellationToken)
    {
        var installation = await dbContext.Installations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.GitHubInstallationId == gitHubInstallationId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"GitHub installation {gitHubInstallationId} was not found.");
        if (!installation.IsActive)
        {
            throw new InvalidOperationException(
                $"GitHub installation {gitHubInstallationId} is not active.");
        }

        var now = timeProvider.GetUtcNow();
        if (tokenCache.TryGet(gitHubInstallationId, now, RefreshBeforeExpiration, out var cached))
        {
            return cached;
        }

        var refreshLock = tokenCache.GetRefreshLock(gitHubInstallationId);
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = timeProvider.GetUtcNow();
            if (tokenCache.TryGet(gitHubInstallationId, now, RefreshBeforeExpiration, out cached))
            {
                return cached;
            }

            var created = await tokenClient
                .CreateAsync(gitHubInstallationId, cancellationToken)
                .ConfigureAwait(false);
            if (created.ExpiresAt <= now)
            {
                throw new InvalidOperationException("GitHub returned an expired installation access token.");
            }

            tokenCache.Set(gitHubInstallationId, created);
            return created;
        }
        finally
        {
            refreshLock.Release();
        }
    }
}

/// <summary>Stores installation tokens and serializes refreshes per installation.</summary>
public sealed class GitHubInstallationTokenCache
{
    private readonly ConcurrentDictionary<long, GitHubInstallationAccessToken> tokens = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> refreshLocks = new();

    internal bool TryGet(
        long installationId,
        DateTimeOffset now,
        TimeSpan refreshBeforeExpiration,
        out GitHubInstallationAccessToken token)
    {
        if (tokens.TryGetValue(installationId, out var cached) &&
            cached.ExpiresAt > now.Add(refreshBeforeExpiration))
        {
            token = cached;
            return true;
        }

        token = null!;
        return false;
    }

    internal SemaphoreSlim GetRefreshLock(long installationId) =>
        refreshLocks.GetOrAdd(installationId, static _ => new SemaphoreSlim(1, 1));

    internal void Set(long installationId, GitHubInstallationAccessToken token) =>
        tokens[installationId] = token;
}