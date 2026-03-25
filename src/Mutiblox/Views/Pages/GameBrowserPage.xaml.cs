using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Mutiblox.ViewModels;

namespace Mutiblox.Views.Pages;

public partial class GameBrowserPage : UserControl
{
    private const string RobloxDiscoverUrl = "https://www.roblox.com/discover";
    private const string RobloxHomeUrl = "https://www.roblox.com/home";
    private bool _isInitialized;

    public GameBrowserPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is GameBrowserViewModel vm)
        {
            vm.CookieReady += OnCookieReady;
            vm.RefreshAccountsCommand.Execute(null);
        }

        if (e.OldValue is GameBrowserViewModel oldVm)
        {
            oldVm.CookieReady -= OnCookieReady;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        try
        {
            var userDataFolder = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "Mutiblox", "GameBrowser");

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await GameWebView.EnsureCoreWebView2Async(env);

            // Intercept roblox-player: protocol navigation
            GameWebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            GameWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            GameWebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            _isInitialized = true;

            // If we already have an account selected, inject cookie and navigate
            if (DataContext is GameBrowserViewModel vm && vm.SelectedAccount != null)
            {
                vm.SwitchAccountCommand.Execute(null);
            }
            else
            {
                GameWebView.CoreWebView2.Navigate(RobloxDiscoverUrl);
            }
        }
        catch (Exception ex)
        {
            if (DataContext is GameBrowserViewModel vm)
                vm.StatusMessage = $"WebView2 初始化失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// Injects the .ROBLOSECURITY cookie into the WebView2 session,
    /// then navigates to the Roblox home/discover page.
    /// </summary>
    private async void OnCookieReady(string cookie)
    {
        if (GameWebView.CoreWebView2 == null) return;

        var manager = GameWebView.CoreWebView2.CookieManager;

        // Clear existing Roblox cookies first
        var existingCookies = await manager.GetCookiesAsync("https://www.roblox.com");
        foreach (var c in existingCookies)
        {
            if (c.Name == ".ROBLOSECURITY")
                manager.DeleteCookie(c);
        }

        // Set the new cookie
        var newCookie = manager.CreateCookie(".ROBLOSECURITY", cookie, ".roblox.com", "/");
        newCookie.IsSecure = true;
        newCookie.IsHttpOnly = true;
        manager.AddOrUpdateCookie(newCookie);

        // Navigate to discover page
        GameWebView.CoreWebView2.Navigate(RobloxDiscoverUrl);
    }

    /// <summary>
    /// Intercepts roblox-player: protocol URIs to launch through our system.
    /// </summary>
    private async void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
        {
            // Cancel the default navigation - we handle this ourselves
            e.Cancel = true;

            if (DataContext is GameBrowserViewModel vm)
            {
                await vm.HandleProtocolLaunchAsync(e.Uri);
            }
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UrlBar.Text = GameWebView.Source?.ToString() ?? "";
        });
    }

    /// <summary>
    /// Handle links that try to open in a new window (target="_blank").
    /// Open them in our WebView instead.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // If it's a roblox-player: protocol, intercept it
        if (e.Uri.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            if (DataContext is GameBrowserViewModel vm)
            {
                _ = vm.HandleProtocolLaunchAsync(e.Uri);
            }
            return;
        }

        // For regular Roblox URLs, navigate in the same window
        if (e.Uri.Contains("roblox.com", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            GameWebView.CoreWebView2.Navigate(e.Uri);
        }
    }

    private void Account_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        if (DataContext is GameBrowserViewModel vm && vm.SelectedAccount != null)
        {
            vm.SwitchAccountCommand.Execute(null);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (GameWebView.CoreWebView2?.CanGoBack == true)
            GameWebView.CoreWebView2.GoBack();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        GameWebView.CoreWebView2?.Reload();
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        GameWebView.CoreWebView2?.Navigate(RobloxDiscoverUrl);
    }
}
