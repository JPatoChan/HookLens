using HookLens.Models;

namespace HookLens.Services;

public interface IWebhookCaptureStore
{
    CapturedRequest Capture(string source, IReadOnlyDictionary<string, string[]> headers, string body, DateTimeOffset receivedAtUtc);
    IReadOnlyList<CapturedRequest> GetAllNewestFirst();
    bool TryGetById(string id, out CapturedRequest? capturedRequest);
}
