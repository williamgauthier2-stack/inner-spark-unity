using System.Collections.Generic;
using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// Builds the 3D look of a board from its nodes and traces using Unity's built-in meshes.
    /// Board space: the front face is at z = 0 facing -Z (towards the camera), the back face at z = thickness.
    /// Everything generated is marked DontSave, so levels and scenes only store the gameplay data.
    /// </summary>
    public static class BoardVisuals
    {
        public const string RootName = "~Visuals";

        static Mesh cube, cylinder, sphere;
        public static Mesh Cube => cube ? cube : cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        public static Mesh Cylinder => cylinder ? cylinder : cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        public static Mesh Sphere => sphere ? sphere : sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");

        // Built-in cylinder is 2 units tall along Y; this stands it up along the board normal (Z).
        static readonly Quaternion Upright = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>Direction pointing away from the board on that side (-1 front, +1 back).</summary>
        public static float Out(PcbLayer layer) => layer == PcbLayer.Front ? -1f : 1f;

        /// <summary>Z of the board surface on that side.</summary>
        public static float Surface(PcbLayer layer, float thickness) => layer == PcbLayer.Front ? 0f : thickness;

        public static Transform Build(Board board, List<Renderer> front, List<Renderer> back, List<Renderer> both)
        {
            var theme = board.theme;
            front.Clear(); back.Clear(); both.Clear();
            var root = new GameObject(RootName).transform;
            root.SetParent(board.transform, false);

            float t = theme.boardThickness;
            Vector2 size = board.Size;
            var slab = Group(root, "Board", Vector3.zero, board);
            Vector2 area = size + Vector2.one * theme.boardMargin * 2f;
            var tile = board.BoardTileModel;
            if (tile)
            {
                // Repeat the tile over the board; round to a whole number of tiles and resize them to fit exactly.
                int nx = Mathf.Max(1, Mathf.RoundToInt(area.x / theme.boardTileSize));
                int ny = Mathf.Max(1, Mathf.RoundToInt(area.y / theme.boardTileSize));
                var cell = new Vector2(area.x / nx, area.y / ny);
                for (int x = 0; x < nx; x++)
                for (int y = 0; y < ny; y++)
                {
                    var center = new Vector3(-theme.boardMargin + cell.x * (x + 0.5f), -theme.boardMargin + cell.y * (y + 0.5f), t * 0.5f);
                    Fit(slab, tile, center, Quaternion.identity, new Vector3(cell.x, cell.y, t), both);
                }
            }
            else Part(slab, Cube, new Vector3(size.x * 0.5f, size.y * 0.5f, t * 0.5f), Quaternion.identity,
                new Vector3(area.x, area.y, t), theme.boardMaterial, both);

            var path = new List<Vector2>();
            foreach (var trace in board.Traces)
            {
                if (!trace || !trace.IsValid) continue;
                trace.GetPath(board, false, path);
                BuildTrace(root, board, trace, path, theme, trace.layer == PcbLayer.Front ? front : back);
                if (trace.TryGetComponent(out GateMechanic gate))
                    BuildGate(root, board, trace, gate, path, theme, trace.layer == PcbLayer.Front ? front : back);
            }
            foreach (var node in board.Nodes)
            {
                if (!node) continue; // can go missing mid-rebuild (deleted via Undo/Erase while editing)
                BuildNode(root, board, node, theme, node.IsVia ? both : node.layer == PcbLayer.Front ? front : back);
            }
            foreach (var decor in board.Decorations)
            {
                if (!decor) continue;
                BuildDecoration(root, board, decor, theme, decor.layer == PcbLayer.Front ? front : back);
            }

            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                tr.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
                foreach (var c in tr.GetComponents<Component>()) c.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            }
            return root;
        }

        static void BuildTrace(Transform root, Board board, Trace trace, List<Vector2> path, PcbTheme theme, List<Renderer> list)
        {
            var g = Group(root, trace.name, Vector3.zero, trace);
            float w = theme.traceWidth, h = theme.traceHeight;
            float z = Surface(trace.layer, theme.boardThickness) + Out(trace.layer) * h * 0.5f;
            GameObject piece = board.TraceModel, bend = board.TraceBendModel;
            if (piece)
            {
                // Back side: turn pieces over so their top faces away from the board there too.
                var flip = trace.layer == PcbLayer.Back ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity;
                // Without a bend piece, straight pieces run half a width past each bend so corners have no gaps.
                float overlap = bend ? 0f : w * 0.5f;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    Vector2 a = path[i], b = path[i + 1], d = b - a, dir = d.normalized;
                    float before = i > 0 ? overlap : 0f, after = i < path.Count - 2 ? overlap : 0f;
                    Vector2 mid = (a + b) * 0.5f + dir * ((after - before) * 0.5f);
                    var rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg) * flip;
                    Fit(g, piece, new Vector3(mid.x, mid.y, z), rotation,
                        new Vector3(d.magnitude + before + after, w, h), list);

                    if (bend && i < path.Count - 2) // bend at the end of this segment
                        Fit(g, bend, new Vector3(b.x, b.y, z), rotation, new Vector3(w, w, h), list);
                }
                return;
            }
            for (int i = 0; i < path.Count; i++)
            {
                // Round joint at every point, flat strip for every segment.
                Part(g, Cylinder, new Vector3(path[i].x, path[i].y, z), Upright, new Vector3(w, h * 0.5f, w), theme.copperMaterial, list);
                if (i == path.Count - 1) break;
                Vector2 a = path[i], b = path[i + 1], d = b - a;
                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                Part(g, Cube, new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, z), Quaternion.Euler(0f, 0f, angle),
                    new Vector3(d.magnitude, w, h), theme.copperMaterial, list);
            }
        }

        static void BuildNode(Transform root, Board board, PcbNode node, PcbTheme theme, List<Renderer> list)
        {
            float t = theme.boardThickness;
            Vector2 p = board.NodePosition(node);
            PcbLayer side = node.IsVia ? PcbLayer.Front : node.layer;
            float o = Out(side);
            var g = Group(root, node.name, new Vector3(p.x, p.y, node.IsVia ? 0f : Surface(side, t)), node);
            g.localRotation = Quaternion.Euler(0f, 0f, node.rotationDegrees); // spin around the board normal
            if (node.TryGetComponent(out DataMechanic _))
                BuildHover(g, "Data", theme.dataPrefab, theme.dataHeight, 0f, side, theme, list);
            if (node.type == NodeType.Goal && board.GoalLocked) // a lock hovers over the goal while data is left
                BuildHover(g, "Goal Lock", theme.goalLockPrefab, theme.goalLockHeight, theme.goalLockUpOffset, side, theme, list);

            // GameObject model = node.type switch
            // {
            //     NodeType.Capacitor => theme.capacitorPrefab,
            //     NodeType.Via => theme.viaPrefab,
            //     NodeType.Start => theme.startPrefab,
            //     NodeType.Goal => theme.goalPrefab,
            //     _ => null
            // };
            var model = board.NodeModel(node);
            if (model)
            {
                var m = Object.Instantiate(model, g, false);
                m.transform.localRotation = side == PcbLayer.Back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                list.AddRange(m.GetComponentsInChildren<Renderer>(true));

                if (node.IsVia)
                {
                    // Vias need to appear on both sides of the board.
                    var m2 = Object.Instantiate(model, g, false);
                    var lp = m2.transform.localPosition;
                    m2.transform.localPosition = new Vector3(-lp.x, lp.y, t - lp.z);
                    m2.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * m2.transform.localRotation;
                    list.AddRange(m2.GetComponentsInChildren<Renderer>(true));
                }

                // Switch models show their starting ON/OFF look; SwitchMechanic swaps it on each press.
                bool isOn = node.TryGetComponent(out SwitchMechanic sw) && sw.isOn;
                foreach (var look in g.GetComponentsInChildren<SwitchVisual>(true)) look.Show(isOn);
                return;
            }

            switch (node.type)
            {
                case NodeType.Capacitor:
                {
                    float d = theme.capacitorSize, h = theme.capacitorHeight;
                    Part(g, Cylinder, new Vector3(0f, 0f, o * h * 0.5f), Upright, new Vector3(d, h * 0.5f, d), theme.capacitorMaterial, list);
                    Part(g, Cylinder, new Vector3(0f, 0f, o * (h + 0.004f)), Upright, new Vector3(d * 0.8f, 0.004f, d * 0.8f), theme.metalMaterial, list);
                    break;
                }
                case NodeType.Via:
                {
                    float d = theme.viaSize, half = t * 0.5f + theme.viaLip;
                    Part(g, Cylinder, new Vector3(0f, 0f, t * 0.5f), Upright, new Vector3(d, half, d), theme.metalMaterial, list);
                    Part(g, Cylinder, new Vector3(0f, 0f, t * 0.5f), Upright, new Vector3(d * 0.45f, half + 0.003f, d * 0.45f), theme.holeMaterial, list);
                    break;
                }
                case NodeType.Start:
                {
                    Vector3 s = theme.plugSize;
                    Part(g, Cube, new Vector3(0f, 0f, o * s.z * 0.5f), Quaternion.identity, s, theme.plugMaterial, list);
                    for (int i = -1; i <= 1; i += 2)
                        Part(g, Cylinder, new Vector3(i * s.x * 0.22f, 0f, o * (s.z + 0.08f)), Upright,
                            new Vector3(0.06f, 0.08f, 0.06f), theme.metalMaterial, list);
                    break;
                }
                case NodeType.Goal:
                {
                    Vector2 c = node.chipSize;
                    float h = theme.chipHeight, lift = 0.02f;
                    Part(g, Cube, new Vector3(0f, 0f, o * (lift + h * 0.5f)), Quaternion.identity, new Vector3(c.x, c.y, h), theme.chipMaterial, list);
                    // Pin-1 dot
                    Part(g, Cylinder, new Vector3(-c.x * 0.5f + 0.14f, c.y * 0.5f - 0.14f, o * (lift + h + 0.002f)), Upright,
                        new Vector3(0.08f, 0.002f, 0.08f), theme.holeMaterial, list);
                    // Legs along the two long sides
                    bool tall = c.y >= c.x;
                    float along = tall ? c.y : c.x, across = tall ? c.x : c.y;
                    int count = Mathf.Max(2, Mathf.FloorToInt(along / 0.25f));
                    float step = along / count;
                    for (int i = 0; i < count; i++)
                    {
                        float a = -along * 0.5f + step * (i + 0.5f);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float b = s * (across * 0.5f + 0.05f);
                            var pos = tall ? new Vector3(b, a, o * 0.025f) : new Vector3(a, b, o * 0.025f);
                            var scale = tall ? new Vector3(0.14f, 0.07f, 0.05f) : new Vector3(0.07f, 0.14f, 0.05f);
                            Part(g, Cube, pos, Quaternion.identity, scale, theme.metalMaterial, list);
                        }
                    }
                    break;
                }
                case NodeType.Switch:
                case NodeType.AndSwitch:
                {
                    float d = theme.capacitorSize, h = theme.capacitorHeight;
                    Part(g, Cylinder, new Vector3(0f, 0f, o * h * 0.5f), Upright, new Vector3(d, h * 0.5f, d), theme.metalMaterial, list);
                    Part(g, Cylinder, new Vector3(0f, 0f, o * (h + 0.008f)), Upright, new Vector3(d * 0.7f, 0.008f, d * 0.7f), theme.plugMaterial, list);
                    break;
                }
            }
        }

        /// <summary>
        /// A model hovering above a node (data pickup, goal lock): bobs gently, and HoverVisual.Dismiss() shrinks it
        /// away. No prefab = a small glowing cube placeholder.
        /// </summary>
        static void BuildHover(Transform g, string name, GameObject prefab, float height, float upOffset, PcbLayer side, PcbTheme theme, List<Renderer> list)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(g, false);
            // 'height' out from the board face; 'upOffset' up on screen = the board's +Y, undoing the node's own spin.
            Vector3 up = Quaternion.Inverse(g.localRotation) * Vector3.up;
            holder.localPosition = new Vector3(0f, 0f, Out(side) * height) + up * upOffset;
            holder.gameObject.AddComponent<HoverVisual>();
            if (prefab)
            {
                var m = Object.Instantiate(prefab, holder, false);
                if (side == PcbLayer.Back) m.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * m.transform.localRotation;
                list.AddRange(m.GetComponentsInChildren<Renderer>(true));
            }
            else Part(holder, Cube, Vector3.zero, Quaternion.Euler(45f, 45f, 0f), Vector3.one * 0.12f, theme.sparkMaterial, list);
        }

        /// <summary>
        /// A gate standing across the middle of its trace (by length, so bends are fine), lined up with the
        /// segment it sits on. Placed at the model's authored size; X = along the trace, top facing -Z.
        /// </summary>
        static void BuildGate(Transform root, Board board, Trace trace, GateMechanic gate, List<Vector2> path, PcbTheme theme, List<Renderer> list)
        {
            if (path.Count < 2) return;
            PathMiddle(path, out Vector2 middle, out Vector2 direction);
            float z = Surface(trace.layer, theme.boardThickness) + Out(trace.layer) * theme.traceHeight; // on top of the copper
            var g = Group(root, trace.name + " Gate", new Vector3(middle.x, middle.y, z), gate);
            g.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg) *
                (trace.layer == PcbLayer.Back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);

            var model = board.GateModel(gate);
            if (model)
            {
                var m = Object.Instantiate(model, g, false);
                list.AddRange(m.GetComponentsInChildren<Renderer>(true));
            }
            else // placeholder barrier across the trace
                Part(g, Cube, new Vector3(0f, 0f, Out(PcbLayer.Front) * 0.08f), Quaternion.identity,
                    new Vector3(0.06f, theme.traceWidth * 2.5f, 0.16f), theme.chipMaterial, list);
            ShowGateState(g, gate.ShownOpen);
        }

        /// <summary>OPEN / CLOSED look of a gate: its LockVisual (Locked = closed) if it has one, else hidden while open.</summary>
        public static void ShowGateState(Transform gateLook, bool open)
        {
            var looks = gateLook.GetComponentsInChildren<LockVisual>(true);
            if (looks.Length > 0)
            {
                foreach (var look in looks) look.Show(!open);
                return;
            }
            for (int i = 0; i < gateLook.childCount; i++) gateLook.GetChild(i).gameObject.SetActive(!open);
        }

        /// <summary>Point halfway along a path (by length) and the direction of the segment it's on.</summary>
        static void PathMiddle(List<Vector2> path, out Vector2 middle, out Vector2 direction)
        {
            float total = 0f;
            for (int i = 0; i < path.Count - 1; i++) total += Vector2.Distance(path[i], path[i + 1]);
            float half = total * 0.5f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                float d = Vector2.Distance(path[i], path[i + 1]);
                if (half <= d || i == path.Count - 2)
                {
                    direction = (path[i + 1] - path[i]).normalized;
                    middle = Vector2.Lerp(path[i], path[i + 1], d > 0f ? Mathf.Clamp01(half / d) : 0f);
                    return;
                }
                half -= d;
            }
            middle = path[0];
            direction = Vector2.right;
        }

        static void BuildDecoration(Transform root, Board board, PcbDecoration decor, PcbTheme theme, List<Renderer> list)
        {
            float t = theme.boardThickness;
            Vector2 p = board.WorldToLocal(decor.transform.position);
            var g = Group(root, decor.name, new Vector3(p.x, p.y, Surface(decor.layer, t)), decor);
            g.localRotation = Quaternion.Euler(0f, 0f, decor.rotationDegrees);

            var model = board.DecorationModel(decor);
            if (model)
            {
                var m = Object.Instantiate(model, g, false);
                if (decor.layer == PcbLayer.Back)
                {
                    var lp = m.transform.localPosition;
                    m.transform.localPosition = new Vector3(-lp.x, lp.y, -lp.z);
                    m.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * m.transform.localRotation;
                }
                list.AddRange(m.GetComponentsInChildren<Renderer>(true));
                return;
            }

            // Generic placeholder until real art is assigned in PcbTheme.decorationPrefabs.
            float thickness = 0.02f;
            var flip = decor.layer == PcbLayer.Back ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            Part(g, Cube, new Vector3(0f, 0f, Out(decor.layer) * thickness * 0.5f), flip,
                new Vector3(0.22f, 0.12f, thickness), theme.metalMaterial, list);
        }

        static Transform Group(Transform parent, string name, Vector3 localPosition, Component owner)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<PcbVisualOwner>().owner = owner;
            return go.transform;
        }

        /// <summary>
        /// Places a model so its mesh bounds exactly fill a box of 'size' centred on 'center', whatever size it was authored at.
        /// Keeps the theme's numbers (trace height, board thickness...) the single source of truth for gameplay.
        /// </summary>
        static void Fit(Transform parent, GameObject model, Vector3 center, Quaternion rotation, Vector3 size, List<Renderer> list)
        {
            var pivot = new GameObject(model.name).transform;
            pivot.SetParent(parent, false);
            pivot.SetLocalPositionAndRotation(center, rotation);
            var m = Object.Instantiate(model, pivot, false).transform;

            bool any = false;
            var bounds = new Bounds();
            void Encapsulate(Transform owner, Mesh mesh)
            {
                if (!mesh) return;
                Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = pivot.InverseTransformPoint(owner.TransformPoint(corner));
                    if (any) bounds.Encapsulate(p); else { bounds = new Bounds(p, Vector3.zero); any = true; }
                }
            }
            foreach (var mf in m.GetComponentsInChildren<MeshFilter>(true)) Encapsulate(mf.transform, mf.sharedMesh);
            foreach (var sm in m.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Encapsulate(sm.transform, sm.sharedMesh);

            if (any)
            {
                m.localPosition -= bounds.center;
                Vector3 b = bounds.size;
                // A perfectly flat axis (e.g. a plane) can't be stretched; leave it as is.
                pivot.localScale = new Vector3(
                    b.x > 1e-4f ? size.x / b.x : 1f,
                    b.y > 1e-4f ? size.y / b.y : 1f,
                    b.z > 1e-4f ? size.z / b.z : 1f);
            }
            list?.AddRange(m.GetComponentsInChildren<Renderer>(true));
        }

        public static MeshRenderer Part(Transform parent, Mesh mesh, Vector3 localPosition, Quaternion localRotation,
            Vector3 localScale, Material material, List<Renderer> list)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            list?.Add(r);
            return r;
        }
    }
}
