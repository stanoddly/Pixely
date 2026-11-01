using SDL;

namespace Pixely.Input;

public class TextInputEventArgs : ConsumableInputEventArgs
{
    public string Text { get; internal set; } = string.Empty;
    public ulong Timestamp { get; internal set; }
}

public class TextEditingEventArgs : ConsumableInputEventArgs
{
    public string Text { get; internal set; } = string.Empty;
    public int Start { get; internal set; }
    public int Length { get; internal set; }
    public ulong Timestamp { get; internal set; }
}

public class TextInputService : ITextInputService
{
    private readonly Window _window;

    private readonly TextInputEventArgs _textInputEventArgs = new();
    private readonly TextEditingEventArgs _textEditingEventArgs = new();
    private readonly OrderedEventHandlers<TextInputEventArgs> _textInputHandlers = new();
    private readonly OrderedEventHandlers<TextEditingEventArgs> _textEditingHandlers = new();

    public bool IsActive
    {
        get
        {
            unsafe
            {
                return SDL3.SDL_TextInputActive(_window.SdlWindow);
            }
        }
    }

    public event InputEventHandler<TextInputEventArgs> TextInput
    {
        add => _textInputHandlers.Add(0, value);
        remove => _textInputHandlers.Remove(value);
    }

    public event InputEventHandler<TextEditingEventArgs> TextEditing
    {
        add => _textEditingHandlers.Add(0, value);
        remove => _textEditingHandlers.Remove(value);
    }

    public void SubscribeTextInput(int order, InputEventHandler<TextInputEventArgs> handler)
    {
        _textInputHandlers.Add(order, handler);
    }

    public void SubscribeTextEditing(int order, InputEventHandler<TextEditingEventArgs> handler)
    {
        _textEditingHandlers.Add(order, handler);
    }

    public void Start()
    {
        unsafe
        {
            SDL3.SDL_StartTextInput(_window.SdlWindow);
        }
    }

    public void Stop()
    {
        unsafe
        {
            SDL3.SDL_StopTextInput(_window.SdlWindow);
        }
    }

    internal TextInputService(Window window)
    {
        _window = window;
    }

    internal void OnTextInputEvent(string text, ulong timestamp)
    {
        _textInputEventArgs.Text = text;
        _textInputEventArgs.Timestamp = timestamp;
        _textInputHandlers.Invoke(_textInputEventArgs);
    }

    internal void OnTextEditingEvent(string text, int start, int length, ulong timestamp)
    {
        _textEditingEventArgs.Text = text;
        _textEditingEventArgs.Start = start;
        _textEditingEventArgs.Length = length;
        _textEditingEventArgs.Timestamp = timestamp;
        _textEditingHandlers.Invoke(_textEditingEventArgs);
    }
}
