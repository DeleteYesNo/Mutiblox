using Mutiblox.Core;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class RobloxLocator : IRobloxLocator
{
    private string? _cachedPath;

    public Result<string> FindRobloxPath()
    {
        if (_cachedPath != null && Directory.Exists(_cachedPath))
            return Result<string>.Success(_cachedPath);

        // Check common Roblox installation paths
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Roblox", "Versions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Roblox", "Versions"),
        };

        foreach (var versionsDir in candidates)
        {
            if (!Directory.Exists(versionsDir))
                continue;

            // Find the most recent version directory that contains the player
            var versionDirs = Directory.GetDirectories(versionsDir)
                .OrderByDescending(Directory.GetLastWriteTime);

            foreach (var dir in versionDirs)
            {
                var playerPath = Path.Combine(dir, Constants.RobloxPlayerExe);
                if (File.Exists(playerPath))
                {
                    _cachedPath = dir;
                    return Result<string>.Success(dir);
                }
            }
        }

        return Result<string>.Failure("找不到 Roblox 安裝路徑。請確認 Roblox 已安裝。");
    }

    public Result<string> FindPlayerExecutable()
    {
        var pathResult = FindRobloxPath();
        if (!pathResult.IsSuccess)
            return pathResult;

        var exePath = Path.Combine(pathResult.Value!, Constants.RobloxPlayerExe);
        return File.Exists(exePath)
            ? Result<string>.Success(exePath)
            : Result<string>.Failure("找不到 RobloxPlayerBeta.exe");
    }

    public Result<string> GetClientSettingsPath()
    {
        var pathResult = FindRobloxPath();
        if (!pathResult.IsSuccess)
            return pathResult;

        var settingsPath = Path.Combine(pathResult.Value!, Constants.ClientSettingsFolder);
        return Result<string>.Success(settingsPath);
    }
}
