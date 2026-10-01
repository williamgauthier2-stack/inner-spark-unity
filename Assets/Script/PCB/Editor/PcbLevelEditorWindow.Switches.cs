using System.Collections.Generic;
using Pcb;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Switch part of the PCB Level Editor: the Link tool (switch -> gate on a trace) and the switch/gate validation.
/// Normal switches control normal gates (any press flips the gate); AND switches control AND gates
/// (open only while all are on). The two kinds are never mixed on one gate.
/// </summary>
public partial class PcbLevelEditorWindow
{
    static readonly Color NormalLinkColor = new Color(1f, 0.75f, 0.2f);
    static readonly Color AndLinkColor = new Color(0.4f, 0.9f, 1f);

    PcbNode linkSwitch;
    bool linkStartsOpen;

    // ---------------------------------------------------------------- Link tool

    void LinkOptionsGUI()
    {
        linkStartsOpen = EditorGUILayout.Toggle(new GUIContent("New Gates Start Open",
            "Normal gate: open at the start, each press flips it.\nAND gate (inverted): open until all its switches are on."), linkStartsOpen);
        string picked = linkSwitch ? $"{linkSwitch.name} ({(linkSwitch.type == NodeType.AndSwitch ? "AND" : "normal")})" : "none: click a switch";
        EditorGUILayout.LabelField("Switch", picked);
    }

    void LinkTool(Event e, Vector2 mouse)
    {
        if (linkSwitch && !linkSwitch.IsSwitch) linkSwitch = null; // type changed / deleted meanwhile
        var node = PickNode(mouse);
        var trace = node ? null : PickTrace(mouse);

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && linkSwitch)
        {
            linkSwitch = null;
            Repaint();
            e.Use();
        }
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            if (node && node.IsSwitch) { linkSwitch = node; Repaint(); }
            else if (trace && linkSwitch)
            {
                if (e.control) Unlink(linkSwitch, trace);
                else Link(linkSwitch, trace);
            }
            e.Use();
        }

        if (e.type != EventType.Repaint) return;
        DrawLinks();
        if (linkSwitch) HighlightNode(linkSwitch, LinkColor(linkSwitch));
        if (node && node.IsSwitch) HighlightNode(node, Color.white);
        else if (trace && linkSwitch)
        {
            var path = new List<Vector3>();
            trace.GetWorldPath(board, false, path);
            Handles.color = e.control ? new Color(1f, 0.3f, 0.3f) : Color.white;
            Handles.DrawAAPolyLine(8f, path.ToArray());
        }
    }

    static Color LinkColor(PcbNode switchNode) => switchNode.type == NodeType.AndSwitch ? AndLinkColor : NormalLinkColor;

    void Link(PcbNode switchNode, Trace trace)
    {
        bool and = switchNode.type == NodeType.AndSwitch;
        var gate = trace.GetComponent<GateMechanic>();
        if (gate && (gate is AndGateMechanic) != and)
        {
            ShowNotification(new GUIContent(and
                ? "That trace has a normal gate: only normal switches can control it."
                : "That trace has an AND gate: only AND switches can control it."));
            return;
        }

        Undo.SetCurrentGroupName("Link Switch");
        int group = Undo.GetCurrentGroup();
        var mech = switchNode.GetComponent<SwitchMechanic>();
        if (!mech) mech = Undo.AddComponent<SwitchMechanic>(switchNode.gameObject);
        bool created = !gate;
        if (created) gate = and ? Undo.AddComponent<AndGateMechanic>(trace.gameObject) : Undo.AddComponent<GateMechanic>(trace.gameObject);

        Undo.RecordObject(gate, "Link Switch");
        if (created)
        {
            if (gate is AndGateMechanic andGate) andGate.inverted = linkStartsOpen;
            else gate.isOpen = linkStartsOpen;
        }
        if (!gate.switches.Contains(mech)) gate.switches.Add(mech);
        Undo.CollapseUndoOperations(group);
        dirtyCheckDue = true;
    }

    void Unlink(PcbNode switchNode, Trace trace)
    {
        var mech = switchNode.GetComponent<SwitchMechanic>();
        var gate = trace.GetComponent<GateMechanic>();
        if (!mech || !gate || !gate.switches.Contains(mech)) return;

        Undo.SetCurrentGroupName("Unlink Switch");
        int group = Undo.GetCurrentGroup();
        Undo.RecordObject(gate, "Unlink Switch");
        gate.switches.Remove(mech);
        // Nothing controls it any more: remove the gate, unless it's also wired by hand (onToggle -> SetOpen).
        if (gate.switches.Count == 0 && EventDrivers(gate).Count == 0) Undo.DestroyObjectImmediate(gate);
        Undo.CollapseUndoOperations(group);
        dirtyCheckDue = true;
    }

    /// <summary>Dotted line from every switch to the middle of each trace it controls.</summary>
    void DrawLinks()
    {
        var path = new List<Vector3>();
        foreach (var t in board.Traces)
        {
            if (!t || !t.IsValid) continue;
            var gate = t.GetComponent<GateMechanic>();
            if (!gate) continue;
            t.GetWorldPath(board, false, path);
            Vector3 middle = PathMiddle(path);
            Handles.color = gate is AndGateMechanic ? AndLinkColor : NormalLinkColor;
            Handles.DrawWireCube(middle, Vector3.one * board.cellSize * 0.3f);
            foreach (var s in gate.switches)
                if (s) Handles.DrawDottedLine(s.transform.position, middle, 4f);
        }
    }

    static Vector3 PathMiddle(List<Vector3> path)
    {
        if (path.Count == 0) return Vector3.zero;
        float total = 0f;
        for (int i = 0; i < path.Count - 1; i++) total += Vector3.Distance(path[i], path[i + 1]);
        float half = total * 0.5f;
        for (int i = 0; i < path.Count - 1; i++)
        {
            float d = Vector3.Distance(path[i], path[i + 1]);
            if (half <= d) return Vector3.Lerp(path[i], path[i + 1], d > 0f ? half / d : 0f);
            half -= d;
        }
        return path[path.Count - 1];
    }

    /// <summary>A switch node needs a SwitchMechanic; any other node shouldn't have one (after a type change).</summary>
    static void EnsureSwitchMechanic(PcbNode node)
    {
        var mech = node.GetComponent<SwitchMechanic>();
        if (node.IsSwitch && !mech) Undo.AddComponent<SwitchMechanic>(node.gameObject);
        else if (!node.IsSwitch && mech) Undo.DestroyObjectImmediate(mech);
    }

    /// <summary>Switches wired to this gate by hand, through their onToggle event (the Level 04 way).</summary>
    List<SwitchMechanic> EventDrivers(GateMechanic gate)
    {
        var result = new List<SwitchMechanic>();
        foreach (var s in board.GetComponentsInChildren<SwitchMechanic>(true))
            for (int i = 0; i < s.onToggle.GetPersistentEventCount(); i++)
                if (s.onToggle.GetPersistentTarget(i) == gate && !result.Contains(s)) result.Add(s);
        return result;
    }

    // ---------------------------------------------------------------- validation

    void ValidateSwitches()
    {
        var used = new HashSet<SwitchMechanic>();
        foreach (var t in board.Traces)
        {
            if (!t) continue;
            foreach (var gate in t.GetComponents<GateMechanic>())
            {
                bool and = gate is AndGateMechanic;
                string kind = and ? "AND gate" : "Gate";
                var wired = EventDrivers(gate);
                used.UnionWith(wired);

                if (gate.switches.Count == 0 && wired.Count == 0)
                {
                    bool open = and ? ((AndGateMechanic)gate).inverted : gate.isOpen;
                    Add($"{t.name}: {kind} isn't linked to any switch, so it stays {(open ? "open" : "closed")} forever. Link one with the Link tool.", gate);
                }
                if (and && wired.Count > 0)
                    Add($"{t.name}: AND gate is also wired through a switch's On Toggle, which bypasses the AND rule. Remove that On Toggle entry.", gate);

                foreach (var s in gate.switches)
                {
                    if (!s)
                    {
                        Add($"{t.name}: {kind} has a missing switch in its list (deleted?). Relink with the Link tool.", gate);
                        continue;
                    }
                    used.Add(s);
                    var n = s.GetComponent<PcbNode>();
                    if (!s.transform.IsChildOf(board.transform))
                        Add($"{t.name}: {kind} is controlled by '{s.name}', which isn't part of this level.", gate);
                    else if (!n || !n.IsSwitch)
                        Add($"{t.name}: {kind} is controlled by '{s.name}', which isn't a switch node.", gate);
                    else if ((n.type == NodeType.AndSwitch) != and)
                        Add($"{t.name}: {kind} is controlled by {(and ? "normal" : "AND")} switch '{s.name}'. Normal switches go with normal gates, AND switches with AND gates.", gate);
                    if (wired.Contains(s))
                        Add($"{t.name}: '{s.name}' is in the gate's switch list AND wired through its On Toggle, so it acts twice per press. Remove the On Toggle entry.", s);
                }
            }
        }

        foreach (var n in board.Nodes)
        {
            if (!n) continue;
            var s = n.GetComponent<SwitchMechanic>();
            if (n.IsSwitch && !s) Add($"{n.name}: switch node has no Switch Mechanic, so pressing Space does nothing. Link it with the Link tool (adds one).", n);
            else if (!n.IsSwitch && s) Add($"{n.name}: has a Switch Mechanic but isn't a switch node, so it can't be pressed.", n);
            else if (s && !used.Contains(s)) Add($"{n.name}: switch doesn't control any gate. Link it with the Link tool.", n);
        }
    }
}
