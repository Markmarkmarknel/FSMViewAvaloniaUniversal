using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
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
    }

    private void UpdateOpenLastText()
    {
        OpenLastEnabled = !string.IsNullOrEmpty(_lastOpenedFile) && File.Exists(_lastOpenedFile);
        OpenLastMenuText = _lastOpenedFile != null ? $"Open Last ({Path.GetFileName(_lastOpenedFile)})" : "Open Last";
    }

    private async Task<bool> OpenFsmFile(string fileName)
    {
        var fileInst = _manager.LoadAssetsFile(fileName);
        if (!_manager.LoadClassDatabase(fileInst))
        {
            await MessageBoxUtil.ShowDialog("Class Database failed to load", "Couldn't load class database class. Check if classdata.tpk exists?");
            return false;
        }

        _currentFileInstance = fileInst;
        FsmList = new FsmListPanelViewModel(_manager, fileInst);
        await FsmList.FillFsmEntries();

        _lastOpenedFile = fileName;
        UpdateOpenLastText();
        return true;
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
