using AssetsTools.NET;

namespace FSMExpress.ViewModels;

public class FsmSelectorListEntry
{
    public string Name { get; }
    public AssetPPtr Ptr { get; }
    public int StateCount { get; }
    public int TransitionCount { get; }
    public string SourceFileName { get; }

    public FsmSelectorListEntry(string name, AssetPPtr ptr)
    {
        Name = name;
        Ptr = ptr;
        StateCount = 0;
        TransitionCount = 0;
        SourceFileName = System.IO.Path.GetFileName(ptr.FilePath);
    }

    public FsmSelectorListEntry(string name, AssetPPtr ptr, int stateCount, int transitionCount)
    {
        Name = name;
        Ptr = ptr;
        StateCount = stateCount;
        TransitionCount = transitionCount;
        SourceFileName = System.IO.Path.GetFileName(ptr.FilePath);
    }

    public FsmSelectorListEntry(string name, AssetPPtr ptr, int stateCount, int transitionCount, string sourceFileName)
    {
        Name = name;
        Ptr = ptr;
        StateCount = stateCount;
        TransitionCount = transitionCount;
        SourceFileName = sourceFileName;
    }
}