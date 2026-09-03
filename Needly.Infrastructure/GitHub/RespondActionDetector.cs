using System.Text.Json;
using System.Text.RegularExpressions;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

internal sealed class RespondActionDetector : IGitHubActionDetector
{
    private static readonly Regex MentionPattern = new(
        @"(?<![A-Za-z0-9-])@(?<login>[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?)(?![A-Za-z0-9-])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public string Key => "github.respond.v1";

    public int Order => 350;

    public async Task<IReadOnlyList<GitHubActionOperation>> DetectAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Event.EventName is "pull_request" or "issues" && context.Event.Action == "closed")
        {
            return await ResolveClosedAsync(context, cancellationToken).ConfigureAwait(false);
        }

        if (context.Event.EventName is not ("issue_comment" or "pull_request_review_comment") ||
            context.Event.Action != "created")
        {
            return [];
        }

        var payload = GitHubActionWebhookParser.Parse(context);
        var subject = GetSubject(context, payload);
        var comment = payload.Comment ?? throw new JsonException("The GitHub comment payload did not contain comment.");
        if (comment.Id <= 0 || comment.User.Id <= 0 || string.IsNullOrWhiteSpace(comment.User.Login))
        {
            throw new JsonException("The GitHub comment payload was missing comment identity fields.");
        }

        if (IsBot(comment.User))
        {
            return [];
        }

        var occurredAt = comment.UpdatedAt ?? comment.CreatedAt ?? context.Event.ReceivedAt;
        var states = await context.State.GetResponsesAsync(
            subject.Type,
            subject.Number,
            cancellationToken).ConfigureAwait(false);
        var operations = new List<GitHubActionOperation>();
        var ownState = states.SingleOrDefault(state =>
            state.GitHubAssigneeId == comment.User.Id && state.IsPending);
        if (ownState is not null && occurredAt > ownState.LastTriggeredAt)
        {
            await context.State.UpsertResponseAsync(
                ownState with { IsPending = false, UpdatedAt = occurredAt },
                cancellationToken).ConfigureAwait(false);
            operations.Add(new ResolveGitHubActionOperation(
                CreateTarget(subject, ownState.GitHubAssigneeId),
                occurredAt));
        }

        var targets = FindMentionedIdentities(context, comment.Body)
            .Where(identity => identity.GitHubId != comment.User.Id)
            .ToDictionary(identity => identity.GitHubId);
        if (!IsBot(subject.Author) && subject.Author.Id != comment.User.Id)
        {
            var author = FindUser(context, subject.Author.Id);
            if (author is not null && !IsBot(author.Login))
            {
                targets.TryAdd(author.GitHubId, author);
            }
        }

        foreach (var target in targets.Values.OrderBy(identity => identity.GitHubId))
        {
            var existing = states.SingleOrDefault(state => state.GitHubAssigneeId == target.GitHubId);
            if (existing?.LastTriggerCommentId == comment.Id)
            {
                continue;
            }

            var triggerCount = (existing?.TriggerCount ?? 0) + 1;
            await context.State.UpsertResponseAsync(
                new GitHubResponseState(
                    subject.Type,
                    subject.Number,
                    target.GitHubId,
                    IsPending: true,
                    triggerCount,
                    comment.Id,
                    occurredAt,
                    occurredAt),
                cancellationToken).ConfigureAwait(false);
            var countLabel = triggerCount == 1 ? "comment" : "comments";
            operations.Add(new CreateGitHubActionOperation(
                CreateTarget(subject, target.GitHubId),
                subject.Url,
                $"Respond on {GetSubjectLabel(subject.Type)} #{subject.Number}: {subject.Title}",
                $"{triggerCount} {countLabel} need a response. Latest from @{comment.User.Login}: {Summarize(comment.Body)}",
                $"@{comment.User.Login} added activity that needs a response.",
                occurredAt,
                ReactivateTerminal: true,
                Significance: ActionEventSignificance.Significant));
        }

        return operations;
    }

    private static async Task<IReadOnlyList<GitHubActionOperation>> ResolveClosedAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        var payload = GitHubActionWebhookParser.Parse(context);
        var subject = GetSubject(context, payload);
        var states = await context.State.GetResponsesAsync(subject.Type, subject.Number, cancellationToken)
            .ConfigureAwait(false);
        foreach (var state in states.Where(state => state.IsPending))
        {
            await context.State.UpsertResponseAsync(
                state with { IsPending = false, UpdatedAt = context.Event.ReceivedAt },
                cancellationToken).ConfigureAwait(false);
        }

        return context.Actions
            .Where(action => action.Target.Type == ActionType.Respond &&
                action.Target.SubjectType == subject.Type &&
                action.Target.SubjectNumber == subject.Number &&
                action.State is ActionState.Open or ActionState.Snoozed)
            .Select(action => (GitHubActionOperation)new ResolveGitHubActionOperation(
                action.Target,
                context.Event.ReceivedAt))
            .ToArray();
    }

    private static CommentSubject GetSubject(
        GitHubActionDetectionContext context,
        GitHubActionWebhookPayload payload)
    {
        if (context.Event.EventName == "pull_request_review_comment" || payload.PullRequest is not null)
        {
            var pullRequest = GitHubActionWebhookParser.RequirePullRequest(payload);
            var number = payload.Number > 0 ? payload.Number : pullRequest.Number;
            return new CommentSubject(
                GitHubSubjectType.PullRequest,
                number,
                pullRequest.HtmlUrl,
                pullRequest.Title,
                pullRequest.User);
        }

        var issue = payload.Issue ?? throw new JsonException("The GitHub issue payload did not contain issue.");
        return new CommentSubject(
            issue.PullRequest is null ? GitHubSubjectType.Issue : GitHubSubjectType.PullRequest,
            issue.Number,
            issue.HtmlUrl,
            issue.Title,
            issue.User);
    }

    private static IEnumerable<GitHubActionIdentity> FindMentionedIdentities(
        GitHubActionDetectionContext context,
        string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        var logins = MentionPattern.Matches(body)
            .Select(match => match.Groups["login"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return context.Identities
            .Where(identity => identity.Type == ActionAssigneeType.User &&
                logins.Contains(identity.Login) &&
                !IsBot(identity.Login))
            .DistinctBy(identity => identity.GitHubId);
    }

    private static GitHubActionIdentity? FindUser(GitHubActionDetectionContext context, long gitHubUserId) =>
        context.Identities.SingleOrDefault(identity =>
            identity.Type == ActionAssigneeType.User && identity.GitHubId == gitHubUserId);

    private static bool IsBot(GitHubActionUserPayload user) =>
        string.Equals(user.Type, "Bot", StringComparison.OrdinalIgnoreCase) || IsBot(user.Login);

    private static bool IsBot(string login) => login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase);

    private static string Summarize(string? body)
    {
        var normalized = string.IsNullOrWhiteSpace(body) ? "Comment added." : body.Trim();
        return normalized.Length <= 1000 ? normalized : $"{normalized[..997]}...";
    }

    private static string GetSubjectLabel(GitHubSubjectType subjectType) =>
        subjectType == GitHubSubjectType.PullRequest ? "PR" : "issue";

    private static GitHubActionTarget CreateTarget(CommentSubject subject, long gitHubAssigneeId) =>
        new(
            ActionType.Respond,
            subject.Type,
            subject.Number,
            ActionAssigneeType.User,
            gitHubAssigneeId);

    private sealed record CommentSubject(
        GitHubSubjectType Type,
        int Number,
        string Url,
        string Title,
        GitHubActionUserPayload Author);
}