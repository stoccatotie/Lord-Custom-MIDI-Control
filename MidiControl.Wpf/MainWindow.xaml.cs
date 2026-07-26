using MidiControl.Core.Services;
using System.Windows;
using System.Windows.Media;

namespace MidiControl.Wpf;

public partial class MainWindow : Window
{
    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(66, 211, 146));
    private static readonly Brush StoppedBrush = new SolidColorBrush(Color.FromRgb(104, 115, 132));
    private readonly MidiDeviceService _midiDeviceService;

    public MainWindow()
    {
        InitializeComponent();
        _midiDeviceService = new MidiDeviceService();
        RefreshMidiDevices();
        LoadTestMessages();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshMidiDevices();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (InputDeviceComboBox.SelectedItem is null)
        {
            SetStoppedState("Select MIDI input device");
            return;
        }

        if (OutputDeviceComboBox.SelectedItem is null)
        {
            SetStoppedState("Select MIDI output device");
            return;
        }

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusIndicator.Fill = RunningBrush;
        StatusText.Text = "Running";
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        SetStoppedState("Stopped");
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        MidiMonitorListView.Items.Clear();
    }

    private void RefreshMidiDevices()
    {
        var selectedInput = InputDeviceComboBox.SelectedItem as string;
        var selectedOutput = OutputDeviceComboBox.SelectedItem as string;

        InputDeviceComboBox.ItemsSource = null;
        OutputDeviceComboBox.ItemsSource = null;
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

    private static void RestoreSelection(System.Windows.Controls.ComboBox comboBox, string? previousSelection)
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

    private void SetStoppedState(string status)
    {
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusIndicator.Fill = StoppedBrush;
        StatusText.Text = status;
    }

    private void LoadTestMessages()
    {
        MidiMonitorListView.Items.Add(new MidiMonitorEntry("12:04:18.042", "Input", "Note On", "1", "Note 60, Velocity 96"));
        MidiMonitorListView.Items.Add(new MidiMonitorEntry("12:04:18.318", "Input", "Control Change", "1", "CC 64, Value 127"));
        MidiMonitorListView.Items.Add(new MidiMonitorEntry("12:04:18.641", "Output", "Program Change", "2", "Program 28"));
        MidiMonitorListView.Items.Add(new MidiMonitorEntry("12:04:19.105", "Input", "Note Off", "1", "Note 60, Velocity 0"));
        MidiMonitorListView.Items.Add(new MidiMonitorEntry("12:04:19.487", "Output", "Pitch Bend", "2", "Value 8192"));
    }

    private sealed record MidiMonitorEntry(
        string Time,
        string Direction,
        string Message,
        string Channel,
        string Data);
}
