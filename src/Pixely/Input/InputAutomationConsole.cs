using System.Collections.Concurrent;
using Pixely.Content;
using Pixely.RenderOrchestration;

namespace Pixely.Input;

/// <summary>
/// Reads command lines from a text stream on a background thread and runs them on the frame loop, one reply line per command.
/// </summary>
internal sealed class InputAutomationConsole : IUpdatable
{
    private readonly InputAutomationCommandInterpreter _interpreter;
    private readonly TextReader _input;
    private readonly ConcurrentQueue<string> _pendingLines = new();
    private Thread? _readerThread;

    internal InputAutomationConsole(IInputAutomation automation, IFrameCapture? frameCapture, IImageWriter imageWriter, TextReader input, TextWriter output)
    {
        _interpreter = new InputAutomationCommandInterpreter(automation, frameCapture, imageWriter, output.WriteLine);
        _input = input;
    }

    public int UpdateOrder => UpdateOrders.Input;

    public void Update()
    {
        _readerThread ??= StartReader();

        // Only what was queued when the frame started, so a fast writer cannot hold the frame.
        int pendingCount = _pendingLines.Count;
        for (int i = 0; i < pendingCount && _pendingLines.TryDequeue(out string? line); i++)
        {
            _interpreter.Execute(line);
        }
    }

    private Thread StartReader()
    {
        Thread thread = new(ReadLines) { IsBackground = true, Name = "Pixely input automation reader" };
        thread.Start();
        return thread;
    }

    private void ReadLines()
    {
        while (_input.ReadLine() is { } line)
        {
            _pendingLines.Enqueue(line);
        }
    }
}
