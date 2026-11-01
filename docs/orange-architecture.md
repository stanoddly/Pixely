# Orange Architecture

- Pixely has two architectures, [Peach](peach-architecture.md) and Orange. A game follows one
- Architecture for any genre, built on `Pixely.Ecs`, see `docs/ecs.md`
- The framework is Pixely. This document adds only what a game does with it
- MUST, SHOULD and MAY are RFC 2119
- `Foo` is a placeholder for the game's name, the rest of every name is literal. A game named
  Ashfall has `Ashfall.Vocabulary`

## What orange means here

- A reader, human or model, finds the game in three places:
  - Components: what is true
  - Systems: how it changes
  - Messages and view models: what crosses into or out of the systems, as intents, observations
    and what a view shows
- Code divides into the world, the systems and the code outside systems. Systems write the world.
  Code outside systems reads it and passes intents to systems through view models or messages
- Speed is a side benefit. Components are stored by type in contiguous arrays

## Status

Parts of what this document builds on are not final:

- `Pixely.Ecs` is experimental
- `Pixely.Fitness` checks only the rules that constructors and types show, see `docs/fitness.md`

## Project

- A game is one project, `src/Foo/Foo.csproj`, that sets `PixelyHosting`, see `docs/hosting.md`
- Its namespaces are `Foo.Vocabulary`, `Foo.Components`, `Foo.Messages`, `Foo.ViewModels`,
  `Foo.Systems`, `Foo.Views`, `Foo.Rendering` and `Foo.Audio`. Input systems are systems too
- Types SHOULD be internal, except `Program` and its `Configure`, which a test calls, see
  `docs/testing.md`. Tests reach the rest through `InternalsVisibleTo`. Pixely.Ecs, not a project
  boundary, keeps code outside systems from writing the world
- Every system is in the project, so a system orders itself after or before any other with
  `IAfter<...>` and `IBefore<...>`

## Composition

- `Configure` in `Program.cs` registers the root: Pixely and what survives a stage change, e.g. render
  targets and content. An `OnBuilt` callback loads the first stage through `IStageManager.Load`
- `AddGame`, an internal extension method on `ServiceCollection` in the root namespace `Foo`,
  registers the stage the game is played in. It calls `AddEcs` and registers the uniques, the
  messages, the systems, the view models, the views, the renderers, audio, and the queries and lookups
  that code outside systems reads
- It registers the same types whatever its arguments. Arguments configure values
- A stage owns its world, see World. State that the next stage needs is passed to its `AddGame` as an
  object, e.g. `AddGame(new GameStart(Seed: 42))` with `GameStart` a sealed record class, which it
  registers for the systems that read it. The container holds only classes
- A test builds the game's app with `TestApp.CreateBuilder()` and the game's `Configure`, sends input
  through `TestInput` and runs frames with `RunFrame`, see `docs/testing.md`

## Foo.Vocabulary namespace

- Primitives shared all over: enums, ids, read-only record structures, e.g. `TileCoordinate`,
  and static classes of `const` fields and `static readonly` fields of value types or strings
- A type only one namespace names lives with it, not here
- An id is its own type

## World

- Game state is components, tags and uniques in a `World`. The `World` is the state root
- A component is a struct. A tag is a struct that implements `ITag` and holds no data
- Components hold no rules. A system reads them through read-only spans or writes them through
  spans, so a setter or a read-only collection guards nothing
- A component MUST NOT hold a reference other than a `string`. A read-only span guards only the
  component's own bytes, so a `List<T>` it holds stays writable to every reader. It holds values or
  ids instead
- State that is not one entity's, e.g. terrain read by coordinate, is a unique, see `docs/ecs.md`. A tile is
  not an entity: neighbors are read by index, and every tile has the same components, so archetypes
  add nothing
- A unique's read-only type MUST NOT expose a reference that writes, e.g. it returns a
  `ReadOnlySpan<Tile>`, not a `Tile[]`. C# has no deep immutability, so the read-only type is the only guard
- `AddGame` adds the uniques with `AddUnique`, after `AddEcs`. Code outside systems takes
  `ReadOnlyUnique<TReadOnly>`, never the class, which `Orange OutsideSystemsTakeNoWritableUniques` checks
- The stage the game is played in calls `AddEcs`, not the root, so unloading or reloading the stage
  drops the world with its entities. A stage without a world, e.g. a main menu, does not call it
- Code outside systems MUST NOT write the world. `World` has no public members, so only a system's
  writers, spawners, lookups and uniques can
- The camera is components. Aiming is a system that runs after the camera system

## Systems

- Every rule lives in a system, so a system is the only code that changes game state
- A system implements `ISystem`. Its constructor takes its contract, which is queries, lookups,
  writers and spawners, and services, such as message readers and writers and the view model it
  writes
- A system MUST NOT implement `IUpdatable`, PXECS014 reports one that does
- A system SHOULD declare every system it depends on with `IAfter<...>` and `IBefore<...>`, see `docs/ecs.md`. The order of `AddSystem` calls
  only breaks ties
- A system that handles an intent MUST validate it. A rejected intent becomes an observation
  message

## Messages

- A message is an intent or an observation
  - An intent is what code asks the game to do, e.g. save the game
  - An observation is a transition that two reads of the world one frame apart cannot derive
- A message passes between systems, to more than one reader, or to readers its writer does not
  know. A flow between one view and one system goes through a view model, see UI
- Messages are `Pixely.Messages`, see `docs/messages.md`. Each message type is registered with
  `AddMessages<T>`
- A system takes `MessageWriter<T>` in its constructor like any service
- A system that reads a message takes its own `MessageReader<T>` in its constructor and reads it
  in `Update`. A reader MUST read at the same point in every frame, because a message lasts only
  until `UpdateOrders.Maintenance` of the next frame
- Any code on the main thread MAY write a message, except a view's UI event handler, see UI
- Messages are not saved. A save captures the world at `UpdateOrders.Maintenance`, so state that
  must outlive a frame MUST be a component or a unique, e.g. a queued order
- Code MUST NOT save directly. It writes an intent, and the saver reads it at
  `UpdateOrders.Maintenance`

## Input

- An input system reads Pixely's input services and writes intents
- A continuous intent is a component, e.g. the direction the player steers
- A one-shot intent is a message, e.g. a key that switches the flashlight. A button that does the
  same goes through its view model, see UI
- Any system MAY read Pixely's input services
- Pixely dispatches input at the start of the frame, before any updatable runs, so the intents of
  that frame are in place before the first system

## Actors

- An actor is an entity that acts, e.g. the player's character, a non-player participant, or an
  entity a scripted scenario drives
- An AI system writes intents like an input system, and MUST NOT apply them. The system that handles
  an intent validates it whoever wrote it, so the player and an AI follow the same rules
- A continuous intent is a component on its actor, e.g. `Steering` on the entity that steers. The
  input system writes it for the player's entity, and an AI system for the others
- A one-shot intent is a message that names its actor, e.g. `ToggleFlashlight(Entity Actor)`. Its
  reader reads every message and skips one whose actor is dead or lacks what the intent needs, which
  a lookup's `Has` tells
- A scripted scenario is systems. Its progress, e.g. the current step and its timers, MUST be
  components or a unique, because messages are not saved

## Hidden information

- What a participant perceives is state, which a perception system writes from the true state:
  components on the observer, e.g. `LastSeen` on a guard, tags on what is perceived, e.g.
  `Revealed`, or a unique for a grid, e.g. the tiles each participant has explored
- An AI system MUST read what its actor perceives, not the true state, e.g. the guard's `LastSeen`
  instead of the player's `Position`
- Rendering, audio and view models of a participant MUST read what that participant perceives, e.g.
  a query with `IWith<Revealed>` that draws only revealed enemies
- With several participants, the perceived state names its participant, e.g. a component that holds
  which participants have revealed an entity
- Nothing checks these rules. A query of the true state compiles anywhere, and Pixely.Fitness cannot
  tell a perceived component from a true one

## UI

- `Pixely.Ui` keeps its retained element tree and its events. Layout, focus, the caret and an edit in
  progress are UI state, not game state
- The rules below cover view models that show game state. View models of state outside the world
  follow them too, with an updatable in place of the system, see UI outside the world
- A view takes exactly one view model. `Pixely ViewsTakeOneViewModel`, a Pixely conventions rule in
  Pixely.Fitness, checks its constructor. This is stricter than `Pixely.Ui`, which lets a view
  observe several models, see `docs/ui.md`
- A view reads the world only through its view model. It MUST NOT take queries, lookups or uniques
- A view MUST NOT take a message reader or writer
- A view model holds what its view shows and the intents its view requested. It is UI state, not
  game state
- A view's UI event handler calls its view model, e.g. `viewModel.Toggle()`. It MUST NOT call a
  system, write the world or write a message
- A system that writes a view model takes it in its constructor. It handles each intent the view
  model holds: it reads the intent, validates it like any intent, and clears it in the same
  `Update`, whether it applies or rejects it. Then it writes what the view shows
- It MAY also apply other rules and derive what the view shows, e.g. a percentage from two
  components
- Exactly one system MUST write each view model, and it MUST handle every intent the view model
  holds. With two writers, `Changed` is raised twice every frame and the view shows only the later
  system's values. An intent that no system handles waits forever

```csharp
internal sealed class PlayerFlashlights : Query<Span<Flashlight>>, IWith<Player>, IWithout<Dead>;

internal sealed class FlashlightSystem(PlayerFlashlights flashlights, FlashlightViewModel viewModel, MessageWriter<FlashlightRejected> rejections)
    : ISystem
{
    public void Update()
    {
        bool found = false;
        bool on = false;
        foreach (Columns<Span<Flashlight>> columns in flashlights)
        {
            Span<Flashlight> lights = columns.Item1;
            for (int i = 0; i < lights.Length; i++)
            {
                if (viewModel.ToggleRequested)
                {
                    lights[i].On = !lights[i].On;
                }
                found = true;
                on = lights[i].On;
            }
        }
        if (viewModel.ToggleRequested && !found)
        {
            // no living player has a flashlight
            rejections.Write(new FlashlightRejected());
        }
        viewModel.ToggleRequested = false;
        viewModel.On = on;
    }
}
```

- Every value that shows game state MUST come from what the system reads: the world, or messages,
  e.g. an observation shown as a toast. It MUST NOT come from a structural change the system
  recorded itself
- E.g. `DeathSystem` adds `Dead` through `ComponentAdder<Dead>`, so it MUST NOT set
  `viewModel.Dead = true` in the same `Update`. `Dead` exists only after the next sync point, and is
  dropped if a system throws before it. The system that writes the view model sets
  `viewModel.Dead` from a query or lookup that names `Dead`
- A write through a span or a lookup applies at once, so reading it back is reading the world, as
  `lights[i].On` above
- A rejected intent becomes an observation message, see Systems. The system MAY also show the
  rejection in the view model
- A system that writes a view model SHOULD declare `IAfter<...>` for the systems that change the
  components its queries and lookups name, including those in `IWithout<...>`. The scheduler then
  applies their structural changes at a sync point before it
- A system MUST write every value of its view model every frame, because Pixely.Ecs has no change
  detection. A view model MUST raise `Changed` when, and only when, a value its view shows changes
- The view's `Sync` runs on attach, and then on each `Changed` while the view is attached. A view
  model that raises `Changed` in a setter runs `Sync` inside the system's `Update`, so an exception
  in `Sync` drops the structural changes recorded since the last sync point, like an exception in
  any system
- An intent waits in the view model until the next `Update` of the system that handles it. A system
  that applies it through a structural change loses it if a system throws before the next sync point
- An intent that more than one system reads is a message. The system that writes the view model
  forwards it as one

## UI outside the world

- A view model of state outside the world, e.g. settings, an FPS counter, or a main menu before a
  world exists, is written by an `IUpdatable`, because no system can write it: a stage without a
  world has no systems, and state in the root outlives every stage's systems
- The updatable lives where its state lives: settings in the root, a main menu in its stage
- It follows the rules of a system that writes a view model: exactly one updatable writes each view
  model, handles every intent the view model holds, validates it and clears it in the same `Update`,
  and writes every value its view shows
- A view model that shows any game state is written by a system. An updatable MUST NOT write a view
  model that a system writes, and MUST NOT read the world for one it writes

## Rendering and audio

- Rendering and audio take read-only queries, lookups and uniques in their constructors, registered
  with `AddQuery`, `AddReadOnlyComponentLookup` and `AddUnique`, see `docs/ecs.md`
- They MUST NOT write
- A renderer MUST NOT read messages, because Pixely skips rendering while a window is minimized. An
  updatable reads them and keeps what the renderer draws. The updatable that reads them MUST NOT be
  the renderer

## Not settled yet

- References between entities, and finding the children of an entity without scanning every entity
- Iteration order, and the tie-breaks a rule needs where order matters
- Loading a save, and what it does with messages written before it, since `Messages<T>` cannot be
  cleared
- Whether two views may share one view model, which would run both views' `Sync` on each change and
  merge their intents
