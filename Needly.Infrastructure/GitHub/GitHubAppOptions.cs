using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Needly.Infrastructure.GitHub;

/// <summary>Contains configuration for the Needly GitHub App integration.</summary>
public sealed class GitHubAppOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "GitHubApp";

    /// <summary>Gets or sets whether GitHub integration is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the numeric GitHub App identifier.</summary>
    public long AppId { get; set; }

    /// <summary>Gets or sets the public GitHub App slug.</summary>
    public string AppSlug { get; set; } = string.Empty;

    /// <summary>Gets or sets the OAuth client identifier.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the OAuth client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Gets or sets the RSA private key in PEM format.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the webhook secret used by the delivery endpoint.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum accepted webhook payload size in bytes.</summary>
    public int WebhookMaxPayloadBytes { get; set; } = 1_048_576;

    /// <summary>Gets or sets the bounded in-process webhook queue capacity.</summary>
    public int WebhookQueueCapacity { get; set; } = 1_024;

    /// <summary>Gets or sets the maximum number of webhook processing attempts.</summary>
    public int WebhookMaxAttempts { get; set; } = 5;
}

/// <summary>Validates enabled GitHub App configuration at application startup.</summary>
public sealed class GitHubAppOptionsValidator : IValidateOptions<GitHubAppOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GitHubAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.AppId <= 0)
        {
            failures.Add("GitHubApp:AppId must be a positive GitHub App identifier.");
        }

        AddRequiredFailure(options.AppSlug, "AppSlug", failures);
        AddRequiredFailure(options.ClientId, "ClientId", failures);
        AddRequiredFailure(options.ClientSecret, "ClientSecret", failures);
        AddRequiredFailure(options.PrivateKey, "PrivateKey", failures);
        AddRequiredFailure(options.WebhookSecret, "WebhookSecret", failures);

        if (options.WebhookMaxPayloadBytes is < 1 or > 10_485_760)
        {
            failures.Add("GitHubApp:WebhookMaxPayloadBytes must be between 1 and 10485760.");
        }

        if (options.WebhookQueueCapacity is < 1 or > 100_000)
        {
            failures.Add("GitHubApp:WebhookQueueCapacity must be between 1 and 100000.");
        }

        if (options.WebhookMaxAttempts is < 1 or > 20)
        {
            failures.Add("GitHubApp:WebhookMaxAttempts must be between 1 and 20.");
        }

        if (!string.IsNullOrWhiteSpace(options.PrivateKey))
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(options.PrivateKey);
            }
            catch (ArgumentException)
            {
                failures.Add("GitHubApp:PrivateKey must contain a valid RSA private key in PEM format.");
            }
            catch (CryptographicException)
            {
                failures.Add("GitHubApp:PrivateKey must contain a valid RSA private key in PEM format.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddRequiredFailure(string value, string propertyName, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"GitHubApp:{propertyName} is required when GitHub integration is enabled.");
        }
    }
}