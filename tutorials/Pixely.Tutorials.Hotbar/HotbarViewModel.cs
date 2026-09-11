using Pixely.Ui;

namespace Pixely.Tutorials.Hotbar;

public sealed class HotbarViewModel : IUiViewModel
{
    public const int SlotCount = 9;

    private int _selectedSlot;

    public event Action? Changed;

    public int SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (_selectedSlot == value)
            {
                return;
            }

            _selectedSlot = value;
            Changed?.Invoke();
        }
    }
}
