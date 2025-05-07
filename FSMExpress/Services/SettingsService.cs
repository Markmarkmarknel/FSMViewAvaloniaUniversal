using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FSMExpress.Services;

public interface ISettingsService
{
    bool IsDarkMode { get; set; }
    
    // FSM Search preferences
    bool ShowNameColumn { get; set; }
    bool ShowStatesColumn { get; set; }
    bool ShowTransitionsColumn { get; set; }
    string SearchMode { get; set; }
    string DisplayDensity { get; set; }
    bool SortAscending { get; set; }
    
    // Generic settings methods for extensibility
    T GetSetting<T>(string key, T defaultValue);
    void SetSetting<T>(string key, T value);
    
    void SaveSettings();
    void LoadSettings();
}

public class SettingsService : ISettingsService
{
    private readonly string SETTINGS_FILE_PATH;
    private Settings _settings;

    public SettingsService()
    {
        // Get the executable directory or the application data directory
        string appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FSMExpress");
            
        // Create the directory if it doesn't exist
        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }
        
        // Set the full path to the settings file
        SETTINGS_FILE_PATH = Path.Combine(appDataPath, "settings.json");
        
        _settings = new Settings();
        LoadSettings();
    }

    public bool IsDarkMode
    {
        get => _settings.IsDarkMode;
        set
        {
            _settings.IsDarkMode = value;
            SaveSettings();
        }
    }

    public bool ShowNameColumn
    {
        get => _settings.ShowNameColumn;
        set
        {
            _settings.ShowNameColumn = value;
            SaveSettings();
        }
    }

    public bool ShowStatesColumn
    {
        get => _settings.ShowStatesColumn;
        set
        {
            _settings.ShowStatesColumn = value;
            SaveSettings();
        }
    }

    public bool ShowTransitionsColumn
    {
        get => _settings.ShowTransitionsColumn;
        set
        {
            _settings.ShowTransitionsColumn = value;
            SaveSettings();
        }
    }

    public string SearchMode
    {
        get => _settings.SearchMode;
        set
        {
            _settings.SearchMode = value;
            SaveSettings();
        }
    }

    public string DisplayDensity
    {
        get => _settings.DisplayDensity;
        set
        {
            _settings.DisplayDensity = value;
            SaveSettings();
        }
    }

    public bool SortAscending
    {
        get => _settings.SortAscending;
        set
        {
            _settings.SortAscending = value;
            SaveSettings();
        }
    }

    // Generic settings methods for extensibility
    public T GetSetting<T>(string key, T defaultValue)
    {
        if (_settings.CustomSettings.TryGetValue(key, out var value) && value is JsonElement jsonElement)
        {
            try
            {
                // Handle different types
                if (typeof(T) == typeof(bool) && jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False)
                    return (T)(object)jsonElement.GetBoolean();
                else if (typeof(T) == typeof(int) && jsonElement.ValueKind == JsonValueKind.Number)
                    return (T)(object)jsonElement.GetInt32();
                else if (typeof(T) == typeof(string) && jsonElement.ValueKind == JsonValueKind.String)
                    return (T)(object)jsonElement.GetString()!;
                else
                    return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    public void SetSetting<T>(string key, T value)
    {
        _settings.CustomSettings[key] = value!;
        SaveSettings();
    }

    public void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SETTINGS_FILE_PATH, json);
        }
        catch (Exception ex)
        {
            // Log the exception or handle it as needed
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    public void LoadSettings()
    {
        if (File.Exists(SETTINGS_FILE_PATH))
        {
            try
            {
                var json = File.ReadAllText(SETTINGS_FILE_PATH);
                var settings = JsonSerializer.Deserialize<Settings>(json);
                if (settings != null)
                {
                    _settings = settings;
                    
                    // Ensure CustomSettings dictionary exists
                    if (_settings.CustomSettings == null)
                    {
                        _settings.CustomSettings = new Dictionary<string, object>();
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception or handle it as needed
                Console.WriteLine($"Error loading settings: {ex.Message}");
                // If there's an error loading settings, just use defaults
            }
        }
    }

    private class Settings
    {
        public bool IsDarkMode { get; set; } = false;
        
        // FSM Search preferences
        public bool ShowNameColumn { get; set; } = true;
        public bool ShowStatesColumn { get; set; } = true;
        public bool ShowTransitionsColumn { get; set; } = true;
        public string SearchMode { get; set; } = "Contains";
        public string DisplayDensity { get; set; } = "Normal";
        public bool SortAscending { get; set; } = true;
        
        // Dictionary to store custom settings
        public Dictionary<string, object> CustomSettings { get; set; } = new Dictionary<string, object>();
    }
}