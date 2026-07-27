using MidiControl.Core.Models;
using MidiControl.Core.Services;
using MidiControl.Wpf.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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
    private readonly DispatcherTimer _midiDeviceCheckTimer;
    private readonly ObservableCollection<MidiMapping> _mappings = new();
    private readonly ObservableCollection<MidiLogEntry> _midiLogEntries = new();
    private List<MidiMapping> _lastAppliedMappings = new();
    private MidiMapping? _learningMapping;
    private bool _isMidiLearnActive;
    private bool _isClosing;
    private bool _autoConnectAttempted;
    private bool _isCheckingMidiDevices;
    private bool _isRefreshingMidiDevices;
    private bool _midiAvailabilityCheckErrorReported;
    private string? _unavailableSavedInputDeviceName;
    private string? _unavailableSavedOutputDeviceName;
    private bool _unavailableInputWasSaved;
    private bool _unavailableOutputWasSaved;

    public IReadOnlyList<MidiControlChangeOption> MidiControlChangeOptions =>
        MidiControlChangeCatalog.Options;

    public MainWindow()
    {
        InitializeComponent();

        _midiDeviceService = new MidiDeviceService();
        _midiConnectionService = new MidiConnectionService();
        _settingsService = new SettingsService();
        _midiConnectionService.MessageReceived += MidiConnectionService_MessageReceived;
        _midiConnectionService.MidiLearnCaptured += MidiConnectionService_MidiLearnCaptured;
        _midiConnectionService.ConnectionError += MidiConnectionService_ConnectionError;
        _midiDeviceCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _midiDeviceCheckTimer.Tick += MidiDeviceCheckTimer_Tick;

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
            restoreSavedSelections: true);
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
            _midiDeviceCheckTimer.Stop();
            _midiDeviceCheckTimer.Tick -= MidiDeviceCheckTimer_Tick;
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
            if (!TrySetUnavailableDeviceStatus())
            {
                SetConnectionStatus(isRunning: false);
            }
            return;
        }

        if (_unavailableSavedInputDeviceName is not null ||
            _unavailableSavedOutputDeviceName is not null)
        {
            UpdateConnectionUi(isRunning: false);
            TrySetUnavailableDeviceStatus();
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
            SetStatus("Auto-connect requires available saved MIDI input and output devices");
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
            SetStatus(_unavailableSavedInputDeviceName is null
                ? "Select MIDI input device"
                : $"Select a replacement for unavailable MIDI input: {_unavailableSavedInputDeviceName}");
            return false;
        }

        if (OutputDeviceComboBox.SelectedItem is not string outputDeviceName)
        {
            UpdateConnectionUi(isRunning: false);
            SetStatus(_unavailableSavedOutputDeviceName is null
                ? "Select MIDI output device"
                : $"Select a replacement for unavailable MIDI output: {_unavailableSavedOutputDeviceName}");
            return false;
        }

        try
        {
            _midiConnectionService.Start(inputDeviceName, outputDeviceName);
            ClearUnavailableInputDevice();
            ClearUnavailableOutputDevice();
            _midiDeviceCheckTimer.Start();
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
        StopMidiConnection(GetConnectionStatus(isRunning: false), refreshDevices: false);
    }

    private void InputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingMidiDevices)
        {
            return;
        }

        if (InputDeviceComboBox.SelectedItem is string)
        {
            ClearUnavailableInputDevice();
        }

        UpdateStatusAfterManualSelection();
    }

    private void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingMidiDevices)
        {
            return;
        }

        if (OutputDeviceComboBox.SelectedItem is string)
        {
            ClearUnavailableOutputDevice();
        }

        UpdateStatusAfterManualSelection();
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

    private void TestRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MidiMapping mapping })
        {
            SetStatus("Mapping could not be tested");
            return;
        }

        if (!MappingsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !MappingsDataGrid.CommitEdit(DataGridEditingUnit.Row, true))
        {
            SetStatus("Correct the value currently being edited");
            return;
        }

        if (mapping.OutputChannel is < 1 or > 16)
        {
            SetStatus("Output channel must be between 1 and 16");
            return;
        }

        if (mapping.OutputController is < 0 or > 127)
        {
            SetStatus("CC number must be between 0 and 127");
            return;
        }

        if (mapping.OutputValue is < 0 or > 127)
        {
            SetStatus("CC value must be between 0 and 127");
            return;
        }

        if (OutputDeviceComboBox.SelectedItem is not string)
        {
            SetStatus("Select MIDI output device");
            return;
        }

        if (!_midiConnectionService.IsRunning)
        {
            SetStatus("Start MIDI connection first");
            return;
        }

        try
        {
            _midiConnectionService.SendTestControlChange(
                mapping.OutputChannel,
                mapping.OutputController,
                mapping.OutputValue,
                mapping.Name);

            SetStatus(
                $"Tested \"{mapping.Name}\" — Ch {mapping.OutputChannel}, " +
                $"CC {mapping.OutputController}, Value {mapping.OutputValue}");
        }
        catch (Exception exception)
        {
            SetStatus(string.IsNullOrWhiteSpace(exception.Message)
                ? "MIDI test message could not be sent"
                : exception.Message);
        }
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

    private void MidiDeviceCheckTimer_Tick(object? sender, EventArgs e)
    {
        if (_isCheckingMidiDevices || !_midiConnectionService.IsRunning)
        {
            return;
        }

        _isCheckingMidiDevices = true;

        try
        {
            var activeInputName = _midiConnectionService.InputDeviceName;
            var activeOutputName = _midiConnectionService.OutputDeviceName;

            if (!_midiDeviceService.TryGetInputDeviceNames(out var inputDeviceNames) ||
                !_midiDeviceService.TryGetOutputDeviceNames(out var outputDeviceNames))
            {
                ReportMidiAvailabilityCheckError(
                    "MIDI device availability could not be checked.");
                return;
            }

            _midiAvailabilityCheckErrorReported = false;

            var inputDisconnected =
                activeInputName is not null &&
                !inputDeviceNames.Any(name =>
                    string.Equals(name, activeInputName, StringComparison.Ordinal));
            var outputDisconnected =
                activeOutputName is not null &&
                !outputDeviceNames.Any(name =>
                    string.Equals(name, activeOutputName, StringComparison.Ordinal));

            if (!inputDisconnected && !outputDisconnected)
            {
                return;
            }

            if (inputDisconnected)
            {
                _unavailableSavedInputDeviceName = activeInputName;
                _unavailableInputWasSaved = false;
            }

            if (outputDisconnected)
            {
                _unavailableSavedOutputDeviceName = activeOutputName;
                _unavailableOutputWasSaved = false;
            }

            var statusMessage = inputDisconnected && outputDisconnected
                ? $"MIDI devices disconnected: input \"{activeInputName}\", output \"{activeOutputName}\""
                : inputDisconnected
                    ? $"MIDI input device disconnected: {activeInputName}"
                    : $"MIDI output device disconnected: {activeOutputName}";

            StopMidiConnection(statusMessage, refreshDevices: true);
        }
        catch (Exception exception)
        {
            var message = string.IsNullOrWhiteSpace(exception.Message)
                ? "MIDI device availability could not be checked."
                : $"MIDI device availability check failed: {exception.Message}";

            ReportMidiAvailabilityCheckError(message);
        }
        finally
        {
            _isCheckingMidiDevices = false;
        }
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
        string? preferredInputName = null,
        string? preferredOutputName = null,
        bool restoreSavedSelections = false,
        bool updateStatus = true)
    {
        if (_midiConnectionService.IsRunning)
        {
            return;
        }

        var selectedInput = restoreSavedSelections
            ? preferredInputName
            : InputDeviceComboBox.SelectedItem as string;
        var selectedOutput = restoreSavedSelections
            ? preferredOutputName
            : OutputDeviceComboBox.SelectedItem as string;

        if (!restoreSavedSelections)
        {
            selectedInput ??= _unavailableSavedInputDeviceName;
            selectedOutput ??= _unavailableSavedOutputDeviceName;
        }

        var inputDeviceNames = _midiDeviceService.GetInputDeviceNames();
        var outputDeviceNames = _midiDeviceService.GetOutputDeviceNames();

        _isRefreshingMidiDevices = true;

        try
        {
            ReplaceComboBoxItems(InputDeviceComboBox, inputDeviceNames);
            ReplaceComboBoxItems(OutputDeviceComboBox, outputDeviceNames);

            RestoreDeviceSelection(
                InputDeviceComboBox,
                inputDeviceNames,
                selectedInput,
                restoreSavedSelections,
                isInput: true);
            RestoreDeviceSelection(
                OutputDeviceComboBox,
                outputDeviceNames,
                selectedOutput,
                restoreSavedSelections,
                isInput: false);
        }
        finally
        {
            _isRefreshingMidiDevices = false;
        }

        UpdateConnectionUi(isRunning: false);

        if (!updateStatus)
        {
            return;
        }

        if (TrySetUnavailableDeviceStatus())
        {
            return;
        }

        if (inputDeviceNames.Count == 0)
        {
            SetStatus("No MIDI input devices");
        }
        else if (outputDeviceNames.Count == 0)
        {
            SetStatus("No MIDI output devices");
        }
        else
        {
            SetConnectionStatus(isRunning: false);
        }
    }

    private void RestoreDeviceSelection(
        ComboBox comboBox,
        IReadOnlyList<string> availableNames,
        string? preferredName,
        bool restoringSavedSelection,
        bool isInput)
    {
        if (preferredName is not null)
        {
            var availableName = availableNames.FirstOrDefault(name =>
                string.Equals(name, preferredName, StringComparison.Ordinal));

            if (availableName is not null)
            {
                comboBox.SelectedItem = availableName;

                if (isInput)
                {
                    ClearUnavailableInputDevice();
                }
                else
                {
                    ClearUnavailableOutputDevice();
                }

                return;
            }

            comboBox.SelectedIndex = -1;

            if (isInput)
            {
                var wasSaved =
                    _unavailableInputWasSaved &&
                    string.Equals(
                        _unavailableSavedInputDeviceName,
                        preferredName,
                        StringComparison.Ordinal);
                _unavailableSavedInputDeviceName = preferredName;
                _unavailableInputWasSaved = restoringSavedSelection || wasSaved;
            }
            else
            {
                var wasSaved =
                    _unavailableOutputWasSaved &&
                    string.Equals(
                        _unavailableSavedOutputDeviceName,
                        preferredName,
                        StringComparison.Ordinal);
                _unavailableSavedOutputDeviceName = preferredName;
                _unavailableOutputWasSaved = restoringSavedSelection || wasSaved;
            }

            return;
        }

        comboBox.SelectedIndex = availableNames.Count > 0 ? 0 : -1;
    }

    private static void ReplaceComboBoxItems(
        ComboBox comboBox,
        IReadOnlyList<string> deviceNames)
    {
        comboBox.Items.Clear();

        foreach (var deviceName in deviceNames)
        {
            comboBox.Items.Add(deviceName);
        }
    }

    private void StopMidiConnection(string statusMessage, bool refreshDevices)
    {
        _midiConnectionService.CancelMidiLearn();
        _midiDeviceCheckTimer.Stop();
        _midiConnectionService.Stop();
        ResetMidiLearnUi();
        UpdateConnectionUi(isRunning: false);

        if (refreshDevices)
        {
            RefreshMidiDevices(updateStatus: false);
        }

        SetStatus(statusMessage);
    }

    private void ReportMidiAvailabilityCheckError(string message)
    {
        if (_midiAvailabilityCheckErrorReported)
        {
            return;
        }

        _midiAvailabilityCheckErrorReported = true;
        MidiConnectionService_ConnectionError(
            this,
            new MidiConnectionErrorEventArgs(DateTime.Now, message));
    }

    private void UpdateStatusAfterManualSelection()
    {
        if (_midiConnectionService.IsRunning)
        {
            return;
        }

        UpdateConnectionUi(isRunning: false);

        if (!TrySetUnavailableDeviceStatus())
        {
            SetConnectionStatus(isRunning: false);
        }
    }

    private bool TrySetUnavailableDeviceStatus()
    {
        var inputName = _unavailableSavedInputDeviceName;
        var outputName = _unavailableSavedOutputDeviceName;

        if (inputName is null && outputName is null)
        {
            return false;
        }

        if (inputName is not null && outputName is not null)
        {
            if (_unavailableInputWasSaved && _unavailableOutputWasSaved)
            {
                SetStatus(
                    $"Saved MIDI devices are unavailable: input \"{inputName}\", output \"{outputName}\"");
            }
            else
            {
                var inputPrefix = _unavailableInputWasSaved ? "Saved" : "Selected";
                var outputPrefix = _unavailableOutputWasSaved ? "saved" : "selected";
                SetStatus(
                    $"{inputPrefix} MIDI input device is unavailable: {inputName}; " +
                    $"{outputPrefix} MIDI output device is unavailable: {outputName}");
            }

            return true;
        }

        if (inputName is not null)
        {
            SetStatus(_unavailableInputWasSaved
                ? $"Saved MIDI input device is unavailable: {inputName}"
                : $"Selected MIDI input device is unavailable: {inputName}");
            return true;
        }

        SetStatus(_unavailableOutputWasSaved
            ? $"Saved MIDI output device is unavailable: {outputName}"
            : $"Selected MIDI output device is unavailable: {outputName}");
        return true;
    }

    private void ClearUnavailableInputDevice()
    {
        _unavailableSavedInputDeviceName = null;
        _unavailableInputWasSaved = false;
    }

    private void ClearUnavailableOutputDevice()
    {
        _unavailableSavedOutputDeviceName = null;
        _unavailableOutputWasSaved = false;
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
        StartButton.IsEnabled =
            !isRunning &&
            InputDeviceComboBox.SelectedItem is string &&
            OutputDeviceComboBox.SelectedItem is string;
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
