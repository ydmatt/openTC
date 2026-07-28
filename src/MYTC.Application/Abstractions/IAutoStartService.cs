namespace MYTC.Application.Abstractions;

public interface IAutoStartService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
