namespace MidiControl.Core.Models;

public sealed class AppSettings
{
    public bool AutoConnect { get; set; }

    public string? InputDeviceName { get; set; }

    public string? OutputDeviceName { get; set; }

    public List<MidiMapping> Mappings { get; set; } = new();
}
