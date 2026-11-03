using System.Diagnostics;
using System.Runtime.InteropServices;
using Mutiblox.Core;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

/// <summary>
/// Enables multi-instance Roblox by pre-acquiring the singleton mutex/event
/// and holding them for the entire application lifetime.
///
/// Strategy (matches Roblox Account Manager):
/// - Acquire ROBLOX_singletonMutex + ROBLOX_singletonEvent at startup
/// - Hold permanently — never release between launches
/// - When Roblox starts, it sees the mutex already exists and skips singleton enforcement
/// - If Roblox is already running at startup, close its mutex handles first, then acquire
/// </summary>
public sealed class MutexService : IMutexService, IDisposable
{
    private Mutex? _singletonMutex;
    private EventWaitHandle? _singletonEvent;

    // Dedicated thread that owns the mutex for the entire app lifetime.
    // Windows mutexes are thread-affine — if the owning thread terminates,
    // the mutex becomes ABANDONED and Roblox's WndProcessCheck will seize it,
    // then close all other instances. A thread-pool thread would be recycled.
    private Thread? _mutexOwnerThread;
    private readonly ManualResetEventSlim _shutdownSignal = new(false);
    private readonly ManualResetEventSlim _acquisitionDone = new(false);
    private Result? _acquisitionResult;

    public bool IsMultiInstanceEnabled => _singletonMutex != null;

    public async Task<Result> EnableMultiInstanceAsync(CancellationToken ct = default)
    {
        if (_singletonMutex != null)
            return Result.Success();

        // Handle enumeration is slow — close existing handles on a thread pool thread first.
        await Task.Run(() =>
        {
            LogDiagnostic("Attempting mutex acquisition...");
            TerminateTrayProcesses();
            LogDiagnostic("Closing existing Roblox handles (if any)...");
            CloseExistingRobloxHandles();
        }, ct).ConfigureAwait(false);

        // Acquire the mutex on a dedicated long-lived thread.
        _mutexOwnerThread = new Thread(MutexOwnerThreadProc)
        {
            Name = "MutexOwner",
            IsBackground = true
        };
        _mutexOwnerThread.Start();

        // Wait for the acquisition attempt to complete.
        _acquisitionDone.Wait(ct);
        return _acquisitionResult ?? Result.Failure("Mutex 取得結果未設定");
    }

    /// <summary>
    /// Runs on a dedicated thread. Acquires the mutex and then blocks until
    /// the application signals shutdown, keeping the mutex ownership alive.
    /// </summary>
    private void MutexOwnerThreadProc()
    {
        try
        {
            if (TryAcquireMutex())
            {
                LogDiagnostic("Multi-instance enabled: mutex acquired on dedicated thread");
                _acquisitionResult = Result.Success();
                _acquisitionDone.Set();

                // Block this thread until shutdown — the mutex stays owned.
                _shutdownSignal.Wait();

                // Shutdown requested — release mutex on THIS thread (the owner).
                ReleaseMutexOnOwnerThread();
            }
            else
            {
                LogDiagnostic("All attempts failed");
                _acquisitionResult = Result.Failure(
                    "無法取得 ROBLOX_singletonMutex。請確認已關閉所有 Roblox 進程（包括背景進程），然後重試。");
                _acquisitionDone.Set();
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"MutexOwnerThread exception: {ex.Message}");
            _acquisitionResult = Result.Failure($"Mutex 執行緒異常: {ex.Message}");
            _acquisitionDone.Set();
        }
    }

    /// <summary>
    /// Acquires BOTH ROBLOX_singletonMutex AND ROBLOX_singletonEvent.
    /// MultiBloxy targets the Event, Fishstrap/Bloxstrap target the Mutex.
    /// We hold both for maximum compatibility.
    /// Must be called from the dedicated mutex owner thread.
    /// </summary>
    private bool TryAcquireMutex()
    {
        Mutex? mtx = null;
        EventWaitHandle? evt = null;

        try
        {
            // Acquire the Event first (MultiBloxy and Zgoly's script target this)
            evt = new EventWaitHandle(false, EventResetMode.ManualReset,
                Constants.RobloxSingletonEvent);

            // Acquire the Mutex (Fishstrap/Bloxstrap approach)
            mtx = new Mutex(true, Constants.RobloxSingletonMutex, out bool createdNew);

            if (createdNew)
            {
                _singletonMutex = mtx;
                _singletonEvent = evt;
                return true;
            }

            // Mutex already exists — try to acquire ownership.
            try
            {
                if (mtx.WaitOne(TimeSpan.FromSeconds(2)))
                {
                    _singletonMutex = mtx;
                    _singletonEvent = evt;
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                _singletonMutex = mtx;
                _singletonEvent = evt;
                LogDiagnostic("Acquired abandoned mutex");
                return true;
            }

            mtx.Dispose();
            evt.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            LogDiagnostic($"TryAcquireMutex exception: {ex.Message}");
            mtx?.Dispose();
            evt?.Dispose();
            return false;
        }
    }

    private void ReleaseMutexOnOwnerThread()
    {
        try { _singletonMutex?.ReleaseMutex(); } catch { }
        _singletonMutex?.Dispose();
        _singletonMutex = null;
        _singletonEvent?.Dispose();
        _singletonEvent = null;
        LogDiagnostic("Multi-instance disabled: mutex released");
    }

    public void Disable()
    {
        // Signal the owner thread to release and exit.
        _shutdownSignal.Set();
        _mutexOwnerThread?.Join(3000);
        _mutexOwnerThread = null;
    }

    public void Dispose()
    {
        Disable();
        _shutdownSignal.Dispose();
        _acquisitionDone.Dispose();
    }

    /// <summary>
    /// Terminates idle Roblox tray processes ("RobloxPlayerBeta.exe --launch-to-tray").
    /// Since late 2026 Roblox autostarts this background process via the HKCU Run key.
    /// It owns ROBLOX_singletonMutex/Event before we start, and its WndProcessCheck
    /// would compete with us for the mutex. It is not a game client, so killing it is safe;
    /// it simply restarts at the next Windows login.
    /// </summary>
    private static void TerminateTrayProcesses()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'RobloxPlayerBeta.exe'");

            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var cmd = obj["CommandLine"] as string;
                    if (cmd == null || !cmd.Contains("--launch-to-tray", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var pid = Convert.ToInt32(obj["ProcessId"]);
                    try
                    {
                        using var proc = Process.GetProcessById(pid);
                        proc.Kill();
                        proc.WaitForExit(3000);
                        LogDiagnostic($"Terminated Roblox tray process (PID {pid})");
                    }
                    catch (Exception ex)
                    {
                        LogDiagnostic($"Failed to terminate tray process PID {pid}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogDiagnostic($"TerminateTrayProcesses failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Closes ROBLOX_singletonMutex/Event handles from ALL Roblox-related processes.
    /// Searches for both RobloxPlayerBeta and RobloxPlayerLauncher.
    /// </summary>
    private void CloseExistingRobloxHandles()
    {
        try
        {
            var pids = new HashSet<int>();

            foreach (var name in new[] { "RobloxPlayerBeta", "RobloxPlayerLauncher" })
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    pids.Add(proc.Id);
                    proc.Dispose();
                }
            }

            if (pids.Count == 0)
            {
                LogDiagnostic("No existing Roblox processes found");
                return;
            }

            LogDiagnostic($"Found {pids.Count} Roblox process(es): [{string.Join(", ", pids)}]");

            foreach (var pid in pids)
                CloseSingletonHandlesForProcess(pid);
        }
        catch (Exception ex)
        {
            LogDiagnostic($"CloseExistingRobloxHandles failed: {ex.Message}");
        }
    }

    private static void CloseSingletonHandlesForProcess(int processId)
    {
        int bufferSize = 0x10000;
        nint buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            int status;
            while ((status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemExtendedHandleInformation, buffer, bufferSize, out int returnLength)) ==
                NativeMethods.STATUS_INFO_LENGTH_MISMATCH)
            {
                bufferSize = returnLength + 0x1000;
                Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal(bufferSize);
            }

            if (status != NativeMethods.STATUS_SUCCESS)
                return;

            long handleCount = Marshal.ReadIntPtr(buffer).ToInt64();
            int entrySize = Marshal.SizeOf<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
            nint entryPtr = buffer + nint.Size + nint.Size;

            nint processHandle = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_DUP_HANDLE, false, processId);

            if (processHandle == 0)
                return;

            try
            {
                for (long i = 0; i < handleCount; i++)
                {
                    var entry = Marshal.PtrToStructure<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(
                        entryPtr + (int)(i * entrySize));

                    if (entry.UniqueProcessId.ToInt64() != processId)
                        continue;

                    if (TryGetHandleName(processHandle, entry.HandleValue, out string? name) &&
                        name != null)
                    {
                        if (name.Contains(Constants.RobloxSingletonMutex, StringComparison.OrdinalIgnoreCase))
                        {
                            NativeMethods.DuplicateHandle(
                                processHandle,
                                entry.HandleValue,
                                0,
                                out _,
                                0,
                                false,
                                NativeMethods.DUPLICATE_CLOSE_SOURCE);

                            LogDiagnostic($"Closed handle: {name} (PID {processId})");
                        }
                    }
                }
            }
            finally
            {
                NativeMethods.CloseHandle(processHandle);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryGetHandleName(nint processHandle, nint handle, out string? name)
    {
        name = null;

        if (!NativeMethods.DuplicateHandle(processHandle, handle,
            NativeMethods.GetCurrentProcess(), out nint dupHandle, 0, false, 0))
            return false;

        try
        {
            int bufferSize = 1024;
            nint buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int status = NativeMethods.NtQueryObject(dupHandle, 1, buffer, bufferSize, out _);
                if (status == NativeMethods.STATUS_SUCCESS)
                {
                    int length = Marshal.ReadInt16(buffer);
                    if (length > 0)
                    {
                        nint strPtr = Marshal.ReadIntPtr(buffer + nint.Size);
                        name = Marshal.PtrToStringUni(strPtr, length / 2);
                        return true;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(dupHandle);
        }

        return false;
    }

    private static void LogDiagnostic(string message)
    {
        try
        {
            var logPath = Path.Combine(Constants.AppDataPath, "mutex_debug.log");
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
            File.AppendAllText(logPath, entry);
        }
        catch { }
    }
}
