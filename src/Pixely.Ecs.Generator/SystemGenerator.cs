using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pixely.DependencyInjection.Generator;

namespace Pixely.Ecs.Generator;

// The ISystem<...> a system implements, and for each of its type arguments the type and the SystemBuilder call that creates it.
readonly record struct SystemBinding(string Interface, EquatableArray<string> ArgumentTypes, EquatableArray<string> BuilderCalls);

// ConstructorArguments is null for SystemScheduler.Add, which gets the system already created.
readonly record struct SystemRegistration(string InterceptsLocationAttribute, string SystemType, EquatableArray<string>? ConstructorArguments, SystemBinding? Binding);

readonly record struct SystemExtraction(SystemRegistration? Registration, DiagnosticInfo? Diagnostic);

// Intercepts AddSystem<TSystem>() and SystemScheduler.Add(system). For an ISystem<...>, the generated code creates the queries and writers of its type arguments
// through the SystemBuilder, which records them as the system's contract, and calls Update with them, so the system itself needs no generated code.
// AddSystem also creates the system with services from the provider. An ISystem added with SystemScheduler.Add needs no generated code.
[Generator]
public class SystemGenerator : IIncrementalGenerator
{
    private const string ExtensionsName = "Pixely.Ecs.EcsServiceCollectionExtensions";
    private const string SchedulerName = "Pixely.Ecs.SystemScheduler";
    private const string SystemName = "Pixely.Ecs.ISystem";

    private static readonly Dictionary<string, string> Writers = new Dictionary<string, string>
    {
        ["Pixely.Ecs.ComponentAdder<T>"] = "ComponentAdder",
        ["Pixely.Ecs.ComponentRemover<T>"] = "ComponentRemover",
        ["Pixely.Ecs.Spawner<TBlueprint>"] = "Spawner"
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

    // AddSystem<T>() or Add(system), called directly, on a receiver or through ?.
    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        SimpleNameSyntax? name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            SimpleNameSyntax simpleName => simpleName,
            _ => null
        };

        return name is GenericNameSyntax { Identifier.Text: "AddSystem" } || (name?.Identifier.Text == "Add" && invocation.ArgumentList.Arguments.Count == 1);
    }

    private static SystemExtraction? Extract(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
        // Generators do not see each other's output, so a type argument such as Spawner<TBlueprint> of a [Blueprint] struct breaks its constraint here
        // and the call resolves only to a candidate. The final compilation, with the blueprint's generated part, resolves it to the same method.
        SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, cancellationToken);
        if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method)
        {
            return null;
        }

        string? containingType = method.ContainingType?.ToDisplayString();
        bool isAddSystem = method.Name == "AddSystem" && containingType == ExtensionsName;
        bool isSchedulerAdd = method.Name == "Add" && containingType == SchedulerName && method.Parameters.Length == 1;
        if (!isAddSystem && !isSchedulerAdd)
        {
            return null;
        }

        // A candidate of Add has no inferred type argument, so the system type comes from the argument.
        // A type parameter is known only where the generic code is called, so Add throws for it at runtime unless it is an ISystem.
        ITypeSymbol? systemType = isAddSystem ? method.TypeArguments[0] : context.SemanticModel.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression, cancellationToken).Type;
        if (systemType is null || (isSchedulerAdd && systemType is ITypeParameterSymbol))
        {
            return null;
        }

        // The generated code is outside the system, so the system must be accessible anywhere in the assembly.
        Compilation compilation = context.SemanticModel.Compilation;
        string systemName = systemType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        string call = isAddSystem ? $"AddSystem<{systemName}>()" : $"Add({systemName})";
        if (systemType is not INamedTypeSymbol { IsAbstract: false } namedType || GeneratorSupport.ContainsTypeParameter(systemType)
            || !compilation.IsSymbolAccessibleWithin(namedType, compilation.Assembly) || IsInFileLocalType(namedType))
        {
            return Fail("PXECS010", $"{call} requires a concrete class without type parameters, accessible anywhere in its assembly.", invocation);
        }

        INamedTypeSymbol[] bindingInterfaces = [.. namedType.AllInterfaces.Where(implemented => implemented.IsGenericType && implemented.ConstructedFrom.ToDisplayString().StartsWith(SystemName + "<"))];
        bool isSystem = namedType.AllInterfaces.Any(implemented => implemented.ToDisplayString() == SystemName);
        if (bindingInterfaces.Length + (isSystem ? 1 : 0) != 1)
        {
            return Fail("PXECS015", $"{call} requires {systemName} to implement exactly one of ISystem and ISystem<...>.", invocation);
        }

        // The registry of IUpdatable would update it too, so it would update twice per frame.
        if (namedType.AllInterfaces.Any(implemented => implemented.ToDisplayString() == "Pixely.IUpdatable"))
        {
            return Fail("PXECS017", $"{systemName} implements IUpdatable, so it would update twice per frame: once as an IUpdatable and once through the SystemScheduler. Remove IUpdatable.", invocation);
        }

        // SystemScheduler.Add handles an ISystem at runtime.
        if (isSchedulerAdd && isSystem)
        {
            return null;
        }

        SystemBinding? binding = null;
        if (bindingInterfaces.Length == 1)
        {
            SystemExtraction? failure = ExtractBinding(bindingInterfaces[0], systemName, compilation, invocation, out binding);
            if (failure is not null)
            {
                return failure;
            }
        }

        EquatableArray<string>? constructorArguments = null;
        if (isAddSystem)
        {
            IMethodSymbol[] constructors = [.. namedType.InstanceConstructors.Where(constructor => compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly))];
            if (constructors.Length != 1)
            {
                return Fail("PXECS011", $"{call} requires {systemName} to have exactly one public or internal constructor.", invocation);
            }

            if (constructors[0].Parameters.FirstOrDefault(parameter => parameter.RefKind is not (RefKind.None or RefKind.In)) is { } byReference)
            {
                return Fail("PXECS012", $"The constructor of {systemName} takes {byReference.Name} by reference, but its arguments are created for the call.", invocation);
            }

            if (constructors[0].Parameters.FirstOrDefault(parameter => IsQuery(parameter.Type) || GetWriterCall(parameter.Type) is not null) is { } contract)
            {
                return Fail("PXECS013", $"The constructor of {systemName} takes {contract.Name}, a query or writer. Name its type in the ISystem<...> of {systemName}, which passes it to Update; the constructor takes only services.",
                    invocation);
            }

            constructorArguments = new EquatableArray<string>([.. constructors[0].Parameters.Select(CreateArgument)]);
        }

        InterceptableLocation? location = context.SemanticModel.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return Fail("PXECS018", $"{call} cannot be intercepted where it is called, so it would throw. Call it as a method of its receiver.", invocation);
        }

        return new SystemExtraction(new SystemRegistration(location.GetInterceptsLocationAttributeSyntax(), systemType.ToDisplayString(TypeFormat), constructorArguments, binding), null);
    }

    private static SystemExtraction? ExtractBinding(INamedTypeSymbol bindingInterface, string systemName, Compilation compilation, InvocationExpressionSyntax invocation, out SystemBinding? binding)
    {
        binding = null;
        List<string> types = [];
        List<string> calls = [];
        foreach (ITypeSymbol typeArgument in bindingInterface.TypeArguments)
        {
            string argumentName = typeArgument.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            string type = typeArgument.ToDisplayString(TypeFormat);
            if (IsQuery(typeArgument))
            {
                SystemExtraction? failure = ValidateQuery((INamedTypeSymbol)typeArgument, argumentName, compilation, invocation);
                if (failure is not null)
                {
                    return failure;
                }

                calls.Add($"builder.Query<{type}>()");
            }
            else if (GetWriterCall(typeArgument) is { } writerCall)
            {
                calls.Add(writerCall);
            }
            else
            {
                return Fail("PXECS016", $"The type argument {argumentName} of the ISystem<...> of {systemName} must be a query, ComponentAdder<T>, ComponentRemover<T>, EntityDestroyer or Spawner<TBlueprint>. A service goes in the constructor.",
                    invocation);
            }

            types.Add(type);
        }

        binding = new SystemBinding(bindingInterface.ToDisplayString(TypeFormat), new EquatableArray<string>([.. types]), new EquatableArray<string>([.. calls]));
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

    private static bool IsQuery(ITypeSymbol type) => type is INamedTypeSymbol named && GeneratorSupport.GetQueryBase(named) is not null;

    private static string? GetWriterCall(ITypeSymbol type)
    {
        if (type.ToDisplayString() == "Pixely.Ecs.EntityDestroyer")
        {
            return "builder.EntityDestroyer()";
        }

        return type is INamedTypeSymbol { IsGenericType: true } generic && Writers.TryGetValue(generic.ConstructedFrom.ToDisplayString(), out string? method)
            ? $"builder.{method}<{generic.TypeArguments[0].ToDisplayString(TypeFormat)}>()"
            : null;
    }

    // The type arguments of every IWith<...> or IWithout<...> the query implements. The compiler allows at most one of each, because two would both implement the same member.
    private static List<string> GetFilters(INamedTypeSymbol type, string prefix) => [.. type.AllInterfaces
        .Where(implemented => implemented.IsGenericType && implemented.ConstructedFrom.ToDisplayString().StartsWith(prefix))
        .SelectMany(implemented => implemented.TypeArguments)
        .Select(argument => argument.ToDisplayString(TypeFormat))];

    private static string CreateArgument(IParameterSymbol parameter)
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

    private static SystemExtraction Fail(string id, string message, InvocationExpressionSyntax invocation) => new SystemExtraction(null, GeneratorSupport.CreateDiagnostic(id, message, invocation.GetLocation()));

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
            if (registration.ConstructorArguments is { } arguments)
            {
                // The provider creates singletons grouped by service type, and earlier when another service needs one, so the order is reserved when
                // AddSystem runs and the scheduler sorts by it. The system joins the scheduler when it is created, before any OnBuilt callback.
                string bind = registration.Binding is { } binding ? GenerateBind(binding, "target", "            ") : "static _ => static target => ((global::Pixely.Ecs.ISystem)target).Update()";
                sb.AppendLine($"        public static void AddSystem_{i}<TSystem>(this global::Pixely.DependencyInjection.ServiceCollection services) where TSystem : class");
                sb.AppendLine("        {");
                sb.AppendLine("            long order = global::Pixely.Ecs.SystemScheduler.ReserveOrder();");
                sb.AppendLine($"            services.AddSingleton<{system}>(sp => sp.GetRequiredService<global::Pixely.Ecs.SystemScheduler>().Add(new {system}({string.Join(", ", arguments)}), {bind}, order));");
                sb.AppendLine("        }");
            }
            else
            {
                // The interceptor is generic like Add, so the system reaches Update through object.
                sb.AppendLine($"        public static TSystem Add_{i}<TSystem>(this global::Pixely.Ecs.SystemScheduler scheduler, TSystem system) where TSystem : class");
                sb.AppendLine("        {");
                sb.AppendLine($"            return scheduler.Add(system, {GenerateBind(registration.Binding!.Value, "(object)target", "            ")}, global::Pixely.Ecs.SystemScheduler.ReserveOrder());");
                sb.AppendLine("        }");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // The function SystemScheduler.Add calls with the SystemBuilder: it creates each query and writer once and returns the call of Update with them.
    private static string GenerateBind(SystemBinding binding, string target, string indent)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("static builder =>");
        sb.AppendLine($"{indent}{{");
        for (int i = 0; i < binding.ArgumentTypes.Length; i++)
        {
            sb.AppendLine($"{indent}    {binding.ArgumentTypes[i]} item{i + 1} = {binding.BuilderCalls[i]};");
        }

        string items = string.Join(", ", Enumerable.Range(1, binding.ArgumentTypes.Length).Select(index => $"item{index}"));
        sb.AppendLine($"{indent}    return target => (({binding.Interface}){target}).Update({items});");
        sb.Append($"{indent}}}");
        return sb.ToString();
    }
}
