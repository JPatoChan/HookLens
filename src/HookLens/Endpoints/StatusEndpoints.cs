using HookLens.Models;

namespace HookLens.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/status", () => Results.Ok(new StatusResponse(
            Name: "HookLens",
            Version: "0.1.0",
            Environment: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            Status: "ready"
        )));
    }
}
