# Messages

`Pixely.Messages` passes messages from the code that writes them to every code that reads them, one frame at a
time. Nothing is called back. Each reader holds its own position and reads when it suits its own point in the
frame. It follows Bevy's messages: `Messages<T>`, `MessageWriter<T>` and `MessageReader<T>`.

A message is an intent or an observation:

- An intent is what code outside systems asks the systems to do, for example switch the flashlight.
- An observation is a transition that two reads of game state one frame apart cannot derive: a unit that walked
  a path and arrived, one that appeared and was gone again, an action that resolved and was reversed inside the
  same frame. State answers what is true now, and a rule can resolve many transitions between two reads, so any
  "most recent transition" field is overwritten before a reader looks at it.

Three types carry the roles:

- `Messages<TMessage>` is the storage. Its only public member is its constructor. It implements `IUpdatable` for
  the frame loop, and nothing else may call its `Update`, because each call drops messages.
- `MessageWriter<TMessage>` writes. It cannot read.
- `MessageReader<TMessage>` is one reader's position. It cannot write.

So code holding a writer cannot read the messages, and a reader cannot write a message.

The messages belong to one frame loop and are not thread safe. Writing and reading happen on the main thread.

## The message type

One `Messages<TMessage>` carries one message type. To carry several kinds of message in one order, make
`TMessage` a tagged type; the storage never looks inside it. A value type is the one to reach for, because writing
it costs no allocation.

```csharp
public readonly record struct UnitMoved(UnitId Unit, TilePoint From, TilePoint To);
public readonly record struct UnitDied(UnitId Unit);

public enum ObservationKind { UnitMoved, UnitDied }

public readonly record struct Observation(ObservationKind Kind, UnitMoved Moved, UnitDied Died);
```

Messages are past-tense records of ids and value types, never a live reference into game state. With a value
message nothing allocates per message once the buffers have grown to the largest frame: `Write` takes it by `in`
and `TryRead` copies it out.

`TMessage` may be a class where that suits the game better. The storage releases a message when it drops it. It
costs an allocation per write, so it does not belong on a path that writes every frame.

## Registration

```csharp
services.AddMessages<Observation>();
```

`AddMessages<TMessage>` registers `Messages<TMessage>`, a `MessageWriter<TMessage>` over it, and
`MessageReader<TMessage>` as a transient, so each consumer that takes a reader gets its own. Calling it again, or
in a child collection whose parent already called it, changes nothing.

Register it in the scope it belongs to. Messages registered in a stage's child collection die with that stage,
and nothing carries into the next one.

## Writing

Take `MessageWriter<TMessage>` where code writes messages:

```csharp
public sealed class MoveSystem : ISystem
{
    private readonly MessageWriter<Observation> _observations;

    public MoveSystem(MessageWriter<Observation> observations) => _observations = observations;

    public void Update()
    {
        // change the world, then record what happened
        _observations.Write(new Observation(ObservationKind.UnitMoved, moved, default));
    }
}
```

## Reading

Take `MessageReader<TMessage>` in a system or an updatable, and read it at the same point in every frame:

```csharp
public sealed class UnitSpritePresenter : IUpdatable
{
    private readonly MessageReader<Observation> _observations;

    public UnitSpritePresenter(MessageReader<Observation> observations) => _observations = observations;

    public void Update()
    {
        while (_observations.TryRead(out Observation observation))
        {
            // switch on the tag
        }
    }
}
```

A reader starts after the last message written, so a consumer created part-way through a run sees only what is
written from then on. Readers read independently: two readers of the same messages both see every message.

A renderer MUST NOT read messages. Pixely skips rendering in a frame where a window has nothing to draw into, for
example while it is minimized, so a reader in a renderer misses messages. An updatable reads them instead and keeps
what the renderer draws.

## How long a message lasts

Example:

1. In frame 5, `UnitSpritePresenter` reads and finds nothing. Later in frame 5, `MoveSystem` writes `UnitMoved`.
2. In frame 6, the presenter reads `UnitMoved`.
3. At `UpdateOrders.Maintenance` in frame 6, the message is dropped.

`Messages<TMessage>` keeps two buffers, one for the current frame and one for the previous frame. It is an
updatable at `UpdateOrders.Maintenance`, and each update drops the older buffer. So a message written before
Maintenance lasts until Maintenance of the next frame. A message written after it, for example while rendering, lasts
one frame longer.

A reader that reads at the same point in every frame therefore sees every message. A reader that moves its read
across Maintenance can miss one. For example, it reads at the default order in frame 5, before a later system writes,
and then reads only after Maintenance in frame 6. The message is dropped between its two reads.

A reader that misses messages throws on its next read and names the message type, because a dropped message is a lost
intent or observation that nothing else would report:

```text
A reader of Observation missed 3 of its messages, which were dropped before it read them. A reader must read at the same point in every frame.
```

After it throws, the reader continues from the oldest message still kept, so a game that catches the exception keeps
reading.

## Reading as one participant

Addressing a message to a subset of readers is the game's business, not the storage's: it never looks inside
`TMessage`. Carry the perceiver in the message and implement `IMessageParticipation<TParticipantId>` on it. A
consumer bound to one participant then reads through `ParticipantMessageReader<TMessage, TParticipantId>`
instead of repeating the check in every place that reads.

```csharp
public readonly record struct Observation(ObservationKind Kind, ParticipantId Perceiver, UnitMoved Moved, UnitDied Died)
    : IMessageParticipation<ParticipantId>;
```

A positional `Perceiver` parameter already satisfies the interface, so implementing it adds no member.

The consumer wraps its own reader:

```csharp
public UnitSpritePresenter(MessageReader<Observation> observations, ParticipantId participant)
{
    _observations = new ParticipantMessageReader<Observation, ParticipantId>(observations, participant);
}
```

`TryRead` then yields only the messages that participant perceived. It reads the others and passes over them,
so it must still read at the same point in every frame like any reader.

## Saving

Messages are not saved. A save captures the world at `UpdateOrders.Maintenance`, after every system and view of the
frame has run and before rendering.

Example: in frame 5 a system removes a unit and writes `UnitDied`, and the presenter plays its sound. The game
saves in frame 5. After a load, the world has no unit, so the view draws none, and the sound does not play
again.

So a message reports what happened and never holds state the game needs later. A system that must remember an
intent beyond one frame stores it in a component, for example a queued order on the unit.

Code does not save directly. It writes an intent such as `SaveRequested`, and the code that saves reads it at
`UpdateOrders.Maintenance`.

What a load does with messages is not settled. `Messages<TMessage>` cannot be cleared, so a reader that outlives a
load reads the messages written before it. A load that rebuilds the stage, with messages registered in the stage's
child collection, starts with new readers and no messages.
