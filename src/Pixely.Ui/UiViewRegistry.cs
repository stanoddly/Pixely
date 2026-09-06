using Pixely.DependencyInjection;

namespace Pixely.Ui;

/// <summary>
/// Adds every <see cref="UiView"/> the container builds to the root for its window, and takes it
/// away again when it is disposed.
/// </summary>
/// <remarks>
/// <para>
/// So that registering a view is one line beside everything else it needs, rather than a line of
/// registration and a second line somewhere else remembering to add it to a root. Which root is the
/// view's own answer, through <see cref="IUiView.ViewScope"/> — an application with a window per
/// screen registers the same way for all of them.
/// </para>
/// <para>
/// Views are held until there is a provider to resolve their root from, because nothing says a view
/// is built after this is: the container decides the order, and a view that arrived first would
/// otherwise be silently dropped.
/// </para>
/// <para>
/// Register views as singletons. The container only keeps hold of a transient it has to dispose, so
/// a transient view would be added to a root and never taken away again. Only the container taking a
/// view down is noticed; disposing one by hand is not.
/// </para>
/// <para>
/// A view has to derive from <see cref="UiView"/> to be found. <see cref="IUiView"/> is the role an
/// application registers under, not an alternative to inheriting the behaviour.
/// </para>
/// </remarks>
internal sealed class UiViewRegistry
{
    // The root each view went to, rather than the scope it asked for. Disposal is where this is read,
    // and by then the provider has marked itself disposed and will refuse to resolve anything.
    private readonly List<(UiView View, UiRoot? Root)> _views = new();

    private ServiceProvider? _provider;

    internal static UiViewRegistry Register(ServiceCollection services)
    {
        UiViewRegistry registry = new();

        services.OnActivated((instance, _) =>
        {
            if (instance is UiView view)
            {
                registry.Add(view);
            }
        });

        services.OnDisposing((instance, _) =>
        {
            if (instance is UiView view)
            {
                registry.Remove(view);
            }
        });

        return registry;
    }

    /// <summary>Hands over the provider roots are resolved from, and adds whatever was waiting.</summary>
    internal UiViewRegistry Bind(ServiceProvider provider)
    {
        _provider = provider;

        // Indexed rather than foreach: attaching a view can build another one, which lands here.
        for (int i = 0; i < _views.Count; i++)
        {
            (UiView view, UiRoot? root) = _views[i];

            if (root == null)
            {
                Record(view, AddToRoot(view));
            }
        }

        return this;
    }

    private void Add(UiView view)
    {
        foreach ((UiView existing, UiRoot? _) in _views)
        {
            if (ReferenceEquals(existing, view))
            {
                return;
            }
        }

        _views.Add((view, null));

        // Recorded first and filled in afterwards, because attaching is what builds the tree and a
        // tree can ask the container for another view. That one lands here too, so by the time this
        // returns the entry to write to is no longer the last one — or even at the same index.
        UiRoot? root = AddToRoot(view);
        Record(view, root);
    }

    private void Record(UiView view, UiRoot? root)
    {
        for (int i = 0; i < _views.Count; i++)
        {
            if (ReferenceEquals(_views[i].View, view))
            {
                _views[i] = (view, root);
                return;
            }
        }

        // The entry went while this view was attaching, which is what happens when attaching it is
        // what took the container down. It has just been put on a root that nothing is left to take
        // it off, so it comes off now.
        root?.RemoveView(view);
    }

    private void Remove(UiView view)
    {
        for (int i = 0; i < _views.Count; i++)
        {
            (UiView existing, UiRoot? root) = _views[i];

            if (!ReferenceEquals(existing, view))
            {
                continue;
            }

            _views.RemoveAt(i);
            root?.RemoveView(view);
            return;
        }
    }

    private UiRoot? AddToRoot(UiView view)
    {
        if (_provider == null || view.IsAttached)
        {
            return null;
        }

        UiRoot root = ScopedUiRoot.GetRequired(_provider, view.ViewScope).Root;
        root.AddView(view);
        return root;
    }
}
