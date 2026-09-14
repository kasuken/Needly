namespace Needly.Infrastructure.GitHub;

/// <summary>Configures the label vocabulary that signals a pending decision on an issue.</summary>
public sealed class DecideOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Decide";

    /// <summary>Gets or sets the issue labels that signal a decision is needed.</summary>
    public List<string> Labels { get; set; } =
    [
        "needs-decision",
        "question",
        "rfc",
        "discussion"
    ];
}
