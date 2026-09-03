using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;

namespace Needly.Infrastructure.GitHub;

/// <summary>Creates short-lived RS256 tokens used to authenticate as the GitHub App.</summary>
public sealed class GitHubAppJwtProvider(
    IOptions<GitHubAppOptions> options,
    TimeProvider timeProvider) : IGitHubAppJwtProvider
{
    private readonly GitHubAppOptions options = options?.Value
        ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public string CreateToken()
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("GitHub integration is disabled.");
        }

        var now = timeProvider.GetUtcNow();
        var header = JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = now.AddSeconds(-60).ToUnixTimeSeconds(),
            exp = now.AddMinutes(9).ToUnixTimeSeconds(),
            iss = options.AppId
        });
        var encodedHeader = Base64UrlEncode(header);
        var encodedPayload = Base64UrlEncode(payload);
        var signingInput = $"{encodedHeader}.{encodedPayload}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(options.PrivateKey);
        var signature = rsa.SignData(
            System.Text.Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}