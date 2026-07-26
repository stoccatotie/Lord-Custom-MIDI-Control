using System.Collections.Concurrent;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using Melanchall.DryWetMidi.MusicTheory;

Console.WriteLine("Guitar Pro MIDI Control");
Console.WriteLine("Application started successfully.");
Console.WriteLine();

List<InputDevice> inputDevices;

try
{
    inputDevices = InputDevice.GetAll().ToList();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Failed to enumerate MIDI input devices: {exception.Message}");
    return;
}

if (inputDevices.Count == 0)
{
    Console.WriteLine("No MIDI input devices found.");
    return;
}

Console.WriteLine("Available MIDI input devices:");

for (var index = 0; index < inputDevices.Count; index++)
{
    Console.WriteLine($"{index + 1}. {inputDevices[index].Name}");
}

var selectedInputIndex = ReadDeviceIndex("MIDI input", inputDevices.Count);
var selectedInputDevice = inputDevices[selectedInputIndex];

for (var index = 0; index < inputDevices.Count; index++)
{
    if (index != selectedInputIndex)
    {
        inputDevices[index].Dispose();
    }
}

List<OutputDevice> outputDevices;

try
{
    outputDevices = OutputDevice.GetAll().ToList();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Failed to enumerate MIDI output devices: {exception.Message}");
    selectedInputDevice.Dispose();
    return;
}

if (outputDevices.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("No MIDI output devices found.");
    selectedInputDevice.Dispose();
    return;
}

Console.WriteLine();
Console.WriteLine("Available MIDI output devices:");

for (var index = 0; index < outputDevices.Count; index++)
{
    Console.WriteLine($"{index + 1}. {outputDevices[index].Name}");
}

var selectedOutputIndex = ReadDeviceIndex("MIDI output", outputDevices.Count);
var selectedOutputDevice = outputDevices[selectedOutputIndex];

for (var index = 0; index < outputDevices.Count; index++)
{
    if (index != selectedOutputIndex)
    {
        outputDevices[index].Dispose();
    }
}

try
{
    selectedOutputDevice.PrepareForEventsSending();
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"Failed to open MIDI output device \"{selectedOutputDevice.Name}\": {exception.Message}");
    selectedInputDevice.Dispose();
    selectedOutputDevice.Dispose();
    return;
}

var receivedEvents = new ConcurrentQueue<ReceivedMidiEvent>();
using var eventAvailable = new SemaphoreSlim(0);
using var writerCancellation = new CancellationTokenSource();
var writerTask = ProcessEventsAsync(
    receivedEvents,
    eventAvailable,
    selectedOutputDevice,
    writerCancellation.Token);

EventHandler<MidiEventReceivedEventArgs> eventHandler = (_, eventArgs) =>
{
    receivedEvents.Enqueue(new ReceivedMidiEvent(DateTimeOffset.Now, eventArgs.Event));
    eventAvailable.Release();
};

var listeningStarted = false;
var exitRequested = new TaskCompletionSource(
    TaskCreationOptions.RunContinuationsAsynchronously);

ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    exitRequested.TrySetResult();
};

Console.CancelKeyPress += cancelHandler;
selectedInputDevice.EventReceived += eventHandler;

try
{
    try
    {
        selectedInputDevice.StartEventsListening();
        listeningStarted = true;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"Failed to open MIDI input device \"{selectedInputDevice.Name}\": {exception.Message}");
        return;
    }

    Console.WriteLine();
    Console.WriteLine(
        $"Listening to \"{selectedInputDevice.Name}\" and sending to \"{selectedOutputDevice.Name}\".");
    Console.WriteLine("Press Enter or Ctrl+C to stop.");

    var enterPressed = Task.Run(Console.ReadLine);
    await Task.WhenAny(enterPressed, exitRequested.Task);
}
finally
{
    if (listeningStarted)
    {
        try
        {
            selectedInputDevice.StopEventsListening();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to stop MIDI event listening: {exception.Message}");
        }
    }

    selectedInputDevice.EventReceived -= eventHandler;
    Console.CancelKeyPress -= cancelHandler;
    selectedInputDevice.Dispose();

    writerCancellation.Cancel();
    eventAvailable.Release();
    await writerTask;
    selectedOutputDevice.Dispose();
}

Console.WriteLine("MIDI input and output devices closed.");

static int ReadDeviceIndex(string deviceType, int deviceCount)
{
    while (true)
    {
        Console.Write($"Select a {deviceType} device (1-{deviceCount}): ");
        var input = Console.ReadLine();

        if (!int.TryParse(input, out var selectedNumber))
        {
            Console.WriteLine("Invalid input. Enter an integer number.");
            continue;
        }

        if (selectedNumber < 1 || selectedNumber > deviceCount)
        {
            Console.WriteLine($"Device number must be between 1 and {deviceCount}.");
            continue;
        }

        return selectedNumber - 1;
    }
}

static string FormatMidiEvent(DateTimeOffset receivedAt, MidiEvent midiEvent)
{
    var timestamp = receivedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");

    if (midiEvent is NoteEvent noteEvent)
    {
        var noteNumber = (byte)noteEvent.NoteNumber;
        var noteName = NoteUtilities.GetNoteName(noteEvent.NoteNumber);
        var octave = noteNumber / 12 - 1;
        var channel = (byte)noteEvent.Channel + 1;
        var velocity = (byte)noteEvent.Velocity;
        var eventType = midiEvent is NoteOnEvent && velocity > 0
            ? "Note On"
            : "Note Off";

        return $"{timestamp} | {eventType} | Channel: {channel} | " +
               $"Note: {noteNumber} ({noteName}{octave}) | Velocity: {velocity}";
    }

    var channelText = midiEvent is ChannelEvent channelEvent
        ? ((byte)channelEvent.Channel + 1).ToString()
        : "N/A";

    return $"{timestamp} | {midiEvent.EventType} | Channel: {channelText} | {midiEvent}";
}

static async Task ProcessEventsAsync(
    ConcurrentQueue<ReceivedMidiEvent> receivedEvents,
    SemaphoreSlim eventAvailable,
    OutputDevice outputDevice,
    CancellationToken cancellationToken)
{
    while (true)
    {
        try
        {
            await eventAvailable.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Drain events already received before shutdown.
        }

        while (receivedEvents.TryDequeue(out var receivedEvent))
        {
            Console.WriteLine(FormatMidiEvent(receivedEvent.ReceivedAt, receivedEvent.Event));
            ApplyRules(receivedEvent.Event, outputDevice);
        }

        if (cancellationToken.IsCancellationRequested && receivedEvents.IsEmpty)
        {
            return;
        }
    }
}

static void ApplyRules(MidiEvent midiEvent, OutputDevice outputDevice)
{
    if (midiEvent is not NoteOnEvent noteOnEvent ||
        (byte)noteOnEvent.Channel != 0 ||
        (byte)noteOnEvent.NoteNumber != 67 ||
        (byte)noteOnEvent.Velocity == 0)
    {
        return;
    }

    var controlChangeEvent = new ControlChangeEvent(
        (SevenBitNumber)20,
        (SevenBitNumber)127)
    {
        Channel = (FourBitNumber)0
    };

    try
    {
        outputDevice.SendEvent(controlChangeEvent);

        Console.WriteLine("RULE MATCH:");
        Console.WriteLine(
            $"Input: Note On | Channel: 1 | Note: 67 (G4) | Velocity: {(byte)noteOnEvent.Velocity}");
        Console.WriteLine("Output: Control Change | Channel: 1 | CC: 20 | Value: 127");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"Failed to send Control Change to \"{outputDevice.Name}\": {exception.Message}");
    }
}

internal sealed record ReceivedMidiEvent(DateTimeOffset ReceivedAt, MidiEvent Event);
