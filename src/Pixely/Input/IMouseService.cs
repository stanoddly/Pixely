namespace Pixely.Input;

public interface IMouseService
{
    event InputEventHandler<MouseButtonEventArgs> ButtonPress;
    event InputEventHandler<MouseButtonEventArgs> ButtonRelease;
    event InputEventHandler<MouseMotionEventArgs> Motion;
    event InputEventHandler<MouseWheelEventArgs> Wheel;
    event InputEventHandler<MouseWindowPresenceEventArgs> WindowEnter;
    event InputEventHandler<MouseWindowPresenceEventArgs> WindowLeave;

    bool IsInWindow();

    MouseState GetGlobalState();

    void SubscribeButtonPress(int order, InputEventHandler<MouseButtonEventArgs> handler);
    void SubscribeButtonRelease(int order, InputEventHandler<MouseButtonEventArgs> handler);
    void SubscribeMotion(int order, InputEventHandler<MouseMotionEventArgs> handler);
    void SubscribeWheel(int order, InputEventHandler<MouseWheelEventArgs> handler);
    void SubscribeWindowEnter(int order, InputEventHandler<MouseWindowPresenceEventArgs> handler);
    void SubscribeWindowLeave(int order, InputEventHandler<MouseWindowPresenceEventArgs> handler);
}
