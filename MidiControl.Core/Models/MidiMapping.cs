namespace MidiControl.Core.Models;

public sealed class MidiMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public bool IsEnabled { get; set; } = true;

    public string Name { get; set; } = "New Mapping";

    public int InputChannel { get; set; } = 1;

    public int InputNote { get; set; } = 60;

    public int MinimumVelocity { get; set; } = 1;

    public int OutputChannel { get; set; } = 1;

    public int OutputController { get; set; } = 20;

    public int OutputValue { get; set; } = 127;
}
