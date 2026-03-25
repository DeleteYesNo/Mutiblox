using Mutiblox.Models;

namespace Mutiblox.Services.Interfaces;

public interface IUserSettingsService
{
    UserSettings Current { get; }
    void Load();
    void Save();
}
