using System.Diagnostics;
using System.Threading;

namespace Mutiblox.Watcher;

/// <summary>
/// Standalone watcher process that acquires Roblox singleton mutex/event
/// to prevent single-instance enforcement.
///
/// Protocol:
/// 1. Parent launches this process
/// 2. Acquires ROBLOX_singletonEvent + ROBLOX_singletonMutex
/// 3. Writes "WATCHER_READY" to stdout
/// 4. Waits for "EXIT" command on stdin
/// 5. If stdin closes (parent crash), enters orphan mode:
///    keeps holding mutex until all Roblox processes exit.
/// </summary>
internal class Program
{
    private const string SingletonEvent = "ROBLOX_singletonEvent";
    private const string SingletonMutex = "ROBLOX_singletonMutex";
    private const string ReadySignal = "WATCHER_READY";

    static int Main()
    {
        EventWaitHandle? singletonEvent = null;
        Mutex? singletonMutex = null;

        try
        {
            // Acquire the singleton event
            singletonEvent = new EventWaitHandle(false, EventResetMode.ManualReset, SingletonEvent);

            // Acquire the singleton mutex
            singletonMutex = new Mutex(true, SingletonMutex, out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    singletonMutex.WaitOne(TimeSpan.FromSeconds(5));
                }
                catch (AbandonedMutexException)
                {
                    // Previous owner crashed — we now own it
                }
            }

            // Signal ready to parent
            Console.Out.WriteLine(ReadySignal);
            Console.Out.Flush();

            // Wait for parent commands or stdin close
            bool parentAlive = true;
            while (parentAlive)
            {
                try
                {
                    var line = Console.In.ReadLine();
                    if (line == "EXIT")
                        return 0; // Clean exit requested
                    if (line == null)
                        parentAlive = false; // Parent process died (stdin closed)
                }
                catch
                {
                    parentAlive = false;
                }
            }

            // === Orphan mode ===
            // Parent crashed — keep holding mutex until all Roblox instances exit.
            // This prevents Roblox from detecting mutex release and misbehaving.
            int noRobloxCount = 0;
            while (noRobloxCount < 6) // Exit after ~30s with no Roblox processes
            {
                Thread.Sleep(5000);

                try
                {
                    var processes = Process.GetProcessesByName("RobloxPlayerBeta");
                    bool anyRunning = processes.Length > 0;
                    foreach (var p in processes) p.Dispose();

                    noRobloxCount = anyRunning ? 0 : noRobloxCount + 1;
                }
                catch
                {
                    noRobloxCount++;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WATCHER_ERROR: {ex.Message}");
            return 1;
        }
        finally
        {
            try { singletonMutex?.ReleaseMutex(); } catch { }
            singletonMutex?.Dispose();
            singletonEvent?.Dispose();
        }
    }
}
