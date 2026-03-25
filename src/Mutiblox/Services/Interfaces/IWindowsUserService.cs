using Mutiblox.Core;

namespace Mutiblox.Services.Interfaces;

public record WindowsUserInfo(string Username, string Password, string ProfilePath);

public interface IWindowsUserService
{
    /// <summary>
    /// Ensures a Windows user account exists for the given slot.
    /// Slot 0 = current user (no action). Slot 1+ = Mutiblox_N.
    /// </summary>
    Task<Result<WindowsUserInfo>> EnsureUserAsync(int slotIndex);

    /// <summary>
    /// Launches a process under the given slot's Windows user.
    /// Slot 0 = current user via Process.Start.
    /// </summary>
    Result<int> LaunchProcess(int slotIndex, string exePath, string arguments, string workingDirectory);

    /// <summary>
    /// Injects a cookie into the slot's user RobloxCookies.dat,
    /// encrypted with that user's DPAPI context.
    /// </summary>
    Result InjectCookieForUser(int slotIndex, string cookie);

    /// <summary>
    /// Gets the next available slot index based on running instances.
    /// </summary>
    int GetNextSlot(IReadOnlyList<Models.RobloxInstance> runningInstances);
}
