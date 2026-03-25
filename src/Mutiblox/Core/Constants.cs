namespace Mutiblox.Core;

public static class Constants
{
    // Roblox API URLs
    public const string RobloxAuthUrl = "https://auth.roblox.com";
    public const string RobloxUsersUrl = "https://users.roblox.com";
    public const string RobloxGamesUrl = "https://games.roblox.com";
    public const string RobloxPresenceUrl = "https://presence.roblox.com";
    public const string RobloxThumbnailsUrl = "https://thumbnails.roblox.com";

    // Mutex/Event names used by Roblox for single-instance enforcement
    public const string RobloxSingletonMutex = "ROBLOX_singletonMutex";
    public const string RobloxSingletonEvent = "ROBLOX_singletonEvent";

    // Application paths
    public static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mutiblox");

    public static readonly string AccountsFilePath = Path.Combine(AppDataPath, "accounts.json");

    // Roblox installation detection paths
    public static readonly string RobloxLocalAppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Roblox");

    public static readonly string RobloxProgramFiles = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Roblox");

    public const string RobloxPlayerExe = "RobloxPlayerBeta.exe";
    public const string ClientSettingsFolder = "ClientSettings";
    public const string ClientAppSettingsFile = "ClientAppSettings.json";

    // Watcher
    public const string WatcherExeName = "Mutiblox.Watcher.exe";
    public const string WatcherReadySignal = "WATCHER_READY";

    // Roblox cookie storage
    public static readonly string RobloxCookiesPath = Path.Combine(
        RobloxLocalAppData, "LocalStorage", "RobloxCookies.dat");

    // Performance monitoring interval
    public static readonly TimeSpan PerformanceUpdateInterval = TimeSpan.FromSeconds(3);
}
