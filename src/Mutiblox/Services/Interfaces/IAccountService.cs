using Mutiblox.Core;
using Mutiblox.Models;

namespace Mutiblox.Services.Interfaces;

public interface IAccountService
{
    IReadOnlyList<RobloxAccount> Accounts { get; }
    Task LoadAsync();
    Task SaveAsync();
    Task<Result<RobloxAccount>> AddAccountAsync(string cookie, string? alias = null, CancellationToken ct = default);
    Task<Result> VerifyAccountAsync(RobloxAccount account, CancellationToken ct = default);
    void RemoveAccount(RobloxAccount account);
    string DecryptCookie(RobloxAccount account);

    /// <summary>
    /// Updates the stored cookie for an account when Roblox rotates it.
    /// </summary>
    Task UpdateCookieAsync(RobloxAccount account, string newCookie);
}
