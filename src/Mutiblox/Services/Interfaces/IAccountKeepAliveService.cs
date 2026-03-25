namespace Mutiblox.Services.Interfaces;

/// <summary>
/// Background service that periodically validates account cookies to prevent
/// session expiration. When a cookie is found invalid, attempts to re-import
/// from the user's browser automatically.
/// </summary>
public interface IAccountKeepAliveService
{
    bool IsRunning { get; }

    /// <summary>
    /// Raised on the calling thread when an account's cookie has been
    /// automatically refreshed (either kept alive or re-imported).
    /// </summary>
    event Action<string>? StatusChanged;

    void Start();
    void Stop();
}
