using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>
/// Loads changed file paths for a pull request through an installation-authenticated GitHub API
/// client (issue #34).
/// </summary>
public sealed class GitHubPullRequestFileLookup(
    IGitHubApiClientFactory clientFactory,
    ILogger<GitHubPullRequestFileLookup> logger) : IGitHubPullRequestFileLookup
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> GetChangedFilePathsAsync(
        long gitHubInstallationId,
        string repositoryOwner,
        string repositoryName,
        int pullRequestNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = await clientFactory.CreateAsync(gitHubInstallationId, cancellationToken)
                .ConfigureAwait(false);
            var repositoryPath =
                $"repos/{Uri.EscapeDataString(repositoryOwner)}/{Uri.EscapeDataString(repositoryName)}";
            using var response = await client.SendAsync(
                HttpMethod.Get,
                $"{repositoryPath}/pulls/{pullRequestNumber}/files?per_page=100",
                null,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var files = await JsonSerializer.DeserializeAsync<IReadOnlyList<PullRequestFileResponse>>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return files?.Select(file => file.Filename).ToArray() ?? [];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Review risk must degrade to Unknown rather than fail the webhook transaction or silently
            // report Low when the changed-file list cannot be fetched (rate limiting, transient HTTP
            // failures, or a malformed response). See docs/github-app.md.
            logger.LogWarning(
                exception,
                "Failed to fetch changed files for pull request {PullRequestNumber} in {Owner}/{Repository}; review risk will be Unknown.",
                pullRequestNumber,
                repositoryOwner,
                repositoryName);
            return null;
        }
    }

    private sealed record PullRequestFileResponse(
        [property: JsonPropertyName("filename")] string Filename);
}
