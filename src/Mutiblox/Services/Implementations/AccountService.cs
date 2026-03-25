using System.Text.Json;
using Mutiblox.Core;
using Mutiblox.Models;
using Mutiblox.Services.Interfaces;

namespace Mutiblox.Services.Implementations;

public sealed class AccountService : IAccountService
{
    private readonly IRobloxApiService _apiService;
    private readonly IEncryptionService _encryptionService;
    private readonly List<RobloxAccount> _accounts = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public IReadOnlyList<RobloxAccount> Accounts => _accounts.AsReadOnly();

    public AccountService(IRobloxApiService apiService, IEncryptionService encryptionService)
    {
        _apiService = apiService;
        _encryptionService = encryptionService;
    }

    public async Task LoadAsync()
    {
        _accounts.Clear();

        if (!File.Exists(Constants.AccountsFilePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(Constants.AccountsFilePath);
            var accounts = JsonSerializer.Deserialize<List<RobloxAccount>>(json, JsonOptions);
            if (accounts != null)
                _accounts.AddRange(accounts);
        }
        catch (Exception)
        {
            // Corrupted file - start fresh
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Constants.AppDataPath);
        var json = JsonSerializer.Serialize(_accounts, JsonOptions);
        await File.WriteAllTextAsync(Constants.AccountsFilePath, json);
    }

    public async Task<Result<RobloxAccount>> AddAccountAsync(string cookie, string? alias = null,
        CancellationToken ct = default)
    {
        // Trim whitespace
        cookie = cookie.Trim();

        // Verify the cookie works
        var userResult = await _apiService.GetUserInfoAsync(cookie, ct);
        if (!userResult.IsSuccess)
            return Result<RobloxAccount>.Failure($"Cookie 驗證失敗: {userResult.Error}");

        var (userId, username, displayName) = userResult.Value;

        // Check for duplicate
        var existing = _accounts.FindIndex(a => a.UserId == userId);
        if (existing >= 0)
        {
            // Update existing account's cookie
            _accounts[existing].EncryptedCookie = _encryptionService.Encrypt(cookie);
            _accounts[existing].Username = username;
            _accounts[existing].DisplayName = displayName;
            _accounts[existing].LastVerified = DateTimeOffset.UtcNow;
            if (alias != null)
                _accounts[existing].Alias = alias;
            await SaveAsync();
            return Result<RobloxAccount>.Success(_accounts[existing]);
        }

        var account = new RobloxAccount
        {
            UserId = userId,
            Username = username,
            DisplayName = displayName,
            EncryptedCookie = _encryptionService.Encrypt(cookie),
            Alias = alias,
            AddedAt = DateTimeOffset.UtcNow,
            LastVerified = DateTimeOffset.UtcNow
        };

        _accounts.Add(account);
        await SaveAsync();
        return Result<RobloxAccount>.Success(account);
    }

    public async Task<Result> VerifyAccountAsync(RobloxAccount account, CancellationToken ct = default)
    {
        try
        {
            var cookie = DecryptCookie(account);
            var result = await _apiService.GetUserInfoAsync(cookie, ct);

            if (result.IsSuccess)
            {
                var (_, username, displayName) = result.Value;
                account.Username = username;
                account.DisplayName = displayName;
                account.LastVerified = DateTimeOffset.UtcNow;
                await SaveAsync();
                return Result.Success();
            }

            return Result.Failure(result.Error!);
        }
        catch (Exception ex)
        {
            return Result.Failure($"驗證失敗: {ex.Message}");
        }
    }

    public void RemoveAccount(RobloxAccount account)
    {
        _accounts.Remove(account);
        _ = SaveAsync();
    }

    public string DecryptCookie(RobloxAccount account)
        => _encryptionService.Decrypt(account.EncryptedCookie);

    public async Task UpdateCookieAsync(RobloxAccount account, string newCookie)
    {
        account.EncryptedCookie = _encryptionService.Encrypt(newCookie);
        await SaveAsync();
    }

}
