# Content distribution

Pixely loads runtime content through a read-only `ContentSource`. Content paths such as `shaders/terrain` do not identify a physical distribution format. A project can provide the same path from a directory, a ZIP archive, an assembly resource, or a composition of those sources. Saved games and other writable application data use normal file access rather than `ContentSource`.

## Runtime sources

`PixelyAppBuilder.ConfigureContent` configures the application-owned `ContentSourceBuilder`. Multiple calls append to the same builder in call order. The completed `ContentSource` is registered when the application service provider is built.

- `AddDirectory` adds one physical directory.
- `AddProjectDirectory` resolves a directory from the application output or project tree.
- `AddDirectoryPattern` adds matching directories beside the application.
- `AddZip` adds one ZIP archive.
- `AddZipPattern` adds matching ZIP files beside the application.
- `AddSource` adds an existing `ContentSource`, including one created by `EmbeddedContentSource.Create`.
- `WithCache` caches the composed source when the application service provider is built.

```csharp
PixelyAppBuilder appBuilder = new();
appBuilder.ConfigureContent(contentSourceBuilder => contentSourceBuilder
    .AddZipPattern("assets-*.pak")
    .AddDirectoryPattern("assets-*")
    .WithCache());
```

Patterns are resolved relative to `AppContext.BaseDirectory`. Matching directory names are sorted ordinally. When multiple sources contain the same content path, the source added last wins.

`UseDefaultContent()` loads `Content.pk3` beside the application when present, then adds a loose `Content` directory so it takes precedence over the archive. When neither exists beside the application, it resolves the `Content` directory from the project tree for development. In the browser there is no project tree, so it throws instead and names the `PixelyBrowserVfsFile` item (see [Browser](#browser)).

## Build and publish policy

Content producers do not choose how a consumer distributes their outputs. In particular, `Pixely.SdlangCompiler.SdlangCompileTask` compiles `@(SdlangShader)` and exposes the physical generated files as `@(SdlangShaderOutput)`. The consuming project decides whether those files are copied, embedded, or packaged.

Files generated during a build do not exist when MSBuild evaluates the project. A content pipeline that must include generated files therefore enumerates its content tree inside an execution-time target, after the producer has run. An evaluation-time item glob is not sufficient for a clean build.

Use one of these policies:

- **Loose directory:** copy the content tree to `$(OutDir)` for direct loading and iteration.
- **Embedded resources:** add generated files to `@(EmbeddedResource)` before `AssignTargetPaths`; suitable for content owned by a library.
- **ZIP archive:** package the tracked build content and register the archive through `@(ResolvedFileToPublish)`; suitable for an application-owned content bundle.
- **Browser file system:** package the content tree and add the archive as a `PixelyBrowserVfsFile`; the browser counterpart of the ZIP archive (see [Browser](#browser)).

The policies are independent of file type. Generated shaders motivate the execution-time integration, but the same content tree can contain textures, fonts, audio, and data files.

## Tutorials

- [Embed generated shaders in an assembly](../tutorials/Pixely.Tutorials.EmbeddedContent/README.md)
- [Publish content in a ZIP archive](../tutorials/Pixely.Tutorials.ZipContent/README.md)

The embedded tutorial follows the policy used by `Pixely.Ui`. The ZIP tutorial follows the policy used by Nerudova: normal builds use a loose `Content` directory, published builds use `Content.pk3`, and browser builds use `Content.pk3` in the browser's file system.

## Browser

A browser app has no directory beside the executable. Its file system is in memory, and `AppContext.BaseDirectory` is `/`. A `PixelyBrowserVfsFile` item puts a file into it before `Main` runs, at its `TargetPath` below `/`. `UseDefaultContent()` loads an item with `TargetPath` `Content.pk3`. `AddZipPattern` searches `/` as it searches beside a desktop executable, and `AddZip` takes the full path, such as `/levels.pak`.

```xml
<PropertyGroup>
    <PixelyBrowserVfsFileDependsOn>$(PixelyBrowserVfsFileDependsOn);PackageBrowserContent</PixelyBrowserVfsFileDependsOn>
</PropertyGroup>

<Target Name="PackageBrowserContent">
    <ZipDirectory SourceDirectory="$(ContentSourceDirectory)" DestinationFile="$(IntermediateOutputPath)Content.pk3" Overwrite="true" />
    <ItemGroup>
        <PixelyBrowserVfsFile Include="$(IntermediateOutputPath)Content.pk3" />
        <FileWrites Include="$(IntermediateOutputPath)Content.pk3" />
    </ItemGroup>
</Target>
```

- The targets in `PixelyBrowserVfsFileDependsOn` run after `Compile`, so generated shaders exist, and before the static web assets are resolved, which is before `CopyFilesToOutputDirectory`. Zip the project's content tree, not a copy in `$(OutDir)`.
- A target that produces the file adds the item itself, because `$(IntermediateOutputPath)` is not set in the project body. A file that already exists in the source tree can be an item in the project body.
- Zip on every build. `Inputs` and `Outputs` cannot see a deleted source file, so an incremental archive keeps it.
- `TargetPath` defaults to the file name. It is a relative path of segments made of `A-Z`, `a-z`, `0-9`, `.`, `_` and `-`, separated by `/`, without `.` or `..` segments and not below `_framework/` or `_content/`. Two items cannot share a `TargetPath`, and neither can a `TargetPath` and a static web asset defined before this step: a `wwwroot` file, a linked asset or a file of the default page, such as `index.html`. An asset that a later target adds fails the build later, with the WebAssembly SDK's message. Both checks ignore case, although the browser's file system does not, because MSBuild batches item metadata ignoring case. Each of these is error PIXELY0011.
- An item whose file does not exist when the browser assets are defined is error PIXELY0010.
- The item applies to browser builds only. A desktop build ignores it, and neither a design-time build nor a publish with `--no-build` runs the producing targets.
- The browser downloads the whole file into memory before `Main` starts, so its size adds to startup time and memory use. A publish serves it as it is, without a compressed copy.

## Publish without building

`dotnet publish --no-build` does not refresh generated content. Run a normal build first. An embedded-content target should also test `$(NoBuild)` so publishing an existing assembly does not invoke its content producers. A ZIP target packages the tracked content from the preceding build.
