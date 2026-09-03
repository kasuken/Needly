using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Needly.Application.GitHub;
using Needly.Domain;
using Needly.Infrastructure;
using Needly.Infrastructure.GitHub;
using Xunit;

namespace Needly.Tests.Infrastructure;

public sealed class GitHubWebhookProcessingTests
{
    private const string WebhookSecret = "test-webhook-secret";

    [Fact]
    public async Task IngestAsync_ValidSignature_PersistsExactPayloadAndEnqueuesEvent()
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var queue = new RecordingQueue();
        var service = CreateIngestionService(database.Context, queue);
        var payload = Encoding.UTF8.GetBytes(
            "{\"action\":\"opened\",\"installation\":{\"id\":501},\"repository\":{\"id\":701}}");

        var receipt = await service.IngestAsync(
            CreateRequest("delivery-valid", "pull_request", payload, Sign(payload)),
            CancellationToken.None);

        var persisted = await database.Context.RawEvents.AsNoTracking().SingleAsync();
        Assert.False(receipt.IsDuplicate);
        Assert.Equal(receipt.EventId, persisted.Id);
        Assert.Equal(Encoding.UTF8.GetString(payload), persisted.PayloadJson);
        Assert.Equal(501, persisted.GitHubInstallationId);
        Assert.Equal(701, persisted.GitHubRepositoryId);
        Assert.Equal(RawEventStatus.Pending, persisted.Status);
        Assert.Equal([receipt.EventId], queue.EventIds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task IngestAsync_MissingOrInvalidSignature_DoesNotPersistOrEnqueue(string signature)
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var queue = new RecordingQueue();
        var service = CreateIngestionService(database.Context, queue);
        var payload = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":501}}");

        await Assert.ThrowsAsync<GitHubWebhookAuthenticationException>(() => service.IngestAsync(
            CreateRequest("delivery-rejected", "issues", payload, signature),
            CancellationToken.None));

        Assert.Equal(0, await database.Context.RawEvents.CountAsync());
        Assert.Empty(queue.EventIds);
    }

    [Fact]
    public async Task IngestAsync_DuplicateDelivery_AcknowledgesExistingEventWithoutReenqueue()
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var queue = new RecordingQueue();
        var service = CreateIngestionService(database.Context, queue);
        var payload = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":501}}");
        var request = CreateRequest("delivery-duplicate", "issues", payload, Sign(payload));

        var first = await service.IngestAsync(request, CancellationToken.None);
        var duplicate = await service.IngestAsync(request, CancellationToken.None);

        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(first.EventId, duplicate.EventId);
        Assert.Equal(1, await database.Context.RawEvents.CountAsync());
        Assert.Equal([first.EventId], queue.EventIds);
    }

    [Fact]
    public async Task DispatchAsync_UnknownEvent_MarksStoredEventSkipped()
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var rawEvent = CreateRawEvent("unknown_event", "delivery-unknown");
        database.Context.RawEvents.Add(rawEvent);
        await database.Context.SaveChangesAsync();
        var dispatcher = CreateDispatcher(database.Context, new RecordingInventoryService(), new RecordingMembershipService());

        await dispatcher.DispatchAsync(rawEvent.Id, CancellationToken.None);

        Assert.Equal(RawEventStatus.Skipped, rawEvent.Status);
        Assert.Equal(1, rawEvent.AttemptCount);
        Assert.Equal(TestData.CreatedAt, rawEvent.ProcessedAt);
    }

    [Fact]
    public async Task DispatchAsync_KnownActionEvent_InvokesActionHandlerAndMarksProcessed()
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var rawEvent = CreateRawEvent("pull_request", "delivery-known");
        database.Context.RawEvents.Add(rawEvent);
        await database.Context.SaveChangesAsync();
        var actionHandler = new RecordingActionHandler();
        var dispatcher = CreateDispatcher(
            database.Context,
            new RecordingInventoryService(),
            new RecordingMembershipService(),
            actionHandler: actionHandler);

        await dispatcher.DispatchAsync(rawEvent.Id, CancellationToken.None);

        var dispatched = Assert.Single(actionHandler.Events);
        Assert.Equal(rawEvent.Id, dispatched.EventId);
        Assert.Equal("pull_request", dispatched.EventName);
        Assert.Equal(RawEventStatus.Processed, rawEvent.Status);
    }

    [Fact]
    public async Task DispatchAsync_ActionInventoryUnavailable_MarksStoredEventSkipped()
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var rawEvent = CreateRawEvent("pull_request", "delivery-untracked-installation");
        database.Context.RawEvents.Add(rawEvent);
        await database.Context.SaveChangesAsync();
        var dispatcher = CreateDispatcher(
            database.Context,
            new RecordingInventoryService(),
            new RecordingMembershipService(),
            actionHandler: new RecordingActionHandler(
                new GitHubActionInventoryUnavailableException("Installation is not tracked.")));

        await dispatcher.DispatchAsync(rawEvent.Id, CancellationToken.None);

        Assert.Equal(RawEventStatus.Skipped, rawEvent.Status);
    }

    [Fact]
    public async Task DispatchAsync_TransientFailure_PersistsRetryStatusAndRequeues()
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var rawEvent = CreateRawEvent(
            "installation",
            "delivery-retry",
            "{\"action\":\"created\",\"installation\":{\"id\":501,\"account\":{\"id\":601,\"login\":\"octo-org\",\"type\":\"Organization\"},\"repository_selection\":\"selected\"},\"repositories\":[]}");
        database.Context.RawEvents.Add(rawEvent);
        await database.Context.SaveChangesAsync();
        var queue = new RecordingQueue();
        var dispatcher = CreateDispatcher(
            database.Context,
            new RecordingInventoryService(new HttpRequestException("temporary")),
            new RecordingMembershipService(),
            queue);

        await dispatcher.DispatchAsync(rawEvent.Id, CancellationToken.None);

        Assert.Equal(RawEventStatus.RetryPending, rawEvent.Status);
        Assert.Equal(1, rawEvent.AttemptCount);
        Assert.Equal(TestData.CreatedAt.AddSeconds(1), rawEvent.NextAttemptAt);
        Assert.Equal("HttpRequestException: transient webhook processing failure.", rawEvent.LastError);
        Assert.Equal([rawEvent.Id], queue.EventIds);
    }

    [Fact]
    public async Task DispatchAsync_TransientFailureAtMaximumAttempt_MarksFailedWithoutRequeue()
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var rawEvent = CreateRawEvent(
            "installation",
            "delivery-retry-exhausted",
            "{\"action\":\"created\",\"installation\":{\"id\":501,\"account\":{\"id\":601,\"login\":\"octo-org\",\"type\":\"Organization\"},\"repository_selection\":\"selected\"},\"repositories\":[]}");
        rawEvent.MarkProcessing();
        rawEvent.MarkFailed("temporary", TestData.CreatedAt);
        rawEvent.MarkProcessing();
        rawEvent.MarkFailed("temporary", TestData.CreatedAt);
        database.Context.RawEvents.Add(rawEvent);
        await database.Context.SaveChangesAsync();
        var queue = new RecordingQueue();
        var dispatcher = CreateDispatcher(
            database.Context,
            new RecordingInventoryService(new HttpRequestException("temporary")),
            new RecordingMembershipService(),
            queue);

        await dispatcher.DispatchAsync(rawEvent.Id, CancellationToken.None);

        Assert.Equal(RawEventStatus.Failed, rawEvent.Status);
        Assert.Equal(3, rawEvent.AttemptCount);
        Assert.Null(rawEvent.NextAttemptAt);
        Assert.Empty(queue.EventIds);
    }

    [Fact]
    public async Task RecoverAsync_PendingInterruptedAndRetryable_RepairsAndQueuesInReceiptOrder()
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var pending = CreateRawEvent("issues", "delivery-pending", receivedAt: TestData.CreatedAt);
        var interrupted = CreateRawEvent(
            "issues",
            "delivery-interrupted",
            receivedAt: TestData.CreatedAt.AddSeconds(1));
        interrupted.MarkProcessing();
        var retryable = CreateRawEvent(
            "issues",
            "delivery-retryable",
            receivedAt: TestData.CreatedAt.AddSeconds(2));
        retryable.MarkProcessing();
        retryable.MarkFailed("temporary", TestData.CreatedAt.AddMinutes(1));
        var completed = CreateRawEvent(
            "issues",
            "delivery-completed",
            receivedAt: TestData.CreatedAt.AddSeconds(3));
        completed.MarkProcessing();
        completed.MarkProcessed(TestData.CreatedAt.AddSeconds(4));
        database.Context.RawEvents.AddRange(pending, interrupted, retryable, completed);
        await database.Context.SaveChangesAsync();
        var queue = new RecordingQueue();
        var service = new GitHubWebhookRecoveryService(
            database.Context,
            queue,
            NullLogger<GitHubWebhookRecoveryService>.Instance);

        var recoveredCount = await service.RecoverAsync(CancellationToken.None);

        Assert.Equal(3, recoveredCount);
        Assert.Equal([pending.Id, interrupted.Id, retryable.Id], queue.EventIds);
        Assert.Equal(RawEventStatus.Pending, interrupted.Status);
        Assert.Equal(RawEventStatus.RetryPending, retryable.Status);
        Assert.Equal(RawEventStatus.Processed, completed.Status);
    }

    [Fact]
    public async Task DispatchAsync_InstallationEvent_UsesInstallationInventoryHandler()
    {
        await using var database = await WebhookTestDatabase.CreateAsync();
        var payload = "{\"action\":\"deleted\",\"installation\":{\"id\":501,\"account\":{\"id\":601,\"login\":\"octo-org\",\"type\":\"Organization\"},\"repository_selection\":\"selected\"}}";
        var rawEvent = CreateRawEvent("installation", "delivery-installation", payload);
        database.Context.RawEvents.Add(rawEvent);
        await database.Context.SaveChangesAsync();
        var inventory = new RecordingInventoryService();
        var dispatcher = CreateDispatcher(database.Context, inventory, new RecordingMembershipService());

        await dispatcher.DispatchAsync(rawEvent.Id, CancellationToken.None);

        Assert.Equal("deleted", Assert.Single(inventory.InstallationEvents).Action);
        Assert.Equal(RawEventStatus.Processed, rawEvent.Status);
    }

    [Fact]
    public async Task BackgroundService_SameRepositoryEvents_DispatchesInReceiptOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var queue = new GitHubWebhookQueue(Options.Create(CreateOptions()));
        var dispatcher = new RecordingDispatcher(expectedCount: 2);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NeedlyDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<IGitHubWebhookQueue>(queue);
        services.AddSingleton<IGitHubWebhookDispatcher>(dispatcher);
        services.AddScoped<IGitHubWebhookRecoveryService, GitHubWebhookRecoveryService>();
        await using var provider = services.BuildServiceProvider();
        var first = RawEvent.CreateDelivery(
            Guid.NewGuid(), null, 501, null, 701, "delivery-order-1", "issues", null,
            "{\"installation\":{\"id\":501},\"repository\":{\"id\":701}}",
            TestData.CreatedAt);
        var second = RawEvent.CreateDelivery(
            Guid.NewGuid(), null, 501, null, 701, "delivery-order-2", "issues", null,
            "{\"installation\":{\"id\":501},\"repository\":{\"id\":701}}",
            TestData.CreatedAt.AddSeconds(1));
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<NeedlyDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.RawEvents.AddRange(second, first);
            await context.SaveChangesAsync();
        }

        var backgroundService = new GitHubWebhookBackgroundService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>());
        await backgroundService.StartAsync(CancellationToken.None);
        await dispatcher.Completed.WaitAsync(TimeSpan.FromSeconds(5));
        await backgroundService.StopAsync(CancellationToken.None);

        Assert.Equal([first.Id, second.Id], dispatcher.EventIds);
    }

    private static GitHubWebhookIngestionService CreateIngestionService(
        NeedlyDbContext context,
        RecordingQueue queue) =>
        new(
            context,
            queue,
            Options.Create(CreateOptions()),
            new FixedTimeProvider(TestData.CreatedAt),
            NullLogger<GitHubWebhookIngestionService>.Instance);

    private static GitHubWebhookDispatcher CreateDispatcher(
        NeedlyDbContext context,
        IInstallationInventoryService inventory,
        IGitHubOrganizationMembershipService membership,
        RecordingQueue? queue = null,
        IGitHubActionEventHandler? actionHandler = null) =>
        new(
            context,
            inventory,
            membership,
            actionHandler ?? new RecordingActionHandler(),
            queue ?? new RecordingQueue(),
            Options.Create(CreateOptions()),
            new FixedTimeProvider(TestData.CreatedAt),
            NullLogger<GitHubWebhookDispatcher>.Instance);

    private static GitHubAppOptions CreateOptions() => new()
    {
        Enabled = true,
        WebhookSecret = WebhookSecret,
        WebhookMaxPayloadBytes = 1024,
        WebhookQueueCapacity = 10,
        WebhookMaxAttempts = 3
    };

    private static GitHubWebhookRequest CreateRequest(
        string deliveryId,
        string eventName,
        byte[] payload,
        string signature) =>
        new(deliveryId, eventName, signature, payload);

    private static RawEvent CreateRawEvent(
        string eventName,
        string deliveryId,
        string payload = "{\"installation\":{\"id\":501}}",
        DateTimeOffset? receivedAt = null) =>
        RawEvent.CreateDelivery(
            Guid.NewGuid(),
            null,
            501,
            null,
            null,
            deliveryId,
            eventName,
            null,
            payload,
            receivedAt ?? TestData.CreatedAt);

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

    private sealed class RecordingInventoryService(Exception? installationException = null)
        : IInstallationInventoryService
    {
        internal List<GitHubInstallationEvent> InstallationEvents { get; } = [];

        public Task HandleInstallationAsync(
            GitHubInstallationEvent installationEvent,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (installationException is not null)
            {
                return Task.FromException(installationException);
            }

            InstallationEvents.Add(installationEvent);
            return Task.CompletedTask;
        }

        public Task HandleRepositoriesAsync(
            GitHubInstallationRepositoriesEvent repositoriesEvent,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task LinkUserAsync(
            Guid needlyUserId,
            long gitHubInstallationId,
            DateTimeOffset linkedAt,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingMembershipService : IGitHubOrganizationMembershipService
    {
        public Task SyncAsync(long gitHubInstallationId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleMemberAsync(
            GitHubMemberEvent memberEvent,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleTeamAsync(
            GitHubTeamEvent teamEvent,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleMembershipAsync(
            GitHubMembershipEvent membershipEvent,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingActionHandler(Exception? exception = null) : IGitHubActionEventHandler
    {
        internal List<GitHubStoredEvent> Events { get; } = [];

        public Task HandleAsync(GitHubStoredEvent storedEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (exception is not null)
            {
                return Task.FromException(exception);
            }

            Events.Add(storedEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDispatcher(int expectedCount) : IGitHubWebhookDispatcher
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal List<Guid> EventIds { get; } = [];

        internal Task Completed => completion.Task;

        public Task DispatchAsync(Guid eventId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EventIds.Add(eventId);
            if (EventIds.Count == expectedCount)
            {
                completion.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class WebhookTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private WebhookTestDatabase(SqliteConnection connection, NeedlyDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal NeedlyDbContext Context { get; }

        internal static async Task<WebhookTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new NeedlyDbContext(
                new DbContextOptionsBuilder<NeedlyDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new WebhookTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}