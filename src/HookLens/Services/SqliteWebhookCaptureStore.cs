using System.Text.Json;
using HookLens.Data;
using HookLens.Models;
using Microsoft.EntityFrameworkCore;

namespace HookLens.Services;

public sealed class SqliteWebhookCaptureStore : IWebhookCaptureStore
{
    private readonly HookLensDbContext _dbContext;

    public SqliteWebhookCaptureStore(HookLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private static IReadOnlyDictionary<string, string[]> DeserializeHeaders(string headersJson)
    {
        var headers = JsonSerializer.Deserialize<Dictionary<string, string[]>>(headersJson)
            ?? new Dictionary<string, string[]>();

        return new Dictionary<string, string[]>(headers, StringComparer.OrdinalIgnoreCase);
    }

    public CapturedRequest Capture(
        string source,
        IReadOnlyDictionary<string, string[]> headers,
        string body,
        DateTimeOffset receivedAtUtc)
    {
        var capturedRequest = new CapturedRequest(
            Id: Guid.NewGuid().ToString("N"),
            Source: source,
            ReceivedAtUtc: receivedAtUtc,
            Headers: headers,
            Body: body);

        var entity = new CapturedRequestEntity
        {
            Id = capturedRequest.Id,
            Source = capturedRequest.Source,
            ReceivedAtUtc = DateTime.SpecifyKind(capturedRequest.ReceivedAtUtc.UtcDateTime, DateTimeKind.Utc),
            HeadersJson = JsonSerializer.Serialize(capturedRequest.Headers),
            Body = capturedRequest.Body
        };

        _dbContext.CapturedRequests.Add(entity);
        _dbContext.SaveChanges();

        return capturedRequest;
    }

    public IReadOnlyList<CapturedRequest> GetAllNewestFirst(string? source = null, string? query = null)
    {
        var requests = _dbContext.CapturedRequests
            .AsNoTracking()
            .OrderByDescending(request => request.ReceivedAtUtc)
            .Select(request => new CapturedRequest(
                Id: request.Id,
                Source: request.Source,
                ReceivedAtUtc: new DateTimeOffset(DateTime.SpecifyKind(request.ReceivedAtUtc, DateTimeKind.Utc), TimeSpan.Zero),
                Headers: DeserializeHeaders(request.HeadersJson),
                Body: request.Body))
            .ToList();

        return WebhookRequestFilter.Apply(requests, source, query);
    }

    public bool TryGetById(string id, out CapturedRequest? capturedRequest)
    {
        var entity = _dbContext.CapturedRequests
            .AsNoTracking()
            .SingleOrDefault(request => request.Id == id);

        if (entity is null)
        {
            capturedRequest = null;
            return false;
        }

        var headers = DeserializeHeaders(entity.HeadersJson);

        capturedRequest = new CapturedRequest(
            Id: entity.Id,
            Source: entity.Source,
            ReceivedAtUtc: new DateTimeOffset(DateTime.SpecifyKind(entity.ReceivedAtUtc, DateTimeKind.Utc), TimeSpan.Zero),
            Headers: headers,
            Body: entity.Body);

        return true;
    }
}
