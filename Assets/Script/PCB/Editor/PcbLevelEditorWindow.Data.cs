using Pcb;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Data part of the PCB Level Editor: the Data tool (add/remove a data pickup on a capacitor) and its validation.
/// While any data on a level is uncollected, the goal is locked (grayed out, reaching it doesn't win).
/// </summary>
public partial class PcbLevelEditorWindow
{
    static readonly Color DataColor = new Color(0.4f, 1f, 0.6f);

    void DataOptionsGUI()
    {
        int count = 0;
        foreach (var n in board.Nodes) if (n && n.GetComponent<DataMechanic>()) count++;
        EditorGUILayout.LabelField("Data on this level", count == 0 ? "none (goal always unlocked)" : count.ToString());
    }

    void DataTool(Event e, Vector2 mouse)
    {
        var node = PickNode(mouse);
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            if (node && node.type == NodeType.Capacitor)
            {
                var data = node.GetComponent<DataMechanic>();
                if (data) Undo.DestroyObjectImmediate(data);
                else Undo.AddComponent<DataMechanic>(node.gameObject);
                board.Rebuild();
                dirtyCheckDue = true;
                Repaint();
            }
            else if (node) ShowNotification(new GUIContent("Data can only go on capacitors."));
            e.Use();
        }

        if (e.type != EventType.Repaint) return;
        // Mark every data node on this side, and the one under the mouse.
        Handles.color = DataColor;
        foreach (var n in board.Nodes)
            if (n && n.IsOnLayer(board.editorView) && n.GetComponent<DataMechanic>())
                Handles.DrawWireDisc(n.transform.position, Vector3.forward, board.cellSize * 0.42f, 3f);
        if (node) HighlightNode(node, node.type == NodeType.Capacitor ? Color.white : new Color(1f, 0.3f, 0.3f));
    }

    void ValidateData()
    {
        foreach (var n in board.Nodes)
            if (n && n.GetComponent<DataMechanic>() && n.type != NodeType.Capacitor)
                Add($"{n.name}: has a data pickup but isn't a capacitor (data only works on capacitors). Remove it with the Data tool.", n);
    }
}
