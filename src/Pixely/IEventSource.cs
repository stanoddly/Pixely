namespace Pixely;

// What a frame takes from the platform before any update: input, window changes and quit requests. EventService reads them from
// SDL; Pixely.Testing's stub raises what its windows queued.
internal interface IEventSource
{
    void Process();
}
