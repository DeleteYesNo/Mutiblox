using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.ViewModels;

public partial class LauncherViewModel : ObservableObject
{
    private readonly IAccountService _accountService;
    private readonly IServerJoinService _serverJoinService;
    private readonly IUserSettingsService _settingsService;

    [ObservableProperty]
    private RobloxAccount? _selectedAccount;

    [ObservableProperty]
    private string _placeId = string.Empty;

    [ObservableProperty]
    private string _jobId = string.Empty;

    [ObservableProperty]
    private string _accessCode = string.Empty;

    [ObservableProperty]
    private string _privateServerLink = string.Empty;

    [ObservableProperty]
    private GraphicsPreset _selectedPreset = GraphicsPreset.Default;

    [ObservableProperty]
    private int _fpsLimit = 60;

    [ObservableProperty]
    private LaunchMode _selectedLaunchMode = LaunchMode.Normal;

    [ObservableProperty]
    private bool _isLaunching;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isLoadingServers;

    public ObservableCollection<RobloxAccount> Accounts { get; } = [];
    public ObservableCollection<ServerInfo> Servers { get; } = [];
    public ObservableCollection<RobloxAccount> SelectedAccounts { get; } = [];

    public Array GraphicsPresets => Enum.GetValues<GraphicsPreset>();
    public Array LaunchModes => Enum.GetValues<LaunchMode>();

    public LauncherViewModel(IAccountService accountService, IServerJoinService serverJoinService, IUserSettingsService settingsService)
    {
        _accountService = accountService;
        _serverJoinService = serverJoinService;
        _settingsService = settingsService;

        // Restore saved settings
        var s = _settingsService.Current;
        _placeId = s.PlaceId;
        _jobId = s.JobId;
        _accessCode = s.AccessCode;
        _privateServerLink = s.PrivateServerLink;
        _selectedLaunchMode = s.LaunchMode;
        _selectedPreset = s.GraphicsPreset;
        _fpsLimit = s.FpsLimit;
    }

    [RelayCommand]
    private void RefreshAccounts()
    {
        Accounts.Clear();
        foreach (var account in _accountService.Accounts)
            Accounts.Add(account);
    }

    [RelayCommand]
    private void SelectAll()
    {
        SelectedAccounts.Clear();
        foreach (var account in Accounts)
        {
            account.IsSelected = true;
            SelectedAccounts.Add(account);
        }
        OnPropertyChanged(nameof(SelectedCountDisplay));
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var account in Accounts)
            account.IsSelected = false;
        SelectedAccounts.Clear();
        OnPropertyChanged(nameof(SelectedCountDisplay));
    }

    public void ToggleAccountSelection(RobloxAccount account)
    {
        if (SelectedAccounts.Contains(account))
            SelectedAccounts.Remove(account);
        else
            SelectedAccounts.Add(account);
        OnPropertyChanged(nameof(SelectedCountDisplay));
    }

    public string SelectedCountDisplay => SelectedAccounts.Count > 0
        ? $"已選擇 {SelectedAccounts.Count} 個帳號"
        : "未選擇帳號";

    partial void OnPlaceIdChanged(string value) => SaveSettings();
    partial void OnFpsLimitChanged(int value) => SaveSettings();
    partial void OnSelectedPresetChanged(GraphicsPreset value) => SaveSettings();
    partial void OnAccessCodeChanged(string value) => SaveSettings();
    partial void OnPrivateServerLinkChanged(string value) => SaveSettings();

    partial void OnSelectedLaunchModeChanged(LaunchMode value)
    {
        // Clear fields not relevant to the new mode
        if (value != LaunchMode.VipServer)
        {
            AccessCode = string.Empty;
            PrivateServerLink = string.Empty;
        }
        if (value != LaunchMode.JoinServer)
            JobId = string.Empty;

        SaveSettings();
    }

    private void SaveSettings()
    {
        var s = _settingsService.Current;
        s.PlaceId = PlaceId;
        s.JobId = JobId;
        s.AccessCode = AccessCode;
        s.PrivateServerLink = PrivateServerLink;
        s.LaunchMode = SelectedLaunchMode;
        s.GraphicsPreset = SelectedPreset;
        s.FpsLimit = FpsLimit;
        _settingsService.Save();
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        var accountsToLaunch = SelectedAccounts.Count > 0
            ? SelectedAccounts.ToList()
            : (SelectedAccount != null ? [SelectedAccount] : []);

        if (accountsToLaunch.Count == 0)
        {
            StatusMessage = "請選擇至少一個帳號";
            return;
        }

        if (!long.TryParse(PlaceId, out var placeId) || placeId <= 0)
        {
            StatusMessage = "請輸入有效的 Place ID";
            return;
        }

        IsLaunching = true;
        int launched = 0;
        var errors = new List<string>();

        foreach (var account in accountsToLaunch)
        {
            var config = new LaunchConfiguration
            {
                PlaceId = placeId,
                JobId = SelectedLaunchMode == LaunchMode.JoinServer && !string.IsNullOrWhiteSpace(JobId) ? JobId : null,
                AccessCode = SelectedLaunchMode == LaunchMode.VipServer && !string.IsNullOrWhiteSpace(AccessCode) ? AccessCode : null,
                GraphicsPreset = SelectedPreset,
                FpsLimit = FpsLimit > 0 ? FpsLimit : null,
                LaunchMode = SelectedLaunchMode,
            };

            StatusMessage = $"正在啟動 {account.Username}... ({launched + errors.Count + 1}/{accountsToLaunch.Count})";
            var result = await _serverJoinService.LaunchAsync(account, config);

            if (result.IsSuccess)
            {
                launched++;
            }
            else
            {
                errors.Add($"{account.Username}: {result.Error}");
            }

            // Brief delay between launches to avoid API rate limiting on auth tickets.
            // LaunchAsync itself already waits ~2s for process detection.
            if (accountsToLaunch.Count > 1)
                await Task.Delay(1000);
        }

        if (errors.Count > 0)
            StatusMessage = $"已啟動 {launched}/{accountsToLaunch.Count} 個實例\n失敗:\n{string.Join("\n", errors)}";
        else
            StatusMessage = $"已啟動 {launched}/{accountsToLaunch.Count} 個實例";
        IsLaunching = false;
    }

    [RelayCommand]
    private async Task LoadServersAsync()
    {
        if (!long.TryParse(PlaceId, out var placeId) || placeId <= 0)
        {
            StatusMessage = "請輸入有效的 Place ID";
            return;
        }

        IsLoadingServers = true;
        Servers.Clear();

        var result = await _serverJoinService.GetServersAsync(placeId);
        if (result.IsSuccess)
        {
            foreach (var server in result.Value!)
                Servers.Add(server);

            StatusMessage = $"找到 {Servers.Count} 個伺服器";
        }
        else
        {
            StatusMessage = result.Error;
        }

        IsLoadingServers = false;
    }

    [RelayCommand]
    private void SelectServer(ServerInfo? server)
    {
        if (server == null) return;
        JobId = server.JobId;
        SelectedLaunchMode = LaunchMode.JoinServer;
    }

    /// <summary>
    /// Resolves a Roblox share link or privateServerLinkCode URL into PlaceId + AccessCode,
    /// and auto-fills the launcher fields.
    /// </summary>
    [RelayCommand]
    private async Task ResolvePrivateServerAsync()
    {
        if (string.IsNullOrWhiteSpace(PrivateServerLink))
        {
            StatusMessage = "請貼上私人伺服器連結";
            return;
        }

        var account = SelectedAccounts.FirstOrDefault() ?? SelectedAccount;
        if (account == null)
        {
            StatusMessage = "請先選擇一個帳號（需要帳號權限來解析連結）";
            return;
        }

        IsLaunching = true;
        StatusMessage = "正在解析私人伺服器連結...";

        try
        {
            var cookie = _accountService.DecryptCookie(account);
            var result = await _serverJoinService.ResolveShareLinkAsync(PrivateServerLink, cookie);

            if (result.IsSuccess)
            {
                var info = result.Value!;
                PlaceId = info.PlaceId.ToString();
                AccessCode = info.AccessCode;
                SelectedLaunchMode = LaunchMode.VipServer;
                StatusMessage = $"已解析: Place {info.PlaceId}，可直接啟動";
            }
            else
            {
                StatusMessage = result.Error;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"解析失敗: {ex.Message}";
        }

        IsLaunching = false;
    }
}
