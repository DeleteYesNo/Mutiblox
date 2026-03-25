using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IRobloxLocator _locator;
    private readonly IClientSettingsService _clientSettingsService;
    private readonly IMutexService _mutexService;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IAccountKeepAliveService _keepAliveService;

    [ObservableProperty]
    private string _robloxPath = "未偵測";

    [ObservableProperty]
    private bool _isMultiInstanceEnabled;

    [ObservableProperty]
    private bool _autoEnableMultiInstance;

    [ObservableProperty]
    private bool _autoCookieSync;

    [ObservableProperty]
    private int _defaultFpsLimit = 60;

    [ObservableProperty]
    private GraphicsPreset _defaultPreset = GraphicsPreset.Default;

    [ObservableProperty]
    private string? _statusMessage;

    public Array GraphicsPresets => Enum.GetValues<GraphicsPreset>();

    public SettingsViewModel(
        IRobloxLocator locator,
        IClientSettingsService settingsService,
        IMutexService mutexService,
        IUserSettingsService userSettingsService,
        IAccountKeepAliveService keepAliveService)
    {
        _locator = locator;
        _clientSettingsService = settingsService;
        _mutexService = mutexService;
        _userSettingsService = userSettingsService;
        _keepAliveService = keepAliveService;

        _autoEnableMultiInstance = userSettingsService.Current.AutoEnableMultiInstance;
        _autoCookieSync = userSettingsService.Current.AutoCookieSync;

        DetectRoblox();
    }

    partial void OnAutoEnableMultiInstanceChanged(bool value)
    {
        _userSettingsService.Current.AutoEnableMultiInstance = value;
        _userSettingsService.Save();
    }

    partial void OnAutoCookieSyncChanged(bool value)
    {
        _userSettingsService.Current.AutoCookieSync = value;
        _userSettingsService.Save();

        if (value)
        {
            _keepAliveService.Start();
            StatusMessage = "Cookie 自動保活已啟用（每 6 小時檢查）";
        }
        else
        {
            _keepAliveService.Stop();
            StatusMessage = "Cookie 自動保活已停用";
        }
    }

    [RelayCommand]
    private void DetectRoblox()
    {
        var result = _locator.FindRobloxPath();
        RobloxPath = result.IsSuccess ? result.Value! : "未偵測到 Roblox";
        IsMultiInstanceEnabled = _mutexService.IsMultiInstanceEnabled;
    }

    [RelayCommand]
    private async Task ToggleMultiInstanceAsync()
    {
        if (_mutexService.IsMultiInstanceEnabled)
        {
            _mutexService.Disable();
            IsMultiInstanceEnabled = false;
            StatusMessage = "多開已停用";
        }
        else
        {
            var result = await _mutexService.EnableMultiInstanceAsync();
            IsMultiInstanceEnabled = result.IsSuccess;
            StatusMessage = result.IsSuccess ? "多開已啟用" : $"啟用失敗: {result.Error}";
        }
    }

    [RelayCommand]
    private void ResetClientSettings()
    {
        var pathResult = _locator.FindRobloxPath();
        if (!pathResult.IsSuccess)
        {
            StatusMessage = "找不到 Roblox 路徑";
            return;
        }

        var result = _clientSettingsService.ResetSettings(pathResult.Value!);
        StatusMessage = result.IsSuccess ? "已重設 ClientAppSettings" : result.Error;
    }
}
