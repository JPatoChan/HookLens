using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HookLens.Tests;

public class WebhookCaptureEndpointsTests
{
    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>();
    }

    [Fact]
    public async Task Capture_ShouldPersistRequestAndReturnCreatedResponse()
    {
        using var factory = CreateFactory();
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
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        await client.PostAsync("/capture/first", new StringContent("{\"sequence\":1}", Encoding.UTF8, "application/json"));
        await client.PostAsync("/capture/second", new StringContent("{\"sequence\":2}", Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("/requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.ValueKind == JsonValueKind.Array);
        Assert.True(payload.GetArrayLength() >= 2);
        Assert.Equal("second", payload[0].GetProperty("source").GetString());
        Assert.Equal("first", payload[1].GetProperty("source").GetString());
    }

    [Fact]
    public async Task GetRequestById_ShouldReturnMatchingCapturedRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var captureResponse = await client.PostAsync(
            "/capture/slack",
            new StringContent("{\"event\":\"deployment\"}", Encoding.UTF8, "application/json"));

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
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/requests/not-found-id");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
