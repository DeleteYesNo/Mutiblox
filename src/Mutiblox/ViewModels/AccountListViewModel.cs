using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.ViewModels;

public partial class AccountListViewModel : ObservableObject
{
    private readonly IAccountService _accountService;
    private readonly IBrowserCookieService _browserCookieService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private RobloxAccount? _selectedAccount;

    public ObservableCollection<RobloxAccount> Accounts { get; } = [];

    /// <summary>
    /// Raised when a browser login dialog should be shown.
    /// The View handles this by opening BrowserLoginDialog.
    /// </summary>
    public event Func<Task<string?>>? BrowserLoginRequested;

    public AccountListViewModel(IAccountService accountService, IBrowserCookieService browserCookieService)
    {
        _accountService = accountService;
        _browserCookieService = browserCookieService;
    }

    [RelayCommand]
    private async Task LoadAccountsAsync()
    {
        IsLoading = true;
        StatusMessage = null;

        try
        {
            await _accountService.LoadAsync();
            Accounts.Clear();
            foreach (var account in _accountService.Accounts)
                Accounts.Add(account);

            StatusMessage = $"已載入 {Accounts.Count} 個帳號";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入失敗: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddAccountAsync(string cookie)
    {
        if (string.IsNullOrWhiteSpace(cookie))
        {
            StatusMessage = "請輸入 Cookie";
            return;
        }

        await AddAccountByCookieAsync(cookie);
    }

    /// <summary>
    /// Opens a WebView2 browser window for the user to log in to Roblox.
    /// Cookie is captured automatically after login.
    /// </summary>
    [RelayCommand]
    private async Task BrowserLoginAsync()
    {
        if (BrowserLoginRequested == null)
            return;

        StatusMessage = "正在開啟登入視窗...";
        var cookie = await BrowserLoginRequested.Invoke();

        if (string.IsNullOrEmpty(cookie))
        {
            StatusMessage = "登入已取消";
            return;
        }

        await AddAccountByCookieAsync(cookie);
    }

    /// <summary>
    /// Re-login a specific account whose cookie has expired.
    /// Opens browser login, then updates the existing account's cookie.
    /// </summary>
    [RelayCommand]
    private async Task ReLoginAccountAsync(RobloxAccount? account)
    {
        if (account == null || BrowserLoginRequested == null) return;

        StatusMessage = $"正在重新登入 {account.Username}...請在瀏覽器中登入此帳號";
        var cookie = await BrowserLoginRequested.Invoke();

        if (string.IsNullOrEmpty(cookie))
        {
            StatusMessage = "重新登入已取消";
            return;
        }

        IsLoading = true;
        StatusMessage = "正在驗證新 Cookie...";

        var result = await _accountService.AddAccountAsync(cookie);
        if (result.IsSuccess)
        {
            var updated = result.Value!;
            updated.IsCookieExpired = false;

            // Replace in UI list
            var idx = -1;
            for (int i = 0; i < Accounts.Count; i++)
            {
                if (Accounts[i].UserId == updated.UserId)
                {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0)
                Accounts[idx] = updated;
            else
                Accounts.Add(updated);

            StatusMessage = $"{updated.Username} 重新登入成功";
        }
        else
        {
            StatusMessage = $"重新登入失敗: {result.Error}";
        }

        IsLoading = false;
    }

    /// <summary>
    /// Imports cookies from installed browsers (Chrome/Edge).
    /// </summary>
    [RelayCommand]
    private async Task ImportFromBrowserAsync()
    {
        IsLoading = true;
        StatusMessage = "正在從瀏覽器匯入...";

        var result = _browserCookieService.GetRobloxCookies();
        if (!result.IsSuccess)
        {
            StatusMessage = result.Error;
            IsLoading = false;
            return;
        }

        int added = 0, processed = 0;
        foreach (var entry in result.Value!)
        {
            StatusMessage = $"正在驗證 {entry.BrowserName} 的帳號...";
            var addResult = await _accountService.AddAccountAsync(entry.CookieValue);
            if (addResult.IsSuccess)
            {
                var account = addResult.Value!;
                var existing = Accounts.FirstOrDefault(a => a.UserId == account.UserId);
                if (existing != null)
                    Accounts.Remove(existing);
                Accounts.Add(account);
                added++;
            }

            processed++;
            // Throttle between API calls to avoid rate limiting
            if (processed < result.Value!.Count)
                await Task.Delay(1500);
        }

        StatusMessage = added > 0
            ? $"已從瀏覽器匯入 {added} 個帳號"
            : "未找到可用的帳號";
        IsLoading = false;
    }

    [RelayCommand]
    private async Task VerifyAccountAsync(RobloxAccount? account)
    {
        if (account == null) return;

        StatusMessage = $"正在驗證 {account.Username}...";
        var result = await _accountService.VerifyAccountAsync(account);

        if (result.IsSuccess)
        {
            account.IsCookieExpired = false;
            StatusMessage = $"{account.Username} 驗證成功";
        }
        else
        {
            account.IsCookieExpired = true;
            StatusMessage = $"{account.Username} 驗證失敗: {result.Error}（可點擊「重新登入」更新 Cookie）";
        }

        // Refresh list to update status display
        RefreshList();
    }

    [RelayCommand]
    private void RemoveAccount(RobloxAccount? account)
    {
        if (account == null) return;

        _accountService.RemoveAccount(account);
        Accounts.Remove(account);
        StatusMessage = $"已移除帳號: {account.Username}";
    }

    [RelayCommand]
    private async Task VerifyAllAsync()
    {
        IsLoading = true;
        StatusMessage = "正在驗證所有帳號...";

        int success = 0, failed = 0;
        var failedNames = new List<string>();

        foreach (var account in Accounts.ToList())
        {
            StatusMessage = $"正在驗證 {account.Username}...({success + failed + 1}/{Accounts.Count})";
            var result = await _accountService.VerifyAccountAsync(account);
            if (result.IsSuccess)
            {
                account.IsCookieExpired = false;
                success++;
            }
            else
            {
                account.IsCookieExpired = true;
                failedNames.Add(account.Username);
                failed++;
            }

            // Throttle between API calls to avoid rate limiting
            if (success + failed < Accounts.Count)
                await Task.Delay(1500);
        }

        RefreshList();

        if (failed > 0)
            StatusMessage = $"驗證完成: {success} 成功, {failed} 失敗（{string.Join(", ", failedNames)}）— 過期帳號可點擊「重新登入」";
        else
            StatusMessage = $"驗證完成: 全部 {success} 個帳號均有效";

        IsLoading = false;
    }

    private async Task AddAccountByCookieAsync(string cookie)
    {
        IsLoading = true;
        StatusMessage = "正在驗證帳號...";

        var result = await _accountService.AddAccountAsync(cookie);
        if (result.IsSuccess)
        {
            var account = result.Value!;
            var existing = Accounts.FirstOrDefault(a => a.UserId == account.UserId);
            if (existing != null)
                Accounts.Remove(existing);

            Accounts.Add(account);
            StatusMessage = $"已新增帳號: {account.Username}";
        }
        else
        {
            StatusMessage = result.Error;
        }

        IsLoading = false;
    }

    /// <summary>
    /// Force refresh the ListView to pick up property changes on items.
    /// </summary>
    private void RefreshList()
    {
        var items = Accounts.ToList();
        Accounts.Clear();
        foreach (var item in items)
            Accounts.Add(item);
    }
}
