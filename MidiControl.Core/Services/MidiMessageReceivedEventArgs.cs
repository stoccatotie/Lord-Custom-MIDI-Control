namespace MidiControl.Core.Services;

public sealed class MidiMessageReceivedEventArgs : EventArgs
{
    public MidiMessageReceivedEventArgs(
        DateTime timestamp,
        string direction,
        string messageType,
        int? channel,
        string data)
    {
        Timestamp = timestamp;
        Direction = direction;
        MessageType = messageType;
        Channel = channel;
        Data = data;
    }

    public DateTime Timestamp { get; }

    public string Direction { get; }

    public string MessageType { get; }

    public int? Channel { get; }

    public string Data { get; }
}
