using System.ComponentModel.DataAnnotations;

namespace Needly.Infrastructure.GitHub;

/// <summary>Configures GitHub-derived action behavior.</summary>
public sealed class GitHubActionOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "GitHubActions";

    /// <summary>Gets or sets the minimum approvals required before a pull request is ready to merge.</summary>
    [Range(1, 100)]
    public int RequiredApprovals { get; set; } = 1;
}