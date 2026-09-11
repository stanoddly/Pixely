using Pixely.Ui;

namespace Pixely.Tutorials.FileDialogs;

public sealed class FileDialogsViewModel : IUiViewModel
{
    private string _loadedFilename = "none";
    private string _savedFilename = "none";

    public event Action? Changed;

    public string LoadedFilename
    {
        get => _loadedFilename;
        set => Set(ref _loadedFilename, value);
    }

    public string SavedFilename
    {
        get => _savedFilename;
        set => Set(ref _savedFilename, value);
    }

    /// <summary>Turns a dialog's outcome into the one line the view shows for it.</summary>
    public static string Describe(FileDialogResult result)
    {
        if (result.Status == FileDialogStatus.Accepted && result.Paths.Count > 0)
        {
            return result.Paths[0];
        }

        return result.Status == FileDialogStatus.Canceled ? "canceled" : result.Error ?? "error";
    }

    private void Set(ref string field, string value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        Changed?.Invoke();
    }
}
