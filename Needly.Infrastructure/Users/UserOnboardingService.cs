using Microsoft.EntityFrameworkCore;
using Needly.Application.Users;

namespace Needly.Infrastructure.Users;

/// <summary>Persists first-run onboarding state for Needly users.</summary>
public sealed class UserOnboardingService(
    IDbContextFactory<NeedlyDbContext> contextFactory,
    TimeProvider timeProvider) : IUserOnboardingService
{
    private readonly IDbContextFactory<NeedlyDbContext> contextFactory = contextFactory
        ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly TimeProvider timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async Task<bool> IsCompletedAsync(Guid needlyUserId, CancellationToken cancellationToken)
    {
        if (needlyUserId == Guid.Empty)
        {
            throw new ArgumentException("A Needly user identifier is required.", nameof(needlyUserId));
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var hasIncompleteUser = await dbContext.NeedlyUsers
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == needlyUserId && user.OnboardingCompletedAt == null,
                cancellationToken)
            .ConfigureAwait(false);

        return !hasIncompleteUser;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid needlyUserId, CancellationToken cancellationToken)
    {
        if (needlyUserId == Guid.Empty)
        {
            throw new ArgumentException("A Needly user identifier is required.", nameof(needlyUserId));
        }

        await using var dbContext = await contextFactory
            .CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var user = await dbContext.NeedlyUsers
            .SingleAsync(user => user.Id == needlyUserId, cancellationToken)
            .ConfigureAwait(false);
        user.CompleteOnboarding(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}