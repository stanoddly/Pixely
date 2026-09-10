# Observations

`ObservationLog<TEntry>` in `Pixely.Observations` is an append-only log of value entries that readers drain at their
own pace. Rules append; nothing is called back. A reader holds a cursor, reads when it suits its own point in
the frame, and the log drops an entry once every cursor has passed it.

Use it for a transition that two reads of game state one frame apart cannot derive: a unit that walked a path
and arrived, one that appeared and was gone again, an action that resolved and was reversed inside the same
frame. State answers what is true now, and a rule can resolve many transitions between two reads, so any
"most recent transition" field is overwritten before a reader looks at it.

## The entry type

One log carries one entry type. To carry several kinds of entry in one order, make `TEntry` a tagged value
type; the log never looks inside it.

```csharp
public readonly record struct UnitMovedEntry(UnitId Unit, TilePoint From, TilePoint To);
public readonly record struct UnitDiedEntry(UnitId Unit);

public enum ObservationKind { UnitMoved, UnitDied }

public readonly record struct Observation(ObservationKind Kind, ParticipantId Perceiver, UnitMovedEntry Moved, UnitDiedEntry Died);
```

Entries are past-tense records of ids and value types, never a live reference into game state. Nothing
allocates per entry: the log stores `TEntry` in an array, `Append` takes it by `in`, and `TryRead` copies it out.

## Writing

Inject `IObservationWriter<TEntry>` where rules append, so a writer cannot read:

```csharp
internal sealed class MoveMechanic
{
    private readonly IObservationWriter<Observation> _observations;

    internal MoveMechanic(IObservationWriter<Observation> observations) => _observations = observations;

    internal void Move(UnitId unit, TilePoint destination)
    {
        // mutate state, then record what happened
        _observations.Append(new Observation(ObservationKind.UnitMoved, perceiver, moved, default));
    }
}
```

## Reading

Inject `IObservationLog<TEntry>`, create a cursor named after the reader, and drain it in the reader's own
update. Dispose the cursor with the reader:

```csharp
internal sealed class UnitSpritePresenter : IUpdatable, IDisposable
{
    private readonly ObservationCursor<Observation> _observations;

    internal UnitSpritePresenter(IObservationLog<Observation> observations)
    {
        _observations = observations.CreateCursor(nameof(UnitSpritePresenter));
    }

    public void Update()
    {
        while (_observations.TryRead(out Observation observation))
        {
            // skip what another participant perceived, then switch on the tag
        }
    }

    public void Dispose() => _observations.Dispose();
}
```

A cursor starts positioned after the last appended entry, so a reader created part-way through a run sees
only what is appended from then on. Cursors read independently: entries appended this frame may be drained by
one reader now and by another several frames later.

Addressing an entry to a subset of readers is the game's business, not the log's. Carry the perceiver in
`TEntry` and have the reader skip what it did not perceive. A reader bound to one participant should wrap its
cursor once rather than repeat the check in every consumer.

## Bounds and stalls

The buffer starts small and doubles towards the maximum capacity given to the constructor. It never shrinks,
so it settles at the high-water mark of its bursts and is reclaimed when the log's scope dies.

Maximum capacity is a stall detector, not a working size. Pick a number far past any legitimate burst, knowing
a slot costs the size of `TEntry`. A reader that stops draining holds the trim point where it stopped; once the
log fills, `Append` throws and names the cursor that stopped and how far behind it is. Dropping the oldest
entry instead would hide exactly that failure.

```text
Observation log reached its maximum capacity of 4096 entries. Cursor 'UnitSpritePresenter' stopped draining
4096 entries ago.
```

## Registration

The log takes its capacity as a constructor argument, so register the instance and alias the two halves:

```csharp
services.AddSingleton(new ObservationLog<Observation>(4096));
services.AddAlias<IObservationWriter<Observation>, ObservationLog<Observation>>();
services.AddAlias<IObservationLog<Observation>, ObservationLog<Observation>>();
```

Register it in the scope it belongs to. A log registered in a stage's child provider dies with that stage, and
nothing carries into the next one.
