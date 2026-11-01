namespace Pixely.Input;

public interface ITextInputService
{
    bool IsActive { get; }
    void Start();
    void Stop();

    event InputEventHandler<TextInputEventArgs> TextInput;
    event InputEventHandler<TextEditingEventArgs> TextEditing;

    void SubscribeTextInput(int order, InputEventHandler<TextInputEventArgs> handler);
    void SubscribeTextEditing(int order, InputEventHandler<TextEditingEventArgs> handler);
}
