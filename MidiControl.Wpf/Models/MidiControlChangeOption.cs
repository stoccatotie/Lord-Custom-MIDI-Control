namespace MidiControl.Wpf.Models;

public sealed class MidiControlChangeOption
{
    public MidiControlChangeOption(int number, string name)
    {
        Number = number;
        Name = name;
        DisplayName = $"CC {number} — {name}";
    }

    public int Number { get; }

    public string Name { get; }

    public string DisplayName { get; }
}
