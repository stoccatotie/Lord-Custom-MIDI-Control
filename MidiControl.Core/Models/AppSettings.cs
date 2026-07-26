namespace MidiControl.Core.Models;

public sealed class AppSettings
{
    public string? InputDeviceName { get; set; }

    public string? OutputDeviceName { get; set; }

    public List<MidiMapping> Mappings { get; set; } = new();
}
