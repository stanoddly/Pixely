namespace Pixely.Observations;

/// <summary>
/// Appends to an <see cref="ObservationLog{TEntry}"/>. Inject it where rules record what happened, so nothing
/// that writes can also read.
/// </summary>
public sealed class ObservationWriter<TEntry>
{
    private readonly ObservationLog<TEntry> _log;

    public ObservationWriter(ObservationLog<TEntry> log)
    {
        _log = log;
    }

    public void Append(in TEntry entry)
    {
        _log.Append(entry);
    }
}
