using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pcb
{
    /// <summary>Shared look of every board. Created automatically by Tools > PCB > Level Editor.</summary>
    [CreateAssetMenu(menuName = "PCB/Theme", fileName = "PcbTheme")]
    public class PcbTheme : ScriptableObject
    {
        [Header("Materials (generated in Assets/PCB/Materials, edit freely)")]
        public Material boardMaterial;
        public Material copperMaterial;
        public Material capacitorMaterial;
        public Material metalMaterial;
        public Material holeMaterial;
        public Material chipMaterial;
        public Material plugMaterial;
        public Material sparkMaterial;
        [Tooltip("Unlit material for the spark trail and the direction arrows.")]
        public Material spriteMaterial;
        public Sprite triangle;

        [Header("Optional models (empty = simple shapes). Author them facing -Z, sitting at Z = 0.")]
        public GameObject capacitorPrefab;
        public GameObject viaPrefab;
        public GameObject startPrefab;
        public GameObject goalPrefab;
        [Tooltip("Normal switch. Give the prefab a SwitchVisual with its ON and OFF models to show the state.")]
        public GameObject switchPrefab;
        [Tooltip("AND switch. Give the prefab a SwitchVisual with its ON (pressed) and OFF models to show the state.")]
        public GameObject andSwitchPrefab;
        [Tooltip("Gate standing across a trace: length along X (the trace), top facing -Z. Give it a LockVisual with its CLOSED (Locked) and OPEN (Unlocked) models; without one it just disappears when open.")]
        public GameObject gatePrefab;
        [Tooltip("Data pickup, hovering above its capacitor. Author it centred on its own origin. Empty = small glowing cube.")]
        public GameObject dataPrefab;
        [Tooltip("Lock hovering over the goal while data is left; shrinks away when the last data is collected. Centred on its own origin.")]
        public GameObject goalLockPrefab;
        [Tooltip("One repeatable board tile, thickness along Z. Resized to fill boardTileSize x boardTileSize x boardThickness and repeated across the whole board.")]
        public GameObject boardTilePrefab;
        [Tooltip("One straight piece of trace, length along X, height along Z. Stretched to each segment's length x traceWidth x traceHeight.")]
        public GameObject tracePrefab;
        [Tooltip("Optional, used with Trace Prefab: piece placed at every bend (e.g. a round disc). Resized to traceWidth x traceWidth x traceHeight; X points along the incoming segment. Empty = straight pieces overlap at corners instead.")]
        public GameObject traceBendPrefab;

        [Serializable]
        public struct DecorationLook
        {
            public DecorType type;
            public GameObject prefab;
        }
        [Header("Decorations (optional models, empty = generic placeholder box)")]
        [Tooltip("Several entries of the same type = variants for the Paint tool; the first one is the default.")]
        public DecorationLook[] decorationPrefabs;

        [Header("Catalog: extra choices for the Level Editor's Look section and Paint tool (the slots above stay the defaults)")]
        public GameObject[] capacitorVariants;
        public GameObject[] viaVariants;
        public GameObject[] startVariants;
        public GameObject[] goalVariants;
        public GameObject[] switchVariants;
        public GameObject[] andSwitchVariants;
        public GameObject[] gateVariants;
        public GameObject[] boardTileVariants;
        public GameObject[] traceVariants;
        public GameObject[] traceBendVariants;

        public GameObject GetDecorationPrefab(DecorType type)
        {
            if (decorationPrefabs == null) return null;
            foreach (var d in decorationPrefabs)
                if (d.type == type) return d.prefab;
            return null;
        }

        /// <summary>Every model listed for a decoration type, default first.</summary>
        public void GetDecorationVariants(DecorType type, List<GameObject> result)
        {
            result.Clear();
            if (decorationPrefabs == null) return;
            foreach (var d in decorationPrefabs)
                if (d.type == type && d.prefab && !result.Contains(d.prefab)) result.Add(d.prefab);
        }

        /// <summary>The theme's default model for a node type (null = simple shapes).</summary>
        public GameObject NodePrefab(NodeType type) => type switch
        {
            NodeType.Capacitor => capacitorPrefab,
            NodeType.Via => viaPrefab,
            NodeType.Start => startPrefab,
            NodeType.Switch => switchPrefab,
            NodeType.AndSwitch => andSwitchPrefab,
            _ => goalPrefab
        };

        public GameObject[] NodeVariants(NodeType type) => type switch
        {
            NodeType.Capacitor => capacitorVariants,
            NodeType.Via => viaVariants,
            NodeType.Start => startVariants,
            NodeType.Switch => switchVariants,
            NodeType.AndSwitch => andSwitchVariants,
            _ => goalVariants
        };

        [Header("Scene")]
        public Color background = new Color32(228, 228, 228, 255);

        [Header("Board")]
        public float boardThickness = 0.16f;
        [Tooltip("Extra board around the outermost grid line, in world units.")]
        public float boardMargin = 0.5f;
        [Tooltip("Board tile only: target size of one tile in world units. Tiles are resized slightly so a whole number of them fills the board.")]
        [Min(0.1f)] public float boardTileSize = 1f;

        [Header("Traces")]
        public float traceWidth = 0.14f;
        public float traceHeight = 0.025f;

        [Header("Capacitor")]
        public float capacitorSize = 0.24f;
        public float capacitorHeight = 0.21f;

        [Header("Via")]
        public float viaSize = 0.2f;
        [Tooltip("How far the via sticks out of each face.")]
        public float viaLip = 0.02f;

        [Header("Goal chip")]
        public float chipHeight = 0.14f;

        [Header("Data pickup / goal lock")]
        [Tooltip("How far above the board surface the data floats, in world units.")]
        public float dataHeight = 0.45f;
        [Tooltip("How far out from the board face the goal lock hovers while data is left (towards the camera).")]
        public float goalLockHeight = 0.4f;
        [Tooltip("Moves the goal lock up on screen (along the board), so it floats above the goal instead of in front of the spark standing there.")]
        public float goalLockUpOffset = 0f;

        [Header("Start plug")]
        public Vector3 plugSize = new Vector3(0.46f, 0.34f, 0.26f);

        [Header("Spark")]
        public Color spark = new Color32(134, 227, 255, 255);
        public Color sparkBlocked = new Color32(255, 90, 90, 255);
        [Tooltip("Emission multiplier. Higher = stronger bloom glow.")]
        public float sparkGlow = 4f;
        public float sparkSize = 0.12f;
        public float sparkLightRange = 1.5f;
        public float sparkLightIntensity = 2f;

        [Header("Editor")]
        [Tooltip("Colour of the hidden side, drawn over the board while editing only.")]
        public Color editorGhost = new Color(1f, 1f, 1f, 0.35f);

        public bool IsComplete =>
            boardMaterial && copperMaterial && capacitorMaterial && metalMaterial && holeMaterial &&
            chipMaterial && plugMaterial && sparkMaterial && spriteMaterial && triangle;
    }
}
