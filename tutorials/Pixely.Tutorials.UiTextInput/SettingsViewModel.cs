using Pixely.Ui;

namespace Pixely.Tutorials.UiTextInput;

/// <summary>
/// The values the fields edit. It knows nothing about elements: it raises <see cref="Changed"/>,
/// and the view decides what that means on screen.
/// </summary>
public sealed class SettingsViewModel : IUiViewModel
{
    private string _name = "Player";
    private int _width = 64;
    private int _height = 48;
    private float _scale = 1f;
    private bool _isLocked;

    public event Action? Changed;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public int Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public int Height
    {
        get => _height;
        set => Set(ref _height, value);
    }

    public float Scale
    {
        get => _scale;
        set => Set(ref _scale, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => Set(ref _isLocked, value);
    }

    /// <summary>
    /// Writes the fields directly and reports one change, rather than four assignments reporting
    /// four. A view synchronises on every notification, so batching an action that moves several
    /// values at once is the view model's job.
    /// </summary>
    public void Reset()
    {
        if (_name == "Player" && _width == 64 && _height == 48 && _scale == 1f)
        {
            return;
        }

        _name = "Player";
        _width = 64;
        _height = 48;
        _scale = 1f;
        Changed?.Invoke();
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Changed?.Invoke();
    }
}
