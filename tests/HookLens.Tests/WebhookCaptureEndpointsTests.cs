using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HookLens.Data;
using HookLens.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HookLens.Tests;

public class WebhookCaptureEndpointsTests
{
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

    private static WebApplicationFactory<Program> CreateFactory(TemporarySqliteDatabase database)
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
