using System.Collections.Generic;
using Pcb;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > PCB > Level Editor. Draw nodes and traces directly in the Scene view.
/// </summary>
public partial class PcbLevelEditorWindow : EditorWindow
{
    enum Tool { Select, Node, Trace, Erase, Decoration, Paint, Link, Data }

    struct Issue { public string message; public Object target; }

    static readonly string[] ToolNames = { "Select", "Node", "Trace", "Erase", "Decor", "Paint", "Link", "Data" };
    static readonly string[] SideNames = { "Front", "Back" };
    static readonly Color FrontColor = new Color(1f, 0.85f, 0.4f);
    static readonly Color BackColor = new Color(0.5f, 0.85f, 1f);

    Board board;
    Tool tool = Tool.Node;
    NodeType placeType = NodeType.Capacitor;
    float nodeRotation = 0f;
    bool autoRoute = true;
    DecorType decorType = DecorType.Resistor;
    float decorRotation = 0f;
    Vector2 scroll;
    readonly List<Issue> issues = new List<Issue>();
    bool validated;

    // Trace drawing
    PcbNode traceStart;
    readonly List<Vector2> tracePoints = new List<Vector2>(); // board-local bend points placed so far

    // Dragging
    PcbNode dragNode;
    Trace dragTrace;
    int dragBend = -1;
    PcbDecoration dragDecor;

    [MenuItem("Tools/PCB/Level Editor")]
    static void Open() => GetWindow<PcbLevelEditorWindow>("PCB Editor");

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        FindBoard();
    }

    void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    void OnHierarchyChange()
    {
        if (!board) FindBoard();
        dirtyCheckDue = true;
        Repaint();
    }

    void FindBoard() => board = FindAnyObjectByType<Board>();

    Color SideColor => board && board.editorView == PcbLayer.Back ? BackColor : FrontColor;

    // ================================================================ window

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Play mode: R restarts, Esc pauses.\nEdits made now are lost when you stop playing.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }
        if (!board)
        {
            EditorGUILayout.HelpBox("There is no Board in this scene.", MessageType.Info);
            if (GUILayout.Button("New Level", GUILayout.Height(32))) CreateBoard();
            LevelListGUI();
            EditorGUILayout.EndScrollView();
            return;
        }

        board = (Board)EditorGUILayout.ObjectField("Board", board, typeof(Board), true);
        LevelGUI();
        if (board.theme && !board.theme.IsComplete)
        {
            EditorGUILayout.HelpBox("Theme is missing its 3D materials.", MessageType.Warning);
            if (GUILayout.Button("Upgrade Theme")) PcbAssetSetup.GetOrCreateTheme();
        }
        if (!board.theme)
        {
            EditorGUILayout.HelpBox("Board has no theme.", MessageType.Warning);
            if (GUILayout.Button("Create / Assign Theme"))
            {
                Undo.RecordObject(board, "Assign Theme");
                board.theme = PcbAssetSetup.GetOrCreateTheme();
            }
        }

        EditorGUI.BeginChangeCheck();
        var size = EditorGUILayout.Vector2IntField("Size (cells)", board.sizeInCells);
        var cell = EditorGUILayout.FloatField("Cell Size", board.cellSize);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(board, "Resize Board");
            board.sizeInCells = Vector2Int.Max(size, Vector2Int.one);
            board.cellSize = Mathf.Max(0.1f, cell);
        }
        LookGUI();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Editing Side", EditorStyles.boldLabel);
        var side = (PcbLayer)GUILayout.Toolbar((int)board.editorView, SideNames, GUILayout.Height(24));
        if (side != board.editorView) SetEditSide(side);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Tool", EditorStyles.boldLabel);
        var newTool = (Tool)GUILayout.Toolbar((int)tool, ToolNames, GUILayout.Height(28));
        if (newTool != tool)
        {
            tool = newTool;
            CancelTrace();
            SceneView.RepaintAll();
        }
        if (tool == Tool.Node)
        {
            placeType = (NodeType)EditorGUILayout.EnumPopup("Node Type", placeType);
            // 15° steps: lands exactly on 45°/90° and matches the 45° trace routing.
            nodeRotation = Mathf.Round(EditorGUILayout.Slider("Rotation", nodeRotation, 0f, 360f) / 15f) * 15f % 360f;
        }
        if (tool == Tool.Trace) autoRoute = EditorGUILayout.Toggle("Auto 45° Routing", autoRoute);
        if (tool == Tool.Decoration)
        {
            decorType = (DecorType)EditorGUILayout.EnumPopup("Decor Type", decorType);
            decorRotation = EditorGUILayout.Slider("Rotation", decorRotation, 0f, 360f);
        }
        if (tool == Tool.Paint) PaintOptionsGUI();
        if (tool == Tool.Link) LinkOptionsGUI();
        if (tool == Tool.Data) DataOptionsGUI();
        EditorGUILayout.HelpBox(HelpText(), MessageType.None);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Validate Level")) Validate();
            if (GUILayout.Button("Frame Board")) FrameBoard();
        }
        if (validated && issues.Count == 0) EditorGUILayout.HelpBox("No problems found.", MessageType.Info);
        foreach (var issue in issues)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(issue.message, MessageType.Warning);
                if (issue.target && GUILayout.Button("Select", GUILayout.Width(52), GUILayout.Height(38)))
                {
                    Selection.activeObject = issue.target;
                    EditorGUIUtility.PingObject(issue.target);
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    string HelpText()
    {
        switch (tool)
        {
            case Tool.Node:
                return "Click empty grid: place node on the current side.\n" +
                       "Drag a node: move it.   Drag a small square: move a trace bend.\n" +
                       "Ctrl+Click a node: change it to the selected type.\n" +
                       "Shift+Click a node: give it the rotation below (new nodes get it too).";
            case Tool.Trace:
                return "Click a node (or empty grid) to start.\n" +
                       "Click empty grid: add a bend.   Click a node: finish.\n" +
                       "Shift+Click empty: finish with a new capacitor.\n" +
                       "Hold Ctrl: route diagonal first.   Backspace: undo bend.   Esc: cancel.";
            case Tool.Erase:
                return "Click a node: delete it and its traces.\nClick a trace: delete it.\nClick a decoration: delete it.";
            case Tool.Decoration:
                return "Click empty grid: place a decoration on the current side.\n" +
                       "Drag a decoration: move it.   Ctrl+Click: change its type/rotation.";
            case Tool.Paint:
                return "Pick Nodes / Decorations / Gates, a type and a model below, then click them in the scene to paint them.\n" +
                       "Shift+Click: reset to the default.   Level-wide defaults: Look section above.";
            case Tool.Link:
                return "Click a switch to pick it, then click the traces it should control (adds a gate).\n" +
                       "Ctrl+Click a trace: unlink it.   Esc: drop the switch.\n" +
                       "Normal switches flip normal gates; AND switches open AND gates only while all are on.";
            case Tool.Data:
                return "Click a capacitor: add / remove its data pickup.\n" +
                       "The goal stays locked (grayed out, no win) until all data on the level is collected.";
            default:
                return "Normal Unity selection. Select nodes/traces to edit them in the Inspector.";
        }
    }

    void SetEditSide(PcbLayer side)
    {
        Undo.RecordObject(board, "Switch Side");
        board.editorView = side;
        CancelTrace();
        AlignSceneView();
        SceneView.RepaintAll();
    }

    void CreateBoard()
    {
        var theme = PcbAssetSetup.GetOrCreateTheme();
        var go = new GameObject("Board");
        board = go.AddComponent<Board>();
        board.theme = theme;
        board.levelName = NextLevelName();
        Undo.RegisterCreatedObjectUndo(go, "Create Board");
        EnsureLevelManager();
        board.Rebuild();

        var cam = Camera.main;
        if (cam)
        {
            Undo.RecordObjects(new Object[] { cam, cam.transform }, "Setup Camera");
            BoardRig.FitCamera(cam, board);
        }
        Selection.activeGameObject = go;
        FrameBoard();
    }

    void FrameBoard() => AlignSceneView();

    /// <summary>Looks straight at the side being edited: from the front, or from behind (mirrored, as the player sees it).</summary>
    void AlignSceneView()
    {
        var sceneView = SceneView.lastActiveSceneView;
        if (!sceneView || !board) return;
        sceneView.in2DMode = false;
        var rotation = board.transform.rotation *
                       (board.editorView == PcbLayer.Back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);
        sceneView.LookAt(board.Center, rotation, Mathf.Max(board.Size.x, board.Size.y) * 0.6f + 1f, true, false);
    }

    // ================================================================ scene view

    void OnSceneGUI(SceneView sceneView)
    {
        if (!board || !board.theme || EditorApplication.isPlaying) return;
        DrawOverlayLabel();
        if (Event.current.type == EventType.Repaint) DrawHiddenSide();
        if (tool == Tool.Select) return;

        Event e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id); // stop clicks from selecting objects

        if (!TryGetMouseLocal(e.mousePosition, out Vector2 mouse)) return;
        Vector2 snapped = board.SnapLocal(mouse);

        if (e.type == EventType.Repaint) DrawGrid();

        switch (tool)
        {
            case Tool.Node: NodeTool(e, id, mouse, snapped); break;
            case Tool.Trace: TraceTool(e, mouse, snapped); break;
            case Tool.Erase: EraseTool(e, mouse); break;
            case Tool.Decoration: DecorationTool(e, id, mouse, snapped); break;
            case Tool.Paint: PaintTool(e, mouse); break;
            case Tool.Link: LinkTool(e, mouse); break;
            case Tool.Data: DataTool(e, mouse); break;
        }

        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) sceneView.Repaint();
    }

    bool TryGetMouseLocal(Vector2 guiPosition, out Vector2 local)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);
        var plane = new Plane(board.transform.forward, board.transform.position);
        if (plane.Raycast(ray, out float distance) || distance < 0f)
        {
            local = board.WorldToLocal(ray.GetPoint(distance));
            return true;
        }
        local = default;
        return false;
    }

    /// <summary>Editor-only ghost of the side you are not editing (in game it is fully hidden).</summary>
    void DrawHiddenSide()
    {
        var hidden = board.editorView.Other();
        Handles.color = board.theme.editorGhost;
        var points = new List<Vector3>();
        foreach (var t in board.Traces)
        {
            if (!t || !t.IsValid || t.layer != hidden) continue;
            t.GetWorldPath(board, false, points);
            Handles.DrawAAPolyLine(4f, points.ToArray());
        }
        foreach (var n in board.Nodes)
        {
            if (!n || n.IsVia || n.layer != hidden) continue;
            if (n.type == NodeType.Goal) DrawChipOutline(n, 0f);
            else Handles.DrawWireDisc(n.transform.position, board.transform.forward, board.cellSize * 0.35f, 2f);
        }
        foreach (var d in board.Decorations)
        {
            if (!d || d.layer != hidden) continue;
            Handles.DrawWireDisc(d.transform.position, board.transform.forward, board.cellSize * 0.25f, 2f);
        }
    }

    void DrawOverlayLabel()
    {
        Handles.BeginGUI();
        var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 12, richText = true };
        string sideText = board.editorView == PcbLayer.Front ? "FRONT" : "<color=#7fd4ff>BACK</color>";
        string extra = traceStart ? $"   drawing trace ({tracePoints.Count} bends)" : "";
        GUI.Label(new Rect(8, 8, 360, 22), $" PCB  <b>{tool}</b>   Side: <b>{sideText}</b>{extra}", style);
        Handles.EndGUI();
    }

    void DrawGrid()
    {
        Vector2 size = board.Size;
        float c = board.cellSize;
        Handles.color = new Color(1f, 1f, 1f, 0.08f);
        for (int x = 0; x <= board.sizeInCells.x; x++)
            Handles.DrawLine(board.LocalToWorld(new Vector2(x * c, 0f)), board.LocalToWorld(new Vector2(x * c, size.y)));
        for (int y = 0; y <= board.sizeInCells.y; y++)
            Handles.DrawLine(board.LocalToWorld(new Vector2(0f, y * c)), board.LocalToWorld(new Vector2(size.x, y * c)));
    }

    void DrawCursor(Vector2 local, Color color)
    {
        Handles.color = color;
        Handles.DrawWireDisc(board.LocalToWorld(local), Vector3.forward, board.cellSize * 0.35f, 2f);
    }

    // ---------------------------------------------------------------- Node tool

    void NodeTool(Event e, int id, Vector2 mouse, Vector2 snapped)
    {
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && !e.alt:
            {
                var hit = PickNode(mouse);
                if (hit && e.control)
                {
                    Undo.RecordObject(hit, "Change Node Type");
                    if (hit.type != placeType) hit.model = null; // a painted model belongs to the old type
                    hit.type = placeType;
                    hit.name = NodeName(placeType);
                    EnsureSwitchMechanic(hit);
                    if (hit.type != NodeType.Capacitor && hit.TryGetComponent(out DataMechanic data))
                        Undo.DestroyObjectImmediate(data); // data only lives on capacitors
                }
                else if (hit && e.shift)
                {
                    Undo.RecordObject(hit, "Rotate Node");
                    hit.rotationDegrees = nodeRotation;
                }
                else if (hit) dragNode = hit;
                else if (PickBend(mouse, out dragTrace, out dragBend)) { }
                else dragNode = CreateNode(snapped, placeType); // keep dragging to position it
                GUIUtility.hotControl = id;
                e.Use();
                break;
            }
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                if (dragNode)
                {
                    Undo.RecordObject(dragNode.transform, "Move Node");
                    dragNode.transform.position = board.LocalToWorld(snapped);
                    board.Rebuild();
                }
                else if (dragTrace)
                {
                    Undo.RecordObject(dragTrace, "Move Bend");
                    dragTrace.bends[dragBend] = snapped;
                    board.Rebuild();
                }
                e.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                dragNode = null;
                dragTrace = null;
                e.Use();
                break;
            case EventType.Repaint:
                DrawBendHandles();
                var hover = PickNode(mouse);
                if (hover) HighlightNode(hover, Color.white);
                else DrawCursor(snapped, SideColor);
                break;
        }
    }

    void DrawBendHandles()
    {
        Handles.color = SideColor;
        float s = board.cellSize * 0.3f;
        foreach (var t in board.Traces)
        {
            if (!t || t.layer != board.editorView) continue;
            foreach (var b in t.bends) Handles.DrawWireCube(board.LocalToWorld(b), new Vector3(s, s, 0f));
        }
    }

    bool PickBend(Vector2 mouse, out Trace trace, out int index)
    {
        float radius = board.cellSize * 0.3f;
        foreach (var t in board.Traces)
        {
            if (!t || t.layer != board.editorView) continue;
            for (int i = 0; i < t.bends.Count; i++)
            {
                if (Vector2.Distance(t.bends[i], mouse) > radius) continue;
                trace = t;
                index = i;
                return true;
            }
        }
        trace = null;
        index = -1;
        return false;
    }

    // ---------------------------------------------------------------- Trace tool

    void TraceTool(Event e, Vector2 mouse, Vector2 snapped)
    {
        if (e.type == EventType.KeyDown && traceStart)
        {
            if (e.keyCode == KeyCode.Escape) { CancelTrace(); e.Use(); }
            else if (e.keyCode == KeyCode.Backspace)
            {
                if (tracePoints.Count > 0) tracePoints.RemoveAt(tracePoints.Count - 1);
                else CancelTrace();
                e.Use();
            }
        }

        var hover = PickNode(mouse);

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            if (!traceStart)
            {
                traceStart = hover ? hover : CreateNode(snapped, NodeType.Capacitor);
                tracePoints.Clear();
            }
            else if (hover && hover != traceStart) FinishTrace(hover, e.control);
            else if (!hover && e.shift) FinishTrace(CreateNode(snapped, NodeType.Capacitor), e.control);
            else if (!hover)
            {
                var last = LastTracePoint();
                if (snapped != last) Route(last, snapped, e.control, tracePoints);
            }
            e.Use();
        }

        if (e.type != EventType.Repaint) return;
        if (hover) HighlightNode(hover, Color.white);
        else DrawCursor(snapped, SideColor);
        if (!traceStart) return;

        // Preview of the trace being drawn
        var preview = new List<Vector2>(tracePoints);
        Vector2 target = hover ? board.WorldToLocal(hover.transform.position) : snapped;
        Route(LastTracePoint(), target, e.control, preview);
        var points = new List<Vector3> { traceStart.transform.position };
        foreach (var p in preview) points.Add(board.LocalToWorld(p));
        Handles.color = SideColor;
        Handles.DrawAAPolyLine(5f, points.ToArray());
    }

    Vector2 LastTracePoint() =>
        tracePoints.Count > 0 ? tracePoints[tracePoints.Count - 1] : board.WorldToLocal(traceStart.transform.position);

    /// <summary>Adds the points to go from a to b; inserts a 45° elbow when the line isn't straight or diagonal.</summary>
    void Route(Vector2 a, Vector2 b, bool diagonalFirst, List<Vector2> result)
    {
        Vector2 d = b - a;
        float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y);
        bool clean = ax < 1e-4f || ay < 1e-4f || Mathf.Abs(ax - ay) < 1e-4f;
        if (autoRoute && !clean)
        {
            float m = Mathf.Min(ax, ay);
            var diagonal = new Vector2(Mathf.Sign(d.x) * m, Mathf.Sign(d.y) * m);
            result.Add(diagonalFirst ? a + diagonal : b - diagonal);
        }
        result.Add(b);
    }

    void FinishTrace(PcbNode end, bool diagonalFirst)
    {
        var bends = new List<Vector2>(tracePoints);
        Route(LastTracePoint(), board.WorldToLocal(end.transform.position), diagonalFirst, bends);
        bends.RemoveAt(bends.Count - 1); // the end node itself
        RemoveCollinear(board.WorldToLocal(traceStart.transform.position), bends, board.WorldToLocal(end.transform.position));

        var go = new GameObject($"Trace {traceStart.name} - {end.name}");
        go.transform.SetParent(Container("Traces"), false);
        var trace = go.AddComponent<Trace>();
        trace.from = traceStart;
        trace.to = end;
        trace.layer = board.editorView;
        trace.bends = bends;
        Undo.RegisterCreatedObjectUndo(go, "Create Trace");
        board.Rebuild();
        CancelTrace();
    }

    static void RemoveCollinear(Vector2 start, List<Vector2> bends, Vector2 end)
    {
        for (int i = bends.Count - 1; i >= 0; i--)
        {
            Vector2 prev = i > 0 ? bends[i - 1] : start;
            Vector2 next = i < bends.Count - 1 ? bends[i + 1] : end;
            Vector2 d1 = bends[i] - prev, d2 = next - bends[i];
            bool duplicate = d1.sqrMagnitude < 1e-6f || d2.sqrMagnitude < 1e-6f;
            bool straight = Mathf.Abs(d1.x * d2.y - d1.y * d2.x) < 1e-4f && Vector2.Dot(d1, d2) > 0f;
            if (duplicate || straight) bends.RemoveAt(i);
        }
    }

    void CancelTrace()
    {
        traceStart = null;
        tracePoints.Clear();
    }

    // ---------------------------------------------------------------- Erase tool

    void EraseTool(Event e, Vector2 mouse)
    {
        var node = PickNode(mouse);
        var trace = node ? null : PickTrace(mouse);
        var decor = (node || trace) ? null : PickDecoration(mouse);

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            Undo.SetCurrentGroupName("Erase");
            int group = Undo.GetCurrentGroup();
            if (node)
            {
                foreach (var t in new List<Trace>(board.Traces))
                    if (t.from == node || t.to == node) Undo.DestroyObjectImmediate(t.gameObject);
                Undo.DestroyObjectImmediate(node.gameObject);
            }
            else if (trace) Undo.DestroyObjectImmediate(trace.gameObject);
            else if (decor) Undo.DestroyObjectImmediate(decor.gameObject);
            Undo.CollapseUndoOperations(group);
            board.Rebuild();
            e.Use();
        }

        if (e.type != EventType.Repaint) return;
        var red = new Color(1f, 0.3f, 0.3f);
        if (node) HighlightNode(node, red);
        else if (trace)
        {
            var path = new List<Vector3>();
            trace.GetWorldPath(board, false, path);
            Handles.color = red;
            Handles.DrawAAPolyLine(8f, path.ToArray());
        }
        else if (decor) HighlightDecoration(decor, red);
    }

    // ---------------------------------------------------------------- Decoration tool

    void DecorationTool(Event e, int id, Vector2 mouse, Vector2 snapped)
    {
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && !e.alt:
            {
                var hit = PickDecoration(mouse);
                if (hit && e.control)
                {
                    Undo.RecordObject(hit, "Change Decoration Type");
                    if (hit.type != decorType) hit.model = null; // a painted model belongs to the old type
                    hit.type = decorType;
                    hit.rotationDegrees = decorRotation;
                    hit.name = DecorationName(decorType);
                }
                else if (hit) dragDecor = hit;
                else dragDecor = CreateDecoration(snapped, decorType);
                GUIUtility.hotControl = id;
                e.Use();
                break;
            }
            case EventType.MouseDrag when GUIUtility.hotControl == id:
                if (dragDecor)
                {
                    Undo.RecordObject(dragDecor.transform, "Move Decoration");
                    dragDecor.transform.position = board.LocalToWorld(snapped);
                    board.Rebuild();
                }
                e.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                dragDecor = null;
                e.Use();
                break;
            case EventType.Repaint:
                var hover = PickDecoration(mouse);
                if (hover) HighlightDecoration(hover, Color.white);
                else DrawCursor(snapped, SideColor);
                break;
        }
    }

    PcbDecoration PickDecoration(Vector2 local)
    {
        PcbDecoration best = null;
        float bestDistance = board.cellSize * 0.6f;
        foreach (var d in board.Decorations)
        {
            if (!d || d.layer != board.editorView) continue;
            float dist = Vector2.Distance(local, board.WorldToLocal(d.transform.position));
            if (dist < bestDistance) { bestDistance = dist; best = d; }
        }
        return best;
    }

    void HighlightDecoration(PcbDecoration decor, Color color)
    {
        Handles.color = color;
        Handles.DrawWireDisc(decor.transform.position, Vector3.forward, board.cellSize * 0.4f, 3f);
    }

    PcbDecoration CreateDecoration(Vector2 local, DecorType type)
    {
        var go = new GameObject(DecorationName(type));
        go.transform.SetParent(Container("Decorations"), false);
        go.transform.position = board.LocalToWorld(local);
        var decor = go.AddComponent<PcbDecoration>();
        decor.type = type;
        decor.layer = board.editorView;
        decor.rotationDegrees = decorRotation;
        Undo.RegisterCreatedObjectUndo(go, "Create " + type);
        board.Rebuild();
        return decor;
    }

    string DecorationName(DecorType type)
    {
        int count = 0;
        foreach (var d in board.Decorations) if (d && d.type == type) count++;
        string side = board.editorView == PcbLayer.Front ? " F" : " B";
        return $"{type}{side} {count + 1:00}";
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Nodes on the side being edited (vias count on both sides).</summary>
    PcbNode PickNode(Vector2 local)
    {
        PcbNode best = null;
        float bestDistance = board.cellSize * 0.6f;
        foreach (var n in board.Nodes)
        {
            if (!n || !n.IsOnLayer(board.editorView)) continue;
            Vector2 p = board.WorldToLocal(n.transform.position);
            if (n.type == NodeType.Goal)
            {
                Vector2 q = Quaternion.Euler(0f, 0f, -n.rotationDegrees) * (local - p); // into the (rotated) chip's frame
                if (Mathf.Abs(q.x) <= n.chipSize.x * 0.5f && Mathf.Abs(q.y) <= n.chipSize.y * 0.5f) return n;
                continue;
            }
            float d = Vector2.Distance(local, p);
            if (d < bestDistance) { bestDistance = d; best = n; }
        }
        return best;
    }

    Trace PickTrace(Vector2 local)
    {
        Trace best = null;
        float bestDistance = board.cellSize * 0.4f;
        foreach (var t in board.Traces)
        {
            if (!t || t.layer != board.editorView) continue;
            float d = t.DistanceTo(board, local);
            if (d < bestDistance) { bestDistance = d; best = t; }
        }
        return best;
    }

    void HighlightNode(PcbNode node, Color color)
    {
        Handles.color = color;
        Vector3 p = node.transform.position;
        if (node.type == NodeType.Goal) DrawChipOutline(node, 0.1f);
        else Handles.DrawWireDisc(p, Vector3.forward, board.cellSize * 0.5f, 3f);
    }

    /// <summary>Wire rectangle of a goal chip, following its rotation.</summary>
    void DrawChipOutline(PcbNode node, float padding)
    {
        var rotation = board.transform.rotation * Quaternion.Euler(0f, 0f, node.rotationDegrees);
        using (new Handles.DrawingScope(Handles.color, Matrix4x4.TRS(node.transform.position, rotation, Vector3.one)))
            Handles.DrawWireCube(Vector3.zero, new Vector3(node.chipSize.x + padding, node.chipSize.y + padding, 0f));
    }

    PcbNode CreateNode(Vector2 local, NodeType type)
    {
        var go = new GameObject(NodeName(type));
        go.transform.SetParent(Container("Nodes"), false);
        go.transform.position = board.LocalToWorld(local);
        var node = go.AddComponent<PcbNode>();
        node.type = type;
        node.layer = board.editorView;
        node.rotationDegrees = nodeRotation;
        if (node.IsSwitch) go.AddComponent<SwitchMechanic>(); // a switch node without it does nothing
        Undo.RegisterCreatedObjectUndo(go, "Create " + type);
        board.Rebuild();
        return node;
    }

    string NodeName(NodeType type)
    {
        int count = 0;
        foreach (var n in board.Nodes) if (n && n.type == type) count++;
        string side = type == NodeType.Via ? "" : board.editorView == PcbLayer.Front ? " F" : " B";
        return $"{type}{side} {count + 1:00}";
    }

    Transform Container(string containerName)
    {
        var t = board.transform.Find(containerName);
        if (t) return t;
        var go = new GameObject(containerName);
        go.transform.SetParent(board.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "Create " + containerName);
        return go.transform;
    }

    // ================================================================ validation

    void Validate()
    {
        issues.Clear();
        validated = true;
        board.Rebuild();

        int starts = 0, goals = 0;
        foreach (var n in board.Nodes)
        {
            if (n.type == NodeType.Start) starts++;
            if (n.type == NodeType.Goal) goals++;
        }
        if (starts != 1) Add($"Need exactly 1 Start node (found {starts}).", board);
        if (goals == 0) Add("No Goal chip. Place a node of type Goal.", board);

        foreach (var t in board.Traces)
        {
            if (!t.IsValid) { Add($"{t.name}: missing an end node.", t); continue; }
            if (!t.from.IsOnLayer(t.layer) || !t.to.IsOnLayer(t.layer))
                Add($"{t.name}: connects to a node on the other side. Use a Via to change side.", t);
        }

        var directions8 = new[] { 
            Vector2.right, Vector2.up, Vector2.left, Vector2.down,
            new Vector2(1, 1), new Vector2(-1, 1), new Vector2(-1, -1), new Vector2(1, -1)
        };
        foreach (var n in board.Nodes)
        {
            var exits = board.GetExits(n);
            if (exits.Count == 0) { Add($"{n.name}: not connected to any trace.", n); continue; }

            bool front = false, back = false;
            for (int i = 0; i < exits.Count; i++)
            {
                var a = exits[i];
                if (a.layer == PcbLayer.Front) front = true; else back = true;

                for (int j = i + 1; j < exits.Count; j++)
                {
                    var b = exits[j];
                    if (a.layer == b.layer && Vector2.Dot(a.direction, b.direction) > 0.92f)
                        Add($"{n.name}: '{a.trace.name}' and '{b.trace.name}' leave in the same direction. The player can't choose between them.", n);
                }

                bool reachable = false;
                foreach (var dir in directions8)
                    if (board.TryPickExit(n, a.layer, dir, out var picked) && picked.trace == a.trace && picked.reversed == a.reversed)
                        reachable = true;
                if (!reachable)
                    Add($"{n.name}: '{a.trace.name}' cannot be taken reliably with an 8-way input. Consider adjusting its angle.", a.trace);
            }
            if (n.IsVia && !(front && back))
                Add($"{n.name}: via only has traces on one side, so flipping here is useless.", n);
        }
        ValidateSwitches();
        ValidateData();
    }

    void Add(string message, Object target) => issues.Add(new Issue { message = message, target = target });
}
