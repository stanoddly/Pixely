using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// Records what the platform was told to do about text input, which is the part of the bridge worth
/// pinning: everything else it does is forwarding, but starting and stopping is a decision.
/// </summary>
internal sealed class FakeTextInputService : ITextInputService
{
    public List<string> Calls { get; } = new();

    public bool IsActive => Calls.Count > 0 && Calls[^1] == "start";

    public void Start() => Calls.Add("start");

    public void Stop() => Calls.Add("stop");

    /// <summary>What subscribed for committed text, so a test can deliver some.</summary>
    public InputEventHandler<TextInputEventArgs>? TextInputHandler { get; private set; }

    /// <summary>The order it subscribed with.</summary>
    public int? TextInputOrder { get; private set; }

    public event InputEventHandler<TextInputEventArgs>? TextInput { add { } remove { } }
    public event InputEventHandler<TextEditingEventArgs>? TextEditing { add { } remove { } }

    public void SubscribeTextInput(int order, InputEventHandler<TextInputEventArgs> handler)
    {
        TextInputHandler = handler;
        TextInputOrder = order;
    }

    public void SubscribeTextEditing(int order, InputEventHandler<TextEditingEventArgs> handler) { }
}

/// <summary>Keeps what subscribed to key presses, so a test can deliver one.</summary>
internal sealed class FakeKeyboardService : IKeyboardService
{
    public InputEventHandler<KeyEventArgs>? KeyDownHandler { get; private set; }

    /// <summary>The order it subscribed with.</summary>
    public int? KeyDownOrder { get; private set; }

    public event InputEventHandler<KeyEventArgs>? KeyDown { add { } remove { } }
    public event InputEventHandler<KeyEventArgs>? KeyUp { add { } remove { } }

    public void SubscribeKeyDown(int order, InputEventHandler<KeyEventArgs> handler)
    {
        KeyDownHandler = handler;
        KeyDownOrder = order;
    }

    public void SubscribeKeyUp(int order, InputEventHandler<KeyEventArgs> handler) { }
}

/// <summary>Keeps what subscribed to the wheel, so a test can deliver one; everything else is dropped.</summary>
internal sealed class FakeMouseService : IMouseService
{
    public InputEventHandler<MouseWheelEventArgs>? WheelHandler { get; private set; }

    /// <summary>The order it subscribed with.</summary>
    public int? WheelOrder { get; private set; }

    public event InputEventHandler<MouseButtonEventArgs>? ButtonPress { add { } remove { } }
    public event InputEventHandler<MouseButtonEventArgs>? ButtonRelease { add { } remove { } }
    public event InputEventHandler<MouseMotionEventArgs>? Motion { add { } remove { } }
    public event InputEventHandler<MouseWheelEventArgs>? Wheel { add { } remove { } }
    public event InputEventHandler<MouseWindowPresenceEventArgs>? WindowEnter { add { } remove { } }
    public event InputEventHandler<MouseWindowPresenceEventArgs>? WindowLeave { add { } remove { } }

    public bool IsInWindow() => true;

    public MouseState GetGlobalState() => default;

    public void SubscribeButtonPress(int order, InputEventHandler<MouseButtonEventArgs> handler) { }
    public void SubscribeButtonRelease(int order, InputEventHandler<MouseButtonEventArgs> handler) { }
    public void SubscribeMotion(int order, InputEventHandler<MouseMotionEventArgs> handler) { }
    public void SubscribeWheel(int order, InputEventHandler<MouseWheelEventArgs> handler)
    {
        WheelHandler = handler;
        WheelOrder = order;
    }
    public void SubscribeWindowEnter(int order, InputEventHandler<MouseWindowPresenceEventArgs> handler) { }
    public void SubscribeWindowLeave(int order, InputEventHandler<MouseWindowPresenceEventArgs> handler) { }
}
