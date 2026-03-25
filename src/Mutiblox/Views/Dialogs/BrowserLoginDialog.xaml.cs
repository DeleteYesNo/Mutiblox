using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Mutiblox.Views.Dialogs;

public partial class BrowserLoginDialog : Window
{
    private const string RobloxLoginUrl = "https://www.roblox.com/login";
    private const string RobloxHomeUrl = "https://www.roblox.com/home";
    private const string CookieName = ".ROBLOSECURITY";

    /// <summary>
    /// The captured .ROBLOSECURITY cookie value, or null if login was cancelled.
    /// </summary>
    public string? CapturedCookie { get; private set; }

    private readonly System.Timers.Timer _cookieCheckTimer;

    public BrowserLoginDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        _cookieCheckTimer = new System.Timers.Timer(1500);
        _cookieCheckTimer.Elapsed += async (_, _) => await CheckForCookieAsync();
        _cookieCheckTimer.AutoReset = true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Initialize WebView2 with a unique user data folder to avoid conflicts
            var userDataFolder = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "Mutiblox", "WebView2");

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await LoginWebView.EnsureCoreWebView2Async(env);

            // Clear existing cookies to ensure clean login
            LoginWebView.CoreWebView2.CookieManager.DeleteAllCookies();

            // Navigate to Roblox login
            LoginWebView.CoreWebView2.Navigate(RobloxLoginUrl);

            // Start polling for the cookie
            _cookieCheckTimer.Start();

            // Also check on navigation completed
            LoginWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"WebView2 初始化失敗: {ex.Message}";
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        await CheckForCookieAsync();
    }

    private async Task CheckForCookieAsync()
    {
        try
        {
            if (LoginWebView.CoreWebView2 == null)
                return;

            var cookies = await LoginWebView.CoreWebView2.CookieManager
                .GetCookiesAsync("https://www.roblox.com");

            var roblosecurity = cookies.FirstOrDefault(c =>
                c.Name == CookieName && !string.IsNullOrEmpty(c.Value));

            if (roblosecurity != null)
            {
                CapturedCookie = roblosecurity.Value;
                _cookieCheckTimer.Stop();

                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "登入成功！Cookie 已擷取";
                    DialogResult = true;
                    Close();
                });
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    var uri = LoginWebView.Source;
                    if (uri != null && uri.AbsolutePath.Contains("home"))
                        StatusText.Text = "偵測到登入頁面，正在擷取 Cookie...";
                    else
                        StatusText.Text = "等待登入...";
                });
            }
        }
        catch
        {
            // Ignore errors during polling
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        LoginWebView.CoreWebView2?.CookieManager.DeleteAllCookies();
        LoginWebView.CoreWebView2?.Navigate(RobloxLoginUrl);
        StatusText.Text = "等待登入...";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cookieCheckTimer.Stop();
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cookieCheckTimer.Stop();
        _cookieCheckTimer.Dispose();
        LoginWebView.Dispose();
        base.OnClosed(e);
    }
}
