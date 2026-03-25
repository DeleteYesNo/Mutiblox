namespace Mutiblox.Models;

public sealed class UserSettings
{
    // Launcher
    public string PlaceId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string AccessCode { get; set; } = string.Empty;
    public string PrivateServerLink { get; set; } = string.Empty;
    public LaunchMode LaunchMode { get; set; } = LaunchMode.Normal;
    public GraphicsPreset GraphicsPreset { get; set; } = GraphicsPreset.Default;
    public int FpsLimit { get; set; } = 60;

    // Dashboard
    public int AntiAfkIntervalMinutes { get; set; } = 10;
    public bool AutoCookieSync { get; set; }
    public bool AutoEnableMultiInstance { get; set; }
}
