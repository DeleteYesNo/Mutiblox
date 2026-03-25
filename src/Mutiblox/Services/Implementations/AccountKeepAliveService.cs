using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

/// <summary>
/// Periodically checks account cookie health and automatically refreshes them when needed.
///
/// Strategy (inspired by Roblox Account Manager):
/// - Checks every 6 hours (conservative to avoid detection)
/// - Only acts on cookies that haven't been verified in 3+ days (stale threshold)
/// - If a cookie fails validation, attempts auto-import from browser
/// - Throttles API calls with 5-second delays between accounts
/// </summary>
public sealed class AccountKeepAliveService : IAccountKeepAliveService, IDisposable
{
    /// <summary>How often the background timer fires.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>Only validate cookies that haven't been verified in this long.</summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(3);

    /// <summary>Delay between API calls to avoid rate limiting / detection.</summary>
    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromSeconds(5);

    private readonly IAccountService _accountService;
    private readonly IRobloxApiService _apiService;
    private readonly IBrowserCookieService _browserCookieService;

    private Timer? _timer;
    private CancellationTokenSource? _cts;
    private int _running; // 0 = idle, 1 = executing cycle

    public bool IsRunning => _timer != null;
    public event Action<string>? StatusChanged;

    public AccountKeepAliveService(
        IAccountService accountService,
        IRobloxApiService apiService,
        IBrowserCookieService browserCookieService)
    {
        _accountService = accountService;
        _apiService = apiService;
        _browserCookieService = browserCookieService;
    }

    public void Start()
    {
        if (_timer != null) return;

        _cts = new CancellationTokenSource();
        // First check after 5 minutes (let app settle), then every 6 hours
        _timer = new Timer(_ => _ = RunCycleAsync(), null, TimeSpan.FromMinutes(5), CheckInterval);
        StatusChanged?.Invoke("Cookie 自動保活已啟用（每 6 小時檢查）");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _timer?.Dispose();
        _timer = null;
        _cts?.Dispose();
        _cts = null;
        StatusChanged?.Invoke("Cookie 自動保活已停用");
    }

    private async Task RunCycleAsync()
    {
        // Prevent concurrent cycles
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        try
        {
            var ct = _cts?.Token ?? default;
            if (ct.IsCancellationRequested) return;

            var accounts = _accountService.Accounts;
            if (accounts.Count == 0) return;

            int valid = 0, refreshed = 0, failed = 0, skipped = 0;

            foreach (var account in accounts.ToList())
            {
                if (ct.IsCancellationRequested) break;

                // Only check accounts whose cookies are stale (not recently verified)
                if (account.LastVerified.HasValue &&
                    (DateTimeOffset.UtcNow - account.LastVerified.Value) < StaleThreshold)
                {
                    skipped++;
                    continue;
                }

                var result = await CheckAccountAsync(account, ct);
                switch (result)
                {
                    case KeepAliveResult.Valid: valid++; break;
                    case KeepAliveResult.Refreshed: refreshed++; break;
                    case KeepAliveResult.Failed: failed++; break;
                }

                // Conservative throttle between API calls
                if (!ct.IsCancellationRequested)
                    await Task.Delay(ThrottleDelay, ct).ConfigureAwait(false);
            }

            // Only emit status when something interesting happened
            if (valid + refreshed + failed > 0)
            {
                var parts = new List<string>();
                if (valid > 0) parts.Add($"{valid} 有效");
                if (refreshed > 0) parts.Add($"{refreshed} 已自動更新");
                if (failed > 0) parts.Add($"{failed} 已過期");
                if (skipped > 0) parts.Add($"{skipped} 略過");

                StatusChanged?.Invoke($"Cookie 檢查完成: {string.Join(", ", parts)}");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down — expected
        }
        catch (Exception)
        {
            // Don't let background failures crash the app
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task<KeepAliveResult> CheckAccountAsync(RobloxAccount account, CancellationToken ct)
    {
        try
        {
            var cookie = _accountService.DecryptCookie(account);

            // Validate current cookie via CSRF token check (lightweight, same as RAM approach)
            var csrfResult = await _apiService.GetCsrfTokenAsync(cookie, ct);
            if (csrfResult.IsSuccess)
            {
                // Cookie is still valid — update verification timestamp
                account.LastVerified = DateTimeOffset.UtcNow;
                account.IsCookieExpired = false;
                await _accountService.SaveAsync();
                return KeepAliveResult.Valid;
            }

            // Cookie expired — try auto-import from browser
            return await TryBrowserRefreshAsync(account, ct);
        }
        catch
        {
            return KeepAliveResult.Failed;
        }
    }

    /// <summary>
    /// Attempts to find a fresh cookie for the same account from installed browsers.
    /// This is safer than calling Roblox's re-authenticate endpoint, as it doesn't
    /// invalidate other sessions.
    /// </summary>
    private async Task<KeepAliveResult> TryBrowserRefreshAsync(RobloxAccount account, CancellationToken ct)
    {
        try
        {
            var browserResult = _browserCookieService.GetRobloxCookies();
            if (!browserResult.IsSuccess)
                return MarkExpired(account);

            foreach (var entry in browserResult.Value!)
            {
                if (ct.IsCancellationRequested) break;

                // Validate this browser cookie
                var userResult = await _apiService.GetUserInfoAsync(entry.CookieValue, ct);
                if (!userResult.IsSuccess) continue;

                // Check if it belongs to the same account
                if (userResult.Value.UserId != account.UserId) continue;

                // Match! Update the stored cookie
                await _accountService.UpdateCookieAsync(account, entry.CookieValue);
                account.Username = userResult.Value.Username;
                account.DisplayName = userResult.Value.DisplayName;
                account.LastVerified = DateTimeOffset.UtcNow;
                account.IsCookieExpired = false;
                await _accountService.SaveAsync();

                StatusChanged?.Invoke($"{account.Username} 的 Cookie 已從 {entry.BrowserName} 自動更新");
                return KeepAliveResult.Refreshed;
            }
        }
        catch
        {
            // Browser extraction can fail for many reasons — not critical
        }

        return MarkExpired(account);
    }

    private static KeepAliveResult MarkExpired(RobloxAccount account)
    {
        account.IsCookieExpired = true;
        return KeepAliveResult.Failed;
    }

    public void Dispose()
    {
        Stop();
    }

    private enum KeepAliveResult
    {
        Valid,
        Refreshed,
        Failed,
    }
}
