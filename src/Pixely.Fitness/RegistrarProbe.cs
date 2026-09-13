using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.DependencyInjection;

namespace Pixely.Fitness;

internal static class RegistrarProbe
{
    // A registrar is an Add* or Use* extension method on PixelyAppBuilder or ServiceCollection.
    internal static bool IsRegistrarMethod(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        return method.GetCustomAttribute<ExtensionAttribute>() != null
            && (method.Name.StartsWith("Add", StringComparison.Ordinal) || method.Name.StartsWith("Use", StringComparison.Ordinal))
            && parameters.Length > 0
            && (parameters[0].ParameterType == typeof(PixelyAppBuilder) || parameters[0].ParameterType == typeof(ServiceCollection));
    }

    internal static IEnumerable<MethodInfo> Registrars(IEnumerable<Assembly> assemblies, Type container)
    {
        return assemblies
            .SelectMany(assembly => TypeGraph.DeclaredTypes(assembly).Where(type => type.IsPublic && TypeGraph.IsStatic(type)))
            .SelectMany(TypeGraph.DeclaredMethods)
            .Where(method => method.IsPublic && !method.IsGenericMethodDefinition && IsRegistrarMethod(method) && method.GetParameters()[0].ParameterType == container)
            .OrderBy(method => method.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal);
    }

    // A registrar registers the same types whatever its arguments, so defaults compose the real set of registrations.
    internal static IReadOnlyList<ServiceRegistration> Compose(IEnumerable<MethodInfo> registrars, ServiceCollection target, List<string> violations)
    {
        foreach (MethodInfo registrar in registrars)
        {
            object?[] arguments = registrar.GetParameters().Select(parameter => parameter.Position == 0 ? target : DefaultArgument(parameter)).ToArray();
            try
            {
                registrar.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                violations.Add($"registrar rejected default arguments: {TypeGraph.Describe(registrar)}: {exception.InnerException?.Message ?? exception.Message}");
            }
        }

        return ServiceCollectionProbe.Registrations(target);

        static object? DefaultArgument(ParameterInfo parameter)
        {
            if (parameter.HasDefaultValue && parameter.DefaultValue != null)
            {
                return parameter.DefaultValue;
            }

            return parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) == null ? Activator.CreateInstance(parameter.ParameterType) : null;
        }
    }
}
