using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MidiControl.Core.Models;

public sealed class MidiMapping : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private bool _isEnabled = true;
    private string _name = "New Mapping";
    private int _inputChannel = 1;
    private int _inputNote = 60;
    private int _minimumVelocity = 1;
    private int _outputChannel = 1;
    private int _outputController = 20;
    private int _outputValue = 127;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public int InputChannel
    {
        get => _inputChannel;
        set => SetField(ref _inputChannel, value);
    }

    public int InputNote
    {
        get => _inputNote;
        set
        {
            if (SetField(ref _inputNote, value))
            {
                OnPropertyChanged(nameof(InputNoteName));
            }
        }
    }

    public string InputNoteName => GetNoteName(InputNote);

    public int MinimumVelocity
    {
        get => _minimumVelocity;
        set => SetField(ref _minimumVelocity, value);
    }

    public int OutputChannel
    {
        get => _outputChannel;
        set => SetField(ref _outputChannel, value);
    }

    public int OutputController
    {
        get => _outputController;
        set => SetField(ref _outputController, value);
    }

    public int OutputValue
    {
        get => _outputValue;
        set => SetField(ref _outputValue, value);
    }

    public MidiMapping Clone()
    {
        return new MidiMapping
        {
            Id = Id,
            IsEnabled = IsEnabled,
            Name = Name,
            InputChannel = InputChannel,
            InputNote = InputNote,
            MinimumVelocity = MinimumVelocity,
            OutputChannel = OutputChannel,
            OutputController = OutputController,
            OutputValue = OutputValue
        };
    }

    private static string GetNoteName(int noteNumber)
    {
        string[] noteNames =
        [
            "C", "C#", "D", "D#", "E", "F",
            "F#", "G", "G#", "A", "A#", "B"
        ];

        if (noteNumber is < 0 or > 127)
        {
            return string.Empty;
        }

        return $"{noteNames[noteNumber % 12]}{noteNumber / 12 - 1}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
