using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.ViewModels;

public partial class GameBrowserViewModel : ObservableObject
{
    private readonly IAccountService _accountService;
    private readonly IMutexService _mutexService;
    private readonly IClientSettingsService _settingsService;
    private readonly IRobloxLocator _locator;
    private readonly IInstanceManager _instanceManager;

    [ObservableProperty]
    private RobloxAccount? _selectedAccount;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private GraphicsPreset _selectedPreset = GraphicsPreset.Default;

    [ObservableProperty]
    private int _fpsLimit = 60;

    public ObservableCollection<RobloxAccount> Accounts { get; } = [];

    public Array GraphicsPresets => Enum.GetValues<GraphicsPreset>();

    /// <summary>
    /// Raised when WebView2 needs to be initialized with a cookie.
    /// View handles this by setting the cookie in WebView2.
    /// </summary>
    public event Action<string>? CookieReady;

    public GameBrowserViewModel(
        IAccountService accountService,
        IMutexService mutexService,
        IClientSettingsService settingsService,
        IRobloxLocator locator,
        IInstanceManager instanceManager)
    {
        _accountService = accountService;
        _mutexService = mutexService;
        _settingsService = settingsService;
        _locator = locator;
        _instanceManager = instanceManager;
    }

    [RelayCommand]
    private void RefreshAccounts()
    {
        Accounts.Clear();
        foreach (var account in _accountService.Accounts)
            Accounts.Add(account);

        if (SelectedAccount == null && Accounts.Count > 0)
            SelectedAccount = Accounts[0];
    }

    [RelayCommand]
    private void SwitchAccount()
    {
        if (SelectedAccount == null)
        {
            StatusMessage = "請先選擇帳號";
            return;
        }

        try
        {
            var cookie = _accountService.DecryptCookie(SelectedAccount);
            CookieReady?.Invoke(cookie);
            StatusMessage = $"已切換至 {SelectedAccount.Label}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cookie 解密失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// Called by the View when a roblox-player: protocol URI is intercepted.
    /// Clears singleton handles, applies settings, then launches the exe directly.
    /// </summary>
    public async Task HandleProtocolLaunchAsync(string protocolUri)
    {
        if (SelectedAccount == null)
        {
            StatusMessage = "請先選擇帳號";
            return;
        }

        IsLaunching = true;
        StatusMessage = "正在啟動...";

        try
        {
            // Ensure multi-instance is enabled (mutex held permanently)
            if (!_mutexService.IsMultiInstanceEnabled)
                await _mutexService.EnableMultiInstanceAsync();

            // Apply graphics preset
            if (SelectedPreset != GraphicsPreset.Default)
            {
                var pathResult = _locator.FindRobloxPath();
                if (pathResult.IsSuccess)
                {
                    _settingsService.ApplyPreset(SelectedPreset, pathResult.Value!);
                    if (FpsLimit > 0)
                        _settingsService.SetFpsLimit(FpsLimit, pathResult.Value!);
                }
            }

            // Snapshot existing PIDs before launch
            var existingPids = new HashSet<int>();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                existingPids.Add(p.Id);
                p.Dispose();
            }

            // Launch via protocol handler — the launcher spawns a fresh
            // RobloxPlayerBeta.exe each time, avoiding Chromium ProcessSingleton conflicts.
            var launcher = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = protocolUri,
                UseShellExecute = true,
            });
            if (launcher != null)
            {
                await Task.Run(() => launcher.WaitForExit(10_000)).ConfigureAwait(false);
                launcher.Dispose();
            }

            // Wait for a NEW process to appear (by PID snapshot diff)
            System.Diagnostics.Process? newProc = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(1000);
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta"))
                {
                    if (!existingPids.Contains(p.Id))
                    {
                        newProc = p;
                        goto found;
                    }
                    p.Dispose();
                }
            }
            found:

            if (newProc != null)
            {
                var instance = new RobloxInstance
                {
                    ProcessId = newProc.Id,
                    Account = SelectedAccount,
                    Status = InstanceStatus.Running,
                };
                _instanceManager.TrackInstance(instance);
                StatusMessage = $"已啟動 {SelectedAccount.Label} (PID: {newProc.Id})";
                newProc.Dispose();
            }
            else
            {
                StatusMessage = "已發送啟動請求";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"啟動失敗: {ex.Message}";
        }

        IsLaunching = false;
    }
}
