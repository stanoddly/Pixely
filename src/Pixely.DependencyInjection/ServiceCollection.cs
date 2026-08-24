using System.Diagnostics.CodeAnalysis;

namespace Pixely.DependencyInjection;

/// <summary>Collects service registrations and builds a <see cref="ServiceProvider"/> with singleton services eagerly resolved and transient services resolved on demand.</summary>
public class ServiceCollection
{
    private readonly ServiceProvider? _parent;
    private readonly HashSet<int> _registeredTypeIds = new();
    private readonly Dictionary<int, List<ServiceDescriptor>> _serviceGroups = new();
    private readonly List<Action<ServiceProvider>> _onStartActions = new();
    private readonly List<ServiceActivatedCallback> _activatedCallbacks = new();
    private readonly List<ServiceDisposingCallback> _disposingCallbacks = new();

    public ServiceCollection()
    {
    }

    internal ServiceCollection(ServiceProvider parent)
    {
        _parent = parent;
    }

    /// <summary>Registers <typeparamref name="T"/> as a singleton, constructing it via its single public constructor with dependencies resolved from the provider.</summary>
    /// <typeparam name="T">The concrete service type to register. Must be a named concrete type that does not contain type parameters from a generic calling scope.</typeparam>
    /// <remarks>This overload is intercepted by the source generator at each call site. Unsupported implementation types produce compile-time error <c>GK0001</c>.</remarks>
    /// <exception cref="InvalidOperationException">Thrown at runtime if the source generator did not intercept this call, such as when the generator is not referenced.</exception>
    public void AddSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>() where T : class
    {
        throw new InvalidOperationException(
            $"AddSingleton<{typeof(T).Name}>() was not intercepted by the source generator. Ensure the Pixely.DependencyInjection.Generator is referenced.");
    }

    /// <summary>
    /// Registers <typeparamref name="T"/> under the service type <typeparamref name="TService"/>.
    /// When <typeparamref name="T"/> is assignable to <typeparamref name="TService"/>, constructs <typeparamref name="T"/> via its single public constructor with dependencies resolved from the provider.
    /// When <typeparamref name="T"/> is not assignable to <typeparamref name="TService"/>, resolves <typeparamref name="T"/> from the provider and invokes the single accessible instance method on <typeparamref name="T"/> that returns <typeparamref name="TService"/>, with its parameters resolved from the provider.
    /// </summary>
    /// <typeparam name="TService">The service type (interface or base class) under which the instance is resolved.</typeparam>
    /// <typeparam name="T">Either the concrete implementation type (when assignable to <typeparamref name="TService"/>), or a factory type with an instance method returning <typeparamref name="TService"/>. Constructor implementations must be named concrete types that do not contain type parameters from a generic calling scope.</typeparam>
    /// <remarks>This overload is intercepted by the source generator at each call site. Unsupported constructor implementation types produce compile-time error <c>GK0001</c>.</remarks>
    /// <exception cref="InvalidOperationException">Thrown at runtime if the source generator did not intercept this call, such as when the generator is not referenced.</exception>
    public void AddSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>()
        where TService : class
        where T : class
    {
        throw new InvalidOperationException(
            $"AddSingleton<{typeof(TService).Name}, {typeof(T).Name}>() was not intercepted by the source generator. Ensure the Pixely.DependencyInjection.Generator is referenced.");
    }

    /// <summary>Registers an already-constructed instance as the singleton for <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service type under which the instance is registered.</typeparam>
    /// <param name="instance">The pre-constructed instance to register.</param>
    public void AddSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>(T instance) where T : class
    {
        ServiceDescriptor descriptor = ServiceDescriptor.ForInstance(instance);
        RegisterDescriptor(ServiceTypeId<T>.Id, descriptor);
    }

    /// <summary>Registers a factory delegate for <typeparamref name="T"/> whose parameters are resolved as services from the provider.</summary>
    /// <typeparam name="T">The service type to register. Must be a named concrete type at the call site, not a type parameter.</typeparam>
    /// <param name="factory">A static method group or lambda whose parameter types are all registered services. A null result contributes no service.</param>
    /// <remarks>This overload is intercepted by the source generator at each call site. The type argument must be a named concrete type — passing a type parameter prevents interception and causes the method to throw at runtime. Use the <see cref="AddSingleton{T}(Func{ServiceProvider,T})"/> overload when <typeparamref name="T"/> is a type parameter.</remarks>
    /// <exception cref="InvalidOperationException">Thrown at runtime if the source generator did not intercept this call — either because the generator is not referenced or because <typeparamref name="T"/> is a type parameter at the call site.</exception>
    public void AddSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>(Delegate factory) where T : class
    {
        throw new InvalidOperationException(
            $"AddSingleton<{typeof(T).Name}>(Delegate) was not intercepted by the source generator. Ensure the Pixely.DependencyInjection.Generator is referenced.");
    }

    /// <summary>
    /// Registers a typed factory that produces <typeparamref name="TImpl"/> instances under the service type
    /// <typeparamref name="TService"/>. Activation and disposal callbacks receive <c>typeof(TImpl)</c>.
    /// </summary>
    /// <typeparam name="TService">The service type (interface or base class) under which the instance is resolved.</typeparam>
    /// <typeparam name="TImpl">The concrete implementation type produced by <paramref name="factory"/>.</typeparam>
    /// <param name="factory">A delegate that receives the <see cref="ServiceProvider"/> and returns the constructed instance, or null to contribute no service.</param>
    public void AddSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TImpl>(
        Func<ServiceProvider, TImpl?> factory)
        where TService : class
        where TImpl : class, TService
    {
        ServiceDescriptor descriptor = ServiceDescriptor.ForTypedFactoryWithConcreteType<TService, TImpl>(factory);
        RegisterDescriptor(ServiceTypeId<TService>.Id, descriptor);
    }

    /// <summary>Registers a typed factory delegate that receives the <see cref="ServiceProvider"/> directly and returns the singleton instance for <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service type to register.</typeparam>
    /// <param name="factory">A delegate that receives the <see cref="ServiceProvider"/> and returns the constructed instance, or null to contribute no service.</param>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;WorldMap&gt;(static sp =&gt;
    /// {
    ///     MapLoader loader = sp.GetRequiredService&lt;MapLoader&gt;();
    ///     return loader.LoadDefault();
    /// });
    /// </code>
    /// </example>
    public void AddSingleton<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>(Func<ServiceProvider, T?> factory) where T : class
    {
        ServiceDescriptor descriptor = ServiceDescriptor.ForTypedFactory(factory);
        RegisterDescriptor(ServiceTypeId<T>.Id, descriptor);
    }

    /// <summary>Registers <typeparamref name="T"/> as a transient, constructing a new instance for each resolution.</summary>
    /// <typeparam name="T">The concrete service type to register. Must be a named concrete type that does not contain type parameters from a generic calling scope.</typeparam>
    /// <remarks>This overload is intercepted by the source generator at each call site. Unsupported implementation types produce compile-time error <c>GK0001</c>.</remarks>
    public void AddTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>() where T : class
    {
        throw new InvalidOperationException(
            $"AddTransient<{typeof(T).Name}>() was not intercepted by the source generator. Ensure the Pixely.DependencyInjection.Generator is referenced.");
    }

    /// <summary>Registers <typeparamref name="T"/> under <typeparamref name="TService"/> as a transient.</summary>
    /// <typeparam name="TService">The service type under which instances are resolved.</typeparam>
    /// <typeparam name="T">The concrete implementation or factory type. Constructor implementations must be named concrete types that do not contain type parameters from a generic calling scope.</typeparam>
    /// <remarks>This overload is intercepted by the source generator at each call site. Unsupported constructor implementation types produce compile-time error <c>GK0001</c>.</remarks>
    public void AddTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>()
        where TService : class
        where T : class
    {
        throw new InvalidOperationException(
            $"AddTransient<{typeof(TService).Name}, {typeof(T).Name}>() was not intercepted by the source generator. Ensure the Pixely.DependencyInjection.Generator is referenced.");
    }

    /// <summary>Registers a factory delegate for <typeparamref name="T"/> whose parameters are resolved from the provider each time the service is requested.</summary>
    /// <typeparam name="T">The service type to register.</typeparam>
    /// <param name="factory">A static method group or lambda whose parameter types are all registered services. A null result contributes no service for that resolution.</param>
    /// <remarks>This overload is intercepted by the source generator at each call site.</remarks>
    public void AddTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>(Delegate factory) where T : class
    {
        throw new InvalidOperationException(
            $"AddTransient<{typeof(T).Name}>(Delegate) was not intercepted by the source generator. Ensure the Pixely.DependencyInjection.Generator is referenced.");
    }

    /// <summary>Registers a typed transient factory that receives the <see cref="ServiceProvider"/> directly.</summary>
    /// <typeparam name="T">The service type to register.</typeparam>
    /// <param name="factory">A delegate that receives the <see cref="ServiceProvider"/> and returns a new instance, or null to contribute no service for that resolution.</param>
    public void AddTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>(Func<ServiceProvider, T?> factory) where T : class
    {
        ServiceDescriptor descriptor = ServiceDescriptor.ForTransientTypedFactory(factory);
        RegisterDescriptor(ServiceTypeId<T>.Id, descriptor);
    }

    /// <summary>
    /// Registers a typed transient factory that produces <typeparamref name="TImpl"/> instances under
    /// <typeparamref name="TService"/>. Activation and disposal callbacks receive <c>typeof(TImpl)</c>.
    /// </summary>
    public void AddTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TImpl>(
        Func<ServiceProvider, TImpl?> factory)
        where TService : class
        where TImpl : class, TService
    {
        ServiceDescriptor descriptor = ServiceDescriptor.ForTransientTypedFactoryWithConcreteType<TService, TImpl>(factory);
        RegisterDescriptor(ServiceTypeId<TService>.Id, descriptor);
    }

    /// <summary>Registers a callback that runs after all services are constructed but before the provider is frozen.</summary>
    /// <param name="action">The callback to invoke with the fully constructed <see cref="ServiceProvider"/>.</param>
    public void OnStart(Action<ServiceProvider> action)
    {
        _onStartActions.Add(action);
    }

    /// <summary>Makes <typeparamref name="TService"/> resolve to the same instance as the already-registered <typeparamref name="TImplementation"/>.</summary>
    /// <typeparam name="TService">The alias service type (interface or base class) to register.</typeparam>
    /// <typeparam name="TImplementation">The concrete type whose existing instance will be shared. Must already be registered.</typeparam>
    /// <exception cref="InvalidOperationException">Thrown if <typeparamref name="TImplementation"/> has not been registered before calling this method.</exception>
    public void AddAlias<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        if (!_registeredTypeIds.Contains(ServiceTypeId<TImplementation>.Id))
        {
            throw new InvalidOperationException($"{typeof(TImplementation).Name} has not been registered first.");
        }

        ServiceDescriptor sourceDescriptor = _serviceGroups[ServiceTypeId<TImplementation>.Id][^1];
        ServiceDescriptor descriptor = sourceDescriptor.Lifetime == ServiceLifetime.Transient
            ? ServiceDescriptor.ForTransientTypedFactoryWithConcreteType<TService, TImplementation>(
                static sp => sp.GetService<TImplementation>())
            : ServiceDescriptor.ForAlias<TService, TImplementation>();
        RegisterDescriptor(ServiceTypeId<TService>.Id, descriptor);
    }

    private void RegisterDescriptor(int id, ServiceDescriptor descriptor)
    {
        _registeredTypeIds.Add(id);

        if (!_serviceGroups.TryGetValue(id, out List<ServiceDescriptor>? group))
        {
            group = new List<ServiceDescriptor>();
            _serviceGroups[id] = group;
        }

        group.Add(descriptor);
    }

    /// <summary>Registers a callback whose parameters are resolved as services, invoked after all services are constructed but before the provider is frozen.</summary>
    /// <param name="action">A delegate whose parameter types are all registered services.</param>
    /// <remarks>This overload is intercepted by the source generator at each call site. The delegate argument must be resolvable at compile time — otherwise the method throws at runtime.</remarks>
    /// <exception cref="InvalidOperationException">Thrown at runtime if the source generator did not intercept this call — either because the generator is not referenced or because the delegate is not resolvable at compile time.</exception>
    public void OnStart(Delegate action)
    {
        throw new InvalidOperationException(
            "OnStart() was not intercepted by the source generator. Ensure the Pixely.DependencyInjection.Generator is referenced.");
    }

    /// <summary>
    /// Registers a callback invoked immediately after each singleton or transient is constructed (or, for pre-constructed
    /// instances, when the provider is built). The callback receives the instance and its concrete implementation type.
    /// </summary>
    /// <param name="callback">The callback to invoke for each activated service.</param>
    public void OnActivated(ServiceActivatedCallback callback)
    {
        _activatedCallbacks.Add(callback);
    }

    /// <summary>
    /// Registers a callback invoked during <see cref="ServiceProvider.Dispose"/> for each provider-owned disposable service,
    /// immediately before that service's own <see cref="IDisposable.Dispose"/> call. Services are visited
    /// in reverse creation order.
    /// </summary>
    /// <param name="callback">The callback to invoke for each service being disposed.</param>
    public void OnDisposing(ServiceDisposingCallback callback)
    {
        _disposingCallbacks.Add(callback);
    }

    /// <summary>Returns <see langword="true"/> if <typeparamref name="T"/> has been registered at least once.</summary>
    /// <typeparam name="T">The service type to check.</typeparam>
    /// <returns><see langword="true"/> if <typeparamref name="T"/> is registered; otherwise <see langword="false"/>.</returns>
    public bool IsRegistered<T>()
    {
        int id = ServiceTypeId<T>.Id;
        return _registeredTypeIds.Contains(id) ||
            _parent?.IsRegistered(id) == true;
    }

    /// <summary>
    /// Registers a live registry of activated services assignable to <typeparamref name="TService"/>.
    /// The registry does not create services by itself; it observes services as normal dependency
    /// resolution activates them.
    /// </summary>
    /// <typeparam name="TService">The service role to track.</typeparam>
    /// <param name="comparison">An optional comparison applied before each outermost registry iteration.</param>
    public void AddRegistry<TService>(Comparison<TService>? comparison = null) where TService : class
    {
        if (IsRegistered<ServiceRegistry<TService>>())
        {
            return;
        }

        ServiceRegistry<TService> registry = new(comparison);
        AddSingleton(registry);
        OnActivated((instance, _) =>
        {
            if (instance is TService service)
            {
                registry.Register(service);
            }
        });
        OnDisposing((instance, _) =>
        {
            if (instance is TService service)
            {
                registry.Unregister(service);
            }
        });
    }

    /// <summary>Resolves all services, fires <c>OnStart</c> callbacks, freezes the provider, and returns it.</summary>
    /// <returns>The fully constructed and frozen <see cref="ServiceProvider"/>.</returns>
    public ServiceProvider BuildServiceProvider()
    {
        ServiceProvider provider = new ServiceProvider(_parent);

        try
        {
            return BuildServiceProvider(provider);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    private ServiceProvider BuildServiceProvider(ServiceProvider provider)
    {
        provider.SetRegisteredTypeIds(_registeredTypeIds);

        List<ServiceActivatedCallback>? activatedCallbacks =
            MergeCallbacks(_parent?.ActivatedCallbacks, _activatedCallbacks, parentFirst: true);
        List<ServiceDisposingCallback>? disposingCallbacks =
            MergeCallbacks(_parent?.DisposingCallbacks, _disposingCallbacks, parentFirst: false);
        provider.SetCallbacks(activatedCallbacks, disposingCallbacks);

        // Register ServiceProvider itself
        provider.SetService(ServiceTypeId<ServiceProvider>.Id, provider);

        // A null value records that a singleton factory was evaluated and contributed no service.
        Dictionary<ServiceDescriptor, object?> singletonInstances = new();
        HashSet<ServiceDescriptor> resolving = new();

        // Set build-time resolvers so generated factories can trigger on-demand resolution
        provider.SetBuildTimeResolver(
            (id, type) => ResolveServiceById(id, type, provider, _parent, singletonInstances, resolving),
            id => TryResolveServiceById(id, provider, _parent, singletonInstances, resolving),
            id => ResolveServiceCollectionById(id, provider, _parent, singletonInstances, resolving));

        // Singleton descriptors are eager, including descriptors shadowed for single-service
        // resolution. Null results are cached so their factories run exactly once.
        foreach (KeyValuePair<int, List<ServiceDescriptor>> entry in _serviceGroups)
        {
            List<ServiceDescriptor> group = entry.Value;
            for (int i = 0; i < group.Count; i++)
            {
                ServiceDescriptor descriptor = group[i];
                if (descriptor.Lifetime == ServiceLifetime.Singleton)
                {
                    ResolveSingletonDescriptor(
                        descriptor,
                        provider,
                        _parent,
                        singletonInstances,
                        resolving);
                }
            }
        }

        // Singleton-only types retain the O(1) frozen lookup path. Types containing a transient
        // use their registration list at runtime because a later transient may return null and
        // reveal an earlier registration.
        foreach (KeyValuePair<int, List<ServiceDescriptor>> entry in _serviceGroups)
        {
            List<ServiceDescriptor> group = entry.Value;
            if (ContainsTransient(group))
            {
                continue;
            }

            for (int i = group.Count - 1; i >= 0; i--)
            {
                object? instance = singletonInstances[group[i]];
                if (instance != null)
                {
                    provider.SetService(entry.Key, instance);
                    break;
                }
            }
        }

        // Build service collections for GetServices<T>(), keyed by service-type id.
        // Store object[] here instead of a runtime-created T[]: Array.CreateInstance(Type, ...)
        // requires dynamic code under NativeAOT. ServiceProvider creates and caches the typed
        // T[] on first GetServices<T>() call, when T is known generically.
        Dictionary<int, object[]> serviceCollections = new();
        Dictionary<int, ServiceCollectionRegistration[]> serviceCollectionRegistrations = new();
        foreach (KeyValuePair<int, List<ServiceDescriptor>> entry in _serviceGroups)
        {
            List<ServiceDescriptor> group = entry.Value;
            if (ContainsTransient(group))
            {
                List<ServiceCollectionRegistration> registrations = new(group.Count);
                for (int i = 0; i < group.Count; i++)
                {
                    ServiceDescriptor descriptor = group[i];
                    if (descriptor.Lifetime == ServiceLifetime.Transient)
                    {
                        registrations.Add(ServiceCollectionRegistration.ForTransient(descriptor));
                        continue;
                    }

                    object? instance = singletonInstances[descriptor];
                    if (instance != null)
                    {
                        registrations.Add(ServiceCollectionRegistration.ForSingleton(instance));
                    }
                }

                serviceCollectionRegistrations[entry.Key] = registrations.ToArray();
                continue;
            }

            List<object> instances = new(group.Count);

            for (int i = 0; i < group.Count; i++)
            {
                object? instance = singletonInstances[group[i]];
                if (instance != null)
                {
                    instances.Add(instance);
                }
            }

            serviceCollections[entry.Key] = instances.ToArray();
        }

        // Every singleton descriptor has now been evaluated and both collection maps are
        // complete. OnStart callbacks can only read through the public ServiceProvider API —
        // there is no supported path to add a new registration during OnStart.
        provider.SetServiceCollections(serviceCollections);
        provider.SetServiceCollectionRegistrations(serviceCollectionRegistrations);

        foreach (Action<ServiceProvider> action in _onStartActions)
        {
            action(provider);
        }

        // Clear build-time resolvers — after build, all singletons are resolved
        provider.SetBuildTimeResolver(null, null, null);

        // FreezeServices snapshots _pending into the flat _services array for O(1) lookup.
        // Ordering relative to OnStart is not load-bearing (OnStart only reads), but freezing
        // last keeps the build-time and runtime resolution paths consistent for callbacks.
        provider.FreezeServices();

        return provider;
    }

    private static bool ContainsTransient(List<ServiceDescriptor> group)
    {
        for (int i = 0; i < group.Count; i++)
        {
            if (group[i].Lifetime == ServiceLifetime.Transient)
            {
                return true;
            }
        }

        return false;
    }

    private object? ResolveSingletonDescriptor(
        ServiceDescriptor descriptor,
        ServiceProvider provider,
        ServiceProvider? parent,
        Dictionary<ServiceDescriptor, object?> singletonInstances,
        HashSet<ServiceDescriptor> resolving)
    {
        if (singletonInstances.TryGetValue(descriptor, out object? cachedInstance))
        {
            return cachedInstance;
        }

        if (!resolving.Add(descriptor))
        {
            throw new InvalidOperationException(
                $"Circular dependency detected while resolving {descriptor.ServiceType.Name}.");
        }

        try
        {
            object? instance = descriptor.Kind switch
            {
                ServiceDescriptorKind.Instance => descriptor.Instance,
                ServiceDescriptorKind.TypedFactory => descriptor.TypedFactory!(provider),
                ServiceDescriptorKind.Alias => TryResolveServiceById(
                    descriptor.AliasSourceId,
                    provider,
                    parent,
                    singletonInstances,
                    resolving),
                _ => null
            };

            singletonInstances[descriptor] = instance;

            if (instance != null && descriptor.Kind != ServiceDescriptorKind.Alias)
            {
                provider.TrackSingleton(instance, descriptor.ConcreteType!);
                provider.RunActivatedCallbacks(instance, descriptor.ConcreteType!);
            }

            return instance;
        }
        finally
        {
            resolving.Remove(descriptor);
        }
    }

    private static List<TCallback>? MergeCallbacks<TCallback>(
        List<TCallback>? parentCallbacks,
        List<TCallback> childCallbacks,
        bool parentFirst)
    {
        int callbackCount = (parentCallbacks?.Count ?? 0) + childCallbacks.Count;
        if (callbackCount == 0)
        {
            return null;
        }

        List<TCallback> callbacks = new(callbackCount);
        if (parentFirst && parentCallbacks != null)
        {
            callbacks.AddRange(parentCallbacks);
        }
        callbacks.AddRange(childCallbacks);
        if (!parentFirst && parentCallbacks != null)
        {
            callbacks.AddRange(parentCallbacks);
        }

        return callbacks;
    }

    private object ResolveServiceById(
        int id,
        Type serviceType,
        ServiceProvider provider,
        ServiceProvider? parent,
        Dictionary<ServiceDescriptor, object?> singletonInstances,
        HashSet<ServiceDescriptor> resolving)
    {
        object? service = TryResolveServiceById(
            id,
            provider,
            parent,
            singletonInstances,
            resolving);
        if (service != null)
        {
            return service;
        }

        throw new InvalidOperationException(
            $"Cannot resolve service {serviceType.Name} with id {id}.");
    }

    private object? TryResolveServiceById(
        int id,
        ServiceProvider provider,
        ServiceProvider? parent,
        Dictionary<ServiceDescriptor, object?> singletonInstances,
        HashSet<ServiceDescriptor> resolving)
    {
        object? service = provider.GetServiceById(id);

        if (service != null)
        {
            return service;
        }

        if (_serviceGroups.TryGetValue(id, out List<ServiceDescriptor>? group))
        {
            for (int i = group.Count - 1; i >= 0; i--)
            {
                ServiceDescriptor descriptor = group[i];
                service = descriptor.Lifetime == ServiceLifetime.Transient
                    ? provider.CreateTransient(descriptor)
                    : ResolveSingletonDescriptor(
                        descriptor,
                        provider,
                        parent,
                        singletonInstances,
                        resolving);

                if (service != null)
                {
                    return service;
                }
            }
        }

        ServiceCollectionRegistration[]? parentRegistrations =
            parent?.GetMergedServiceCollectionRegistrationsById(id);
        if (parentRegistrations != null)
        {
            return ResolveLatestRegistration(parentRegistrations, provider);
        }

        return parent?.GetServiceByIdInChain(id);
    }

    private object[] ResolveServiceCollectionById(
        int id,
        ServiceProvider provider,
        ServiceProvider? parent,
        Dictionary<ServiceDescriptor, object?> singletonInstances,
        HashSet<ServiceDescriptor> resolving)
    {
        ServiceCollectionRegistration[]? parentRegistrations = parent?.GetMergedServiceCollectionRegistrationsById(id);
        object[]? parentCollection = parentRegistrations == null ? parent?.GetMergedServiceCollectionById(id) : null;

        if (!_serviceGroups.TryGetValue(id, out List<ServiceDescriptor>? group))
        {
            if (parentRegistrations != null)
            {
                return ResolveRegistrations(parentRegistrations, provider);
            }

            if (parentCollection == null)
            {
                return Array.Empty<object>();
            }

            return parentCollection;
        }

        List<object> instances = new(group.Count + (parentCollection?.Length ?? 0));

        if (parentRegistrations != null)
        {
            instances.AddRange(ResolveRegistrations(parentRegistrations, provider));
        }

        if (parentCollection != null)
        {
            instances.AddRange(parentCollection);
        }

        for (int i = 0; i < group.Count; i++)
        {
            ServiceDescriptor descriptor = group[i];
            object? instance = descriptor.Lifetime == ServiceLifetime.Transient
                ? provider.CreateTransient(descriptor)
                : ResolveSingletonDescriptor(
                    descriptor,
                    provider,
                    parent,
                    singletonInstances,
                    resolving);

            if (instance != null)
            {
                instances.Add(instance);
            }
        }

        return instances.ToArray();
    }

    private static object[] ResolveRegistrations(ServiceCollectionRegistration[] registrations, ServiceProvider provider)
    {
        List<object> instances = new(registrations.Length);
        for (int i = 0; i < registrations.Length; i++)
        {
            ServiceCollectionRegistration registration = registrations[i];
            object? instance = registration.TransientDescriptor != null
                ? provider.CreateTransient(registration.TransientDescriptor)
                : registration.SingletonInstance;

            if (instance != null)
            {
                instances.Add(instance);
            }
        }

        return instances.ToArray();
    }

    private static object? ResolveLatestRegistration(
        ServiceCollectionRegistration[] registrations,
        ServiceProvider provider)
    {
        for (int i = registrations.Length - 1; i >= 0; i--)
        {
            ServiceCollectionRegistration registration = registrations[i];
            object? instance = registration.TransientDescriptor != null
                ? provider.CreateTransient(registration.TransientDescriptor)
                : registration.SingletonInstance;

            if (instance != null)
            {
                return instance;
            }
        }

        return null;
    }
}
