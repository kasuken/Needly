using Microsoft.EntityFrameworkCore;
using Needly.Application.Users;

namespace Needly.Infrastructure.Users;

/// <summary>Persists first-run onboarding state for Needly users.</summary>
public sealed class UserOnboardingService(
    NeedlyDbContext dbContext,
    TimeProvider timeProvider) : IUserOnboardingService
{
    private readonly NeedlyDbContext dbContext = dbContext
        ?? throw new ArgumentNullException(nameof(dbContext));
    private readonly TimeProvider timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public Task<bool> IsCompletedAsync(Guid needlyUserId, CancellationToken cancellationToken)
    {
        if (needlyUserId == Guid.Empty)
        {
            throw new ArgumentException("A Needly user identifier is required.", nameof(needlyUserId));
        }

        return dbContext.NeedlyUsers
            .AsNoTracking()
            .Where(user => user.Id == needlyUserId)
            .Select(user => user.OnboardingCompletedAt != null)
            .SingleAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid needlyUserId, CancellationToken cancellationToken)
    {
        if (needlyUserId == Guid.Empty)
        {
            throw new ArgumentException("A Needly user identifier is required.", nameof(needlyUserId));
        }

        var user = await dbContext.NeedlyUsers
            .SingleAsync(user => user.Id == needlyUserId, cancellationToken)
            .ConfigureAwait(false);
        user.CompleteOnboarding(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}