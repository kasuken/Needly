namespace Needly.Infrastructure.GitHub;

/// <summary>
/// Configures the data-driven ruleset <see cref="AgentClassifier"/> uses to identify the specific agent
/// or bot behind GitHub activity, so a newly observed agent can be recognized by editing configuration
/// rather than shipping a code change (issue #35).
/// </summary>
public sealed class AgentDetectionOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "AgentDetection";

    /// <summary>
    /// Gets or sets the ordered ruleset. The first rule whose <see cref="AgentDetectionRule.LoginEquals"/>,
    /// <see cref="AgentDetectionRule.LoginEndsWith"/> or <see cref="AgentDetectionRule.AppSlugEquals"/>
    /// entry matches wins.
    /// </summary>
    /// <remarks>
    /// The default rules below cover the agents named in issue #35. Confidence varies by agent:
    /// Dependabot and Renovate's bot-account login conventions are stable and well documented.
    /// GitHub Copilot's coding-agent login is documented by GitHub. Devin's GitHub App login follows its
    /// published integration name. Claude Code, Codex and Cursor do not (as of this writing) have a single
    /// documented, stable login or App-slug convention for PRs they author on a user's behalf — the
    /// patterns below are a best-effort starting point and operators should verify and adjust them
    /// (via configuration, not a deployment) against what actually shows up in their organization's PRs.
    /// Anything that does not match a specific rule but still looks like a bot (see
    /// <see cref="AgentClassifier"/>) falls back to <see cref="OtherBotKey"/>, preserving today's
    /// <c>HasBotInvolvement</c>-only behavior for unrecognized bots.
    /// </remarks>
    public List<AgentDetectionRule> Rules { get; set; } =
    [
        new AgentDetectionRule
        {
            Key = "dependabot",
            DisplayName = "Dependabot",
            LoginEquals = ["dependabot[bot]"],
            AppSlugEquals = ["dependabot"]
        },
        new AgentDetectionRule
        {
            Key = "renovate",
            DisplayName = "Renovate",
            LoginEquals = ["renovate[bot]"],
            AppSlugEquals = ["renovate"]
        },
        new AgentDetectionRule
        {
            Key = "github-copilot",
            DisplayName = "GitHub Copilot",
            LoginEquals = ["copilot-swe-agent[bot]", "copilot[bot]"],
            AppSlugEquals = ["copilot-swe-agent", "copilot"]
        },
        new AgentDetectionRule
        {
            Key = "devin",
            DisplayName = "Devin",
            LoginEquals = ["devin-ai-integration[bot]"],
            AppSlugEquals = ["devin-ai-integration"]
        },
        // Best-effort: no single documented login/App-slug convention as of this writing. Operators
        // should confirm and adjust these against real activity in their organization.
        new AgentDetectionRule
        {
            Key = "claude-code",
            DisplayName = "Claude Code",
            LoginEquals = ["claude[bot]"],
            LoginEndsWith = ["-claude[bot]"],
            AppSlugEquals = ["claude"]
        },
        new AgentDetectionRule
        {
            Key = "codex",
            DisplayName = "Codex",
            LoginEquals = ["chatgpt-codex-connector[bot]"],
            AppSlugEquals = ["chatgpt-codex-connector"]
        },
        new AgentDetectionRule
        {
            Key = "cursor",
            DisplayName = "Cursor",
            LoginEquals = ["cursor[bot]", "cursoragent[bot]"],
            AppSlugEquals = ["cursor"]
        }
    ];

    /// <summary>
    /// Gets or sets the fallback agent key used for activity that is recognized as a bot (see
    /// <see cref="AgentClassifier"/>) but matches no specific <see cref="Rules"/> entry. This preserves
    /// today's <c>HasBotInvolvement</c>-only behavior for unrecognized bots while still recording that
    /// some agent was involved.
    /// </summary>
    public string OtherBotKey { get; set; } = "other-bot";

    /// <summary>Gets or sets the human-readable display name for <see cref="OtherBotKey"/>.</summary>
    public string OtherBotDisplayName { get; set; } = "Other bot";
}

/// <summary>
/// Describes one data-driven agent/bot identification rule (see <see cref="AgentDetectionOptions.Rules"/>).
/// Matching uses OR semantics across all populated criteria on the rule.
/// </summary>
public sealed class AgentDetectionRule
{
    /// <summary>Gets or sets the stable identity key persisted as <see cref="Needly.Domain.NeedlyAction.AgentAuthor"/>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable display name persisted as <see cref="Needly.Domain.NeedlyAction.AgentDisplayName"/>.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets logins matched exactly (case-insensitive), e.g. "dependabot[bot]".</summary>
    public List<string> LoginEquals { get; set; } = [];

    /// <summary>Gets or sets login suffixes matched case-insensitively, e.g. "[bot]".</summary>
    public List<string> LoginEndsWith { get; set; } = [];

    /// <summary>Gets or sets GitHub App slugs matched exactly (case-insensitive), e.g. "dependabot".</summary>
    public List<string> AppSlugEquals { get; set; } = [];
}
