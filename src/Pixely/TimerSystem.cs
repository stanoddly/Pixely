using Pixely.Collections;

namespace Pixely;

public class TimerTag
{
    private TimerTag() { }
}

public readonly struct TimerCallback
{
    public readonly TimeSpan TriggerTime;
    public readonly Action Action;

    public TimerCallback(TimeSpan triggerTime, Action action)
    {
        TriggerTime = triggerTime;
        Action = action;
    }
}

public class TimerSystem : IUpdatable
{
    private readonly FrameContext _frameContext;
    private DenseSlotMapStruct<Handle<TimerTag>, TimerCallback> _timers = new();
    private readonly List<Handle<TimerTag>> _toRemove = new();

    public TimerSystem(FrameContext frameContext)
    {
        _frameContext = frameContext;
    }

    public int UpdateOrder => UpdateOrders.Default;

    public void Update()
    {
        _toRemove.Clear();
        
        ReadOnlySpan<Handle<TimerTag>> handles = _timers.Handles;
        ReadOnlySpan<TimerCallback> callbacks = _timers.Values1;
        
        for (int i = 0; i < handles.Length; i++)
        {
            TimerCallback callback = callbacks[i];
            if (_frameContext.ElapsedTime >= callback.TriggerTime)
            {
                callback.Action();
                _toRemove.Add(handles[i]);
            }
        }
        
        foreach (Handle<TimerTag> handle in _toRemove)
        {
            _timers.Remove(handle);
        }
    }

    public Handle<TimerTag> Schedule(TimeSpan delay, Action action)
    {
        TimeSpan triggerTime = _frameContext.ElapsedTime + delay;
        TimerCallback callback = new(triggerTime, action);
        return _timers.Add(callback);
    }

    public void Cancel(Handle<TimerTag> handle)
    {
        _timers.Remove(handle);
    }

    public bool Reschedule(Handle<TimerTag> handle, TimeSpan delay)
    {
        if (!_timers.TryGetValue1(handle, out TimerCallback existing))
        {
            return false;
        }

        TimeSpan newTriggerTime = _frameContext.ElapsedTime + delay;
        TimerCallback updated = new(newTriggerTime, existing.Action);
        _timers.Set(handle, updated);
        return true;
    }
}