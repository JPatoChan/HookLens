using HookLens.Models;

namespace HookLens.Services;

public sealed class InMemoryWebhookCaptureStore : IWebhookCaptureStore
{
    private readonly List<CapturedRequest> _requests = [];
    private readonly object _lock = new();

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
            Headers: new Dictionary<string, string[]>(headers, StringComparer.OrdinalIgnoreCase),
            Body: body);

        lock (_lock)
        {
            _requests.Add(capturedRequest);
        }

        return capturedRequest;
    }

    public IReadOnlyList<CapturedRequest> GetAllNewestFirst()
    {
        lock (_lock)
        {
            return _requests
                .OrderByDescending(request => request.ReceivedAtUtc)
                .ToList();
        }
    }

    public bool TryGetById(string id, out CapturedRequest? capturedRequest)
    {
        lock (_lock)
        {
            capturedRequest = _requests.FirstOrDefault(request => request.Id == id);
            return capturedRequest is not null;
        }
    }
}
