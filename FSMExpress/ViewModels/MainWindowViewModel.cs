using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using FSMExpress.Common.Assets;
using FSMExpress.Common.Document;
using FSMExpress.Logic.Util;
using FSMExpress.PlayMaker;
using FSMExpress.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace FSMExpress.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AssetsManager _manager = new();
    private string? _lastOpenedFile;
    private readonly ISettingsService _settingsService;

    // Collection to track all loaded files
    [ObservableProperty]
    private ObservableCollection<string> _loadedFiles = new();

    // Collection to track all file instances
    private Dictionary<string, AssetsFileInstance> _fileInstances = new();

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
    
    [ObservableProperty]
    private bool _showInstructions = true;

    // New file count property to show in the status bar
    [ObservableProperty]
    private string _fileCountStatus = "No files loaded";

    // Track the overall loading operation status
    private bool _isFileLoadingOperation = false;
    private bool _isFsmProcessingOperation = false;
    private int _totalFilesToLoad = 0;
    private int _successfullyLoadedFiles = 0;
    private int _filesBeingProcessed = 0;
    
    // A lock object for thread-safe status updates
    private readonly object _statusLock = new();

    partial void OnLoadedFilesChanged(ObservableCollection<string> value)
    {
        UpdateFileCountStatus();
    }

    private void UpdateFileCountStatus()
    {
        int count = LoadedFiles.Count;
        FileCountStatus = count switch
        {
            0 => "No files loaded",
            1 => "1 file loaded",
            _ => $"{count} files loaded"
        };
    }

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
            // Check if the file is already loaded
            if (_fileInstances.ContainsKey(fileName))
            {
                StatusMessage = $"File {Path.GetFileName(fileName)} is already loaded";
                return true;
            }

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

            // Add to tracked files
            _fileInstances[fileName] = fileInst;
            LoadedFiles.Add(fileName);
            
            // Create or update FsmList
            await CreateOrUpdateFsmList();

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
        }
    }

    private async Task CreateOrUpdateFsmList()
    {
        // If we don't have a list yet, create it with the first file
        if (FsmList == null && _fileInstances.Count > 0)
        {
            FsmList = new FsmListPanelViewModel(_manager);
            FsmList.ProgressChanged += OnFsmLoadProgressChanged;
        }
        
        if (FsmList != null && _fileInstances.Count > 0)
        {
            // Begin FSM processing with status tracking
            BeginFsmProcessingOperation();
            
            // Allow UI to update
            await Task.Delay(10);
            
            // Process files in parallel for better performance
            await ProcessFilesInParallel(FsmList);
            
            // Complete FSM processing with status tracking
            CompleteFsmProcessingOperation();
        }
    }
    
    /// <summary>
    /// Process FSM entries from multiple files in parallel
    /// </summary>
    private async Task ProcessFilesInParallel(FsmListPanelViewModel fsmList)
    {
        // Make a copy to avoid concurrent modification
        var filesToProcess = _fileInstances.Values
            .Where(file => !fsmList.ProcessedFiles.Contains(file.name))
            .ToList();
            
        if (filesToProcess.Count == 0)
            return;
            
        // Single file case - just use the standard method
        if (filesToProcess.Count == 1)
        {
            await fsmList.AddFileInstance(filesToProcess[0]);
            return;
        }
        
        // For multiple files, we can process them in parallel
        // Limit parallelism to avoid overloading the system
        int maxParallelism = Math.Max(1, Environment.ProcessorCount / 2);
        var options = new ParallelOptions 
        { 
            MaxDegreeOfParallelism = maxParallelism 
        };
        
        // Create semaphore to limit parallel operations
        using var semaphore = new System.Threading.SemaphoreSlim(maxParallelism);
        
        // Start processing all files
        var tasks = filesToProcess.Select(async fileInst => 
        {
            await semaphore.WaitAsync();
            
            try
            {
                await fsmList.AddFileInstance(fileInst);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();
        
        // Wait for all files to be processed
        await Task.WhenAll(tasks);
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
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open assets file(s)",
            AllowMultiple = true
        });

        if (files.Count == 0)
            return;

        // Begin tracking file loading operation
        BeginFileLoadingOperation(files.Count);
        
        // Use parallel file loading to improve performance
        int successCount = await LoadFilesInParallel(files.Select(f => f.Path.LocalPath).ToList());
        
        // Complete the file loading operation
        CompleteFileLoadingOperation(false);
    }
    
    /// <summary>
    /// Loads multiple files in parallel to improve performance
    /// </summary>
    private async Task<int> LoadFilesInParallel(List<string> filePaths)
    {
        if (filePaths.Count == 0)
            return 0;
            
        // Single file case - just use the standard method
        if (filePaths.Count == 1)
        {
            bool success = await OpenFsmFile(filePaths[0]);
            return success ? 1 : 0;
        }
        
        int successCount = 0;
        int totalFiles = filePaths.Count;
        
        // Filter out already loaded files
        var filesToLoad = filePaths.Where(path => !_fileInstances.ContainsKey(path)).ToList();
        if (filesToLoad.Count == 0)
        {
            UpdateStatus("All selected files are already loaded", false);
            return 0;
        }
        
        // First load the class database to ensure it's ready
        var firstFile = await Task.Run(() => _manager.LoadAssetsFile(filesToLoad[0]));
        if (!_manager.LoadClassDatabase(firstFile))
        {
            await MessageBoxUtil.ShowDialog("Class Database failed to load", 
                "Couldn't load class database class. Check if classdata.tpk exists?");
            _manager.UnloadAssetsFile(firstFile);
            return 0;
        }
        
        // Add the first file
        lock (_fileInstances)
        {
            _fileInstances[filesToLoad[0]] = firstFile;
        }
        
        // Add to tracked files (UI collection)
        await Dispatcher.UIThread.InvokeAsync(() => 
        {
            LoadedFiles.Add(filesToLoad[0]);
        });
        
        // Report first file progress
        UpdateFileLoadingProgress(filesToLoad[0], true);
        successCount++;
        
        // Load remaining files in parallel
        var remainingFiles = filesToLoad.Skip(1).ToList();
        var parallelOptions = new ParallelOptions 
        { 
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) 
        };
        
        await Task.Run(() => 
        {
            Parallel.ForEach(remainingFiles, parallelOptions, filePath => 
            {
                try
                {
                    // Load the file
                    var fileInst = _manager.LoadAssetsFile(filePath);
                    
                    // Add to private collection (thread-safe)
                    lock (_fileInstances)
                    {
                        _fileInstances[filePath] = fileInst;
                    }
                    
                    // Add to UI collection (must be on UI thread)
                    Dispatcher.UIThread.InvokeAsync(() => 
                    {
                        LoadedFiles.Add(filePath);
                    }).Wait();
                    
                    // Report success using our thread-safe progress reporting
                    lock (_statusLock)
                    {
                        successCount++;
                        UpdateFileLoadingProgress(filePath, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading file {filePath}: {ex.Message}");
                    // Report failure using our thread-safe progress reporting
                    UpdateFileLoadingProgress(filePath, false);
                }
            });
        });
        
        // Set the last opened file
        if (successCount > 0)
        {
            _lastOpenedFile = filesToLoad[0]; // Use the first successfully loaded file
            UpdateOpenLastText();
        }
        
        // Now process all FSMs for the loaded files
        if (successCount > 0)
        {
            BeginFsmProcessingOperation();
            await CreateOrUpdateFsmList();
            CompleteFsmProcessingOperation();
        }
        
        return successCount;
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

    public void ToggleInstructions()
    {
        ShowInstructions = !ShowInstructions;
    }

    // File Management Operations
    public async Task CloseFile(string filePath)
    {
        if (!_fileInstances.TryGetValue(filePath, out var fileInstance))
            return;
            
        // Unload the asset file
        _manager.UnloadAssetsFile(fileInstance);
        
        // Remove from collections
        _fileInstances.Remove(filePath);
        LoadedFiles.Remove(filePath);
        
        // Re-create FSM list if there are still files loaded
        if (_fileInstances.Count > 0)
        {
            await CreateOrUpdateFsmList();
            StatusMessage = $"Closed {Path.GetFileName(filePath)}";
        }
        else
        {
            // Clear UI if no files are loaded
            if (FsmList != null)
            {
                FsmList.ProgressChanged -= OnFsmLoadProgressChanged;
                FsmList = null;
            }
            ActiveDocument = null;
            SelectedNode = null;
            StatusMessage = "Ready";
        }
        
        // Update file count
        UpdateFileCountStatus();
    }

    public async void CloseTabs()
    {
        if (LoadedFiles.Count > 0 && _lastOpenedFile != null)
        {
            await CloseFile(_lastOpenedFile);
        }
    }

    public async void CloseAllTabs()
    {
        if (LoadedFiles.Count == 0)
            return;
            
        IsLoading = true;
        StatusMessage = "Closing all files...";
            
        // Make a copy to avoid collection modification issues during iteration
        var filesToClose = LoadedFiles.ToList();
        
        foreach (var file in filesToClose)
        {
            await CloseFile(file);
        }
        
        StatusMessage = "All files closed";
        IsLoading = false;
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
    
    /// <summary>
    /// Updates the status message in a thread-safe manner and ensures consistency
    /// </summary>
    private void UpdateStatus(string message, bool isLoading = true)
    {
        lock (_statusLock)
        {
            // Update the UI thread safely
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = message;
                IsLoading = isLoading;
            });
        }
    }
    
    /// <summary>
    /// Begins tracking a file loading operation 
    /// </summary>
    private void BeginFileLoadingOperation(int totalFiles)
    {
        lock (_statusLock)
        {
            _isFileLoadingOperation = true;
            _totalFilesToLoad = totalFiles;
            _successfullyLoadedFiles = 0;
            _filesBeingProcessed = 0;
            UpdateStatus($"Loading {totalFiles} file(s)...");
        }
    }
    
    /// <summary>
    /// Updates file loading progress
    /// </summary>
    private void UpdateFileLoadingProgress(string fileName, bool success)
    {
        lock (_statusLock)
        {
            _filesBeingProcessed++;
            if (success)
            {
                _successfullyLoadedFiles++;
            }
            
            // Calculate percentage and update UI
            int percentage = _totalFilesToLoad > 0 ? (int)((_filesBeingProcessed / (double)_totalFilesToLoad) * 100) : 0;
            
            // Only update if we're still in a file loading operation
            if (_isFileLoadingOperation)
            {
                UpdateStatus($"Loading files: {_filesBeingProcessed}/{_totalFilesToLoad} ({percentage}%)");
                
                // Update progress percentage
                Dispatcher.UIThread.InvokeAsync(() => 
                {
                    ProgressPercentage = percentage;
                });
            }
        }
    }
    
    /// <summary>
    /// Completes a file loading operation with a final status message
    /// </summary>
    private void CompleteFileLoadingOperation(bool startFsmProcessing = true)
    {
        lock (_statusLock)
        {
            // Only complete if we're in a file loading operation
            if (_isFileLoadingOperation)
            {
                _isFileLoadingOperation = false;
                
                // If any files were loaded, show the summary
                if (_totalFilesToLoad > 0)
                {
                    UpdateStatus($"Loaded {_successfullyLoadedFiles} of {_totalFilesToLoad} file(s)");
                }
                
                // If we're not starting FSM processing, mark loading as complete
                if (!startFsmProcessing)
                {
                    UpdateStatus("Ready", false);
                }
            }
        }
    }
    
    /// <summary>
    /// Begins tracking FSM processing operation
    /// </summary>
    private void BeginFsmProcessingOperation()
    {
        lock (_statusLock)
        {
            _isFsmProcessingOperation = true;
            UpdateStatus("Processing FSM entries from all files...");
        }
    }
    
    /// <summary>
    /// Completes FSM processing operation
    /// </summary>
    private void CompleteFsmProcessingOperation()
    {
        lock (_statusLock)
        {
            if (_isFsmProcessingOperation)
            {
                _isFsmProcessingOperation = false;
                UpdateStatus($"Ready", false);
                
                // Reset progress bar
                Dispatcher.UIThread.InvokeAsync(() => 
                {
                    ProgressPercentage = 0;
                });
            }
        }
    }
}
