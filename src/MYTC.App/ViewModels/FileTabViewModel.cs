using MYTC.App.Mvvm;
using System.IO;
using MYTC.Domain.Files;
using MYTC.Domain.Workspaces;

namespace MYTC.App.ViewModels;

public sealed class FileTabViewModel : ObservableObject
{
    private string _customTitle;
    private TabMode _mode;
    private string _currentPath;
    private string? _fixedPath;
    private bool _isActive;

    public FileTabViewModel(TabSnapshot snapshot)
    {
        Id = snapshot.Id;
        _customTitle = snapshot.CustomTitle;
        _mode = snapshot.Mode;
        _currentPath = snapshot.CurrentPath;
        _fixedPath = snapshot.FixedPath;
        BackHistory = [.. snapshot.BackHistory];
        ForwardHistory = [.. snapshot.ForwardHistory];
        Sort = snapshot.Sort;
    }

    public string Id { get; }

    public List<string> BackHistory { get; }

    public List<string> ForwardHistory { get; }

    public SortDescriptor Sort { get; set; }

    public string CustomTitle
    {
        get => _customTitle;
        set
        {
            if (SetProperty(ref _customTitle, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public TabMode Mode
    {
        get => _mode;
        set
        {
            if (SetProperty(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsFixed));
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public bool IsFixed => Mode == TabMode.Fixed;

    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            if (SetProperty(ref _currentPath, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string? FixedPath
    {
        get => _fixedPath;
        set
        {
            if (SetProperty(ref _fixedPath, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public string DisplayTitle
    {
        get
        {
            if (IsFixed && !string.IsNullOrWhiteSpace(CustomTitle))
            {
                return CustomTitle;
            }

            var titlePath = IsFixed && !string.IsNullOrWhiteSpace(FixedPath)
                ? FixedPath
                : CurrentPath;
            var trimmed = titlePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(folderName) ? titlePath : folderName;
        }
    }

    public TabSnapshot Capture()
    {
        return new TabSnapshot(
            Id,
            CustomTitle,
            Mode,
            CurrentPath,
            FixedPath,
            BackHistory.ToArray(),
            ForwardHistory.ToArray(),
            Sort);
    }
}
