using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using FSMExpress.Common.Assets;
using FSMExpress.Logic.Util;
using FSMExpress.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FSMExpress.ViewModels;

public partial class FsmListPanelViewModel : ViewModelBase
{
    // Search modes for FSM entries
    public enum FsmSearchMode
    {
        Contains,
        ExactMatch,
        BeginsWith,
        EndsWith
    }
    
    // Display density options for the DataGrid
    public enum DisplayDensity
    {
        Compact,
        Normal,
        Comfortable
    }

    // Batch processing configuration
    private const int DefaultBatchSize = 25;
    private const int MinimumBatchSize = 5;
    private const int MaximumBatchSize = 100;
    
    [ObservableProperty]
    private int _processingBatchSize = DefaultBatchSize;

    // Auto-adjust batch size based on system performance
    private bool _adaptiveBatchSize = true;
    private readonly System.Diagnostics.Stopwatch _batchStopwatch = new();
    private const int TargetBatchProcessingTimeMs = 500; // Target time for processing a batch

    [ObservableProperty]
    private string _searchText = "";
    [ObservableProperty]
    private FsmSearchMode _searchMode = FsmSearchMode.Contains;
    [ObservableProperty]
    private FsmSelectorListEntry? _selectedEntry;
    [ObservableProperty]
    private ObservableCollection<FsmSelectorListEntry> _entries;
    [ObservableProperty]
    private ObservableCollection<FsmSearchMode> _searchModes;
    [ObservableProperty]
    private ObservableCollection<FsmSelectorListEntry> _fsmList = new();
    
    // Column visibility properties
    [ObservableProperty]
    private bool _showNameColumn = true;
    [ObservableProperty]
    private bool _showStatesColumn = true;
    [ObservableProperty]
    private bool _showTransitionsColumn = true;
    [ObservableProperty]
    private bool _showFileNameColumn = true;
    
    // Display density properties
    [ObservableProperty]
    private ObservableCollection<DisplayDensity> _displayDensityOptions;
    [ObservableProperty]
    private DisplayDensity _selectedDisplayDensity = DisplayDensity.Normal;

    // Sort order property
    [ObservableProperty]
    private bool _sortAscending = true;

    // New property to track processed files
    [ObservableProperty]
    private ObservableCollection<string> _processedFiles = new();

    private List<FsmSelectorListEntry> _internalEntries = [];
    private readonly AssetsManager _manager;
    private readonly Action<string> _searchDb;
    private readonly ISettingsService _settingsService;

    // Constructor that only takes the manager - used for multi-file support
    public FsmListPanelViewModel(AssetsManager manager)
    {
        _manager = manager;
        _searchDb = DebounceUtils.Debounce<string>(FilterEntries, 300);
        
        // Get the settings service from the dependency injection container
        _settingsService = Ioc.Default.GetService<ISettingsService>() ?? new SettingsService();

        // Initialize collections
        _entries = new ObservableCollection<FsmSelectorListEntry>();
        _searchModes = new ObservableCollection<FsmSearchMode>(Enum.GetValues<FsmSearchMode>());
        _displayDensityOptions = new ObservableCollection<DisplayDensity>(Enum.GetValues<DisplayDensity>());
        
        // Load settings from the settings service
        LoadSettings();
        
        // Initialize display settings
        ApplyDisplayDensity(_selectedDisplayDensity);
    }

    // Constructor that takes both manager and fileInst - for backward compatibility
    public FsmListPanelViewModel(AssetsManager manager, AssetsFileInstance fileInst) : this(manager)
    {
        // Add the file instance immediately
        _ = AddFileInstance(fileInst);
    }
    
    private void LoadSettings()
    {
        // Load column visibility settings
        ShowNameColumn = _settingsService.ShowNameColumn;
        ShowStatesColumn = _settingsService.ShowStatesColumn;
        ShowTransitionsColumn = _settingsService.ShowTransitionsColumn;
        ShowFileNameColumn = _settingsService.GetSetting("ShowFileNameColumn", true);
        
        // Load search mode setting
        if (Enum.TryParse<FsmSearchMode>(_settingsService.SearchMode, out var searchMode))
        {
            SearchMode = searchMode;
        }
        
        // Load display density setting
        if (Enum.TryParse<DisplayDensity>(_settingsService.DisplayDensity, out var displayDensity))
        {
            SelectedDisplayDensity = displayDensity;
        }
        
        // Load sort order setting
        SortAscending = _settingsService.SortAscending;
    }

    // Delegate for progress reporting
    public delegate void ProgressReportHandler(string message, int processed, int total);
    public event ProgressReportHandler? ProgressChanged;

    // Handlers for property changes
    partial void OnSearchTextChanged(string value) => _searchDb(value);
    
    partial void OnSearchModeChanged(FsmSearchMode value)
    {
        _searchDb(SearchText);
        _settingsService.SearchMode = value.ToString();
    }
    
    partial void OnSelectedDisplayDensityChanged(DisplayDensity value)
    {
        ApplyDisplayDensity(value);
        _settingsService.DisplayDensity = value.ToString();
    }
    
    partial void OnShowNameColumnChanged(bool value) => _settingsService.ShowNameColumn = value;
    partial void OnShowStatesColumnChanged(bool value) => _settingsService.ShowStatesColumn = value;
    partial void OnShowTransitionsColumnChanged(bool value) => _settingsService.ShowTransitionsColumn = value;
    partial void OnShowFileNameColumnChanged(bool value) => _settingsService.SetSetting("ShowFileNameColumn", value);
    
    partial void OnSortAscendingChanged(bool value)
    {
        _settingsService.SortAscending = value;
        _searchDb(SearchText); // Refresh the list to apply the new sort order
    }

    // Apply display density to the DataGrid rows (will be connected to view through attached properties)
    private void ApplyDisplayDensity(DisplayDensity density)
    {
        // This will be used by the view to adjust row heights
        RowHeight = density switch
        {
            DisplayDensity.Compact => 24,
            DisplayDensity.Normal => 32,
            DisplayDensity.Comfortable => 40,
            _ => 32
        };
        
        // Update column headers based on density
        if (density == DisplayDensity.Compact)
        {
            StatesColumnHeader = "S";
            TransitionsColumnHeader = "T";
            FileNameColumnHeader = "F";
        }
        else
        {
            StatesColumnHeader = "States";
            TransitionsColumnHeader = "Transitions";
            FileNameColumnHeader = "File";
        }
    }
    
    [ObservableProperty]
    private int _rowHeight = 32; // Default to Normal
    
    // Properties for column headers that change with density
    [ObservableProperty]
    private string _statesColumnHeader = "States";
    
    [ObservableProperty]
    private string _transitionsColumnHeader = "Transitions";
    
    [ObservableProperty]
    private string _fileNameColumnHeader = "File";

    private void FilterEntries(string searchText)
    {
        // Create a safe copy of the internal entries to avoid concurrent modification
        var entriesCopy = _internalEntries.ToList();
        
        Entries.Clear();
        
        // Apply search filtering on the copy
        var filtered = entriesCopy.AsEnumerable();
        if (!string.IsNullOrEmpty(searchText))
        {
            filtered = SearchMode switch
            {
                FsmSearchMode.Contains => filtered.Where(e => e.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)),
                FsmSearchMode.ExactMatch => filtered.Where(e => e.Name.Equals(searchText, StringComparison.OrdinalIgnoreCase)),
                FsmSearchMode.BeginsWith => filtered.Where(e => e.Name.StartsWith(searchText, StringComparison.OrdinalIgnoreCase)),
                FsmSearchMode.EndsWith => filtered.Where(e => e.Name.EndsWith(searchText, StringComparison.OrdinalIgnoreCase)),
                _ => filtered
            };
        }
        
        // Apply sorting
        if (SortAscending)
        {
            filtered = filtered.OrderBy(e => e.Name);
        }
        else
        {
            filtered = filtered.OrderByDescending(e => e.Name);
        }

        // Convert filtered results to a list before adding to avoid enumeration issues
        var filteredList = filtered.ToList();
        foreach (var entry in filteredList)
        {
            Entries.Add(entry);
        }
    }

    // Add FSM entries from a specific file instance
    public async Task AddFileInstance(AssetsFileInstance fileInst)
    {
        // Check if this file has already been processed
        if (ProcessedFiles.Contains(fileInst.name))
            return;
            
        SearchText = "Loading...";
        
        if (!_manager.LoadMonoBehaviours(fileInst))
        {
            await MessageBoxUtil.ShowDialog("Mono error", "Couldn't find game assemblies. Check your Managed or il2cpp_data folder?");
            return;
        }

        // Get script indices on a background thread
        var (playMakerFsmSis, fsmTemplateSis) = await Task.Run(() => 
        {
            var pFsmSis = new HashSet<ushort>();
            var fTempSis = new HashSet<ushort>();
            var scriptInfos = AssetHelper.GetAssetsFileScriptInfos(_manager, fileInst);
            foreach (var scriptInfo in scriptInfos)
            {
                var scriptInfoRef = scriptInfo.Value;
                var asmName = scriptInfoRef.AsmName;
                var nameSpace = scriptInfoRef.Namespace;
                var className = scriptInfoRef.ClassName;
                if (asmName == "PlayMaker.dll" && nameSpace == "" && className == "PlayMakerFSM")
                    pFsmSis.Add((ushort)scriptInfo.Key);
                if (asmName == "PlayMaker.dll" && nameSpace == "" && className == "FsmTemplate")
                    fTempSis.Add((ushort)scriptInfo.Key);
            }
            return (pFsmSis, fTempSis);
        });

        // Get all FSM infos to process as a batch
        var fsmInfos = await Task.Run(() => 
        {
            var file = fileInst.file;
            var result = new List<AssetFileInfo>();
            
            foreach (var info in file.AssetInfos)
            {
                if (info.TypeId != (int)AssetClassID.MonoBehaviour)
                    continue;

                var infoSi = info.GetScriptIndex(fileInst.file);
                if (infoSi == ushort.MaxValue)
                    continue;

                if (playMakerFsmSis.Contains(infoSi))
                {
                    result.Add(info);
                }
            }
            return result;
        });

        int totalFsms = fsmInfos.Count;
        string fileName = System.IO.Path.GetFileName(fileInst.name);
        ProgressChanged?.Invoke($"Processing FSMs from {fileName}", 0, totalFsms);

        // Process FSMs in batches
        await ProcessFsmInfosInBatches(fileInst, fsmInfos, fileName);

        // Add this file to the processed list
        ProcessedFiles.Add(fileInst.name);
        
        SearchText = "";
        FilterEntries(string.Empty);
    }
    
    // Process FSM infos in batches for better performance
    private async Task ProcessFsmInfosInBatches(AssetsFileInstance fileInst, List<AssetFileInfo> fsmInfos, string fileName)
    {
        int totalFsms = fsmInfos.Count;
        int processedCount = 0;
        var afNamer = new AfAssetNamer(_manager, fileInst);
        
        // Process in batches for better performance
        for (int i = 0; i < totalFsms; i += ProcessingBatchSize)
        {
            int batchSize = Math.Min(ProcessingBatchSize, totalFsms - i);
            var batch = fsmInfos.Skip(i).Take(batchSize).ToList();
            
            _batchStopwatch.Restart();
            
            // Process this batch on a background thread
            var batchResults = await Task.Run(() =>
            {
                var results = new List<(FsmSelectorListEntry entry, long processingTime)>(batchSize);
                
                foreach (var info in batch)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var (fsmName, stateCount, transitionCount) = GetFSMDetailsExtended(_manager, fileInst, info, afNamer);
                    var fsmPtr = new AssetPPtr(fileInst.name, info.PathId);
                    var entry = new FsmSelectorListEntry(fsmName, fsmPtr, stateCount, transitionCount, fileName);
                    sw.Stop();
                    
                    results.Add((entry, sw.ElapsedMilliseconds));
                }
                
                return results;
            });
            
            // Add processed entries
            foreach (var result in batchResults)
            {
                _internalEntries.Add(result.entry);
            }
            
            processedCount += batchSize;
            ProgressChanged?.Invoke($"Processing FSMs from {fileName}", processedCount, totalFsms);
            
            // Adjust batch size if adaptive batching is enabled
            if (_adaptiveBatchSize)
            {
                AdjustBatchSizeBasedOnPerformance(batchResults);
            }
            
            // Yield to allow UI updates between batches
            await Task.Delay(1);
        }
    }
    
    // Adjust batch size based on performance metrics
    private void AdjustBatchSizeBasedOnPerformance(List<(FsmSelectorListEntry entry, long processingTime)> batchResults)
    {
        if (batchResults.Count == 0)
            return;
            
        // Calculate average processing time per FSM in this batch
        double avgProcessingTimeMs = batchResults.Average(r => r.processingTime);
        
        // Determine optimal batch size to meet target batch processing time
        int optimalBatchSize = (int)(TargetBatchProcessingTimeMs / avgProcessingTimeMs);
        
        // Ensure batch size stays within reasonable bounds
        optimalBatchSize = Math.Max(MinimumBatchSize, Math.Min(optimalBatchSize, MaximumBatchSize));
        
        // Gradual adjustment to avoid wild fluctuations (move 25% toward optimal size)
        ProcessingBatchSize = (int)(0.75 * ProcessingBatchSize + 0.25 * optimalBatchSize);
    }
    
    // Clear entries from a specific file
    public void RemoveFileEntries(string fileName)
    {
        // Remove entries for this file
        _internalEntries.RemoveAll(e => e.SourceFileName == fileName);
        
        // Remove from processed files
        ProcessedFiles.Remove(fileName);
        
        // Refresh the filtered list
        FilterEntries(SearchText);
    }

    // Clear all entries
    public void ClearAllEntries()
    {
        _internalEntries.Clear();
        ProcessedFiles.Clear();
        Entries.Clear();
    }

    // Cache for GameObject names to avoid repeated lookups
    private static readonly Dictionary<string, string> _gameObjectNameCache = new();
    private static readonly object _cachelock = new();
    
    // Cache for template fields to reduce template generation overhead
    private static readonly Dictionary<(string filePath, long pathId), AssetTypeTemplateField> _templateCache = new();
    
    private static (string name, int stateCount, int transitionCount) GetFSMDetailsExtended(
        AssetsManager manager, AssetsFileInstance fileInst, AssetFileInfo info, AfAssetNamer namer)
    {
        // Get or create template field from cache
        AssetTypeTemplateField fsmTemp;
        var cacheKey = (fileInst.name, info.PathId);
        
        lock (_cachelock)
        {
            if (!_templateCache.TryGetValue(cacheKey, out fsmTemp!))
            {
                fsmTemp = manager.GetTemplateBaseField(fileInst, info);
                _templateCache[cacheKey] = fsmTemp;
                
                // Limit cache size to prevent memory issues
                if (_templateCache.Count > 500)
                {
                    // Remove oldest 100 entries when cache gets too large
                    var keysToRemove = _templateCache.Keys.Take(100).ToList();
                    foreach (var key in keysToRemove)
                    {
                        _templateCache.Remove(key);
                    }
                }
            }
        }
        
        AssetTypeValueField? monoBf;
        lock (fileInst.LockReader)
        {
            monoBf = fsmTemp.MakeValue(fileInst.file.Reader, info.GetAbsoluteByteOffset(fileInst.file));
        }
        
        var fsmData = monoBf["fsm"];
        var fsmName = fsmData["name"].AsString;
        var goPtr = monoBf["m_GameObject"];
        
        // Use cached game object name if available
        string goName;
        int fileId = goPtr["m_FileID"].AsInt;
        long pathId = goPtr["m_PathID"].AsLong;
        string cacheKeyStr = $"{fileInst.name}:{fileId}:{pathId}";
        
        lock (_cachelock)
        {
            if (!_gameObjectNameCache.TryGetValue(cacheKeyStr, out goName!))
            {
                goName = namer.GetName(fileId, pathId) ?? "Unknown";
                _gameObjectNameCache[cacheKeyStr] = goName;
                
                // Limit cache size to prevent memory issues
                if (_gameObjectNameCache.Count > 1000)
                {
                    // Remove oldest 200 entries when cache gets too large
                    var keysToRemove = _gameObjectNameCache.Keys.Take(200).ToList();
                    foreach (var key in keysToRemove)
                    {
                        _gameObjectNameCache.Remove(key);
                    }
                }
            }
        }

        // Extract state and transition counts with optimized parsing
        int stateCount = 0;
        int transitionCount = 0;
        
        try
        {
            // Try to extract counts directly without creating full FsmPlaymaker instance when possible
            if (fsmData["states"]["Array"].Children.Count > 0)
            {
                stateCount = fsmData["states"]["Array"].Children.Count;
                
                // Only create full FSM object if we need transition counts and can't get them directly
                var statesArray = fsmData["states"]["Array"];
                bool canAccessTransitionsDirect = !statesArray[0]["transitions"].IsDummy && 
                                                !statesArray[0]["transitions"]["Array"].IsDummy;
                
                if (canAccessTransitionsDirect)
                {
                    // Extract transition counts directly from the data structure
                    foreach (var state in statesArray.Children)
                    {
                        var transitionsArray = state["transitions"]["Array"];
                        if (!transitionsArray.IsDummy)
                        {
                            transitionCount += transitionsArray.Children.Count;
                        }
                    }
                }
                else
                {
                    // Fall back to creating FSM object for complex structures
                    var afField = new FSMExpress.Common.Assets.AfAssetField(fsmData, namer);
                    var fsm = new FSMExpress.PlayMaker.FsmPlaymaker(afField);
                    transitionCount = fsm.States?.Sum(s => s.Transitions?.Count ?? 0) ?? 0;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FSM count parse error: {ex.Message}");
        }
        
        return ($"{goName} - {fsmName}", stateCount, transitionCount);
    }

    // Legacy FillFsmEntries method for backwards compatibility
    public async Task FillFsmEntries()
    {
        // Nothing to do if we don't have a file instance
        // This should not happen with the new architecture but keeping it for safety
        await Task.CompletedTask; // Add await to satisfy the compiler warning
    }
}