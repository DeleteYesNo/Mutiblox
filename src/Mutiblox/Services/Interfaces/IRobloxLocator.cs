using Mutiblox.Core;

namespace Mutiblox.Services.Interfaces;

public interface IRobloxLocator
{
    Result<string> FindRobloxPath();
    Result<string> FindPlayerExecutable();
    Result<string> GetClientSettingsPath();
}
