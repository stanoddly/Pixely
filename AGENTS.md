# AGENTS.md

## Documentation

- `docs/class-registration.md` - ServiceCollection/ServiceProvider API: registration overloads, source generator requirements, lifecycle, aliases, multi-registration, parent/child provider callback merging and scoped lifecycles
- `docs/events.md` - Pixely.Events EventBus, event handlers, publishing, and DI auto-subscription
- `docs/input-automation.md` - Synchronous synthetic mouse, keyboard, and text input, coordinate semantics, view targeting, and physical-input boundaries
- `docs/static-factory-methods.md` - Static Create() method pattern
- `docs/componentize.md` - Pixely.Componentize setup and usage
- `docs/components.md` - GameWorld, GameObject, GameComponent lifecycle, Services<T>, UpdateSystem
- `docs/pipeline-configuration.md` - GraphicsPipelineBuilder API (vertex types, shaders, depth testing)
- `docs/shaders.md` - Writing and using shaders (Slang, constant buffers, vertex/fragment stages)
- `docs/content-distribution.md` - Virtual content sources, source precedence, and build/publish distribution policies
- `docs/render-pass-flow.md` - Rendering architecture overview, CommandBuffer vs RenderPass, push constants, binding order
- `docs/subrenderers.md` - Composing multiple renderers within IRenderer<T> (IOrderable, IEnumerable injection)
- `docs/architecture-concept.md` - MVP + CQS + Events: layer responsibilities, boundary contract vs. internal representation, per-genre decision framework
- `docs/architecture-library.md` - Pixely.Architecture API: command/query handlers, dispatcher, domain event stream/cursor, pump, post-dispatch hooks, registration extensions
- `docs/architecture-testing.md` - Pixely.Architecture.Testing: CqsConventions and ModelBoundary checks that enforce the architecture-concept.md boundary claims as unit tests
- `docs/development-packages.md` - Consuming packages from the public development feed
- `docs/taskbar-icons.md` - Application-wide taskbar and Dock icons loaded from virtual content

## Maintenance

- `.github/PUBLISHING.md` - Development-feed and nuget.org publication setup, workflows, and recovery
