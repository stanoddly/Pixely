using System.Runtime.InteropServices;
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
    private readonly WindowRegistry _windowRegistry;

    private readonly TextInputEventArgs _textInputEventArgs = new();
    private readonly TextEditingEventArgs _textEditingEventArgs = new();
    private readonly ViewScopedOrderedEventHandlers<TextInputEventArgs> _textInputHandlers = new();
    private readonly ViewScopedOrderedEventHandlers<TextEditingEventArgs> _textEditingHandlers = new();

    public bool IsActiveFor(ViewScope viewScope = default)
    {
        Window window = _windowRegistry.GetWindow(viewScope);
        unsafe
        {
            return SDL3.SDL_TextInputActive(window.SdlWindow);
        }
    }

    public event InputEventHandler<TextInputEventArgs> TextInput
    {
        add => _textInputHandlers.Add(default, 0, value);
        remove => _textInputHandlers.Remove(default, value);
    }

    public event InputEventHandler<TextEditingEventArgs> TextEditing
    {
        add => _textEditingHandlers.Add(default, 0, value);
        remove => _textEditingHandlers.Remove(default, value);
    }

    public void SubscribeTextInput(int order, InputEventHandler<TextInputEventArgs> handler)
    {
        _textInputHandlers.Add(default, order, handler);
    }

    public void SubscribeTextEditing(int order, InputEventHandler<TextEditingEventArgs> handler)
    {
        _textEditingHandlers.Add(default, order, handler);
    }

    public void SubscribeTextInput(ViewScope viewScope, int order, InputEventHandler<TextInputEventArgs> handler)
    {
        _textInputHandlers.Add(viewScope, order, handler);
    }

    public void SubscribeTextEditing(ViewScope viewScope, int order, InputEventHandler<TextEditingEventArgs> handler)
    {
        _textEditingHandlers.Add(viewScope, order, handler);
    }

    public void Start(ViewScope viewScope = default)
    {
        Window window = _windowRegistry.GetWindow(viewScope);
        unsafe
        {
            SDL3.SDL_StartTextInput(window.SdlWindow);
        }
    }

    public void Stop(ViewScope viewScope = default)
    {
        Window window = _windowRegistry.GetWindow(viewScope);
        unsafe
        {
            SDL3.SDL_StopTextInput(window.SdlWindow);
        }
    }

    internal TextInputService(WindowRegistry windowRegistry)
    {
        _windowRegistry = windowRegistry;
    }

    internal void OnTextInputEvent(
        ViewScope viewScope,
        in SDL_TextInputEvent textInputEvent)
    {
        string text;
        unsafe
        {
            text = Marshal.PtrToStringUTF8((IntPtr)textInputEvent.text) ?? string.Empty;
        }

        _textInputEventArgs.Text = text;
        _textInputEventArgs.Timestamp = textInputEvent.timestamp;
        _textInputHandlers.Invoke(viewScope, _textInputEventArgs);
    }

    internal void OnTextEditingEvent(
        ViewScope viewScope,
        in SDL_TextEditingEvent textEditingEvent)
    {
        string text;
        unsafe
        {
            text = Marshal.PtrToStringUTF8((IntPtr)textEditingEvent.text) ?? string.Empty;
        }

        _textEditingEventArgs.Text = text;
        _textEditingEventArgs.Start = textEditingEvent.start;
        _textEditingEventArgs.Length = textEditingEvent.length;
        _textEditingEventArgs.Timestamp = textEditingEvent.timestamp;
        _textEditingHandlers.Invoke(viewScope, _textEditingEventArgs);
    }
}
