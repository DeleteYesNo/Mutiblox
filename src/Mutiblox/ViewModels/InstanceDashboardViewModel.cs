using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mutiblox.Core;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.ViewModels;

public partial class InstanceDashboardViewModel : ObservableObject
{
    private readonly IInstanceManager _instanceManager;
    private readonly IPerformanceService _performanceService;
    private readonly IAntiAfkService _antiAfkService;
    private readonly IAccountService _accountService;
    private readonly IUserSettingsService _settingsService;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isAntiAfkEnabled;

    [ObservableProperty]
    private int _antiAfkIntervalMinutes = 10;

    public ObservableCollection<RobloxInstance> Instances { get; } = [];

    public int RunningCount => Instances.Count(i => i.Status == InstanceStatus.Running);
    public long TotalMemoryBytes => Instances.Where(i => i.Status == InstanceStatus.Running).Sum(i => i.MemoryBytes);

    public string TotalMemoryDisplay => TotalMemoryBytes switch
    {
        < 1024 * 1024 => $"{TotalMemoryBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{TotalMemoryBytes / (1024.0 * 1024):F1} MB",
        _ => $"{TotalMemoryBytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    public InstanceDashboardViewModel(
        IInstanceManager instanceManager,
        IPerformanceService performanceService,
        IAntiAfkService antiAfkService,
        IAccountService accountService,
        IUserSettingsService settingsService)
    {
        _instanceManager = instanceManager;
        _performanceService = performanceService;
        _antiAfkService = antiAfkService;
        _accountService = accountService;
        _settingsService = settingsService;

        // Restore saved settings
        _antiAfkIntervalMinutes = settingsService.Current.AntiAfkIntervalMinutes;

        _instanceManager.InstancesChanged += OnInstancesChanged;
        _performanceService.MetricsUpdated += OnMetricsUpdated;
    }

    [RelayCommand]
    private void Refresh()
    {
        _instanceManager.RefreshInstances();
        OnInstancesChanged();
    }

    /// <summary>
    /// Cookie injection is disabled — RobloxCookies.dat is locked to prevent
    /// cross-instance cookie contamination (773 fix). Each instance uses its
    /// auth ticket session for teleportation instead.
    /// </summary>
    [RelayCommand]
    private void ActivateTeleport(int processId)
    {
        StatusMessage = "RobloxCookies.dat 已鎖定（773 修復），每個實例使用獨立 session 進行傳送";
    }

    [RelayCommand]
    private void StopInstance(int processId)
    {
        _instanceManager.StopInstance(processId);
        StatusMessage = $"已停止進程 {processId}";
    }

    [RelayCommand]
    private void StopAll()
    {
        _instanceManager.StopAll();
        StatusMessage = "已停止所有實例";
    }

    [RelayCommand]
    private void StartMonitoring()
    {
        _performanceService.StartMonitoring();
        StatusMessage = "效能監控已啟動";
    }

    [RelayCommand]
    private void StopMonitoring()
    {
        _performanceService.StopMonitoring();
        StatusMessage = "效能監控已停止";
    }

    [RelayCommand]
    private void ToggleAntiAfk()
    {
        if (_antiAfkService.IsRunning)
        {
            _antiAfkService.Stop();
            IsAntiAfkEnabled = false;
            StatusMessage = "防掛機已停用";
        }
        else
        {
            _antiAfkService.Interval = TimeSpan.FromMinutes(
                AntiAfkIntervalMinutes > 0 ? AntiAfkIntervalMinutes : 10);
            _antiAfkService.Start();
            IsAntiAfkEnabled = true;
            StatusMessage = $"防掛機已啟用（每 {AntiAfkIntervalMinutes} 分鐘）";
        }

        _settingsService.Current.AntiAfkIntervalMinutes = AntiAfkIntervalMinutes;
        _settingsService.Save();
    }

    private void OnInstancesChanged()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Instances.Clear();
            foreach (var instance in _instanceManager.Instances)
                Instances.Add(instance);

            OnPropertyChanged(nameof(RunningCount));
            OnPropertyChanged(nameof(TotalMemoryBytes));
            OnPropertyChanged(nameof(TotalMemoryDisplay));

            // Auto-start monitoring when instances exist
            if (Instances.Any(i => i.Status == InstanceStatus.Running))
                _performanceService.StartMonitoring();
        });
    }

    private void OnMetricsUpdated()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            // Rebuild collection to reflect updated CpuPercent/MemoryBytes
            Instances.Clear();
            foreach (var instance in _instanceManager.Instances)
                Instances.Add(instance);

            OnPropertyChanged(nameof(TotalMemoryBytes));
            OnPropertyChanged(nameof(TotalMemoryDisplay));
        });
    }
}
