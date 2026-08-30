using HookLens.Models;

namespace HookLens.Services;

public static class WebhookRequestFilter
{
    public static IReadOnlyList<CapturedRequest> Apply(IEnumerable<CapturedRequest> requests, string? source, string? query)
    {
        var normalizedSource = NormalizeFilter(source);
        var normalizedQuery = NormalizeFilter(query);

        var filtered = requests.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(normalizedSource))
        {
            filtered = filtered.Where(request => request.Source.Equals(normalizedSource, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            filtered = filtered.Where(request =>
                request.Source.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || request.Id.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || (request.Body is not null && request.Body.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                || request.Headers.Any(header =>
                    header.Key.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    || header.Value.Any(value => value.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))));
        }

        return filtered.ToList();
    }

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
