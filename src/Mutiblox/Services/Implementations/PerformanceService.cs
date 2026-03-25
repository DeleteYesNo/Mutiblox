using System.Diagnostics;
using Mutiblox.Core;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class PerformanceService : IPerformanceService, IDisposable
{
    private Timer? _timer;
    private readonly IInstanceManager _instanceManager;

    public event Action? MetricsUpdated;

    public PerformanceService(IInstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
    }

    public void StartMonitoring()
    {
        _timer?.Dispose();
        _timer = new Timer(
            async _ => await UpdateMetricsAsync(_instanceManager.Instances),
            null,
            Constants.PerformanceUpdateInterval,
            Constants.PerformanceUpdateInterval);
    }

    public void StopMonitoring()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public Task UpdateMetricsAsync(IEnumerable<RobloxInstance> instances)
    {
        foreach (var instance in instances.Where(i => i.Status == InstanceStatus.Running))
        {
            try
            {
                using var proc = Process.GetProcessById(instance.ProcessId);
                instance.MemoryBytes = proc.WorkingSet64;

                // Simple CPU estimation using total processor time
                var totalTime = proc.TotalProcessorTime.TotalMilliseconds;
                var uptime = (DateTimeOffset.UtcNow - instance.LaunchedAt).TotalMilliseconds;
                if (uptime > 0)
                {
                    instance.CpuPercent = (totalTime / uptime / Environment.ProcessorCount) * 100;
                }
            }
            catch (ArgumentException)
            {
                instance.Status = InstanceStatus.Exited;
            }
            catch (InvalidOperationException)
            {
                // Process exited between check and metric read
            }
        }

        MetricsUpdated?.Invoke();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
