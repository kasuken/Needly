using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubAppOptionsTests
{
    [Fact]
    public void Validate_DisabledWithMissingValues_Succeeds()
    {
        var result = new GitHubAppOptionsValidator().Validate(null, new GitHubAppOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EnabledWithMissingValues_FailsEveryRequiredSetting()
    {
        var options = new GitHubAppOptions { Enabled = true };

        var result = new GitHubAppOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(6, result.Failures.Count());
        Assert.Contains(result.Failures, failure => failure.Contains("AppId", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("AppSlug", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("ClientId", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("ClientSecret", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("PrivateKey", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("WebhookSecret", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EnabledWithValidValues_Succeeds()
    {
        using var rsa = RSA.Create(2048);
        var options = CreateEnabledOptions(rsa.ExportRSAPrivateKeyPem());

        var result = new GitHubAppOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EnabledWithMalformedPrivateKey_FailsPrivateKeyValidation()
    {
        var options = CreateEnabledOptions("not-a-pem-key");

        var result = new GitHubAppOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("valid RSA private key", StringComparison.Ordinal));
    }

    internal static GitHubAppOptions CreateEnabledOptions(string privateKey) =>
        new()
        {
            Enabled = true,
            AppId = 12345,
            AppSlug = "needly-test",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            PrivateKey = privateKey,
            WebhookSecret = "webhook-secret"
        };
}