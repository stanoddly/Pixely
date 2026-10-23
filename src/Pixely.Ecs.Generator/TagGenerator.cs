using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pixely.Ecs.Generator;

// Generates nothing: it reports a tag that holds data, which the world would drop because a tag has no column.
[Generator]
public class TagGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<DiagnosticInfo?> results = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => node is TypeDeclarationSyntax { BaseList: not null } and (StructDeclarationSyntax or RecordDeclarationSyntax),
            transform: static (ctx, ct) => Extract(ctx, ct)).Where(static result => result is not null);

        context.RegisterSourceOutput(results, static (spc, diagnostic) => GeneratorSupport.Report(spc, diagnostic!.Value));
    }

    private static DiagnosticInfo? Extract(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        TypeDeclarationSyntax syntax = (TypeDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(syntax, cancellationToken) is not INamedTypeSymbol { TypeKind: TypeKind.Struct } type || !GeneratorSupport.IsTag(type))
        {
            return null;
        }

        // A partial tag reports once, on its first declaration that has a base list.
        if (type.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken)).OfType<TypeDeclarationSyntax>().First(declaration => declaration.BaseList is not null) != syntax)
        {
            return null;
        }

        // Fields include auto-property backing fields and captured primary constructor parameters.
        bool holdsData = type.GetMembers().Any(member => member is IFieldSymbol { IsStatic: false, IsConst: false });
        return holdsData
            ? GeneratorSupport.CreateDiagnostic("PXECS014", $"The tag {type.Name} must have no instance fields, because a tag has no column and its data would be dropped.", syntax.Identifier.GetLocation())
            : null;
    }
}
