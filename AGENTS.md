# AGENTS.md

## Documentation

- `docs/class-registration.md` - ServiceCollection/ServiceProvider API: registration overloads, source generator requirements, lifecycle, aliases, multi-registration, parent/child provider callback merging and scoped lifecycles
- `docs/events.md` - Pixely.Events EventBus, event handlers, publishing, and DI auto-subscription
- `docs/observations.md` - Pixely.Observations: the log, its writer and its readers, trimming behind the slowest reader, capacity and stall detection, registration
- `docs/headless.md` - Headless mode: hidden offscreen windows and screenshots, synthetic mouse, keyboard, and text input on standard input, coordinate semantics, view targeting, physical-input boundaries, the `PIXELY_*` environment variables that override `PixelyConfig`
- `docs/static-factory-methods.md` - Static Create() method pattern
- `docs/componentize.md` - Pixely.Componentize setup and usage
- `docs/components.md` - GameWorld, GameObject, GameComponent lifecycle, Services<T>, UpdateSystem
- `docs/frame-order.md` - Update and render ordering: UpdateOrders/RenderOrders constants, the registration-order tie-break, input order as a separate axis
- `docs/pipeline-configuration.md` - GraphicsPipelineBuilder API (vertex types, shaders, depth testing)
- `docs/shaders.md` - Writing and using shaders (Slang, constant buffers, vertex/fragment stages)
- `docs/content-distribution.md` - Virtual content sources, source precedence, and build/publish distribution policies
- `docs/render-pass-flow.md` - Rendering architecture overview, CommandBuffer vs RenderPass, push constants, binding order
- `docs/subrenderers.md` - Composing multiple renderers within IRenderer<T> (ordering members, IEnumerable injection)
- `docs/ui.md` - Pixely.Ui retained UI: elements, sizing and layouts, scrolling, views and view models, pointer, wheel and focus routing, text fields, styling
- `docs/path-finding-grids.md` - Pixely.PathFinding.Grids: grid geometry, clearance-based agent footprints, connectivity and the corner rule, overlays, the admissible grid heuristic
- `docs/development-packages.md` - Consuming packages from the public development feed
- `docs/sdk.md` - Pixely as an MSBuild project SDK: the `<Sdk Name="Pixely" />` line, what it adds to a project, the browser build and its framework switch, central package management, the PIXELY0002 error for projects without it, the package layout with its two `lib/` folders
- `docs/hosting.md` - Hosting: opting in with `PixelyHosting`, the generated `Main`, `Configure` and `OnException`, diagnostics, the browser: `dotnet publish -r browser-wasm`, the `net11.0-browser` framework switch and its lateness, multi-targeting with `-f net11.0-browser`, `BrowserHost` and the frame loop, exit codes, the default page and its replacement, linking SDL3 into the browser runtime, WebGPU in the browser: the SDL fork, `PixelyBrowserWebGpu`, device adoption through `BrowserHost.PrepareAsync`, device loss and `GpuDeviceLostException`, the default page's stop message, teardown, what is unsupported
- `docs/taskbar-icons.md` - Application-wide taskbar and Dock icons loaded from virtual content
- `docs/sprites.md` - Sprite and animated sprite JSON files, the loaders and their cache, mirroring with `flip`
- `docs/peach-architecture.md` - Peach architecture for games built on Pixely: project layout, Game/Frontend boundary, stages, systems, AI and scenario projects; shipped in the package under `docs/` and enforced by Pixely.Fitness
- `docs/fitness.md` - Pixely.Fitness: PeachArchitectureOptions, evaluating and asserting a FitnessReport from a game's tests, the Pixely conventions rule set, adding other rule sets
- `docs/logging.md` - Pixely.Logging: registering ZLogger through the service collection, the application logger, logging calls, shutdown and durability
- `docs/window-rendering.md` - Single- and multi-window rendering: ViewScope, UseDefaultRendering, custom render contexts and window providers, scoped input, activating mouse clicks

## Maintenance

- `DECISIONS.md` - Design decisions with their deciding constraints and known costs, newest first
- `.github/PUBLISHING.md` - Development-feed and nuget.org publication setup, workflows, and recovery
