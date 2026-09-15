using System.Diagnostics.CodeAnalysis;

namespace Pixely.DependencyInjection;

// The composed Decorate<T> delegates for one service type, and that type for describing a replacement to callbacks.
internal sealed class ServiceDecorator
{
    public ServiceDecorator(Func<object, object> apply, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type serviceType)
    {
        Apply = apply;
        ServiceType = serviceType;
    }

    public Func<object, object> Apply { get; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
    public Type ServiceType { get; }

    public static ServiceDecorator? Compose(ServiceDecorator? first, ServiceDecorator? second)
    {
        if (first == null || second == null)
        {
            return first ?? second;
        }

        Func<object, object> firstApply = first.Apply;
        Func<object, object> secondApply = second.Apply;
        return new ServiceDecorator(instance => secondApply(firstApply(instance)), first.ServiceType);
    }
}
