using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// Records what the platform was told to do about text input, which is the part of the bridge worth
/// pinning: everything else it does is forwarding, but starting and stopping is a decision.
/// </summary>
internal sealed class FakeTextInputService : ITextInputService
{
    public List<string> Calls { get; } = new();

    public bool IsActiveFor(ViewScope viewScope = default) => Calls.Count > 0 && Calls[^1].StartsWith("start", StringComparison.Ordinal);

    public void Start(ViewScope viewScope = default) => Calls.Add($"start {viewScope}");

    public void Stop(ViewScope viewScope = default) => Calls.Add($"stop {viewScope}");

    public event InputEventHandler<TextInputEventArgs>? TextInput { add { } remove { } }
    public event InputEventHandler<TextEditingEventArgs>? TextEditing { add { } remove { } }

    public void SubscribeTextInput(int priority, InputEventHandler<TextInputEventArgs> handler) { }

    public void SubscribeTextEditing(int priority, InputEventHandler<TextEditingEventArgs> handler) { }

    public void SubscribeTextInput(ViewScope viewScope, int priority, InputEventHandler<TextInputEventArgs> handler) { }

    public void SubscribeTextEditing(ViewScope viewScope, int priority, InputEventHandler<TextEditingEventArgs> handler) { }
}

/// <summary>Subscribes to nothing that happens, because these tests drive the root directly.</summary>
internal sealed class SilentKeyboardService : IKeyboardService
{
    public event InputEventHandler<KeyEventArgs>? KeyDown { add { } remove { } }
    public event InputEventHandler<KeyEventArgs>? KeyUp { add { } remove { } }

    public void SubscribeKeyDown(int priority, InputEventHandler<KeyEventArgs> handler) { }

    public void SubscribeKeyUp(int priority, InputEventHandler<KeyEventArgs> handler) { }

    public void SubscribeKeyDown(ViewScope viewScope, int priority, InputEventHandler<KeyEventArgs> handler) { }

    public void SubscribeKeyUp(ViewScope viewScope, int priority, InputEventHandler<KeyEventArgs> handler) { }
}

/// <inheritdoc cref="SilentKeyboardService"/>
internal sealed class SilentMouseService : IMouseService
{
    public event InputEventHandler<MouseButtonEventArgs>? ButtonPress { add { } remove { } }
    public event InputEventHandler<MouseButtonEventArgs>? ButtonRelease { add { } remove { } }
    public event InputEventHandler<MouseMotionEventArgs>? Motion { add { } remove { } }
    public event InputEventHandler<MouseWheelEventArgs>? Wheel { add { } remove { } }
    public event InputEventHandler<MouseWindowPresenceEventArgs>? WindowEnter { add { } remove { } }
    public event InputEventHandler<MouseWindowPresenceEventArgs>? WindowLeave { add { } remove { } }

    public bool IsInWindow(ViewScope viewScope = default) => true;

    public MouseState GetGlobalState() => default;

    public void SubscribeButtonPress(int priority, InputEventHandler<MouseButtonEventArgs> handler) { }
    public void SubscribeButtonRelease(int priority, InputEventHandler<MouseButtonEventArgs> handler) { }
    public void SubscribeMotion(int priority, InputEventHandler<MouseMotionEventArgs> handler) { }
    public void SubscribeWheel(int priority, InputEventHandler<MouseWheelEventArgs> handler) { }
    public void SubscribeWindowEnter(int priority, InputEventHandler<MouseWindowPresenceEventArgs> handler) { }
    public void SubscribeWindowLeave(int priority, InputEventHandler<MouseWindowPresenceEventArgs> handler) { }
    public void SubscribeButtonPress(ViewScope viewScope, int priority, InputEventHandler<MouseButtonEventArgs> handler) { }
    public void SubscribeButtonRelease(ViewScope viewScope, int priority, InputEventHandler<MouseButtonEventArgs> handler) { }
    public void SubscribeMotion(ViewScope viewScope, int priority, InputEventHandler<MouseMotionEventArgs> handler) { }
    public void SubscribeWheel(ViewScope viewScope, int priority, InputEventHandler<MouseWheelEventArgs> handler) { }
    public void SubscribeWindowEnter(ViewScope viewScope, int priority, InputEventHandler<MouseWindowPresenceEventArgs> handler) { }
    public void SubscribeWindowLeave(ViewScope viewScope, int priority, InputEventHandler<MouseWindowPresenceEventArgs> handler) { }
}
