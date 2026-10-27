using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Pixely.DependencyInjection.Generator;

namespace Pixely.Ecs.Generator;

// A registration is "RegisterSpan" or "RegisterReadOnlySpan" with the component it reads.
readonly record struct TermRegistration(string Method, string Component);

readonly record struct TermExtraction(EquatableArray<TermRegistration> Registrations, DiagnosticInfo? Diagnostic);

// Registers every Span<T> and ReadOnlySpan<T> that a Query<...> in the assembly names, through a module initializer, because the library cannot get T
// from a span type argument without reflection. It finds them wherever Query<...> is written: in the base list of a query class, and in a call of
// World.Query or SystemBuilder.Query. A query whose Query<...> comes through a generic base, such as Positions : ComponentQuery<Position>, is found
// through its declaration, or through the closed type named in World.Query or SystemBuilder.Query, or by the constructor of a system that AddSystem
// names. The constructor is read from the closed system type, so a generic system's queries are closed with its type arguments.
[Generator]
public class QueryTermGenerator : IIncrementalGenerator
{
    private const string ExtensionsName = "Pixely.Ecs.EcsServiceCollectionExtensions";
    private const string WorldName = "Pixely.Ecs.World";
    private const string BuilderName = "Pixely.Ecs.SystemBuilder";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<TermExtraction?> results = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => IsCandidate(node),
            transform: static (ctx, ct) => Extract(ctx, ct)).Where(static result => result is not null);

        context.RegisterSourceOutput(results.Collect(), static (spc, results) =>
        {
            SortedSet<string> lines = new SortedSet<string>(StringComparer.Ordinal);
            foreach (TermExtraction? result in results)
            {
                if (result!.Value.Diagnostic is { } diagnostic)
                {
                    GeneratorSupport.Report(spc, diagnostic);
                    continue;
                }

                foreach (TermRegistration registration in result.Value.Registrations)
                {
                    lines.Add($"global::Pixely.Ecs.QueryTerms.{registration.Method}<{registration.Component}>();");
                }
            }

            if (lines.Count > 0)
            {
                spc.AddSource("QueryTerms.g.cs", Generate(lines));
            }
        });
    }

    // Query<...>, a Query or AddSystem call, or a class with a base list. The type arguments are read from the symbols, so qualified and aliased names
    // count too.
    private static bool IsCandidate(SyntaxNode node) => node is GenericNameSyntax { Identifier.Text: "Query" or "AddSystem" } or ClassDeclarationSyntax { BaseList: not null };

    private static TermExtraction? Extract(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.Node is ClassDeclarationSyntax declaration)
        {
            // A base that is Query<...> itself is handled where Query<...> is written, even in an alias, and a generic class is closed elsewhere.
            if (context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol { IsGenericType: false, BaseType: { } baseType }
                || GeneratorSupport.IsQueryType(baseType) || GeneratorSupport.GetQueryBase(baseType) is not { } inherited)
            {
                return null;
            }

            return ExtractTerms(inherited, declaration.Identifier.GetLocation());
        }

        GenericNameSyntax syntax = (GenericNameSyntax)context.Node;
        return context.SemanticModel.GetSymbolInfo(syntax, cancellationToken).Symbol switch
        {
            INamedTypeSymbol type when GeneratorSupport.IsQueryType(type) => ExtractTerms(type, syntax.GetLocation()),
            IMethodSymbol { Name: "Query", TypeArguments.Length: 1 } method when method.ContainingType.ToDisplayString() is WorldName or BuilderName =>
                ExtractClosedQueries(method.TypeArguments, syntax.GetLocation()),
            // nameof adds no system.
            IMethodSymbol { Name: "AddSystem", TypeArguments.Length: 1 } method when method.ContainingType.ToDisplayString() == ExtensionsName
                && method.TypeArguments[0] is INamedTypeSymbol system
                && !syntax.Ancestors().OfType<InvocationExpressionSyntax>().Any(ancestor => context.SemanticModel.GetOperation(ancestor, cancellationToken) is INameOfOperation) =>
                ExtractConstructorQueries(system, context.SemanticModel.Compilation, syntax.GetLocation()),
            _ => null
        };
    }

    // The closed generic query classes among the types, such as Of<Position>. Query<...> itself is handled where it is written, and a
    // non-generic query class through its declaration.
    private static TermExtraction? ExtractClosedQueries(IEnumerable<ITypeSymbol> types, Location location)
    {
        List<TermRegistration> registrations = [];
        foreach (ITypeSymbol type in types)
        {
            if (type is not INamedTypeSymbol { IsGenericType: true } query || GeneratorSupport.ContainsTypeParameter(query) || GeneratorSupport.IsQueryType(query)
                || GeneratorSupport.GetQueryBase(query) is not { } queryBase)
            {
                continue;
            }

            TermExtraction? terms = ExtractTerms(queryBase, location);
            if (terms?.Diagnostic is not null)
            {
                return terms;
            }

            registrations.AddRange(terms!.Value.Registrations);
        }

        return registrations.Count == 0 ? null : new TermExtraction(new EquatableArray<TermRegistration>([.. registrations]), null);
    }

    // The queries a system's constructor takes, read from the closed system type. Only the constructor the SystemGenerator calls counts, and without
    // exactly one the SystemGenerator reports the system. A Query<...> written with a type parameter of the system, such as Query<Span<T>> in S<T>, is
    // closed only here; one written closed is handled where it is written.
    private static TermExtraction? ExtractConstructorQueries(INamedTypeSymbol system, Compilation compilation, Location location)
    {
        IMethodSymbol[] constructors = [.. system.InstanceConstructors.Where(constructor => compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly))];
        if (GeneratorSupport.ContainsTypeParameter(system) || constructors.Length != 1)
        {
            return null;
        }

        List<TermRegistration> registrations = [];
        List<ITypeSymbol> others = [];
        foreach (IParameterSymbol parameter in constructors[0].Parameters)
        {
            if (parameter.OriginalDefinition.Type is not INamedTypeSymbol written || !GeneratorSupport.ContainsTypeParameter(written) || parameter.Type is not INamedTypeSymbol query
                || !GeneratorSupport.IsQueryType(query))
            {
                others.Add(parameter.Type);
                continue;
            }

            // A problem the open query already has is reported where it is written, so only a problem that closing it adds is reported here.
            TermExtraction? terms = ExtractTerms(query, location);
            if (terms?.Diagnostic is not null)
            {
                if (ExtractTerms(written, location)?.Diagnostic is not null)
                {
                    continue;
                }

                return terms;
            }

            registrations.AddRange(terms!.Value.Registrations);
        }

        TermExtraction? closed = ExtractClosedQueries(others, location);
        if (closed?.Diagnostic is not null)
        {
            return closed;
        }

        if (closed is { } closedTerms)
        {
            registrations.AddRange(closedTerms.Registrations);
        }

        return registrations.Count == 0 ? null : new TermExtraction(new EquatableArray<TermRegistration>([.. registrations]), null);
    }

    private static TermExtraction? ExtractTerms(INamedTypeSymbol query, Location location)
    {
        string queryName = query.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        List<TermRegistration> registrations = [];
        List<string> read = [];
        foreach (ITypeSymbol typeArgument in query.TypeArguments)
        {
            // An open type argument, as in a helper that takes Query<T1>, is closed where the query is written with its spans, which registers them.
            if (GeneratorSupport.ContainsTypeParameter(typeArgument))
            {
                continue;
            }

            SpanKind kind = GeneratorSupport.ClassifySpan(typeArgument, out ITypeSymbol? element);
            string argumentName = typeArgument.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            switch (kind)
            {
                case SpanKind.Entities:
                    continue;
                case SpanKind.Tag:
                    return Fail("PXECS002", $"{queryName} reads {element!.Name}, a tag, which has no data. Use IWith<{element.Name}> instead.", location);
                case SpanKind.Invalid:
                    return Fail("PXECS002", $"The type argument {argumentName} of {queryName} must be Span<T> or ReadOnlySpan<T> of a struct component that is not a ref struct, or ReadOnlySpan<Entity>.", location);
            }

            string component = element!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (read.Contains(component))
            {
                return Fail("PXECS003", $"{queryName} reads {element.Name} in more than one span.", location);
            }

            read.Add(component);
            registrations.Add(new TermRegistration(kind == SpanKind.Span ? "RegisterSpan" : "RegisterReadOnlySpan", component));
        }

        return new TermExtraction(new EquatableArray<TermRegistration>([.. registrations]), null);
    }

    private static TermExtraction Fail(string id, string message, Location location) =>
        new TermExtraction(new EquatableArray<TermRegistration>([]), GeneratorSupport.CreateDiagnostic(id, message, location));

    private static string Generate(IEnumerable<string> lines)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Pixely.Ecs.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    internal static class QueryTermRegistration");
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Register()");
        sb.AppendLine("        {");
        foreach (string line in lines)
        {
            sb.AppendLine($"            {line}");
        }

        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
