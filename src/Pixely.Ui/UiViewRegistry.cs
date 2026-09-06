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
/// view's own answer, through <see cref="UiView.ViewScope"/> — an application with a window per
/// screen registers the same way for all of them.
/// </para>
/// <para>
/// Views are held until there is a provider to resolve their root from, because nothing says a view
/// is built after this is: the container decides the order, and a view that arrived first would
/// otherwise be silently dropped.
/// </para>
/// </remarks>
internal sealed class UiViewRegistry
{
    private readonly List<UiView> _views = new();

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
            AddToRoot(_views[i]);
        }

        return this;
    }

    private void Add(UiView view)
    {
        foreach (UiView existing in _views)
        {
            if (ReferenceEquals(existing, view))
            {
                return;
            }
        }

        _views.Add(view);
        AddToRoot(view);
    }

    private void Remove(UiView view)
    {
        if (!_views.Remove(view) || _provider == null || !view.IsAttached)
        {
            return;
        }

        ScopedUiRoot.GetRequired(_provider, view.ViewScope).Root.RemoveView(view);
    }

    private void AddToRoot(UiView view)
    {
        if (_provider == null || view.IsAttached)
        {
            return;
        }

        ScopedUiRoot.GetRequired(_provider, view.ViewScope).Root.AddView(view);
    }
}
