using Mutiblox.Core;

namespace Mutiblox.Services.Interfaces;

public interface IBrowserCookieService
{
    Result<List<BrowserCookieEntry>> GetRobloxCookies();
}

public record BrowserCookieEntry(string BrowserName, string CookieValue);
