using HookLens.Services;

namespace HookLens.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/capture/{source}", async (string source, HttpRequest request, IWebhookCaptureStore store) =>
        {
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();

            var headers = request.Headers
                .ToDictionary(
                    header => header.Key,
                    header => header.Value
                        .Select(value => value ?? string.Empty)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            var capturedRequest = store.Capture(source, headers, body, DateTimeOffset.UtcNow);
            return Results.Created($"/requests/{capturedRequest.Id}", capturedRequest);
        });

        app.MapGet("/requests", (IWebhookCaptureStore store) => Results.Ok(store.GetAllNewestFirst()));

        app.MapGet("/requests/{id}", (string id, IWebhookCaptureStore store) =>
        {
            if (!store.TryGetById(id, out var capturedRequest) || capturedRequest is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(capturedRequest);
        });
    }
}
