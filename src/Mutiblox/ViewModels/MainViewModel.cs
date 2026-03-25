using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mutiblox.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private string _title = "Mutiblox";

    [ObservableProperty]
    private int _selectedNavIndex;

    public AccountListViewModel AccountList { get; }
    public GameBrowserViewModel GameBrowser { get; }
    public LauncherViewModel Launcher { get; }
    public InstanceDashboardViewModel Dashboard { get; }
    public SettingsViewModel Settings { get; }

    public MainViewModel(
        AccountListViewModel accountList,
        GameBrowserViewModel gameBrowser,
        LauncherViewModel launcher,
        InstanceDashboardViewModel dashboard,
        SettingsViewModel settings)
    {
        AccountList = accountList;
        GameBrowser = gameBrowser;
        Launcher = launcher;
        Dashboard = dashboard;
        Settings = settings;
        CurrentPage = accountList;
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentPage = value switch
        {
            0 => AccountList,
            1 => GameBrowser,
            2 => Launcher,
            3 => Dashboard,
            4 => Settings,
            _ => AccountList
        };
    }
}
