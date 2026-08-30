namespace HookLens.Models;

public sealed record StatusResponse(
    string Name,
    string Version,
    string Environment,
    string Status
);
