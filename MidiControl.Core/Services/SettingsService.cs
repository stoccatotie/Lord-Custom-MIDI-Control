using MidiControl.Core.Models;
using System.Text;
using System.Text.Json;

namespace MidiControl.Core.Services;

public sealed class SettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsService()
    {
        var applicationDataPath = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        SettingsFilePath = Path.Combine(
            applicationDataPath,
            "GuitarProMidiControl",
            "settings.json");
    }

    public string SettingsFilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return CreateDefaultSettings();
            }

            var json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateDefaultSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            return Normalize(settings);
        }
        catch
        {
            return CreateDefaultSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directoryPath = Path.GetDirectoryName(SettingsFilePath)
            ?? throw new InvalidOperationException("Settings directory could not be determined.");
        var temporaryFilePath = $"{SettingsFilePath}.tmp";

        Directory.CreateDirectory(directoryPath);

        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(temporaryFilePath, json, new UTF8Encoding(false));

            if (File.Exists(SettingsFilePath))
            {
                File.Replace(temporaryFilePath, SettingsFilePath, null);
            }
            else
            {
                File.Move(temporaryFilePath, SettingsFilePath);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryFilePath))
                {
                    File.Delete(temporaryFilePath);
                }
            }
            catch
            {
                // Preserve the original save error.
            }

            throw;
        }
    }

    private static AppSettings Normalize(AppSettings? settings)
    {
        settings ??= new AppSettings();
        settings.Mappings ??= new List<MidiMapping>();
        settings.Mappings.RemoveAll(mapping => mapping is null);

        foreach (var mapping in settings.Mappings)
        {
            if (mapping.Id == Guid.Empty)
            {
                mapping.Id = Guid.NewGuid();
            }

            if (string.IsNullOrWhiteSpace(mapping.Name))
            {
                mapping.Name = "Unnamed Mapping";
            }
        }

        if (settings.Mappings.Count == 0)
        {
            settings.Mappings.Add(CreateDefaultMapping());
        }

        return settings;
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            Mappings = new List<MidiMapping> { CreateDefaultMapping() }
        };
    }

    private static MidiMapping CreateDefaultMapping()
    {
        return new MidiMapping
        {
            Name = "REAPER Mute Test",
            IsEnabled = true,
            InputChannel = 1,
            InputNote = 67,
            MinimumVelocity = 1,
            OutputChannel = 1,
            OutputController = 20,
            OutputValue = 127
        };
    }
}
