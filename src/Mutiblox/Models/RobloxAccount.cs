using System.Text.Json.Serialization;

namespace Mutiblox.Models;

public sealed class RobloxAccount
{
    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("encryptedCookie")]
    public string EncryptedCookie { get; set; } = string.Empty;

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    [JsonPropertyName("addedAt")]
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("lastVerified")]
    public DateTimeOffset? LastVerified { get; set; }

    [JsonIgnore]
    public bool IsVerified => LastVerified.HasValue &&
        (DateTimeOffset.UtcNow - LastVerified.Value).TotalHours < 24;

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Alias) ? Username : $"{Alias} ({Username})";

    /// <summary>
    /// UI-only: whether cookie verification has failed (expired/invalid).
    /// </summary>
    [JsonIgnore]
    public bool IsCookieExpired { get; set; }

    /// <summary>
    /// UI-only: display text for account status.
    /// </summary>
    [JsonIgnore]
    public string StatusDisplay => IsCookieExpired ? "已過期" : IsVerified ? "已驗證" : "未驗證";

    /// <summary>
    /// UI-only property for multi-select in the launcher.
    /// </summary>
    [JsonIgnore]
    public bool IsSelected { get; set; }
}
