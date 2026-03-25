using Mutiblox.Core;

namespace Mutiblox.Services.Interfaces;

public interface IMutexService
{
    bool IsMultiInstanceEnabled { get; }
    Task<Result> EnableMultiInstanceAsync(CancellationToken ct = default);
    void Disable();
}
