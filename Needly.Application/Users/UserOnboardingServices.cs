namespace Needly.Application.Users;

/// <summary>Reads and completes first-run onboarding for a Needly user.</summary>
public interface IUserOnboardingService
{
    /// <summary>Gets whether the user completed or skipped onboarding.</summary>
    Task<bool> IsCompletedAsync(Guid needlyUserId, CancellationToken cancellationToken);

    /// <summary>Marks onboarding complete for the user.</summary>
    Task CompleteAsync(Guid needlyUserId, CancellationToken cancellationToken);
}