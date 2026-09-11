using Pixely.Ui;

namespace Pixely.Tutorials.StageSwitching;

public sealed class MenuViewModel : IUiViewModel
{
    private string? _activeStage;

    public event Action? Changed;

    public string? ActiveStage
    {
        get => _activeStage;
        set
        {
            if (_activeStage == value)
            {
                return;
            }

            _activeStage = value;
            Changed?.Invoke();
        }
    }
}
