# AGENTS.md

## Documentation

- `docs/class-registration.md` - ServiceCollection/ServiceProvider API: registration overloads, source generator requirements, lifecycle, aliases, multi-registration, parent/child provider callback merging and scoped lifecycles
- `docs/events.md` - Pixely.Events EventBus, event handlers, publishing, and DI auto-subscription
- `docs/observations.md` - Pixely.Observations: the log, its writer and its readers, trimming behind the slowest reader, capacity and stall detection, registration
- `docs/input-automation.md` - Synchronous synthetic mouse, keyboard, and text input, coordinate semantics, view targeting, physical-input boundaries, the standard-input command protocol, headless mode and screenshots
- `docs/static-factory-methods.md` - Static Create() method pattern
- `docs/componentize.md` - Pixely.Componentize setup and usage
- `docs/components.md` - GameWorld, GameObject, GameComponent lifecycle, Services<T>, UpdateSystem
- `docs/frame-order.md` - Update and render ordering: UpdateOrders/RenderOrders constants, the registration-order tie-break, input order as a separate axis
- `docs/pipeline-configuration.md` - GraphicsPipelineBuilder API (vertex types, shaders, depth testing)
- `docs/shaders.md` - Writing and using shaders (Slang, constant buffers, vertex/fragment stages)
- `docs/content-distribution.md` - Virtual content sources, source precedence, and build/publish distribution policies
- `docs/render-pass-flow.md` - Rendering architecture overview, CommandBuffer vs RenderPass, push constants, binding order
- `docs/subrenderers.md` - Composing multiple renderers within IRenderer<T> (ordering members, IEnumerable injection)
- `docs/ui.md` - Pixely.Ui retained UI: elements, sizing and layouts, views and view models, pointer and focus routing, text fields, styling
- `docs/path-finding-grids.md` - Pixely.PathFinding.Grids: grid geometry, clearance-based agent footprints, connectivity and the corner rule, overlays, the admissible grid heuristic
- `docs/development-packages.md` - Consuming packages from the public development feed
- `docs/taskbar-icons.md` - Application-wide taskbar and Dock icons loaded from virtual content
- `docs/peach-architecture.md` - Peach architecture for games built on Pixely: project layout, Game/Frontend boundary, stages, systems, AI and scenario projects; shipped in the package under `docs/` and enforced by Pixely.Fitness
- `docs/fitness.md` - Pixely.Fitness: PeachArchitectureOptions, evaluating and asserting a FitnessReport from a game's tests, adding other rule sets

## Maintenance

- `.github/PUBLISHING.md` - Development-feed and nuget.org publication setup, workflows, and recovery
