using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Pixely.DependencyInjection.Generator;

namespace Pixely.Ecs.Generator;

// Each constructor argument is the SystemBuilder call that creates a query, lookup or writer, or the provider call that resolves a service.
readonly record struct SystemRegistration(string InterceptsLocationAttribute, string SystemType, EquatableArray<string> ConstructorArguments);

readonly record struct SystemExtraction(SystemRegistration? Registration, DiagnosticInfo? Diagnostic);

// Intercepts AddSystem<TSystem>(). The generated code creates the system with the queries, lookups and writers its constructor takes from the SystemBuilder,
// which records them as the system's contract, and the rest from the provider, so the system itself needs no generated code.
[Generator]
public class SystemGenerator : IIncrementalGenerator
{
    private const string ExtensionsName = "Pixely.Ecs.EcsServiceCollectionExtensions";
    private const string SystemName = "Pixely.Ecs.ISystem";

    // The generic writers and lookups by their arities, each created by the SystemBuilder method of its name with its type arguments.
    private static readonly Dictionary<string, (int MinArity, int MaxArity)> BuilderMethods = new Dictionary<string, (int, int)>
    {
        ["ComponentAdder"] = (1, 1),
        ["ComponentRemover"] = (1, 1),
        ["ComponentLookup"] = (1, 8),
        ["ReadOnlyComponentLookup"] = (1, 8)
    };

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<SystemExtraction?> results = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => IsCandidate(node),
            transform: static (ctx, ct) => Extract(ctx, ct)).Where(static result => result is not null);

        context.RegisterSourceOutput(results.Collect(), static (spc, results) =>
        {
            List<SystemRegistration> registrations = [];
            foreach (SystemExtraction? result in results)
            {
                if (result!.Value.Diagnostic is { } diagnostic)
                {
                    GeneratorSupport.Report(spc, diagnostic);
                }
                else
                {
                    registrations.Add(result.Value.Registration!.Value);
                }
            }

            if (registrations.Count > 0)
            {
                spc.AddSource("SystemInterceptors.g.cs", Generate(registrations));
            }
        });
    }

    // AddSystem<T>(), called directly, on a receiver or through ?., or named without a call, as a method group.
    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
        {
            return IsMethodGroupCandidate(node);
        }

        SimpleNameSyntax? name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            SimpleNameSyntax simpleName => simpleName,
            _ => null
        };

        return name is GenericNameSyntax { Identifier.Text: "AddSystem" };
    }

    // A name of AddSystem that no invocation calls. The name part of a member access is covered by the member access, and the name after ?.
    // is always called.
    private static bool IsMethodGroupCandidate(SyntaxNode node)
    {
        if (node.Parent is MemberBindingExpressionSyntax || (node.Parent is MemberAccessExpressionSyntax { Name: var accessedName } && accessedName == node))
        {
            return false;
        }

        SimpleNameSyntax? name = node switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            SimpleNameSyntax simpleName => simpleName,
            _ => null
        };

        bool isCalled = node.Parent is InvocationExpressionSyntax parentInvocation && parentInvocation.Expression == node;
        return name?.Identifier.Text == "AddSystem" && !isCalled;
    }

    private static SystemExtraction? Extract(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
        {
            return ExtractMethodGroup(context, cancellationToken);
        }

        // A call whose type arguments break a constraint resolves only to a candidate, which is still the method to report on.
        SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
        if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method)
        {
            return null;
        }

        if (method.Name != "AddSystem" || method.ContainingType?.ToDisplayString() != ExtensionsName)
        {
            return null;
        }

        // A type argument that is not an ISystem breaks the constraint, which the compiler reports. A type parameter is reported below.
        ITypeSymbol systemType = method.TypeArguments[0];
        if (systemType is not ITypeParameterSymbol && !systemType.AllInterfaces.Any(implemented => implemented.ToDisplayString() == SystemName))
        {
            return null;
        }

        // The generated code is outside the system, so the system must be accessible anywhere in the assembly.
        string systemName = systemType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        string call = $"AddSystem<{systemName}>()";
        Compilation compilation = context.SemanticModel.Compilation;
        if (systemType is not INamedTypeSymbol { IsAbstract: false } namedType || GeneratorSupport.ContainsTypeParameter(systemType)
            || !compilation.IsSymbolAccessibleWithin(namedType, compilation.Assembly) || IsInFileLocalType(namedType))
        {
            return Fail("PXECS010", $"{call} requires a concrete class without type parameters, accessible anywhere in its assembly.", invocation);
        }

        // The registry of IUpdatable would update it too, so it would update twice per frame.
        if (namedType.AllInterfaces.Any(implemented => implemented.ToDisplayString() == "Pixely.IUpdatable"))
        {
            return Fail("PXECS014", $"{systemName} implements IUpdatable, so it would update twice per frame: once as an IUpdatable and once through the SystemScheduler. Remove IUpdatable.", invocation);
        }

        IMethodSymbol[] constructors = [.. namedType.InstanceConstructors.Where(constructor => compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly))];
        if (constructors.Length != 1)
        {
            return Fail("PXECS011", $"{call} requires {systemName} to have exactly one public or internal constructor.", invocation);
        }

        if (constructors[0].Parameters.FirstOrDefault(parameter => parameter.RefKind is not (RefKind.None or RefKind.In)) is { } byReference)
        {
            return Fail("PXECS012", $"The constructor of {systemName} takes {byReference.Name} by reference, but its arguments are created for the call.", invocation);
        }

        List<string> arguments = [];
        foreach (IParameterSymbol parameter in constructors[0].Parameters)
        {
            SystemExtraction? failure = CreateArgument(parameter, compilation, invocation, out string argument);
            if (failure is not null)
            {
                return failure;
            }

            arguments.Add(argument);
        }

        InterceptableLocation? location = context.SemanticModel.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return Fail("PXECS015", $"{call} cannot be intercepted where it is called, so it would throw. Call it as a method of its receiver.", invocation);
        }

        return new SystemExtraction(new SystemRegistration(location.GetInterceptsLocationAttributeSyntax(), systemType.ToDisplayString(TypeFormat), new EquatableArray<string>([.. arguments])), null);
    }

    // A method group of AddSystem would throw when called, because no call can be intercepted. nameof calls nothing.
    private static SystemExtraction? ExtractMethodGroup(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        SyntaxNode group = context.Node;
        if (context.SemanticModel.GetSymbolInfo(group, cancellationToken).Symbol is not IMethodSymbol { TypeArguments.Length: 1 } method
            || group.Ancestors().OfType<InvocationExpressionSyntax>().Any(ancestor => context.SemanticModel.GetOperation(ancestor, cancellationToken) is INameOfOperation))
        {
            return null;
        }

        if (method.Name == "AddSystem" && method.ContainingType?.ToDisplayString() == ExtensionsName)
        {
            string systemName = method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return Fail("PXECS015", $"AddSystem<{systemName}>() is used as a method group, which cannot be intercepted, so it would throw. Call it directly.", group);
        }

        return null;
    }

    // A query, spawner, lookup or writer comes from the SystemBuilder, which records it as the system's contract, and anything else from the provider.
    private static SystemExtraction? CreateArgument(IParameterSymbol parameter, Compilation compilation, InvocationExpressionSyntax invocation, out string argument)
    {
        ITypeSymbol type = parameter.Type;
        string typeName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        argument = "";
        if (IsQuery(type))
        {
            SystemExtraction? failure = ValidateQuery((INamedTypeSymbol)type, typeName, compilation, invocation);
            argument = $"builder.Query<{type.ToDisplayString(TypeFormat)}>()";
            return failure;
        }

        if (IsSpawner(type))
        {
            SystemExtraction? failure = ValidateSpawner((INamedTypeSymbol)type, typeName, compilation, invocation);
            argument = $"builder.Spawner<{type.ToDisplayString(TypeFormat)}>()";
            return failure;
        }

        if (GetBuilderCall(type) is { } builderCall)
        {
            // Only lookups take more than one type argument.
            if (GetRepeated(((INamedTypeSymbol)type).TypeArguments) is { } repeated)
            {
                return Fail("PXECS008", $"The lookup {typeName} names {repeated.Name} more than once.", invocation);
            }

            argument = builderCall;
            return null;
        }

        argument = CreateServiceArgument(parameter);
        return null;
    }

    // Repeats the checks World.Query makes when it creates the query, so a query a system names fails the build instead. The QueryTermGenerator
    // checks the span type arguments where Query<...> is written.
    private static SystemExtraction? ValidateQuery(INamedTypeSymbol query, string queryName, Compilation compilation, InvocationExpressionSyntax invocation)
    {
        bool creatable = query.TypeKind == TypeKind.Class && !query.IsAbstract && !GeneratorSupport.ContainsTypeParameter(query) && !IsInFileLocalType(query)
            && compilation.IsSymbolAccessibleWithin(query, compilation.Assembly)
            && query.InstanceConstructors.Any(constructor => constructor.Parameters.IsEmpty && constructor.DeclaredAccessibility == Accessibility.Public);
        if (!creatable)
        {
            return Fail("PXECS001", $"The query {queryName} must be a non-abstract class without type parameters, accessible anywhere in its assembly, with a public parameterless constructor.", invocation);
        }

        List<string> required = [];
        foreach (ITypeSymbol typeArgument in GeneratorSupport.GetQueryBase(query)!.TypeArguments)
        {
            if (GeneratorSupport.ClassifySpan(typeArgument, out ITypeSymbol? element) is SpanKind.Span or SpanKind.ReadOnlySpan)
            {
                required.Add(element!.ToDisplayString(TypeFormat));
            }
        }

        required.AddRange(GetFilters(query, "Pixely.Ecs.IWith<"));
        if (required.Count == 0)
        {
            return Fail("PXECS004", $"The query {queryName} must require at least one component through Span<T>, ReadOnlySpan<T> or IWith<...>.", invocation);
        }

        string? contradiction = GetFilters(query, "Pixely.Ecs.IWithout<").FirstOrDefault(required.Contains);
        if (contradiction is not null)
        {
            return Fail("PXECS005", $"The query {queryName} both requires and excludes {contradiction}, so it matches no entity.", invocation);
        }

        return null;
    }

    // Repeats the checks the world makes when it creates the spawner's layout, so a spawner a system names fails the build instead.
    private static SystemExtraction? ValidateSpawner(INamedTypeSymbol spawner, string spawnerName, Compilation compilation, InvocationExpressionSyntax invocation)
    {
        bool creatable = spawner.TypeKind == TypeKind.Class && !spawner.IsAbstract && !GeneratorSupport.ContainsTypeParameter(spawner) && !IsInFileLocalType(spawner)
            && compilation.IsSymbolAccessibleWithin(spawner, compilation.Assembly)
            && spawner.InstanceConstructors.Any(constructor => constructor.Parameters.IsEmpty && constructor.DeclaredAccessibility == Accessibility.Public);
        if (!creatable)
        {
            return Fail("PXECS006", $"The spawner {spawnerName} must be a non-abstract class without type parameters, accessible anywhere in its assembly, with a public parameterless constructor.", invocation);
        }

        List<ITypeSymbol> components = [];
        foreach (ITypeSymbol argument in GeneratorSupport.GetSpawnerBase(spawner)!.TypeArguments)
        {
            if (argument.ToDisplayString() == GeneratorSupport.EntityName)
            {
                return Fail("PXECS007", $"The spawner {spawnerName} takes Entity, which is not a component. Spawn returns the entity.", invocation);
            }

            if (GeneratorSupport.IsTag(argument))
            {
                return Fail("PXECS007", $"The spawner {spawnerName} takes {argument.Name}, a tag, which has no data. Use IWith<{argument.Name}> instead.", invocation);
            }

            components.Add(argument);
        }

        foreach (ITypeSymbol tag in GetFilterTypes(spawner, "Pixely.Ecs.IWith<"))
        {
            if (!GeneratorSupport.IsTag(tag))
            {
                return Fail("PXECS009", $"The spawner {spawnerName} names {tag.Name} in IWith<...>, but it has data. Name it in Spawner<...>, whose Spawn takes its value.", invocation);
            }

            components.Add(tag);
        }

        if (GetFilterTypes(spawner, "Pixely.Ecs.IWithout<").Any())
        {
            return Fail("PXECS009", $"The spawner {spawnerName} implements IWithout<...>, which a spawner has no use for.", invocation);
        }

        if (GetRepeated(components) is { } repeated)
        {
            return Fail("PXECS008", $"The spawner {spawnerName} names {repeated.Name} more than once.", invocation);
        }

        return null;
    }

    private static ITypeSymbol? GetRepeated(IEnumerable<ITypeSymbol> types) =>
        types.GroupBy(type => type, SymbolEqualityComparer.Default).FirstOrDefault(group => group.Count() > 1)?.Key as ITypeSymbol;

    private static bool IsQuery(ITypeSymbol type) => type is INamedTypeSymbol named && GeneratorSupport.GetQueryBase(named) is not null;

    private static bool IsSpawner(ITypeSymbol type) => type is INamedTypeSymbol named && GeneratorSupport.GetSpawnerBase(named) is not null;

    private static string? GetBuilderCall(ITypeSymbol type)
    {
        if (type.ToDisplayString() == "Pixely.Ecs.EntityDestroyer")
        {
            return "builder.EntityDestroyer()";
        }

        return type is INamedTypeSymbol { IsGenericType: true } generic && generic.ContainingNamespace.ToDisplayString() == "Pixely.Ecs"
            && BuilderMethods.TryGetValue(generic.Name, out (int MinArity, int MaxArity) arities) && generic.Arity >= arities.MinArity && generic.Arity <= arities.MaxArity
            ? $"builder.{generic.Name}<{string.Join(", ", generic.TypeArguments.Select(argument => argument.ToDisplayString(TypeFormat)))}>()"
            : null;
    }

    // The type arguments of every IWith<...> or IWithout<...> the type implements. The compiler allows at most one of each, because two would both implement the same member.
    private static IEnumerable<ITypeSymbol> GetFilterTypes(INamedTypeSymbol type, string prefix) => type.AllInterfaces
        .Where(implemented => implemented.IsGenericType && implemented.ConstructedFrom.ToDisplayString().StartsWith(prefix))
        .SelectMany(implemented => implemented.TypeArguments);

    private static List<string> GetFilters(INamedTypeSymbol type, string prefix) => [.. GetFilterTypes(type, prefix).Select(argument => argument.ToDisplayString(TypeFormat))];

    private static string CreateServiceArgument(IParameterSymbol parameter)
    {
        ITypeSymbol type = parameter.Type;
        if (type is INamedTypeSymbol { IsGenericType: true } generic && generic.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return $"sp.GetServices<{generic.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat)}>()";
        }

        string resolutionType = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat);
        return type.IsReferenceType && parameter.NullableAnnotation == NullableAnnotation.Annotated
            ? $"sp.GetService<{resolutionType}>()"
            : $"sp.GetRequiredService<{resolutionType}>()";
    }

    private static bool IsInFileLocalType(INamedTypeSymbol type) => type.IsFileLocal || (type.ContainingType is not null && IsInFileLocalType(type.ContainingType));

    private static SystemExtraction Fail(string id, string message, SyntaxNode node) => new SystemExtraction(null, GeneratorSupport.CreateDiagnostic(id, message, node.GetLocation()));

    private static string Generate(List<SystemRegistration> registrations)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace System.Runtime.CompilerServices");
        sb.AppendLine("{");
        sb.AppendLine("#pragma warning disable CS9113");
        sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        sb.AppendLine("    file sealed class InterceptsLocationAttribute(int version, string data) : global::System.Attribute;");
        sb.AppendLine("#pragma warning restore CS9113");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("namespace Pixely.Ecs.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    file static class SystemInterceptors");
        sb.AppendLine("    {");
        for (int i = 0; i < registrations.Count; i++)
        {
            SystemRegistration registration = registrations[i];
            if (i > 0)
            {
                sb.AppendLine();
            }

            string system = registration.SystemType;
            sb.AppendLine($"        {registration.InterceptsLocationAttribute}");

            // The provider creates singletons grouped by service type, and earlier when another service needs one, so the order is reserved when
            // AddSystem runs and the scheduler sorts by it. The system joins the scheduler when it is created, before any OnBuilt callback.
            sb.AppendLine($"        public static void AddSystem_{i}<TSystem>(this global::Pixely.DependencyInjection.ServiceCollection services) where TSystem : class, global::Pixely.Ecs.ISystem");
            sb.AppendLine("        {");
            sb.AppendLine("            long order = global::Pixely.Ecs.SystemScheduler.ReserveOrder();");
            sb.AppendLine($"            services.AddSingleton<{system}>(sp => sp.GetRequiredService<global::Pixely.Ecs.SystemScheduler>().Add(builder => new {system}({string.Join(", ", registration.ConstructorArguments)}), order));");
            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
