using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pixely.DependencyInjection.Generator;

namespace Pixely.Ecs.Generator;

readonly record struct SystemRegistration(string InterceptsLocationAttribute, string SystemType, EquatableArray<string> Arguments);

readonly record struct SystemExtraction(SystemRegistration? Registration, DiagnosticInfo? Diagnostic);

// Intercepts AddSystem<TSystem>() and creates TSystem through SystemScheduler.Add: the SystemBuilder creates the parameters that are
// queries and writers, so they become the system's contract, and the service provider resolves the rest.
[Generator]
public class SystemGenerator : IIncrementalGenerator
{
    private const string ExtensionsName = "Pixely.Ecs.EcsServiceCollectionExtensions";

    private static readonly Dictionary<string, string> BuilderMethods = new Dictionary<string, string>
    {
        ["Pixely.Ecs.Query<TQuery>"] = "Query",
        ["Pixely.Ecs.ComponentAdder<T>"] = "ComponentAdder",
        ["Pixely.Ecs.ComponentRemover<T>"] = "ComponentRemover",
        ["Pixely.Ecs.Spawner<TBlueprint>"] = "Spawner"
    };

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<SystemExtraction?> results = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.Text: "AddSystem" } } }
                or InvocationExpressionSyntax { Expression: GenericNameSyntax { Identifier.Text: "AddSystem" } },
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

    private static SystemExtraction? Extract(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { Name: "AddSystem" } method
            || method.ContainingType?.ToDisplayString() != ExtensionsName)
        {
            return null;
        }

        InterceptableLocation? location = context.SemanticModel.GetInterceptableLocation(invocation, cancellationToken);
        if (location is null)
        {
            return null;
        }

        ITypeSymbol systemType = method.TypeArguments[0];
        string systemName = systemType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (systemType is not INamedTypeSymbol { IsAbstract: false } namedType || ContainsTypeParameter(systemType))
        {
            return Fail("PXECS010", $"AddSystem<{systemName}>() requires a concrete class without type parameters.", invocation);
        }

        IMethodSymbol[] constructors = namedType.InstanceConstructors.Where(constructor => context.SemanticModel.IsAccessible(invocation.SpanStart, constructor)).ToArray();
        if (constructors.Length != 1)
        {
            return Fail("PXECS011", $"AddSystem<{systemName}>() requires {systemName} to have exactly one constructor accessible at the call site.", invocation);
        }

        ImmutableArray<string> arguments = [.. constructors[0].Parameters.Select(CreateArgument)];
        return new SystemExtraction(new SystemRegistration(location.GetInterceptsLocationAttributeSyntax(), systemType.ToDisplayString(TypeFormat), new EquatableArray<string>(arguments)), null);
    }

    private static string CreateArgument(IParameterSymbol parameter)
    {
        ITypeSymbol type = parameter.Type;
        if (type.ToDisplayString() == "Pixely.Ecs.EntityDestroyer")
        {
            return "builder.EntityDestroyer()";
        }

        if (type is INamedTypeSymbol { IsGenericType: true } generic)
        {
            if (BuilderMethods.TryGetValue(generic.ConstructedFrom.ToDisplayString(), out string? builderMethod))
            {
                return $"builder.{builderMethod}<{generic.TypeArguments[0].ToDisplayString(TypeFormat)}>()";
            }

            if (generic.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return $"sp.GetServices<{generic.TypeArguments[0].WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat)}>()";
            }
        }

        string resolutionType = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat);
        return type.IsReferenceType && parameter.NullableAnnotation == NullableAnnotation.Annotated
            ? $"sp.GetService<{resolutionType}>()"
            : $"sp.GetRequiredService<{resolutionType}>()";
    }

    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter) || (named.ContainingType is not null && ContainsTypeParameter(named.ContainingType)),
        _ => false
    };

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

            sb.AppendLine($"        {registration.InterceptsLocationAttribute}");
            sb.AppendLine($"        public static void AddSystem_{i}<TSystem>(this global::Pixely.DependencyInjection.ServiceCollection services) where TSystem : class, global::Pixely.Ecs.ISystem");
            sb.AppendLine("        {");
            sb.AppendLine($"            services.AddSingleton<global::Pixely.Ecs.ISystem, {registration.SystemType}>(static sp => sp.GetRequiredService<global::Pixely.Ecs.SystemScheduler>()");
            sb.AppendLine($"                .Add(builder => new {registration.SystemType}({string.Join(", ", registration.Arguments)})));");
            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
