namespace Mutiblox.Models;

public sealed class ServerInfo
{
    public string JobId { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public int Ping { get; set; }
    public double Fps { get; set; }

    public string PlayerDisplay => $"{PlayerCount}/{MaxPlayers}";
    public bool IsFull => PlayerCount >= MaxPlayers;
}
