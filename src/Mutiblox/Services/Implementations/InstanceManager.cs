using System.Diagnostics;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class InstanceManager : IInstanceManager, IDisposable
{
    private readonly List<RobloxInstance> _instances = [];
    private readonly object _lock = new();
    private readonly Timer _refreshTimer;

    public IReadOnlyList<RobloxInstance> Instances
    {
        get
        {
            lock (_lock) return _instances.ToList().AsReadOnly();
        }
    }

    public event Action? InstancesChanged;

    public InstanceManager()
    {
        _refreshTimer = new Timer(_ => RefreshInstances(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void TrackInstance(RobloxInstance instance)
    {
        lock (_lock)
        {
            _instances.Add(instance);
        }
        InstancesChanged?.Invoke();
    }

    public void StopInstance(int processId)
    {
        try
        {
            var proc = Process.GetProcessById(processId);
            proc.Kill();
            proc.Dispose();
        }
        catch (ArgumentException)
        {
            // Process already exited
        }

        lock (_lock)
        {
            var instance = _instances.Find(i => i.ProcessId == processId);
            if (instance != null)
                instance.Status = InstanceStatus.Exited;
        }

        InstancesChanged?.Invoke();
    }

    public void StopAll()
    {
        List<RobloxInstance> snapshot;
        lock (_lock)
        {
            snapshot = [.. _instances];
        }

        foreach (var instance in snapshot)
        {
            try
            {
                var proc = Process.GetProcessById(instance.ProcessId);
                proc.Kill();
                proc.Dispose();
            }
            catch (ArgumentException) { }
        }

        lock (_lock)
        {
            foreach (var instance in _instances)
                instance.Status = InstanceStatus.Exited;
        }

        InstancesChanged?.Invoke();
    }

    public void RefreshInstances()
    {
        bool changed = false;

        lock (_lock)
        {
            foreach (var instance in _instances.Where(i => i.Status != InstanceStatus.Exited))
            {
                try
                {
                    Process.GetProcessById(instance.ProcessId).Dispose();
                }
                catch (ArgumentException)
                {
                    instance.Status = InstanceStatus.Exited;
                    changed = true;
                }
            }

            // Clean up old exited instances (older than 5 minutes)
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-5);
            changed |= _instances.RemoveAll(i =>
                i.Status == InstanceStatus.Exited && i.LaunchedAt < cutoff) > 0;
        }

        if (changed)
            InstancesChanged?.Invoke();
    }

    public void Dispose()
    {
        _refreshTimer.Dispose();
    }
}
