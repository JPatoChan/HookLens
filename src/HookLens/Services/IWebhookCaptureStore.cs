using HookLens.Models;

namespace HookLens.Services;

public interface IWebhookCaptureStore
{
    CapturedRequest Capture(string source, IReadOnlyDictionary<string, string[]> headers, string body, DateTimeOffset receivedAtUtc);
    IReadOnlyList<CapturedRequest> GetAllNewestFirst(string? source = null, string? query = null);
    bool TryGetById(string id, out CapturedRequest? capturedRequest);
}
