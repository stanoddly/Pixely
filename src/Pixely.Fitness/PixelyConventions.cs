using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Events;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Fitness;

/// <summary>
/// The fitness functions of Pixely's own conventions, one per rule, each returning the members that
/// break it. They hold for any consumer, whatever architecture it follows.
/// </summary>
public static class PixelyConventions
{
    // A renderer builds nothing: pipelines come out of Create, geometry out of a service that owns the buffers.
    private static readonly Type[] BuilderTypes = [typeof(GraphicsPipelineBuilder), typeof(ShaderLoader), typeof(IShaderLoader), typeof(GpuMemorySystem)];

    // The GPU objects a type owns when it holds them; the device and a command buffer are held but never owned.
    private static readonly Type[] OwnedGpuTypes = [typeof(Texture), typeof(GpuVertexBuffer), typeof(GpuIndexBuffer), typeof(GpuStorageBuffer), typeof(GraphicsPipeline), typeof(ComputePipeline), typeof(Sampler), typeof(GraphicsShaderProgram)];

    private static readonly Type[] FrameParticipantTypes = [typeof(IUpdatable), typeof(IRenderer<>), typeof(IEventHandler<>)];

    public static FitnessReport Evaluate(PixelyConventionsOptions options)
    {
        List<FitnessResult> results =
        [
            Run("Pixely 01 RenderersTakeNoBuilders", RenderersTakeNoBuilders),
            Run("Pixely 02 VertexTypesMatchTheirElements", VertexTypesMatchTheirElements)
        ];
        if (options.FactoriesHideConstructors)
        {
            results.Add(Run("Pixely 03 FactoriesHideConstructors", FactoriesHideConstructors));
        }

        if (options.GpuOwnersAreDisposable)
        {
            results.Add(Run("Pixely 04 GpuOwnersAreDisposable", GpuOwnersAreDisposable));
        }

        if (options.FrameParticipantsAreRegistered)
        {
            results.Add(Run("Pixely 05 FrameParticipantsAreRegistered", FrameParticipantsAreRegistered));
        }

        return new FitnessReport(results);

        FitnessResult Run(string name, Func<PixelyConventionsOptions, IReadOnlyList<string>> function)
        {
            return new FitnessResult(name, function(options));
        }
    }

    public static IReadOnlyList<string> RenderersTakeNoBuilders(PixelyConventionsOptions options)
    {
        return Classes(options).Where(type => Implements(type, typeof(IRenderer<>)))
            .SelectMany(TypeGraph.Constructors)
            .SelectMany(constructor => constructor.GetParameters().Where(parameter => BuilderTypes.Contains(parameter.ParameterType))
                .Select(parameter => $"{TypeGraph.Describe(constructor.DeclaringType!)}: constructor takes {parameter.ParameterType.Name}; build in Create and pass the result"))
            .ToArray();
    }

    // VertexElements is what the GPU reads; the struct is what the CPU writes. One entry per field and the same number of bytes.
    public static IReadOnlyList<string> VertexTypesMatchTheirElements(PixelyConventionsOptions options)
    {
        List<string> violations = new List<string>();
        foreach (Type type in options.Assemblies.SelectMany(TypeGraph.DeclaredTypes).Where(type => type.IsValueType && !type.IsGenericTypeDefinition && typeof(IVertexType).IsAssignableFrom(type)))
        {
            MethodInfo getter = type.GetInterfaceMap(typeof(IVertexType)).TargetMethods.Single();
            System.Collections.Immutable.ImmutableArray<VertexElementFormat> elements = (System.Collections.Immutable.ImmutableArray<VertexElementFormat>)getter.Invoke(null, null)!;
            int fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length;
            int declaredBytes = elements.Sum(element => element.GetNumberOfBytes());
            int actualBytes = (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;
            if (!type.IsLayoutSequential && !type.IsExplicitLayout)
            {
                violations.Add($"{type.FullName}: layout is Auto, the GPU reads fields in order");
            }

            if (fields != elements.Length)
            {
                violations.Add($"{type.FullName}: {fields} fields but {elements.Length} vertex elements");
            }

            if (declaredBytes != actualBytes)
            {
                violations.Add($"{type.FullName}: vertex elements add up to {declaredBytes} bytes, the struct is {actualBytes}");
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> FactoriesHideConstructors(PixelyConventionsOptions options)
    {
        return Classes(options)
            .Where(type => TypeGraph.DeclaredMethods(type).Any(method => method.IsStatic && method.IsPublic && method.Name == "Create" && method.ReturnType == type))
            .Where(type => TypeGraph.Constructors(type).Any(constructor => constructor.IsPublic))
            .Select(type => $"{type.FullName}: has a public constructor beside Create")
            .ToArray();
    }

    public static IReadOnlyList<string> GpuOwnersAreDisposable(PixelyConventionsOptions options)
    {
        return Classes(options)
            .Where(type => !typeof(IDisposable).IsAssignableFrom(type))
            .SelectMany(type => TypeGraph.DeclaredFields(type).Where(field => !field.IsStatic)
                .SelectMany(field => TypeGraph.Expand(field.FieldType).Where(IsOwnedGpuType).Take(1).Select(_ => field)))
            .Select(field => $"{TypeGraph.Describe(field)}: holds a {field.FieldType.Name} but the type is not IDisposable")
            .ToArray();
    }

    // Every registrar in the assemblies composes one root and one stage; a participant nothing registered never runs.
    public static IReadOnlyList<string> FrameParticipantsAreRegistered(PixelyConventionsOptions options)
    {
        List<string> violations = new List<string>();
        HashSet<Type> registered = RegistrarProbe.Compose(RegistrarProbe.Registrars(options.Assemblies, typeof(PixelyAppBuilder)), new PixelyAppBuilder(), violations)
            .Concat(RegistrarProbe.Compose(RegistrarProbe.Registrars(options.Assemblies, typeof(ServiceCollection)), new ServiceCollection(), violations))
            .SelectMany(registration => new[] { registration.ServiceType, registration.ConcreteType }.OfType<Type>())
            .ToHashSet();
        violations.AddRange(Classes(options)
            .Where(type => !type.IsGenericTypeDefinition && FrameParticipantTypes.Any(participant => Implements(type, participant)) && !registered.Contains(type))
            .Select(type => $"{type.FullName}: no registrar registers it"));
        return violations;
    }

    private static IEnumerable<Type> Classes(PixelyConventionsOptions options)
    {
        return options.Assemblies.SelectMany(TypeGraph.DeclaredTypes).Where(type => type.IsClass && !type.IsAbstract);
    }

    private static bool Implements(Type type, Type interfaceType)
    {
        return type.GetInterfaces().Any(candidate => candidate == interfaceType || candidate.IsGenericType && candidate.GetGenericTypeDefinition() == interfaceType);
    }

    private static bool IsOwnedGpuType(Type type)
    {
        return OwnedGpuTypes.Any(owned => owned.IsAssignableFrom(type));
    }
}
