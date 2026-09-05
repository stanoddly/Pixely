namespace Pixely.Ui;

/// <summary>
/// State a view renders, which announces when it has changed. Unlike Pencuil's dirty flag there is
/// nothing to poll: a retained tree only needs telling when to push new values into it.
/// </summary>
public interface IUiViewModel
{
    event Action? Changed;
}

/// <summary>
/// A view over a view model. The element tree is built once and afterwards the view only assigns
/// to the elements it kept, which is the whole point of retaining the tree.
/// </summary>
public abstract class UiView
{
    private Element? _root;

    /// <summary>The built tree. Available once the view has been added to a <see cref="UiRoot"/>.</summary>
    public Element Root => _root ?? throw new InvalidOperationException(
        $"{GetType().Name} has not been attached yet. Add it to a UiRoot before using its Root.");

    internal bool IsAttached => _root != null;

    internal void Attach()
    {
        if (_root != null)
        {
            throw new InvalidOperationException($"{GetType().Name} is already attached.");
        }

        _root = BuildRoot();
        Subscribe();
        Synchronize();
    }

    internal void Detach()
    {
        Unsubscribe();
        _root = null;
    }

    private protected abstract Element BuildRoot();
    private protected abstract void Subscribe();
    private protected abstract void Unsubscribe();
    private protected abstract void Synchronize();
}

/// <inheritdoc cref="UiView"/>
/// <typeparam name="TViewModel">The view model this view reads.</typeparam>
public abstract class UiView<TViewModel> : UiView
    where TViewModel : IUiViewModel
{
    /// <summary>
    /// Constructors stay assignment only. <see cref="Build"/> and <see cref="Sync"/> are virtual,
    /// so calling them here would run before a derived class had initialised its own fields; they
    /// run on attach instead.
    /// </summary>
    protected UiView(TViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
    }

    protected TViewModel ViewModel { get; }

    /// <summary>
    /// Builds the element tree. Called exactly once, when the view is attached. Keep references to
    /// the elements <see cref="Sync"/> writes to.
    /// </summary>
    protected abstract Element Build();

    /// <summary>
    /// Copies the view model into the tree. Called once on attach and then only when the view model
    /// reports a change — never per frame.
    /// </summary>
    protected abstract void Sync();

    private protected sealed override Element BuildRoot() => Build();

    private protected sealed override void Subscribe() => ViewModel.Changed += Sync;

    private protected sealed override void Unsubscribe() => ViewModel.Changed -= Sync;

    private protected sealed override void Synchronize() => Sync();
}
