namespace Pixely.Input;

public interface IMouseService
{
    event InputEventHandler<MouseButtonEventArgs> ButtonPress;
    event InputEventHandler<MouseButtonEventArgs> ButtonRelease;
    event InputEventHandler<MouseMotionEventArgs> Motion;
    event InputEventHandler<MouseWheelEventArgs> Wheel;
    event InputEventHandler<MouseWindowPresenceEventArgs> WindowEnter;
    event InputEventHandler<MouseWindowPresenceEventArgs> WindowLeave;

    bool IsInWindow(ViewScope viewScope = default);

    MouseState GetGlobalState();

    void SubscribeButtonPress(int order, InputEventHandler<MouseButtonEventArgs> handler);
    void SubscribeButtonRelease(int order, InputEventHandler<MouseButtonEventArgs> handler);
    void SubscribeMotion(int order, InputEventHandler<MouseMotionEventArgs> handler);
    void SubscribeWheel(int order, InputEventHandler<MouseWheelEventArgs> handler);
    void SubscribeWindowEnter(int order, InputEventHandler<MouseWindowPresenceEventArgs> handler);
    void SubscribeWindowLeave(int order, InputEventHandler<MouseWindowPresenceEventArgs> handler);
    void SubscribeButtonPress(ViewScope viewScope, int order, InputEventHandler<MouseButtonEventArgs> handler);
    void SubscribeButtonRelease(ViewScope viewScope, int order, InputEventHandler<MouseButtonEventArgs> handler);
    void SubscribeMotion(ViewScope viewScope, int order, InputEventHandler<MouseMotionEventArgs> handler);
    void SubscribeWheel(ViewScope viewScope, int order, InputEventHandler<MouseWheelEventArgs> handler);
    void SubscribeWindowEnter(ViewScope viewScope, int order, InputEventHandler<MouseWindowPresenceEventArgs> handler);
    void SubscribeWindowLeave(ViewScope viewScope, int order, InputEventHandler<MouseWindowPresenceEventArgs> handler);
}
