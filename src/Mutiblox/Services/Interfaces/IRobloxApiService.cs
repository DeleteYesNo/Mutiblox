using Mutiblox.Core;

namespace Mutiblox.Services.Interfaces;

/// <summary>
/// Result from GetAuthTicketAsync that includes the ticket and any rotated cookie.
/// </summary>
public record AuthTicketResult(string Ticket, string? NewCookie);

public interface IRobloxApiService
{
    Task<Result<string>> GetCsrfTokenAsync(string cookie, CancellationToken ct = default);
    Task<Result<AuthTicketResult>> GetAuthTicketAsync(string cookie, CancellationToken ct = default);
    Task<Result<(long UserId, string Username, string DisplayName)>> GetUserInfoAsync(string cookie, CancellationToken ct = default);
    Task<Result<string>> GetThumbnailUrlAsync(long userId, CancellationToken ct = default);
}
