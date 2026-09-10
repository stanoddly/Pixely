namespace Pixely.Input;

public interface IKeyboardService
{
    event InputEventHandler<KeyEventArgs> KeyDown;
    event InputEventHandler<KeyEventArgs> KeyUp;

    void SubscribeKeyDown(int order, InputEventHandler<KeyEventArgs> handler);
    void SubscribeKeyUp(int order, InputEventHandler<KeyEventArgs> handler);
    void SubscribeKeyDown(ViewScope viewScope, int order, InputEventHandler<KeyEventArgs> handler);
    void SubscribeKeyUp(ViewScope viewScope, int order, InputEventHandler<KeyEventArgs> handler);
}
