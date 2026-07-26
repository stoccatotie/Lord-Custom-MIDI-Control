using System.Collections.ObjectModel;

namespace MidiControl.Wpf.Models;

public static class MidiControlChangeCatalog
{
    private static readonly IReadOnlyDictionary<int, string> KnownNames =
        new Dictionary<int, string>
        {
            [0] = "Bank Select MSB",
            [1] = "Modulation Wheel",
            [2] = "Breath Controller",
            [4] = "Foot Controller",
            [5] = "Portamento Time",
            [6] = "Data Entry MSB",
            [7] = "Channel Volume",
            [8] = "Balance",
            [10] = "Pan",
            [11] = "Expression Controller",
            [12] = "Effect Control 1",
            [13] = "Effect Control 2",
            [16] = "General Purpose Controller 1",
            [17] = "General Purpose Controller 2",
            [18] = "General Purpose Controller 3",
            [19] = "General Purpose Controller 4",
            [32] = "Bank Select LSB",
            [38] = "Data Entry LSB",
            [64] = "Sustain Pedal",
            [65] = "Portamento",
            [66] = "Sostenuto",
            [67] = "Soft Pedal",
            [68] = "Legato Footswitch",
            [69] = "Hold 2",
            [70] = "Sound Variation",
            [71] = "Resonance",
            [72] = "Release Time",
            [73] = "Attack Time",
            [74] = "Brightness",
            [75] = "Sound Controller 6",
            [76] = "Sound Controller 7",
            [77] = "Sound Controller 8",
            [78] = "Sound Controller 9",
            [79] = "Sound Controller 10",
            [80] = "General Purpose Controller 5",
            [81] = "General Purpose Controller 6",
            [82] = "General Purpose Controller 7",
            [83] = "General Purpose Controller 8",
            [84] = "Portamento Control",
            [91] = "Reverb Send Level",
            [92] = "Tremolo Depth",
            [93] = "Chorus Send Level",
            [94] = "Celeste Depth",
            [95] = "Phaser Depth",
            [96] = "Data Increment",
            [97] = "Data Decrement",
            [98] = "NRPN LSB",
            [99] = "NRPN MSB",
            [100] = "RPN LSB",
            [101] = "RPN MSB",
            [120] = "All Sound Off",
            [121] = "Reset All Controllers",
            [122] = "Local Control",
            [123] = "All Notes Off",
            [124] = "Omni Mode Off",
            [125] = "Omni Mode On",
            [126] = "Mono Mode On",
            [127] = "Poly Mode On"
        };

    public static IReadOnlyList<MidiControlChangeOption> Options { get; } =
        new ReadOnlyCollection<MidiControlChangeOption>(
            Enumerable.Range(0, 128)
                .Select(number => new MidiControlChangeOption(
                    number,
                    KnownNames.TryGetValue(number, out var name) ? name : "Undefined"))
                .ToList());
}
