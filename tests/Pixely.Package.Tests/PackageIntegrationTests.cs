using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework.Interfaces;

namespace Pixely.Package.Tests;

[Category("PackageIntegration")]
[Explicit("Builds and consumes a local NuGet package and must execute in isolation.")]
[NonParallelizable]
public class PackageIntegrationTests
{
    private const long NuGetPackageSizeLimitBytes = 250_000_000;
    // NuGet's folder name for net11.0-browser carries the platform version.
    private const string BrowserLibFolder = "lib/net11.0-browser1.0";
    private static readonly string[] LibFolders = ["lib/net11.0", BrowserLibFolder];
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
        "Pixely.Ui",
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
        "BrowserLoopConsumer",
        "BrowserContentConsumer",
        "BrowserContentLibrary",
        "MultiTargetConsumer"
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

    // CI splits the tests between jobs with PIXELY_PACKAGE_TEST_SHARD, such as 1/2 for the first of two: the test methods, ordered by
    // name, are dealt out in turn, which spreads the slow browser tests that share a prefix. A test filter cannot take the rest of a
    // split: a negated filter does not select an explicit test.
    [SetUp]
    public void SkipTestOfAnotherShard()
    {
        string? shard = Environment.GetEnvironmentVariable("PIXELY_PACKAGE_TEST_SHARD");
        if (shard is null)
        {
            return;
        }

        string[] parts = shard.Split('/');
        Assert.That(parts, Has.Length.EqualTo(2), "PIXELY_PACKAGE_TEST_SHARD is <index>/<count>, such as 1/2.");
        int index = int.Parse(parts[0]);
        int count = int.Parse(parts[1]);
        Assert.That(index, Is.InRange(1, count), "PIXELY_PACKAGE_TEST_SHARD is <index>/<count>, such as 1/2.");
        string[] methods = typeof(PackageIntegrationTests).GetMethods()
            .Where(method => method.GetCustomAttributes(inherit: false).Any(attribute => attribute is ISimpleTestBuilder or ITestBuilder))
            .Select(method => method.Name).Order(StringComparer.Ordinal).ToArray();
        int position = Array.IndexOf(methods, TestContext.CurrentContext.Test.MethodName);
        if (position % count != index - 1)
        {
            Assert.Ignore($"Runs in shard {position % count + 1}/{count}.");
        }
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

        // Every assembly is in both folders: NuGet takes one lib/ folder per consumer. Only Pixely has a browser build; the others are the same file.
        string[] versionComponents = _packageVersion.Split('-')[0].Split('.');
        Version expectedAssemblyVersion = new(int.Parse(versionComponents[0]), int.Parse(versionComponents[1]), int.Parse(versionComponents[2]), 0);
        foreach (string libFolder in LibFolders)
        {
            foreach (string assembly in RuntimeAssemblies)
            {
                string entryName = $"{libFolder}/{assembly}.dll";
                Assert.That(entries, Does.Contain(entryName));
                byte[] assemblyBytes = ReadPackageEntryBytes(package, entryName);
                using PEReader peReader = new(new MemoryStream(assemblyBytes));
                Assert.That(peReader.GetMetadataReader().GetAssemblyDefinition().Version, Is.EqualTo(expectedAssemblyVersion), entryName);
                if (libFolder != BrowserLibFolder)
                {
                    continue;
                }

                byte[] desktopBytes = ReadPackageEntryBytes(package, $"lib/net11.0/{assembly}.dll");
                Assert.That(assemblyBytes.AsSpan().SequenceEqual(desktopBytes), Is.EqualTo(assembly != "Pixely"), $"{entryName} against the desktop build");
            }
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
            Assert.That(entries.Where(entry => entry.StartsWith("lib/", StringComparison.Ordinal)).Select(entry => entry[..entry.LastIndexOf('/')]).Distinct(), Is.EquivalentTo(LibFolders));
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
        // One group per framework with the same dependencies, so a browser consumer restores the same packages as a desktop one.
        Dictionary<string, string[]> dependencyGroups = metadata
            .Descendants(ns + "group")
            .ToDictionary(
                group => (string?)group.Attribute("targetFramework") ?? throw new InvalidOperationException("Pixely.nuspec has a dependency group without a target framework."),
                group => group.Elements(ns + "dependency")
                    .Select(dependency => $"{(string?)dependency.Attribute("id")}:{(string?)dependency.Attribute("version")}")
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        string[] dependencies = dependencyGroups.Values.SelectMany(group => group).Distinct().Order(StringComparer.Ordinal).ToArray();

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
            Assert.That(dependencyGroups.Keys, Is.EquivalentTo(["net11.0", "net11.0-browser1.0"]));
            Assert.That(dependencyGroups.Values, Has.All.EqualTo(expectedDependencies.Order(StringComparer.Ordinal)));
            Assert.That(dependencies.Any(dependency => dependency.StartsWith("Pixely", StringComparison.Ordinal)), Is.False);
            Assert.That(dependencies.Any(dependency => dependency.StartsWith("SlangDxcBundle.Toolchain", StringComparison.Ordinal)), Is.False);
            Assert.That(nuspecContents, Does.Not.Contain("Package Description"));
            Assert.That(nuspecContents, Does.Not.Contain("_._"));
        });

        using ZipArchive symbols = ZipFile.OpenRead(_symbolPackagePath);
        HashSet<string> symbolEntries = symbols.Entries
            .Select(entry => entry.FullName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string libFolder in LibFolders)
        {
            foreach (string assembly in RuntimeAssemblies)
            {
                Assert.That(symbolEntries, Does.Contain($"{libFolder}/{assembly}.pdb"));
            }
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
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.vertex.wgsl")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.wgsl")), Is.True);
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
    // The fixture exits with 3 from AppDomain.UnhandledException, which fires only once the exception has left Main; the no-handler
    // variant is covered by the browser publish under node.
    [Test]
    public async Task HostedConsumerWithoutAnApplicableOnExceptionLetsTheFailurePropagate()
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");

        await BuildConsumerAsync(consumerDirectory, defineConstants: "HOSTED_CONSUMER_NARROW_HANDLER");
        string outputDirectory = Path.Combine(consumerDirectory, "bin", "Release", "net11.0");
        (int exitCode, string output) = await RunDotnetExpectingExitCodeAsync(
            consumerDirectory,
            Path.Combine(outputDirectory, "HostedConsumer.dll"));
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(3));
            Assert.That(output, Does.Contain("Configure ran."));
            Assert.That(output, Does.Contain("Unhandled: Configure failed on purpose."));
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

    // One browser publish covers the layout, the consumer's own asset replacing a default, PixelyBrowserBrotli, a referenced library
    // staying a library and, under node, the generated default OnException rethrowing from the async Main (the fixture is compiled
    // without a handler). The publish is the one command a consumer runs, with --runtime and its implicit restore: the SDK switches the
    // framework after the project body, restore reads the switched value, and the browser build of Pixely, not the desktop one, is what
    // the app references.
    [Test]
    public async Task HostedConsumerPublishesABrowserBundle()
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        string libraryDirectory = GetConsumerDirectory("LibraryConsumer");
        DeleteConsumerOutputs("HostedConsumer");
        DeleteConsumerOutputs("LibraryConsumer");
        // The check is per file, so main.js stands in for any of the defaults and index.html stays the package's.
        string consumerWwwroot = Path.Combine(consumerDirectory, "wwwroot");
        Directory.CreateDirectory(consumerWwwroot);
        File.WriteAllText(Path.Combine(consumerWwwroot, "main.js"), "// consumer bootstrap\n");
        WriteConsumerConfiguration(consumerDirectory);

        string projectPath = Directory.GetFiles(consumerDirectory, "*.csproj").Single();
        await RunConsumerDotnetAsync(consumerDirectory, "publish", projectPath, "--configuration", "Release", "--runtime", "browser-wasm", "--property:UseAppHost=false", $"--property:PixelyPackageVersion={_packageVersion}",
            "--property:DefineConstants=HOSTED_CONSUMER_NO_HANDLER", "--property:HostedConsumerReferencesLibrary=true", "--property:PixelyBrowserBrotli=true", "--nologo");
        string generatedFile = Path.Combine(consumerDirectory, "obj", "Release", "net11.0-browser", "browser-wasm", "PixelyProgram.g.cs");
        string wwwroot = GetPublishedWwwroot(consumerDirectory);
        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(consumerDirectory, "obj", "project.assets.json")));
        string[] targets = assets.RootElement.GetProperty("targets").EnumerateObject().Select(target => target.Name).ToArray();
        using ZipArchive package = ZipFile.OpenRead(_packagePath);
        byte[] browserPixely = ReadPackageEntryBytes(package, $"{BrowserLibFolder}/Pixely.dll");
        byte[] referencedPixely = File.ReadAllBytes(Path.Combine(consumerDirectory, "bin", "Release", "net11.0-browser", "browser-wasm", "Pixely.dll"));
        // Without the guard the library's restore pulls the WebAssembly pack, whose props turn it into an exe (CS5001) in the reference build,
        // and the WebAssembly props' SelfContained and PublishTrimmed pull the Mono browser runtime pack and ILLink into its restore.
        string libraryAssets = File.ReadAllText(Path.Combine(libraryDirectory, "obj", "project.assets.json"));
        Assert.Multiple(() =>
        {
            Assert.That(targets, Is.EquivalentTo(["net11.0-browser", "net11.0-browser/browser-wasm"]));
            Assert.That(referencedPixely.AsSpan().SequenceEqual(browserPixely), Is.True, "The app referenced the desktop build of Pixely.");
            Assert.That(Directory.Exists(Path.Combine(consumerDirectory, "bin", "Release", "net11.0")), Is.False);
            Assert.That(File.ReadAllText(generatedFile), Does.Contain("[global::System.Runtime.Versioning.SupportedOSPlatform(\"browser\")]")
                .And.Contain("private static async global::System.Threading.Tasks.Task<int> Main()")
                .And.Contain("return await global::Pixely.App.BrowserHost.RunAsync(app);")
                .And.Not.Contain("app.Run()"));
            Assert.That(File.ReadAllText(Path.Combine(wwwroot, "index.html")), Does.Contain("<canvas id=\"canvas\""));
            Assert.That(File.ReadAllText(Path.Combine(wwwroot, "main.js")), Does.Contain("consumer bootstrap"));
            Assert.That(File.Exists(Path.Combine(wwwroot, "pixely-host.js")), Is.True);
            // The publish merges its boot config into dotnet.js.
            Assert.That(File.ReadAllText(Path.Combine(wwwroot, "_framework", "dotnet.js")), Does.Match(@"""pixely"":\s*\{\s*""brotli"":\s*true"));
            // dotnet.js is not fingerprinted on disk; the assemblies (WebCIL) are, and the endpoint manifest aliases their plain names.
            Assert.That(File.Exists(Path.Combine(wwwroot, "_framework", "dotnet.js")), Is.True);
            Assert.That(Directory.GetFiles(Path.Combine(wwwroot, "_framework"), "HostedConsumer.*.wasm"), Has.Length.EqualTo(1));
            // loadCompressedResource in pixely-host.js fetches these beside the files they compress.
            Assert.That(Directory.GetFiles(Path.Combine(wwwroot, "_framework"), "HostedConsumer.*.wasm.br"), Has.Length.EqualTo(1));
            Assert.That(Directory.GetFiles(Path.Combine(wwwroot, "_framework"), "dotnet.native.*.wasm.br"), Has.Length.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(libraryDirectory, "obj", "LibraryConsumer.csproj.nuget.g.props")), Does.Not.Contain("WebAssembly"));
            Assert.That(libraryAssets, Does.Not.Contain("Microsoft.NETCore.App.Runtime.Mono.browser-wasm").And.Not.Contain("Microsoft.NET.ILLink.Tasks"));
            Assert.That(File.Exists(Path.Combine(libraryDirectory, "bin", "Release", "net11.0", "LibraryConsumer.dll")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(libraryDirectory, "obj", "Release", "net11.0", "browser-wasm")), Is.False);
        });

        if (HasNode())
        {
            string result = await RunBrowserBundleAsync(wwwroot, environment: null);
            Assert.That(result, Does.Contain("Configure ran.").And.Contain("Configure failed on purpose.").And.Not.Contain("OnException ran").And.Contain("RESULT rejected"));
            AssertBrotliLoaderOutcome(await RunBrotliLoaderAsync(wwwroot, "HostedConsumer"));
            string decompressed = await RunBrowserBundleWithBrotliLoaderAsync(wwwroot);
            Assert.That(decompressed, Does.Contain("Configure failed on purpose.").And.Contain("RESULT rejected").And.Not.Contain("without Brotli").And.Match(@"RESULT brotli [1-9]"));
        }

        // A desktop build afterwards keeps its own obj/ and bin/ and its synchronous Main.
        await BuildConsumerAsync(consumerDirectory);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(consumerDirectory, "obj", "Release", "net11.0", "PixelyProgram.g.cs")), Does.Contain("private static int Main()"));
            Assert.That(File.ReadAllText(generatedFile), Does.Contain("BrowserHost.RunAsync"));
        });
    }

    // The bundle runs under node as it would in a page: the generated async Main runs Configure, which fails on purpose, and the handler
    // decides whether runMain() resolves with its return value or rejects with the exception. One publish serves both outcomes: the
    // fixture's handler rethrows when HOSTED_CONSUMER_RETHROW is set, which the node script passes through dotnet.withEnvironmentVariable.
    [Test]
    public async Task HostedConsumerBundleRunsItsGeneratedEntryPointUnderNode()
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");

        // PixelyBrowserBrotli without .br files leaves the loader that main.js installs off, with a warning.
        string publishOutput = await PublishConsumerAsync(consumerDirectory, "browser-wasm", properties: ["PixelyBrowserBrotli=true", "PublishCompressionFormats=gzip"]);
        string wwwroot = GetPublishedWwwroot(consumerDirectory);
        string handled = await RunBrowserBundleAsync(wwwroot, environment: null);
        string rethrown = await RunBrowserBundleAsync(wwwroot, environment: new() { ["HOSTED_CONSUMER_RETHROW"] = "1" });
        Assert.Multiple(() =>
        {
            Assert.That(publishOutput, Does.Contain("PIXELY0012"));
            Assert.That(Directory.GetFiles(Path.Combine(wwwroot, "_framework"), "*.br"), Is.Empty);
            Assert.That(File.ReadAllText(Path.Combine(wwwroot, "_framework", "dotnet.js")), Does.Match(@"""pixely"":\s*\{\s*""brotli"":\s*false"));
            Assert.That(handled, Does.Contain("Configure ran.").And.Contain("OnException ran: Configure failed on purpose.").And.Contain("RESULT exit code 1"));
            Assert.That(rethrown, Does.Contain("Configure ran.").And.Contain("Configure failed on purpose.").And.Not.Contain("OnException ran").And.Contain("RESULT rejected"));
        });
    }

    // A hand-written Main around a fake app reaches BrowserHost and pixely-host.js, which a generated Main cannot without SDL. One publish
    // serves both outcomes: the fixture's third frame throws when BROWSER_LOOP_THROWS is set.
    [Test]
    public async Task BrowserHostRunsTheFrameLoopUnderNode()
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("BrowserLoopConsumer");
        DeleteConsumerOutputs("BrowserLoopConsumer");

        await PublishConsumerAsync(consumerDirectory, "browser-wasm");
        string wwwroot = GetPublishedWwwroot(consumerDirectory);
        AssertBrowserLoopOutcome(await RunBrowserBundleAsync(wwwroot, environment: null), "Frame 3 of 3.", "Loop ended after 3 frames with 0.", "RESULT exit code 40");
        AssertBrowserLoopOutcome(await RunBrowserBundleAsync(wwwroot, environment: new() { ["BROWSER_LOOP_THROWS"] = "1" }), "Frame 2 of 3.", "Caught InvalidOperationException: Frame 3 failed on purpose.", "RESULT exit code 1");
    }

    // A native reference relinks the runtime (wasm-tools workload) and its file name is the P/Invoke module DllImport binds to.
    [Test]
    public async Task NativeReferenceIsRelinkedAndBoundInTheBrowserBuild()
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("BrowserLoopConsumer");
        DeleteConsumerOutputs("BrowserLoopConsumer");
        await RequireWasmToolsAsync(consumerDirectory);

        await PublishConsumerAsync(consumerDirectory, "browser-wasm", defineConstants: "BROWSER_LOOP_NATIVE", properties: ["BrowserLoopConsumerNative=true"]);
        AssertBrowserLoopOutcome(await RunBrowserBundleAsync(GetPublishedWwwroot(consumerDirectory), environment: null), "Frame 3 of 3.", "Loop ended after 3 frames with 0.", "RESULT exit code 40");
    }

    // The URL answers with a redirect, as a GitHub release asset does, and is declared twice. The cache folder is set in Directory.Build.targets.
    // The second publish finds the file in the cache and downloads nothing.
    [Test]
    public async Task NativeUrlReferenceIsDownloadedVerifiedAndRelinked()
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("BrowserLoopConsumer");
        DeleteConsumerOutputs("BrowserLoopConsumer");
        await RequireWasmToolsAsync(consumerDirectory);
        string nativeSource = Path.Combine(consumerDirectory, "native.c");
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nativeSource)));
        string cacheDirectory = Path.Combine(_testArtifactsDirectory, "native-url-cache");
        using ReleaseAssetServer server = ReleaseAssetServer.Start(nativeSource);
        string[] properties = [$"BrowserLoopConsumerNativeUrl={server.AssetUrl}", $"BrowserLoopConsumerNativeSha256={sha256}", $"BrowserLoopConsumerNativeUrlCache={cacheDirectory}"];

        await PublishConsumerAsync(consumerDirectory, "browser-wasm", defineConstants: "BROWSER_LOOP_NATIVE", properties: properties);
        AssertBrowserLoopOutcome(await RunBrowserBundleAsync(GetPublishedWwwroot(consumerDirectory), environment: null), "Frame 3 of 3.", "Loop ended after 3 frames with 0.", "RESULT exit code 40");
        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFiles(cacheDirectory, "*", SearchOption.AllDirectories), Is.EqualTo(new[] { Path.Combine(cacheDirectory, sha256, "native.c") }));
            Assert.That(server.AssetDownloads, Is.EqualTo(1));
        });

        DeleteConsumerOutputs("BrowserLoopConsumer");
        await PublishConsumerAsync(consumerDirectory, "browser-wasm", defineConstants: "BROWSER_LOOP_NATIVE", properties: properties);
        Assert.That(server.AssetDownloads, Is.EqualTo(1));
    }

    // The first reference matches its hash and the second does not; neither is cached nor left behind as a partial download.
    [Test]
    public async Task NativeUrlReferenceWithAnotherHashFailsAndCachesNothing()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserLoopConsumer");
        DeleteConsumerOutputs("BrowserLoopConsumer");
        string nativeSource = Path.Combine(consumerDirectory, "native.c");
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nativeSource)));
        // The default cache, in the project's obj folder, which DeleteConsumerOutputs has deleted.
        string cacheDirectory = Path.Combine(consumerDirectory, "obj", "native-url-cache");
        string wrongSha256 = new('0', 64);
        using ReleaseAssetServer server = ReleaseAssetServer.Start(nativeSource);
        string otherAssetUrl = server.GetAssetUrl("other.c");

        string output = await BuildConsumerAsync(consumerDirectory, "browser-wasm", expectSuccess: false,
            properties: [$"BrowserLoopConsumerNativeUrl={server.AssetUrl}", $"BrowserLoopConsumerNativeSha256={sha256}", $"BrowserLoopConsumerOtherNativeUrl={otherAssetUrl}",
                $"BrowserLoopConsumerOtherNativeSha256={wrongSha256}"]);
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("error PIXELY0009").And.Contain(otherAssetUrl));
            Assert.That(server.AssetDownloads, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(cacheDirectory, "*", SearchOption.AllDirectories), Is.Empty);
        });
    }

    // The first reference downloads and the second answers 404; the first is not cached nor left behind as a partial download.
    [Test]
    public async Task NativeUrlReferenceThatFailsToDownloadCachesNothing()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserLoopConsumer");
        DeleteConsumerOutputs("BrowserLoopConsumer");
        string nativeSource = Path.Combine(consumerDirectory, "native.c");
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nativeSource)));
        // The default cache, in the project's obj folder, which DeleteConsumerOutputs has deleted.
        string cacheDirectory = Path.Combine(consumerDirectory, "obj", "native-url-cache");
        using ReleaseAssetServer server = ReleaseAssetServer.Start(nativeSource);
        string missingUrl = server.GetMissingUrl("other.c");

        string output = await BuildConsumerAsync(consumerDirectory, "browser-wasm", expectSuccess: false,
            properties: [$"BrowserLoopConsumerNativeUrl={server.AssetUrl}", $"BrowserLoopConsumerNativeSha256={sha256}", $"BrowserLoopConsumerOtherNativeUrl={missingUrl}",
                $"BrowserLoopConsumerOtherNativeSha256={sha256}"]);
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain(missingUrl));
            Assert.That(server.AssetDownloads, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(cacheDirectory, "*", SearchOption.AllDirectories), Is.Empty);
        });
    }

    private sealed class ReleaseAssetServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly byte[] _content;
        private readonly Task _serving;
        private readonly string _contentFileName;
        private readonly int _port;
        private int _assetDownloads;

        private ReleaseAssetServer(HttpListener listener, string contentPath, int port)
        {
            _listener = listener;
            _content = File.ReadAllBytes(contentPath);
            _contentFileName = Path.GetFileName(contentPath);
            _port = port;
            _serving = Task.Run(ServeAsync);
        }

        public string AssetUrl => GetAssetUrl(_contentFileName);

        public int AssetDownloads => Volatile.Read(ref _assetDownloads);

        // Every release asset path redirects to the same content.
        public string GetAssetUrl(string fileName)
        {
            return $"http://127.0.0.1:{_port}/releases/download/v1/{fileName}";
        }

        public string GetMissingUrl(string fileName)
        {
            return $"http://127.0.0.1:{_port}/missing/{fileName}";
        }

        public static ReleaseAssetServer Start(string contentPath)
        {
            TcpListener portProbe = new(IPAddress.Loopback, 0);
            portProbe.Start();
            int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();
            HttpListener listener = new();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            return new ReleaseAssetServer(listener, contentPath, port);
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                string path = context.Request.Url?.AbsolutePath ?? "";
                if (path.StartsWith("/releases/download/", StringComparison.Ordinal))
                {
                    context.Response.Redirect("/objects/asset?response-content-disposition=attachment");
                }
                else if (path == "/objects/asset")
                {
                    Interlocked.Increment(ref _assetDownloads);
                    context.Response.ContentType = "application/octet-stream";
                    await context.Response.OutputStream.WriteAsync(_content);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Close();
            _serving.Wait();
        }
    }

    private static void AssertBrowserLoopOutcome(string result, string lastFrame, string outcome, string expectedResult)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(lastFrame).And.Contain(outcome).And.Contain("Disposed.").And.Contain(expectedResult));
            Assert.That(result.IndexOf("Disposed.", StringComparison.Ordinal), Is.GreaterThan(result.IndexOf(outcome, StringComparison.Ordinal)));
        });
    }

    // The fixture zips its Content tree, generated shaders included, into a PixelyBrowserVfsFile; UseDefaultContent's loader finds it at
    // /Content.pk3. A second publish after a change and a deletion in Content replaces the archive. Only the runs need node.
    [Test]
    public async Task BrowserPublishLoadsAndReplacesContentArchiveThroughDefaultContent()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");
        string greetingPath = Path.Combine(consumerDirectory, "Content", "greeting.txt");
        string greeting = File.ReadAllText(greetingPath);
        string removedPath = Path.Combine(consumerDirectory, "Content", "removed.txt");
        try
        {
            File.WriteAllText(removedPath, "Removed before the second publish\n");
            await PublishConsumerAsync(consumerDirectory, "browser-wasm");
            string wwwroot = GetPublishedWwwroot(consumerDirectory);
            string archivePath = Path.Combine(wwwroot, "Content.pk3");
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(wwwroot, "_framework", "dotnet.js")), Does.Contain(BrowserContentArchiveBootEntry));
                Assert.That(ReadArchiveEntries(archivePath), Does.Contain(BrowserContentGeneratedShader).And.Contain("removed.txt"));
            });
            if (HasNode())
            {
                AssertBrowserContentLoaded(await RunBrowserBundleAsync(wwwroot, environment: null));
            }

            File.WriteAllText(greetingPath, "Hello from the changed archive\n");
            File.Delete(removedPath);
            await PublishConsumerAsync(consumerDirectory, "browser-wasm");
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')), Does.Not.Contain("removed.txt"));
                    Assert.That(ReadPackageEntry(archive, "greeting.txt"), Does.Contain("Hello from the changed archive"));
                });
            }
            if (HasNode())
            {
                Assert.That(await RunBrowserBundleAsync(wwwroot, environment: null), Does.Contain("RESULT greeting Hello from the changed archive"));
            }
        }
        finally
        {
            File.WriteAllText(greetingPath, greeting);
            File.Delete(removedPath);
        }
    }

    // The CoreCLR runtime would relink by default in a trimmed publish, which the managed-only fixture does not need.
    [Test]
    public async Task CoreClrBrowserPublishLoadsContentArchiveThroughDefaultContent()
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");

        await PublishConsumerAsync(consumerDirectory, "browser-wasm", properties: ["UseMonoRuntime=false", "WasmBuildNative=false"]);
        string wwwroot = GetPublishedWwwroot(consumerDirectory);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(wwwroot, "_framework", "dotnet.js")), Does.Contain(BrowserContentArchiveBootEntry));
            Assert.That(ReadArchiveEntries(Path.Combine(wwwroot, "Content.pk3")), Does.Contain(BrowserContentGeneratedShader));
        });
        AssertBrowserContentLoaded(await RunBrowserBundleAsync(wwwroot, environment: null));
    }

    // A build is what dotnet run serves; its boot config and manifest list the archive without a publish.
    [Test]
    public async Task BrowserBuildListsContentArchiveInBuildBootConfig()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");

        await BuildConsumerAsync(consumerDirectory, runtimeIdentifier: "browser-wasm");
        string intermediateDirectory = GetBrowserIntermediateDirectory(consumerDirectory);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(Path.Combine(intermediateDirectory, "dotnet.js")), Does.Contain(BrowserContentArchiveBootEntry));
            Assert.That(File.ReadAllText(Path.Combine(intermediateDirectory, "staticwebassets.build.json")), Does.Contain("vfs:Content.pk3"));
        });
    }

    // Invoked directly, the static web asset inputs still compile first, so the archive has the generated shaders of a clean tree.
    [Test]
    public async Task ResolvingStaticWebAssetInputsCompilesBeforePackaging()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");

        await RunBrowserConsumerTargetAsync(consumerDirectory, "ResolveStaticWebAssetsInputs");
        Assert.That(ReadArchiveEntries(Path.Combine(GetBrowserIntermediateDirectory(consumerDirectory), "Content.pk3")), Does.Contain(BrowserContentGeneratedShader));
    }

    // The WebAssembly targets in the same call need a compiled assembly, so a build comes first; its archive and the marker of the
    // fixture's BeforeTargets hook are then removed. Neither the producer nor the hook runs again.
    [Test]
    public async Task DesignTimeBuildDoesNotPackageBrowserContent()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");
        await BuildConsumerAsync(consumerDirectory, runtimeIdentifier: "browser-wasm");
        string intermediateDirectory = GetBrowserIntermediateDirectory(consumerDirectory);
        string hookMarkerPath = Path.Combine(intermediateDirectory, BrowserContentHookMarker);
        Assert.That(File.Exists(hookMarkerPath), Is.True);
        File.Delete(hookMarkerPath);
        File.Delete(Path.Combine(intermediateDirectory, "Content.pk3"));
        DeleteDirectory(Path.Combine(intermediateDirectory, "pixely-browser-vfs"));

        await RunBrowserConsumerTargetAsync(consumerDirectory, "ResolveStaticWebAssetsInputs", "DesignTimeBuild=true");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(intermediateDirectory, "Content.pk3")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(intermediateDirectory, "pixely-browser-vfs")), Is.False);
            Assert.That(File.Exists(hookMarkerPath), Is.False);
        });
    }

    // The nested publish of a relinked runtime and a publish without build skip ComputeWasmVfs and reload the build manifest, which keeps
    // the tag Pixely gave the asset. Without a build nothing packages again: the archive in obj keeps its time stamp, and the fixture's
    // BeforeTargets hook leaves no marker.
    [Test]
    public async Task RelinkedBrowserPublishKeepsVfsAfterPublishWithoutBuild()
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");
        await RequireWasmToolsAsync(consumerDirectory);

        string[] properties = ["BrowserContentConsumerNative=true"];
        await PublishConsumerAsync(consumerDirectory, "browser-wasm", properties: properties);
        string wwwroot = GetPublishedWwwroot(consumerDirectory);
        Assert.That(File.ReadAllText(Path.Combine(wwwroot, "_framework", "dotnet.js")), Does.Contain(BrowserContentArchiveBootEntry));
        string archivePath = Path.Combine(GetBrowserIntermediateDirectory(consumerDirectory), "Content.pk3");
        DateTime packagedAt = File.GetLastWriteTimeUtc(archivePath);
        string hookMarkerPath = Path.Combine(GetBrowserIntermediateDirectory(consumerDirectory), BrowserContentHookMarker);
        Assert.That(File.Exists(hookMarkerPath), Is.True);
        File.Delete(hookMarkerPath);

        await PublishConsumerAsync(consumerDirectory, "browser-wasm", properties: properties, noBuild: true);
        Assert.Multiple(() =>
        {
            Assert.That(File.GetLastWriteTimeUtc(archivePath), Is.EqualTo(packagedAt));
            Assert.That(File.Exists(hookMarkerPath), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(wwwroot, "_framework", "dotnet.js")), Does.Contain(BrowserContentArchiveBootEntry));
        });
        AssertBrowserContentLoaded(await RunBrowserBundleAsync(wwwroot, environment: null));
    }

    [Test]
    public async Task BrowserPublishPlacesNestedTargetPath()
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");

        await PublishConsumerAsync(consumerDirectory, "browser-wasm", properties: ["BrowserContentConsumerExtraFile=data/levels.pak"]);
        string result = await RunBrowserBundleAsync(GetPublishedWwwroot(consumerDirectory), environment: new() { ["BROWSER_CONTENT_EXTRA"] = "data/levels.pak" });
        Assert.That(result, Does.Contain("RESULT extra data/levels.pak First extra file"));
    }

    // An item removed between builds leaves the manifest and the boot config, and MSBuild's incremental clean deletes its staged file.
    [Test]
    public async Task BrowserBuildWithoutAPreviousVfsFileRemovesIt()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");
        string intermediateDirectory = GetBrowserIntermediateDirectory(consumerDirectory);
        string stagedPath = Path.Combine(intermediateDirectory, "pixely-browser-vfs", "data", "levels.pak");

        await BuildConsumerAsync(consumerDirectory, runtimeIdentifier: "browser-wasm", properties: ["BrowserContentConsumerExtraFile=data/levels.pak"]);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(stagedPath), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(intermediateDirectory, "staticwebassets.build.json")), Does.Contain("vfs:data/levels.pak"));
            Assert.That(File.ReadAllText(Path.Combine(intermediateDirectory, "dotnet.js")), Does.Contain("\"virtualPath\": \"data/levels.pak\""));
        });
        await BuildConsumerAsync(consumerDirectory, runtimeIdentifier: "browser-wasm");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(stagedPath), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(intermediateDirectory, "staticwebassets.build.json")), Does.Not.Contain("levels.pak"));
            Assert.That(File.ReadAllText(Path.Combine(intermediateDirectory, "dotnet.js")), Does.Not.Contain("levels.pak").And.Contain(BrowserContentArchiveBootEntry));
        });
    }

    [Test]
    public async Task BrowserBuildWithMissingVfsFileFailsWithPixely0010()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");

        string output = await BuildConsumerAsync(consumerDirectory, runtimeIdentifier: "browser-wasm", expectSuccess: false, properties: ["BrowserContentConsumerMissingFile=true"]);
        Assert.That(output, Does.Contain("error PIXELY0010").And.Contain("missing.txt"));
    }

    // The first failing check stops the target, so each build holds the target paths of one check, whose error names every path it rejects.
    // Content.pk3 duplicates the archive, content.pk3 too because MSBuild batches ignoring case; index.html is the default page's asset,
    // with no wwwroot file; the module initializer takes the mandatory fingerprint expression #[.{fingerprint}]!. A quote would break a
    // property function. Content.pk3/x.pak needs the archive as a folder, css/site.css the file css as a folder, and index.html/x.pak the
    // default page as a folder. _framework/y.pak and _Framework/x.pak differ in more than case: batching ignores case, so with one name
    // they would share a batch and only one spelling would reach the pattern.
    private static readonly TestCaseData[] InvalidTargetPathBuilds =
    [
        new TestCaseData(new[] { "/abs.pak", "C:/abs.pak", "../up.pak", "a/./b.pak", "a//b.pak", "a\\b.pak", "dir/", "*.pak", "a?.pak", "[ab].pak", "_content/x.pak", "_framework/y.pak", "_Framework/x.pak", "_framework", "Bob's.pak" },
            null, Array.Empty<string>(), false).SetArgDisplayNames("pattern"),
        new TestCaseData(new[] { "Content.pk3", "content.pk3" }, null, Array.Empty<string>(), false).SetArgDisplayNames("duplicate"),
        new TestCaseData(new[] { "index.html", "static.txt", "x.lib.module.js", "linked.txt" }, null, new[] { "static.txt", "x.lib.module.js" }, true).SetArgDisplayNames("static web asset"),
        new TestCaseData(new[] { "Content.pk3/x.pak", "css" }, new[] { "Content.pk3", "css" }, new[] { "css/site.css" }, false).SetArgDisplayNames("file needed as a folder"),
        new TestCaseData(new[] { "index.html/x.pak" }, new[] { "index.html" }, Array.Empty<string>(), false).SetArgDisplayNames("static web asset needed as a folder")
    ];

    [TestCaseSource(nameof(InvalidTargetPathBuilds))]
    public async Task BrowserBuildRejectsInvalidTargetPath(string[] targetPaths, string[]? rejectedPaths, string[] wwwrootFiles, bool linkedAsset)
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");
        foreach (string wwwrootFile in wwwrootFiles)
        {
            string wwwrootPath = Path.Combine(consumerDirectory, "wwwroot", wwwrootFile);
            Directory.CreateDirectory(Path.GetDirectoryName(wwwrootPath)!);
            File.WriteAllText(wwwrootPath, "// wwwroot fixture\n");
        }
        string itemsPath = Path.Combine(_testArtifactsDirectory, "extra-items.props");
        File.WriteAllText(itemsPath, $"""
            <Project>
              <ItemGroup>
            {string.Concat(targetPaths.Select(targetPath => $"    <PixelyBrowserVfsFile Include=\"Extras\\first.txt\" TargetPath=\"{SecurityElement.Escape(targetPath)}\" />\n"))}  </ItemGroup>
            </Project>
            """);

        try
        {
            string[] properties = linkedAsset ? [$"BrowserContentConsumerExtraItems={itemsPath}", "BrowserContentConsumerLinkedAsset=true"] : [$"BrowserContentConsumerExtraItems={itemsPath}"];
            string output = await BuildConsumerAsync(consumerDirectory, runtimeIdentifier: "browser-wasm", expectSuccess: false, properties: properties);
            string[] errors = output.Split('\n').Where(line => line.Contains("error PIXELY0011:", StringComparison.Ordinal)).Select(line => line[(line.IndexOf("error PIXELY0011:", StringComparison.Ordinal) + "error PIXELY0011:".Length)..]).Distinct().ToArray();
            Assert.That(errors, Has.Length.EqualTo(1), output);
            // Whole words, so that C:/abs.pak in the error does not stand in for /abs.pak.
            string[] words = errors[0].Split(' ').Select(word => word.TrimEnd(',', '.')).ToArray();
            Assert.Multiple(() =>
            {
                foreach (string rejectedPath in rejectedPaths ?? targetPaths)
                {
                    Assert.That(words, Does.Contain(rejectedPath), errors[0]);
                }
            });
        }
        finally
        {
            DeleteDirectory(Path.Combine(consumerDirectory, "wwwroot"));
        }
    }

    // DefineStaticWebAssets prefers a candidate's RelativePath over its parameters, so metadata the item brings along must not reach it.
    [Test]
    public async Task BrowserBuildIgnoresOtherMetadataOfAVfsFile()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");
        string intermediateDirectory = GetBrowserIntermediateDirectory(consumerDirectory);

        await BuildConsumerAsync(consumerDirectory, runtimeIdentifier: "browser-wasm", properties: ["BrowserContentConsumerExtraFile=data/levels.pak", "BrowserContentConsumerExtraMetadata=true"]);
        Assert.That(File.ReadAllText(Path.Combine(intermediateDirectory, "dotnet.js")), Does.Contain("\"virtualPath\": \"data/levels.pak\""));
    }

    // ComputeWasmVfs matches every static web asset by relative path, so the asset a referenced project serves below _content/ collides too.
    [Test]
    public async Task BrowserBuildRejectsTargetPathOfAReferencedProjectAsset()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentLibrary");

        string output = await BuildConsumerAsync(consumerDirectory, runtimeIdentifier: "browser-wasm", expectSuccess: false, properties: ["BrowserContentConsumerExtraFile=shared.txt", "BrowserContentConsumerLibraryAsset=true"]);
        Assert.That(output, Does.Contain("error PIXELY0011").And.Contain("TargetPath shared.txt is already the path"));
    }

    [Test]
    public async Task BrowserAppWithoutContentArchiveNamesTheArchiveInTheError()
    {
        RequireNode();
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");

        await PublishConsumerAsync(consumerDirectory, "browser-wasm", properties: ["BrowserContentConsumerArchive=false"]);
        string result = await RunBrowserBundleAsync(GetPublishedWwwroot(consumerDirectory), environment: null);
        Assert.That(result, Does.Contain("RESULT error").And.Contain("PixelyBrowserVfsFile with TargetPath Content.pk3").And.Contain("RESULT exit code 1"));
    }

    // On the desktop the item is inert and the loader finds the Content directory through the project tree.
    [Test]
    public async Task DesktopBuildIgnoresPixelyBrowserVfsFile()
    {
        string consumerDirectory = GetConsumerDirectory("BrowserContentConsumer");
        DeleteConsumerOutputs("BrowserContentConsumer");

        await BuildConsumerAsync(consumerDirectory, properties: ["BrowserContentConsumerMissingFile=true"]);
        string result = await RunConsumerDotnetAsync(consumerDirectory, Path.Combine(consumerDirectory, "bin", "Release", "net11.0", "BrowserContentConsumer.dll"));
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(Path.Combine(consumerDirectory, "obj", "Release", "net11.0", "pixely-browser-vfs")), Is.False);
            Assert.That(File.Exists(Path.Combine(consumerDirectory, "obj", "Release", "net11.0", "Content.pk3")), Is.False);
        });
        AssertBrowserContentLoaded(result);
    }

    private const string BrowserContentArchiveBootEntry = "\"virtualPath\": \"Content.pk3\"";
    private const string BrowserContentGeneratedShader = "shaders/.generated/package.vertex.wgsl";
    private const string BrowserContentHookMarker = "browser-content-hook.txt";

    private static void AssertBrowserContentLoaded(string result)
    {
        Assert.That(result, Does.Contain("RESULT greeting Hello from Content.pk3").And.Contain("RESULT shader True").And.Not.Contain("RESULT error"));
    }

    private static string GetBrowserIntermediateDirectory(string consumerDirectory)
    {
        return Path.Combine(consumerDirectory, "obj", "Release", "net11.0-browser", "browser-wasm");
    }

    private static string[] ReadArchiveEntries(string archivePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        return archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();
    }

    // One target of the browser build, as an IDE or another target would invoke it, with the restore the build commands run first.
    private async Task RunBrowserConsumerTargetAsync(string consumerDirectory, string target, params string[] properties)
    {
        WriteConsumerConfiguration(consumerDirectory);
        string projectPath = Directory.GetFiles(consumerDirectory, "*.csproj").Single();
        List<string> arguments = ["msbuild", projectPath, "-restore", $"-t:{target}", "-p:Configuration=Release", "-p:RuntimeIdentifier=browser-wasm", "-p:UseAppHost=false", $"-p:PixelyPackageVersion={_packageVersion}", "-nologo"];
        arguments.AddRange(properties.Select(property => $"-p:{property}"));
        await RunConsumerDotnetAsync(consumerDirectory, arguments.ToArray());
    }

    [Test]
    public async Task BrowserRuntimeIdentifierInTheProjectBodyFailsWithThePlainMessage()
    {
        string consumerDirectory = GetConsumerDirectory("HostedConsumer");
        DeleteConsumerOutputs("HostedConsumer");

        string output = await BuildConsumerAsync(consumerDirectory, expectSuccess: false, properties: ["HostedConsumerBodyRuntimeIdentifier=true"]);
        Assert.That(output, Does.Contain("error PIXELY0004").And.Contain("pass -r browser-wasm on the command line"));
    }

    // A project that lists net11.0-browser itself is not switched: its browser inner build is selected with -f, and its desktop
    // build stays a desktop build. The framework may carry its platform version; the SDK recognises both spellings as browser 1.0.
    [TestCase("net11.0-browser")]
    [TestCase("net11.0-browser1.0")]
    public async Task MultiTargetingConsumerPublishesTheBrowserFrameworkItNames(string browserFramework)
    {
        string consumerDirectory = GetConsumerDirectory("MultiTargetConsumer");
        DeleteConsumerOutputs("MultiTargetConsumer");

        // A semicolon in a command-line property value splits it; %3B is what MSBuild unescapes to one.
        string frameworks = $"MultiTargetConsumerFrameworks=net11.0%3B{browserFramework}";
        await PublishConsumerAsync(consumerDirectory, "browser-wasm", properties: [frameworks, $"TargetFramework={browserFramework}"]);
        string generatedFile = Path.Combine(consumerDirectory, "obj", "Release", browserFramework, "browser-wasm", "PixelyProgram.g.cs");
        string wwwroot = Path.Combine(consumerDirectory, "bin", "Release", browserFramework, "browser-wasm", "publish", "wwwroot");
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(generatedFile), Does.Contain("BrowserHost.RunAsync"));
            Assert.That(Directory.GetFiles(Path.Combine(wwwroot, "_framework"), "MultiTargetConsumer.*.wasm"), Has.Length.EqualTo(1));
        });
        if (HasNode())
        {
            string result = await RunBrowserBundleAsync(wwwroot, environment: null);
            Assert.That(result, Does.Contain("Configure ran for the browser.").And.Contain("OnException ran: Configure failed on purpose.").And.Contain("RESULT exit code 1"));
        }

        await BuildConsumerAsync(consumerDirectory, properties: [frameworks]);
        (int exitCode, string output) = await RunDotnetExpectingExitCodeAsync(consumerDirectory, Path.Combine(consumerDirectory, "bin", "Release", "net11.0", "MultiTargetConsumer.dll"));
        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(output, Does.Contain("Configure ran for the desktop.").And.Contain("OnException ran: Configure failed on purpose."));
            Assert.That(File.ReadAllText(Path.Combine(consumerDirectory, "obj", "Release", "net11.0", "PixelyProgram.g.cs")), Does.Contain("private static int Main()"));
        });
    }

    // Without -f every inner build gets the browser RID, and one that is not net11.0-browser cannot be a browser app; a list without
    // the browser framework has no inner build that could be. Built rather than published: publish refuses a multi-targeting
    // project without -f on its own (NETSDK1129).
    [TestCase(null)]
    [TestCase("net11.0")]
    public async Task MultiTargetingConsumerWithoutTheBrowserFrameworkSelectedFailsWithTheFrameworkMessage(string? frameworks)
    {
        string consumerDirectory = GetConsumerDirectory("MultiTargetConsumer");
        DeleteConsumerOutputs("MultiTargetConsumer");

        string[] properties = frameworks is null ? [] : [$"MultiTargetConsumerFrameworks={frameworks}"];
        string output = await BuildConsumerAsync(consumerDirectory, "browser-wasm", expectSuccess: false, properties: properties);
        Assert.That(output, Does.Contain("error PIXELY0007").And.Contain("dotnet publish -f net11.0-browser -r browser-wasm"));
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
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.vertex.wgsl")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "package.fragment.wgsl")), Is.True);
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
        return RunConsumerBuildCommandAsync("build", consumerDirectory, runtimeIdentifier, defineConstants, expectSuccess, properties, noBuild: false);
    }

    private Task<string> PublishConsumerAsync(string consumerDirectory, string runtimeIdentifier, string? defineConstants = null, string[]? properties = null, bool noBuild = false)
    {
        return RunConsumerBuildCommandAsync("publish", consumerDirectory, runtimeIdentifier, defineConstants, expectSuccess: true, properties, noBuild);
    }

    private static string GetPublishedWwwroot(string consumerDirectory)
    {
        return Path.Combine(consumerDirectory, "bin", "Release", "net11.0-browser", "browser-wasm", "publish", "wwwroot");
    }

    private async Task<string> RunConsumerBuildCommandAsync(string command, string consumerDirectory, string? runtimeIdentifier, string? defineConstants, bool expectSuccess, string[]? properties, bool noBuild)
    {
        string[] projectPaths = Directory.GetFiles(consumerDirectory, "*.csproj");
        Assert.That(projectPaths, Has.Length.EqualTo(1), $"Expected one consumer project in {consumerDirectory}.");
        string projectPath = projectPaths[0];
        string projectContents = File.ReadAllText(projectPath);
        // Fixtures consume the package, never the repository sources.
        Assert.That(projectContents, Does.Not.Contain("src\\").And.Not.Contain("src/"));
        WriteConsumerConfiguration(consumerDirectory);
        // The implicit restore saves a separate restore process per command, and its warnings (NU*) appear in the same output.
        List<string> arguments = [command, projectPath, "--configuration", "Release", $"--property:PixelyPackageVersion={_packageVersion}", "--nologo"];
        if (noBuild)
        {
            arguments.Add("--no-build");
        }
        if (runtimeIdentifier is not null)
        {
            arguments.Add($"--property:RuntimeIdentifier={runtimeIdentifier}");
            arguments.Add("--property:UseAppHost=false");
        }
        if (defineConstants is not null)
        {
            arguments.Add($"--property:DefineConstants={defineConstants}");
        }
        foreach (string property in properties ?? [])
        {
            arguments.Add($"--property:{property}");
        }

        if (!expectSuccess)
        {
            (int exitCode, string failedOutput) = await RunDotnetExpectingExitCodeAsync(consumerDirectory, ConsumerEnvironment, arguments.ToArray());
            Assert.That(exitCode, Is.Not.EqualTo(0), failedOutput);
            return failedOutput;
        }

        string output = await RunConsumerDotnetAsync(consumerDirectory, arguments.ToArray());
        // MSB4011 would mean the SDK and buildTransitive/Pixely.targets both imported the version props.
        Assert.That(output, Does.Not.Contain("Downloading Slang").And.Not.Contain("MSB4011"));
        return output;
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

    // requestAnimationFrame does not exist under node; a timer stands in for it. The script mirrors the package's main.js without a canvas,
    // and hands the fixture its run-time variant as environment variables of the managed app.
    private static async Task<string> RunBrowserBundleAsync(string wwwroot, Dictionary<string, string>? environment)
    {
        string script = Path.Combine(wwwroot, "run-under-node.mjs");
        string variables = string.Concat((environment ?? []).Select(pair => $".withEnvironmentVariable({JsonSerializer.Serialize(pair.Key)}, {JsonSerializer.Serialize(pair.Value)})"));
        File.WriteAllText(script, $$"""
            import { dotnet } from './_framework/dotnet.js';
            globalThis.requestAnimationFrame = (callback) => setTimeout(callback, 5);
            try {
                const exitCode = await dotnet{{variables}}.runMain();
                console.log(`RESULT exit code ${exitCode}`);
            } catch (error) {
                console.log(`RESULT rejected ${error}`);
            }
            """);
        (int exitCode, string output) = await RunProcessExpectingExitCodeAsync("node", wwwroot, null, script);
        Assert.That(exitCode, Is.EqualTo(0), output);
        return output;
    }

    // Runs the bundle as RunBrowserBundleAsync does, with the loader installed as main.js installs it, so the publish's boot config turns
    // it on and the runtime consumes the decompressed responses. Under node the runtime's URLs are file paths, so the loader gets
    // the same file from a local HTTP server; RESULT brotli counts the .br files that server sent.
    private static async Task<string> RunBrowserBundleWithBrotliLoaderAsync(string wwwroot)
    {
        string script = Path.Combine(wwwroot, "run-brotli-bundle.mjs");
        File.WriteAllText(script, """
            import { dotnet } from './_framework/dotnet.js';
            import * as host from './pixely-host.js';
            import http from 'node:http';
            import fs from 'node:fs';
            import path from 'node:path';

            let brotliFiles = 0;
            const server = http.createServer((request, response) => {
                const file = path.resolve('_framework', path.basename(new URL(request.url, 'http://localhost').pathname));
                if (!fs.existsSync(file)) {
                    response.writeHead(404);
                    response.end();
                    return;
                }
                brotliFiles += file.endsWith('.br') ? 1 : 0;
                response.writeHead(200, { 'Content-Type': 'application/octet-stream' });
                response.end(fs.readFileSync(file));
            });
            await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
            const origin = `http://127.0.0.1:${server.address().port}`;
            globalThis.requestAnimationFrame = (callback) => setTimeout(callback, 5);
            const loader = (type, name, defaultUri, integrity) => host.loadCompressedResource(type, name, `${origin}/_framework/${path.basename(defaultUri)}`, integrity);
            try {
                const exitCode = await dotnet.withModuleConfig({ onConfigLoaded: host.configureCompressedResources }).withResourceLoader(loader).runMain();
                console.log(`RESULT exit code ${exitCode}`);
            } catch (error) {
                console.log(`RESULT rejected ${error}`);
            }
            console.log(`RESULT brotli ${brotliFiles}`);
            server.close();
            """);
        (int exitCode, string output) = await RunProcessExpectingExitCodeAsync("node", wwwroot, null, script);
        Assert.That(exitCode, Is.EqualTo(0), output);
        return output;
    }

    // Calls loadCompressedResource from the published pixely-host.js under node against a local HTTP server, as dotnet.js would in a page.
    private static async Task<string> RunBrotliLoaderAsync(string wwwroot, string assemblyName)
    {
        string script = Path.Combine(wwwroot, "run-brotli-loader.mjs");
        File.WriteAllText(script, """
            // Serves this publish over HTTP and calls loadCompressedResource from pixely-host.js directly, as dotnet.js would. The first path
            // segment picks how the server answers a .br request; the original files are always served. Each CASE line says whether the
            // response body equals the original file, and its content type tells a decoded file (as the loader labels it) from the original
            // (as the server does).
            import http from 'node:http';
            import fs from 'node:fs';
            import path from 'node:path';
            import crypto from 'node:crypto';

            const framework = path.resolve('_framework');
            const files = fs.readdirSync(framework);
            const runtime = files.find(file => /^dotnet\.native\.[^.]+\.wasm$/.test(file));
            const assembly = files.find(file => new RegExp(`^${process.argv[2]}\\.[^.]+\\.wasm$`).test(file));
            const server = http.createServer((request, response) => {
                const [, mode, ...segments] = new URL(request.url, 'http://localhost').pathname.split('/');
                const file = path.resolve(segments.join('/'));
                const brotli = file.endsWith('.br');
                if (mode === 'decoder') {
                    response.writeHead(200, { 'Content-Type': 'text/javascript' });
                    response.end('export function BrotliDecode() { return new Int8Array(0); }');
                } else if (brotli && mode === 'reset') {
                    request.socket.destroy();
                } else if (brotli && mode === 'truncated') {
                    const compressed = fs.readFileSync(file);
                    response.writeHead(200, { 'Content-Type': 'application/octet-stream' });
                    response.end(compressed.subarray(0, compressed.length / 2));
                } else if (brotli && mode === 'missing') {
                    response.writeHead(404);
                    response.end();
                } else if (brotli && mode === 'page') {
                    response.writeHead(200, { 'Content-Type': 'Text/HTML; charset=utf-8' });
                    response.end(fs.readFileSync('index.html'));
                } else {
                    // As GitHub Pages does, a .br file is opaque; "encoded" labels it the way a server that negotiates Brotli would.
                    const headers = { 'Content-Type': brotli ? 'application/octet-stream' : 'application/wasm' };
                    if (brotli && mode === 'encoded') {
                        headers['Content-Encoding'] = 'Br';
                    }
                    response.writeHead(200, headers);
                    response.end(fs.readFileSync(file));
                }
            });
            await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
            const origin = `http://127.0.0.1:${server.address().port}`;
            const integrityOf = file => `sha256-${crypto.createHash('sha256').update(fs.readFileSync(path.join(framework, file))).digest('base64')}`;

            async function check(name, host, mode, type, file, integrity = '') {
                try {
                    const response = await host.loadCompressedResource(type, file, `${origin}/${mode}/_framework/${file}`, integrity);
                    const same = Buffer.from(await response.arrayBuffer()).equals(fs.readFileSync(path.join(framework, file)));
                    console.log(`CASE ${name} ${same ? 'same' : 'different'} ${response.headers.get('Content-Type')}`);
                } catch (error) {
                    console.log(`CASE ${name} rejected ${error.message}`);
                }
            }

            const leftToRuntime = (host, type, uri) => host.loadCompressedResource(type, path.basename(uri), uri, '') === undefined ? 'runtime' : 'loader';
            // Each import URL is its own module instance, so each remembers its own answer to whether DecompressionStream decodes Brotli.
            // An instance stays off until the boot config of a publish with PixelyBrowserBrotli turns it on.
            const streams = await import('./pixely-host.js?streams');
            console.log(`CASE unconfigured ${leftToRuntime(streams, 'assembly', `${origin}/served/_framework/${assembly}`)}`);
            const brotliConfig = { extensions: { pixely: { brotli: true } } };
            streams.configureCompressedResources(brotliConfig);
            await check('streams-runtime', streams, 'served', 'dotnetwasm', runtime);
            await check('streams-assembly', streams, 'served', 'assembly', assembly);
            await check('encoded-runtime', streams, 'encoded', 'dotnetwasm', runtime);
            await check('missing-runtime', streams, 'missing', 'dotnetwasm', runtime, integrityOf(runtime));
            await check('missing-runtime-other-integrity', streams, 'missing', 'dotnetwasm', runtime, integrityOf(assembly));
            await check('page-assembly', streams, 'page', 'assembly', assembly);
            await check('reset-assembly', streams, 'reset', 'assembly', assembly);
            // Half a .br file decodes partway; the rest of the stream comes from the original.
            await check('truncated-runtime', streams, 'truncated', 'dotnetwasm', runtime);
            await check('truncated-assembly', streams, 'truncated', 'assembly', assembly);
            console.log(`CASE pdb ${leftToRuntime(streams, 'pdb', `${origin}/served/_framework/x.pdb`)}`);
            // Without a document or location a relative path cannot be resolved.
            console.log(`CASE relative ${leftToRuntime(streams, 'assembly', `_framework/${assembly}`)}`);
            // A page on another origin than the file cannot read its Content-Encoding.
            globalThis.location = { origin: 'https://elsewhere.example', href: 'https://elsewhere.example/' };
            console.log(`CASE cross-origin ${leftToRuntime(streams, 'assembly', `${origin}/served/_framework/${assembly}`)}`);
            delete globalThis.location;
            streams.configureCompressedResources({ ...brotliConfig, disableIntegrityCheck: true });
            await check('missing-runtime-integrity-disabled', streams, 'missing', 'dotnetwasm', runtime, integrityOf(assembly));

            // A DecompressionStream without Brotli, as in Chrome, makes the second instance load google/brotli's decoder.
            const decompressionStream = globalThis.DecompressionStream;
            globalThis.DecompressionStream = class extends decompressionStream {
                constructor(format) {
                    if (format === 'brotli') {
                        throw new TypeError('Unsupported compression format');
                    }
                    super(format);
                }
            };
            // The decoder comes from the CDN, which the test does not reach. "offline" rejects that request and "tampered" answers it with
            // other bytes, which fetch rejects by the integrity value; either way the loader falls back to the original and leaves later
            // files to the runtime. "online" answers with a stand-in on node's Brotli decoder, skipping the integrity check that only
            // google/brotli's file passes. An instance remembers a failed decoder, so each answer gets its own.
            const decoderInstance = async query => {
                const host = await import(`./pixely-host.js?${query}`);
                host.configureCompressedResources(brotliConfig);
                return host;
            };
            let cdn = 'offline';
            const realFetch = globalThis.fetch;
            const standIn = "import zlib from 'node:zlib'; export function BrotliDecode(bytes) { return new Int8Array(zlib.brotliDecompressSync(bytes)); }";
            globalThis.fetch = (input, init) => {
                if (!String(input).startsWith('https://cdn.jsdelivr.net/')) {
                    return realFetch(input, init);
                }
                if (cdn === 'online') {
                    return Promise.resolve(new Response(standIn, { headers: { 'Content-Type': 'text/javascript' } }));
                }
                return cdn === 'offline' ? Promise.reject(new TypeError('offline')) : realFetch(`${origin}/decoder/tampered.js`, init);
            };
            const offline = await decoderInstance('offline');
            await check('decoder-offline-assembly', offline, 'served', 'assembly', assembly);
            console.log(`CASE decoder-offline-next ${leftToRuntime(offline, 'assembly', `${origin}/served/_framework/${assembly}`)}`);
            cdn = 'tampered';
            await check('decoder-tampered-assembly', await decoderInstance('tampered'), 'served', 'assembly', assembly);
            cdn = 'online';
            const decoder = await decoderInstance('decoder');
            await check('decoder-runtime', decoder, 'served', 'dotnetwasm', runtime, integrityOf(runtime));
            // A decoded file that matches its integrity value keeps the loader's content type.
            await check('decoder-assembly', decoder, 'served', 'assembly', assembly, integrityOf(assembly));
            // A decoded file is checked against the integrity value, so another file's hash falls back to the original, which fails it too.
            await check('decoder-assembly-other-integrity', decoder, 'served', 'assembly', assembly, integrityOf(runtime));
            globalThis.fetch = realFetch;
            globalThis.DecompressionStream = decompressionStream;
            server.close();
            """);
        (int exitCode, string output) = await RunProcessExpectingExitCodeAsync("node", wwwroot, null, script, assemblyName);
        Assert.That(exitCode, Is.EqualTo(0), output);
        return output;
    }

    private static void AssertBrotliLoaderOutcome(string output)
    {
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("CASE streams-runtime same application/wasm"));
            Assert.That(output, Does.Contain("CASE streams-assembly same application/octet-stream"));
            Assert.That(output, Does.Contain("CASE encoded-runtime same application/wasm"));
            Assert.That(output, Does.Contain("CASE missing-runtime same application/wasm"));
            // The fallback fetch carries the runtime's integrity value, so another file's hash fails it.
            Assert.That(output, Does.Contain("CASE missing-runtime-other-integrity rejected"));
            Assert.That(output, Does.Contain("CASE page-assembly same application/wasm"));
            Assert.That(output, Does.Contain("CASE reset-assembly same application/wasm"));
            Assert.That(output, Does.Contain("CASE truncated-runtime same application/wasm"));
            Assert.That(output, Does.Contain("CASE truncated-assembly same application/octet-stream"));
            Assert.That(output, Does.Contain("CASE unconfigured runtime"));
            Assert.That(output, Does.Contain("CASE pdb runtime"));
            Assert.That(output, Does.Contain("CASE relative runtime"));
            Assert.That(output, Does.Contain("CASE cross-origin runtime"));
            Assert.That(output, Does.Contain("CASE missing-runtime-integrity-disabled same application/wasm"));
            Assert.That(output, Does.Contain("CASE decoder-offline-assembly same application/wasm"));
            Assert.That(output, Does.Contain("CASE decoder-offline-next runtime"));
            Assert.That(output, Does.Contain("CASE decoder-tampered-assembly same application/wasm"));
            Assert.That(output, Does.Contain("CASE decoder-assembly-other-integrity rejected"));
            Assert.That(output, Does.Contain("CASE decoder-runtime same application/wasm"));
            Assert.That(output, Does.Contain("CASE decoder-assembly same application/octet-stream"));
        });
    }

    // The workload is per SDK band, so the fixture is asked whether the toolchain that would relink it is installed: the wasm-tools
    // manifest sets WasmNativeWorkloadAvailable for the target framework. A workload listing can name wasm-tools for another SDK on
    // the machine, and the WebAssembly pack alone then ignores the native reference instead of failing.
    private async Task RequireWasmToolsAsync(string consumerDirectory)
    {
        WriteConsumerConfiguration(consumerDirectory);
        string projectPath = Directory.GetFiles(consumerDirectory, "*.csproj").Single();
        (int exitCode, string output) = await RunDotnetExpectingExitCodeAsync(consumerDirectory, ConsumerEnvironment, "msbuild", projectPath, "-p:RuntimeIdentifier=browser-wasm", "-getProperty:WasmNativeWorkloadAvailable");
        if (exitCode != 0 || output.Trim() != "true")
        {
            Assert.Ignore("The wasm-tools workload is not installed for this SDK; the runtime cannot be relinked.");
        }
    }

    private static bool HasNode()
    {
        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return directories.Any(directory => File.Exists(Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node")));
    }

    private static void RequireNode()
    {
        if (!HasNode())
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
        // A StreamReader drops the byte order mark, which XDocument.Parse rejects.
        using StreamReader reader = new(new MemoryStream(ReadPackageEntryBytes(package, entryName)));
        return reader.ReadToEnd();
    }

    private static byte[] ReadPackageEntryBytes(ZipArchive package, string entryName)
    {
        ZipArchiveEntry entry = package.GetEntry(entryName)
            ?? throw new InvalidOperationException($"{entryName} is missing from the package.");
        using Stream stream = entry.Open();
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);
        return bytes.ToArray();
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
        // written by HostedConsumerPublishesABrowserBundle and BrowserBuildRejectsInvalidTargetPath; no fixture has a wwwroot of its own
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
