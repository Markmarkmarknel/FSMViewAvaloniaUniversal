using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using FSMExpress.Common.Assets;
using FSMExpress.Logic.Util;
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

    private List<FsmSelectorListEntry> _internalEntries = [];
    private readonly AssetsManager _manager;
    private readonly AssetsFileInstance _fileInst;
    private readonly Action<string> _searchDb;

    public FsmListPanelViewModel(AssetsManager manager, AssetsFileInstance fileInst)
    {
        _manager = manager;
        _fileInst = fileInst;
        _searchDb = DebounceUtils.Debounce<string>(FilterEntries, 300);

        // Initialize collections
        _entries = new ObservableCollection<FsmSelectorListEntry>();
        _searchModes = new ObservableCollection<FsmSearchMode>(Enum.GetValues<FsmSearchMode>());
    }

    // Delegate for progress reporting
    public delegate void ProgressReportHandler(string message, int processed, int total);
    public event ProgressReportHandler? ProgressChanged;

    // Handlers for property changes
    partial void OnSearchTextChanged(string value) => _searchDb(value);
    partial void OnSearchModeChanged(FsmSearchMode value) => _searchDb(SearchText);

    private void FilterEntries(string searchText)
    {
        Entries.Clear();
        
        // Apply search filtering
        var filtered = _internalEntries.AsEnumerable();
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

        foreach (var entry in filtered)
        {
            Entries.Add(entry);
        }
    }

    public async Task FillFsmEntries()
    {
        SearchText = "Loading...";
        _internalEntries.Clear();
        
        if (!_manager.LoadMonoBehaviours(_fileInst))
        {
            await MessageBoxUtil.ShowDialog("Mono error", "Couldn't find game assemblies. Check your Managed or il2cpp_data folder?");
            return;
        }

        // Get script indices on a background thread
        var (playMakerFsmSis, fsmTemplateSis) = await Task.Run(() => 
        {
            var pFsmSis = new HashSet<ushort>();
            var fTempSis = new HashSet<ushort>();
            var scriptInfos = AssetHelper.GetAssetsFileScriptInfos(_manager, _fileInst);
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

        // First count total FSMs to process
        int totalFsms = await Task.Run(() => 
        {
            int count = 0;
            var file = _fileInst.file;
            foreach (var info in file.AssetInfos)
            {
                if (info.TypeId != (int)AssetClassID.MonoBehaviour)
                    continue;

                var infoSi = info.GetScriptIndex(_fileInst.file);
                if (infoSi == ushort.MaxValue)
                    continue;

                if (playMakerFsmSis.Contains(infoSi))
                {
                    count++;
                }
            }
            return count;
        });

        ProgressChanged?.Invoke("Processing FSMs", 0, totalFsms);

        // Process asset infos on a background thread with progress reporting
        await Task.Run(() => 
        {
            var file = _fileInst.file;
            var afNamer = new AfAssetNamer(_manager, _fileInst);
            int processed = 0;
            
            foreach (var info in file.AssetInfos)
            {
                if (info.TypeId != (int)AssetClassID.MonoBehaviour)
                    continue;

                var infoSi = info.GetScriptIndex(_fileInst.file);
                if (infoSi == ushort.MaxValue)
                    continue;

                if (playMakerFsmSis.Contains(infoSi))
                {
                    var (fsmName, stateCount, transitionCount) = GetFSMDetailsExtended(_manager, _fileInst, info, afNamer);
                    var fsmPtr = new AssetPPtr(_fileInst.name, info.PathId);
                    _internalEntries.Add(new FsmSelectorListEntry(fsmName, fsmPtr, stateCount, transitionCount));
                    
                    processed++;
                    if (processed % 10 == 0 || processed == totalFsms) // Report every 10 FSMs or on completion
                    {
                        ProgressChanged?.Invoke("Processing FSMs", processed, totalFsms);
                    }
                }
            }
        });

        SearchText = "";
        FilterEntries(string.Empty);
    }

    private static (string name, int stateCount, int transitionCount) GetFSMDetailsExtended(
        AssetsManager manager, AssetsFileInstance fileInst, AssetFileInfo info, AfAssetNamer namer)
    {
        var fsmTemp = manager.GetTemplateBaseField(fileInst, info);
        AssetTypeValueField? monoBf;
        lock (fileInst.LockReader)
        {
            monoBf = fsmTemp.MakeValue(fileInst.file.Reader, info.GetAbsoluteByteOffset(fileInst.file));
        }
        var fsmData = monoBf["fsm"];
        var fsmName = fsmData["name"].AsString;
        var goPtr = monoBf["m_GameObject"];
        var goName = namer.GetName(goPtr["m_FileID"].AsInt, goPtr["m_PathID"].AsLong);

        // Use the robust parser for accurate counts
        int stateCount = 0;
        int transitionCount = 0;
        try
        {
            var afField = new FSMExpress.Common.Assets.AfAssetField(fsmData, namer);
            var fsm = new FSMExpress.PlayMaker.FsmPlaymaker(afField);
            stateCount = fsm.States?.Count ?? 0;
            transitionCount = fsm.States?.Sum(s => s.Transitions?.Count ?? 0) ?? 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FSM count parse error: {ex.Message}");
        }
        return ($"{goName} - {fsmName}", stateCount, transitionCount);
    }

    // Keep the legacy method for any code that might still be using it
    private static string GetFSMNameFast(AssetsManager manager, AssetsFileInstance fileInst, AssetFileInfo info, AfAssetNamer namer)
    {
        var (name, _, _) = GetFSMDetailsExtended(manager, fileInst, info, namer);
        return name;
    }
}