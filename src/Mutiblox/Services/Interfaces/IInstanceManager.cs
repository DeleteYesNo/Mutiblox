using Mutiblox.Models;

namespace Mutiblox.Services.Interfaces;

public interface IInstanceManager
{
    IReadOnlyList<RobloxInstance> Instances { get; }
    event Action? InstancesChanged;
    void TrackInstance(RobloxInstance instance);
    void StopInstance(int processId);
    void StopAll();
    void RefreshInstances();
}
