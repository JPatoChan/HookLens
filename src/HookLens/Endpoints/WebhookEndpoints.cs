using HookLens.Services;

namespace HookLens.Endpoints;

public static class WebhookEndpoints
{
    private static bool TryParseTargetUrl(string? targetUrlText, out Uri? targetUri)
    {
        targetUri = null;

        if (string.IsNullOrWhiteSpace(targetUrlText))
        {
            return false;
        }

        return Uri.TryCreate(targetUrlText, UriKind.Absolute, out targetUri)
            && (targetUri.Scheme == Uri.UriSchemeHttp || targetUri.Scheme == Uri.UriSchemeHttps);
    }

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

        app.MapPost("/requests/{id}/replay", async (string id, ReplayRequest request, IWebhookCaptureStore store, IWebhookReplayService replayService, CancellationToken cancellationToken) =>
        {
            if (!store.TryGetById(id, out var capturedRequest) || capturedRequest is null)
            {
                return Results.NotFound();
            }

            if (!TryParseTargetUrl(request.TargetUrl, out var targetUri) || targetUri is null)
            {
                return Results.BadRequest(new
                {
                    error = "A valid absolute http or https targetUrl is required.",
                    targetUrl = request.TargetUrl
                });
            }

            var result = await replayService.ReplayAsync(capturedRequest, targetUri, cancellationToken);
            return Results.Ok(new
            {
                targetUrl = result.TargetUrl,
                statusCode = result.StatusCode,
                succeeded = result.Succeeded,
                timestampUtc = result.TimestampUtc,
                error = result.Error
            });
        });
    }
}
