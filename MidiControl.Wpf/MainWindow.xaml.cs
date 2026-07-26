using MidiControl.Core.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MidiControl.Wpf;

public partial class MainWindow : Window
{
    private const int MaximumLogEntries = 1000;
    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(66, 211, 146));
    private static readonly Brush StoppedBrush = new SolidColorBrush(Color.FromRgb(104, 115, 132));

    private readonly MidiDeviceService _midiDeviceService;
    private readonly MidiConnectionService _midiConnectionService;
    private readonly ObservableCollection<MidiLogEntry> _midiLogEntries = new();

    public MainWindow()
    {
        InitializeComponent();

        _midiDeviceService = new MidiDeviceService();
        _midiConnectionService = new MidiConnectionService();
        _midiConnectionService.MessageReceived += MidiConnectionService_MessageReceived;

        MidiMonitorListView.ItemsSource = _midiLogEntries;
        RefreshMidiDevices();
    }

    protected override void OnClosed(EventArgs e)
    {
        _midiConnectionService.MessageReceived -= MidiConnectionService_MessageReceived;
        _midiConnectionService.Stop();
        _midiConnectionService.Dispose();
        base.OnClosed(e);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshMidiDevices();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (InputDeviceComboBox.SelectedItem is not string inputDeviceName)
        {
            SetStoppedState("Select MIDI input device");
            return;
        }

        if (OutputDeviceComboBox.SelectedItem is not string outputDeviceName)
        {
            SetStoppedState("Select MIDI output device");
            return;
        }

        try
        {
            _midiConnectionService.Start(inputDeviceName, outputDeviceName);
            SetRunningState();
        }
        catch (Exception exception)
        {
            SetStoppedState(exception.Message);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _midiConnectionService.Stop();
        SetStoppedState("Stopped");
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _midiLogEntries.Clear();
    }

    private void MidiConnectionService_MessageReceived(object? sender, MidiMessageReceivedEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var entry = new MidiLogEntry(
                e.Timestamp.ToString("HH:mm:ss.fff"),
                e.Direction,
                e.MessageType,
                e.Channel?.ToString() ?? string.Empty,
                e.Data,
                e.MappingName ?? string.Empty);

            _midiLogEntries.Add(entry);

            while (_midiLogEntries.Count > MaximumLogEntries)
            {
                _midiLogEntries.RemoveAt(0);
            }

            MidiMonitorListView.ScrollIntoView(entry);
        });
    }

    private void RefreshMidiDevices()
    {
        var selectedInput = InputDeviceComboBox.SelectedItem as string;
        var selectedOutput = OutputDeviceComboBox.SelectedItem as string;

        InputDeviceComboBox.Items.Clear();
        OutputDeviceComboBox.Items.Clear();

        var inputDeviceNames = _midiDeviceService.GetInputDeviceNames();
        var outputDeviceNames = _midiDeviceService.GetOutputDeviceNames();

        foreach (var deviceName in inputDeviceNames)
        {
            InputDeviceComboBox.Items.Add(deviceName);
        }

        foreach (var deviceName in outputDeviceNames)
        {
            OutputDeviceComboBox.Items.Add(deviceName);
        }

        RestoreSelection(InputDeviceComboBox, selectedInput);
        RestoreSelection(OutputDeviceComboBox, selectedOutput);

        if (inputDeviceNames.Count == 0)
        {
            SetStoppedState("No MIDI input devices");
        }
        else if (outputDeviceNames.Count == 0)
        {
            SetStoppedState("No MIDI output devices");
        }
        else
        {
            SetStoppedState("Stopped");
        }
    }

    private static void RestoreSelection(ComboBox comboBox, string? previousSelection)
    {
        if (previousSelection is not null && comboBox.Items.Contains(previousSelection))
        {
            comboBox.SelectedItem = previousSelection;
        }
        else if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private void SetRunningState()
    {
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        InputDeviceComboBox.IsEnabled = false;
        OutputDeviceComboBox.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        StatusIndicator.Fill = RunningBrush;
        StatusText.Text = "Running";
    }

    private void SetStoppedState(string status)
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        InputDeviceComboBox.IsEnabled = true;
        OutputDeviceComboBox.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        StatusIndicator.Fill = StoppedBrush;
        StatusText.Text = status;
    }
}
