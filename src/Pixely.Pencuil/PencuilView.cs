namespace Pixely.Pencuil;

public abstract class PencuilView<TValue> : PencuilViewBase<ViewModel<TValue>> where TValue : unmanaged
{
    protected PencuilView(ViewModel<TValue> viewModel)
        : base(viewModel)
    {
    }

    protected PencuilView(ViewScope viewScope, ViewModel<TValue> viewModel)
        : base(viewScope, viewModel)
    {
    }
}
