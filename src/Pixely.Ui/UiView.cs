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
/// A view over one or more view models. The element tree is built once and afterwards the view only
/// assigns to the elements it kept, which is the whole point of retaining the tree.
/// </summary>
/// <remarks>
/// Subscription is the base class's business, not a derived one's: <see cref="Observe"/> is how a
/// view says which models it reads, and everything about when those subscriptions start and stop
/// belongs to attach and detach. A view that also has to subscribe to something that is not a view
/// model does it in <see cref="OnAttached"/>.
/// </remarks>
public abstract class UiView
{
    private readonly List<IUiViewModel> _viewModels = new();

    private Element? _root;
    private bool _isSubscribed;

    /// <summary>The built tree. Available once the view has been added to a <see cref="UiRoot"/>.</summary>
    public Element Root => _root ?? throw new InvalidOperationException(
        $"{GetType().Name} has not been attached yet. Add it to a UiRoot before using its Root.");

    /// <summary>Which window this view belongs to, for whatever adds it to a root on the view's behalf.</summary>
    public virtual ViewScope ViewScope => default;

    internal bool IsAttached => _root != null;

    /// <summary>
    /// Reads <paramref name="viewModel"/> from here on, so a change to it syncs this view. Registered
    /// for the view's lifetime rather than the attachment's, because a view usually says what it
    /// reads once, in its constructor, and detaching must not lose that.
    /// </summary>
    protected void Observe(IUiViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        foreach (IUiViewModel observed in _viewModels)
        {
            if (ReferenceEquals(observed, viewModel))
            {
                return;
            }
        }

        _viewModels.Add(viewModel);

        // A model named while already attached still has to be heard from, or whether a view syncs
        // would depend on when it happened to say so.
        if (_isSubscribed)
        {
            viewModel.Changed += Synchronize;
        }
    }

    /// <summary>Builds the element tree. Called exactly once per attachment.</summary>
    protected abstract Element BuildRoot();

    /// <summary>Copies the view models into the tree.</summary>
    protected abstract void Synchronize();

    /// <summary>
    /// Runs once the tree exists and the view models are subscribed, before the first sync. Where a
    /// view subscribes to anything else it needs — the root's pointer position, for something that
    /// follows the cursor.
    /// </summary>
    protected virtual void OnAttached()
    {
    }

    /// <summary>Undoes <see cref="OnAttached"/>. Runs even if attaching failed partway.</summary>
    protected virtual void OnDetached()
    {
    }

    /// <summary>
    /// Builds, subscribes, and syncs, in that order. Any step throwing undoes the ones before it, so
    /// a view that failed to attach is not left half-attached and still listening.
    /// </summary>
    internal void Attach()
    {
        if (_root != null)
        {
            throw new InvalidOperationException($"{GetType().Name} is already attached.");
        }

        try
        {
            _root = BuildRoot();
            Subscribe();
            OnAttached();
            Synchronize();
        }
        catch
        {
            Detach();
            throw;
        }
    }

    internal void Detach()
    {
        try
        {
            OnDetached();
        }
        finally
        {
            Unsubscribe();
            _root = null;
        }
    }

    private void Subscribe()
    {
        _isSubscribed = true;

        foreach (IUiViewModel viewModel in _viewModels)
        {
            viewModel.Changed += Synchronize;
        }
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed)
        {
            return;
        }

        _isSubscribed = false;

        foreach (IUiViewModel viewModel in _viewModels)
        {
            viewModel.Changed -= Synchronize;
        }
    }
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
        Observe(viewModel);
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

    protected sealed override Element BuildRoot() => Build();

    protected sealed override void Synchronize() => Sync();
}
