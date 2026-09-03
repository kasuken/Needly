using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubAppJwtProviderTests
{
    [Fact]
    public void CreateToken_EnabledApp_ContainsExpectedClaimsAndValidSignature()
    {
        using var rsa = RSA.Create(2048);
        var now = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
        var options = GitHubAppOptionsTests.CreateEnabledOptions(rsa.ExportRSAPrivateKeyPem());
        var provider = new GitHubAppJwtProvider(
            Options.Create(options),
            new FixedTimeProvider(now));

        var token = provider.CreateToken();
        var segments = token.Split('.');

        Assert.Equal(3, segments.Length);
        using var header = JsonDocument.Parse(Base64UrlDecode(segments[0]));
        using var payload = JsonDocument.Parse(Base64UrlDecode(segments[1]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.RootElement.GetProperty("typ").GetString());
        Assert.Equal(options.AppId, payload.RootElement.GetProperty("iss").GetInt64());
        Assert.Equal(now.AddSeconds(-60).ToUnixTimeSeconds(), payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(now.AddMinutes(9).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
        Assert.True(rsa.VerifyData(
            Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"),
            Base64UrlDecode(segments[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}