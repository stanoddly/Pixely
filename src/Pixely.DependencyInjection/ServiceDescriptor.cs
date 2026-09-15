using System.Diagnostics.CodeAnalysis;

namespace Pixely.DependencyInjection;

internal enum ServiceDescriptorKind
{
    TypedFactory,
    Instance,
    Alias
}

internal enum ServiceLifetime
{
    Singleton,
    Transient
}

internal class ServiceDescriptor
{
    public int ServiceTypeId { get; }
    public Type ServiceType { get; }
    public ServiceDescriptorKind Kind { get; }
    public ServiceLifetime Lifetime { get; }
    public object? Instance { get; private init; }
    public Func<ServiceProvider, object?>? TypedFactory { get; private init; }
    public int AliasSourceId { get; private init; }

    // The concrete implementation type — used for typed activation/disposal callbacks.
    // For Instance and TypedFactory descriptors this equals the T in ForInstance<T>/ForTypedFactory<T>.
    // Null for Alias descriptors (the source descriptor owns the type).
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
    public Type? ConcreteType { get; private init; }

    public bool TracksDisposal { get; private init; }

    private ServiceDescriptor(int serviceTypeId, Type serviceType, ServiceDescriptorKind kind, ServiceLifetime lifetime)
    {
        ServiceTypeId = serviceTypeId;
        ServiceType = serviceType;
        Kind = kind;
        Lifetime = lifetime;
    }

    public static ServiceDescriptor ForInstance<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>(T instance) where T : class
    {
        return new ServiceDescriptor(ServiceTypeId<T>.Id, typeof(T), ServiceDescriptorKind.Instance, ServiceLifetime.Singleton)
        {
            Instance = instance,
            ConcreteType = typeof(T),
            TracksDisposal = instance is IDisposable
        };
    }

    public static ServiceDescriptor ForTypedFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>(Func<ServiceProvider, T?> typedFactory) where T : class
    {
        return ForTypedFactory(typedFactory, ServiceLifetime.Singleton);
    }

    public static ServiceDescriptor ForTransientTypedFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>(Func<ServiceProvider, T?> typedFactory) where T : class
    {
        return ForTypedFactory(typedFactory, ServiceLifetime.Transient);
    }

    private static ServiceDescriptor ForTypedFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] T>(Func<ServiceProvider, T?> typedFactory, ServiceLifetime lifetime) where T : class
    {
        return new ServiceDescriptor(ServiceTypeId<T>.Id, typeof(T), ServiceDescriptorKind.TypedFactory, lifetime)
        {
            TypedFactory = typedFactory,
            ConcreteType = typeof(T),
            TracksDisposal = typeof(IDisposable).IsAssignableFrom(typeof(T))
        };
    }

    // Used when the service type and implementation type differ (AddSingleton<TService, TImpl>(...))
    // so that activation/disposal callbacks receive typeof(TImpl) rather than typeof(TService).
    public static ServiceDescriptor ForTypedFactoryWithConcreteType<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TImpl>(
        Func<ServiceProvider, TImpl?> typedFactory)
        where TService : class
        where TImpl : class, TService
    {
        return ForTypedFactoryWithConcreteType<TService, TImpl>(typedFactory, ServiceLifetime.Singleton);
    }

    public static ServiceDescriptor ForTransientTypedFactoryWithConcreteType<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TImpl>(
        Func<ServiceProvider, TImpl?> typedFactory)
        where TService : class
        where TImpl : class, TService
    {
        return ForTypedFactoryWithConcreteType<TService, TImpl>(typedFactory, ServiceLifetime.Transient);
    }

    private static ServiceDescriptor ForTypedFactoryWithConcreteType<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] TImpl>(
        Func<ServiceProvider, TImpl?> typedFactory,
        ServiceLifetime lifetime)
        where TService : class
        where TImpl : class, TService
    {
        return new ServiceDescriptor(ServiceTypeId<TService>.Id, typeof(TService), ServiceDescriptorKind.TypedFactory, lifetime)
        {
            TypedFactory = typedFactory,
            ConcreteType = typeof(TImpl),
            TracksDisposal = typeof(IDisposable).IsAssignableFrom(typeof(TImpl))
        };
    }

    public static ServiceDescriptor ForAlias<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        return new ServiceDescriptor(ServiceTypeId<TService>.Id, typeof(TService), ServiceDescriptorKind.Alias, ServiceLifetime.Singleton)
        {
            AliasSourceId = ServiceTypeId<TImplementation>.Id
        };
    }
}
