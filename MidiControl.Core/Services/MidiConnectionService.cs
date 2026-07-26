using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace MidiControl.Core.Services;

public sealed class MidiConnectionService : IDisposable
{
    private const int MappedInputChannel = 0;
    private const int MappedNoteNumber = 67;
    private const int MappedOutputChannel = 0;
    private const int MappedControlNumber = 20;
    private const int MappedControlValue = 127;

    private static readonly string[] NoteNames =
    {
        "C", "C#", "D", "D#", "E", "F",
        "F#", "G", "G#", "A", "A#", "B"
    };

    private readonly object _syncRoot = new();
    private InputDevice? _inputDevice;
    private OutputDevice? _outputDevice;

    public event EventHandler<MidiMessageReceivedEventArgs>? MessageReceived;

    public bool IsRunning { get; private set; }

    public string? InputDeviceName { get; private set; }

    public string? OutputDeviceName { get; private set; }

    public void Start(string inputDeviceName, string outputDeviceName)
    {
        lock (_syncRoot)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("MIDI connection is already running.");
            }

            if (string.IsNullOrWhiteSpace(inputDeviceName))
            {
                throw new ArgumentException("MIDI input device name is required.", nameof(inputDeviceName));
            }

            if (string.IsNullOrWhiteSpace(outputDeviceName))
            {
                throw new ArgumentException("MIDI output device name is required.", nameof(outputDeviceName));
            }

            try
            {
                _inputDevice = GetInputDevice(inputDeviceName);
                _outputDevice = GetOutputDevice(outputDeviceName);

                _inputDevice.EventReceived += InputDevice_EventReceived;
                _inputDevice.StartEventsListening();

                InputDeviceName = inputDeviceName;
                OutputDeviceName = outputDeviceName;
                IsRunning = true;
            }
            catch
            {
                Stop();
                throw;
            }
        }
    }

    public void Stop()
    {
        InputDevice? inputDevice;
        OutputDevice? outputDevice;

        lock (_syncRoot)
        {
            IsRunning = false;
            inputDevice = _inputDevice;
            outputDevice = _outputDevice;
            _inputDevice = null;
            _outputDevice = null;
            InputDeviceName = null;
            OutputDeviceName = null;
        }

        if (inputDevice is not null)
        {
            inputDevice.EventReceived -= InputDevice_EventReceived;

            try
            {
                inputDevice.StopEventsListening();
            }
            catch
            {
                // The device can already be disconnected. Cleanup must still continue.
            }

            try
            {
                inputDevice.Dispose();
            }
            catch
            {
                // A disconnected native port must not prevent the service from stopping.
            }
        }

        if (outputDevice is not null)
        {
            try
            {
                outputDevice.Dispose();
            }
            catch
            {
                // A disconnected native port must not prevent the service from stopping.
            }
        }

    }

    public void Dispose()
    {
        Stop();
    }

    private static InputDevice GetInputDevice(string deviceName)
    {
        try
        {
            return InputDevice.GetByName(deviceName);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"MIDI input device '{deviceName}' was not found or could not be opened.",
                exception);
        }
    }

    private static OutputDevice GetOutputDevice(string deviceName)
    {
        try
        {
            return OutputDevice.GetByName(deviceName);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"MIDI output device '{deviceName}' was not found or could not be opened.",
                exception);
        }
    }

    private void InputDevice_EventReceived(object? sender, MidiEventReceivedEventArgs e)
    {
        MessageReceived?.Invoke(this, CreateMessage(e.Event));
        TryProcessMapping(e.Event);
    }

    private void TryProcessMapping(MidiEvent midiEvent)
    {
        // Temporary hardcoded mapping. Will be replaced by configurable mappings.
        if (midiEvent is not NoteOnEvent noteOn ||
            (int)noteOn.Channel != MappedInputChannel ||
            (int)noteOn.NoteNumber != MappedNoteNumber ||
            (int)noteOn.Velocity <= 0)
        {
            return;
        }

        MidiMessageReceivedEventArgs? result = null;

        lock (_syncRoot)
        {
            if (!IsRunning || _outputDevice is null)
            {
                return;
            }

            var controlChange = new ControlChangeEvent(
                (SevenBitNumber)MappedControlNumber,
                (SevenBitNumber)MappedControlValue)
            {
                Channel = (FourBitNumber)MappedOutputChannel
            };

            try
            {
                _outputDevice.SendEvent(controlChange);
                result = new MidiMessageReceivedEventArgs(
                    DateTime.Now,
                    "OUTPUT",
                    "Control Change",
                    MappedOutputChannel + 1,
                    $"CC {MappedControlNumber}, Value {MappedControlValue}");
            }
            catch (Exception exception)
            {
                var errorMessage = string.IsNullOrWhiteSpace(exception.Message)
                    ? "Failed to send the MIDI message."
                    : exception.Message;

                result = new MidiMessageReceivedEventArgs(
                    DateTime.Now,
                    "ERROR",
                    "Send Error",
                    null,
                    errorMessage);
            }
        }

        MessageReceived?.Invoke(this, result);
    }

    private static MidiMessageReceivedEventArgs CreateMessage(MidiEvent midiEvent)
    {
        var channel = midiEvent is ChannelEvent channelEvent
            ? (int)(FourBitNumber)channelEvent.Channel + 1
            : (int?)null;

        var (messageType, data) = midiEvent switch
        {
            NoteOnEvent noteOn when noteOn.Velocity == (SevenBitNumber)0 =>
                ("Note Off", FormatNote(noteOn.NoteNumber, noteOn.Velocity)),
            NoteOnEvent noteOn =>
                ("Note On", FormatNote(noteOn.NoteNumber, noteOn.Velocity)),
            NoteOffEvent noteOff =>
                ("Note Off", FormatNote(noteOff.NoteNumber, noteOff.Velocity)),
            ControlChangeEvent controlChange =>
                ("Control Change", $"CC {(int)controlChange.ControlNumber}, Value {(int)controlChange.ControlValue}"),
            ProgramChangeEvent programChange =>
                ("Program Change", $"Program {(int)programChange.ProgramNumber}"),
            PitchBendEvent pitchBend =>
                ("Pitch Bend", $"Value {pitchBend.PitchValue}"),
            _ => (midiEvent.GetType().Name, midiEvent.ToString() ?? string.Empty)
        };

        return new MidiMessageReceivedEventArgs(
            DateTime.Now,
            "INPUT",
            messageType,
            channel,
            data);
    }

    private static string FormatNote(SevenBitNumber noteNumber, SevenBitNumber velocity)
    {
        var number = (int)noteNumber;
        return $"Note {number} ({GetNoteName(number)}), Velocity {(int)velocity}";
    }

    private static string GetNoteName(int noteNumber)
    {
        if (noteNumber is < 0 or > 127)
        {
            return noteNumber.ToString();
        }

        var octave = noteNumber / 12 - 1;
        return $"{NoteNames[noteNumber % 12]}{octave}";
    }
}
