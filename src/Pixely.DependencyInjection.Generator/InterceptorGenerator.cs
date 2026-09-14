using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pixely.DependencyInjection.Generator;

enum InterceptionKind
{
    AddSingleton,
    AddSingletonWithAlias,
    AddSingletonInstanceFactory,
    AddSingletonFactory,
    AddTransient,
    AddTransientWithAlias,
    AddTransientInstanceFactory,
    AddTransientFactory,
    OnBuilt,
    GetRequiredServiceEnumerable,
    GetServiceEnumerable
}

enum ServiceResolutionKind
{
    Required,
    Optional,
    Collection
}

readonly record struct ExtractionResult(InterceptionInfo? Interception, DiagnosticInfo? Diagnostic);

readonly record struct DiagnosticInfo(string Id, string Message, string FilePath, int Line, int Column, int EndLine, int EndColumn);

readonly record struct ServiceParameter(
    string DeclaredTypeFullName,
    string ResolutionTypeFullName,
    ServiceResolutionKind ResolutionKind);

readonly record struct InterceptionInfo(
    InterceptionKind Kind,
    string InterceptsLocationAttribute,
    string? ImplementationTypeFullName,
    EquatableArray<ServiceParameter> Parameters,
    string? ServiceTypeFullName,
    string? DelegateTypeFullName,
    bool ReturnsVoid,
    string? FactoryTypeFullName,
    string? FactoryMethodName
);

[Generator]
public class InterceptorGenerator : IIncrementalGenerator
{
    private const string ServiceCollectionFullName = "Pixely.DependencyInjection.ServiceCollection";
    private const string ServiceProviderFullName = "Pixely.DependencyInjection.ServiceProvider";

    private const string EmitTrimAnnotationsProperty = "PixelyDIEmitTrimAnnotations";
    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ExtractionResult?> results = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) => IsCandidateInvocation(node),
            transform: static (ctx, ct) => ExtractInterception(ctx, ct)
        ).Where(static r => r is not null);

        IncrementalValueProvider<ImmutableArray<ExtractionResult?>> collected = results.Collect();

        IncrementalValueProvider<bool> emitTrimAnnotations = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) =>
            {
                if (options.GlobalOptions.TryGetValue(
                        $"build_property.{EmitTrimAnnotationsProperty}",
                        out string? value)
                    && string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            });

        IncrementalValueProvider<(ImmutableArray<ExtractionResult?> Items, bool EmitTrimAnnotations)> combined =
            collected.Combine(emitTrimAnnotations);

        context.RegisterSourceOutput(combined, static (spc, input) =>
        {
            ImmutableArray<ExtractionResult?> items = input.Items;
            bool emitAnnotations = input.EmitTrimAnnotations;

            ImmutableArray<InterceptionInfo>.Builder interceptions = ImmutableArray.CreateBuilder<InterceptionInfo>();

            foreach (ExtractionResult? item in items)
            {
                if (item is not { } result)
                {
                    continue;
                }

                if (result.Diagnostic is { } diag)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            diag.Id,
                            "Pixely.DependencyInjection",
                            diag.Message,
                            "Pixely.DependencyInjection",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.Create(
                            diag.FilePath,
                            default,
                            new LinePositionSpan(
                                new LinePosition(diag.Line, diag.Column),
                                new LinePosition(diag.EndLine, diag.EndColumn)))));
                }

                if (result.Interception is { } interception)
                {
                    interceptions.Add(interception);
                }
            }

            if (interceptions.Count == 0)
            {
                return;
            }

            string source = GenerateInterceptors(interceptions.ToImmutable(), emitAnnotations);
            spc.AddSource("ServiceCollectionInterceptors.g.cs", source);
        });
    }

    private static bool IsCandidateInvocation(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        string? methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name switch
            {
                GenericNameSyntax g => g.Identifier.Text,
                IdentifierNameSyntax id => id.Identifier.Text,
                _ => null
            },
            GenericNameSyntax g => g.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };

        return methodName is "AddSingleton" or "AddTransient" or "OnBuilt" or "GetRequiredService" or "GetService";
    }

    private static ExtractionResult? ExtractInterception(GeneratorSyntaxContext context, CancellationToken ct)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;

        SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, ct);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        string? containingType = methodSymbol.ContainingType?.ToDisplayString();

        if (containingType == ServiceProviderFullName)
        {
            InterceptableLocation? providerLocation = context.SemanticModel.GetInterceptableLocation(invocation, ct);
            if (providerLocation == null)
            {
                return null;
            }

            string providerMethodName = methodSymbol.Name;

            if (providerMethodName is "GetRequiredService" or "GetService")
            {
                return ExtractGetServiceEnumerable(methodSymbol, providerMethodName, providerLocation);
            }

            return null;
        }

        if (containingType != ServiceCollectionFullName)
        {
            return null;
        }

        InterceptableLocation? interceptableLocation = context.SemanticModel.GetInterceptableLocation(invocation, ct);
        if (interceptableLocation == null)
        {
            return null;
        }

        string methodName = methodSymbol.Name;

        if (methodName == "AddSingleton")
        {
            return ExtractAddLifetimeRegistration(
                methodSymbol,
                invocation,
                context,
                interceptableLocation,
                ct,
                InterceptionKind.AddSingleton,
                InterceptionKind.AddSingletonWithAlias,
                InterceptionKind.AddSingletonInstanceFactory,
                InterceptionKind.AddSingletonFactory,
                "AddSingleton");
        }

        if (methodName == "AddTransient")
        {
            return ExtractAddLifetimeRegistration(
                methodSymbol,
                invocation,
                context,
                interceptableLocation,
                ct,
                InterceptionKind.AddTransient,
                InterceptionKind.AddTransientWithAlias,
                InterceptionKind.AddTransientInstanceFactory,
                InterceptionKind.AddTransientFactory,
                "AddTransient");
        }

        if (methodName == "OnBuilt")
        {
            InterceptionInfo? onBuilt = ExtractOnBuilt(methodSymbol, invocation, context, interceptableLocation, ct);
            return onBuilt is { } info ? new ExtractionResult(info, null) : null;
        }

        return null;
    }

    private static ExtractionResult? ExtractGetServiceEnumerable(
        IMethodSymbol methodSymbol,
        string calledMethodName,
        InterceptableLocation interceptableLocation)
    {
        if (methodSymbol.TypeArguments.Length != 1)
        {
            return null;
        }

        ITypeSymbol typeArg = methodSymbol.TypeArguments[0];

        // Only intercept when type argument is IEnumerable<T>
        if (typeArg is not INamedTypeSymbol namedTypeArg)
        {
            return null;
        }

        if (namedTypeArg.ConstructedFrom.SpecialType != SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return null;
        }

        if (namedTypeArg.TypeArguments.Length != 1)
        {
            return null;
        }

        string elementTypeFullName = GetResolutionTypeName(namedTypeArg.TypeArguments[0]);

        InterceptionKind kind = calledMethodName == "GetRequiredService"
            ? InterceptionKind.GetRequiredServiceEnumerable
            : InterceptionKind.GetServiceEnumerable;

        return new ExtractionResult(new InterceptionInfo(
            kind,
            interceptableLocation.GetInterceptsLocationAttributeSyntax(),
            null,
            new EquatableArray<ServiceParameter>(ImmutableArray<ServiceParameter>.Empty),
            elementTypeFullName,
            null,
            ReturnsVoid: false,
            null,
            null
        ), null);
    }

    private static ExtractionResult? ExtractAddLifetimeRegistration(
        IMethodSymbol methodSymbol,
        InvocationExpressionSyntax invocation,
        GeneratorSyntaxContext context,
        InterceptableLocation interceptableLocation,
        CancellationToken ct,
        InterceptionKind typeKind,
        InterceptionKind implementationKind,
        InterceptionKind instanceFactoryKind,
        InterceptionKind delegateFactoryKind,
        string methodDisplayName)
    {
        // Add{Lifetime}<T>(Delegate) — factory overload.
        // Must check parameter type to distinguish from singleton AddSingleton<T>(T instance).
        if (methodSymbol.TypeArguments.Length == 1 && methodSymbol.Parameters.Length == 1
            && methodSymbol.Parameters[0].Type.ToDisplayString() == "System.Delegate")
        {
            string serviceTypeFullName = GetTypeName(methodSymbol.TypeArguments[0]);
            InterceptionInfo? result = ExtractDelegateInterception(
                delegateFactoryKind,
                invocation,
                context,
                interceptableLocation,
                ct,
                serviceTypeFullName);
            return result is { } info ? new ExtractionResult(info, null) : null;
        }

        // Add{Lifetime}<T>() — single type param, no args
        if (methodSymbol.TypeArguments.Length == 1 && methodSymbol.Parameters.Length == 0)
        {
            return ExtractAddLifetimeType(methodSymbol, invocation, context, interceptableLocation, typeKind, methodDisplayName);
        }

        // Add{Lifetime}<TService, TImpl>() — two type params, no args
        if (methodSymbol.TypeArguments.Length == 2 && methodSymbol.Parameters.Length == 0)
        {
            ITypeSymbol serviceType = methodSymbol.TypeArguments[0];
            ITypeSymbol secondType = methodSymbol.TypeArguments[1];
            Conversion conversion = context.SemanticModel.Compilation.ClassifyConversion(secondType, serviceType);
            if (conversion.IsImplicit || conversion.IsIdentity)
            {
                return ExtractAddLifetimeWithImplementation(
                    methodSymbol,
                    invocation,
                    context,
                    interceptableLocation,
                    implementationKind,
                    methodDisplayName);
            }
            else
            {
                return ExtractAddLifetimeInstanceFactory(
                    methodSymbol,
                    invocation,
                    context,
                    interceptableLocation,
                    instanceFactoryKind,
                    methodDisplayName);
            }
        }

        return null;
    }

    private static ExtractionResult? ExtractAddLifetimeType(
        IMethodSymbol methodSymbol,
        InvocationExpressionSyntax invocation,
        GeneratorSyntaxContext context,
        InterceptableLocation interceptableLocation,
        InterceptionKind kind,
        string methodDisplayName)
    {
        ITypeSymbol implType = methodSymbol.TypeArguments[0];

        string implementationTypeName = GetDiagnosticTypeName(implType);

        if (ContainsTypeParameter(implType))
        {
            return new ExtractionResult(null, CreateDiagnostic(invocation, "GK0001",
                $"{methodDisplayName}<{implementationTypeName}>() cannot be used when the implementation type is or contains a type parameter. Use {methodDisplayName}<{implementationTypeName}>(Func<ServiceProvider, {implementationTypeName}>) instead."));
        }

        if (implType is not INamedTypeSymbol implNamedType)
        {
            return new ExtractionResult(null, CreateDiagnostic(invocation, "GK0001",
                $"{methodDisplayName}<{implementationTypeName}>() requires the implementation type to be a named concrete type."));
        }

        IMethodSymbol? constructor = GetSingleAccessibleConstructor(implNamedType, context.SemanticModel, invocation.SpanStart);
        if (constructor == null)
        {
            return new ExtractionResult(null, CreateDiagnostic(invocation, "GK0002",
                $"{methodDisplayName}<{implementationTypeName}>() requires exactly one constructor accessible at the call site."));
        }

        ImmutableArray<ServiceParameter> parameters = constructor.Parameters
            .Select(CreateServiceParameter)
            .ToImmutableArray();

        return new ExtractionResult(new InterceptionInfo(
            kind,
            interceptableLocation.GetInterceptsLocationAttributeSyntax(),
            GetTypeName(implType),
            new EquatableArray<ServiceParameter>(parameters),
            null,
            null,
            ReturnsVoid: false,
            null,
            null
        ), null);
    }

    private static ExtractionResult? ExtractAddLifetimeWithImplementation(
        IMethodSymbol methodSymbol,
        InvocationExpressionSyntax invocation,
        GeneratorSyntaxContext context,
        InterceptableLocation interceptableLocation,
        InterceptionKind kind,
        string methodDisplayName)
    {
        ITypeSymbol serviceType = methodSymbol.TypeArguments[0];
        ITypeSymbol implType = methodSymbol.TypeArguments[1];
        string serviceTypeName = GetDiagnosticTypeName(serviceType);
        string implementationTypeName = GetDiagnosticTypeName(implType);

        if (ContainsTypeParameter(implType))
        {
            return new ExtractionResult(null, CreateDiagnostic(invocation, "GK0001",
                $"{methodDisplayName}<{serviceTypeName}, {implementationTypeName}>() cannot be used when the implementation type is or contains a type parameter. Use {methodDisplayName}<{serviceTypeName}, {implementationTypeName}>(Func<ServiceProvider, {implementationTypeName}>) instead."));
        }

        if (implType is not INamedTypeSymbol implNamedType)
        {
            return new ExtractionResult(null, CreateDiagnostic(invocation, "GK0001",
                $"{methodDisplayName}<{serviceTypeName}, {implementationTypeName}>() requires the implementation type to be a named concrete type."));
        }

        IMethodSymbol? constructor = GetSingleAccessibleConstructor(implNamedType, context.SemanticModel, invocation.SpanStart);
        if (constructor == null)
        {
            return new ExtractionResult(null, CreateDiagnostic(invocation, "GK0002",
                $"{methodDisplayName}<{serviceTypeName}, {implementationTypeName}>() requires {implementationTypeName} to have exactly one constructor accessible at the call site."));
        }

        ImmutableArray<ServiceParameter> parameters = constructor.Parameters
            .Select(CreateServiceParameter)
            .ToImmutableArray();

        return new ExtractionResult(new InterceptionInfo(
            kind,
            interceptableLocation.GetInterceptsLocationAttributeSyntax(),
            GetTypeName(implType),
            new EquatableArray<ServiceParameter>(parameters),
            GetTypeName(serviceType),
            null,
            ReturnsVoid: false,
            null,
            null
        ), null);
    }

    private static ExtractionResult? ExtractAddLifetimeInstanceFactory(
        IMethodSymbol methodSymbol,
        InvocationExpressionSyntax invocation,
        GeneratorSyntaxContext context,
        InterceptableLocation interceptableLocation,
        InterceptionKind kind,
        string methodDisplayName)
    {
        ITypeSymbol serviceType = methodSymbol.TypeArguments[0];
        ITypeSymbol factoryType = methodSymbol.TypeArguments[1];
        string serviceTypeName = GetDiagnosticTypeName(serviceType);
        string factoryTypeName = GetDiagnosticTypeName(factoryType);

        if (factoryType is not INamedTypeSymbol namedFactoryType)
        {
            return new ExtractionResult(null, CreateDiagnostic(invocation, "GK0003",
                $"{methodDisplayName}<{serviceTypeName}, {factoryTypeName}>() requires {factoryTypeName} to be a concrete type."));
        }

        List<IMethodSymbol> candidates = new();
        foreach (IMethodSymbol method in GetAccessibleInstanceMethods(namedFactoryType, context.SemanticModel, invocation.SpanStart))
        {
            Conversion returnConversion = context.SemanticModel.Compilation.ClassifyConversion(method.ReturnType, serviceType);
            if (returnConversion.IsImplicit || returnConversion.IsIdentity)
            {
                candidates.Add(method);
            }
        }

        if (candidates.Count == 0)
        {
            return new ExtractionResult(null, CreateDiagnostic(invocation, "GK0003",
                $"{methodDisplayName}<{serviceTypeName}, {factoryTypeName}>() requires {factoryTypeName} to have an accessible instance method returning {serviceTypeName}."));
        }

        if (candidates.Count > 1)
        {
            return new ExtractionResult(null, CreateDiagnostic(invocation, "GK0004",
                $"{methodDisplayName}<{serviceTypeName}, {factoryTypeName}>() found multiple methods on {factoryTypeName} returning {serviceTypeName}: {string.Join(", ", candidates.Select(m => m.Name))}."));
        }

        IMethodSymbol factoryMethod = candidates[0];
        ImmutableArray<ServiceParameter> parameters = factoryMethod.Parameters
            .Select(CreateServiceParameter)
            .ToImmutableArray();

        string serviceTypeFullName = GetTypeName(serviceType);
        string factoryTypeFullName = GetTypeName(factoryType);

        return new ExtractionResult(new InterceptionInfo(
            kind,
            interceptableLocation.GetInterceptsLocationAttributeSyntax(),
            null,
            new EquatableArray<ServiceParameter>(parameters),
            serviceTypeFullName,
            null,
            ReturnsVoid: false,
            factoryTypeFullName,
            factoryMethod.Name
        ), null);
    }

    private static InterceptionInfo? ExtractOnBuilt(
        IMethodSymbol methodSymbol,
        InvocationExpressionSyntax invocation,
        GeneratorSyntaxContext context,
        InterceptableLocation interceptableLocation,
        CancellationToken ct)
    {
        if (methodSymbol.Parameters.Length != 1)
        {
            return null;
        }

        return ExtractDelegateInterception(
            InterceptionKind.OnBuilt,
            invocation,
            context,
            interceptableLocation,
            ct,
            null);
    }

    private static InterceptionInfo? ExtractDelegateInterception(
        InterceptionKind kind,
        InvocationExpressionSyntax invocation,
        GeneratorSyntaxContext context,
        InterceptableLocation interceptableLocation,
        CancellationToken ct,
        string? serviceTypeFullName)
    {
        if (invocation.ArgumentList.Arguments.Count != 1)
        {
            return null;
        }

        ExpressionSyntax argExpression = invocation.ArgumentList.Arguments[0].Expression;

        // Get the type info of the delegate argument
        TypeInfo typeInfo = context.SemanticModel.GetTypeInfo(argExpression, ct);
        ITypeSymbol? delegateType = typeInfo.Type ?? typeInfo.ConvertedType;

        if (delegateType is INamedTypeSymbol namedDelegateType && namedDelegateType.DelegateInvokeMethod != null)
        {
            IMethodSymbol invokeMethod = namedDelegateType.DelegateInvokeMethod;

            ImmutableArray<ServiceParameter> delegateParameters = invokeMethod.Parameters
                .Select(CreateServiceParameter)
                .ToImmutableArray();

            string delegateTypeStr = GetTypeName(namedDelegateType);
            bool returnsVoid = invokeMethod.ReturnsVoid;

            return new InterceptionInfo(
                kind,
                interceptableLocation.GetInterceptsLocationAttributeSyntax(),
                null,
                new EquatableArray<ServiceParameter>(delegateParameters),
                serviceTypeFullName,
                delegateTypeStr,
                returnsVoid,
                null,
                null
            );
        }

        // Method group: the argument has no concrete delegate type, but GetSymbolInfo
        // resolves the target method directly.
        SymbolInfo argSymbolInfo = context.SemanticModel.GetSymbolInfo(argExpression, ct);
        IMethodSymbol? targetMethod = argSymbolInfo.Symbol as IMethodSymbol
            ?? argSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (targetMethod == null)
        {
            return null;
        }

        ImmutableArray<ServiceParameter> methodParameters = targetMethod.Parameters
            .Select(CreateServiceParameter)
            .ToImmutableArray();

        // Build the appropriate Func<> or Action<> delegate type string
        string methodDelegateTypeStr;
        bool methodReturnsVoid = targetMethod.ReturnsVoid;

        if (methodReturnsVoid)
        {
            if (methodParameters.Length == 0)
            {
                methodDelegateTypeStr = "global::System.Action";
            }
            else
            {
                methodDelegateTypeStr = $"global::System.Action<{string.Join(", ", methodParameters.Select(p => p.DeclaredTypeFullName))}>";
            }
        }
        else
        {
            string returnType = GetTypeName(targetMethod.ReturnType);
            if (methodParameters.Length == 0)
            {
                methodDelegateTypeStr = $"global::System.Func<{returnType}>";
            }
            else
            {
                methodDelegateTypeStr = $"global::System.Func<{string.Join(", ", methodParameters.Select(p => p.DeclaredTypeFullName))}, {returnType}>";
            }
        }

        return new InterceptionInfo(
            kind,
            interceptableLocation.GetInterceptsLocationAttributeSyntax(),
            null,
            new EquatableArray<ServiceParameter>(methodParameters),
            serviceTypeFullName,
            methodDelegateTypeStr,
            methodReturnsVoid,
            null,
            null
        );
    }

    private static IEnumerable<IMethodSymbol> GetAccessibleInstanceMethods(
        INamedTypeSymbol type, SemanticModel semanticModel, int position)
    {
        INamedTypeSymbol? current = type;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (IMethodSymbol method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (!semanticModel.IsAccessible(position, method))
                {
                    continue;
                }

                yield return method;
            }

            current = current.BaseType;
        }
    }

    private static string GetTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(FullyQualifiedNullableFormat);
    }

    private static ServiceParameter CreateServiceParameter(IParameterSymbol parameter)
    {
        ITypeSymbol type = parameter.Type;
        string declaredTypeFullName = GetTypeName(type);

        if (type is INamedTypeSymbol namedType &&
            namedType.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return new ServiceParameter(
                declaredTypeFullName,
                GetResolutionTypeName(namedType.TypeArguments[0]),
                ServiceResolutionKind.Collection);
        }

        ServiceResolutionKind resolutionKind = type.IsReferenceType &&
            parameter.NullableAnnotation == NullableAnnotation.Annotated
                ? ServiceResolutionKind.Optional
                : ServiceResolutionKind.Required;

        return new ServiceParameter(
            declaredTypeFullName,
            GetResolutionTypeName(type),
            resolutionKind);
    }

    private static string GetResolutionTypeName(ITypeSymbol type)
    {
        ITypeSymbol resolutionType = type.IsReferenceType
            ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            : type;
        return GetTypeName(resolutionType);
    }

    private static string GetDiagnosticTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol)
        {
            return true;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return ContainsTypeParameter(arrayType.ElementType);
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (namedType.ContainingType != null && ContainsTypeParameter(namedType.ContainingType))
        {
            return true;
        }

        foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
        {
            if (ContainsTypeParameter(typeArgument))
            {
                return true;
            }
        }

        return false;
    }

    private static IMethodSymbol? GetSingleAccessibleConstructor(INamedTypeSymbol type, SemanticModel semanticModel, int position)
    {
        IMethodSymbol[] accessibleConstructors = type.InstanceConstructors
            .Where(c => semanticModel.IsAccessible(position, c))
            .ToArray();

        if (accessibleConstructors.Length == 1)
        {
            return accessibleConstructors[0];
        }

        // Allow implicit parameterless constructor
        if (accessibleConstructors.Length == 0)
        {
            IMethodSymbol? implicitCtor = type.InstanceConstructors
                .FirstOrDefault(c => c.IsImplicitlyDeclared && c.Parameters.Length == 0 && semanticModel.IsAccessible(position, c));
            return implicitCtor;
        }

        // Multiple constructors — callers emit a GK0002 diagnostic
        return null;
    }

    private static DiagnosticInfo CreateDiagnostic(InvocationExpressionSyntax invocation, string id, string message)
    {
        FileLinePositionSpan span = invocation.SyntaxTree.GetLineSpan(invocation.Span);
        return new DiagnosticInfo(
            id,
            message,
            span.Path,
            span.StartLinePosition.Line,
            span.StartLinePosition.Character,
            span.EndLinePosition.Line,
            span.EndLinePosition.Character);
    }

    private const string DynamicallyAccessedMembersAttribute =
        "[global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]";

    private static string GenerateInterceptors(ImmutableArray<InterceptionInfo> interceptions, bool emitTrimAnnotations)
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
        sb.AppendLine("namespace Pixely.DependencyInjection.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    file static class ServiceCollectionInterceptors");
        sb.AppendLine("    {");

        for (int i = 0; i < interceptions.Length; i++)
        {
            InterceptionInfo info = interceptions[i];

            if (i > 0)
            {
                sb.AppendLine();
            }

            switch (info.Kind)
            {
                case InterceptionKind.AddSingleton:
                    GenerateAddSingletonInterceptor(sb, info, i, emitTrimAnnotations);
                    break;
                case InterceptionKind.AddSingletonWithAlias:
                    GenerateAddSingletonWithAliasInterceptor(sb, info, i, emitTrimAnnotations);
                    break;
                case InterceptionKind.AddSingletonInstanceFactory:
                    GenerateAddSingletonInstanceFactoryInterceptor(sb, info, i, emitTrimAnnotations);
                    break;
                case InterceptionKind.AddSingletonFactory:
                    GenerateAddSingletonFactoryInterceptor(sb, info, i, emitTrimAnnotations);
                    break;
                case InterceptionKind.AddTransient:
                    GenerateAddTransientInterceptor(sb, info, i, emitTrimAnnotations);
                    break;
                case InterceptionKind.AddTransientWithAlias:
                    GenerateAddTransientWithImplementationInterceptor(sb, info, i, emitTrimAnnotations);
                    break;
                case InterceptionKind.AddTransientInstanceFactory:
                    GenerateAddTransientInstanceFactoryInterceptor(sb, info, i, emitTrimAnnotations);
                    break;
                case InterceptionKind.AddTransientFactory:
                    GenerateAddTransientFactoryInterceptor(sb, info, i, emitTrimAnnotations);
                    break;
                case InterceptionKind.OnBuilt:
                    GenerateOnBuiltInterceptor(sb, info, i);
                    break;
                case InterceptionKind.GetRequiredServiceEnumerable:
                    GenerateGetRequiredServiceEnumerableInterceptor(sb, info, i);
                    break;
                case InterceptionKind.GetServiceEnumerable:
                    GenerateGetServiceEnumerableInterceptor(sb, info, i);
                    break;
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string BuildServiceArgumentExpression(ServiceParameter parameter)
    {
        return parameter.ResolutionKind switch
        {
            ServiceResolutionKind.Collection => $"sp.GetServices<{parameter.ResolutionTypeFullName}>()",
            ServiceResolutionKind.Optional => $"sp.GetService<{parameter.ResolutionTypeFullName}>()",
            _ => $"sp.GetRequiredService<{parameter.ResolutionTypeFullName}>()"
        };
    }

    private static void GenerateAddSingletonInterceptor(StringBuilder sb, InterceptionInfo info, int index, bool emitTrimAnnotations)
    {
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        if (emitTrimAnnotations)
        {
            sb.AppendLine($"        public static void AddSingleton_{index}<{DynamicallyAccessedMembersAttribute} T>(");
        }
        else
        {
            sb.AppendLine($"        public static void AddSingleton_{index}<T>(");
        }
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceCollection collection)");
        sb.AppendLine($"            where T : class");
        sb.AppendLine($"        {{");
        sb.Append($"            collection.AddSingleton<{info.ImplementationTypeFullName}>(static sp => new {info.ImplementationTypeFullName}(");

        for (int j = 0; j < info.Parameters.Length; j++)
        {
            if (j > 0)
            {
                sb.Append(", ");
            }
            sb.Append(BuildServiceArgumentExpression(info.Parameters[j]));
        }

        sb.AppendLine("));");
        sb.AppendLine($"        }}");
    }

    private static void GenerateAddSingletonWithAliasInterceptor(StringBuilder sb, InterceptionInfo info, int index, bool emitTrimAnnotations)
    {
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        if (emitTrimAnnotations)
        {
            sb.AppendLine($"        public static void AddSingleton_{index}<TService, {DynamicallyAccessedMembersAttribute} TImplementation>(");
        }
        else
        {
            sb.AppendLine($"        public static void AddSingleton_{index}<TService, TImplementation>(");
        }
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceCollection collection)");
        sb.AppendLine($"            where TService : class");
        sb.AppendLine($"            where TImplementation : class");
        sb.AppendLine($"        {{");
        sb.Append($"            collection.AddSingleton<{info.ServiceTypeFullName}, {info.ImplementationTypeFullName}>(static sp => new {info.ImplementationTypeFullName}(");

        for (int j = 0; j < info.Parameters.Length; j++)
        {
            if (j > 0)
            {
                sb.Append(", ");
            }
            sb.Append(BuildServiceArgumentExpression(info.Parameters[j]));
        }

        sb.AppendLine($"));");
        sb.AppendLine($"        }}");
    }

    private static void GenerateAddSingletonInstanceFactoryInterceptor(StringBuilder sb, InterceptionInfo info, int index, bool emitTrimAnnotations)
    {
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        if (emitTrimAnnotations)
        {
            sb.AppendLine($"        public static void AddSingleton_{index}<TService, {DynamicallyAccessedMembersAttribute} TFactory>(");
        }
        else
        {
            sb.AppendLine($"        public static void AddSingleton_{index}<TService, TFactory>(");
        }
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceCollection collection)");
        sb.AppendLine($"            where TService : class");
        sb.AppendLine($"            where TFactory : class");
        sb.AppendLine($"        {{");
        sb.Append($"            collection.AddSingleton<{info.ServiceTypeFullName}>(static sp => sp.GetRequiredService<{info.FactoryTypeFullName}>().{info.FactoryMethodName}(");

        for (int j = 0; j < info.Parameters.Length; j++)
        {
            if (j > 0)
            {
                sb.Append(", ");
            }
            sb.Append(BuildServiceArgumentExpression(info.Parameters[j]));
        }

        sb.AppendLine("));");
        sb.AppendLine($"        }}");
    }

    private static void GenerateAddSingletonFactoryInterceptor(StringBuilder sb, InterceptionInfo info, int index, bool emitTrimAnnotations)
    {
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        if (emitTrimAnnotations)
        {
            sb.AppendLine($"        public static void AddSingleton_{index}<{DynamicallyAccessedMembersAttribute} T>(");
        }
        else
        {
            sb.AppendLine($"        public static void AddSingleton_{index}<T>(");
        }
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceCollection collection,");
        sb.AppendLine($"            global::System.Delegate factory)");
        sb.AppendLine($"            where T : class");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            {info.DelegateTypeFullName} typedFactory = ({info.DelegateTypeFullName})factory;");
        sb.Append($"            collection.AddSingleton<{info.ServiceTypeFullName}>(sp => typedFactory(");

        for (int j = 0; j < info.Parameters.Length; j++)
        {
            if (j > 0)
            {
                sb.Append(", ");
            }
            sb.Append(BuildServiceArgumentExpression(info.Parameters[j]));
        }

        sb.AppendLine("));");
        sb.AppendLine($"        }}");
    }

    private static void GenerateAddTransientInterceptor(StringBuilder sb, InterceptionInfo info, int index, bool emitTrimAnnotations)
    {
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        if (emitTrimAnnotations)
        {
            sb.AppendLine($"        public static void AddTransient_{index}<{DynamicallyAccessedMembersAttribute} T>(");
        }
        else
        {
            sb.AppendLine($"        public static void AddTransient_{index}<T>(");
        }
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceCollection collection)");
        sb.AppendLine($"            where T : class");
        sb.AppendLine($"        {{");
        sb.Append($"            collection.AddTransient<{info.ImplementationTypeFullName}>(static sp => new {info.ImplementationTypeFullName}(");

        for (int j = 0; j < info.Parameters.Length; j++)
        {
            if (j > 0)
            {
                sb.Append(", ");
            }
            sb.Append(BuildServiceArgumentExpression(info.Parameters[j]));
        }

        sb.AppendLine("));");
        sb.AppendLine($"        }}");
    }

    private static void GenerateAddTransientWithImplementationInterceptor(StringBuilder sb, InterceptionInfo info, int index, bool emitTrimAnnotations)
    {
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        if (emitTrimAnnotations)
        {
            sb.AppendLine($"        public static void AddTransient_{index}<TService, {DynamicallyAccessedMembersAttribute} TImplementation>(");
        }
        else
        {
            sb.AppendLine($"        public static void AddTransient_{index}<TService, TImplementation>(");
        }
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceCollection collection)");
        sb.AppendLine($"            where TService : class");
        sb.AppendLine($"            where TImplementation : class");
        sb.AppendLine($"        {{");
        sb.Append($"            collection.AddTransient<{info.ServiceTypeFullName}, {info.ImplementationTypeFullName}>(static sp => new {info.ImplementationTypeFullName}(");

        for (int j = 0; j < info.Parameters.Length; j++)
        {
            if (j > 0)
            {
                sb.Append(", ");
            }
            sb.Append(BuildServiceArgumentExpression(info.Parameters[j]));
        }

        sb.AppendLine($"));");
        sb.AppendLine($"        }}");
    }

    private static void GenerateAddTransientInstanceFactoryInterceptor(StringBuilder sb, InterceptionInfo info, int index, bool emitTrimAnnotations)
    {
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        if (emitTrimAnnotations)
        {
            sb.AppendLine($"        public static void AddTransient_{index}<TService, {DynamicallyAccessedMembersAttribute} TFactory>(");
        }
        else
        {
            sb.AppendLine($"        public static void AddTransient_{index}<TService, TFactory>(");
        }
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceCollection collection)");
        sb.AppendLine($"            where TService : class");
        sb.AppendLine($"            where TFactory : class");
        sb.AppendLine($"        {{");
        sb.Append($"            collection.AddTransient<{info.ServiceTypeFullName}>(static sp => sp.GetRequiredService<{info.FactoryTypeFullName}>().{info.FactoryMethodName}(");

        for (int j = 0; j < info.Parameters.Length; j++)
        {
            if (j > 0)
            {
                sb.Append(", ");
            }
            sb.Append(BuildServiceArgumentExpression(info.Parameters[j]));
        }

        sb.AppendLine("));");
        sb.AppendLine($"        }}");
    }

    private static void GenerateAddTransientFactoryInterceptor(StringBuilder sb, InterceptionInfo info, int index, bool emitTrimAnnotations)
    {
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        if (emitTrimAnnotations)
        {
            sb.AppendLine($"        public static void AddTransient_{index}<{DynamicallyAccessedMembersAttribute} T>(");
        }
        else
        {
            sb.AppendLine($"        public static void AddTransient_{index}<T>(");
        }
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceCollection collection,");
        sb.AppendLine($"            global::System.Delegate factory)");
        sb.AppendLine($"            where T : class");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            {info.DelegateTypeFullName} typedFactory = ({info.DelegateTypeFullName})factory;");
        sb.Append($"            collection.AddTransient<{info.ServiceTypeFullName}>(sp => typedFactory(");

        for (int j = 0; j < info.Parameters.Length; j++)
        {
            if (j > 0)
            {
                sb.Append(", ");
            }
            sb.Append(BuildServiceArgumentExpression(info.Parameters[j]));
        }

        sb.AppendLine("));");
        sb.AppendLine($"        }}");
    }

    private static void GenerateOnBuiltInterceptor(StringBuilder sb, InterceptionInfo info, int index)
    {
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        sb.AppendLine($"        public static void OnBuilt_{index}(");
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceCollection collection,");
        sb.AppendLine($"            global::System.Delegate action)");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            {info.DelegateTypeFullName} typedAction = ({info.DelegateTypeFullName})action;");
        sb.AppendLine($"            collection.OnBuilt(sp => typedAction(");

        for (int j = 0; j < info.Parameters.Length; j++)
        {
            if (j > 0)
            {
                sb.Append(", ");
            }
            sb.Append(BuildServiceArgumentExpression(info.Parameters[j]));
        }

        sb.AppendLine("));");
        sb.AppendLine($"        }}");
    }

    private static void GenerateGetRequiredServiceEnumerableInterceptor(StringBuilder sb, InterceptionInfo info, int index)
    {
        string elementType = info.ServiceTypeFullName!;
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        sb.AppendLine($"        public static global::System.Collections.Generic.IEnumerable<{elementType}> GetRequiredService_{index}<T>(");
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceProvider provider)");
        sb.AppendLine($"            where T : class");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            return provider.GetServices<{elementType}>();");
        sb.AppendLine($"        }}");
    }

    private static void GenerateGetServiceEnumerableInterceptor(StringBuilder sb, InterceptionInfo info, int index)
    {
        string elementType = info.ServiceTypeFullName!;
        sb.AppendLine($"        {info.InterceptsLocationAttribute}");
        sb.AppendLine($"        public static global::System.Collections.Generic.IEnumerable<{elementType}> GetService_{index}<T>(");
        sb.AppendLine($"            this global::Pixely.DependencyInjection.ServiceProvider provider)");
        sb.AppendLine($"            where T : class");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            return provider.GetServices<{elementType}>();");
        sb.AppendLine($"        }}");
    }
}
