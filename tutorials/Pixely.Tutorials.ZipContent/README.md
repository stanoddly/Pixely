# ZIP content

This tutorial compiles a Slang shader and uses two distributions for the same virtual content path:

- `dotnet build` copies `Content` beside the executable.
- `dotnet publish` creates `Content.pk3` beside the executable.
- A browser build or publish (`-r browser-wasm`) puts `Content.pk3` into the browser's file system, at `/`.

Run the loose-directory build from the repository root:

```bash
dotnet run --project tutorials/Pixely.Tutorials.ZipContent
```

Publish and run the ZIP distribution:

```bash
dotnet publish tutorials/Pixely.Tutorials.ZipContent -o /tmp/pixely-zip-content
/tmp/pixely-zip-content/Pixely.Tutorials.ZipContent
```

`AddZipPattern` and `AddDirectoryPattern` both resolve beside the application. The directory source is added last, so it overrides the archive when both contain the same content path.

The build copy records its destinations in `@(FileWrites)`, allowing MSBuild's incremental cleanup to remove outputs whose source files were deleted. Publishing packages that tracked build content under `$(IntermediateOutputPath)` and registers `Content.pk3` through `@(ResolvedFileToPublish)`. The SDK then owns the final copy into `$(PublishDir)` and its incremental cleanup.

In the browser `AppContext.BaseDirectory` is `/`, so `AddZipPattern` finds the archive there. The browser branch adds it as a `PixelyBrowserVfsFile`. It zips the project's `Content` tree, not the build copy, because the browser assets are defined before `CopyFilesToOutputDirectory`. It zips on every build, so a deleted file leaves the archive. [Content distribution](../../docs/content-distribution.md#browser) describes the item.

This packages every file in the build content tree, including the Slang source. Filter the build content items when a release must exclude source files.
