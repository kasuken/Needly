using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Domain;

namespace Needly.Infrastructure.GitHub;

/// <summary>
/// Detects issues that carry a decision-signalling label and are assigned to, or mention, a
/// Needly-tracked user. The full current <c>assignees</c> and <c>labels</c> arrays on every GitHub
/// <c>issues</c> webhook reflect the post-action state, so this detector recomputes the desired set
/// of Decide actions on each event rather than tracking deltas.
/// </summary>
internal sealed class DecideActionDetector(IOptions<DecideOptions> options) : IGitHubActionDetector
{
    private static readonly Regex MentionPattern = new(
        @"(?<![A-Za-z0-9-])@(?<login>[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?)(?![A-Za-z0-9-])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly HashSet<string> RelevantActions = new(StringComparer.Ordinal)
    {
        "opened", "edited", "reopened", "assigned", "unassigned", "labeled", "unlabeled"
    };

    private readonly DecideOptions options = options.Value;

    public string Key => "github.decide.v1";

    public int Order => 150;

    public Task<IReadOnlyList<GitHubActionOperation>> DetectAsync(
        GitHubActionDetectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Event.EventName != "issues")
        {
            return Task.FromResult<IReadOnlyList<GitHubActionOperation>>([]);
        }

        var payload = GitHubActionWebhookParser.Parse(context);
        var issue = payload.Issue ?? throw new JsonException("The GitHub issues payload did not contain issue.");
        if (issue.Number <= 0 || issue.User.Id <= 0 || string.IsNullOrWhiteSpace(issue.User.Login) ||
            string.IsNullOrWhiteSpace(issue.HtmlUrl) || string.IsNullOrWhiteSpace(issue.Title))
        {
            throw new JsonException("The GitHub issues payload was missing required identity fields.");
        }

        if (issue.PullRequest is not null)
        {
            // Pull requests already have Review, Resolve, Fix, and Merge coverage; Decide targets issues only.
            return Task.FromResult<IReadOnlyList<GitHubActionOperation>>([]);
        }

        var occurredAt = issue.UpdatedAt ?? context.Event.ReceivedAt;
        if (context.Event.Action == "closed")
        {
            return Task.FromResult(ResolveAll(context, issue.Number, occurredAt));
        }

        if (context.Event.Action is null || !RelevantActions.Contains(context.Event.Action))
        {
            return Task.FromResult<IReadOnlyList<GitHubActionOperation>>([]);
        }

        var matchedLabels = (issue.Labels ?? [])
            .Select(label => label.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name) &&
                options.Labels.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var targets = FindTargets(context, issue);

        var operations = new List<GitHubActionOperation>();
        if (matchedLabels.Length > 0)
        {
            var labelList = string.Join(", ", matchedLabels);
            foreach (var target in targets.Values.OrderBy(identity => identity.GitHubId))
            {
                operations.Add(new CreateGitHubActionOperation(
                    CreateTarget(issue.Number, target.GitHubId),
                    issue.HtmlUrl,
                    $"Decide on issue #{issue.Number}: {issue.Title}",
                    $"Labeled {labelList}.",
                    $"Issue #{issue.Number} is assigned to or mentions you and carries a decision label.",
                    occurredAt,
                    ReactivateTerminal: true,
                    Significance: ActionEventSignificance.Significant));
            }
        }

        operations.AddRange(context.Actions
            .Where(action => action.Target.Type == ActionType.Decide &&
                action.Target.SubjectType == GitHubSubjectType.Issue &&
                action.Target.SubjectNumber == issue.Number &&
                action.State is ActionState.Open or ActionState.Snoozed &&
                (matchedLabels.Length == 0 || !targets.ContainsKey(action.Target.GitHubAssigneeId)))
            .Select(action => (GitHubActionOperation)new ResolveGitHubActionOperation(action.Target, occurredAt)));

        return Task.FromResult<IReadOnlyList<GitHubActionOperation>>(operations);
    }

    private static IReadOnlyList<GitHubActionOperation> ResolveAll(
        GitHubActionDetectionContext context,
        int issueNumber,
        DateTimeOffset occurredAt) =>
        context.Actions
            .Where(action => action.Target.Type == ActionType.Decide &&
                action.Target.SubjectType == GitHubSubjectType.Issue &&
                action.Target.SubjectNumber == issueNumber &&
                action.State is ActionState.Open or ActionState.Snoozed)
            .Select(action => (GitHubActionOperation)new ResolveGitHubActionOperation(action.Target, occurredAt))
            .ToArray();

    private static Dictionary<long, GitHubActionIdentity> FindTargets(
        GitHubActionDetectionContext context,
        GitHubIssuePayload issue)
    {
        var targets = new Dictionary<long, GitHubActionIdentity>();
        foreach (var assignee in issue.Assignees ?? [])
        {
            var identity = FindUser(context, assignee.Id);
            if (identity is not null)
            {
                targets.TryAdd(identity.GitHubId, identity);
            }
        }

        foreach (var identity in FindMentionedIdentities(context, issue.Body))
        {
            targets.TryAdd(identity.GitHubId, identity);
        }

        return targets;
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
            .Where(identity => identity.Type == ActionAssigneeType.User && logins.Contains(identity.Login))
            .DistinctBy(identity => identity.GitHubId);
    }

    private static GitHubActionIdentity? FindUser(GitHubActionDetectionContext context, long gitHubUserId) =>
        context.Identities.SingleOrDefault(identity =>
            identity.Type == ActionAssigneeType.User && identity.GitHubId == gitHubUserId);

    private static GitHubActionTarget CreateTarget(int issueNumber, long gitHubAssigneeId) =>
        new(
            ActionType.Decide,
            GitHubSubjectType.Issue,
            issueNumber,
            ActionAssigneeType.User,
            gitHubAssigneeId);
}
