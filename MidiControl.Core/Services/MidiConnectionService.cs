using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using MidiControl.Core.Models;

namespace MidiControl.Core.Services;

public sealed class MidiConnectionService : IDisposable
{
    private static readonly string[] NoteNames =
    {
        "C", "C#", "D", "D#", "E", "F",
        "F#", "G", "G#", "A", "A#", "B"
    };

    private readonly object _syncRoot = new();
    private readonly List<MidiMapping> _mappings;
    private InputDevice? _inputDevice;
    private OutputDevice? _outputDevice;
    private bool _isMidiLearnActive;

    public MidiConnectionService()
    {
        _mappings =
        [
            new MidiMapping
            {
                IsEnabled = true,
                Name = "REAPER Mute Test",
                InputChannel = 1,
                InputNote = 67,
                MinimumVelocity = 1,
                OutputChannel = 1,
                OutputController = 20,
                OutputValue = 127
            }
        ];
    }

    public event EventHandler<MidiMessageReceivedEventArgs>? MessageReceived;

    public event EventHandler<MidiLearnCapturedEventArgs>? MidiLearnCaptured;

    public bool IsRunning { get; private set; }

    public string? InputDeviceName { get; private set; }

    public string? OutputDeviceName { get; private set; }

    public bool IsMidiLearnActive
    {
        get
        {
            lock (_syncRoot)
            {
                return _isMidiLearnActive;
            }
        }
    }

    public IReadOnlyList<MidiMapping> Mappings
    {
        get
        {
            lock (_syncRoot)
            {
                return _mappings.Select(mapping => mapping.Clone()).ToArray();
            }
        }
    }

    public void ReplaceMappings(IEnumerable<MidiMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        var copies = mappings.Select(mapping =>
            mapping?.Clone() ?? throw new ArgumentException(
                "Mappings cannot contain null items.",
                nameof(mappings)))
            .ToArray();

        lock (_syncRoot)
        {
            _mappings.Clear();
            _mappings.AddRange(copies);
        }
    }

    public void BeginMidiLearn()
    {
        lock (_syncRoot)
        {
            if (!IsRunning)
            {
                throw new InvalidOperationException("MIDI connection is not running.");
            }

            if (_isMidiLearnActive)
            {
                throw new InvalidOperationException("MIDI Learn is already active.");
            }

            _isMidiLearnActive = true;
        }
    }

    public void CancelMidiLearn()
    {
        lock (_syncRoot)
        {
            _isMidiLearnActive = false;
        }
    }

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
            _isMidiLearnActive = false;
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

        if (TryCaptureMidiLearn(e.Event))
        {
            return;
        }

        TryProcessMapping(e.Event);
    }

    private bool TryCaptureMidiLearn(MidiEvent midiEvent)
    {
        if (midiEvent is not NoteOnEvent noteOn || (int)noteOn.Velocity <= 0)
        {
            return false;
        }

        MidiLearnCapturedEventArgs? capturedEvent = null;

        lock (_syncRoot)
        {
            if (!_isMidiLearnActive || !IsRunning)
            {
                return false;
            }

            _isMidiLearnActive = false;
            capturedEvent = new MidiLearnCapturedEventArgs(
                (int)noteOn.Channel + 1,
                (int)noteOn.NoteNumber,
                (int)noteOn.Velocity,
                DateTime.Now);
        }

        MidiLearnCaptured?.Invoke(this, capturedEvent);
        return true;
    }

    private void TryProcessMapping(MidiEvent midiEvent)
    {
        if (midiEvent is not NoteOnEvent noteOn || (int)noteOn.Velocity <= 0)
        {
            return;
        }

        var results = new List<MidiMessageReceivedEventArgs>();

        lock (_syncRoot)
        {
            if (!IsRunning || _outputDevice is null)
            {
                return;
            }

            foreach (var mapping in _mappings)
            {
                if (!mapping.IsEnabled)
                {
                    continue;
                }

                var mappingName = string.IsNullOrWhiteSpace(mapping.Name)
                    ? "Unnamed mapping"
                    : mapping.Name;
                var validationError = ValidateMapping(mapping);

                if (validationError is not null)
                {
                    results.Add(new MidiMessageReceivedEventArgs(
                        DateTime.Now,
                        "ERROR",
                        "Invalid Mapping",
                        null,
                        $"{mappingName}: {validationError}",
                        mappingName));
                    continue;
                }

                if ((int)noteOn.Channel + 1 != mapping.InputChannel ||
                    (int)noteOn.NoteNumber != mapping.InputNote ||
                    (int)noteOn.Velocity < mapping.MinimumVelocity)
                {
                    continue;
                }

                var controlChange = new ControlChangeEvent(
                    (SevenBitNumber)mapping.OutputController,
                    (SevenBitNumber)mapping.OutputValue)
                {
                    Channel = (FourBitNumber)(mapping.OutputChannel - 1)
                };

                try
                {
                    _outputDevice.SendEvent(controlChange);
                    results.Add(new MidiMessageReceivedEventArgs(
                        DateTime.Now,
                        "OUTPUT",
                        "Control Change",
                        mapping.OutputChannel,
                        $"CC {mapping.OutputController}, Value {mapping.OutputValue}",
                        mappingName));
                }
                catch (Exception exception)
                {
                    var errorMessage = string.IsNullOrWhiteSpace(exception.Message)
                        ? "Failed to send the MIDI message."
                        : exception.Message;

                    results.Add(new MidiMessageReceivedEventArgs(
                        DateTime.Now,
                        "ERROR",
                        "Send Error",
                        null,
                        errorMessage,
                        mappingName));
                }
            }
        }

        foreach (var result in results)
        {
            MessageReceived?.Invoke(this, result);
        }
    }

    private static string? ValidateMapping(MidiMapping mapping)
    {
        if (mapping.InputChannel is < 1 or > 16)
        {
            return "Input channel must be between 1 and 16.";
        }

        if (mapping.InputNote is < 0 or > 127)
        {
            return "Input note must be between 0 and 127.";
        }

        if (mapping.MinimumVelocity is < 1 or > 127)
        {
            return "Minimum velocity must be between 1 and 127.";
        }

        if (mapping.OutputChannel is < 1 or > 16)
        {
            return "Output channel must be between 1 and 16.";
        }

        if (mapping.OutputController is < 0 or > 127)
        {
            return "Output controller must be between 0 and 127.";
        }

        if (mapping.OutputValue is < 0 or > 127)
        {
            return "Output value must be between 0 and 127.";
        }

        return null;
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

        var receivedNoteOn = midiEvent as NoteOnEvent;
        var velocity = receivedNoteOn is null ? (int?)null : (int)receivedNoteOn.Velocity;

        return new MidiMessageReceivedEventArgs(
            DateTime.Now,
            "INPUT",
            messageType,
            channel,
            data,
            noteNumber: receivedNoteOn is null ? null : (int)receivedNoteOn.NoteNumber,
            velocity: velocity,
            isNoteOn: receivedNoteOn is not null && velocity > 0);
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
