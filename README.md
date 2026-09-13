# Pixely

Pixely is a personal, highly experimental .NET game-development toolkit.

It is public for practical reasons: easier CI integration across my projects,
easier sharing with peers, and less friction around GitHub's open-source
tooling. The codebase is not presented as a stable engine or a supported
framework; APIs, architecture, and package boundaries may change whenever that
helps the projects it serves.

The project collects ideas and patterns that grew out of years of game and tool
prototyping: rendering helpers, shader compilation, input/windowing,
immediate-mode UI experiments, audio, content loading, events, dependency
injection, and architecture/testing utilities.

## Status

Experimental. Expect breaking changes.

## Goals

- Keep small game prototypes fast to build.
- Prefer simple, direct APIs over general engine abstractions.
- Reuse proven pieces across my own projects.
- Preserve useful experiments in one place.

## Non-goals

- Stable public API compatibility.
- Complete engine documentation.
- Broad platform/support guarantees.
- General-purpose community support.

## Development

Pixely is developed by stanoddly. Since November 2025, most changes have been
made through AI pair-programming: botoddly contributes the implementation work,
and stanoddly reviews, directs, and merges the changes through PRs. Earlier
parts of the project were mostly written manually.

## Documentation

`docs/` is shipped inside the NuGet package. In a project that references
Pixely, `dotnet msbuild -getProperty:PixelyDocsDirectory` prints where the
package version's copy lives.

## License

MIT.
