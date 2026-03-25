using Mutiblox.Core;
using Mutiblox.Models;

namespace Mutiblox.Services.Interfaces;

public interface IServerJoinService
{
    Task<Result<int>> LaunchAsync(RobloxAccount account, LaunchConfiguration config, CancellationToken ct = default);
    Task<Result<List<ServerInfo>>> GetServersAsync(long placeId, CancellationToken ct = default);
    Task<Result<PrivateServerInfo>> ResolveShareLinkAsync(string shareLink, string cookie, CancellationToken ct = default);
}

public record PrivateServerInfo(long PlaceId, string AccessCode, string? LinkCode);
