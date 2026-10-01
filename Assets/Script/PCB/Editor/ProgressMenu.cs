using Pcb;
using UnityEditor;
using UnityEngine;

/// <summary>Testing helpers for the saved stage progress (players can't reset it).</summary>
static class ProgressMenu
{
    [MenuItem("Tools/PCB/Reset Progress")]
    static void ResetProgress()
    {
        Pcb.Progress.Reset();
        Debug.Log("[PCB] Progress reset: only stage 1 is unlocked.");
    }

    [MenuItem("Tools/PCB/Unlock All Stages")]
    static void UnlockAll()
    {
        var list = PcbAssetSetup.GetOrCreateLevelList();
        Pcb.Progress.UnlockAll(list.Count);
        Debug.Log($"[PCB] Progress: all {list.Count} stages unlocked.");
    }
}
