using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Mutiblox.Services.Implementations;
using Mutiblox.Services.Interfaces;
using Mutiblox.ViewModels;
using Mutiblox.Views;

namespace Mutiblox;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;
    private FileStream? _cookieLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers — prevent app crash from killing Roblox instances
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Lock RobloxCookies.dat for the entire application lifetime.
        // This prevents Roblox instances from reading/writing to the shared cookie file,
        // avoiding cross-contamination between accounts (Error 773 / Reason 285).
        // Authentication is handled entirely via per-account auth tickets in the launch URL.
        LockCookieFile();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Load settings and accounts on startup (must complete before UI renders)
        _serviceProvider.GetRequiredService<IUserSettingsService>().Load();
        var accountService = _serviceProvider.GetRequiredService<IAccountService>();
        Task.Run(() => accountService.LoadAsync()).GetAwaiter().GetResult();

        // Auto-enable multi-instance if user enabled this option
        var userSettings = _serviceProvider.GetRequiredService<IUserSettingsService>();
        if (userSettings.Current.AutoEnableMultiInstance)
        {
            var mutexService = _serviceProvider.GetRequiredService<IMutexService>();
            Task.Run(() => mutexService.EnableMultiInstanceAsync()).GetAwaiter().GetResult();
        }

        // Start cookie keep-alive if user enabled auto sync
        if (userSettings.Current.AutoCookieSync)
        {
            _serviceProvider.GetRequiredService<IAccountKeepAliveService>().Start();
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    /// <summary>
    /// Catches exceptions on the UI thread. Logs and continues — do NOT crash.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true; // Prevent app termination

        try
        {
            LogException("UI Thread", e.Exception);

            MessageBox.Show(
                $"發生錯誤，但應用程式將繼續運行。\n\n{e.Exception.Message}",
                "Mutiblox - 錯誤",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // Last resort — swallow everything to keep the app alive
        }
    }

    /// <summary>
    /// Catches exceptions on background threads.
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("Background Thread", ex);

            if (!e.IsTerminating)
                return;

            // App is terminating — show a message
            try
            {
                MessageBox.Show(
                    $"發生嚴重錯誤，應用程式即將關閉。\nRoblox 遊戲視窗不會受到影響。\n\n{ex.Message}",
                    "Mutiblox - 嚴重錯誤",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
        }
    }

    /// <summary>
    /// Catches unobserved Task exceptions (fire-and-forget async tasks).
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved(); // Prevent crash
        LogException("Unobserved Task", e.Exception);
    }

    private static void LogException(string source, Exception ex)
    {
        try
        {
            var logDir = Core.Constants.AppDataPath;
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "crash.log");

            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n";
            File.AppendAllText(logPath, entry);
        }
        catch
        {
            // Logging itself must never crash
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // HTTP — UseCookies must be false so the shared handler pool does not
        // accumulate Set-Cookie responses across different accounts.  Each request
        // sets the Cookie header explicitly for its own account.
        services.AddHttpClient("Roblox", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Roblox/WinInet");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseCookies = false
        });

        // Services
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IEncryptionService, DpapiEncryptionService>();
        services.AddSingleton<IRobloxLocator, RobloxLocator>();
        services.AddSingleton<IRobloxApiService, RobloxApiService>();
        services.AddSingleton<IAccountService, AccountService>();
        services.AddSingleton<IMutexService, MutexService>();
        services.AddSingleton<IClientSettingsService, ClientSettingsService>();
        services.AddSingleton<IInstanceManager, InstanceManager>();
        services.AddSingleton<IPerformanceService, PerformanceService>();
        services.AddSingleton<IServerJoinService, ServerJoinService>();
        services.AddSingleton<IBrowserCookieService, BrowserCookieService>();
        services.AddSingleton<IAntiAfkService, AntiAfkService>();
        services.AddSingleton<IAccountKeepAliveService, AccountKeepAliveService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<AccountListViewModel>();
        services.AddSingleton<GameBrowserViewModel>();
        services.AddSingleton<LauncherViewModel>();
        services.AddSingleton<InstanceDashboardViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Persist user settings before shutdown
        try { _serviceProvider?.GetService<IUserSettingsService>()?.Save(); } catch { }
        _serviceProvider?.Dispose();

        // Release cookie file lock
        try { _cookieLock?.Dispose(); } catch { }
        _cookieLock = null;

        base.OnExit(e);
    }

    /// <summary>
    /// Locks RobloxCookies.dat with exclusive access (FileShare.None) so that no
    /// Roblox instance can read or write to it. Auth tickets handle authentication.
    /// </summary>
    private void LockCookieFile()
    {
        try
        {
            var path = Core.Constants.RobloxCookiesPath;
            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);

            _cookieLock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex)
        {
            // Non-fatal — log and continue. Multi-instance may be less stable without the lock.
            LogException("CookieLock", ex);
        }
    }
}
