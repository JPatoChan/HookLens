using HookLens.Models;

namespace HookLens.Services;

public sealed class ReplayRequest
{
    public string? TargetUrl { get; init; }
}

public sealed record ReplayResult(
    string TargetUrl,
    int? StatusCode,
    bool Succeeded,
    DateTimeOffset TimestampUtc,
    string? Error);

public interface IWebhookReplayService
{
    Task<ReplayResult> ReplayAsync(CapturedRequest capturedRequest, Uri targetUri, CancellationToken cancellationToken = default);
}
