namespace Pixely.Input;

public interface IGamepadService
{
    IReadOnlyCollection<Gamepad> Gamepads { get; }

    event InputEventHandler<GamepadStickEventArgs> LeftStickMotion;
    event InputEventHandler<GamepadStickEventArgs> RightStickMotion;
    event InputEventHandler<GamepadTriggerEventArgs> LeftTriggerMotion;
    event InputEventHandler<GamepadTriggerEventArgs> RightTriggerMotion;
    event InputEventHandler<GamepadButtonEventArgs> ButtonPress;
    event InputEventHandler<GamepadButtonEventArgs> ButtonRelease;
    event GamepadConnectionEventHandler? GamepadConnected;
    event GamepadConnectionEventHandler? GamepadDisconnected;

    void SubscribeLeftStickMotion(int order, InputEventHandler<GamepadStickEventArgs> handler);
    void SubscribeRightStickMotion(int order, InputEventHandler<GamepadStickEventArgs> handler);
    void SubscribeLeftTriggerMotion(int order, InputEventHandler<GamepadTriggerEventArgs> handler);
    void SubscribeRightTriggerMotion(int order, InputEventHandler<GamepadTriggerEventArgs> handler);
    void SubscribeButtonPress(int order, InputEventHandler<GamepadButtonEventArgs> handler);
    void SubscribeButtonRelease(int order, InputEventHandler<GamepadButtonEventArgs> handler);
}
