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

A fourth, `ParticipantObservationReader<TEntry, TParticipantId>`, holds a position of its own and hands on only
what one participant perceived. See [Reading as one participant](#reading-as-one-participant).

A fifth, `ObservationSnapshot<TEntry>`, is the log and its readers as data, so a run can be saved and resumed.
See [Saving and resuming](#saving-and-resuming).

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
the same closed type and the container resolves by type. Constructing it also lets the consumer name it. The
name has to be unique within the log, because it identifies the reader in a stall message and in a save; see
[Saving and resuming](#saving-and-resuming).

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
_observations = new ParticipantObservationReader<Observation, ParticipantId>(log, participant, $"{nameof(UnitSpritePresenter)}:{participant}");
```

The name follows the same rules as any reader's, and the participant is not part of it unless the game puts it
there. One consumer type reading for several participants on one log needs a name per participant, as above, and
the game decides how the participant prints in it.

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

## Saving and resuming

`ObservationSnapshot<TEntry>` is the whole log as data: the entries no reader has passed yet and how many of
them each reader had already read.

```csharp
ObservationSnapshot<Observation> snapshot = ObservationSnapshot<Observation>.Capture(log);
```

Capturing consumes nothing and moves no reader, so a save leaves the run it saved untouched.

The log never looks inside `TEntry`, so it writes no bytes either. The snapshot is a record of an
`IReadOnlyList<TEntry>` and an `IReadOnlyDictionary<string, int>`, and the game writes it with the serializer it
already uses. A position is an offset into the saved entries: 0 means the reader had read none of them, the count
means all of them. Sequence numbers never leave the log, so a restored log counts from zero. It round-trips through `System.Text.Json` as it stands, as long as `TEntry` does.

On load, restore the log in place of constructing one. Consumers do not change: each still constructs its own
reader under its own name, and the restored log puts that reader back where it stopped.

```csharp
ObservationLog<Observation> log = ObservationLog<Observation>.Restore(4096, snapshot);

// in the consumer, the same line as on a first run
_observations = new ObservationReader<Observation>(log, nameof(UnitSpritePresenter));
```

A reader name is an identity, then, not just a label in an error message. Constructing a second reader under a
name the log already has throws. A name the snapshot does not know starts after the restored entries, which is
what a consumer added since the save should do.

`Restore` throws when the snapshot holds more entries than the maximum capacity allows, and when a reader
position is negative or past the end of the entries. A save that cannot be resumed as it was says so
rather than dropping entries quietly.

`ParticipantObservationReader<TEntry, TParticipantId>` resumes the same way, under its own name. Its position
counts every entry it drained, including the entries its participant did not perceive.

### A reader that never comes back

A saved position holds the trim point until its reader is constructed, exactly as the reader itself would.
Order therefore does not matter on load: another consumer can drain everything before a reader is constructed,
and that reader still resumes where it stopped.

The cost is that a consumer dropped from the game keeps holding the log. Nothing ever claims its position, the
log fills, and the message names it:

```text
Observation log reached its maximum capacity of 4096 entries. Reader 'UnitSpritePresenter' was restored from a
save but never constructed.
```

The snapshot is data, so drop that name before restoring:

```csharp
Dictionary<string, int> positions = new(snapshot.ReaderPositions);
positions.Remove(nameof(UnitSpritePresenter));
snapshot = snapshot with { ReaderPositions = positions };
```

## Registration

Construct the log, or restore it from a save, then register it alongside a writer over it:

```csharp
ObservationLog<Observation> log = new ObservationLog<Observation>(4096);
services.AddSingleton(log);
services.AddSingleton(new ObservationWriter<Observation>(log));
```

Register it in the scope it belongs to. A log registered in a stage's child provider dies with that stage, and
nothing carries into the next one.
