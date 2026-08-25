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

`UseDefaultContent()` loads `Content.pk3` beside the application when present, then adds a loose `Content` directory so it takes precedence over the archive. When neither exists beside the application, it resolves the `Content` directory from the project tree for development.

## Build and publish policy

Content producers do not choose how a consumer distributes their outputs. In particular, `Pixely.SdlangCompiler.SdlangCompileTask` compiles `@(SdlangShader)` and exposes the physical generated files as `@(SdlangShaderOutput)`. The consuming project decides whether those files are copied, embedded, or packaged.

Files generated during a build do not exist when MSBuild evaluates the project. A content pipeline that must include generated files therefore enumerates its content tree inside an execution-time target, after the producer has run. An evaluation-time item glob is not sufficient for a clean build.

Use one of these policies:

- **Loose directory:** copy the content tree to `$(OutDir)` for direct loading and iteration.
- **Embedded resources:** add generated files to `@(EmbeddedResource)` before `AssignTargetPaths`; suitable for content owned by a library.
- **ZIP archive:** package the tracked build content and register the archive through `@(ResolvedFileToPublish)`; suitable for an application-owned content bundle.

The policies are independent of file type. Generated shaders motivate the execution-time integration, but the same content tree can contain textures, fonts, audio, and data files.

## Tutorials

- [Embed generated shaders in an assembly](../tutorials/Pixely.Tutorials.EmbeddedContent/README.md)
- [Publish content in a ZIP archive](../tutorials/Pixely.Tutorials.ZipContent/README.md)

The embedded tutorial follows the policy used by `Pixely.Pencuil`. The ZIP tutorial follows the policy used by Nerudova: normal builds use a loose `Content` directory, while published builds use `Content.pk3`.

## Publish without building

`dotnet publish --no-build` does not refresh generated content. Run a normal build first. An embedded-content target should also test `$(NoBuild)` so publishing an existing assembly does not invoke its content producers. A ZIP target packages the tracked content from the preceding build.
