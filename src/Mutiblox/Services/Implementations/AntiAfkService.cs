using System.Runtime.InteropServices;
using Mutiblox.Core;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

/// <summary>
/// Prevents Roblox's 20-minute idle kick by sending periodic key presses
/// to each running Roblox window via PostMessage.
///
/// Safety:
/// - Uses PostMessage (window message), not WriteProcessMemory
/// - Sends to background windows without stealing focus
/// - Sends F13 key which doesn't interfere with gameplay
/// - No process memory access, no injection
/// </summary>
public sealed class AntiAfkService : IAntiAfkService, IDisposable
{
    private readonly IInstanceManager _instanceManager;
    private Timer? _timer;

    public bool IsRunning => _timer != null;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);

    public AntiAfkService(IInstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
    }

    public void Start()
    {
        Stop();
        _timer = new Timer(OnTick, null, TimeSpan.FromMinutes(1), Interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick(object? state)
    {
        var instances = _instanceManager.Instances;

        foreach (var instance in instances.Where(i => i.Status == InstanceStatus.Running))
        {
            SendAntiAfkInput(instance.ProcessId);
        }
    }

    /// <summary>
    /// Finds the main window of a Roblox process and sends a brief key press.
    /// Uses F13 which Roblox registers as input activity but doesn't trigger
    /// any in-game action.
    /// </summary>
    private static void SendAntiAfkInput(int processId)
    {
        var hWnd = FindMainWindow(processId);
        if (hWnd == nint.Zero)
            return;

        // Send F13 key down then up — registers as activity without gameplay effect
        NativeMethods.PostMessage(hWnd, NativeMethods.WM_KEYDOWN, NativeMethods.VK_F13, 0);

        // Small delay between down and up using a thread pool task
        Task.Delay(50).ContinueWith(_ =>
        {
            NativeMethods.PostMessage(hWnd, NativeMethods.WM_KEYUP, NativeMethods.VK_F13, 0);
        });
    }

    /// <summary>
    /// Finds the main visible window belonging to a given process ID.
    /// </summary>
    private static nint FindMainWindow(int processId)
    {
        nint result = nint.Zero;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out int pid);
            if (pid == processId && NativeMethods.IsWindowVisible(hWnd))
            {
                result = hWnd;
                return false; // Stop enumerating
            }
            return true; // Continue
        }, nint.Zero);

        return result;
    }

    public void Dispose()
    {
        Stop();
    }
}
