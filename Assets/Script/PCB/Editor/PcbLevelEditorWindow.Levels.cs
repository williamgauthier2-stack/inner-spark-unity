using System.Collections.Generic;
using System.IO;
using System.Text;
using Pcb;
using UnityEditor;
using UnityEngine;

/// <summary>Level saving / loading part of the PCB Level Editor. Levels are prefabs in Assets/PCB/Levels.</summary>
public partial class PcbLevelEditorWindow
{
    bool dirtyCheckDue = true;
    bool isDirtyCached;
    double lastDirtyCheck;

    void OnInspectorUpdate()
    {
        if (!EditorApplication.isPlaying && board) Repaint(); // keeps the "unsaved" status fresh
    }

    void LevelGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        string newName = EditorGUILayout.TextField("Name", board.levelName);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(board, "Rename Level");
            board.levelName = newName;
            dirtyCheckDue = true;
        }

        if (board.GetComponent<LevelManager>() || !FindAnyObjectByType<LevelManager>(FindObjectsInactive.Include))
        {
            EditorGUILayout.HelpBox("The scene needs a separate Level Manager object.", MessageType.Warning);
            if (GUILayout.Button("Set Up Level Manager")) EnsureLevelManager();
        }

        RefreshDirty();
        string savedName = Path.GetFileNameWithoutExtension(board.savedPath ?? "");
        if (string.IsNullOrEmpty(board.savedPath))
            EditorGUILayout.HelpBox("Not saved yet.", MessageType.Warning);
        else if (isDirtyCached)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox($"Unsaved changes ({savedName}).", MessageType.Warning);
                if (GUILayout.Button(new GUIContent("Why?", "Logs to the Console what differs from the saved level."), GUILayout.Width(44), GUILayout.Height(38)))
                    LogDifference();
            }
        }
        else
            EditorGUILayout.HelpBox($"Saved: {savedName}", MessageType.Info);
        if (!string.IsNullOrEmpty(savedName) && CleanName(board.levelName) != savedName)
            EditorGUILayout.HelpBox($"Name changed: saving creates a new level '{CleanName(board.levelName)}'. '{savedName}' is kept.", MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save Level", GUILayout.Height(28))) SaveLevel();
            if (GUILayout.Button("New Level", GUILayout.Height(28))) { NewLevel(); GUIUtility.ExitGUI(); }
        }
        LevelListGUI();
    }

    void LevelListGUI()
    {
        var list = PcbAssetSetup.GetOrCreateLevelList();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Level List (play order)", EditorStyles.boldLabel);
        if (list.Count == 0) EditorGUILayout.HelpBox("No saved levels yet. Draw a board and press Save Level.", MessageType.None);

        for (int i = 0; i < list.Count; i++)
        {
            var level = list.levels[i];
            bool isCurrent = board && level && board.savedPath == AssetDatabase.GetAssetPath(level);
            using (new EditorGUILayout.HorizontalScope())
            {
                string label = $"{i + 1}. {(level ? level.levelName : "(missing)")}";
                EditorGUILayout.LabelField(isCurrent ? $"▶ {label}" : label, isCurrent ? EditorStyles.boldLabel : EditorStyles.label);

                GUI.enabled = level && !isCurrent;
                if (GUILayout.Button("Load", GUILayout.Width(48))) { LoadLevel(level); GUIUtility.ExitGUI(); }
                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(24))) { MoveLevel(list, i, i - 1); GUIUtility.ExitGUI(); }
                GUI.enabled = i < list.Count - 1;
                if (GUILayout.Button("▼", GUILayout.Width(24))) { MoveLevel(list, i, i + 1); GUIUtility.ExitGUI(); }
                GUI.enabled = true;
                if (GUILayout.Button("✕", GUILayout.Width(24)) &&
                    EditorUtility.DisplayDialog("Remove Level", $"Remove '{label}' from the list?\nThe prefab file is kept in {PcbAssetSetup.LevelFolder}.", "Remove", "Cancel"))
                {
                    Undo.RecordObject(list, "Remove Level");
                    list.levels.RemoveAt(i);
                    SaveList(list);
                    GUIUtility.ExitGUI();
                }
            }
        }
    }

    static void MoveLevel(LevelList list, int from, int to)
    {
        Undo.RecordObject(list, "Reorder Levels");
        (list.levels[from], list.levels[to]) = (list.levels[to], list.levels[from]);
        SaveList(list);
    }

    static void SaveList(LevelList list)
    {
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();
    }

    // ---------------------------------------------------------------- save / load

    void SaveLevel()
    {
        string levelName = CleanName(board.levelName);
        if (string.IsNullOrEmpty(levelName))
        {
            EditorUtility.DisplayDialog("Save Level", "Give the level a name first.", "OK");
            return;
        }
        var list = PcbAssetSetup.GetOrCreateLevelList();
        string path = $"{PcbAssetSetup.LevelFolder}/{levelName}.prefab";
        if (path != board.savedPath && AssetDatabase.LoadAssetAtPath<GameObject>(path) &&
            !EditorUtility.DisplayDialog("Save Level", $"A level named '{levelName}' already exists. Overwrite it?", "Overwrite", "Cancel"))
            return;

        EnsureLevelManager(); // also removes an old LevelManager from the board so it isn't saved into the level
        if (PrefabUtility.IsPartOfPrefabInstance(board.gameObject))
            PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(board.gameObject),
                PrefabUnpackMode.Completely, InteractionMode.UserAction);

        Undo.RecordObject(board, "Save Level");
        board.levelName = levelName;
        board.savedPath = path;
        board.DestroyVisuals(); // the 3D look is generated, only the level data goes in the file
        var prefab = PrefabUtility.SaveAsPrefabAsset(board.gameObject, path, out bool success);
        board.Rebuild();
        if (!success)
        {
            Debug.LogError($"[PCB] Could not save level to {path}.");
            return;
        }

        var saved = prefab.GetComponent<Board>();
        if (!list.levels.Contains(saved))
        {
            Undo.RecordObject(list, "Add Level");
            list.levels.Add(saved);
            SaveList(list);
        }
        dirtyCheckDue = true;
        ShowNotification(new GUIContent($"Saved {levelName}"));
        if (IsDirty()) LogDifference(); // just saved, so this would be a false "unsaved" warning: say why
    }

    /// <summary>Logs every entry that differs between the level being edited and its saved prefab.</summary>
    void LogDifference()
    {
        var saved = string.IsNullOrEmpty(board.savedPath) ? null : AssetDatabase.LoadAssetAtPath<Board>(board.savedPath);
        if (!saved) { Debug.Log("[PCB] This level hasn't been saved yet."); return; }
        var current = new List<string>(Signature(board).Split('\n'));
        var stored = new List<string>(Signature(saved).Split('\n'));
        var sb = new StringBuilder($"[PCB] '{board.levelName}' differs from {board.savedPath}:\n");
        foreach (var line in current) if (!stored.Contains(line)) sb.Append("  in scene only: ").Append(line).Append('\n');
        foreach (var line in stored) if (!current.Contains(line)) sb.Append("  in saved only: ").Append(line).Append('\n');
        Debug.LogWarning(sb.ToString(), board);
    }

    void LoadLevel(Board prefab)
    {
        if (!prefab || !ConfirmDiscard()) return;
        var go = (GameObject)Instantiate(prefab.gameObject); // plain copy, not linked to the prefab
        go.name = "Board";
        var loaded = go.GetComponent<Board>();
        loaded.savedPath = AssetDatabase.GetAssetPath(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Load Level");
        if (board)
        {
            board.DestroyVisuals(); // generated objects shouldn't go into the undo history
            Undo.DestroyObjectImmediate(board.gameObject);
        }
        board = loaded;
        board.Rebuild();
        OnBoardSwitched();
        FrameBoard();
    }

    void NewLevel()
    {
        if (!ConfirmDiscard()) return;
        if (board)
        {
            board.DestroyVisuals(); // generated objects shouldn't go into the undo history
            Undo.DestroyObjectImmediate(board.gameObject);
        }
        board = null;
        CreateBoard();
        OnBoardSwitched();
    }

    void OnBoardSwitched()
    {
        CancelTrace();
        issues.Clear();
        validated = false;
        dirtyCheckDue = true;
        Selection.activeGameObject = board ? board.gameObject : null;
    }

    bool ConfirmDiscard()
    {
        if (!board || !IsDirty()) return true;
        int choice = EditorUtility.DisplayDialogComplex("Unsaved Level",
            $"'{board.levelName}' has unsaved changes. Save it first?", "Save", "Cancel", "Don't Save");
        if (choice == 1) return false;
        if (choice == 0)
        {
            SaveLevel();
            return !IsDirty();
        }
        return true;
    }

    void EnsureLevelManager()
    {
        foreach (var old in SceneLevelManagers())
            if (old.GetComponent<Board>()) Undo.DestroyObjectImmediate(old); // earlier setup put it on the board

        var manager = FindAnyObjectByType<LevelManager>(FindObjectsInactive.Include);
        if (!manager)
        {
            var go = new GameObject("Level Manager");
            manager = go.AddComponent<LevelManager>();
            Undo.RegisterCreatedObjectUndo(go, "Create Level Manager");
        }
        if (!manager.levels)
        {
            Undo.RecordObject(manager, "Assign Level List");
            manager.levels = PcbAssetSetup.GetOrCreateLevelList();
        }
    }

    // Every LevelManager in the open scene, including inactive ones (works across Unity 6 versions).
    static List<LevelManager> SceneLevelManagers()
    {
        var result = new List<LevelManager>();
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            result.AddRange(root.GetComponentsInChildren<LevelManager>(true));
        return result;
    }

    string NextLevelName()
    {
        var list = PcbAssetSetup.GetOrCreateLevelList();
        for (int n = list.Count + 1; ; n++)
        {
            string candidate = $"Level {n:00}";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>($"{PcbAssetSetup.LevelFolder}/{candidate}.prefab")) return candidate;
        }
    }

    static string CleanName(string levelName) =>
        string.Join("_", (levelName ?? "").Split(Path.GetInvalidFileNameChars())).Trim();

    // ---------------------------------------------------------------- unsaved-change detection

    void RefreshDirty()
    {
        if (Event.current.type != EventType.Layout) return;
        if (!dirtyCheckDue && EditorApplication.timeSinceStartup - lastDirtyCheck < 1.0) return;
        isDirtyCached = IsDirty();
        lastDirtyCheck = EditorApplication.timeSinceStartup;
        dirtyCheckDue = false;
    }

    bool IsDirty()
    {
        var saved = string.IsNullOrEmpty(board.savedPath) ? null : AssetDatabase.LoadAssetAtPath<Board>(board.savedPath);
        if (!saved) return board.GetComponentsInChildren<PcbNode>(true).Length > 0;
        return Signature(board) != Signature(saved);
    }

    /// <summary>
    /// Text fingerprint of a level: every script field under the board plus local positions.
    /// References are written as hierarchy paths so a scene copy and its prefab compare equal.
    /// </summary>
    static string Signature(Board b)
    {
        var sb = new StringBuilder();
        Transform root = b.transform;
        foreach (var mb in b.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (!mb || (mb.gameObject.hideFlags & HideFlags.DontSave) != 0) continue; // skip generated visuals
            Vector3 p = root.InverseTransformPoint(mb.transform.position);
            sb.Append(PathOf(root, mb.transform)).Append(':').Append(mb.GetType().Name)
              .Append($"@{Num(p.x)},{Num(p.y)}{{");

            var so = new SerializedObject(mb);
            var prop = so.GetIterator();
            bool enter = true;
            while (prop.Next(enter))
            {
                enter = prop.propertyType == SerializedPropertyType.Generic;
                switch (prop.name)
                {
                    case "m_Script": case "m_ObjectHideFlags": case "m_EditorHideFlags":
                    case "m_EditorClassIdentifier": case "m_Name": case "editorView": case "savedPath":
                    // Unity's prefab-link bookkeeping: differs between a scene copy and the prefab asset, isn't level data
                    case "m_CorrespondingSourceObject": case "m_PrefabInstance": case "m_PrefabAsset":
                        continue;
                }
                sb.Append(prop.name).Append('=').Append(ValueOf(prop, root)).Append(';');
            }
            sb.Append("}\n"); // one line per component, so a difference can be pointed out
        }
        return sb.ToString();
    }

    /// <summary>Fixed-precision number without "-0.000" (float noise around zero must not count as a change).</summary>
    static string Num(float v, string format = "F3")
    {
        string s = v.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        return s.StartsWith("-") && s.Trim('-', '0', '.') == "" ? s.Substring(1) : s;
    }

    static string ValueOf(SerializedProperty p, Transform root)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue.ToString();
            case SerializedPropertyType.Boolean: return p.boolValue ? "1" : "0";
            case SerializedPropertyType.Float: return Num(p.floatValue, "F4");
            case SerializedPropertyType.String: return p.stringValue;
            case SerializedPropertyType.Enum: return p.enumValueIndex.ToString();
            case SerializedPropertyType.ArraySize: return p.intValue.ToString();
            case SerializedPropertyType.Vector2: return $"{Num(p.vector2Value.x)},{Num(p.vector2Value.y)}";
            case SerializedPropertyType.Vector3: return $"{Num(p.vector3Value.x)},{Num(p.vector3Value.y)},{Num(p.vector3Value.z)}";
            case SerializedPropertyType.Vector2Int: return p.vector2IntValue.ToString();
            case SerializedPropertyType.Color: return p.colorValue.ToString("F3");
            case SerializedPropertyType.ObjectReference:
                var o = p.objectReferenceValue;
                if (!o) return "null";
                if (o is Component c && c.transform.IsChildOf(root)) return PathOf(root, c.transform) + ":" + c.GetType().Name;
                if (o is GameObject g && g.transform.IsChildOf(root)) return PathOf(root, g.transform);
                return AssetDatabase.GetAssetPath(o) + "#" + o.name;
            default: return "";
        }
    }

    static string PathOf(Transform root, Transform t)
    {
        var sb = new StringBuilder();
        for (; t && t != root; t = t.parent) sb.Insert(0, "/" + t.GetSiblingIndex());
        return sb.ToString();
    }
}
