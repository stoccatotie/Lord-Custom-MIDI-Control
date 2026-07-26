namespace MidiControl.Core.Services;

public sealed class MidiConnectionErrorEventArgs : EventArgs
{
    public MidiConnectionErrorEventArgs(DateTime timestamp, string message)
    {
        Timestamp = timestamp;
        Message = message;
    }

    public DateTime Timestamp { get; }

    public string Message { get; }
}
