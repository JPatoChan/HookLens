using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HookLens.Data;
using HookLens.Models;
using HookLens.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HookLens.Tests;

public class WebhookCaptureEndpointsTests
{
    private sealed class RecordingReplayHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

        public RecordingReplayHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public RecordingReplayHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
            : this((request, _) => responseFactory(request))
        {
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await _responseFactory(request, cancellationToken);
        }
    }

    private sealed class TemporarySqliteDatabase : IDisposable
    {
        private readonly string _directoryPath;

        public TemporarySqliteDatabase()
        {
            _directoryPath = Path.Combine(Path.GetTempPath(), $"hooklens-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directoryPath);
            DatabasePath = Path.Combine(_directoryPath, "hooklens.db");
        }

        public string DatabasePath { get; }

        public string ConnectionString => $"Data Source={DatabasePath}";

        public void Dispose()
        {
            foreach (var fileName in new[] { DatabasePath, $"{DatabasePath}-shm", $"{DatabasePath}-wal" })
            {
                if (File.Exists(fileName))
                {
                    File.Delete(fileName);
                }
            }

            if (Directory.Exists(_directoryPath))
            {
                Directory.Delete(_directoryPath, recursive: true);
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(TemporarySqliteDatabase database, HttpMessageHandler? replayHandler = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<DbContextOptions<HookLensDbContext>>();
                    services.RemoveAll<HookLensDbContext>();

                    services.AddDbContext<HookLensDbContext>(options =>
                        options.UseSqlite(database.ConnectionString));

                    if (replayHandler is not null)
                    {
                        services.RemoveAll<IWebhookReplayService>();
                        services.AddHttpClient<IWebhookReplayService, WebhookReplayService>()
                            .ConfigurePrimaryHttpMessageHandler(() => replayHandler);
                    }
                });

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:HookLens"] = database.ConnectionString
                    });
                });
            });
    }

    [Fact]
    public void CapturedRequestEntity_ShouldStoreReceivedAtUtcAsUtcDateTime()
    {
        var propertyType = typeof(CapturedRequestEntity).GetProperty(nameof(CapturedRequestEntity.ReceivedAtUtc))?.PropertyType;

        Assert.Equal(typeof(DateTime), propertyType);
    }

    [Fact]
    public void PersistedHeaders_ShouldBeAccessibleCaseInsensitivelyAfterRetrieval()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWebhookCaptureStore>();

        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["application/json"],
            ["X-Test-Header"] = ["alpha", "beta"]
        };

        var captured = store.Capture("github", headers, "{\"ok\":true}", DateTimeOffset.UtcNow);

        Assert.Equal(["application/json"], captured.Headers["content-type"]);
        Assert.Equal(["alpha", "beta"], captured.Headers["x-test-header"]);

        var newest = store.GetAllNewestFirst();
        Assert.Equal(["application/json"], newest[0].Headers["CONTENT-TYPE"]);
        Assert.Equal(["alpha", "beta"], newest[0].Headers["x-test-header"]);

        Assert.True(store.TryGetById(captured.Id, out var byId));
        Assert.NotNull(byId);
        Assert.Equal(["application/json"], byId!.Headers["Content-Type"]);
        Assert.Equal(["alpha", "beta"], byId.Headers["x-test-header"]);
    }

    [Fact]
    public async Task Homepage_ShouldServeDashboardShell()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("HookLens", html);
        Assert.Contains("Capture. Inspect. Debug.", html);
        Assert.Contains("id=\"requestList\"", html);
        Assert.Contains("id=\"detailBody\"", html);
    }

    [Fact]
    public async Task Homepage_ShouldIncludeEmptyStateWhenNoRequestsExist()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("No captured requests yet", html);
    }

    [Fact]
    public async Task Capture_ShouldPersistRequestAndReturnCreatedResponse()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/capture/github",
            new StringContent("{\"event\":\"ping\",\"ok\":true}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("github", payload.GetProperty("source").GetString());
        Assert.True(payload.TryGetProperty("body", out var bodyProp));
        Assert.Contains("ping", bodyProp.GetString());
        Assert.True(payload.TryGetProperty("id", out var idProp));
        Assert.False(string.IsNullOrWhiteSpace(idProp.GetString()));
    }

    [Fact]
    public async Task Requests_ShouldReturnNewestFirst()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        await client.PostAsync("/capture/first", new StringContent("{\"sequence\":1}", Encoding.UTF8, "application/json"));
        await client.PostAsync("/capture/second", new StringContent("{\"sequence\":2}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("/requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.ValueKind == JsonValueKind.Array);
        Assert.Equal(2, payload.GetArrayLength());
        Assert.Equal("second", payload[0].GetProperty("source").GetString());
        Assert.Equal("first", payload[1].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Requests_ShouldFilterBySourceCaseInsensitively()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        await client.PostAsync("/capture/GitHub", new StringContent("{\"event\":\"one\"}", Encoding.UTF8, "application/json"));
        await client.PostAsync("/capture/githubish", new StringContent("{\"event\":\"two\"}", Encoding.UTF8, "application/json"));
        await client.PostAsync("/capture/slack", new StringContent("{\"event\":\"three\"}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("/requests?source=github");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetArrayLength());
        Assert.Equal("GitHub", payload[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Requests_ShouldFilterByBodyTextSearchCaseInsensitively()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        await client.PostAsync("/capture/github", new StringContent("{\"event\":\"alpha\"}", Encoding.UTF8, "application/json"));
        await client.PostAsync("/capture/slack", new StringContent("{\"event\":\"beta\"}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("/requests?q=ALPHA");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetArrayLength());
        Assert.Equal("github", payload[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Requests_ShouldFilterByHeaderTextSearch()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "http://localhost/capture/github");
        firstRequest.Headers.Add("X-Trace-Id", "trace-4321");
        firstRequest.Content = new StringContent("{\"event\":\"alpha\"}", Encoding.UTF8, "application/json");
        var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        await client.PostAsync("/capture/slack", new StringContent("{\"event\":\"beta\"}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("/requests?q=trace-4321");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetArrayLength());
        Assert.Equal("github", payload[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Requests_ShouldCombineSourceAndTextFilters()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        await client.PostAsync("/capture/github", new StringContent("{\"event\":\"alpha\"}", Encoding.UTF8, "application/json"));
        await client.PostAsync("/capture/slack", new StringContent("{\"event\":\"alpha\"}", Encoding.UTF8, "application/json"));
        await client.PostAsync("/capture/slack", new StringContent("{\"event\":\"beta\"}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("/requests?source=slack&q=ALPHA");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetArrayLength());
        Assert.Equal("slack", payload[0].GetProperty("source").GetString());
        Assert.Contains("alpha", payload[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task Requests_ShouldReturnEmptyArrayWhenNoFiltersMatch()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        await client.PostAsync("/capture/github", new StringContent("{\"event\":\"alpha\"}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("/requests?source=slack&q=nope");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
        Assert.Equal(0, payload.GetArrayLength());
    }

    [Fact]
    public async Task Requests_ShouldIgnoreWhitespaceOnlyFilters()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        await client.PostAsync("/capture/github", new StringContent("{\"event\":\"alpha\"}", Encoding.UTF8, "application/json"));
        await client.PostAsync("/capture/slack", new StringContent("{\"event\":\"beta\"}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("/requests?source=%20%20&q=%20%20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, payload.GetArrayLength());
    }

    [Fact]
    public async Task GetRequestById_ShouldReturnMatchingCapturedRequest()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        var captureResponse = await client.PostAsync(
            "/capture/slack",
            new StringContent("{\"event\":\"deployment\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, captureResponse.StatusCode);

        var captured = await captureResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = captured.GetProperty("id").GetString();

        var response = await client.GetAsync($"/requests/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, payload.GetProperty("id").GetString());
        Assert.Equal("slack", payload.GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetRequestById_WhenMissing_ShouldReturnNotFound()
    {
        using var database = new TemporarySqliteDatabase();
        using var factory = CreateFactory(database);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/requests/not-found-id");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Replay_ShouldReturnNotFoundForMissingRequest()
    {
        using var database = new TemporarySqliteDatabase();
        using var replayHandler = new RecordingReplayHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var factory = CreateFactory(database, replayHandler);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/requests/not-found/replay", new { targetUrl = "https://example.com/webhook" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(replayHandler.Requests);
    }

    [Fact]
    public async Task Replay_ShouldRejectMissingOrInvalidTargetUrl()
    {
        using var database = new TemporarySqliteDatabase();
        using var replayHandler = new RecordingReplayHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var factory = CreateFactory(database, replayHandler);
        using var client = factory.CreateClient();

        var captureResponse = await client.PostAsync(
            "/capture/replay-validation",
            new StringContent("{\"event\":\"ping\"}", Encoding.UTF8, "application/json"));

        var captured = await captureResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = captured.GetProperty("id").GetString();

        var missingResponse = await client.PostAsJsonAsync($"/requests/{id}/replay", new { targetUrl = "" });
        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);

        var badResponse = await client.PostAsJsonAsync($"/requests/{id}/replay", new { targetUrl = "ftp://example.com/webhook" });
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);

        Assert.Empty(replayHandler.Requests);
    }

    [Fact]
    public async Task Replay_ShouldPreserveContentTypeParametersAndForwardSupportedHeaders()
    {
        var requestHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["application/json; charset=utf-8"],
            ["X-Trace-Id"] = ["trace-123"],
            ["Content-Disposition"] = ["attachment; filename=\"payload.json\""],
            ["Host"] = ["injected.example"],
            ["Connection"] = ["keep-alive"],
            ["Content-Length"] = ["999"]
        };

        var captured = new CapturedRequest(
            Id: "id-123",
            Source: "github",
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            Headers: requestHeaders,
            Body: "{\"event\":\"ping\",\"ok\":true}");

        var handler = new RecordingReplayHandler(async request =>
        {
            Assert.Equal("https://localhost:8080/webhook", request.RequestUri!.ToString());
            Assert.Equal("application/json; charset=utf-8", request.Content!.Headers.ContentType!.ToString());
            Assert.Equal("{\"event\":\"ping\",\"ok\":true}", await request.Content.ReadAsStringAsync());
            Assert.True(request.Headers.TryGetValues("X-Trace-Id", out var traceIds));
            Assert.Equal("trace-123", traceIds.Single());
            Assert.True(request.Content.Headers.TryGetValues("Content-Disposition", out var dispositionValues));
            Assert.Equal("attachment; filename=\"payload.json\"", dispositionValues.Single());
            Assert.False(request.Headers.Contains("Host"));
            Assert.False(request.Headers.Contains("Connection"));
            Assert.NotEqual(999, request.Content.Headers.ContentLength);
            Assert.Equal(26, request.Content.Headers.ContentLength);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        var service = new WebhookReplayService(new HttpClient(handler));
        var result = await service.ReplayAsync(captured, new Uri("https://localhost:8080/webhook"));

        Assert.Equal(202, result.StatusCode);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Replay_ShouldIgnoreMalformedCapturedContentType()
    {
        var captured = new CapturedRequest(
            Id: "id-123",
            Source: "github",
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            Headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["not valid content-type"],
                ["X-Trace-Id"] = ["trace-456"]
            },
            Body: "{\"event\":\"ping\"}");

        var handler = new RecordingReplayHandler(async request =>
        {
            Assert.Equal("{\"event\":\"ping\"}", await request.Content!.ReadAsStringAsync());
            Assert.Equal("trace-456", request.Headers.GetValues("X-Trace-Id").Single());
            Assert.Equal(HttpMethod.Post, request.Method);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        var service = new WebhookReplayService(new HttpClient(handler));
        var result = await service.ReplayAsync(captured, new Uri("https://localhost:8080/webhook"));

        Assert.Equal(202, result.StatusCode);
        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Replay_ShouldPropagateCallerCancellation()
    {
        var captured = new CapturedRequest(
            Id: "id-123",
            Source: "github",
            ReceivedAtUtc: DateTimeOffset.UtcNow,
            Headers: new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/json; charset=utf-8"]
            },
            Body: "{\"event\":\"ping\"}");

        var handler = new RecordingReplayHandler(async (request, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var service = new WebhookReplayService(new HttpClient(handler));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReplayAsync(captured, new Uri("https://example.com/webhook"), cancellationSource.Token));
    }

    [Fact]
    public async Task Replay_ShouldHandleDownstreamFailureGracefully()
    {
        using var database = new TemporarySqliteDatabase();
        using var replayHandler = new RecordingReplayHandler(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException("downstream unavailable")));
        using var factory = CreateFactory(database, replayHandler);
        using var client = factory.CreateClient();

        var captureResponse = await client.PostAsync(
            "/capture/replay-failure",
            new StringContent("{\"event\":\"failure\"}", Encoding.UTF8, "application/json"));

        var captured = await captureResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = captured.GetProperty("id").GetString();

        var response = await client.PostAsJsonAsync($"/requests/{id}/replay", new { targetUrl = "https://example.com/webhook" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal("https://example.com/webhook", result.GetProperty("targetUrl").GetString());
        Assert.True(result.TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }

    [Fact]
    public async Task Replay_ShouldNotMutateOriginalCapturedRequest()
    {
        using var database = new TemporarySqliteDatabase();
        using var replayHandler = new RecordingReplayHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var factory = CreateFactory(database, replayHandler);
        using var client = factory.CreateClient();

        var captureResponse = await client.PostAsync(
            "/capture/replay-no-mutate",
            new StringContent("{\"event\":\"original\"}", Encoding.UTF8, "application/json"));

        var captured = await captureResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = captured.GetProperty("id").GetString();

        var beforeResponse = await client.GetAsync($"/requests/{id}");
        var beforePayload = await beforeResponse.Content.ReadFromJsonAsync<JsonElement>();

        var replayResponse = await client.PostAsJsonAsync($"/requests/{id}/replay", new { targetUrl = "https://example.com/webhook" });
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);

        var afterResponse = await client.GetAsync($"/requests/{id}");
        var afterPayload = await afterResponse.Content.ReadFromJsonAsync<JsonElement>();
        var allRequestsResponse = await client.GetAsync("/requests");
        var allRequestsPayload = await allRequestsResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(beforePayload.GetProperty("body").GetString(), afterPayload.GetProperty("body").GetString());
        Assert.Equal(beforePayload.GetProperty("source").GetString(), afterPayload.GetProperty("source").GetString());
        Assert.Equal(1, allRequestsPayload.GetArrayLength());
    }

    [Fact]
    public async Task Capture_ShouldBeRetrievableAfterStorageIsRecreated()
    {
        using var database = new TemporarySqliteDatabase();
        string? capturedId;

        using (var factory = CreateFactory(database))
        {
            using var client = factory.CreateClient();

            var captureResponse = await client.PostAsync(
                "/capture/persisted",
                new StringContent("{\"event\":\"recreated\"}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.Created, captureResponse.StatusCode);

            var captured = await captureResponse.Content.ReadFromJsonAsync<JsonElement>();
            capturedId = captured.GetProperty("id").GetString();
            Assert.NotNull(capturedId);
        }

        using (var factory = CreateFactory(database))
        {
            using var client = factory.CreateClient();

            var response = await client.GetAsync($"/requests/{capturedId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("persisted", payload.GetProperty("source").GetString());
            Assert.Equal("{\"event\":\"recreated\"}", payload.GetProperty("body").GetString());
        }
    }
}
