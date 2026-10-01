using System.Collections.Generic;
using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// Root of a level. Collects every PcbNode/Trace under it, builds the movement graph
    /// and the 3D look. Runs in edit mode so the board updates while you draw it.
    /// Gameplay happens in board-local XY; the front face is z = 0, the back face z = thickness.
    /// </summary>
    [ExecuteAlways]
    public class Board : MonoBehaviour
    {
        /// <summary>This level's own models. Empty entries fall back to the theme.</summary>
        [System.Serializable]
        public struct BoardLook
        {
            public GameObject boardTile;
            public GameObject trace;
            public GameObject traceBend;
            [Tooltip("Default model for every node of that type on this level (a node's own Model still wins).")]
            public GameObject capacitor, via, start, goal, switchNode, andSwitch;
            [Tooltip("Default gate model on this level (a gate's own Model still wins).")]
            public GameObject gate;

            public GameObject For(NodeType type) => type switch
            {
                NodeType.Capacitor => capacitor,
                NodeType.Via => via,
                NodeType.Start => start,
                NodeType.Switch => switchNode,
                NodeType.AndSwitch => andSwitch,
                _ => goal
            };

            public void Set(NodeType type, GameObject model)
            {
                switch (type)
                {
                    case NodeType.Capacitor: capacitor = model; break;
                    case NodeType.Via: via = model; break;
                    case NodeType.Start: start = model; break;
                    case NodeType.Switch: switchNode = model; break;
                    case NodeType.AndSwitch: andSwitch = model; break;
                    default: goal = model; break;
                }
            }
        }

        public struct Exit
        {
            public Trace trace;
            public bool reversed;
            public PcbNode target;
            public Vector2 direction;
            public PcbLayer layer;
        }

        public string levelName = "New Level";
        public PcbTheme theme;
        [Tooltip("Optional. Shown once, before the player gets control, when this level starts.")]
        public DialogSequence dialogSequence;
        [Tooltip("Optional. Intro cinematic prefab (StageIntro) played when this stage is entered, before the spark appears.")]
        public StageIntro intro;
        [Tooltip("This level's look (Level Editor > Look). Empty entries use the theme.")]
        public BoardLook look;
        [Min(0.1f)] public float cellSize = 0.5f;
        public Vector2Int sizeInCells = new Vector2Int(32, 20);
        [Tooltip("How closely input must match a trace to take it (1 = exact, 0.5 = within 60 degrees).")]
        [Range(0.1f, 1f)] public float inputTolerance = 0.5f;
        [Tooltip("Side being edited in the Level Editor.")]
        public PcbLayer editorView = PcbLayer.Front;
        [HideInInspector] public string savedPath; // prefab this board was last saved to / loaded from (editor use)

        readonly List<PcbNode> nodes = new List<PcbNode>();
        readonly List<Trace> traces = new List<Trace>();
        readonly List<PcbDecoration> decorations = new List<PcbDecoration>();
        readonly Dictionary<PcbNode, List<Exit>> exits = new Dictionary<PcbNode, List<Exit>>();
        static readonly List<Exit> NoExits = new List<Exit>();

        readonly List<Renderer> frontRenderers = new List<Renderer>();
        readonly List<Renderer> backRenderers = new List<Renderer>();
        readonly List<Renderer> sharedRenderers = new List<Renderer>();
        Transform visuals;
        int visualsSignature;
        bool showBothSides;

        public PcbLayer View { get; private set; }
        public IReadOnlyList<PcbNode> Nodes => nodes;
        public IReadOnlyList<Trace> Traces => traces;
        public IReadOnlyList<PcbDecoration> Decorations => decorations;
        public Vector2 Size => (Vector2)sizeInCells * cellSize;
        public float Thickness => theme ? theme.boardThickness : 0.16f;
        /// <summary>World-space centre of the board volume.</summary>
        public Vector3 Center => transform.TransformPoint(new Vector3(Size.x * 0.5f, Size.y * 0.5f, Thickness * 0.5f));

        void Awake()
        {
            if (!Application.isPlaying) return;
            showBothSides = false;
            Rebuild();
        }

        void Update()
        {
            if (Application.isPlaying) return;
            Rebuild();
        }

        /// <summary>Re-collects nodes and traces, rebuilds the movement graph and (if anything changed) the 3D look.</summary>
        public void Rebuild()
        {
            // Editing: everything visible, the editor draws the hidden side as a ghost. Set here rather than only
            // in Update, so a Rebuild called straight away (Level Editor Load / New Level) doesn't build the look
            // with the back side hidden - it would stay hidden, since nothing re-applies it until the level changes.
            if (!Application.isPlaying) showBothSides = true;
            nodes.Clear();
            foreach (var c in GetComponentsInChildren<PcbNode>(true))
                if ((c.gameObject.hideFlags & HideFlags.DontSave) == 0) nodes.Add(c);

            traces.Clear();
            foreach (var c in GetComponentsInChildren<Trace>(true))
                if ((c.gameObject.hideFlags & HideFlags.DontSave) == 0) traces.Add(c);

            decorations.Clear();
            foreach (var c in GetComponentsInChildren<PcbDecoration>(true))
                if ((c.gameObject.hideFlags & HideFlags.DontSave) == 0) decorations.Add(c);
            RemoveLegacyComponents();
            BuildGraph();

            if (!theme || !theme.IsComplete) return;
            int signature = ComputeSignature();
            if (visuals && signature == visualsSignature) return;
            DestroyVisuals();
            visuals = BoardVisuals.Build(this, frontRenderers, backRenderers, sharedRenderers);
            visualsSignature = signature;
            ApplyVisibility();
        }

        /// <summary>Removes the generated 3D objects (the editor does this before saving a level).</summary>
        public void DestroyVisuals()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == BoardVisuals.RootName) Kill(child.gameObject);
            }
            visuals = null;
            frontRenderers.Clear();
            backRenderers.Clear();
            sharedRenderers.Clear();
        }

        void BuildGraph()
        {
            exits.Clear();
            foreach (var t in traces)
            {
                if (!t || !t.IsValid) continue;
                AddExit(t.from, t, false, t.to);
                AddExit(t.to, t, true, t.from);
            }
        }

        void AddExit(PcbNode node, Trace trace, bool reversed, PcbNode target)
        {
            if (!exits.TryGetValue(node, out var list)) exits[node] = list = new List<Exit>();
            list.Add(new Exit
            {
                trace = trace,
                reversed = reversed,
                target = target,
                direction = trace.ExitDirection(this, reversed),
                layer = trace.layer
            });
        }

        public IReadOnlyList<Exit> GetExits(PcbNode node) =>
            node && exits.TryGetValue(node, out var list) ? list : NoExits;

        /// <summary>Picks the exit on 'layer' that best matches a board-space direction.</summary>
        public bool TryPickExit(PcbNode node, PcbLayer layer, Vector2 direction, out Exit exit)
        {
            exit = default;
            bool found = false;
            float best = inputTolerance;
            Vector2 dir = direction.normalized;
            foreach (var e in GetExits(node))
            {
                if (e.layer != layer) continue;
                float d = Vector2.Dot(e.direction, dir);
                if (d > best) { best = d; exit = e; found = true; }
            }
            return found;
        }

        // ---------------------------------------------------------------- sides

        /// <summary>Sets the side the player is on. In play mode the other side is hidden.</summary>
        public void SetView(PcbLayer layer)
        {
            View = layer;
            showBothSides = !Application.isPlaying;
            ApplyVisibility();
        }

        /// <summary>Shows both sides (used while the board is turning over).</summary>
        public void ShowBothSides()
        {
            showBothSides = true;
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            foreach (var r in frontRenderers) if (r) r.enabled = showBothSides || View == PcbLayer.Front;
            foreach (var r in backRenderers) if (r) r.enabled = showBothSides || View == PcbLayer.Back;
        }

        // ---------------------------------------------------------------- look
        // Which model to use: the object's own override, then this level's look, then the theme.

        public GameObject BoardTileModel => look.boardTile ? look.boardTile : theme ? theme.boardTilePrefab : null;
        public GameObject TraceModel => look.trace ? look.trace : theme ? theme.tracePrefab : null;
        public GameObject TraceBendModel => look.traceBend ? look.traceBend : theme ? theme.traceBendPrefab : null;

        public GameObject DefaultNodeModel(NodeType type)
        {
            var m = look.For(type);
            return m ? m : theme ? theme.NodePrefab(type) : null;
        }

        public GameObject NodeModel(PcbNode node) => node.model ? node.model : DefaultNodeModel(node.type);
        public GameObject DecorationModel(PcbDecoration decor) =>
            decor.model ? decor.model : theme ? theme.GetDecorationPrefab(decor.type) : null;
        public GameObject GateModel(GateMechanic gate) =>
            gate.model ? gate.model : look.gate ? look.gate : theme ? theme.gatePrefab : null;

        // ---------------------------------------------------------------- data / goal lock

        /// <summary>This level has data pickups at all (its goal shows a lock until they're collected).</summary>
        public bool HasData
        {
            get
            {
                foreach (var n in nodes)
                    if (n && n.TryGetComponent(out DataMechanic _)) return true;
                return false;
            }
        }

        /// <summary>True while any data pickup on the board is uncollected: the Goal doesn't win yet.</summary>
        public bool GoalLocked
        {
            get
            {
                foreach (var n in nodes)
                    if (n && n.TryGetComponent(out DataMechanic data) && !data.Collected) return true;
                return false;
            }
        }

        /// <summary>The generated look groups belonging to a node/trace/decoration.</summary>
        public List<Transform> VisualsOf(Component owner)
        {
            var result = new List<Transform>();
            if (visuals)
                foreach (var v in visuals.GetComponentsInChildren<PcbVisualOwner>(true))
                    if (v.owner == owner) result.Add(v.transform);
            return result;
        }

        /// <summary>Sends every goal's hovering lock away; returns the goals' looks (e.g. for an effect).</summary>
        public List<Transform> UnlockGoals()
        {
            var result = new List<Transform>();
            foreach (var n in nodes)
            {
                if (!n || n.type != NodeType.Goal) continue;
                foreach (var look in VisualsOf(n))
                {
                    foreach (var hover in look.GetComponentsInChildren<HoverVisual>()) hover.Dismiss();
                    result.Add(look);
                }
            }
            return result;
        }

        // ---------------------------------------------------------------- coordinates

        public Vector2 NodePosition(PcbNode node) => WorldToLocal(node.transform.position);
        public Vector2 WorldToLocal(Vector3 world) => transform.InverseTransformPoint(world);
        public Vector3 LocalToWorld(Vector2 local) => transform.TransformPoint(local);

        /// <summary>World position on (or above) the surface of one side.</summary>
        public Vector3 SurfaceToWorld(Vector2 local, PcbLayer layer, float height = 0f) =>
            transform.TransformPoint(new Vector3(local.x, local.y,
                BoardVisuals.Surface(layer, Thickness) + BoardVisuals.Out(layer) * height));

        public Vector2 SnapLocal(Vector2 local)
        {
            float x = Mathf.Clamp(Mathf.Round(local.x / cellSize), 0, sizeInCells.x);
            float y = Mathf.Clamp(Mathf.Round(local.y / cellSize), 0, sizeInCells.y);
            return new Vector2(x, y) * cellSize;
        }

        // ---------------------------------------------------------------- helpers

        int ComputeSignature()
        {
            unchecked
            {
                int h = 17;
                void Add(int v) => h = h * 31 + v;
                void AddV(Vector2 v) { Add(Mathf.RoundToInt(v.x * 1000f)); Add(Mathf.RoundToInt(v.y * 1000f)); }

                Add(Id(theme));
                if (!Application.isPlaying) Add(JsonUtility.ToJson(theme).GetHashCode()); // live theme tweaks while editing
                Add(sizeInCells.x); Add(sizeInCells.y); Add(Mathf.RoundToInt(cellSize * 1000f));
                Add(Id(look.boardTile)); Add(Id(look.trace)); Add(Id(look.traceBend));
                Add(Id(look.capacitor)); Add(Id(look.via)); Add(Id(look.start)); Add(Id(look.goal)); Add(Id(look.switchNode)); Add(Id(look.andSwitch)); Add(Id(look.gate));
                foreach (var n in nodes)
                {
                    if (!n) continue; // can go missing mid-rebuild (deleted via Undo/Erase while editing)
                    Add(Id(n)); Add((int)n.type); Add((int)n.layer); Add(Id(n.model));
                    Add(Mathf.RoundToInt(n.rotationDegrees * 1000f));
                    if (n.TryGetComponent(out SwitchMechanic sw)) Add(sw.isOn ? 1 : 2); // starting ON/OFF look
                    if (n.TryGetComponent(out DataMechanic _)) Add(3); // data pickup (also grays the goal)
                    AddV(NodePosition(n)); AddV(n.chipSize); Add(n.name.GetHashCode());
                }
                foreach (var t in traces)
                {
                    if (!t) continue;
                    Add(Id(t)); Add(Id(t.from)); Add(Id(t.to));
                    Add((int)t.layer); Add(t.bends.Count);
                    if (t.TryGetComponent(out GateMechanic gate)) { Add(Id(gate.model)); Add(gate.ShownOpen ? 1 : 2); }
                    foreach (var b in t.bends) AddV(b);
                }
                foreach (var d in decorations)
                {
                    if (!d) continue;
                    Add(Id(d)); Add((int)d.type); Add((int)d.layer); Add(Id(d.model));
                    AddV(WorldToLocal(d.transform.position)); Add(Mathf.RoundToInt(d.rotationDegrees * 1000f));
                }
                return h;
            }
        }

        /// <summary>Levels made with the first 2D version carry sprites and lines; strip them.</summary>
        void RemoveLegacyComponents()
        {
            foreach (var n in nodes)
            {
                if (!n) continue;
                if (n.TryGetComponent(out SpriteRenderer sr)) Kill(sr);
                if (n.transform.localScale != Vector3.one && !Application.isPlaying) n.transform.localScale = Vector3.one;
            }
            foreach (var t in traces)
                if (t && t.TryGetComponent(out LineRenderer lr)) Kill(lr);
            var background = transform.Find("Background");
            if (background && background.GetComponent<SpriteRenderer>()) Kill(background.gameObject);
        }

        // Identity hash for change detection. Avoids GetInstanceID(), which newer Unity 6 versions reject.
        static int Id(Object o) => o ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o) : 0;

        static void Kill(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
