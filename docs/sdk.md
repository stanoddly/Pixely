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

The SDK resolver downloads the package from the sources in the `NuGet.Config` found from the project directory upwards into the global packages folder (`NUGET_PACKAGES`). It does not read `dotnet restore --source`, `--configfile` or `RestorePackagesPath`.

## What the SDK adds

- A `PackageReference` to `Pixely` pinned to the SDK's own version, so the library, the source generator and the SDK never drift apart.
- A `PackageReference` to `SlangDxcBundle.Toolchain` with `PrivateAssets="all"`, pinned to the version the shader targets were built against. The toolchain is a build-host tool of the project being built; a library built on Pixely does not carry it in its nuspec. A consumer of such a library that compiles shaders itself adds the `<Sdk>` line and gets the toolchain from it.
- The shader compilation targets (`SdlangShader`, see [Shaders](shaders.md)).
- The generated entry point for projects that set `PixelyHosting` (see [Hosting](hosting.md)).
- `InterceptorsNamespaces` for the dependency-injection generator and `PixelyDocsDirectory`, the packaged copy of `docs/`.

## Central package management

With `ManagePackageVersionsCentrally=true` the SDK declares its two `PackageVersion` items itself and removes any `Pixely` or `SlangDxcBundle.Toolchain` entries a `Directory.Packages.props` already lists; the SDK owns those pins. Nothing else in the file changes.

## Migrating from PackageReference

Replace

```xml
<PackageReference Include="Pixely" Version="0.0.N" />
```

with the `<Sdk>` line. A project that keeps the `PackageReference` fails its build with a message that names the exact `<Sdk>` line to use. The check runs at build, not at restore, because a package's `build/` folder is not evaluated while the restore graph is built.

## Package layout

```
lib/net11.0/                                   the library
analyzers/dotnet/cs/                           the dependency-injection generator
Sdk/Sdk.props, Sdk/Sdk.targets                 imported by the SDK resolver
Sdk/Pixely.AfterSdk.targets                    shader and hosting targets, imported once the base SDK has set its properties
Sdk/Pixely.Hosting.targets                     the entry point generator
Sdk/Pixely.Version.props                       the package version, generated at pack time
build/Pixely.targets                           the PackageReference guard
tools/net11.0/any/                             the shader compilation task and its props and targets
docs/                                          this documentation
```

An additive SDK's `Sdk.targets` is imported after `Microsoft.NET.Sdk.targets`. When that is not the case, the file registers `Pixely.AfterSdk.targets` on `AfterMicrosoftNETSdkTargets`, which the base SDK imports as its last line; repository tutorials, which reference Pixely as a project, use the same hook from `tutorials/Directory.Build.targets`.
