using MidiControl.Core.Models;
using MidiControl.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly SettingsService _settingsService;
    private readonly AppSettings _loadedSettings;
    private readonly ObservableCollection<MidiMapping> _mappings = new();
    private readonly ObservableCollection<MidiLogEntry> _midiLogEntries = new();
    private List<MidiMapping> _lastAppliedMappings = new();
    private MidiMapping? _learningMapping;
    private bool _isMidiLearnActive;
    private bool _isClosing;
    private bool _autoConnectAttempted;

    public MainWindow()
    {
        InitializeComponent();

        _midiDeviceService = new MidiDeviceService();
        _midiConnectionService = new MidiConnectionService();
        _settingsService = new SettingsService();
        _midiConnectionService.MessageReceived += MidiConnectionService_MessageReceived;
        _midiConnectionService.MidiLearnCaptured += MidiConnectionService_MidiLearnCaptured;
        _midiConnectionService.ConnectionError += MidiConnectionService_ConnectionError;

        _loadedSettings = _settingsService.Load();

        foreach (var mapping in _loadedSettings.Mappings)
        {
            _mappings.Add(mapping.Clone());
        }

        _midiConnectionService.ReplaceMappings(_mappings);
        _lastAppliedMappings = _mappings.Select(mapping => mapping.Clone()).ToList();
        MappingsDataGrid.ItemsSource = _mappings;
        MidiMonitorListView.ItemsSource = _midiLogEntries;
        AutoConnectCheckBox.IsChecked = _loadedSettings.AutoConnect;
        RefreshMidiDevices(
            _loadedSettings.InputDeviceName,
            _loadedSettings.OutputDeviceName,
            usePreferredSelections: true);
        UpdateConnectionUi(isRunning: false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel || _isClosing)
        {
            return;
        }

        _isClosing = true;
        AppSettings settings;

        try
        {
            try
            {
                settings = CreateSettingsForClosing();
            }
            catch
            {
                settings = CreateCurrentSettings(_lastAppliedMappings);
            }

            try
            {
                _settingsService.Save(settings);
            }
            catch
            {
                // Saving must never prevent MIDI ports from being released.
            }
        }
        finally
        {
            _midiConnectionService.CancelMidiLearn();
            _midiConnectionService.Stop();
            _midiConnectionService.MessageReceived -= MidiConnectionService_MessageReceived;
            _midiConnectionService.MidiLearnCaptured -= MidiConnectionService_MidiLearnCaptured;
            _midiConnectionService.ConnectionError -= MidiConnectionService_ConnectionError;
            _midiConnectionService.Dispose();
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_midiConnectionService.IsRunning)
        {
            return;
        }

        RefreshMidiDevices();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_autoConnectAttempted)
        {
            return;
        }

        _autoConnectAttempted = true;

        if (!_loadedSettings.AutoConnect)
        {
            SetConnectionStatus(isRunning: false);
            return;
        }

        var savedDevicesAreAvailable =
            _loadedSettings.InputDeviceName is not null &&
            _loadedSettings.OutputDeviceName is not null &&
            string.Equals(
                InputDeviceComboBox.SelectedItem as string,
                _loadedSettings.InputDeviceName,
                StringComparison.Ordinal) &&
            string.Equals(
                OutputDeviceComboBox.SelectedItem as string,
                _loadedSettings.OutputDeviceName,
                StringComparison.Ordinal);

        if (!savedDevicesAreAvailable)
        {
            UpdateConnectionUi(isRunning: false);
            SetStatus("Auto-connect failed: saved MIDI device is unavailable");
            return;
        }

        TryStartMidiConnection();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        TryStartMidiConnection();
    }

    private bool TryStartMidiConnection()
    {
        if (_midiConnectionService.IsRunning)
        {
            return true;
        }

        if (!TryApplyMappings())
        {
            UpdateConnectionUi(isRunning: false);
            return false;
        }

        if (InputDeviceComboBox.SelectedItem is not string inputDeviceName)
        {
            UpdateConnectionUi(isRunning: false);
            SetStatus("Select MIDI input device");
            return false;
        }

        if (OutputDeviceComboBox.SelectedItem is not string outputDeviceName)
        {
            UpdateConnectionUi(isRunning: false);
            SetStatus("Select MIDI output device");
            return false;
        }

        try
        {
            _midiConnectionService.Start(inputDeviceName, outputDeviceName);
            UpdateConnectionUi(isRunning: true);
            SetConnectionStatus(isRunning: true);
            return true;
        }
        catch (Exception exception)
        {
            UpdateConnectionUi(isRunning: false);
            SetStatus(string.IsNullOrWhiteSpace(exception.Message)
                ? "MIDI connection could not be started"
                : exception.Message);
            return false;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _midiConnectionService.Stop();
        ResetMidiLearnUi();
        UpdateConnectionUi(isRunning: false);
        SetConnectionStatus(isRunning: false);
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
            SetStatus("Finish or cancel MIDI Learn first");
            return;
        }

        if (!TryApplyMappings())
        {
            return;
        }

        try
        {
            _settingsService.Save(CreateCurrentSettings());
            SetStatus(
                $"Mappings applied and saved — {GetActiveMappingCount()} active mappings");
        }
        catch
        {
            SetStatus(
                $"Mappings applied, but settings could not be saved — " +
                $"{GetActiveMappingCount()} active mappings");
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApplyMappings())
        {
            return;
        }

        try
        {
            _settingsService.Save(CreateCurrentSettings());
            SetStatus("Settings saved");
        }
        catch (Exception exception)
        {
            SetStatus(string.IsNullOrWhiteSpace(exception.Message)
                ? "Settings could not be saved"
                : $"Settings could not be saved: {exception.Message}");
        }
    }

    private void MidiLearnButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMidiLearnActive)
        {
            return;
        }

        if (MappingsDataGrid.SelectedItem is not MidiMapping mapping)
        {
            SetStatus("Select a mapping first");
            return;
        }

        if (!_midiConnectionService.IsRunning)
        {
            SetStatus("Start MIDI connection first");
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
            SetStatus("MIDI Learn: play a note");
        }
        catch (Exception exception)
        {
            ResetMidiLearnUi();
            SetStatus(exception.Message);
        }
    }

    private void CancelLearnButton_Click(object sender, RoutedEventArgs e)
    {
        _midiConnectionService.CancelMidiLearn();
        ResetMidiLearnUi();
        SetStatus(
            $"MIDI Learn cancelled — {GetConnectionStatus(_midiConnectionService.IsRunning)}");
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

    private void MidiConnectionService_ConnectionError(object? sender, MidiConnectionErrorEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var entry = new MidiLogEntry(
                e.Timestamp.ToString("HH:mm:ss.fff"),
                "ERROR",
                "Connection Error",
                string.Empty,
                e.Message,
                string.Empty);

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
            SetStatus(
                $"Learned Ch {e.InputChannel}, Note {e.InputNote} ({mapping.InputNoteName}). " +
                $"Press Apply Changes. {GetConnectionStatus(_midiConnectionService.IsRunning)}");
        });
    }

    private void RefreshMidiDevices(
        string? preferredInput = null,
        string? preferredOutput = null,
        bool usePreferredSelections = false)
    {
        var selectedInput = usePreferredSelections
            ? preferredInput
            : InputDeviceComboBox.SelectedItem as string;
        var selectedOutput = usePreferredSelections
            ? preferredOutput
            : OutputDeviceComboBox.SelectedItem as string;

        InputDeviceComboBox.Items.Clear();
        OutputDeviceComboBox.Items.Clear();

        var inputDeviceNames = _midiDeviceService.GetInputDeviceNames();
        var outputDeviceNames = _midiDeviceService.GetOutputDeviceNames();
        var selectedDeviceDisappeared =
            (selectedInput is not null && !inputDeviceNames.Contains(selectedInput)) ||
            (selectedOutput is not null && !outputDeviceNames.Contains(selectedOutput));

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
            SetStatus("No MIDI input devices");
        }
        else if (outputDeviceNames.Count == 0)
        {
            SetStatus("No MIDI output devices");
        }
        else if (selectedDeviceDisappeared)
        {
            SetStatus("Selected MIDI device is no longer available");
        }
        else
        {
            SetConnectionStatus(isRunning: false);
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

    private bool TryApplyMappings()
    {
        if (_isMidiLearnActive)
        {
            SetStatus("Finish or cancel MIDI Learn first");
            return false;
        }

        if (!MappingsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !MappingsDataGrid.CommitEdit(DataGridEditingUnit.Row, true))
        {
            SetStatus("Correct the value currently being edited");

            if (MappingsDataGrid.CurrentItem is MidiMapping currentMapping)
            {
                SelectMapping(currentMapping, beginEdit: true);
            }

            return false;
        }

        if (!ValidateMappings(out var errorMessage, out var invalidMapping))
        {
            SetStatus(errorMessage);

            if (invalidMapping is not null)
            {
                SelectMapping(invalidMapping, beginEdit: true);
            }

            return false;
        }

        _midiConnectionService.ReplaceMappings(_mappings);
        _lastAppliedMappings = _mappings.Select(mapping => mapping.Clone()).ToList();

        return true;
    }

    private AppSettings CreateCurrentSettings()
    {
        return CreateCurrentSettings(_mappings);
    }

    private AppSettings CreateCurrentSettings(IEnumerable<MidiMapping> mappings)
    {
        return new AppSettings
        {
            InputDeviceName = InputDeviceComboBox.SelectedItem as string,
            OutputDeviceName = OutputDeviceComboBox.SelectedItem as string,
            AutoConnect = AutoConnectCheckBox.IsChecked == true,
            Mappings = mappings.Select(mapping => mapping.Clone()).ToList()
        };
    }

    private AppSettings CreateSettingsForClosing()
    {
        var editsCommitted =
            MappingsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true) &&
            MappingsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var currentMappingsAreValid =
            editsCommitted &&
            ValidateMappings(out _, out _);

        return currentMappingsAreValid
            ? CreateCurrentSettings()
            : CreateCurrentSettings(_lastAppliedMappings);
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

    private void UpdateConnectionUi(bool isRunning)
    {
        StartButton.IsEnabled = !isRunning;
        StopButton.IsEnabled = isRunning;
        RefreshButton.IsEnabled = !isRunning;
        InputDeviceComboBox.IsEnabled = !isRunning;
        OutputDeviceComboBox.IsEnabled = !isRunning;
        StatusIndicator.Fill = isRunning ? RunningBrush : StoppedBrush;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void SetConnectionStatus(bool isRunning)
    {
        SetStatus(GetConnectionStatus(isRunning));
    }

    private string GetConnectionStatus(bool isRunning)
    {
        var state = isRunning ? "Running" : "Stopped";
        return $"{state} — {GetActiveMappingCount()} active mappings";
    }

    private int GetActiveMappingCount()
    {
        return _midiConnectionService.Mappings.Count(mapping => mapping.IsEnabled);
    }
}
