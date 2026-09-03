using System.Text.Json.Serialization;
using Needly.Domain;

namespace Needly.Application.GitHub;

/// <summary>Describes a GitHub account included in an installation webhook.</summary>
/// <param name="Id">The GitHub account identifier.</param>
/// <param name="Login">The account login.</param>
/// <param name="Type">The GitHub account type.</param>
public sealed record GitHubAccountPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("type")] string Type);

/// <summary>Describes a repository included in an installation webhook.</summary>
/// <param name="Id">The GitHub repository identifier.</param>
/// <param name="Name">The repository name.</param>
/// <param name="FullName">The owner-qualified repository name.</param>
/// <param name="Owner">The repository owner.</param>
public sealed record GitHubRepositoryPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("owner")] GitHubAccountPayload? Owner);

/// <summary>Describes the installation portion of a GitHub webhook.</summary>
/// <param name="Id">The GitHub installation identifier.</param>
/// <param name="Account">The account that owns the installation.</param>
/// <param name="RepositorySelection">Whether all or selected repositories are available.</param>
public sealed record GitHubInstallationPayload(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("account")] GitHubAccountPayload Account,
    [property: JsonPropertyName("repository_selection")] string RepositorySelection);

/// <summary>Represents an installation created, deleted, suspended, or unsuspended webhook.</summary>
/// <param name="Action">The webhook action.</param>
/// <param name="Installation">The affected installation.</param>
/// <param name="Repositories">Repositories included when the installation is created.</param>
public sealed record GitHubInstallationEvent(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("installation")] GitHubInstallationPayload Installation,
    [property: JsonPropertyName("repositories")] IReadOnlyList<GitHubRepositoryPayload>? Repositories);

/// <summary>Represents repositories added to or removed from an installation.</summary>
/// <param name="Action">The webhook action.</param>
/// <param name="Installation">The affected installation.</param>
/// <param name="RepositoriesAdded">Repositories added to the selection.</param>
/// <param name="RepositoriesRemoved">Repositories removed from the selection.</param>
public sealed record GitHubInstallationRepositoriesEvent(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("installation")] GitHubInstallationPayload Installation,
    [property: JsonPropertyName("repositories_added")] IReadOnlyList<GitHubRepositoryPayload> RepositoriesAdded,
    [property: JsonPropertyName("repositories_removed")] IReadOnlyList<GitHubRepositoryPayload> RepositoriesRemoved);

/// <summary>Contains the verified GitHub profile used to create or update a Needly account.</summary>
/// <param name="GitHubUserId">The GitHub user identifier.</param>
/// <param name="Login">The GitHub login.</param>
/// <param name="Email">The verified email address.</param>
/// <param name="DisplayName">The optional GitHub display name.</param>
/// <param name="AvatarUrl">The optional avatar URL.</param>
public sealed record GitHubIdentityProfile(
    long GitHubUserId,
    string Login,
    string Email,
    string? DisplayName,
    string? AvatarUrl);

/// <summary>Identifies the persisted Needly account associated with a GitHub login.</summary>
/// <param name="NeedlyUserId">The Needly account identifier.</param>
/// <param name="GitHubUserId">The GitHub user identifier.</param>
/// <param name="Login">The current GitHub login.</param>
/// <param name="DisplayName">The current display name.</param>
public sealed record SignedInNeedlyUser(
    Guid NeedlyUserId,
    long GitHubUserId,
    string Login,
    string DisplayName);

/// <summary>Contains an installation access token and its absolute expiration.</summary>
/// <param name="Token">The bearer token.</param>
/// <param name="ExpiresAt">The token expiration.</param>
public sealed record GitHubInstallationAccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Contains repository information displayed in Settings.</summary>
/// <param name="GitHubRepositoryId">The GitHub repository identifier.</param>
/// <param name="Owner">The repository owner.</param>
/// <param name="Name">The repository name.</param>
public sealed record RepositorySettingsItem(long GitHubRepositoryId, string Owner, string Name);

/// <summary>Contains installation information displayed in Settings.</summary>
/// <param name="GitHubInstallationId">The GitHub installation identifier.</param>
/// <param name="AccountLogin">The owning account login.</param>
/// <param name="AccountType">The owning account type.</param>
/// <param name="State">The installation state.</param>
/// <param name="Repositories">The selected repositories.</param>
public sealed record InstallationSettingsItem(
    long GitHubInstallationId,
    string AccountLogin,
    GitHubAccountType AccountType,
    InstallationState State,
    IReadOnlyList<RepositorySettingsItem> Repositories);

/// <summary>Contains the authenticated user's GitHub App installation settings.</summary>
/// <param name="Installations">The installations linked to the user.</param>
public sealed record GitHubSettings(IReadOnlyList<InstallationSettingsItem> Installations);