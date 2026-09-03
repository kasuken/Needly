using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>Persists GitHub profiles and their linked Needly accounts.</summary>
public sealed class GitHubIdentityService(
    NeedlyDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<GitHubIdentityService> logger) : IGitHubIdentityService
{
    private readonly NeedlyDbContext dbContext = dbContext
        ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly TimeProvider timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<GitHubIdentityService> logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<SignedInNeedlyUser> UpsertAsync(
        GitHubIdentityProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var now = timeProvider.GetUtcNow();
        var gitHubUser = await dbContext.GitHubUsers
            .SingleOrDefaultAsync(
                user => user.GitHubUserId == profile.GitHubUserId,
                cancellationToken)
            .ConfigureAwait(false);

        if (gitHubUser is null)
        {
            gitHubUser = GitHubUser.Create(
                Guid.NewGuid(),
                profile.GitHubUserId,
                profile.Login,
                profile.DisplayName,
                profile.AvatarUrl,
                now);
            dbContext.GitHubUsers.Add(gitHubUser);
        }
        else
        {
            gitHubUser.Update(
                profile.Login,
                profile.DisplayName,
                profile.AvatarUrl,
                now);
        }

        var needlyUser = await dbContext.NeedlyUsers
            .SingleOrDefaultAsync(
                user => user.GitHubUserId == gitHubUser.Id,
                cancellationToken)
            .ConfigureAwait(false);
        var displayName = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.Login
            : profile.DisplayName;

        if (needlyUser is null)
        {
            needlyUser = NeedlyUser.Create(
                Guid.NewGuid(),
                gitHubUser.Id,
                profile.Email,
                displayName,
                now);
            dbContext.NeedlyUsers.Add(needlyUser);
        }
        else
        {
            needlyUser.Update(profile.Email, displayName, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Linked Needly user {NeedlyUserId} to GitHub user {GitHubUserId} ({GitHubLogin})",
            needlyUser.Id,
            gitHubUser.GitHubUserId,
            gitHubUser.Login);

        return new SignedInNeedlyUser(
            needlyUser.Id,
            gitHubUser.GitHubUserId,
            gitHubUser.Login,
            needlyUser.DisplayName);
    }
}