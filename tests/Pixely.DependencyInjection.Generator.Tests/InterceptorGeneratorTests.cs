using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Pixely.DependencyInjection.Generator;

namespace Pixely.DependencyInjection.Generator.Tests;

public class InterceptorGeneratorTests
{
    private const string DamAttribute = "[global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]";

    // Source that triggers intercepted overloads of AddSingleton and AddTransient:
    // - AddSingleton<T>()
    // - AddSingleton<TService, TImplementation>()
    // - AddSingleton<T>(Delegate factory)
    // - AddSingleton<TService, TFactory>() instance factory
    private const string RegistrationSource = """
        using Pixely.DependencyInjection;
        using System;

        public class MyService { }
        public interface IMyService { }
        public class MyServiceImpl : IMyService { }
        public class ProductService { public ProductService(string v) {} }
        public class MyFactory
        {
            internal ProductService CreateProduct() => new ProductService("test");
        }

        public static class Registrations
        {
            public static void Register(ServiceCollection services)
            {
                services.AddSingleton<MyService>();
                services.AddSingleton<IMyService, MyServiceImpl>();
                services.AddSingleton<MyService>(() => new MyService());
                services.AddSingleton<MyFactory>();
                services.AddSingleton<ProductService, MyFactory>();
                services.AddTransient<MyService>();
                services.AddTransient<IMyService, MyServiceImpl>();
                services.AddTransient<MyService>(() => new MyService());
                services.AddTransient<ProductService, MyFactory>();
            }
        }

        """;

    private const string NullableDependencyRegistrationSource = """
        #nullable enable
        using Pixely.DependencyInjection;
        using System;
        using System.Collections.Generic;

        public sealed class RequiredDependency { }
        public sealed class OptionalDependency { }
        public sealed class Item { }
        public sealed class Container<T> { }
        public interface IConsumer { }

        public sealed class Consumer : IConsumer
        {
            public Consumer(
                RequiredDependency required,
                OptionalDependency? optional,
                IEnumerable<Item>? items,
                IEnumerable<OptionalDependency?> nullableItems,
                Container<OptionalDependency?>? nested) { }
        }

        public sealed class ImplementationConsumer : IConsumer
        {
            public ImplementationConsumer(OptionalDependency? optional) { }
        }

        public sealed class Product { }

        public sealed class ProductFactory
        {
            internal Product Create(OptionalDependency? optional) => new Product();
        }

        public static class Registrations
        {
            private static Product CreateProduct(OptionalDependency? optional) => new Product();
            private static void Start(OptionalDependency? optional) { }

            public static void Register(ServiceCollection services)
            {
                services.AddSingleton<Consumer>();
                services.AddSingleton<IConsumer, ImplementationConsumer>();
                services.AddSingleton<ProductFactory>();
                services.AddSingleton<Product, ProductFactory>();
                services.AddSingleton<Product>(CreateProduct);
                services.AddSingleton<Product>((OptionalDependency? optional) => new Product());

                services.AddTransient<Consumer>();
                services.AddTransient<IConsumer, ImplementationConsumer>();
                services.AddTransient<Product, ProductFactory>();
                services.AddTransient<Product>(CreateProduct);
                services.AddTransient<Product>((OptionalDependency? optional) => new Product());

                services.OnBuilt(Start);
                services.OnBuilt((OptionalDependency? optional) => { });
            }

            public static void ResolveCollections(ServiceProvider provider)
            {
                _ = provider.GetRequiredService<IEnumerable<OptionalDependency?>>();
                _ = provider.GetService<IEnumerable<OptionalDependency?>>();
            }
        }
        """;

    private const string ObliviousDependencyRegistrationSource = """
        #nullable disable
        using Pixely.DependencyInjection;

        public sealed class Dependency { }

        public sealed class Consumer
        {
            public Consumer(Dependency dependency) { }
        }

        public static class Registrations
        {
            public static void Register(ServiceCollection services)
            {
                services.AddSingleton<Consumer>();
            }
        }
        """;

    private const string MetadataDependencySource = """
        #nullable enable

        public sealed class MetadataDependency { }

        public sealed class NullableMetadataConsumer
        {
            public NullableMetadataConsumer(MetadataDependency? dependency) { }
        }

        #nullable disable

        public sealed class ObliviousMetadataConsumer
        {
            public ObliviousMetadataConsumer(MetadataDependency dependency) { }
        }
        """;

    private const string MetadataDependencyRegistrationSource = """
        using Pixely.DependencyInjection;

        public static class Registrations
        {
            public static void Register(ServiceCollection services)
            {
                services.AddSingleton<NullableMetadataConsumer>();
                services.AddSingleton<ObliviousMetadataConsumer>();
            }
        }
        """;

    private const string GenericHelperRegistrationSource = """
        using Pixely.DependencyInjection;

        public interface IService<T> { }
        public sealed class Dependency { }
        public sealed class SimpleService
        {
            public SimpleService(Dependency dependency) { }
        }

        public sealed class GenericImplementation<T> : IService<T>
        {
            public GenericImplementation(Dependency dependency) { }
        }

        public static class GenericContainer<T>
        {
            public sealed class NestedImplementation : IService<T>
            {
                public NestedImplementation(Dependency dependency) { }
            }
        }

        public static class Registrations
        {
            public static void Register<T>(ServiceCollection services)
                where T : class
            {
                services.AddSingleton<SimpleService>();
                services.AddTransient<T>();
                services.AddSingleton<GenericImplementation<T>>();
                services.AddSingleton<IService<T>, GenericImplementation<T>>();
                services.AddTransient<GenericContainer<T>.NestedImplementation>();
                services.AddTransient<IService<T>, GenericContainer<T>.NestedImplementation>();
                services.AddSingleton<GenericImplementation<T[]>>();
            }

            public static void RegisterClosed(ServiceCollection services)
            {
                services.AddSingleton<GenericImplementation<int>>();
                services.AddSingleton<IService<int>, GenericImplementation<int>>();
                services.AddTransient<GenericContainer<int>.NestedImplementation>();
                services.AddTransient<IService<int>, GenericContainer<int>.NestedImplementation>();
            }
        }

        public sealed class GenericRegistrar<T>
        {
            public void Register(ServiceCollection services)
            {
                services.AddTransient<GenericImplementation<T>>();
            }
        }
        """;

    private const string ArrayImplementationRegistrationSource = """
        using Pixely.DependencyInjection;

        public static class Registrations
        {
            public static void Register(ServiceCollection services)
            {
                services.AddSingleton<byte[]>();
            }
        }
        """;

    private const string NestedDiagnosticTypeRegistrationSource = """
        using Pixely.DependencyInjection;

        public interface IService<T> { }
        public sealed class Dependency { }
        public sealed class Implementation<T> : IService<T> { }

        public static class Outer<T>
        {
            public sealed class MultipleConstructors : IService<T>
            {
                public MultipleConstructors() { }
                public MultipleConstructors(Dependency dependency) { }
            }

            public sealed class MissingFactory { }

            public sealed class AmbiguousFactory
            {
                public IService<T> Create() => new Implementation<T>();
                public IService<T> Build() => new Implementation<T>();
            }
        }

        public static class Registrations
        {
            public static void Register(ServiceCollection services)
            {
                services.AddSingleton<IService<int>, Outer<int>.MultipleConstructors>();
                services.AddSingleton<IService<int>, Outer<int>.MissingFactory>();
                services.AddSingleton<IService<int>, Outer<int>.AmbiguousFactory>();
            }
        }
        """;

    [Test]
    public void GeneratedCode_ContainsTrimAnnotations_WhenPropertyIsUnset()
    {
        string generated = RunGenerator(emitTrimAnnotationsPropertyValue: null);

        Assert.That(generated, Does.Contain(DamAttribute));
    }

    [Test]
    public void GeneratedCode_ContainsTrimAnnotations_WhenPropertyIsTrue()
    {
        string generated = RunGenerator(emitTrimAnnotationsPropertyValue: "true");

        Assert.That(generated, Does.Contain(DamAttribute));
    }

    [Test]
    public void GeneratedCode_ContainsTrimAnnotations_WhenPropertyIsTrue_CaseInsensitive()
    {
        string generated = RunGenerator(emitTrimAnnotationsPropertyValue: "True");

        Assert.That(generated, Does.Contain(DamAttribute));
    }

    [Test]
    public void GeneratedCode_OmitsTrimAnnotations_WhenPropertyIsFalse()
    {
        string generated = RunGenerator(emitTrimAnnotationsPropertyValue: "false");

        Assert.That(generated, Does.Not.Contain(DamAttribute));
    }

    [Test]
    public void GeneratedCode_OmitsTrimAnnotations_WhenPropertyIsFalse_CaseInsensitive()
    {
        string generated = RunGenerator(emitTrimAnnotationsPropertyValue: "False");

        Assert.That(generated, Does.Not.Contain(DamAttribute));
    }

    [Test]
    public void GeneratedCode_AddSingleton_ContainsAnnotation_OnTParam_WhenEnabled()
    {
        string generated = RunGenerator(emitTrimAnnotationsPropertyValue: "true");

        // AddSingleton_N<[DAM] T>( should appear for the single-type overload
        Assert.That(generated, Does.Match(@"AddSingleton_\d+<\[global::System\.Diagnostics\.CodeAnalysis\.DynamicallyAccessedMembers.*?\] T>"));
    }

    [Test]
    public void GeneratedCode_AddSingletonWithAlias_ContainsAnnotation_OnTImplementationParam_WhenEnabled()
    {
        string generated = RunGenerator(emitTrimAnnotationsPropertyValue: "true");

        // AddSingleton_N<TService, [DAM] TImplementation>( should appear for the two-type overload
        Assert.That(generated, Does.Match(@"AddSingleton_\d+<TService, \[global::System\.Diagnostics\.CodeAnalysis\.DynamicallyAccessedMembers.*?\] TImplementation>"));
    }

    [Test]
    public void GeneratedCode_AddSingletonFactory_ContainsAnnotation_OnTParam_WhenEnabled()
    {
        string generated = RunGenerator(emitTrimAnnotationsPropertyValue: "true");

        // The factory overload also adds [DAM] on T
        // The method signature has two type parameters between the attribute and T; just check the
        // annotation is present (already covered by the broad Does.Contain test above).
        Assert.That(generated, Does.Contain(DamAttribute));
    }

    [Test]
    public void GeneratedCode_IsNotEmpty_WhenAnnotationsDisabled()
    {
        string generated = RunGenerator(emitTrimAnnotationsPropertyValue: "false");

        // The interceptors themselves must still be generated — only the attribute is absent
        Assert.That(generated, Does.Contain("ServiceCollectionInterceptors"));
        Assert.That(generated, Does.Contain("AddSingleton_"));
        Assert.That(generated, Does.Contain("AddTransient_"));
    }

    [Test]
    public void ConstructorRegistration_GenericImplementationContainingMethodTypeParameter_ReportsGK0001()
    {
        Compilation outputCompilation;
        GeneratorDriverRunResult result = RunGenerator(
            GenericHelperRegistrationSource,
            emitTrimAnnotationsPropertyValue: null,
            out outputCompilation);
        Diagnostic[] diagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == "GK0001")
            .ToArray();
        string generated = GetGeneratedCode(result);
        string[] expectedMessages =
        [
            "AddTransient<T>() cannot be used when the implementation type is or contains a type parameter. Use AddTransient<T>(Func<ServiceProvider, T>) instead.",
            "AddSingleton<GenericImplementation<T>>() cannot be used when the implementation type is or contains a type parameter. Use AddSingleton<GenericImplementation<T>>(Func<ServiceProvider, GenericImplementation<T>>) instead.",
            "AddSingleton<IService<T>, GenericImplementation<T>>() cannot be used when the implementation type is or contains a type parameter. Use AddSingleton<IService<T>, GenericImplementation<T>>(Func<ServiceProvider, GenericImplementation<T>>) instead.",
            "AddTransient<GenericContainer<T>.NestedImplementation>() cannot be used when the implementation type is or contains a type parameter. Use AddTransient<GenericContainer<T>.NestedImplementation>(Func<ServiceProvider, GenericContainer<T>.NestedImplementation>) instead.",
            "AddTransient<IService<T>, GenericContainer<T>.NestedImplementation>() cannot be used when the implementation type is or contains a type parameter. Use AddTransient<IService<T>, GenericContainer<T>.NestedImplementation>(Func<ServiceProvider, GenericContainer<T>.NestedImplementation>) instead.",
            "AddSingleton<GenericImplementation<T[]>>() cannot be used when the implementation type is or contains a type parameter. Use AddSingleton<GenericImplementation<T[]>>(Func<ServiceProvider, GenericImplementation<T[]>>) instead.",
            "AddTransient<GenericImplementation<T>>() cannot be used when the implementation type is or contains a type parameter. Use AddTransient<GenericImplementation<T>>(Func<ServiceProvider, GenericImplementation<T>>) instead."
        ];
        Diagnostic[] compilationErrors = outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Has.Length.EqualTo(7));
            Assert.That(diagnostics, Has.Length.EqualTo(7));
            Assert.That(diagnostics, Has.All.Property(nameof(Diagnostic.Severity)).EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostics.Select(diagnostic => diagnostic.GetMessage()), Is.EquivalentTo(expectedMessages));
            Assert.That(diagnostics.Count(diagnostic => diagnostic.GetMessage().StartsWith("AddSingleton")), Is.EqualTo(3));
            Assert.That(diagnostics.Count(diagnostic => diagnostic.GetMessage().StartsWith("AddTransient")), Is.EqualTo(4));
            Assert.That(diagnostics.Select(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line), Has.All.GreaterThan(0));
            Assert.That(generated, Does.Contain("global::SimpleService"));
            Assert.That(generated, Does.Contain("global::GenericImplementation<int>"));
            Assert.That(generated, Does.Contain("global::GenericContainer<int>.NestedImplementation"));
            Assert.That(generated, Does.Not.Contain("global::GenericImplementation<T>"));
            Assert.That(generated, Does.Not.Contain("global::GenericImplementation<T[]>"));
            Assert.That(generated, Does.Not.Contain("global::GenericContainer<T>.NestedImplementation"));
            Assert.That(compilationErrors, Is.Empty);
        });
    }

    [Test]
    public void GeneratedCode_UsesNullableAnnotationsForServiceResolution()
    {
        GeneratorDriverRunResult result = RunGenerator(
            NullableDependencyRegistrationSource,
            emitTrimAnnotationsPropertyValue: null,
            out Compilation outputCompilation);
        string generated = GetGeneratedCode(result);

        Diagnostic[] generatedDiagnostics = outputCompilation.GetDiagnostics()
            .Where(diagnostic =>
                diagnostic.Location.SourceTree?.FilePath.EndsWith(
                    "ServiceCollectionInterceptors.g.cs",
                    StringComparison.Ordinal) == true &&
                diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                generated.Split("sp.GetService<global::OptionalDependency>()").Length - 1,
                Is.EqualTo(12));
            Assert.That(
                generated.Split("sp.GetRequiredService<global::RequiredDependency>()").Length - 1,
                Is.EqualTo(2));
            Assert.That(
                generated.Split("sp.GetServices<global::Item>()").Length - 1,
                Is.EqualTo(2));
            Assert.That(
                generated.Split("sp.GetServices<global::OptionalDependency>()").Length - 1,
                Is.EqualTo(2));
            Assert.That(
                generated.Split("sp.GetService<global::Container<global::OptionalDependency?>>()").Length - 1,
                Is.EqualTo(2));
            Assert.That(
                generated.Split("return provider.GetServices<global::OptionalDependency>();").Length - 1,
                Is.EqualTo(2));
            Assert.That(generated, Does.Contain("global::System.Func<global::OptionalDependency?, global::Product>"));
            Assert.That(generated, Does.Contain("global::System.Action<global::OptionalDependency?>"));
            Assert.That(generatedDiagnostics, Is.Empty);
        });
    }

    [Test]
    public void GeneratedCode_TreatsObliviousReferenceDependencyAsRequired()
    {
        GeneratorDriverRunResult result = RunGenerator(
            ObliviousDependencyRegistrationSource,
            emitTrimAnnotationsPropertyValue: null);
        string generated = GetGeneratedCode(result);

        Assert.That(generated, Does.Contain("sp.GetRequiredService<global::Dependency>()"));
        Assert.That(generated, Does.Not.Contain("sp.GetService<global::Dependency>()"));
    }

    [Test]
    public void GeneratedCode_UsesNullableAnnotationsFromReferencedCompilation()
    {
        CSharpCompilation metadataCompilation = CreateCompilation(
            MetadataDependencySource,
            assemblyName: "MetadataDependencyAssembly");
        using MemoryStream metadataStream = new();
        EmitResult emitResult = metadataCompilation.Emit(metadataStream);
        Assert.That(emitResult.Success, Is.True);
        metadataStream.Position = 0;
        MetadataReference metadataReference = MetadataReference.CreateFromStream(metadataStream);

        GeneratorDriverRunResult result = RunGenerator(
            MetadataDependencyRegistrationSource,
            emitTrimAnnotationsPropertyValue: null,
            out Compilation outputCompilation,
            new MetadataReference[] { metadataReference });
        string generated = GetGeneratedCode(result);
        Diagnostic[] compilationErrors = outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("new global::NullableMetadataConsumer(sp.GetService<global::MetadataDependency>())"));
            Assert.That(generated, Does.Contain("new global::ObliviousMetadataConsumer(sp.GetRequiredService<global::MetadataDependency>())"));
            Assert.That(compilationErrors, Is.Empty);
        });
    }

    [Test]
    public void ConstructorRegistration_UnnamedImplementationType_ReportsAccurateGK0001()
    {
        GeneratorDriverRunResult result = RunGenerator(
            ArrayImplementationRegistrationSource,
            emitTrimAnnotationsPropertyValue: null);

        Assert.That(result.Diagnostics, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics[0].Id, Is.EqualTo("GK0001"));
            Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(
                result.Diagnostics[0].GetMessage(),
                Is.EqualTo("AddSingleton<byte[]>() requires the implementation type to be a named concrete type."));
        });
    }

    [Test]
    public void GeneratorDiagnostics_NestedGenericTypes_IncludeContainingTypeNames()
    {
        GeneratorDriverRunResult result = RunGenerator(
            NestedDiagnosticTypeRegistrationSource,
            emitTrimAnnotationsPropertyValue: null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "GK0002", "GK0003", "GK0004" }));
            Assert.That(result.Diagnostics[0].GetMessage(), Does.Contain("Outer<int>.MultipleConstructors"));
            Assert.That(result.Diagnostics[1].GetMessage(), Does.Contain("Outer<int>.MissingFactory"));
            Assert.That(result.Diagnostics[2].GetMessage(), Does.Contain("Outer<int>.AmbiguousFactory"));
        });
    }

    private static string RunGenerator(string? emitTrimAnnotationsPropertyValue)
    {
        GeneratorDriverRunResult result = RunGenerator(RegistrationSource, emitTrimAnnotationsPropertyValue);

        return GetGeneratedCode(result);
    }

    private static string GetGeneratedCode(GeneratorDriverRunResult result)
    {
        // Find the interceptors file
        foreach (GeneratorRunResult generatorResult in result.Results)
        {
            foreach (GeneratedSourceResult source in generatorResult.GeneratedSources)
            {
                if (source.HintName == "ServiceCollectionInterceptors.g.cs")
                {
                    return source.SourceText.ToString();
                }
            }
        }

        return string.Empty;
    }

    private static GeneratorDriverRunResult RunGenerator(string source, string? emitTrimAnnotationsPropertyValue)
    {
        return RunGenerator(source, emitTrimAnnotationsPropertyValue, out _);
    }

    private static GeneratorDriverRunResult RunGenerator(
        string source,
        string? emitTrimAnnotationsPropertyValue,
        out Compilation outputCompilation,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        CSharpCompilation compilation = CreateCompilation(
            source,
            additionalReferences: additionalReferences);
        InterceptorGenerator generator = new InterceptorGenerator();

        OptionsProvider optionsProvider = new OptionsProvider(emitTrimAnnotationsPropertyValue);

        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { generator.AsSourceGenerator() },
            optionsProvider: optionsProvider,
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees[0].Options);

        GeneratorDriver ran = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out outputCompilation,
            out ImmutableArray<Diagnostic> _);
        return ran.GetRunResult();
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "TestAssembly",
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        // Collect references: mscorlib/System.Runtime + the DI runtime assembly
        List<MetadataReference> references = new List<MetadataReference>();

        // Core runtime references
        string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));

        // Pixely.DependencyInjection runtime assembly
        Assembly diAssembly = typeof(Pixely.DependencyInjection.ServiceCollection).Assembly;
        references.Add(MetadataReference.CreateFromFile(diAssembly.Location));

        if (additionalReferences != null)
        {
            references.AddRange(additionalReferences);
        }

        CSharpParseOptions parseOptions = new CSharpParseOptions(
            languageVersion: LanguageVersion.CSharp13,
            preprocessorSymbols: ImmutableArray<string>.Empty)
            .WithFeatures(
            [
                new KeyValuePair<string, string>(
                    "InterceptorsNamespaces",
                    "Pixely.DependencyInjection.Generated")
            ]);

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions);

        return CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));
    }

    // Provides controlled AnalyzerConfigOptions that simulate the MSBuild property.
    private sealed class OptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly ControlledOptions _global;

        public OptionsProvider(string? propertyValue)
        {
            _global = new ControlledOptions(propertyValue);
        }

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _global;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _global;
    }

    private sealed class ControlledOptions : AnalyzerConfigOptions
    {
        private readonly string? _propertyValue;

        public ControlledOptions(string? propertyValue)
        {
            _propertyValue = propertyValue;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.PixelyDIEmitTrimAnnotations" && _propertyValue != null)
            {
                value = _propertyValue;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
