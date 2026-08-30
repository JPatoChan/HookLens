namespace HookLens.Models;

public sealed record CapturedRequest(
    string Id,
    string Source,
    DateTimeOffset ReceivedAtUtc,
    IReadOnlyDictionary<string, string[]> Headers,
    string Body
);
