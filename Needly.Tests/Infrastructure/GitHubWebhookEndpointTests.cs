using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Needly.Web.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubWebhookEndpointTests
{
    private const string WebhookSecret = "endpoint-test-secret";

    [Fact]
    public async Task Post_ValidThenDuplicateDelivery_ReturnsAcceptedThenOkAndEnqueuesOnce()
    {
        await using var host = await WebhookEndpointHost.CreateAsync();
        var payload = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":501}}");

        using var accepted = await host.Client.SendAsync(CreateRequest("delivery-http", payload, Sign(payload)));
        using var duplicate = await host.Client.SendAsync(CreateRequest("delivery-http", payload, Sign(payload)));

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(1, await host.CountEventsAsync());
        Assert.Single(host.Queue.EventIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Post_MissingOrInvalidSignature_ReturnsUnauthorizedWithoutPersistence(string? signature)
    {
        await using var host = await WebhookEndpointHost.CreateAsync();
        var payload = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":501}}");

        using var response = await host.Client.SendAsync(CreateRequest("delivery-http-rejected", payload, signature));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await host.CountEventsAsync());
        Assert.Empty(host.Queue.EventIds);
    }

    [Fact]
    public async Task Post_PayloadOverConfiguredLimit_ReturnsPayloadTooLargeWithoutPersistence()
    {
        await using var host = await WebhookEndpointHost.CreateAsync();
        var payload = new byte[1025];

        using var response = await host.Client.SendAsync(
            CreateRequest("delivery-http-large", payload, Sign(payload)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, await host.CountEventsAsync());
        Assert.Empty(host.Queue.EventIds);
    }

    [Theory]
    [InlineData("X-GitHub-Delivery")]
    [InlineData("X-GitHub-Event")]
    public async Task Post_MissingDeliveryOrEventHeader_ReturnsBadRequestWithoutPersistence(string headerName)
    {
        await using var host = await WebhookEndpointHost.CreateAsync();
        var payload = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":501}}");
        using var request = CreateRequest("delivery-http-header", payload, Sign(payload));
        request.Headers.Remove(headerName);

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await host.CountEventsAsync());
        Assert.Empty(host.Queue.EventIds);
    }

    private static HttpRequestMessage CreateRequest(string deliveryId, byte[] payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/github")
        {
            Content = new ByteArrayContent(payload)
        };
        request.Headers.Add("X-GitHub-Delivery", deliveryId);
        request.Headers.Add("X-GitHub-Event", "issues");
        if (signature is not null)
        {
            request.Headers.Add("X-Hub-Signature-256", signature);
        }

        return request;
    }

    private static string Sign(byte[] payload) =>
        $"sha256={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), payload)).ToLowerInvariant()}";

    private sealed class RecordingQueue : IGitHubWebhookQueue
    {
        internal List<Guid> EventIds { get; } = [];

        public ValueTask EnqueueAsync(Guid eventId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventIds.Add(eventId);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<Guid> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TestData.CreatedAt;
    }

    private sealed class WebhookEndpointHost : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly WebApplication application;

        private WebhookEndpointHost(
            SqliteConnection connection,
            WebApplication application,
            HttpClient client,
            RecordingQueue queue)
        {
            this.connection = connection;
            this.application = application;
            Client = client;
            Queue = queue;
        }

        internal HttpClient Client { get; }

        internal RecordingQueue Queue { get; }

        internal static async Task<WebhookEndpointHost> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var queue = new RecordingQueue();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<NeedlyDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddSingleton<IGitHubWebhookQueue>(queue);
            builder.Services.AddSingleton<TimeProvider, FixedTimeProvider>();
            builder.Services.AddSingleton(Options.Create(new GitHubAppOptions
            {
                Enabled = true,
                WebhookSecret = WebhookSecret,
                WebhookMaxPayloadBytes = 1024,
                WebhookQueueCapacity = 10,
                WebhookMaxAttempts = 3
            }));
            builder.Services.AddScoped<IGitHubWebhookIngestionService, GitHubWebhookIngestionService>();
            var application = builder.Build();
            application.MapNeedlyGitHubWebhookEndpoints();
            await using (var scope = application.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<NeedlyDbContext>().Database.EnsureCreatedAsync();
            }

            await application.StartAsync();
            return new WebhookEndpointHost(connection, application, application.GetTestClient(), queue);
        }

        internal async Task<int> CountEventsAsync()
        {
            await using var scope = application.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<NeedlyDbContext>().RawEvents.CountAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}