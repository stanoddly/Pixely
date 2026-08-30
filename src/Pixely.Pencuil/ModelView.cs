namespace Pixely.Pencuil;

public class ModelView<TValue> : IPencuilViewModel where TValue : unmanaged
{
    private TValue _value;

    public bool Dirty { get; protected set; }

    public TValue Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<TValue>.Default.Equals(_value, value))
            {
                return;
            }

            _value = value;
            Dirty = true;
        }
    }

    bool IPencuilViewModel.IsDirty
    {
        get => Dirty;
        set => Dirty = value;
    }

    public ModelView()
    {
    }

    public ModelView(TValue value)
    {
        _value = value;
    }

    public ref readonly TValue GetValue()
    {
        return ref _value;
    }

    public void SetValue(in TValue value)
    {
        _value = value;
        Dirty = true;
    }
}
