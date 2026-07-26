namespace MidiControl.Core.Services;

public sealed class MidiLearnCapturedEventArgs : EventArgs
{
    public MidiLearnCapturedEventArgs(
        int inputChannel,
        int inputNote,
        int velocity,
        DateTime timestamp)
    {
        InputChannel = inputChannel;
        InputNote = inputNote;
        Velocity = velocity;
        Timestamp = timestamp;
    }

    public int InputChannel { get; }

    public int InputNote { get; }

    public int Velocity { get; }

    public DateTime Timestamp { get; }
}
