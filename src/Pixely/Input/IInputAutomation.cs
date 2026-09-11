using System.Numerics;

namespace Pixely.Input;

public interface IInputAutomation
{
    void MouseMoveTo(Vector2 windowPosition, ViewScope viewScope = default);
    void MouseMoveBy(Vector2 delta, ViewScope viewScope = default);
    void MouseDown(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default);
    void MouseUp(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default);
    void MouseClick(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default);
    void MouseWheel(Vector2 delta, Vector2 windowPosition, ViewScope viewScope = default);
    void KeyDown(Scancode scancode, ViewScope viewScope = default);
    void KeyUp(Scancode scancode, ViewScope viewScope = default);
    void KeyPress(Scancode scancode, ViewScope viewScope = default);
    void TextInput(string text, ViewScope viewScope = default);
}
