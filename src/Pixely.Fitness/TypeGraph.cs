using System.Reflection;
using System.Runtime.CompilerServices;

namespace Pixely.Fitness;

public static class TypeGraph
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// The types a game wrote: everything the compiler and the source generators emitted is skipped.
    /// </summary>
    public static IEnumerable<Type> DeclaredTypes(Assembly assembly)
    {
        return assembly.GetTypes().Where(type => !IsCompilerGenerated(type));
    }

    public static IEnumerable<Type> TypesIn(Assembly assembly, string ns)
    {
        return DeclaredTypes(assembly).Where(type => type.Namespace == ns);
    }

    public static bool IsPublicSurface(Type type)
    {
        return type.IsVisible;
    }

    // A record class gets a <Clone>$ method, a record struct does not; both get PrintMembers(StringBuilder).
    public static bool IsRecord(Type type)
    {
        return type.GetMethod("PrintMembers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(System.Text.StringBuilder)]) != null;
    }

    public static bool IsStatic(Type type)
    {
        return type.IsAbstract && type.IsSealed;
    }

    public static bool IsCompilerGenerated(Type type)
    {
        for (Type? current = type; current != null; current = current.DeclaringType)
        {
            if (current.Name.StartsWith('<') || current.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<MethodInfo> DeclaredMethods(Type type)
    {
        return type.GetMethods(Declared).Where(method => method.GetCustomAttribute<CompilerGeneratedAttribute>() == null);
    }

    public static IEnumerable<FieldInfo> DeclaredFields(Type type)
    {
        return type.GetFields(Declared);
    }

    // Auto-property accessors are compiler generated, so properties are listed on their own rather than through their accessors.
    public static IEnumerable<PropertyInfo> DeclaredProperties(Type type)
    {
        return type.GetProperties(Declared);
    }

    public static IEnumerable<ConstructorInfo> Constructors(Type type)
    {
        return type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    /// <summary>
    /// Every type a type names: its base, interfaces, field types, constructor and method signatures, with generic arguments and
    /// element types expanded.
    /// </summary>
    public static IEnumerable<Type> SignatureTypes(Type type)
    {
        IEnumerable<Type> direct = new[] { type.BaseType }.OfType<Type>()
            .Concat(type.GetInterfaces())
            .Concat(DeclaredFields(type).Select(field => field.FieldType))
            .Concat(DeclaredProperties(type).Select(property => property.PropertyType))
            .Concat(Constructors(type).SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(DeclaredMethods(type).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
            .Concat(type.GetCustomAttributesData().Select(attribute => attribute.AttributeType));
        return direct.SelectMany(Expand).Distinct();
    }

    public static IEnumerable<Type> PublicSignatureTypes(Type type)
    {
        IEnumerable<Type> direct = Constructors(type).Where(constructor => constructor.IsPublic)
            .SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType))
            .Concat(DeclaredMethods(type).Where(method => method.IsPublic)
                .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
            .Concat(DeclaredFields(type).Where(field => field.IsPublic).Select(field => field.FieldType))
            .Concat(DeclaredProperties(type).Where(property => property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true).Select(property => property.PropertyType));
        return direct.SelectMany(Expand).Distinct();
    }

    // A generic parameter may be constrained by itself, e.g. T : IComparable<T>, so each type expands once.
    public static IEnumerable<Type> Expand(Type type)
    {
        return Expand(type, new HashSet<Type>());
    }

    private static IEnumerable<Type> Expand(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            yield break;
        }

        yield return type;
        if (type.IsGenericParameter)
        {
            foreach (Type constraint in type.GetGenericParameterConstraints().SelectMany(constraint => Expand(constraint, visited)))
            {
                yield return constraint;
            }

            yield break;
        }

        if (type.HasElementType)
        {
            foreach (Type element in Expand(type.GetElementType()!, visited))
            {
                yield return element;
            }
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments().SelectMany(argument => Expand(argument, visited)))
            {
                yield return argument;
            }
        }
    }

    public static bool IsEnumerable(Type type)
    {
        return type != typeof(string) && (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
            || type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) || type.GetGenericTypeDefinition() == typeof(Span<>)));
    }

    public static bool IsReadOnlyRecordStruct(Type type)
    {
        return type.IsValueType && IsRecord(type) && type.GetCustomAttribute<IsReadOnlyAttribute>() != null;
    }

    public static string Describe(MemberInfo member)
    {
        return member is Type type ? type.FullName! : $"{member.DeclaringType!.FullName}.{member.Name}";
    }
}
