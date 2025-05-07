using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using FSMExpress.Common.Assets;
using FSMExpress.Common.Document;
using FSMExpress.Logic.Util;
using FSMExpress.PlayMaker;
using FSMExpress.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace FSMExpress.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AssetsManager _manager = new();
    private string? _lastOpenedFile;
    private AssetsFileInstance? _currentFileInstance;
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private FsmDocument? _activeDocument = null;

    [ObservableProperty]
    private FsmDocumentNode? _selectedNode = null;

    [ObservableProperty]
    private string _openLastMenuText = "Open Last";

    [ObservableProperty]
    private bool _openLastEnabled = false;

    [ObservableProperty]
    private FsmListPanelViewModel? _fsmList;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _progressPercentage = 0;
    
    [ObservableProperty]
    private bool _isDarkMode = false;

    partial void OnFsmListChanged(FsmListPanelViewModel? value)
    {
        if (value != null)
        {
            value.PropertyChanged += FsmList_PropertyChanged;
        }
    }

    private void FsmList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FsmListPanelViewModel.SelectedEntry) && 
            sender is FsmListPanelViewModel vm &&
            vm.SelectedEntry is FsmSelectorListEntry entry)
        {
            var fsmFileInst = _manager.FileLookup[entry.Ptr.FilePath];
            var fsmBaseField = _manager.GetBaseField(fsmFileInst, entry.Ptr.PathId);
            var fsmObject = new FsmPlaymaker(new AfAssetField(fsmBaseField["fsm"], new AfAssetNamer(_manager, fsmFileInst)));
            ActiveDocument = fsmObject.MakeDocument();
        }
    }

    public MainWindowViewModel()
    {
        _manager.UseMonoTemplateFieldCache = true;
        
        // Get the settings service from the dependency injection container
        _settingsService = Ioc.Default.GetService<ISettingsService>() ?? new SettingsService();
        
        // Load dark mode setting from settings service
        IsDarkMode = _settingsService.IsDarkMode;
        
        // Apply the theme based on settings
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
        }
        
        // Save the dark mode setting
        _settingsService.IsDarkMode = value;
    }

    private void UpdateOpenLastText()
    {
        OpenLastEnabled = !string.IsNullOrEmpty(_lastOpenedFile) && File.Exists(_lastOpenedFile);
        OpenLastMenuText = _lastOpenedFile != null ? $"Open Last ({Path.GetFileName(_lastOpenedFile)})" : "Open Last";
    }

    private async Task<bool> OpenFsmFile(string fileName)
    {
        try
        {
            IsLoading = true;
            StatusMessage = $"Loading {Path.GetFileName(fileName)}...";
            
            // Allow UI to update before starting the heavy operation
            await Task.Delay(10);
            
            // Load the file on a background thread
            var fileInst = await Task.Run(() => _manager.LoadAssetsFile(fileName));
            
            if (!_manager.LoadClassDatabase(fileInst))
            {
                await MessageBoxUtil.ShowDialog("Class Database failed to load", "Couldn't load class database class. Check if classdata.tpk exists?");
                return false;
            }

            _currentFileInstance = fileInst;
            
            StatusMessage = "Processing FSM entries...";
            FsmList = new FsmListPanelViewModel(_manager, fileInst);
            
            // Subscribe to progress updates
            FsmList.ProgressChanged += OnFsmLoadProgressChanged;
            
            // Allow UI to update again after setting the status message
            await Task.Delay(10);
            
            await FsmList.FillFsmEntries();

            _lastOpenedFile = fileName;
            UpdateOpenLastText();
            
            StatusMessage = $"Loaded {Path.GetFileName(fileName)}";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
            
            // Unsubscribe from progress updates
            if (FsmList != null)
            {
                FsmList.ProgressChanged -= OnFsmLoadProgressChanged;
            }
        }
    }

    private void OnFsmLoadProgressChanged(string message, int processed, int total)
    {
        // Calculate percentage
        ProgressPercentage = total > 0 ? (int)((processed / (double)total) * 100) : 0;
        
        // Update status message with progress information
        StatusMessage = $"{message}: {processed}/{total} ({ProgressPercentage}%)";
    }

    private IStorageProvider StorageProvider =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.StorageProvider ?? throw new InvalidOperationException("Storage provider not available")
            : throw new InvalidOperationException("Storage provider not available");

    public async void FileOpen()
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open assets file",
            AllowMultiple = false
        });

        if (file.Count == 0)
            return;

        await OpenFsmFile(file[0].Path.LocalPath);
    }

    public async void FileOpenLast()
    {
        if (string.IsNullOrEmpty(_lastOpenedFile) || !File.Exists(_lastOpenedFile))
        {
            await MessageBoxUtil.ShowDialog("No previous file", "No previously opened file found.");
            return;
        }

        await OpenFsmFile(_lastOpenedFile);
    }

    public void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
    }

    // Disabled functionality placeholders
    public void FileOpenSceneList()
    {
        // Not implemented
    }

    public void FileOpenFsmJson()
    {
        // Not implemented
    }

    public void FileOpenResourcesAssets()
    {
        // Not implemented
    }

    public void ConfigSetGamePath()
    {
        // Not implemented
    }

    public void CloseTabs()
    {
        // Not implemented
    }

    public void CloseAllTabs()
    {
        // Not implemented
    }
}
