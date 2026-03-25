using System.IO;
using System.Text.Json;
using Mutiblox.Core;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class UserSettingsService : IUserSettingsService
{
    private static readonly string SettingsPath = Path.Combine(Constants.AppDataPath, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public UserSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<UserSettings>(json) ?? new();
            }
        }
        catch
        {
            Current = new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Constants.AppDataPath);
            var json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Settings save must never crash the app
        }
    }
}
