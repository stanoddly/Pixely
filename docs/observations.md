# Observations

`Pixely.Observations` is an append-only log of entries that readers drain at their own pace. Rules append;
nothing is called back. A reader holds its own position, reads when it suits its own point in the frame, and
the log drops an entry once every reader has passed it.

Use it for a transition that two reads of game state one frame apart cannot derive: a unit that walked a path
and arrived, one that appeared and was gone again, an action that resolved and was reversed inside the same
frame. State answers what is true now, and a rule can resolve many transitions between two reads, so any
"most recent transition" field is overwritten before a reader looks at it.

Three types carry the core roles:

- `ObservationLog<TEntry>` is the storage. It is constructed and then handed to the other two; it has no other
  public members.
- `ObservationWriter<TEntry>` appends. It cannot read.
- `ObservationReader<TEntry>` is one reader's position in the log. It cannot append.

So a rule holding a writer has no way to drain the log, and a reader has no way to record an observation no
rule produced.

A fourth, `ParticipantObservationReader<TEntry, TParticipantId>`, reads through a reader of its own and hands on
only what one participant perceived. See [Reading as one participant](#reading-as-one-participant).

The log belongs to one frame loop and is not thread safe. Appending, reading and constructing a reader all
happen on the same thread.

## The entry type

One log carries one entry type. To carry several kinds of entry in one order, make `TEntry` a tagged type; the
log never looks inside it. A value type is the one to reach for, because appending it costs no allocation.

```csharp
public readonly record struct UnitMovedEntry(UnitId Unit, TilePoint From, TilePoint To);
public readonly record struct UnitDiedEntry(UnitId Unit);

public enum ObservationKind { UnitMoved, UnitDied }

public readonly record struct Observation(ObservationKind Kind, ParticipantId Perceiver, UnitMovedEntry Moved, UnitDiedEntry Died);
```

Entries are past-tense records of ids and value types, never a live reference into game state. With a value
entry nothing allocates per entry: the log stores `TEntry` inline in an array, `Append` takes it by `in`, and
`TryRead` copies it out.

`TEntry` may be a class where that suits the game better, and the log releases each slot as it trims so a
drained entry is not held alive. It costs an allocation per append, so it does not belong on a path that
appends every frame.

## Writing

Inject `ObservationWriter<TEntry>` where rules record what happened:

```csharp
internal sealed class MoveMechanic
{
    private readonly ObservationWriter<Observation> _observations;

    internal MoveMechanic(ObservationWriter<Observation> observations) => _observations = observations;

    internal void Move(UnitId unit, TilePoint destination)
    {
        // mutate state, then record what happened
        _observations.Append(new Observation(ObservationKind.UnitMoved, perceiver, moved, default));
    }
}
```

## Reading

Inject the log, construct a reader named after the consumer, and drain it in the consumer's own update. Dispose
the reader with the consumer:

```csharp
internal sealed class UnitSpritePresenter : IUpdatable, IDisposable
{
    private readonly ObservationReader<Observation> _observations;

    internal UnitSpritePresenter(ObservationLog<Observation> log)
    {
        _observations = new ObservationReader<Observation>(log, nameof(UnitSpritePresenter));
    }

    public void Update()
    {
        while (_observations.TryRead(out Observation observation))
        {
            // switch on the tag
        }
    }

    public void Dispose() => _observations.Dispose();
}
```

Each consumer constructs its own reader rather than being handed one, because every reader of a given log is
the same closed type and the container resolves by type. Constructing it also lets the consumer name it.

A reader starts positioned after the last appended entry, so a consumer created part-way through a run sees
only what is appended from then on. Readers drain independently: entries appended this frame may be drained by
one reader now and by another several frames later.

### Reading as one participant

Addressing an entry to a subset of readers is the game's business, not the log's: the log never looks inside
`TEntry`. Carry the perceiver in the entry, implement `IObservationParticipation<TParticipantId>` on it, and a
consumer bound to one participant reads through `ParticipantObservationReader<TEntry, TParticipantId>` instead
of repeating the check in every place that drains.

```csharp
public readonly record struct Observation(ObservationKind Kind, ParticipantId Perceiver, UnitMovedEntry Moved, UnitDiedEntry Died)
    : IObservationParticipation<ParticipantId>;
```

A positional `Perceiver` parameter already satisfies the interface, so implementing it adds no member.

```csharp
_observations = new ParticipantObservationReader<Observation, ParticipantId>(log, participant, nameof(UnitSpritePresenter));
```

`TryRead` then yields only the entries that participant perceived. The rest are drained and passed over rather
than left behind, so a reader bound to a participant who perceives nothing for a while still lets the log trim.
Dispose it with the consumer, as with any reader.

## Bounds and stalls

The buffer starts small and doubles towards the maximum capacity given to the constructor. It never shrinks,
so it settles at the high-water mark of its bursts and is reclaimed when the log's scope dies.

Maximum capacity is a stall detector, not a working size. Pick a number far past any legitimate burst, knowing
a slot costs the size of `TEntry`. A reader that stops draining holds the trim point where it stopped; once the
log fills, appending throws and names the reader that stopped and how far behind it is. Dropping the oldest
entry instead would hide exactly that failure.

```text
Observation log reached its maximum capacity of 4096 entries. Reader 'UnitSpritePresenter' stopped draining
4096 entries ago.
```

## Registration

Construct the log, then register it alongside a writer over it:

```csharp
ObservationLog<Observation> log = new ObservationLog<Observation>(4096);
services.AddSingleton(log);
services.AddSingleton(new ObservationWriter<Observation>(log));
```

Register it in the scope it belongs to. A log registered in a stage's child provider dies with that stage, and
nothing carries into the next one.
