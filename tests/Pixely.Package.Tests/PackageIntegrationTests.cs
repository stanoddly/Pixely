using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Pixely.Package.Tests;

[Category("PackageIntegration")]
[Explicit("Builds and consumes a local NuGet package and must execute in isolation.")]
[NonParallelizable]
public class PackageIntegrationTests
{
    private const long NuGetPackageSizeLimitBytes = 250_000_000;
    private static readonly string[] RuntimeAssemblies =
    [
        "Pixely.PathFinding",
        "Pixely.Fitness",
        "Pixely.Audio",
        "Pixely.Collections",
        "Pixely.Componentize",
        "Pixely.Core",
        "Pixely.DependencyInjection",
        "Pixely.Events",
        "Pixely.Logging",
        "Pixely.Observations",
        "Pixely.ShaderCommon",
        "Pixely.Utils",
        "Pixely"
    ];

    private static readonly string[] ConsumerNames =
    [
        "ShaderConsumer",
        "ShaderFreeConsumer",
        "HostedConsumer",
        "CentralConsumer",
        "PackageReferenceConsumer",
        "LibraryConsumer",
        "TransitiveConsumer",
        "ReversedSdkConsumer",
        "BrowserLoopConsumer"
    ];

    private string _repositoryDirectory = null!;
    private string _testArtifactsDirectory = null!;
    private string _packageDirectory = null!;
    private string _packagesDirectory = null!;
    private string _packageVersion = null!;
    private string _packagePath = null!;
    private string _symbolPackagePath = null!;

    [OneTimeSetUp]
    public async Task CreatePackage()
    {
        _repositoryDirectory = GetRepositoryDirectory();
        _testArtifactsDirectory = Path.Combine(_repositoryDirectory, "artifacts", "package-tests");
        string? suppliedPackageDirectory = Environment.GetEnvironmentVariable("PIXELY_PACKAGE_DIRECTORY");
        _packageDirectory = suppliedPackageDirectory
            ?? Path.Combine(_testArtifactsDirectory, "feed");
        _packagesDirectory = Path.Combine(_testArtifactsDirectory, "restore");
        _packageVersion = Environment.GetEnvironmentVariable("PIXELY_PACKAGE_VERSION")
            ?? "0.0.0-alpha.package-tests";
        _packagePath = Path.Combine(_packageDirectory, $"Pixely.{_packageVersion}.nupkg");
        _symbolPackagePath = Path.Combine(_packageDirectory, $"Pixely.{_packageVersion}.snupkg");

        DeleteDirectory(_testArtifactsDirectory);
        Directory.CreateDirectory(_packagesDirectory);

        if (suppliedPackageDirectory is not null)
        {
            Assert.That(Directory.Exists(_packageDirectory), Is.True);
            Assert.That(File.Exists(_packagePath), Is.True);
            Assert.That(File.Exists(_symbolPackagePath), Is.True);
            return;
        }

        Directory.CreateDirectory(_packageDirectory);

        string projectPath = Path.Combine(
            _repositoryDirectory,
            "packaging",
            "Pixely",
            "Pixely.Package.csproj");
        await RunDotnetAsync(
            _repositoryDirectory,
            "pack",
            projectPath,
            "--configuration",
            "Release",
            "--output",
            _packageDirectory,
            $"--property:PackageVersion={_packageVersion}",
            $"--property:Version={_packageVersion}",
            "--nologo");
    }

    [OneTimeTearDown]
    public void CleanPackageConsumers()
    {
        foreach (string consumer in ConsumerNames)
        {
            DeleteConsumerOutputs(consumer);
        }
        DeleteDirectory(_testArtifactsDirectory);
    }

    [Test]
    public void PackageArchiveContainsAllCoordinatedAssetsAndMetadata()
    {
        Assert.That(File.Exists(_packagePath), Is.True);
        Assert.That(File.Exists(_symbolPackagePath), Is.True);
        Assert.That(new FileInfo(_packagePath).Length, Is.LessThan(NuGetPackageSizeLimitBytes));

        using ZipArchive package = ZipFile.OpenRead(_packagePath);
        HashSet<string> entries = package.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);

        foreach (string assembly in RuntimeAssemblies)
        {
            string entryName = $"lib/net11.0/{assembly}.dll";
            Assert.That(entries, Does.Contain(entryName));
            ZipArchiveEntry assemblyEntry = package.GetEntry(entryName)
                ?? throw new InvalidOperationException($"{entryName} is missing from the package.");
            using Stream assemblyStream = assemblyEntry.Open();
            using MemoryStream assemblyBytes = new();
            assemblyStream.CopyTo(assemblyBytes);
            assemblyBytes.Position = 0;
            using PEReader peReader = new(assemblyBytes);
            Version assemblyVersion = peReader.GetMetadataReader().GetAssemblyDefinition().Version;
            string[] versionComponents = _packageVersion.Split('-')[0].Split('.');
            Version expectedAssemblyVersion = new(
                int.Parse(versionComponents[0]),
                int.Parse(versionComponents[1]),
                int.Parse(versionComponents[2]),
                0);
            Assert.That(assemblyVersion, Is.EqualTo(expectedAssemblyVersion));
        }

        Assert.Multiple(() =>
        {
            Assert.That(entries, Does.Contain("analyzers/dotnet/cs/Pixely.DependencyInjection.Generator.dll"));
            Assert.That(entries, Does.Contain("Sdk/Sdk.props"));
            Assert.That(entries, Does.Contain("Sdk/Sdk.targets"));
            Assert.That(entries, Does.Contain("Sdk/Pixely.AfterSdk.targets"));
            Assert.That(entries, Does.Contain("Sdk/Pixely.Hosting.targets"));
            Assert.That(entries, Does.Contain("Sdk/Pixely.Browser.props"));
            Assert.That(entries, Does.Contain("Sdk/Pixely.Browser.targets"));
            Assert.That(entries, Does.Contain("wwwroot/index.html"));
            Assert.That(entries, Does.Contain("wwwroot/main.js"));
            Assert.That(entries, Does.Contain("wwwroot/pixely-host.js"));
            Assert.That(entries, Does.Contain("Sdk/Pixely.Version.props"));
            Assert.That(entries, Does.Contain("buildTransitive/Pixely.targets"));
            Assert.That(entries.Any(entry => entry.StartsWith("build/", StringComparison.Ordinal)), Is.False);
            Assert.That(entries, Does.Contain("tools/net11.0/any/Pixely.SdlangCompiler.dll"));
            Assert.That(entries, Does.Contain("tools/net11.0/any/Pixely.ShaderCommon.dll"));
            Assert.That(entries, Does.Contain("tools/net11.0/any/build/Pixely.SdlangCompiler.props"));
            Assert.That(entries, Does.Contain("tools/net11.0/any/build/Pixely.SdlangCompiler.targets"));
            Assert.That(entries, Does.Contain("THIRD-PARTY-NOTICES.md"));
            Assert.That(entries, Does.Contain("docs/peach-architecture.md"));
            Assert.That(entries, Does.Not.Contain("lib/net11.0/Pixely.SdlangCompiler.dll"));
            Assert.That(entries, Does.Not.Contain("lib/net11.0/Pixely.DependencyInjection.Generator.dll"));
            Assert.That(entries.Any(entry => entry.StartsWith("tools/slang/", StringComparison.Ordinal)), Is.False);
            Assert.That(entries.Any(IsNuGetEmptyFolderPlaceholder), Is.False);
        });

        string shaderProps = ReadPackageEntry(
            package,
            "tools/net11.0/any/build/Pixely.SdlangCompiler.props");
        string shaderTargets = ReadPackageEntry(
            package,
            "tools/net11.0/any/build/Pixely.SdlangCompiler.targets");
        string pixelyProps = ReadPackageEntry(package, "Sdk/Sdk.props");
        string versionProps = ReadPackageEntry(package, "Sdk/Pixely.Version.props");
        Assert.Multiple(() =>
        {
            Assert.That(versionProps, Does.Contain($"<PixelyVersion>{_packageVersion}</PixelyVersion>"));
            Assert.That(shaderProps, Does.Not.Contain("SlangDownloadUrl"));
            Assert.That(shaderProps, Does.Not.Contain("SlangZipSha256"));
            Assert.That(shaderTargets, Does.Not.Contain("DownloadFile"));
            Assert.That(shaderTargets, Does.Not.Contain("DownloadSlang"));
            Assert.That(shaderTargets, Does.Not.Contain("Unzip"));
            Assert.That(shaderTargets, Does.Not.Contain("<Copy "));
            Assert.That(shaderTargets, Does.Not.Contain("chmod"));
            Assert.That(shaderProps, Does.Contain("SlangDxcToolchainRoot"));
            Assert.That(shaderTargets, Does.Contain("SlangDxcToolchainRoot"));
            Assert.That(pixelyProps, Does.Not.Contain("tools\\slang"));
            Assert.That(pixelyProps, Does.Not.Contain("SlangObjDir"));
            Assert.That(pixelyProps, Does.Not.Contain("_OwnsSlangInstallation"));
            Assert.That(pixelyProps, Does.Not.Contain("_DownloadSlangOnlyWhenShaders"));
        });

        ZipArchiveEntry nuspecEntry = package.GetEntry("Pixely.nuspec")
            ?? throw new InvalidOperationException("Pixely.nuspec is missing from the package.");
        using Stream nuspecStream = nuspecEntry.Open();
        using StreamReader nuspecReader = new(nuspecStream);
        string nuspecContents = nuspecReader.ReadToEnd();
        XDocument nuspec = XDocument.Parse(nuspecContents);
        XNamespace ns = nuspec.Root?.Name.Namespace
            ?? throw new InvalidOperationException("Pixely.nuspec has no root element.");
        XElement metadata = nuspec.Root?.Element(ns + "metadata")
            ?? throw new InvalidOperationException("Pixely.nuspec has no metadata element.");
        XElement repository = metadata.Element(ns + "repository")
            ?? throw new InvalidOperationException("Pixely.nuspec has no repository element.");
        string[] expectedDependencies = GetPackageDependencies();
        string[] dependencies = metadata
            .Descendants(ns + "dependency")
            .Select(dependency => $"{(string?)dependency.Attribute("id")}:{(string?)dependency.Attribute("version")}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That((string?)metadata.Element(ns + "id"), Is.EqualTo("Pixely"));
            Assert.That((string?)metadata.Element(ns + "version"), Is.EqualTo(_packageVersion));
            Assert.That((string?)metadata.Element(ns + "authors"), Is.EqualTo("stanoddly"));
            Assert.That((string?)metadata.Element(ns + "readme"), Is.EqualTo("README.md"));
            Assert.That((string?)metadata.Element(ns + "description"), Does.Contain("experimental"));
            Assert.That((string?)metadata.Element(ns + "license"), Is.EqualTo("MIT"));
            Assert.That((string?)repository.Attribute("url"), Is.EqualTo("https://github.com/stanoddly/Pixely"));
            Assert.That((string?)repository.Attribute("commit"), Is.Not.Empty);
            Assert.That(dependencies, Is.EqualTo(expectedDependencies.Order(StringComparer.Ordinal)));
            Assert.That(dependencies.Any(dependency => dependency.StartsWith("Pixely", StringComparison.Ordinal)), Is.False);
            Assert.That(dependencies.Any(dependency => dependency.StartsWith("SlangDxcBundle.Toolchain", StringComparison.Ordinal)), Is.False);
            Assert.That(nuspecContents, Does.Not.Contain("Package Description"));
            Assert.That(nuspecContents, Does.Not.Contain("_._"));
        });

        using ZipArchive symbols = ZipFile.OpenRead(_symbolPackagePath);
        HashSet<string> symbolEntries = symbols.Entries
            .Select(entry => entry.FullName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string assembly in RuntimeAssemblies)
        {
            Assert.That(symbolEntries, Does.Contain($"lib/net11.0/{assembly}.pdb"));
        }

        ZipArchiveEntry sourceLinkEntry = symbols.GetEntry("lib/net11.0/Pixely.pdb")
            ?? throw new InvalidOperationException("Pixely.pdb is missing from the symbol package.");
        using Stream sourceLinkStream = sourceLinkEntry.Open();
        using MemoryStream sourceLinkBytes = new();
        sourceLinkStream.CopyTo(sourceLinkBytes);
        string sourceLinkContents = Encoding.UTF8.GetString(sourceLinkBytes.ToArray());
        Assert.That(
            sourceLinkContents,
            Does.Contain("https://raw.githubusercontent.com/stanoddly/Pixely/"));
    }

    [Test]
    public async Task ConsumerUsesRuntimeAssembliesGeneratorAndShaderBuildAssets()
    {
        string consumerDirectory = GetConsumerDirectory("ShaderConsumer");
        DeleteConsumerOutputs("ShaderConsumer");

        await BuildConsumerAsync(consumerDirectory);
        string outputDirectory = Path.Combine(consumerDirectory, "bin", "Release", "net11.0");
        string generatedDirectory = Path.Combine(consumerDirectory, "Content", "shaders", ".generated");
        string shaderToolDirectory = Path.Combine(consumerDirectory, "obj", "Pixely.SdlangCompiler");
        string slangDirectory = GetRestoredSlangDirectory(GetCurrentSlangPlatform());

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.vertex.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.vertex.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.vertex.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.metadata.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(
                slangDirectory,
                "bin",
                OperatingSystem.IsWindows() ? "slangc.exe" : "slangc")), Is.True);
            Assert.That(
                Directory.GetFiles(slangDirectory, "slang-glsl-module.bin", SearchOption.AllDirectories),
                Is.Empty);
            Assert.That(Directory.Exists(shaderToolDirectory), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "Pixely.SdlangCompiler.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "Microsoft.Build.Framework.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "Microsoft.Build.Utilities.Core.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "Microsoft.NET.StringTools.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "System.Configuration.ConfigurationManager.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "System.Diagnostics.EventLog.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "System.Security.Cryptography.ProtectedData.dll")), Is.False);
        });

        string output = await RunDotnetAsync(
            consumerDirectory,
            Path.Combine(outputDirectory, "ShaderConsumer.dll"));
        Assert.That(output, Does.Contain("Package consumer succeeded."));
    }

    [Test]
    public async Task ShaderFreeConsumerDoesNotInitializeShaderTooling()
    {
        string consumerDirectory = GetConsumerDirectory("ShaderFreeConsumer");
        DeleteConsumerOutputs("ShaderFreeConsumer");

        await BuildConsumerAsync(consumerDirectory);
        string outputDirectory = Path.Combine(consumerDirectory, "bin", "Release", "net11.0");
        string shaderToolDirectory = Path.Combine(consumerDirectory, "obj", "Pixely.SdlangCompiler");

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(shaderToolDirectory), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "Pixely.SdlangCompiler.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "Microsoft.Build.Framework.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "Microsoft.Build.Utilities.Core.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "Microsoft.NET.StringTools.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "System.Configuration.ConfigurationManager.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "System.Diagnostics.EventLog.dll")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDirectory, "System.Security.Cryptography.ProtectedData.dll")), Is.False);
        });

        string output = await RunDotnetAsync(
            consumerDirectory,
            Path.Combine(outputDirectory, "ShaderFreeConsumer.dll"));
        Assert.That(output, Does.Contain("Package consumer succeeded."));
    }

    [Test]
    public async Task HostedConsumerGetsAGeneratedEntryPoint()
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");

        await BuildConsumerAsync(consumerDirectory);
        string generatedFile = Path.Combine(consumerDirectory, "obj", "Release", "net11.0", "PixelyProgram.g.cs");
        Assert.That(File.Exists(generatedFile), Is.True);
        Assert.That(File.ReadAllText(generatedFile), Does.Contain("namespace HostedConsumer;"));

        string outputDirectory = Path.Combine(consumerDirectory, "bin", "Release", "net11.0");
        (int exitCode, string output) = await RunDotnetExpectingExitCodeAsync(
            consumerDirectory,
            Path.Combine(outputDirectory, "HostedConsumer.dll"));
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("Configure ran."));
            Assert.That(output, Does.Contain("OnException ran: Configure failed on purpose."));
        });

        // A second build has nothing to do; the generated file is not rewritten.
        DateTime written = File.GetLastWriteTimeUtc(generatedFile);
        await BuildConsumerAsync(consumerDirectory);
        Assert.That(File.GetLastWriteTimeUtc(generatedFile), Is.EqualTo(written));

        await RunConsumerDotnetAsync(consumerDirectory, "clean", "--configuration", "Release", "--nologo");
        Assert.That(File.Exists(generatedFile), Is.False);
    }

    // The narrow handler is the documented hazard: an OnException that does not take Exception is not applicable, so the default applies.
    [TestCase("HOSTED_CONSUMER_NO_HANDLER")]
    [TestCase("HOSTED_CONSUMER_NARROW_HANDLER")]
    public async Task HostedConsumerWithoutAnApplicableOnExceptionLetsTheFailurePropagate(string variant)
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");

        await BuildConsumerAsync(consumerDirectory, defineConstants: variant);
        string outputDirectory = Path.Combine(consumerDirectory, "bin", "Release", "net11.0");
        (int exitCode, string output) = await RunDotnetExpectingExitCodeAsync(
            consumerDirectory,
            Path.Combine(outputDirectory, "HostedConsumer.dll"));
        Assert.Multiple(() =>
        {
            // an unhandled exception, not the handled-and-reported exit code 1
            Assert.That(exitCode, Is.Not.EqualTo(0).And.Not.EqualTo(1));
            Assert.That(output, Does.Contain("Configure ran."));
            Assert.That(output, Does.Contain("Configure failed on purpose."));
            Assert.That(output, Does.Not.Contain("OnException ran"));
        });
    }

    [Test]
    public async Task HostedConsumerWithoutConfigureFailsToCompile()
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");

        string output = await BuildConsumerAsync(consumerDirectory, defineConstants: "HOSTED_CONSUMER_NO_CONFIGURE", expectSuccess: false);
        Assert.That(output, Does.Contain("CS0117").And.Contain("'Configure'"));
    }

    [Test]
    public async Task HostedConsumerPublishesABrowserBundleWithAnAsyncEntryPoint()
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");

        await PublishConsumerAsync(consumerDirectory, "browser-wasm");
        string generatedFile = Path.Combine(consumerDirectory, "obj", "Release", "net11.0", "browser-wasm", "PixelyProgram.g.cs");
        string wwwroot = GetPublishedWwwroot(consumerDirectory);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(generatedFile), Does.Contain("[global::System.Runtime.Versioning.SupportedOSPlatform(\"browser\")]")
                .And.Contain("private static async global::System.Threading.Tasks.Task<int> Main()")
                .And.Contain("return await global::Pixely.App.BrowserHost.RunAsync(app);")
                .And.Not.Contain("app.Run()"));
            Assert.That(File.ReadAllText(Path.Combine(wwwroot, "index.html")), Does.Contain("<canvas id=\"canvas\""));
            Assert.That(File.Exists(Path.Combine(wwwroot, "main.js")), Is.True);
            Assert.That(File.Exists(Path.Combine(wwwroot, "pixely-host.js")), Is.True);
            // dotnet.js is not fingerprinted on disk; the assemblies (WebCIL) are, and the endpoint manifest aliases their plain names.
            Assert.That(File.Exists(Path.Combine(wwwroot, "_framework", "dotnet.js")), Is.True);
            Assert.That(Directory.GetFiles(Path.Combine(wwwroot, "_framework"), "HostedConsumer.*.wasm"), Has.Length.EqualTo(1));
        });

        // A desktop build afterwards keeps its own obj/ and bin/ and its synchronous Main.
        await BuildConsumerAsync(consumerDirectory);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(consumerDirectory, "obj", "Release", "net11.0", "PixelyProgram.g.cs")), Does.Contain("private static int Main()"));
            Assert.That(File.ReadAllText(generatedFile), Does.Contain("BrowserHost.RunAsync"));
        });
    }

    // The bundle runs under node as it would in a page: the generated async Main runs Configure, which fails on purpose, and the
    // handler decides whether runMain() resolves with its return value or rejects with the exception (the default rethrows).
    [TestCase(null, "RESULT exit code 1")]
    [TestCase("HOSTED_CONSUMER_NO_HANDLER", "RESULT rejected")]
    public async Task HostedConsumerBundleRunsItsGeneratedEntryPointUnderNode(string? variant, string expectedResult)
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");

        await PublishConsumerAsync(consumerDirectory, "browser-wasm", defineConstants: variant);
        string result = await RunBrowserBundleAsync(GetPublishedWwwroot(consumerDirectory));
        Assert.That(result, Does.Contain("Configure ran.").And.Contain(expectedResult));
        if (variant is null)
        {
            Assert.That(result, Does.Contain("OnException ran: Configure failed on purpose."));
        }
        else
        {
            Assert.That(result, Does.Contain("Configure failed on purpose.").And.Not.Contain("OnException ran"));
        }
    }

    // A hand-written Main around a fake app reaches BrowserHost and pixely-host.js, which a generated Main cannot without SDL.
    [TestCase(null, "Frame 3.", "Loop ended after 3 frames with 0.", "RESULT exit code 40")]
    [TestCase("BROWSER_LOOP_THROWS", "Frame 2.", "Caught InvalidOperationException: Frame 3 failed on purpose.", "RESULT exit code 1")]
    public async Task BrowserHostRunsTheFrameLoopUnderNode(string? variant, string lastFrame, string outcome, string expectedResult)
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("BrowserLoopConsumer");
        DeleteConsumerOutputs("BrowserLoopConsumer");

        await PublishConsumerAsync(consumerDirectory, "browser-wasm", defineConstants: variant);
        string result = await RunBrowserBundleAsync(GetPublishedWwwroot(consumerDirectory));
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(lastFrame).And.Contain(outcome).And.Contain("Disposed.").And.Contain(expectedResult));
            Assert.That(result.IndexOf("Disposed.", StringComparison.Ordinal), Is.GreaterThan(result.IndexOf(outcome, StringComparison.Ordinal)));
        });
    }

    [Test]
    public async Task LibraryOnTheSdkStaysALibraryWhenTheAppIsPublishedForTheBrowser()
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");
        DeleteConsumerOutputs("LibraryConsumer");

        await PublishConsumerAsync(consumerDirectory, "browser-wasm", properties: ["HostedConsumerReferencesLibrary=true"]);
        string libraryDirectory = GetConsumerDirectory("LibraryConsumer");
        // Without the guard the library's restore pulls the WebAssembly pack, whose props turn it into an exe (CS5001) in the reference build,
        // and the WebAssembly props' SelfContained and PublishTrimmed pull the Mono browser runtime pack and ILLink into its restore.
        string libraryAssets = File.ReadAllText(Path.Combine(libraryDirectory, "obj", "project.assets.json"));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(libraryDirectory, "obj", "LibraryConsumer.csproj.nuget.g.props")), Does.Not.Contain("WebAssembly"));
            Assert.That(libraryAssets, Does.Not.Contain("Microsoft.NETCore.App.Runtime.Mono.browser-wasm").And.Not.Contain("Microsoft.NET.ILLink.Tasks"));
            Assert.That(File.Exists(Path.Combine(libraryDirectory, "bin", "Release", "net11.0", "LibraryConsumer.dll")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(libraryDirectory, "obj", "Release", "net11.0", "browser-wasm")), Is.False);
        });
    }

    [Test]
    public async Task ConsumerPageReplacesTheDefaultWithoutAConflict()
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");
        string consumerWwwroot = Path.Combine(consumerDirectory, "wwwroot");
        Directory.CreateDirectory(consumerWwwroot);
        File.WriteAllText(Path.Combine(consumerWwwroot, "index.html"), "<!DOCTYPE html><html><body>consumer page<script type=\"module\" src=\"main.js\"></script></body></html>");

        await PublishConsumerAsync(consumerDirectory, "browser-wasm");
        string wwwroot = Path.Combine(consumerDirectory, "bin", "Release", "net11.0", "browser-wasm", "publish", "wwwroot");
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(wwwroot, "index.html")), Does.Contain("consumer page"));
            Assert.That(File.Exists(Path.Combine(wwwroot, "main.js")), Is.True);
            Assert.That(File.Exists(Path.Combine(wwwroot, "pixely-host.js")), Is.True);
        });
    }

    [Test]
    public async Task BrowserRuntimeIdentifierInTheProjectBodyFailsWithThePlainMessage()
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");

        string output = await BuildConsumerAsync(consumerDirectory, expectSuccess: false, properties: ["HostedConsumerBodyRuntimeIdentifier=true"]);
        Assert.That(output, Does.Contain("error PIXELY0004").And.Contain("pass -r browser-wasm on the command line"));
    }

    [Test]
    public async Task ShaderCompilerSelectionUsesBuildHostInsteadOfTargetRuntime()
    {
        string consumerDirectory = GetConsumerDirectory("ShaderConsumer");
        DeleteConsumerOutputs("ShaderConsumer");
        string targetRuntime = OperatingSystem.IsWindows() ? "linux-x64" : "win-x64";

        await BuildConsumerAsync(consumerDirectory, targetRuntime);

        string shaderToolDirectory = Path.Combine(consumerDirectory, "obj", "Pixely.SdlangCompiler");
        string generatedDirectory = Path.Combine(consumerDirectory, "Content", "shaders", ".generated");
        string hostDirectory = GetRestoredSlangDirectory(GetCurrentSlangPlatform());
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(hostDirectory), Is.True);
            Assert.That(Directory.Exists(shaderToolDirectory), Is.False);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.vertex.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.vertex.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.vertex.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.metadata.json")), Is.True);
        });
    }

    [Test]
    public async Task CentrallyManagedConsumerTakesThePinsFromTheSdk()
    {
        string consumerDirectory = GetConsumerDirectory("CentralConsumer");
        DeleteConsumerOutputs("CentralConsumer");

        string buildOutput = await BuildConsumerAsync(consumerDirectory);
        Assert.That(buildOutput, Does.Not.Contain("NU1009").And.Not.Contain("NU1008"));

        string assetsFile = File.ReadAllText(Path.Combine(consumerDirectory, "obj", "project.assets.json"));
        Assert.Multiple(() =>
        {
            Assert.That(assetsFile, Does.Contain($"\"Pixely/{_packageVersion}\""));
            Assert.That(assetsFile, Does.Contain($"\"SlangDxcBundle.Toolchain/{GetSlangVersion()}\""));
            Assert.That(assetsFile, Does.Not.Contain("\"Pixely/0.0.0\""));
            Assert.That(assetsFile, Does.Not.Contain("\"SlangDxcBundle.Toolchain/1.0.0\""));
        });

        string output = await RunDotnetAsync(consumerDirectory, Path.Combine(consumerDirectory, "bin", "Release", "net11.0", "CentralConsumer.dll"));
        Assert.That(output, Does.Contain("Package consumer succeeded."));
    }

    [TestCase("ShaderFreeConsumer", "Pixely")]
    [TestCase("CentralConsumer", "Pixely and to SlangDxcBundle.Toolchain")]
    public async Task RedundantPackageReferenceIsReplacedByTheSdkPinWithAWarning(string consumer, string redundant)
    {
        string consumerDirectory = GetConsumerDirectory(consumer);
        DeleteConsumerOutputs(consumer);

        string buildOutput = await BuildConsumerAsync(consumerDirectory, properties: ["PixelyRedundantReference=true"]);
        string assetsFile = File.ReadAllText(Path.Combine(consumerDirectory, "obj", "project.assets.json"));
        Assert.Multiple(() =>
        {
            Assert.That(buildOutput, Does.Contain("warning PIXELY0001").And.Contain($"package reference to {redundant};"));
            Assert.That(buildOutput, Does.Not.Contain("NU1504").And.Not.Contain("NU1010").And.Not.Contain("NETSDK1023"));
            Assert.That(assetsFile, Does.Contain($"\"Pixely/{_packageVersion}\""));
            Assert.That(assetsFile, Does.Contain($"\"SlangDxcBundle.Toolchain/{GetSlangVersion()}\""));
            Assert.That(assetsFile, Does.Not.Contain("\"Pixely/0.0.0\""));
            Assert.That(assetsFile, Does.Not.Contain("\"SlangDxcBundle.Toolchain/1.0.0\""));
        });
    }

    [Test]
    public async Task PlainPackageReferenceFailsWithTheMigrationMessage()
    {
        string consumerDirectory = GetConsumerDirectory("PackageReferenceConsumer");
        DeleteConsumerOutputs("PackageReferenceConsumer");

        string output = await BuildConsumerAsync(consumerDirectory, expectSuccess: false);
        AssertSdkRequiredMessage(output);
    }

    // The guard is transitive: a project that reaches Pixely only through a project reference needs the SDK too.
    [Test]
    public async Task ProjectReferencingAPixelyProjectWithoutTheSdkFailsWithTheMigrationMessage()
    {
        string consumerDirectory = GetConsumerDirectory("TransitiveConsumer");
        DeleteConsumerOutputs("TransitiveConsumer");
        DeleteConsumerOutputs("LibraryConsumer");
        WriteConsumerConfiguration(GetConsumerDirectory("LibraryConsumer"));

        string output = await BuildConsumerAsync(consumerDirectory, expectSuccess: false);
        AssertSdkRequiredMessage(output);
        // The guard runs before the referenced project is built.
        Assert.That(Directory.Exists(Path.Combine(GetConsumerDirectory("LibraryConsumer"), "bin")), Is.False);
    }

    [Test]
    public async Task SdkListedBeforeTheBaseSdkFailsWithTheOrderMessage()
    {
        string consumerDirectory = GetConsumerDirectory("ReversedSdkConsumer");
        DeleteConsumerOutputs("ReversedSdkConsumer");

        string output = await BuildConsumerAsync(consumerDirectory, expectSuccess: false);
        Assert.That(output, Does.Contain("error PIXELY0003").And.Contain("Microsoft.NET.Sdk;Pixely/" + _packageVersion));
    }

    private void AssertSdkRequiredMessage(string output)
    {
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("error PIXELY0002").And.Contain("Pixely is an MSBuild project SDK"));
            Assert.That(output, Does.Contain($"<Sdk Name=\"Pixely\" Version=\"{_packageVersion}\" />"));
            Assert.That(output, Does.Contain($"\"msbuild-sdks\": {{ \"Pixely\": \"{_packageVersion}\" }} in global.json"));
        });
    }

    [Test]
    public async Task LibraryBuiltOnTheSdkDependsOnPixelyButNotOnTheToolchain()
    {
        string consumerDirectory = GetConsumerDirectory("LibraryConsumer");
        DeleteConsumerOutputs("LibraryConsumer");

        await BuildConsumerAsync(consumerDirectory);
        Assert.That(File.Exists(Path.Combine(consumerDirectory, "Content", "shaders", ".generated", "library.vertex.spv")), Is.True);

        await RunConsumerDotnetAsync(consumerDirectory, "pack", "--configuration", "Release", "--no-build", "--nologo");
        string packagePath = Path.Combine(consumerDirectory, "bin", "Release", "LibraryConsumer.1.0.0-preview.nupkg");
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        XDocument nuspec = XDocument.Parse(ReadPackageEntry(package, "LibraryConsumer.nuspec"));
        XNamespace ns = nuspec.Root?.Name.Namespace ?? throw new InvalidOperationException("LibraryConsumer.nuspec has no root element.");
        string[] dependencies = nuspec.Descendants(ns + "dependency").Select(dependency => (string?)dependency.Attribute("id") ?? "").ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(dependencies, Does.Contain("Pixely"));
            Assert.That(dependencies, Does.Not.Contain("SlangDxcBundle.Toolchain"));
        });
    }

    private Task<string> BuildConsumerAsync(string consumerDirectory, string? runtimeIdentifier = null, string? defineConstants = null, bool expectSuccess = true, string[]? properties = null)
    {
        return RunConsumerBuildCommandAsync("build", consumerDirectory, runtimeIdentifier, defineConstants, expectSuccess, properties);
    }

    private Task<string> PublishConsumerAsync(string consumerDirectory, string runtimeIdentifier, string? defineConstants = null, string[]? properties = null)
    {
        return RunConsumerBuildCommandAsync("publish", consumerDirectory, runtimeIdentifier, defineConstants, expectSuccess: true, properties);
    }

    private static string GetPublishedWwwroot(string consumerDirectory)
    {
        return Path.Combine(consumerDirectory, "bin", "Release", "net11.0", "browser-wasm", "publish", "wwwroot");
    }

    private async Task<string> RunConsumerBuildCommandAsync(string command, string consumerDirectory, string? runtimeIdentifier, string? defineConstants, bool expectSuccess, string[]? properties)
    {
        string[] projectPaths = Directory.GetFiles(consumerDirectory, "*.csproj");
        Assert.That(projectPaths, Has.Length.EqualTo(1), $"Expected one consumer project in {consumerDirectory}.");
        string projectPath = projectPaths[0];
        string projectContents = File.ReadAllText(projectPath);
        // Fixtures consume the package, never the repository sources.
        Assert.That(projectContents, Does.Not.Contain("src\\").And.Not.Contain("src/"));
        WriteConsumerConfiguration(consumerDirectory);
        List<string> restoreArguments =
        [
            "restore",
            projectPath,
            $"--property:PixelyPackageVersion={_packageVersion}",
            "--nologo"
        ];
        List<string> buildArguments =
        [
            command,
            projectPath,
            "--configuration",
            "Release",
            "--no-restore",
            $"--property:PixelyPackageVersion={_packageVersion}",
            "--nologo"
        ];
        if (runtimeIdentifier is not null)
        {
            restoreArguments.Add($"--property:RuntimeIdentifier={runtimeIdentifier}");
            restoreArguments.Add("--property:UseAppHost=false");
            buildArguments.Add($"--property:RuntimeIdentifier={runtimeIdentifier}");
            buildArguments.Add("--property:UseAppHost=false");
        }
        if (defineConstants is not null)
        {
            buildArguments.Add($"--property:DefineConstants={defineConstants}");
        }
        foreach (string property in properties ?? [])
        {
            restoreArguments.Add($"--property:{property}");
            buildArguments.Add($"--property:{property}");
        }

        // Restore warnings (NU*) only appear in the restore output, so callers get both outputs.
        string restoreOutput = await RunConsumerDotnetAsync(consumerDirectory, restoreArguments.ToArray());
        if (!expectSuccess)
        {
            (int exitCode, string failedOutput) = await RunDotnetExpectingExitCodeAsync(consumerDirectory, ConsumerEnvironment, buildArguments.ToArray());
            Assert.That(exitCode, Is.Not.EqualTo(0), failedOutput);
            return restoreOutput + failedOutput;
        }

        string buildOutput = await RunConsumerDotnetAsync(consumerDirectory, buildArguments.ToArray());
        // MSB4011 would mean the SDK and buildTransitive/Pixely.targets both imported the version props.
        Assert.That(restoreOutput + buildOutput, Does.Not.Contain("Downloading Slang").And.Not.Contain("MSB4011"));
        return restoreOutput + buildOutput;
    }

    private string GetSlangVersion()
    {
        return XDocument.Load(Path.Combine(_repositoryDirectory, "src", "Pixely.SdlangCompiler", "build", "Pixely.SdlangCompiler.props"))
            .Descendants().Single(element => element.Name.LocalName == "SlangVersion").Value;
    }

    // The SDK resolver reads NuGet.Config from the project directory and NUGET_PACKAGES, not restore's --source or RestorePackagesPath.
    // Pixely comes only from the local feed; source mapping keeps NU1507 away from the centrally managed consumer.
    private void WriteConsumerConfiguration(string consumerDirectory)
    {
        File.WriteAllText(Path.Combine(consumerDirectory, "NuGet.Config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
                <add key="pixely-package-tests" value="{SecurityElement.Escape(_packageDirectory)}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
                <packageSource key="pixely-package-tests">
                  <package pattern="Pixely" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        File.WriteAllText(Path.Combine(consumerDirectory, "global.json"), $$"""
            {
              "msbuild-sdks": {
                "Pixely": "{{_packageVersion}}"
              }
            }
            """);
    }

    private Dictionary<string, string> ConsumerEnvironment => new() { ["NUGET_PACKAGES"] = _packagesDirectory };

    private async Task<string> RunConsumerDotnetAsync(string consumerDirectory, params string[] arguments)
    {
        (int exitCode, string output) = await RunDotnetExpectingExitCodeAsync(consumerDirectory, ConsumerEnvironment, arguments);
        if (exitCode != 0)
        {
            Assert.Fail($"dotnet {string.Join(' ', arguments)} failed with exit code {exitCode}.{Environment.NewLine}{output}");
        }

        return output;
    }

    // requestAnimationFrame does not exist under node; a timer stands in for it. The script mirrors the package's main.js without a canvas.
    private static async Task<string> RunBrowserBundleAsync(string wwwroot)
    {
        string script = Path.Combine(wwwroot, "run-under-node.mjs");
        File.WriteAllText(script, """
            import { dotnet } from './_framework/dotnet.js';
            globalThis.requestAnimationFrame = (callback) => setTimeout(callback, 5);
            try {
                const exitCode = await dotnet.runMain();
                console.log(`RESULT exit code ${exitCode}`);
            } catch (error) {
                console.log(`RESULT rejected ${error}`);
            }
            """);
        (int exitCode, string output) = await RunProcessExpectingExitCodeAsync("node", wwwroot, null, script);
        Assert.That(exitCode, Is.EqualTo(0), output);
        return output;
    }

    private static void RequireNode()
    {
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (!directories.Any(directory => File.Exists(Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node"))))
        {
            Assert.Ignore("node is not installed; the browser bundle cannot run.");
        }
    }

    private string[] GetPackageDependencies()
    {
        List<string> dependencies = [];
        string centralPackageVersionsPath = Path.Combine(_repositoryDirectory, "Directory.Packages.props");
        Dictionary<string, string> centralPackageVersions = XDocument.Load(centralPackageVersionsPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion")
            .ToDictionary(
                element => (string?)element.Attribute("Include")
                    ?? throw new InvalidOperationException($"{centralPackageVersionsPath} contains a PackageVersion without Include."),
                element => (string?)element.Attribute("Version")
                    ?? element.Elements().SingleOrDefault(version => version.Name.LocalName == "Version")?.Value
                    ?? throw new InvalidOperationException($"{centralPackageVersionsPath} contains a PackageVersion without Version."),
                StringComparer.OrdinalIgnoreCase);
        string projectPath = Path.Combine(
            _repositoryDirectory,
            "packaging",
            "Pixely",
            "Pixely.Package.csproj");
        XDocument project = XDocument.Load(projectPath);
        foreach (XElement packageReference in project.Descendants().Where(
                     element => element.Name.LocalName == "PackageReference"))
        {
            string? privateAssets = (string?)packageReference.Attribute("PrivateAssets")
                ?? packageReference.Elements().SingleOrDefault(
                    element => element.Name.LocalName == "PrivateAssets")?.Value;
            if (string.Equals(privateAssets, "all", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string packageId = (string?)packageReference.Attribute("Include")
                ?? throw new InvalidOperationException($"{projectPath} contains a PackageReference without Include.");
            string packageVersion = (string?)packageReference.Attribute("VersionOverride")
                ?? packageReference.Elements().SingleOrDefault(
                    element => element.Name.LocalName == "VersionOverride")?.Value
                ?? (string?)packageReference.Attribute("Version")
                ?? packageReference.Elements().SingleOrDefault(
                    element => element.Name.LocalName == "Version")?.Value
                ?? centralPackageVersions.GetValueOrDefault(packageId)
                ?? throw new InvalidOperationException($"{projectPath} contains a PackageReference without a project or central version.");
            dependencies.Add($"{packageId}:{packageVersion}");
        }

        return dependencies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsNuGetEmptyFolderPlaceholder(string entry)
    {
        return entry == "_._" || entry.EndsWith("/_._", StringComparison.Ordinal);
    }

    private static string ReadPackageEntry(ZipArchive package, string entryName)
    {
        ZipArchiveEntry entry = package.GetEntry(entryName)
            ?? throw new InvalidOperationException($"{entryName} is missing from the package.");
        using Stream stream = entry.Open();
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static string GetCurrentSlangPlatform()
    {
        Architecture architecture = RuntimeInformation.OSArchitecture;
        if (OperatingSystem.IsLinux() && architecture == Architecture.X64)
        {
            return "linux-x86_64";
        }

        if (OperatingSystem.IsLinux() && architecture == Architecture.Arm64)
        {
            return "linux-aarch64";
        }

        if (OperatingSystem.IsWindows() && architecture == Architecture.X64)
        {
            return "windows-x86_64";
        }

        if (OperatingSystem.IsMacOS() && architecture == Architecture.Arm64)
        {
            return "macos-aarch64";
        }

        throw new PlatformNotSupportedException(
            $"Unsupported package-integration host: {RuntimeInformation.OSDescription} {architecture}.");
    }

    private string GetRestoredSlangDirectory(string platform)
    {
        string packageDirectory = Path.Combine(_packagesDirectory, "slangdxcbundle.toolchain");
        string[] restoredPackageDirectories = Directory.Exists(packageDirectory) ? Directory.GetDirectories(packageDirectory) : [];
        Assert.That(restoredPackageDirectories, Has.Length.EqualTo(1), $"Expected exactly one restored SlangDxcBundle.Toolchain package in {packageDirectory}.");
        return Path.Combine(restoredPackageDirectories.Single(), "tools", "slang", platform);
    }

    private string GetConsumerDirectory(string name)
    {
        return Path.Combine(_repositoryDirectory, "tests", "Pixely.Package.Tests", "Consumers", name);
    }

    private void DeleteConsumerOutputs(string name)
    {
        if (string.IsNullOrEmpty(_repositoryDirectory))
        {
            return;
        }

        string consumerDirectory = GetConsumerDirectory(name);
        DeleteDirectory(Path.Combine(consumerDirectory, "bin"));
        DeleteDirectory(Path.Combine(consumerDirectory, "obj"));
        DeleteDirectory(Path.Combine(consumerDirectory, "Content", "shaders", ".generated"));
        // written by ConsumerPageReplacesTheDefaultWithoutAConflict; no fixture has a wwwroot of its own
        DeleteDirectory(Path.Combine(consumerDirectory, "wwwroot"));
        File.Delete(Path.Combine(consumerDirectory, "NuGet.Config"));
        File.Delete(Path.Combine(consumerDirectory, "global.json"));
    }

    private static string GetRepositoryDirectory()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Pixely.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository directory.");
    }

    private static async Task<string> RunDotnetAsync(string workingDirectory, params string[] arguments)
    {
        (int exitCode, string output) = await RunDotnetExpectingExitCodeAsync(workingDirectory, null, arguments);
        if (exitCode != 0)
        {
            Assert.Fail($"dotnet {string.Join(' ', arguments)} failed with exit code {exitCode}.{Environment.NewLine}{output}");
        }

        return output;
    }

    private static Task<(int ExitCode, string Output)> RunDotnetExpectingExitCodeAsync(string workingDirectory, params string[] arguments)
    {
        return RunDotnetExpectingExitCodeAsync(workingDirectory, null, arguments);
    }

    private static Task<(int ExitCode, string Output)> RunDotnetExpectingExitCodeAsync(string workingDirectory, Dictionary<string, string>? environment, params string[] arguments)
    {
        return RunProcessExpectingExitCodeAsync("dotnet", workingDirectory, environment, arguments);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessExpectingExitCodeAsync(string fileName, string workingDirectory, Dictionary<string, string>? environment, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach ((string name, string value) in environment ?? [])
        {
            startInfo.Environment[name] = value;
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException(
                $"{fileName} {string.Join(' ', arguments)} exceeded the ten-minute test timeout.");
        }
        string output = await standardOutput;
        string error = await standardError;
        return (process.ExitCode, output + Environment.NewLine + error);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
