namespace MidiControl.Wpf;

public sealed record MidiLogEntry(
    string Time,
    string Direction,
    string Message,
    string Channel,
    string Data,
    string Mapping);
