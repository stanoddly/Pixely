using Pixely.Gpu;
using Pixely.Ui;

namespace Pixely.Tutorials.FileDialogs;

/// <summary>
/// Two buttons that each open a modal dialog. The dialog blocks inside the click handler, which is
/// fine: the handler runs during input routing, and nothing else in the frame is waiting on it.
/// </summary>
public sealed class FileDialogsView : UiView<FileDialogsViewModel>
{
    private const int ButtonWidth = 180;
    private const int ButtonHeight = 48;

    private static readonly Color BackgroundColor = new(28, 30, 34, 255);

    private readonly Window _window;
    private readonly Button _open;
    private readonly Button _save;
    private readonly Label _loaded = new();
    private readonly Label _saved = new();

    public FileDialogsView(FileDialogsViewModel viewModel, WindowRegistry windowRegistry)
        : base(viewModel)
    {
        _window = windowRegistry.GetWindow();

        _open = new Button("Open file") { Width = Sizing.Fixed(ButtonWidth), Height = Sizing.Fixed(ButtonHeight) };
        _open.Clicked += () => ViewModel.LoadedFilename = FileDialogsViewModel.Describe(_window.ShowModalOpenFileDialog());

        _save = new Button("Save file") { Width = Sizing.Fixed(ButtonWidth), Height = Sizing.Fixed(ButtonHeight) };
        _save.Clicked += () => ViewModel.SavedFilename = FileDialogsViewModel.Describe(_window.ShowModalSaveFileDialog());
    }

    protected override Element Build()
    {
        // An overlay as the layer, so the column can be centred in the window by its alignment.
        return new Overlay
        {
            Background = new SolidDrawable(BackgroundColor),
            Children =
            {
                new Column(gap: 16)
                {
                    HorizontalAlignment = Alignment.Center,
                    VerticalAlignment = Alignment.Center,
                    Children =
                    {
                        Centred(_open),
                        Centred(_loaded),
                        Centred(_save),
                        Centred(_saved)
                    }
                }
            }
        };
    }

    protected override void Sync()
    {
        _loaded.Content = ViewModel.LoadedFilename;
        _saved.Content = ViewModel.SavedFilename;
    }

    private static Element Centred(Element element)
    {
        element.HorizontalAlignment = Alignment.Center;
        return element;
    }
}
