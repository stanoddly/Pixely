# Orange Architecture

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

Parts of this document describe Pixely APIs that do not exist yet:

- `Pixely.Ecs` is experimental and not in the Pixely package yet
- `Pixely.Fitness` has no Orange rules yet

## Foo.Vocabulary namespace

- Public primitives shared all over: enums, ids, read-only record structures, e.g. `TileCoordinate`
- A type only one namespace names lives with it, not here
- An id is its own type

## World

- Game state is components and tags in a `World`. The `World` is the state root
- A component is a struct. A tag is a struct that implements `ITag` and holds no data
- Components hold no rules. A system reads them through read-only spans or writes them through
  spans, so a setter or a read-only collection guards nothing
- Code outside systems MUST NOT write the world. `World` has no public members, so only a system's
  writers, spawners and lookups can
- The camera is components. Aiming is a system that runs after the camera system

## Systems

- Every rule lives in a system, so a system is the only code that changes game state
- A system implements `ISystem`. Its constructor takes its contract: queries, lookups, writers,
  spawners, message readers and writers, and the view models it syncs
- A system MUST NOT implement `IUpdatable`, PXECS014 reports one that does
- A system SHOULD declare every system it depends on with `IAfter<...>` and `IBefore<...>`, see `docs/ecs.md`. The order of `AddSystem` calls
  only breaks ties
- A system that consumes an intent MUST validate it. A rejected intent becomes an observation
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
- A system that consumes a message takes its own `MessageReader<T>` in its constructor and reads it
  in `Update`. A reader MUST read at the same point in every frame, because a message lasts only
  until `UpdateOrders.Maintenance` of the next frame
- Any code on the main thread MAY write a message, except a view's event handler, see UI
- Messages are not saved. A save captures the world at `UpdateOrders.Maintenance`, so state that
  must outlive a frame MUST be a component, e.g. a queued order
- Code MUST NOT save directly. It writes an intent, and the saver reads it at
  `UpdateOrders.Maintenance`

## Input

- An input system reads Pixely's input services and writes intents
- A continuous intent is a component, e.g. the direction the player steers
- A one-shot intent is a message, e.g. switching the flashlight
- Any system MAY read Pixely's input services
- Pixely dispatches input at the start of the frame, before any updatable runs, so the intents of
  that frame are in place before the first system

## UI

- `Pixely.Ui` keeps its retained element tree and its events. Layout, focus, the caret and an edit in
  progress are UI state, not game state
- A view takes exactly one view model. Pixely.Fitness checks it with `ViewsTakeOneViewModel`. This
  is stricter than `Pixely.Ui`, which lets a view observe several models, see `docs/ui.md`
- A view reads the world only through its view model. It MUST NOT take queries or lookups
- A view model holds what its view shows and the intents its view requested. It is UI state, not
  game state
- An event handler calls its view model, e.g. `viewModel.Toggle()`. It MUST NOT call a rule, write
  the world or write a message
- A sync system copies the world into a view model and takes the requested intents from it. It is
  a system that takes the view model in its constructor. It validates each intent like any system,
  and a rejected one becomes an observation

```csharp
internal sealed class PlayerFlashlights : Query<Span<Flashlight>>, IWith<Player>;

internal sealed class FlashlightSync(PlayerFlashlights flashlights, FlashlightViewModel viewModel, MessageWriter<FlashlightRejected> rejections)
    : ISystem
{
    public void Update()
    {
        bool toggled = false;
        foreach (Columns<Span<Flashlight>> columns in flashlights)
        {
            Span<Flashlight> lights = columns.Item1;
            for (int i = 0; i < lights.Length; i++)
            {
                if (viewModel.ToggleRequested)
                {
                    lights[i].On = !lights[i].On;
                    toggled = true;
                }
                viewModel.On = lights[i].On;
            }
        }
        if (viewModel.ToggleRequested && !toggled)
        {
            // no player has a flashlight, e.g. the player is dead
            rejections.Write(new FlashlightRejected());
        }
        viewModel.ToggleRequested = false;
    }
}
```

- Only a sync system writes a view model. A rule system MUST NOT, because its structural changes,
  through adders, removers, spawners and destroyers, apply at the next sync point and are dropped
  if a system throws before it. The view could show a state the world never reached. Writes through
  spans and lookups apply at once
- A sync system SHOULD declare `IAfter<...>` for the systems whose components it copies. Its
  queries name those components, so the scheduler applies their structural changes at a sync point
  before it
- A sync system writes every value every frame, because Pixely.Ecs has no change detection. A view
  model MUST raise `Changed` only when a value differs. The view's `Sync` runs on attach and then
  on each `Changed`
- `Changed` calls the view's `Sync` directly, so `Sync` runs inside the sync system's `Update`. An
  exception in `Sync` drops the structural changes recorded since the last sync point, like an
  exception in any system
- An intent waits in the view model until the next `Update` of its sync system. A sync system that
  takes the intent and applies it through a structural change loses it if a later system throws
  before the next sync point
- An intent that more than one system reads is a message, which the sync system writes

## Rendering and audio

- Rendering and audio take read-only queries and lookups in their constructors, registered with
  `AddQuery` and `AddReadOnlyComponentLookup`, see `docs/ecs.md`
- They MUST NOT write
- A renderer MUST NOT read messages, because Pixely skips rendering while a window is minimized. An
  updatable reads them and keeps what the renderer draws

## Not settled yet

- Project layout and the references between projects
- Registrars, stages and where each stage's `World` lives. A child collection that calls `AddEcs`
  keeps its parent's world
- Autonomous actors, e.g. a non player participant or a scripted scenario
- Hidden information and what each participant perceives
- References between entities, and finding the children of an entity without scanning every entity
- Optional components in queries
- Iteration order, and the tie-breaks a rule needs where order matters
- Loading a save, and what it does with messages written before it, since `Messages<T>` cannot be
  cleared
