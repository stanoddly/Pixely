using Pixely.DependencyInjection;

namespace Pixely.Fitness;

// What a registrar put into a collection, read without building it.
internal readonly record struct ServiceRegistration(Type ServiceType, Type? ConcreteType);

internal static class ServiceCollectionProbe
{
    internal static IReadOnlyList<ServiceRegistration> Registrations(ServiceCollection services)
    {
        return services.Descriptors.Select(descriptor => new ServiceRegistration(descriptor.ServiceType, descriptor.ConcreteType)).ToArray();
    }
}
