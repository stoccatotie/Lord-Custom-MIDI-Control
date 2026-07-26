using MidiControl.Core.Models;
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
    private readonly ObservableCollection<MidiMapping> _mappings = new();
    private readonly ObservableCollection<MidiLogEntry> _midiLogEntries = new();
    private MidiMapping? _learningMapping;
    private bool _isMidiLearnActive;

    public MainWindow()
    {
        InitializeComponent();

        _midiDeviceService = new MidiDeviceService();
        _midiConnectionService = new MidiConnectionService();
        _midiConnectionService.MessageReceived += MidiConnectionService_MessageReceived;
        _midiConnectionService.MidiLearnCaptured += MidiConnectionService_MidiLearnCaptured;

        foreach (var mapping in _midiConnectionService.Mappings)
        {
            _mappings.Add(mapping.Clone());
        }

        MappingsDataGrid.ItemsSource = _mappings;
        MidiMonitorListView.ItemsSource = _midiLogEntries;
        RefreshMidiDevices();
    }

    protected override void OnClosed(EventArgs e)
    {
        _midiConnectionService.MessageReceived -= MidiConnectionService_MessageReceived;
        _midiConnectionService.MidiLearnCaptured -= MidiConnectionService_MidiLearnCaptured;
        _midiConnectionService.Stop();
        ResetMidiLearnUi();
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

        if (!TryApplyMappings(showSuccessStatus: false))
        {
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
        ResetMidiLearnUi();
        SetStoppedState("Stopped");
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _midiLogEntries.Clear();
    }

    private void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        var mapping = new MidiMapping();
        _mappings.Add(mapping);
        SelectMapping(mapping);
    }

    private void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (MappingsDataGrid.SelectedItem is not MidiMapping mapping)
        {
            return;
        }

        if (ReferenceEquals(mapping, _learningMapping))
        {
            _midiConnectionService.CancelMidiLearn();
            ResetMidiLearnUi();
        }

        var index = _mappings.IndexOf(mapping);
        _mappings.Remove(mapping);

        if (_mappings.Count > 0)
        {
            SelectMapping(_mappings[Math.Min(index, _mappings.Count - 1)]);
        }
    }

    private void DuplicateRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (MappingsDataGrid.SelectedItem is not MidiMapping source)
        {
            return;
        }

        var copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = $"{source.Name} Copy";

        var sourceIndex = _mappings.IndexOf(source);
        _mappings.Insert(sourceIndex + 1, copy);
        SelectMapping(copy);
    }

    private void ApplyChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMidiLearnActive)
        {
            StatusText.Text = "Finish or cancel MIDI Learn first";
            return;
        }

        TryApplyMappings(showSuccessStatus: true);
    }

    private void MidiLearnButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMidiLearnActive)
        {
            return;
        }

        if (MappingsDataGrid.SelectedItem is not MidiMapping mapping)
        {
            StatusText.Text = "Select a mapping first";
            return;
        }

        if (!_midiConnectionService.IsRunning)
        {
            StatusText.Text = "Start MIDI connection first";
            return;
        }

        try
        {
            _learningMapping = mapping;
            _midiConnectionService.BeginMidiLearn();
            _isMidiLearnActive = true;
            MidiLearnButton.Content = "Listening...";
            MidiLearnButton.IsEnabled = false;
            CancelLearnButton.Visibility = Visibility.Visible;
            CancelLearnButton.IsEnabled = true;
            StatusText.Text = "MIDI Learn: play a note";
        }
        catch (Exception exception)
        {
            ResetMidiLearnUi();
            StatusText.Text = exception.Message;
        }
    }

    private void CancelLearnButton_Click(object sender, RoutedEventArgs e)
    {
        _midiConnectionService.CancelMidiLearn();
        ResetMidiLearnUi();
        StatusText.Text = "MIDI Learn cancelled";
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

    private void MidiConnectionService_MidiLearnCaptured(object? sender, MidiLearnCapturedEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var mapping = _learningMapping;

            if (!_isMidiLearnActive || mapping is null || !_mappings.Contains(mapping))
            {
                ResetMidiLearnUi();
                return;
            }

            mapping.InputChannel = e.InputChannel;
            mapping.InputNote = e.InputNote;
            SelectMapping(mapping);
            ResetMidiLearnUi();
            StatusText.Text =
                $"Learned Ch {e.InputChannel}, Note {e.InputNote} ({mapping.InputNoteName}). Press Apply Changes.";
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

    private bool TryApplyMappings(bool showSuccessStatus)
    {
        if (_isMidiLearnActive)
        {
            StatusText.Text = "Finish or cancel MIDI Learn first";
            return false;
        }

        if (!MappingsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !MappingsDataGrid.CommitEdit(DataGridEditingUnit.Row, true))
        {
            StatusText.Text = "Correct the value currently being edited";

            if (MappingsDataGrid.CurrentItem is MidiMapping currentMapping)
            {
                SelectMapping(currentMapping, beginEdit: true);
            }

            return false;
        }

        if (!ValidateMappings(out var errorMessage, out var invalidMapping))
        {
            StatusText.Text = errorMessage;

            if (invalidMapping is not null)
            {
                SelectMapping(invalidMapping, beginEdit: true);
            }

            return false;
        }

        _midiConnectionService.ReplaceMappings(_mappings);

        if (showSuccessStatus)
        {
            StatusText.Text = "Mappings applied";
        }

        return true;
    }

    private bool ValidateMappings(out string errorMessage, out MidiMapping? invalidMapping)
    {
        foreach (var mapping in _mappings)
        {
            string? validationError = null;

            if (string.IsNullOrWhiteSpace(mapping.Name))
            {
                validationError = "Name must not be empty.";
            }
            else if (mapping.InputChannel is < 1 or > 16)
            {
                validationError = "Input channel must be between 1 and 16.";
            }
            else if (mapping.InputNote is < 0 or > 127)
            {
                validationError = "Input note must be between 0 and 127.";
            }
            else if (mapping.MinimumVelocity is < 1 or > 127)
            {
                validationError = "Minimum velocity must be between 1 and 127.";
            }
            else if (mapping.OutputChannel is < 1 or > 16)
            {
                validationError = "Output channel must be between 1 and 16.";
            }
            else if (mapping.OutputController is < 0 or > 127)
            {
                validationError = "CC must be between 0 and 127.";
            }
            else if (mapping.OutputValue is < 0 or > 127)
            {
                validationError = "Output value must be between 0 and 127.";
            }

            if (validationError is not null)
            {
                invalidMapping = mapping;
                errorMessage = $"{mapping.Name}: {validationError}";
                return false;
            }
        }

        invalidMapping = null;
        errorMessage = string.Empty;
        return true;
    }

    private void SelectMapping(MidiMapping mapping, bool beginEdit = false)
    {
        MappingsDataGrid.SelectedItem = mapping;
        MappingsDataGrid.ScrollIntoView(mapping);
        MappingsDataGrid.Focus();

        if (beginEdit)
        {
            MappingsDataGrid.BeginEdit();
        }
    }

    private void ResetMidiLearnUi()
    {
        _learningMapping = null;
        _isMidiLearnActive = false;
        MidiLearnButton.Content = "MIDI Learn";
        MidiLearnButton.IsEnabled = true;
        CancelLearnButton.IsEnabled = false;
        CancelLearnButton.Visibility = Visibility.Collapsed;
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
