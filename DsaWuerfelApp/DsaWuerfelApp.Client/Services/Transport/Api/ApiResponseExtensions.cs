using System.Text.Json;

namespace DsaWuerfelApp.Client.Services;

internal static class ApiResponseExtensions
{
    public static async Task EnsureApiSuccessAsync(
        this HttpResponseMessage response,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var apiError = await TryReadApiErrorAsync(response, cancellationToken);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiError) ? fallbackMessage : apiError);
    }

    private static async Task<string?> TryReadApiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var apiError =
                JsonSerializer.Deserialize<ApiError>(content, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return string.IsNullOrWhiteSpace(apiError?.Error) ? content : apiError.Error;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ApiError(string? Error);
}