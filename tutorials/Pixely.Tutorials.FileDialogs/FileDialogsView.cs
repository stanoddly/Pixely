using Pixely.Gpu;
using Pixely.Pencuil;
using Pixely.Text;

namespace Pixely.Tutorials.FileDialogs;

public class FileDialogsViewModel : IPencuilViewModel
{
    public bool IsDirty { get; set; } = true;

    private string _loadedFilename = "none";
    private string _savedFilename = "none";

    public string LoadedFilename
    {
        get
        {
            return _loadedFilename;
        }
        set
        {
            if (_loadedFilename != value)
            {
                _loadedFilename = value;
                IsDirty = true;
            }
        }
    }

    public string SavedFilename
    {
        get
        {
            return _savedFilename;
        }
        set
        {
            if (_savedFilename != value)
            {
                _savedFilename = value;
                IsDirty = true;
            }
        }
    }
}

public class FileDialogsView : PencuilViewBase<FileDialogsViewModel>
{
    private const int ButtonWidth = 180;
    private const int ButtonHeight = 48;
    private const int ContentWidth = 720;
    private const int ButtonGap = 86;
    private const int ValueGap = 16;

    private static readonly Color BackgroundColor = new(28, 30, 34, 255);
    private static readonly Color TextColor = new(235, 238, 242, 255);
    private readonly Window _window;
    private readonly Font _font;

    public FileDialogsView(
        FileDialogsViewModel viewModel,
        WindowRegistry windowRegistry,
        IFontSystem fontSystem)
        : base(viewModel)
    {
        _window = windowRegistry.GetWindow();
        _font = fontSystem.Load("fonts/GohuFont-Medium.ttf", 16);
    }

    public override void Build(Pencil pencil)
    {
        pencil.MoveTo(0, 0);
        pencil.Rectangle(pencil.BottomRight.X, pencil.BottomRight.Y, BackgroundColor);

        int startX = pencil.Center.X - ButtonWidth / 2;
        int startY = pencil.Center.Y - ButtonHeight - ButtonGap / 2;

        BuildOpenColumn(pencil, startX, startY);
        BuildSaveColumn(pencil, startX, startY + ButtonHeight + ButtonGap);
    }

    private void BuildOpenColumn(Pencil pencil, int x, int y)
    {
        pencil.MoveTo(x, y);
        if (pencil.Button("Open file", _font, ButtonWidth, ButtonHeight))
        {
            FileDialogResult result = _window.ShowModalOpenFileDialog();

            if (result.Status == FileDialogStatus.Accepted && result.Paths.Count > 0)
            {
                ViewModel.LoadedFilename = result.Paths[0];
            }
            else if (result.Status == FileDialogStatus.Canceled)
            {
                ViewModel.LoadedFilename = "canceled";
            }
            else
            {
                ViewModel.LoadedFilename = result.Error ?? "error";
            }
        }

        DrawValue(pencil, y + ButtonHeight + ValueGap, ViewModel.LoadedFilename);
    }

    private void BuildSaveColumn(Pencil pencil, int x, int y)
    {
        pencil.MoveTo(x, y);
        if (pencil.Button("Save file", _font, ButtonWidth, ButtonHeight))
        {
            FileDialogResult result = _window.ShowModalSaveFileDialog();

            if (result.Status == FileDialogStatus.Accepted && result.Paths.Count > 0)
            {
                ViewModel.SavedFilename = result.Paths[0];
            }
            else if (result.Status == FileDialogStatus.Canceled)
            {
                ViewModel.SavedFilename = "canceled";
            }
            else
            {
                ViewModel.SavedFilename = result.Error ?? "error";
            }
        }

        DrawValue(pencil, y + ButtonHeight + ValueGap, ViewModel.SavedFilename);
    }

    private void DrawValue(Pencil pencil, int y, string text)
    {
        Vector2Int textSize = pencil.MeasureText(text, _font);
        int x = pencil.Center.X - Math.Min(textSize.X, ContentWidth) / 2;

        pencil.MoveTo(x, y);
        pencil.Text(text, _font, TextColor);
    }
}
