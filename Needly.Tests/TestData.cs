using Needly.Domain;

namespace Needly.Tests;

internal static class TestData
{
    internal static readonly Guid InstallationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid RepositoryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    internal static readonly DateTimeOffset CreatedAt = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

    internal static Repository CreateRepository(
        Guid? id = null,
        Guid? installationId = null,
        string owner = "octocat",
        string name = "needly") =>
        Repository.Create(
            id ?? RepositoryId,
            installationId ?? InstallationId,
            101,
            owner,
            name,
            CreatedAt);

    internal static GitHubUser CreateGitHubUser(Guid? id = null, long gitHubUserId = 201) =>
        GitHubUser.Create(
            id ?? UserId,
            gitHubUserId,
            $"user-{gitHubUserId}",
            $"User {gitHubUserId}",
            null,
            CreatedAt);

    internal static NeedlyAction CreateAction(
        Guid? id = null,
        Repository? repository = null,
        GitHubUser? assignee = null,
        ActionType type = ActionType.Review,
        GitHubSubjectType subjectType = GitHubSubjectType.PullRequest,
        int subjectNumber = 42,
        DateTimeOffset? createdAt = null)
    {
        var subjectRepository = repository ?? CreateRepository();
        var assignedUser = assignee ?? CreateGitHubUser();
        var subjectSegment = subjectType == GitHubSubjectType.PullRequest ? "pull" : "issues";

        return NeedlyAction.CreateForUser(
            id ?? Guid.Parse("44444444-4444-4444-4444-444444444444"),
            type,
            subjectRepository,
            assignedUser,
            subjectType,
            subjectNumber,
            $"https://github.com/{subjectRepository.Owner}/{subjectRepository.Name}/{subjectSegment}/{subjectNumber}",
            "Review the change",
            "Requested by the maintainer",
            "The pull request is waiting for review",
            createdAt ?? CreatedAt);
    }
}
