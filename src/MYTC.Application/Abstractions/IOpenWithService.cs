namespace MYTC.Application.Abstractions;

public interface IOpenWithService
{
    void Show(string filePath, nint ownerHandle);
}
