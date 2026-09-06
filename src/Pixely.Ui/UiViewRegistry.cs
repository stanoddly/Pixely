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
/// a transient view would be added to a root and never taken away again.
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
                _views[i] = (view, AddToRoot(view));
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
        _views[^1] = (view, AddToRoot(view));
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
