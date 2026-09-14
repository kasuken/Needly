using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure.Actions;
using Needly.Infrastructure.GitHub;
using Xunit;
using static Needly.Tests.Infrastructure.DetectorTestSupport;

namespace Needly.Tests.Infrastructure;

/// <summary>
/// Covers how <see cref="GitHubActionEventHandler"/> wires review risk (issue #34) into the existing
/// filter-metadata pipeline: recomputed only on "opened"/"synchronize" pull request events, passed
/// through unchanged by unrelated events (e.g. check runs), and degraded to Unknown when the
/// changed-file list cannot be fetched.
/// </summary>
public sealed class GitHubActionEventHandlerReviewRiskTests
{
    [Fact]
    public async Task HandleAsync_SynchronizeWithFetchedFiles_ComputesReviewRiskWithMatchedSignals()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var fileLookup = new FakeFileLookup { Paths = ["src/Auth/Login.cs", "docs/readme.md"] };
        var handler = CreateHandlerWithReviewRisk(database, fileLookup);

        await HandleAsync(
            database.Context, handler, "pull_request", "opened",
            PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context, handler, "check_run", "completed",
            CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));

        await using (var afterOpen = database.CreateContext())
        {
            // The Fix action is created by the check_run event, not by "opened" itself, so no risk has
            // been computed for it yet: "opened" only recomputes risk for actions the event touches.
            var action = await afterOpen.Actions.AsNoTracking().SingleAsync();
            Assert.Null(action.ReviewRiskLevel);
            Assert.Empty(action.ReviewRiskSignals);
        }

        await HandleAsync(
            database.Context, handler, "pull_request", "synchronize",
            PullRequestPayload(false, BaseTime.AddMinutes(3), [], headSha: "head-2"), BaseTime.AddMinutes(3));

        await using var final = database.CreateContext();
        var updated = await final.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ReviewRiskLevel.High, updated.ReviewRiskLevel);
        Assert.Contains("authentication", updated.ReviewRiskSignals);
        Assert.Contains("documentation", updated.ReviewRiskSignals);
    }

    [Fact]
    public async Task HandleAsync_SynchronizeWhenFileListUnavailable_DegradesToUnknownNeverLow()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var fileLookup = new FakeFileLookup { Paths = null };
        var handler = CreateHandlerWithReviewRisk(database, fileLookup);

        await HandleAsync(
            database.Context, handler, "pull_request", "opened",
            PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context, handler, "check_run", "completed",
            CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));
        await HandleAsync(
            database.Context, handler, "pull_request", "synchronize",
            PullRequestPayload(false, BaseTime.AddMinutes(3), [], headSha: "head-2"), BaseTime.AddMinutes(3));

        await using var verification = database.CreateContext();
        var updated = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ReviewRiskLevel.Unknown, updated.ReviewRiskLevel);
        Assert.Empty(updated.ReviewRiskSignals);
    }

    [Fact]
    public async Task HandleAsync_UnrelatedEventAfterRiskComputed_PassesThroughStoredRisk()
    {
        await using var database = await DetectorTestDatabase.CreateAsync();
        await SeedAsync(database.Context);
        var fileLookup = new FakeFileLookup { Paths = ["src/Auth/Login.cs"] };
        var handler = CreateHandlerWithReviewRisk(database, fileLookup);
        await HandleAsync(
            database.Context, handler, "pull_request", "opened",
            PullRequestPayload(false, BaseTime.AddMinutes(1), []), BaseTime.AddMinutes(1));
        await HandleAsync(
            database.Context, handler, "check_run", "completed",
            CheckRunPayload("Build", "failure", "head-1", BaseTime.AddMinutes(2)), BaseTime.AddMinutes(2));
        await HandleAsync(
            database.Context, handler, "pull_request", "synchronize",
            PullRequestPayload(false, BaseTime.AddMinutes(3), [], headSha: "head-2"), BaseTime.AddMinutes(3));

        // The lookup would now report a completely different (Low) file list, but a subsequent check
        // run is neither "opened" nor "synchronize", so risk must not be recomputed from it.
        fileLookup.Paths = ["docs/readme.md"];
        await HandleAsync(
            database.Context, handler, "check_run", "completed",
            CheckRunPayload("Build", "success", "head-2", BaseTime.AddMinutes(4)), BaseTime.AddMinutes(4));

        await using var verification = database.CreateContext();
        var action = await verification.Actions.AsNoTracking().SingleAsync();
        Assert.Equal(ReviewRiskLevel.High, action.ReviewRiskLevel);
        Assert.Contains("authentication", action.ReviewRiskSignals);
    }

    private static GitHubActionEventHandler CreateHandlerWithReviewRisk(
        DetectorTestDatabase database, IGitHubPullRequestFileLookup fileLookup) =>
        new(
            database,
            GetDetectors(),
            NullLogger<GitHubActionEventHandler>.Instance,
            broadcaster: null,
            ruleEvaluator: null,
            agentClassifier: null,
            pullRequestFileLookup: fileLookup,
            reviewRiskClassifier: new ReviewRiskClassifier(Options.Create(new ReviewRiskOptions())));

    private sealed class FakeFileLookup : IGitHubPullRequestFileLookup
    {
        internal IReadOnlyList<string>? Paths { get; set; }

        public Task<IReadOnlyList<string>?> GetChangedFilePathsAsync(
            long gitHubInstallationId,
            string repositoryOwner,
            string repositoryName,
            int pullRequestNumber,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Paths);
        }
    }
}
