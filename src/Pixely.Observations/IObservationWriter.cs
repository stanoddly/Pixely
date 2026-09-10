namespace Pixely.Observations;

/// <summary>
/// The write half of an <see cref="ObservationLog{TEntry}"/>. Inject it where rules append and nothing reads.
/// </summary>
public interface IObservationWriter<TEntry>
{
    void Append(in TEntry entry);
}
