using System.Text.Json.Serialization;
using Needly.Domain;

namespace Needly.Application.GitHub;

/// <summary>Contains a signed GitHub webhook request after bounded body reading.</summary>
public sealed record GitHubWebhookRequest(
    string DeliveryId,
    string EventName,
    string Signature,
    byte[] Payload);

/// <summary>Describes the durable acknowledgment of a webhook delivery.</summary>
public sealed record GitHubWebhookReceipt(Guid EventId, bool IsDuplicate);

/// <summary>Describes a GitHub user in organization and team payloads.</summary>
public sealed record GitHubUserPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

/// <summary>Describes a GitHub organization team.</summary>
public sealed record GitHubTeamPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string Name);

/// <summary>Represents an organization member webhook.</summary>
public sealed record GitHubMemberEvent(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("member")] GitHubUserPayload Member,
    [property: JsonPropertyName("installation")] GitHubInstallationPayload Installation);

/// <summary>Represents a team lifecycle webhook.</summary>
public sealed record GitHubTeamEvent(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("team")] GitHubTeamPayload Team,
    [property: JsonPropertyName("installation")] GitHubInstallationPayload Installation);

/// <summary>Represents a team membership webhook.</summary>
public sealed record GitHubMembershipEvent(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("member")] GitHubUserPayload Member,
    [property: JsonPropertyName("team")] GitHubTeamPayload Team,
    [property: JsonPropertyName("installation")] GitHubInstallationPayload Installation);

/// <summary>Contains an installation-scoped team and its active users.</summary>
public sealed record TeamReviewTarget(
    Guid TeamId,
    long GitHubTeamId,
    string Slug,
    IReadOnlyList<Guid> GitHubUserIds);

/// <summary>Contains an action visible in a user's inbox and its current waiting projection.</summary>
public sealed record VisibleAction(
    Guid ActionId,
    string RepositoryOwner,
    string RepositoryName,
    string SubjectTitle,
    int SubjectNumber,
    GitHubSubjectType SubjectType,
    string SubjectUrl,
    ActionType Type,
    ActionState State,
    string Reason,
    string? Context,
    string AssigneeDisplay,
    string TriggerDisplay,
    DateTimeOffset WaitingSince,
    TimeSpan WaitingDuration,
    bool IsAtRisk,
    string? RiskReason,
    string? AuthorLogin,
    ActionAssigneeScope AssigneeScope,
    bool HasBotInvolvement,
    bool IsPinned);

/// <summary>Creates shared filter candidates from authorized inbox projections.</summary>
public static class VisibleActionFilterCandidate
{
    /// <summary>Maps an authorized visible action to the persistence-neutral matcher input.</summary>
    public static ActionFilterCandidate Create(VisibleAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new ActionFilterCandidate(
            action.Type,
            action.State,
            $"{action.RepositoryOwner}/{action.RepositoryName}",
            action.RepositoryOwner,
            action.AuthorLogin,
            action.AssigneeScope,
            action.WaitingDuration,
            action.HasBotInvolvement);
    }
}

/// <summary>Contains a verified durable event dispatched to the action-processing boundary.</summary>
public sealed record GitHubStoredEvent(
    Guid EventId,
    long GitHubInstallationId,
    long? GitHubRepositoryId,
    string EventName,
    string? Action,
    string PayloadJson,
    DateTimeOffset ReceivedAt);