namespace Mutiblox.Services.Interfaces;

public interface IAntiAfkService
{
    bool IsRunning { get; }
    TimeSpan Interval { get; set; }
    void Start();
    void Stop();
}
