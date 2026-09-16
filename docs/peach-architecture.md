# Peach Architecture

- Architecture for any genre, e.g. RTS, 4X, turn based, Vampire Survivors
- The framework is Pixely. What Pixely provides is listed below, this document adds only what a game
  does with it
- MUST, SHOULD and MAY are RFC 2119. A rule stated for a project holds for every namespace in it
- `Foo` is a placeholder for the game's name, the rest of every name is literal. A game named
  Ashfall has `Ashfall.Game`
- A participant is a side the rules treat as one. This document says participant, a game says its own
  word, e.g. faction
- Each project MUST register what it offers through public extension methods, one per container it
  registers into. A root method extends `PixelyAppBuilder`, a stage method extends
  `ServiceCollection`, e.g. `AddGamePersistence` for root and `AddGame` for the stage. They are the
  only way `Executable` registers an internal type. A registrar registers the same types whatever
  its arguments; arguments configure values, not what exists
- A stage is what the player is in, a mission or a menu. Each has its own state root
- Not every game needs every part
  - `Systems` exist when rules advance with time. A turn based game where nothing happens between
    actions has none
  - The `Ai` project exists when a non player participant acts like a player, by calling Mechanics. A
    Survivors clone drives its enemies in `Systems` and has no `Ai`

## What peach means here

- `Game` is the stone. It is sealed, nothing outside it writes it, and a player never touches it
  directly. Everything the player sees or hears is flesh
- There is exactly one boundary, not a stack of them. Unlike an onion nothing wraps `Frontend`, and
  no call passes inward through a layer to reach State
- The flesh exists to put the stone in front of someone, and the stone is whole without it
- It is not a process or a network boundary. Everything is one process, a reader holds the live
  reference and copies only what it keeps, nothing is serialized and there are no transport records

## Projects

```text
Game                ──> framework only
Frontend            ──> Game, Frontend.Rendering, Frontend.Audio
Frontend.Rendering  ──> Game
Frontend.Audio      ──> Game
Ai                  ──> Game
Scenario            ──> Game
Executable          ──> everything above
```

- The `Foo.` prefix is omitted. Every other name in this document is a namespace in one of these,
  e.g. `Frontend.Forms`
- An arrow is what a project MUST reference. Nothing else MAY compile against it

## What Pixely provides

- The container and the stages, `IStageManager` builds one as a child of the root provider
- The frame loop, an updatable is a `Pixely.IUpdatable` and its `UpdateOrder` places it, lower first
- The log, `Pixely.Observations` is its storage, writer and reader. It has no subscribers, drops an
  entry every reader has passed, and throws when a reader stops draining
- The user interface, `Pixely.Ui` is retained and `IUiViewModel.Changed` decides when a view syncs
- Content loading
- This document, shipped in the package under `docs/`. In a consuming project
  `dotnet msbuild -getProperty:PixelyDocsDirectory` prints where

## Foo.Game project

- The game proper: what is true, the rules that change it and the record of what happened. It is not
  a shared library, code that carries no rule does not belong here
- It MUST NOT know a frontend, a participant that is a player, or an output
- It MUST NOT push: no events, observers or callbacks, so it holds no delegate. A reader polls State
  or drains the log

### Foo.Game.Vocabulary namespace

- Public primitives shared all over: enums, ids, read-only record structures, e.g. `UnitId`,
  `ParticipantId`, `TileCoordinate`, `TilePoint`
- A type only one namespace names lives with it, not here
- An id is its own type

### Foo.Game.State namespace

- There MUST be one state root per stage. It is the one `State` class no other `State` type holds.
  It SHOULD be handed to its readers through the constructor
- Storage MAY be ECS or not, depends on the game needs
- Mutation MUST be `internal`, so nothing outside `Game` writes State. `Mechanics` and `Systems`
  write it. A public property MUST NOT have a setter, a public collection MUST be an
  `IReadOnlyList<T>`, an `IReadOnlyDictionary<TKey, TValue>` or a `ReadOnlySpan<T>`
  - So no `InternalsVisibleTo` between production assemblies, and everything reachable from the
    state root lives in `State` or `Vocabulary`, where those rules are checked
- A read MUST NOT return a transport record. The reader holds the live reference and copies only
  what it keeps
- When a game hides information, what each participant perceives is State, kept per participant. A
  reader bound to a participant MUST read through it, e.g. `ForParticipant(id)` on the state root
- Recomputing it is a Mechanic or a System, not part of the read
- State holds no logic, so it needs no tests. Logic is a rule that reaches beyond the object's own
  fields, e.g. whether a unit is hidden depends on what it is doing and where. A read of the object's
  own fields, e.g. `IsRipe => Growth >= GrowthTime`, is not

### Foo.Game.Mechanics namespace

- Basically game rules exposed via instance class methods
- Each Mechanic MUST have a `Mechanic` suffix
- A Mechanic is public when a project outside `Game` calls it, otherwise internal. Every other type
  in `Mechanics` that no public Mechanic exposes, e.g. as an outcome, MUST be internal, otherwise a
  helper that mutates State is callable from outside `Game`
- A Mechanic MAY call other Mechanics
- A Mechanic method SHOULD return an outcome, semantic, never a user facing message. It MAY return
  what it created, e.g. an id
- A Mechanic MUST apply its effect during the call. No command records, no dispatcher. Work that
  spans time is State the call writes, e.g. a construction job a System advances
- Validation MUST live here. `Frontend` MAY compute the same rule for a preview, the Mechanic's
  answer is authoritative
- A Mechanic SHOULD take its full payload in one call. E.g. drafts and multi-step flows are `Frontend`
  state that become one call when committed

### Foo.Game.Systems namespace

- Basically game rules triggered every tick, but MAY execute its job less often
- Systems MUST implement `Pixely.IUpdatable` and MUST be internal. A System MAY write State
  directly and MAY use Mechanics

### Foo.Game.Observations namespace

- State says what is true, an entry says a transition that reads of State cannot derive
  - The test for a new entry type: name what it carries that two reads of State one frame apart do not
  - The same action may be either, and what State keeps decides it. An RTS walk advances every tick and
    is in State, a turn based walk resolves in one call and is an entry
- A Mechanic or a System appends during the call or tick that caused it
- Entries MUST be past tense records of ids and value types, never live State. Every entry type MUST
  have an `Entry` suffix, e.g. `UnitMovedEntry`
- One log per stage, carrying one entry type. The entry shape and the capacity are the game's, see
  `docs/observations.md`
- When a game hides information, an entry names the participant that perceived it, a game with one
  participant names none
  - A writer appends one entry per participant that perceived the action, carrying only what that
    participant perceived. Perception is a fact of the moment, a reader cannot reconstruct it later
- `Frontend` and an autonomous actor MAY read, each with its own reader
- A reader bound to one participant MUST ignore an entry naming another. That check SHOULD live in one
  reader that filters, rather than be repeated in every consumer that drains the log

## Foo.Frontend project

- Directs and coordinates what player sees and hears
- Its state is selection, drafts, previews and what a form shows. Dropping it loses what the player
  was doing, never game state. A tool the player selected decides what the next click means, and
  that is Frontend's to decide
- Output infrastructure, e.g. render targets and audio clips, is root-scoped and survives stage
  changes. Only what is bound to one stage's state belongs to the stage
- A form that outlives or replaces a stage, e.g. a save picker, is root-scoped. It MAY reach root
  services and `IStageManager`, it MUST NOT hold a reference into a stage
- `Rendering` and `Audio` are output. They present what `Frontend` tells them to, e.g. a walk along a
  path or a hit sound, and MUST NOT mutate State or invoke Mechanics
- `Frontend` and its outputs are one side of the boundary. The split between them is only a
  dependency direction, `Rendering` and `Audio` never reference `Frontend`, and there SHOULD be no
  abstraction layer between them. `Frontend` MAY call or poll anything an output makes public
- `Frontend` SHOULD NOT subscribe to an output. A handler runs inside the output's `Update`, so a
  Mechanic invoked from it mutates State while the output reads it. `Frontend` SHOULD poll the output
  in its own `Update` instead
- Output state MUST NOT carry meaning. Dropping it changes what is seen or heard, never game
  state, never the meaning of input
- `Frontend` interprets, outputs execute. `Frontend` MUST decide what State means for output, e.g.
  that a fast unit plays "run", and tell the output what to play. An output MUST NOT read that
  meaning out of State itself
- An output answers only for what it is doing now. `Frontend` keeps what it has handed over, what
  is pending and when input reopens. An output MUST NOT hold that for it

### Foo.Frontend root namespace

- Owns input interpretation, selection, drafts, previews
- Hit testing MUST resolve against State, never against what `Rendering` drew
- MAY read State and invoke Mechanics
- SHOULD refuse input that targets what the player has not seen yet

### Foo.Frontend.Forms namespace

- `Pixely.Ui` based user interface, a `UiView<TViewModel>` per form
- `Pixely.Ui` is retained. `Sync` runs when the ViewModel raises `Changed`, never per frame, so a form
  MUST NOT read State or invoke Mechanics directly
- A ViewModel MAY hold nothing but Frontend state and never read State, e.g. a settings form
- A ViewModel that shows State SHOULD implement `Pixely.IUpdatable`, read State in `Update` and raise
  `Changed` when what it shows differs. It MAY invoke Mechanics to update the State

## Foo.Frontend.Rendering and Foo.Frontend.Audio projects

- MAY read State. What State says every frame, e.g. a position that advances every tick, is drawn
  from State directly
- State is ahead of the screen, e.g. the Mechanic already put the unit at the end of its path while
  `Rendering` still shows the walk

## Autonomous actor projects

- An autonomous actor MUST NOT be reachable from `Frontend`
- An autonomous actor reads State, MAY read the log, calls Mechanics, and decides nothing about
  output. It MUST NOT own state that outlives a call, a plan spanning turns is State
- `Foo.Ai` is a non player participant that acts like a player
- `Foo.Scenario` is scripted, e.g. triggers that read the log and fire Mechanics. It MUST act after
  the rules that can fire it and before any participant acts on the result

## Foo.Executable project

- Composition and the frame loop, nothing else
- Its assembly is named `Foo.Executable` or `Foo`
- It SHOULD set `PixelyHosting`, see `docs/hosting.md`
- Two containers. Root is the application: platform, window, output infrastructure, content,
  root-scoped forms. A stage is a child container holding its state root, Mechanics, Systems, log,
  `Ai` and the `Frontend` bound to that state. A stage MAY reach root, root MUST NOT reach a stage
- State MUST NOT be reset in place. Another run is another stage
- The frame is single threaded, set by Pixely, so nothing returns a `Task`
- Pixely delivers input before any updatable runs, so `Frontend` input handlers run first in the frame
- Updatables MUST run in this order: `Systems`, `Scenario`, `Ai`, `Frontend`, then `Rendering` and
  `Audio`. Each is an `UpdateOrder` band, lower first. The sort is stable: updatables with the same
  `UpdateOrder` run in registration order, so a game MAY rely on it, see `docs/frame-order.md`

## Out of scope

- Deliberate, not an omission to fill in. Each game decides these where it needs them
- Persistence. Where it lives is the game's call. The "no rule" test on `Game` does not exclude it,
  a save schema is not a rule but it is bound to State tighter than to anything else
- A second frontend, e.g. an editor

## TODO

- Turn `Out of scope` into what a game's own document MUST answer: persistence, stage transitions, a
  second frontend, the frame composition, its own Vocabulary
- Keep the reason behind a rule and add it back where it was cut. A rule with no reason gets
  extrapolated wrongly on a case it does not cover
- Decide whether a Mechanic call from a form passes the input refusal the `Frontend` root owns
- Decide whether a bounded exception to what an entry carries is allowed, e.g. naming the tile a
  unit stepped from when it steps into view, so the move can be animated
