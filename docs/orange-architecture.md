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
  - Messages: what crosses into or out of the systems, as intents and observations
- Code divides into the world, the systems and the code outside systems. Systems write the world.
  Code outside systems reads it and writes intents as messages
- Speed is a side benefit. Components are stored by type in contiguous arrays

## Status

Parts of this document describe Pixely APIs that do not exist yet:

- `Pixely.Ecs` is experimental and not in the Pixely package yet
- `Pixely.Messages` is `Pixely.Observations` today, see `docs/observations.md`.
  `MessageWriter<T>` and `MessageReader<T>` are `ObservationWriter<T>` and `ObservationReader<T>`
- `Pixely.Ui` still has `IUiViewModel` and `UiView<TViewModel>`. Views without view models are
  planned
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
  spawners, and message readers and writers
- A system MUST NOT implement `IUpdatable`, PXECS014 reports one that does
- A system SHOULD declare every system it depends on with `IAfter<...>` and `IBefore<...>`, see `docs/ecs.md`. The order of `AddSystem` calls
  only breaks ties
- A system that consumes an intent MUST validate it. A rejected intent becomes an observation
  message

## Messages

- A message is an intent or an observation
  - An intent is what code outside systems asks the systems to do, e.g. switch the flashlight
  - An observation is a transition that two reads of the world one frame apart cannot derive, see
    `docs/observations.md`
- A system takes `MessageWriter<T>` in its constructor like any service
- A system that consumes a message creates its own `MessageReader<T>` from the log in its
  constructor and drains it in `Update`
- Any code on the main thread MAY write a message, so a UI handler needs no spawner

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
- A view has no view model
- An event handler MUST write intents as messages. It MUST NOT call a rule or write the world
- A view takes the `MessageWriter<T>` types it writes in its constructor, as a system takes its
  writers, so its writes stay visible
- A view's `Sync` runs every frame and reads read-only queries and lookups. It needs no change
  detection, because an element setter ignores an unchanged value

## Rendering and audio

- Rendering and audio take read-only queries and lookups in their constructors, registered with
  `AddQuery` and `AddReadOnlyComponentLookup`, see `docs/ecs.md`
- They MUST NOT write

## Not settled yet

- Project layout and the references between projects
- Registrars, stages and where each stage's `World` lives. A child collection that calls `AddEcs`
  keeps its parent's world
- Autonomous actors, e.g. a non player participant or a scripted scenario
- Hidden information and what each participant perceives
- References between entities, and finding the children of an entity without scanning every entity
- Optional components in queries
- Iteration order, and the tie-breaks a rule needs where order matters
