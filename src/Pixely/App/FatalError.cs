namespace Pixely.App;

internal static class FatalError
{
    /// <summary>
    /// Writes an unrecoverable failure to standard error and presents it in a message box.
    /// </summary>
    /// <remarks>
    /// A windowed application usually has no console attached, so an exception that terminates the
    /// process would otherwise be invisible. The box is shown without a parent window, because the
    /// failure may have happened before a window existed or after one was destroyed.
    /// </remarks>
    public static void Report(Exception exception)
    {
        Console.Error.WriteLine(exception);

        try
        {
            MessageBox.Show(MessageBoxSeverity.Error, "Fatal error", exception.Message);
        }
        catch (Exception messageBoxException)
        {
            // presenting the failure must never replace the failure being reported
            Console.Error.WriteLine(messageBoxException);
        }
    }
}
