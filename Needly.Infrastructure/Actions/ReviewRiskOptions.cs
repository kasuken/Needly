using Needly.Domain;

namespace Needly.Infrastructure.Actions;

/// <summary>
/// Configures the path-pattern and diff-size signals used to classify pull request review risk
/// (issue #34).
/// </summary>
/// <remarks>
/// Signal weights follow docs/productidea.md section 14: authentication, authorization, database
/// migration, and billing changes are High; infrastructure changes are also treated as High here (the
/// issue's "Medium-High" tier is folded into High to keep a simple four-level
/// <see cref="ReviewRiskLevel"/> scale — a human should still look carefully at infrastructure
/// changes); CI/CD configuration, public API, and large diffs are Medium; generated files and
/// documentation are Low. A pull request whose changed files are entirely test files is also Low. The
/// overall level is the highest level among every matched signal.
/// </remarks>
public sealed class ReviewRiskOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "ReviewRisk";

    /// <summary>Gets or sets the configured path-pattern signals, evaluated independently and combined by OR.</summary>
    public List<ReviewRiskSignalOptions> Signals { get; set; } = DefaultSignals();

    /// <summary>
    /// Gets or sets glob path patterns identifying test files. When every changed file matches one of
    /// these patterns, the "tests only" Low signal is added.
    /// </summary>
    public List<string> TestPathPatterns { get; set; } =
    [
        "**/*Tests.cs",
        "**/*Test.cs",
        "**/*.test.js",
        "**/*.test.ts",
        "**/*.spec.js",
        "**/*.spec.ts",
        "**/tests/**",
        "**/test/**",
        "**/__tests__/**"
    ];

    /// <summary>
    /// Gets or sets the total changed line count (additions plus deletions) that triggers the "large
    /// diff" Medium signal.
    /// </summary>
    public int LargeDiffChangedLines { get; set; } = 500;

    private static List<ReviewRiskSignalOptions> DefaultSignals() =>
    [
        new ReviewRiskSignalOptions
        {
            Name = "authentication",
            Level = ReviewRiskLevel.High,
            PathPatterns = ["**/auth/**", "**/authentication/**", "**/*authenticat*", "**/oauth/**", "**/*login*"]
        },
        new ReviewRiskSignalOptions
        {
            Name = "authorization",
            Level = ReviewRiskLevel.High,
            PathPatterns = ["**/authoriz*/**", "**/*authoriz*", "**/*permission*", "**/*acl*", "**/*rbac*"]
        },
        new ReviewRiskSignalOptions
        {
            Name = "database migration",
            Level = ReviewRiskLevel.High,
            PathPatterns = ["**/migrations/**", "**/*migration*.sql", "**/*Migration*.cs", "**/db/migrate/**"]
        },
        new ReviewRiskSignalOptions
        {
            Name = "billing",
            Level = ReviewRiskLevel.High,
            PathPatterns =
                ["**/billing/**", "**/*billing*", "**/*payment*", "**/*invoice*", "**/*stripe*", "**/*subscription*"]
        },
        new ReviewRiskSignalOptions
        {
            Name = "infrastructure",
            Level = ReviewRiskLevel.High,
            PathPatterns =
            [
                "**/*.tf", "**/*.tfvars", "**/terraform/**", "**/*.bicep", "**/k8s/**", "**/kubernetes/**",
                "**/helm/**", "**/Dockerfile*", "**/docker-compose*.yml"
            ]
        },
        new ReviewRiskSignalOptions
        {
            Name = "CI/CD configuration",
            Level = ReviewRiskLevel.Medium,
            PathPatterns =
                [".github/workflows/**", "**/azure-pipelines.yml", "**/.gitlab-ci.yml", "**/Jenkinsfile", "**/.circleci/**"]
        },
        new ReviewRiskSignalOptions
        {
            Name = "public API",
            Level = ReviewRiskLevel.Medium,
            PathPatterns =
                ["**/Controllers/**", "**/*Controller.cs", "**/api/**", "**/*.proto", "**/openapi*.json", "**/openapi*.yaml", "**/swagger*.json"]
        },
        new ReviewRiskSignalOptions
        {
            Name = "generated files",
            Level = ReviewRiskLevel.Low,
            PathPatterns =
                ["**/*.g.cs", "**/*.generated.cs", "**/*.designer.cs", "**/*.min.js", "**/*.pb.go", "**/package-lock.json", "**/*.lock"]
        },
        new ReviewRiskSignalOptions
        {
            Name = "documentation",
            Level = ReviewRiskLevel.Low,
            PathPatterns = ["**/*.md", "**/*.mdx", "docs/**", "**/CHANGELOG*"]
        }
    ];
}

/// <summary>Configures one path-pattern review risk signal.</summary>
public sealed class ReviewRiskSignalOptions
{
    /// <summary>Gets or sets the signal name shown alongside the matched risk level (for example "authentication").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the risk level this signal contributes when one of its patterns matches.</summary>
    public ReviewRiskLevel Level { get; set; }

    /// <summary>
    /// Gets or sets the glob path patterns that match this signal. Supports <c>**</c> (any number of
    /// path segments), <c>*</c> (any characters within one segment), and <c>?</c> (one character).
    /// Matching is case-insensitive.
    /// </summary>
    public List<string> PathPatterns { get; set; } = [];
}
