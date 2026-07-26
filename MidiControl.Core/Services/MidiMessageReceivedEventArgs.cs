namespace MidiControl.Core.Services;

public sealed class MidiMessageReceivedEventArgs : EventArgs
{
    public MidiMessageReceivedEventArgs(
        DateTime timestamp,
        string direction,
        string messageType,
        int? channel,
        string data,
        string? mappingName = null,
        int? noteNumber = null,
        int? velocity = null,
        bool isNoteOn = false)
    {
        Timestamp = timestamp;
        Direction = direction;
        MessageType = messageType;
        Channel = channel;
        Data = data;
        MappingName = mappingName;
        NoteNumber = noteNumber;
        Velocity = velocity;
        IsNoteOn = isNoteOn;
    }

    public DateTime Timestamp { get; }

    public string Direction { get; }

    public string MessageType { get; }

    public int? Channel { get; }

    public string Data { get; }

    public string? MappingName { get; }

    public int? NoteNumber { get; }

    public int? Velocity { get; }

    public bool IsNoteOn { get; }
}
