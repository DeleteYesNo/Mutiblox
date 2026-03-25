using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Mutiblox.Core;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class ServerJoinService : IServerJoinService
{
    private readonly IAccountService _accountService;
    private readonly IRobloxApiService _apiService;
    private readonly IMutexService _mutexService;
    private readonly IClientSettingsService _settingsService;
    private readonly IRobloxLocator _locator;
    private readonly IInstanceManager _instanceManager;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Brief delay after protocol handler exits before scanning for the new process.
    /// Auth ticket is embedded in the URL so no cookie I/O is needed — just give
    /// the OS a moment to spawn RobloxPlayerBeta.
    /// </summary>
    private const int PostLaunchDelayMs = 2000;

    public ServerJoinService(
        IAccountService accountService,
        IRobloxApiService apiService,
        IMutexService mutexService,
        IClientSettingsService settingsService,
        IRobloxLocator locator,
        IInstanceManager instanceManager,
        IHttpClientFactory httpClientFactory)
    {
        _accountService = accountService;
        _apiService = apiService;
        _mutexService = mutexService;
        _settingsService = settingsService;
        _locator = locator;
        _instanceManager = instanceManager;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Result<int>> LaunchAsync(RobloxAccount account, LaunchConfiguration config,
        CancellationToken ct = default)
    {
        // 1. Decrypt cookie & get auth ticket (per-account authentication)
        string cookie;
        try
        {
            cookie = _accountService.DecryptCookie(account);
        }
        catch (Exception ex)
        {
            return Result<int>.Failure($"Cookie 解密失敗: {ex.Message}");
        }

        var ticketResult = await _apiService.GetAuthTicketAsync(cookie, ct);
        if (!ticketResult.IsSuccess)
            return Result<int>.Failure($"取得驗證票證失敗: {ticketResult.Error}");

        // Handle cookie rotation — Roblox may issue a new cookie in the response
        if (ticketResult.Value!.NewCookie is { } newCookie)
        {
            await _accountService.UpdateCookieAsync(account, newCookie);
        }

        // 2. Ensure multi-instance is enabled (mutex is held permanently)
        if (!_mutexService.IsMultiInstanceEnabled)
        {
            var mutexResult = await _mutexService.EnableMultiInstanceAsync(ct);
            if (!mutexResult.IsSuccess)
                return Result<int>.Failure($"多開未啟用: {mutexResult.Error}");
        }

        // 3. Apply graphics preset
        if (config.GraphicsPreset != GraphicsPreset.Default)
        {
            var pathResult = _locator.FindRobloxPath();
            if (pathResult.IsSuccess)
            {
                _settingsService.ApplyPreset(config.GraphicsPreset, pathResult.Value!);
                if (config.FpsLimit.HasValue)
                    _settingsService.SetFpsLimit(config.FpsLimit.Value, pathResult.Value!);
            }
        }

        // 4. Build protocol URI.
        var uri = BuildProtocolUri(ticketResult.Value!.Ticket, config);

        try
        {
            // Snapshot existing Roblox PIDs before launch
            var existingPids = new HashSet<int>();
            foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                existingPids.Add(p.Id);
                p.Dispose();
            }

            // Launch via protocol handler.
            // Authentication is handled entirely by the auth ticket embedded in the URI.
            // RobloxCookies.dat is locked at app startup (App.xaml.cs) to prevent
            // cross-contamination — we do NOT inject cookies into the file.
            var launcher = Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });

            if (launcher != null)
            {
                await Task.Run(() => launcher.WaitForExit(10_000)).ConfigureAwait(false);
                launcher.Dispose();
            }

            // Wait for Roblox to fully initialize with the auth ticket.
            await Task.Delay(PostLaunchDelayMs, ct);

            // Wait for a NEW Roblox process to appear
            var newProcess = await WaitForNewProcessAsync(existingPids, TimeSpan.FromSeconds(30), ct);

            if (newProcess == null)
                return Result<int>.Failure("Roblox 進程啟動後未偵測到");

            // Track immediately — don't block waiting for the window.
            // The instance is already running; window appearance is just UI polish.
            var processId = newProcess.Id;
            var instance = new RobloxInstance
            {
                ProcessId = processId,
                Account = account,
                PlaceId = config.PlaceId,
                JobId = config.JobId,
                Status = InstanceStatus.Running,
            };

            _instanceManager.TrackInstance(instance);
            newProcess.Dispose();

            return Result<int>.Success(processId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<int>.Failure($"啟動失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// Polls for a new RobloxPlayerBeta process that is NOT in the given PID set.
    /// </summary>
    private static async Task<Process?> WaitForNewProcessAsync(
        HashSet<int> existingPids, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(1000, ct);

            foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                if (!existingPids.Contains(p.Id))
                    return p; // Caller is responsible for disposing

                p.Dispose();
            }
        }
        return null;
    }

    /// <summary>
    /// Waits until the process has a visible main window (indicating full initialization).
    /// </summary>
    private static async Task WaitForWindowAsync(
        Process process, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                    return;
            }
            catch { return; } // Process exited
            await Task.Delay(500, ct);
        }
    }

    public async Task<Result<List<ServerInfo>>> GetServersAsync(long placeId, CancellationToken ct = default)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("Roblox");
            var url = $"{Constants.RobloxGamesUrl}/v1/games/{placeId}/servers/0?sortOrder=Asc&limit=100";
            using var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return Result<List<ServerInfo>>.Failure($"取得伺服器列表失敗: {response.StatusCode}");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var servers = new List<ServerInfo>();

            if (json.TryGetProperty("data", out var data))
            {
                foreach (var server in data.EnumerateArray())
                {
                    servers.Add(new ServerInfo
                    {
                        JobId = server.GetProperty("id").GetString() ?? "",
                        PlayerCount = server.GetProperty("playing").GetInt32(),
                        MaxPlayers = server.GetProperty("maxPlayers").GetInt32(),
                        Ping = server.TryGetProperty("ping", out var ping) ? ping.GetInt32() : 0,
                        Fps = server.TryGetProperty("fps", out var fps) ? fps.GetDouble() : 0,
                    });
                }
            }

            return Result<List<ServerInfo>>.Success(servers);
        }
        catch (Exception ex)
        {
            return Result<List<ServerInfo>>.Failure($"伺服器查詢失敗: {ex.Message}");
        }
    }

    private static string BuildProtocolUri(string authTicket, LaunchConfiguration config)
    {
        var launchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var browserTrackerId = Random.Shared.NextInt64(100000000000, 999999999999);
        var placeLauncherUrl = BuildPlaceLauncherUrl(config);
        var encodedUrl = HttpUtility.UrlEncode(placeLauncherUrl);

        return $"roblox-player:1+launchmode:play" +
               $"+gameinfo:{authTicket}" +
               $"+launchtime:{launchTime}" +
               $"+placelauncherurl:{encodedUrl}" +
               $"+browsertrackerid:{browserTrackerId}" +
               $"+robloxLocale:en_us+gameLocale:en_us";
    }

    public async Task<Result<PrivateServerInfo>> ResolveShareLinkAsync(
        string shareLink, string cookie, CancellationToken ct = default)
    {
        try
        {
            // Format 1: /games/PLACEID/NAME?privateServerLinkCode=xxx
            // Can be parsed directly without any API call
            var directResult = ParsePrivateServerFromUrl(shareLink);
            if (directResult != null)
                return Result<PrivateServerInfo>.Success(directResult);

            // Extract the share code
            var linkCode = ParseShareLinkCode(shareLink);
            if (linkCode == null)
                return Result<PrivateServerInfo>.Failure(
                    "無法解析連結。支援的格式:\n" +
                    "• https://www.roblox.com/share?code=xxx&type=Server\n" +
                    "• https://www.roblox.com/games/ID/Name?privateServerLinkCode=xxx");

            // Format 2: /share?code=xxx — need to follow redirect to get the real URL
            // Use a handler that doesn't auto-follow redirects so we can capture the Location header
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            handler.CookieContainer.Add(new System.Net.Cookie(".ROBLOSECURITY", cookie, "/", ".roblox.com"));
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Roblox/WinInet");

            var shareUrl = $"https://www.roblox.com/share?code={linkCode}&type=Server";
            using var response = await client.GetAsync(shareUrl, ct);

            // Roblox redirects share links to the game page with privateServerLinkCode
            if ((int)response.StatusCode is >= 301 and <= 308)
            {
                var location = response.Headers.Location?.ToString();
                if (!string.IsNullOrEmpty(location))
                {
                    // Make absolute URL if relative
                    if (location.StartsWith("/"))
                        location = $"https://www.roblox.com{location}";

                    var parsed = ParsePrivateServerFromUrl(location);
                    if (parsed != null)
                        return Result<PrivateServerInfo>.Success(parsed);
                }
            }

            // Fallback: try the sharelinks API with CSRF token
            var csrfResult = await _apiService.GetCsrfTokenAsync(cookie, ct);
            if (csrfResult.IsSuccess)
            {
                using var apiClient = _httpClientFactory.CreateClient("Roblox");
                apiClient.DefaultRequestHeaders.Add("Cookie", $".ROBLOSECURITY={cookie}");
                apiClient.DefaultRequestHeaders.Add("x-csrf-token", csrfResult.Value);

                var resolveUrl = "https://apis.roblox.com/sharelinks/v1/resolve-link";
                var payload = new { linkId = linkCode, linkType = "Server" };
                using var apiResponse = await apiClient.PostAsJsonAsync(resolveUrl, payload, ct);

                var apiBody = await apiResponse.Content.ReadAsStringAsync(ct);

                if (apiResponse.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(apiBody) && apiBody != "null")
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(apiBody);

                    if (json.ValueKind == JsonValueKind.Object)
                    {
                        // Direct placeId + accessCode response
                        if (json.TryGetProperty("placeId", out var pidProp) && pidProp.ValueKind == JsonValueKind.Number)
                        {
                            var placeId = pidProp.GetInt64();
                            var accessCode = json.TryGetProperty("accessCode", out var ac) ? ac.GetString() ?? "" : linkCode;
                            return Result<PrivateServerInfo>.Success(
                                new PrivateServerInfo(placeId, accessCode, linkCode));
                        }

                        // privateServerInviteData response (from share links)
                        if (json.TryGetProperty("privateServerInviteData", out var psData) &&
                            psData.ValueKind == JsonValueKind.Object)
                        {
                            var universeId = psData.TryGetProperty("universeId", out var uid) ? uid.GetInt64() : 0;
                            var serverLinkCode = psData.TryGetProperty("linkCode", out var lc) ? lc.GetString() ?? "" : linkCode;

                            if (universeId > 0)
                            {
                                // Convert universeId to rootPlaceId
                                var placeId = await GetRootPlaceIdAsync(universeId, ct);
                                if (placeId > 0)
                                    return Result<PrivateServerInfo>.Success(
                                        new PrivateServerInfo(placeId, serverLinkCode, linkCode));

                                return Result<PrivateServerInfo>.Failure(
                                    $"無法從 Universe {universeId} 取得 Place ID");
                            }
                        }

                        // experienceInviteData response
                        if (json.TryGetProperty("experienceInviteData", out var inviteData) &&
                            inviteData.ValueKind == JsonValueKind.Object)
                        {
                            var placeId = inviteData.TryGetProperty("placeId", out var p) ? p.GetInt64() : 0;
                            var instanceId = inviteData.TryGetProperty("instanceId", out var iid) ? iid.GetString() : null;

                            if (placeId > 0)
                                return Result<PrivateServerInfo>.Success(
                                    new PrivateServerInfo(placeId, instanceId ?? linkCode, linkCode));
                        }
                    }

                    return Result<PrivateServerInfo>.Failure(
                        $"API 回應格式不符預期: {apiBody[..Math.Min(300, apiBody.Length)]}");
                }

                return Result<PrivateServerInfo>.Failure(
                    $"API 回應 {(int)apiResponse.StatusCode}: {apiBody[..Math.Min(300, apiBody.Length)]}");
            }

            return Result<PrivateServerInfo>.Failure(
                "無法解析私人伺服器連結。請確認連結正確且帳號有存取權限。");
        }
        catch (Exception ex)
        {
            return Result<PrivateServerInfo>.Failure($"解析連結失敗: {ex.Message}");
        }
    }

    private static string? ParseShareLinkCode(string input)
    {
        input = input.Trim();

        // Try parsing as URL
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            var query = HttpUtility.ParseQueryString(uri.Query);

            // Format: /share?code=xxx
            var code = query["code"];
            if (!string.IsNullOrEmpty(code))
                return code;

            // Format: /games/ID/Name?privateServerLinkCode=xxx
            var linkCode = query["privateServerLinkCode"];
            if (!string.IsNullOrEmpty(linkCode))
                return linkCode;
        }

        // If it's just a raw code string (no URL), accept it directly
        if (!input.Contains('/') && !input.Contains('?') && input.Length > 10)
            return input;

        return null;
    }

    private static PrivateServerInfo? ParsePrivateServerFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        var query = HttpUtility.ParseQueryString(uri.Query);
        var linkCode = query["privateServerLinkCode"];
        if (string.IsNullOrEmpty(linkCode))
            return null;

        // Extract placeId from path: /games/PLACEID/...
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0] == "games" && long.TryParse(segments[1], out var placeId))
            return new PrivateServerInfo(placeId, linkCode, linkCode);

        return null;
    }

    private async Task<long> GetRootPlaceIdAsync(long universeId, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("Roblox");
            var url = $"{Constants.RobloxGamesUrl}/v1/games?universeIds={universeId}";
            using var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode) return 0;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                if (data[0].TryGetProperty("rootPlaceId", out var rpid))
                    return rpid.GetInt64();
            }
        }
        catch { }
        return 0;
    }

    private static string BuildPlaceLauncherUrl(LaunchConfiguration config)
    {
        var baseUrl = "https://assetgame.roblox.com/game/PlaceLauncher.ashx";

        return config.LaunchMode switch
        {
            LaunchMode.JoinServer when !string.IsNullOrEmpty(config.JobId) =>
                $"{baseUrl}?request=RequestGameJob&browserTrackerId=0" +
                $"&placeId={config.PlaceId}" +
                $"&gameId=&gameJoinAttemptId={Guid.NewGuid()}" +
                $"&serverJobId={config.JobId}",

            LaunchMode.VipServer when !string.IsNullOrEmpty(config.AccessCode) =>
                $"{baseUrl}?request=RequestPrivateGame&browserTrackerId=0" +
                $"&placeId={config.PlaceId}" +
                $"&accessCode={config.AccessCode}",

            LaunchMode.FollowPlayer when config.FollowUserId.HasValue =>
                $"{baseUrl}?request=RequestFollowUser&browserTrackerId=0" +
                $"&placeId={config.PlaceId}" +
                $"&followUserId={config.FollowUserId.Value}",

            _ =>
                $"{baseUrl}?request=RequestGame&browserTrackerId=0" +
                $"&placeId={config.PlaceId}"
        };
    }
}
