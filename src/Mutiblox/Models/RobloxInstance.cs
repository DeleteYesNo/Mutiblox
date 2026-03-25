namespace Mutiblox.Models;

public sealed class RobloxInstance
{
    public int ProcessId { get; set; }
    public int SlotIndex { get; init; }
    public RobloxAccount Account { get; init; } = null!;
    public long? PlaceId { get; set; }
    public string? JobId { get; set; }
    public InstanceStatus Status { get; set; } = InstanceStatus.Launching;
    public DateTimeOffset LaunchedAt { get; init; } = DateTimeOffset.UtcNow;

    // Performance metrics
    public double CpuPercent { get; set; }
    public long MemoryBytes { get; set; }

    public string MemoryDisplay => MemoryBytes switch
    {
        < 1024 * 1024 => $"{MemoryBytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{MemoryBytes / (1024.0 * 1024):F1} MB",
        _ => $"{MemoryBytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}

public enum InstanceStatus
{
    Launching,
    Running,
    Exited
}
