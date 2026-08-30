using System.Net.Http.Headers;
using HookLens.Models;

namespace HookLens.Services;

public sealed class WebhookReplayService : IWebhookReplayService
{
    private static readonly HashSet<string> UnsafeHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Content-Length",
        "Transfer-Encoding",
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Upgrade",
        "Proxy-Connection"
    };

    private readonly HttpClient _httpClient;

    public WebhookReplayService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ReplayResult> ReplayAsync(CapturedRequest capturedRequest, Uri targetUri, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, targetUri);

        var contentType = capturedRequest.Headers
            .FirstOrDefault(header => header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            .Value?
            .FirstOrDefault();

        request.Content = new StringContent(capturedRequest.Body, System.Text.Encoding.UTF8);

        if (!string.IsNullOrWhiteSpace(contentType) && MediaTypeHeaderValue.TryParse(contentType.Trim(), out var parsedContentType))
        {
            request.Content.Headers.ContentType = parsedContentType;
        }

        foreach (var header in capturedRequest.Headers)
        {
            var headerName = header.Key;
            if (UnsafeHeaders.Contains(headerName) || headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (header.Value is null || header.Value.Length == 0)
            {
                continue;
            }

            foreach (var value in header.Value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                try
                {
                    if (headerName.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))
                    {
                        if (request.Content is not null)
                        {
                            request.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(value);
                        }

                        continue;
                    }

                    if (request.Headers.TryAddWithoutValidation(headerName, value))
                    {
                        continue;
                    }

                    if (request.Content is not null && request.Content.Headers.TryAddWithoutValidation(headerName, value))
                    {
                        continue;
                    }
                }
                catch
                {
                    // Ignore headers that cannot be forwarded safely.
                }
            }
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;

            return new ReplayResult(
                TargetUrl: targetUri.ToString(),
                StatusCode: statusCode,
                Succeeded: response.IsSuccessStatusCode,
                TimestampUtc: DateTimeOffset.UtcNow,
                Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ReplayResult(
                TargetUrl: targetUri.ToString(),
                StatusCode: null,
                Succeeded: false,
                TimestampUtc: DateTimeOffset.UtcNow,
                Error: ex.Message);
        }
    }
}
