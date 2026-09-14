using Microsoft.EntityFrameworkCore;
using Needly.Domain;
using Needly.Infrastructure;
using Xunit;
using static Needly.Tests.Infrastructure.DetectorTestSupport;

namespace Needly.Tests.Infrastructure;

public sealed class MonitorActionDetectorTests
{
    [Fact]
    public async Task PullRequestMerged_ResolvesOpenMonitorAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await SeedMonitorActionAsync(database.Context, seed);

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "closed",
            PullRequestPayload(false, BaseTime.AddMinutes(2), [], merged: true),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var monitor = await verification.Actions.AsNoTracking().SingleAsync(action => action.Type == ActionType.Monitor);
        Assert.Equal(ActionState.Done, monitor.State);
    }

    [Fact]
    public async Task PullRequestClosedWithoutMerge_ResolvesOpenMonitorAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await SeedMonitorActionAsync(database.Context, seed);

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "closed",
            PullRequestPayload(false, BaseTime.AddMinutes(2), [], merged: false),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var monitor = await verification.Actions.AsNoTracking().SingleAsync(action => action.Type == ActionType.Monitor);
        Assert.Equal(ActionState.Done, monitor.State);
    }

    [Fact]
    public async Task CiTurnsGreenOnCurrentHead_ResolvesOpenMonitorAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await SeedMonitorActionAsync(database.Context, seed);

        await HandleAsync(
            database.Context,
            handler,
            "check_run",
            "completed",
            CheckRunPayload("Build", "success", "head-1", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var monitor = await verification.Actions.AsNoTracking().SingleAsync(action => action.Type == ActionType.Monitor);
        Assert.Equal(ActionState.Done, monitor.State);
    }

    [Fact]
    public async Task CiStillFailingOnCurrentHead_DoesNotResolveMonitorAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await SeedMonitorActionAsync(database.Context, seed);

        await HandleAsync(
            database.Context,
            handler,
            "check_run",
            "completed",
            CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var monitor = await verification.Actions.AsNoTracking().SingleAsync(action => action.Type == ActionType.Monitor);
        Assert.Equal(ActionState.Open, monitor.State);
    }

    [Fact]
    public async Task CiCompletesOnStaleHead_DoesNotResolveMonitorAction()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), [], headSha: "head-2"), BaseTime.AddMinutes(1));
        await SeedMonitorActionAsync(database.Context, seed);

        await HandleAsync(
            database.Context,
            handler,
            "check_run",
            "completed",
            CheckRunPayload("Build", "success", "head-1", BaseTime.AddMinutes(2)),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        var monitor = await verification.Actions.AsNoTracking().SingleAsync(action => action.Type == ActionType.Monitor);
        Assert.Equal(ActionState.Open, monitor.State);
    }

    [Fact]
    public async Task NoExistingMonitorAction_MergeProducesNoActions()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var handler = CreateHandler(database);
        await HandleAsync(database.Context, handler, "pull_request", "opened", PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));

        await HandleAsync(
            database.Context,
            handler,
            "pull_request",
            "closed",
            PullRequestPayload(false, BaseTime.AddMinutes(2), [], merged: true),
            BaseTime.AddMinutes(2));

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Actions.AsNoTracking().Where(action => action.Type == ActionType.Monitor).ToListAsync());
    }

    private static async Task SeedMonitorActionAsync(NeedlyDbContext context, SeedResult seed)
    {
        var action = NeedlyAction.CreateForUser(
            Guid.NewGuid(),
            ActionType.Monitor,
            seed.Repository,
            seed.Author,
            GitHubSubjectType.PullRequest,
            42,
            "https://github.com/octocat/needly/pull/42",
            "Monitor PR #42: Improve action detection",
            "Tell me when this merges.",
            "You asked to be notified about this pull request.",
            BaseTime);
        context.Actions.Add(action);
        await context.SaveChangesAsync();
    }
}
