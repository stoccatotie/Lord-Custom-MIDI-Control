namespace MidiControl.Core.Services;

public sealed class MidiMessageReceivedEventArgs : EventArgs
{
    public MidiMessageReceivedEventArgs(
        DateTime timestamp,
        string direction,
        string messageType,
        int? channel,
        string data,
        string? mappingName = null)
    {
        Timestamp = timestamp;
        Direction = direction;
        MessageType = messageType;
        Channel = channel;
        Data = data;
        MappingName = mappingName;
    }

    public DateTime Timestamp { get; }

    public string Direction { get; }

    public string MessageType { get; }

    public int? Channel { get; }

    public string Data { get; }

    public string? MappingName { get; }
}
