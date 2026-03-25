using System.Text.Json;
using Mutiblox.Core;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class ClientSettingsService : IClientSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public Result ApplyPreset(GraphicsPreset preset, string robloxPath)
    {
        var settings = GetPresetSettings(preset);
        return WriteSettings(settings, robloxPath);
    }

    public Result SetFpsLimit(int fps, string robloxPath)
    {
        var settings = new Dictionary<string, object>
        {
            ["DFIntTaskSchedulerTargetFps"] = fps
        };
        return MergeSettings(settings, robloxPath);
    }

    public Result ResetSettings(string robloxPath)
    {
        try
        {
            var settingsDir = Path.Combine(robloxPath, Constants.ClientSettingsFolder);
            var settingsFile = Path.Combine(settingsDir, Constants.ClientAppSettingsFile);

            if (File.Exists(settingsFile))
                File.Delete(settingsFile);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"無法重設設定: {ex.Message}");
        }
    }

    private static Dictionary<string, object> GetPresetSettings(GraphicsPreset preset) => preset switch
    {
        GraphicsPreset.Ultra => new()
        {
            ["DFIntTaskSchedulerTargetFps"] = 999,
            ["FIntRenderShadowIntensity"] = 100,
            ["DFFlagDebugRenderForceTechnologyVoxel"] = false,
        },
        GraphicsPreset.High => new()
        {
            ["DFIntTaskSchedulerTargetFps"] = 144,
            ["FIntRenderShadowIntensity"] = 75,
        },
        GraphicsPreset.Medium => new()
        {
            ["DFIntTaskSchedulerTargetFps"] = 60,
            ["FIntRenderShadowIntensity"] = 50,
        },
        GraphicsPreset.Low => new()
        {
            ["DFIntTaskSchedulerTargetFps"] = 60,
            ["FIntRenderShadowIntensity"] = 0,
            ["DFFlagDebugRenderForceTechnologyVoxel"] = true,
        },
        GraphicsPreset.Potato => new()
        {
            ["DFIntTaskSchedulerTargetFps"] = 30,
            ["FIntRenderShadowIntensity"] = 0,
            ["DFFlagDebugRenderForceTechnologyVoxel"] = true,
            ["FIntRenderLocalLightUpdatesMax"] = 1,
            ["FIntRenderLocalLightUpdatesMin"] = 1,
            ["FIntTerrainOctreeMaxDepth"] = 3,
            ["FFlagGlobalWindRendering"] = false,
            ["FIntDebugTextureManagerSkipMips"] = 3,
            ["FFlagDebugSkyGray"] = true,
        },
        _ => new()
        {
            ["DFIntTaskSchedulerTargetFps"] = 60,
        }
    };

    private static Result WriteSettings(Dictionary<string, object> settings, string robloxPath)
    {
        try
        {
            var settingsDir = Path.Combine(robloxPath, Constants.ClientSettingsFolder);
            Directory.CreateDirectory(settingsDir);

            var settingsFile = Path.Combine(settingsDir, Constants.ClientAppSettingsFile);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(settingsFile, json);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"無法寫入設定: {ex.Message}");
        }
    }

    private static Result MergeSettings(Dictionary<string, object> newSettings, string robloxPath)
    {
        try
        {
            var settingsDir = Path.Combine(robloxPath, Constants.ClientSettingsFolder);
            Directory.CreateDirectory(settingsDir);

            var settingsFile = Path.Combine(settingsDir, Constants.ClientAppSettingsFile);
            var existing = new Dictionary<string, object>();

            if (File.Exists(settingsFile))
            {
                var existingJson = File.ReadAllText(settingsFile);
                existing = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson) ?? [];
            }

            foreach (var (key, value) in newSettings)
                existing[key] = value;

            var json = JsonSerializer.Serialize(existing, JsonOptions);
            File.WriteAllText(settingsFile, json);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"無法合併設定: {ex.Message}");
        }
    }
}
