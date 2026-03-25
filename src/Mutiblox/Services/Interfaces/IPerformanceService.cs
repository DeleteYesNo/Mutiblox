using Mutiblox.Models;

namespace Mutiblox.Services.Interfaces;

public interface IPerformanceService
{
    event Action? MetricsUpdated;
    void StartMonitoring();
    void StopMonitoring();
    Task UpdateMetricsAsync(IEnumerable<RobloxInstance> instances);
}
