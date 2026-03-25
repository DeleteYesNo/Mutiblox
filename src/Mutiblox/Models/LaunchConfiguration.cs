namespace Mutiblox.Models;

public sealed class LaunchConfiguration
{
    public long PlaceId { get; set; }
    public string? JobId { get; set; }
    public string? AccessCode { get; set; }
    public long? FollowUserId { get; set; }
    public GraphicsPreset GraphicsPreset { get; set; } = GraphicsPreset.Default;
    public int? FpsLimit { get; set; }
    public LaunchMode LaunchMode { get; set; } = LaunchMode.Normal;
}

public enum LaunchMode
{
    Normal,
    JoinServer,
    VipServer,
    FollowPlayer
}
