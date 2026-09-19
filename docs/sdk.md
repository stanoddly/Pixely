# Pixely SDK

Pixely ships as an MSBuild project SDK in the same package as the library. A project adds one line and one version:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Pixely" Version="0.0.N" />

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
  </PropertyGroup>
</Project>
```

The `Version` attribute of `<Sdk>` takes a literal, not a property. To keep the version in one place for several projects, put it in `global.json` and omit the attribute:

```json
{
  "msbuild-sdks": {
    "Pixely": "0.0.N"
  }
}
```

The `<Sdk>` element must follow `Microsoft.NET.Sdk`; in the attribute form that is `Sdk="Microsoft.NET.Sdk;Pixely/0.0.N"`. Pixely's props read the restored packages' props and a `Directory.Packages.props`, which `Microsoft.NET.Sdk` imports first; the other order fails the build with PIXELY0003.

The SDK resolver downloads the package from the sources in the `NuGet.Config` found from the project directory upwards into the global packages folder (`NUGET_PACKAGES`). It does not read `dotnet restore --source`, `--configfile` or `RestorePackagesPath`.

## One version per build

MSBuild resolves a named SDK once per build and keeps the first version it resolves. When projects in one build pin different Pixely versions, the others silently get that first version; the only signal is warning MSB4240, which `TreatWarningsAsErrors` does not promote. Pin one version per build, for example through `global.json`.

## What the SDK adds

- A `PackageReference` to `Pixely` pinned to the SDK's own version, so the library, the source generator and the SDK never drift apart.
- A `PackageReference` to `SlangDxcBundle.Toolchain` with `PrivateAssets="all"`, pinned to the version the shader targets were built against. The toolchain is a build-host tool of the project being built; a library built on Pixely does not carry it in its nuspec. A consumer of such a library adds the `<Sdk>` line, as every project reaching Pixely must, and gets the toolchain from it.
- The shader compilation targets (`SdlangShader`, see [Shaders](shaders.md)).
- The generated entry point for projects that set `PixelyHosting` (see [Hosting](hosting.md)).
- `InterceptorsNamespaces` for the dependency-injection generator and `PixelyDocsDirectory`, the packaged copy of `docs/`.

A library packed on Pixely X depends on exactly `[X]`. A consumer pinned lower fails restore with NU1605; a consumer pinned higher gets its own version with warning NU1608, which `TreatWarningsAsErrors` promotes.

## Central package management

The SDK's two references are implicit and carry their own versions, as the .NET SDK's own implicit references do, so central package management needs no `PackageVersion` for them. The SDK removes any `Pixely` or `SlangDxcBundle.Toolchain` entries a `Directory.Packages.props` lists; the SDK owns those pins. Nothing else in the file changes.

The SDK also removes a `PackageReference` to either package that the project declares itself, with or without a version, and warns PIXELY0001 at build. The SDK's pin always wins.

## Every project needs the line

The `<Sdk>` line belongs in every project that references Pixely, directly or through a project or package reference. A project without it would get the library through the reference graph but not the source generator, and dependency-injection registrations would fail at run time instead of at build time. The package therefore fails the build of such a project with error PIXELY0002, which names the `<Sdk>` line and the `global.json` alternative. The check runs at build, not at restore, because a package's `buildTransitive/` folder is not evaluated while the restore graph is built.

## Package layout

```
lib/net11.0/                                   the library
analyzers/dotnet/cs/                           the dependency-injection generator
Sdk/Sdk.props, Sdk/Sdk.targets                 imported by the SDK resolver
Sdk/Pixely.AfterSdk.targets                    shader and hosting targets, imported once the base SDK has set its properties
Sdk/Pixely.Hosting.targets                     the entry point generator
Sdk/Pixely.Version.props                       the package version, generated at pack time
buildTransitive/Pixely.targets                 fails a project that reaches Pixely without the SDK
tools/net11.0/any/                             the shader compilation task and its props and targets
docs/                                          this documentation
```

An additive SDK's `Sdk.targets` is imported after `Microsoft.NET.Sdk.targets`, so `Pixely.AfterSdk.targets` sees the base SDK's properties. Repository tutorials, which reference Pixely as a project, register the hosting targets on `AfterMicrosoftNETSdkTargets`, which the base SDK imports as its last line, from `tutorials/Directory.Build.targets`.
