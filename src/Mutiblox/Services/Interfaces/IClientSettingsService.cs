using Mutiblox.Core;
using Mutiblox.Models;

namespace Mutiblox.Services.Interfaces;

public interface IClientSettingsService
{
    Result ApplyPreset(GraphicsPreset preset, string robloxPath);
    Result SetFpsLimit(int fps, string robloxPath);
    Result ResetSettings(string robloxPath);
}
