using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Mutiblox.Core;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class RobloxApiService : IRobloxApiService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public RobloxApiService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Result<string>> GetCsrfTokenAsync(string cookie, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(cookie);
            using var response = await SendWithRetryAsync(client,
                () => new HttpRequestMessage(HttpMethod.Post, $"{Constants.RobloxAuthUrl}/v2/logout"), ct);

            if (response.StatusCode == HttpStatusCode.Forbidden &&
                response.Headers.TryGetValues("x-csrf-token", out var values))
            {
                var token = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(token))
                    return Result<string>.Success(token);
            }

            return Result<string>.Failure("Failed to obtain CSRF token");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"CSRF token request failed: {ex.Message}");
        }
    }

    public async Task<Result<AuthTicketResult>> GetAuthTicketAsync(string cookie, CancellationToken ct = default)
    {
        try
        {
            string? rotatedCookie = null;

            var csrfResult = await GetCsrfTokenAsync(cookie, ct);
            if (!csrfResult.IsSuccess)
                return Result<AuthTicketResult>.Failure(csrfResult.Error!);

            using var client = CreateClient(cookie);
            var csrfToken = csrfResult.Value;
            using var response = await SendWithRetryAsync(client, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post,
                    $"{Constants.RobloxAuthUrl}/v1/authentication-ticket/");
                req.Headers.Add("x-csrf-token", csrfToken);
                req.Headers.Add("Referer", "https://www.roblox.com");
                req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                return req;
            }, ct);

            // Capture cookie rotation from Set-Cookie header
            rotatedCookie = ExtractRotatedCookie(response);

            if (response.IsSuccessStatusCode &&
                response.Headers.TryGetValues("rbx-authentication-ticket", out var values))
            {
                var ticket = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(ticket))
                    return Result<AuthTicketResult>.Success(
                        new AuthTicketResult(ticket, rotatedCookie));
            }

            return Result<AuthTicketResult>.Failure($"Failed to get auth ticket. Status: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return Result<AuthTicketResult>.Failure($"Auth ticket request failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts a rotated .ROBLOSECURITY cookie from the Set-Cookie response header.
    /// </summary>
    private static string? ExtractRotatedCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieValues))
            return null;

        foreach (var header in setCookieValues)
        {
            // Format: .ROBLOSECURITY=_|WARNING:...; path=/; ...
            if (!header.StartsWith(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = header.AsSpan()[".ROBLOSECURITY=".Length..];
            var semicolonIndex = value.IndexOf(';');
            if (semicolonIndex > 0)
                value = value[..semicolonIndex];

            var cookie = value.ToString().Trim();
            if (!string.IsNullOrEmpty(cookie))
                return cookie;
        }

        return null;
    }

    public async Task<Result<(long UserId, string Username, string DisplayName)>> GetUserInfoAsync(
        string cookie, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(cookie);
            using var response = await SendWithRetryAsync(client,
                () => new HttpRequestMessage(HttpMethod.Get,
                    $"{Constants.RobloxUsersUrl}/v1/users/authenticated"), ct);

            if (!response.IsSuccessStatusCode)
                return Result<(long, string, string)>.Failure($"Failed to get user info. Status: {response.StatusCode}");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var userId = json.GetProperty("id").GetInt64();
            var username = json.GetProperty("name").GetString() ?? "Unknown";
            var displayName = json.GetProperty("displayName").GetString() ?? username;

            return Result<(long, string, string)>.Success((userId, username, displayName));
        }
        catch (Exception ex)
        {
            return Result<(long, string, string)>.Failure($"User info request failed: {ex.Message}");
        }
    }

    public async Task<Result<string>> GetThumbnailUrlAsync(long userId, CancellationToken ct = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("Roblox");
            var url = $"{Constants.RobloxThumbnailsUrl}/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png";
            using var response = await SendWithRetryAsync(client,
                () => new HttpRequestMessage(HttpMethod.Get, url), ct);

            if (!response.IsSuccessStatusCode)
                return Result<string>.Failure("Failed to get thumbnail");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var data = json.GetProperty("data");
            if (data.GetArrayLength() > 0)
            {
                var imageUrl = data[0].GetProperty("imageUrl").GetString();
                if (!string.IsNullOrEmpty(imageUrl))
                    return Result<string>.Success(imageUrl);
            }

            return Result<string>.Failure("No thumbnail available");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Thumbnail request failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends an HTTP request with automatic retry on 429 (Too Many Requests).
    /// Uses exponential backoff, respecting Retry-After header when present.
    /// </summary>
    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client, Func<HttpRequestMessage> requestFactory,
        CancellationToken ct, int maxRetries = 3)
    {
        for (int attempt = 0; ; attempt++)
        {
            using var request = requestFactory();
            var response = await client.SendAsync(request, ct);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= maxRetries)
                return response;

            // Exponential backoff: 2s, 4s, 8s
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            if (response.Headers.RetryAfter?.Delta is { } retryDelta)
                delay = retryDelta;

            response.Dispose();
            await Task.Delay(delay, ct);
        }
    }

    private HttpClient CreateClient(string cookie)
    {
        var client = _httpClientFactory.CreateClient("Roblox");
        client.DefaultRequestHeaders.Add("Cookie", $".ROBLOSECURITY={cookie}");
        return client;
    }
}
