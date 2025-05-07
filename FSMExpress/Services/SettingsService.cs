using System;
using System.IO;
using System.Text.Json;

namespace FSMExpress.Services;

public interface ISettingsService
{
    bool IsDarkMode { get; set; }
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
    }
}