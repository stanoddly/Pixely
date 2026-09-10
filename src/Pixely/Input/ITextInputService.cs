namespace Pixely.Input;

public interface ITextInputService
{
    bool IsActiveFor(ViewScope viewScope = default);
    void Start(ViewScope viewScope = default);
    void Stop(ViewScope viewScope = default);

    event InputEventHandler<TextInputEventArgs> TextInput;
    event InputEventHandler<TextEditingEventArgs> TextEditing;

    void SubscribeTextInput(int order, InputEventHandler<TextInputEventArgs> handler);
    void SubscribeTextEditing(int order, InputEventHandler<TextEditingEventArgs> handler);
    void SubscribeTextInput(ViewScope viewScope, int order, InputEventHandler<TextInputEventArgs> handler);
    void SubscribeTextEditing(ViewScope viewScope, int order, InputEventHandler<TextEditingEventArgs> handler);
}
