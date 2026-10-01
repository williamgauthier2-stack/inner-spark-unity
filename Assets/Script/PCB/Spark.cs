using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pcb
{
    /// <summary>
    /// The player: a glowing sphere riding on the board. Waits on a node, takes a direction, then slides
    /// along the trace at a fixed speed until the next node. On a via, Flip turns the board over and the
    /// spark passes through to the other side. Directions are read on screen, so they stay intuitive
    /// when the board is showing its (mirrored) back. An input pressed just before the spark is ready
    /// (within 'inputBuffer' seconds) is used on arrival; older ones are forgotten.
    ///
    /// With a character 'model' assigned (the Spark prefab on the LevelManager), the sphere is replaced:
    /// the character shrinks away (MoveStart), travels as electricity (travel VFX), reappears at the next
    /// node (MoveFinish) and idles there. Flips play MoveStart, turn the board, then MoveFinish.
    /// </summary>
    public class Spark : MonoBehaviour
    {
        [Tooltip("World units per second. Fixed for the whole game.")]
        public float speed = 8f;
        [Tooltip("A move/flip pressed at most this many seconds before the spark is ready (still moving or animating) is carried over; earlier presses are forgotten, so a double tap is one step. 0 = never carry over.")]
        [Min(0f)] public float inputBuffer = 0.15f;

        [Header("Character (optional, empty = the glowing sphere)")]
        [Tooltip("Child object holding the character. Its position/rotation in the prefab is how it sits on the FRONT side; the back side is mirrored automatically.")]
        public Transform model;
        [Tooltip("Optional. Found on the model if empty.")]
        public Animator animator;
        [Tooltip("Animator state names, played directly by name (no transitions needed).")]
        public string idleState = "Idle";
        public string moveStartState = "MoveStart";
        public string moveFinishState = "MoveFinish";
        public string winState = "Win";

        [Header("Character VFX (optional)")]
        [Tooltip("Plays while the character stands on a node. A child (put it under the model to follow it), or a VFX prefab file: then a copy is placed on the model at spawn.")]
        public GameObject idleVfx;
        [Tooltip("Plays while running along a trace (the character is hidden then). A child, or a VFX prefab file: then a copy is placed on the spark at spawn.")]
        public GameObject travelVfx;
        [Tooltip("Prefab spawned for a moment: on a blocked move, when using a switch, and now and then while running.")]
        public GameObject burstVfx;
        [Tooltip("Random seconds between bursts while running (min, max). Max 0 = no bursts while running.")]
        public Vector2 travelBurstInterval = new Vector2(0.2f, 0.5f);
        [Tooltip("Keep the point light that lights up the board around the spark.")]
        public bool glowLight = true;
        [Tooltip("Show the direction arrows around the node (always shown for the sphere).")]
        public bool directionArrows = false;

        public Board Board { get; private set; }
        public PcbNode CurrentNode { get; private set; }
        public PcbLayer Layer { get; private set; }
        public bool IsMoving { get; private set; }
        public bool IsTurning => rig && rig.IsTurning;
        /// <summary>Playing a character animation between moves (input waits, like while moving).</summary>
        public bool IsBusy { get; private set; }
        public bool HasCharacter => model;
        /// <summary>
        /// Ignore player input (intro dialog, level won). Used instead of disabling the component,
        /// so the character's animation sequences keep running.
        /// </summary>
        public bool InputLocked { get; set; }

        bool appearRequested;

        /// <summary>
        /// Shows the spark (character: plays MoveFinish on the start node; watch IsBusy for the end). Hidden until
        /// then - the LevelManager calls this in the stage start sequence, after the fade-in / intro cinematic.
        /// </summary>
        public void Appear()
        {
            appearRequested = true;
            if (!HasCharacter) SetSphereVisible(true);
        }

        public event Action<PcbNode> Arrived;
        public event Action<PcbLayer> Flipped;
        public event Action Blocked;
        /// <summary>The goal was reached and the win animation has finished (right away without a character).</summary>
        public event Action WinFinished;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        BoardRig rig;
        InputAction moveAction, flipAction;
        Vector2Int lastAxes;
        bool hasQueuedMove, hasQueuedFlip;
        float moveQueuedAt, flipQueuedAt;
        Vector2 queuedMove;

        Board.Exit travelling;
        readonly List<Vector2> path = new List<Vector2>();
        int segment;
        float segmentProgress;
        bool wasTurning;
        float turnFromZ, turnToZ;

        bool built;
        Transform core;
        MeshRenderer coreRenderer;
        MaterialPropertyBlock block;
        Light glow;
        TrailRenderer trail;
        readonly List<SpriteRenderer> arrows = new List<SpriteRenderer>();
        float blockedFlash;

        // Character
        Vector3 modelPosition, modelBase;
        Quaternion modelRotation;
        readonly List<Renderer> modelRenderers = new List<Renderer>();
        float nextBurst = float.MaxValue;

        void Awake()
        {
            moveAction = new InputAction("Move", InputActionType.Value);
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick");
            moveAction.AddBinding("<Gamepad>/dpad");

            flipAction = new InputAction("Flip", InputActionType.Button);
            flipAction.AddBinding("<Keyboard>/space");
            flipAction.AddBinding("<Gamepad>/buttonSouth");

            if (model)
            {
                if (!animator) animator = model.GetComponentInChildren<Animator>();
                modelPosition = model.localPosition;
                modelRotation = model.localRotation;
                // Only the character's meshes: particle/trail renderers of VFX under the model keep their own control.
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer)) modelRenderers.Add(r);
            }

            // These two may point at the VFX prefab file instead of a child: then place a copy on the spark.
            idleVfx = OwnCopy(idleVfx, model ? model : transform);
            travelVfx = OwnCopy(travelVfx, transform);
        }

        GameObject OwnCopy(GameObject vfx, Transform parent)
        {
            if (!vfx || vfx.transform.IsChildOf(transform)) return vfx; // already part of the spark
            var copy = Instantiate(vfx, parent, false);
            copy.name = vfx.name;
            // Cancel the parent's scale (e.g. a scaled-down model) so the effect keeps its authored size.
            Vector3 s = parent.lossyScale;
            copy.transform.localScale = new Vector3(
                Mathf.Abs(s.x) > 1e-4f ? 1f / s.x : 1f,
                Mathf.Abs(s.y) > 1e-4f ? 1f / s.y : 1f,
                Mathf.Abs(s.z) > 1e-4f ? 1f / s.z : 1f);
            return copy;
        }

        void OnEnable() { moveAction.Enable(); flipAction.Enable(); }
        void OnDisable() { moveAction.Disable(); flipAction.Disable(); }
        void OnDestroy()
        {
            moveAction.Dispose();
            flipAction.Dispose();
            if (IsMoving) AudioManager.SetTravelling(false); // restarted mid-trace
        }

        public void Init(Board board, PcbNode start, BoardRig boardRig)
        {
            Board = board;
            rig = boardRig;
            CurrentNode = start;
            Layer = start.layer;
            IsMoving = false;
            transform.SetParent(board.transform, false);
            transform.localRotation = Quaternion.identity;
            transform.localPosition = LocalPosition(board.NodePosition(start), Layer);
            BuildVisuals();
            if (rig) rig.SnapTo(Layer);
            else board.SetView(Layer);
            if (trail) trail.Clear();
            RefreshArrows();

            if (HasCharacter)
            {
                FaceSide();
                StopVfx(travelVfx);
                StopVfx(idleVfx);
                StartCoroutine(AppearSequence());
            }
            else SetSphereVisible(appearRequested);
        }

        void SetSphereVisible(bool visible)
        {
            if (coreRenderer) coreRenderer.enabled = visible;
            if (glow) glow.enabled = visible;
            if (trail) { trail.Clear(); trail.emitting = visible; }
        }

        void Update()
        {
            if (!Board) return;
            ReadInput();

            if (IsTurning)
            {
                // Travel through the via while the board turns over.
                var p = transform.localPosition;
                p.z = Mathf.Lerp(turnFromZ, turnToZ, rig.TurnProgress);
                transform.localPosition = p;
                wasTurning = true;
                AnimateVisuals();
                return;
            }
            if (wasTurning)
            {
                wasTurning = false;
                transform.localPosition = LocalPosition(Board.NodePosition(CurrentNode), Layer);
                if (trail)
                {
                    trail.Clear();
                    trail.emitting = true;
                }
                RefreshArrows();
            }

            if (IsMoving)
            {
                Move(speed * Time.deltaTime);
                if (IsMoving && HasCharacter && Time.time >= nextBurst) { SpawnBurst(); ScheduleBurst(); }
            }

            if (!IsMoving && !IsBusy && !InputLocked && enabled) // locked: intro dialog showing, or the goal was reached
            {
                // Only carry over a press made shortly before now; an older one (e.g. the second tap of a
                // double tap, made while the first move was still playing) is forgotten.
                if (hasQueuedFlip && Time.time - flipQueuedAt > inputBuffer) hasQueuedFlip = false;
                if (hasQueuedMove && Time.time - moveQueuedAt > inputBuffer) hasQueuedMove = false;

                if (hasQueuedFlip) { hasQueuedFlip = false; TryFlip(); }
                else if (hasQueuedMove) { hasQueuedMove = false; TryMove(ScreenToBoard(queuedMove)); }
            }
            AnimateVisuals();
        }

        float inputGraceTimer;

        void ReadInput()
        {
            // Paused / locked: drop anything queued so nothing fires the moment play resumes.
            bool paused = PauseMenu.GamePaused || InputLocked;
            if (paused) { hasQueuedMove = hasQueuedFlip = false; inputGraceTimer = 0f; }
            else if (flipAction.WasPressedThisFrame()) { hasQueuedFlip = true; flipQueuedAt = Time.time; }

            // Per-axis direction held (-1, 0, 1). Only an axis that becomes active or reverses is a press:
            // letting go of a key never is, so releasing one key of a diagonal a moment before the other
            // (which leaves e.g. just "right" held for a frame) doesn't queue an extra move. Holding a key
            // doesn't repeat either. Tracked while paused too, so a key held through Resume isn't a press.
            Vector2 v = moveAction.ReadValue<Vector2>();
            var axes = new Vector2Int(Axis(v.x), Axis(v.y));
            bool pressed = (axes.x != 0 && axes.x != lastAxes.x) || (axes.y != 0 && axes.y != lastAxes.y);
            lastAxes = axes;

            if (inputGraceTimer > 0f)
            {
                if (!paused)
                {
                    inputGraceTimer -= Time.deltaTime;
                    if (axes != Vector2Int.zero) queuedMove = ((Vector2)axes).normalized; // combine rolling presses into one diagonal
                    if (inputGraceTimer <= 0f) { hasQueuedMove = true; moveQueuedAt = Time.time; }
                }
                return;
            }

            if (!pressed || paused) return;
            queuedMove = ((Vector2)axes).normalized;
            inputGraceTimer = 0.08f; // 80ms grace window to combine rolling inputs
        }

        static int Axis(float value) => value > 0.5f ? 1 : value < -0.5f ? -1 : 0;

        /// <summary>The back of the board is seen mirrored, so screen-right is board-left there.</summary>
        Vector2 ScreenToBoard(Vector2 v) => Board.View == PcbLayer.Back ? new Vector2(-v.x, v.y) : v;

        Vector3 LocalPosition(Vector2 p, PcbLayer layer)
        {
            var theme = Board.theme;
            float height = theme.traceHeight + theme.sparkSize * 0.5f;
            return new Vector3(p.x, p.y, BoardVisuals.Surface(layer, theme.boardThickness) + BoardVisuals.Out(layer) * height);
        }

        bool TryMove(Vector2 dir)
        {
            if (!Board.TryPickExit(CurrentNode, Layer, dir, out var exit)) { Block(); return false; }
            foreach (var m in exit.trace.GetComponents<TraceMechanic>())
                if (!m.CanEnter(this, exit.reversed)) { Block(); return false; }

            foreach (var m in CurrentNode.GetComponents<NodeMechanic>()) m.OnSparkLeave(this);
            travelling = exit;
            exit.trace.GetPath(Board, exit.reversed, path);
            segment = 0;
            segmentProgress = 0f;
            HideArrows();
            if (HasCharacter) StartCoroutine(DepartThenMove());
            else
            {
                IsMoving = true;
                AudioManager.SetTravelling(true);
            }
            return true;
        }

        void TryFlip()
        {
            if (CurrentNode.IsSwitch)
            {
                var switchMech = CurrentNode.GetComponent<SwitchMechanic>();
                if (switchMech) switchMech.Toggle();
                SpawnBurst();
                return;
            }

            if (!CurrentNode.IsVia) { Block(); return; }
            if (HasCharacter) StartCoroutine(FlipSequence());
            else BeginFlip();
        }

        void BeginFlip()
        {
            turnFromZ = transform.localPosition.z;
            Layer = Layer.Other();
            turnToZ = LocalPosition(Vector2.zero, Layer).z;
            HideArrows();
            if (rig)
            {
                if (trail) trail.emitting = false;
                rig.TurnTo(Layer);
            }
            else
            {
                Board.SetView(Layer);
                wasTurning = true;
            }
            Flipped?.Invoke(Layer);
        }

        void Move(float distance)
        {
            while (distance > 0f && segment < path.Count - 1)
            {
                float left = Vector2.Distance(path[segment], path[segment + 1]) - segmentProgress;
                if (distance < left) { segmentProgress += distance; distance = 0f; }
                else { distance -= left; segment++; segmentProgress = 0f; }
            }

            if (segment >= path.Count - 1)
            {
                transform.localPosition = LocalPosition(path[path.Count - 1], Layer);
                Arrive();
            }
            else
            {
                Vector2 p = Vector2.MoveTowards(path[segment], path[segment + 1], segmentProgress);
                transform.localPosition = LocalPosition(p, Layer);
            }
        }

        void Arrive()
        {
            IsMoving = false;
            AudioManager.SetTravelling(false);
            CurrentNode = travelling.target;
            if (HasCharacter)
            {
                IsBusy = true; // until MoveFinish has played
                StopVfx(travelVfx);
                nextBurst = float.MaxValue;
            }
            foreach (var m in travelling.trace.GetComponents<TraceMechanic>()) m.OnTraversed(this, travelling.reversed);
            foreach (var m in CurrentNode.GetComponents<NodeMechanic>()) m.OnSparkArrive(this);
            // A locked goal (data still to collect) is just a node: no win, a shake + burst to say "not yet".
            bool goal = CurrentNode.type == NodeType.Goal;
            if (goal && Board.GoalLocked) { goal = false; Block(); }
            RefreshArrows();
            Arrived?.Invoke(CurrentNode);

            if (HasCharacter) StartCoroutine(ArriveSequence(goal));
            else if (goal)
            {
                AudioManager.Play(Sfx.Win);
                WinFinished?.Invoke();
            }
        }

        void Block()
        {
            blockedFlash = 1f;
            AudioManager.Play(Sfx.Fail);
            if (HasCharacter) SpawnBurst();
            Blocked?.Invoke();
        }

        // ---------------------------------------------------------------- character sequences
        // Never disable this component while one is running (use InputLocked): that would cut the sequence short.

        IEnumerator AppearSequence()
        {
            IsBusy = true;
            ShowModel(false);
            while (!appearRequested) yield return null; // the LevelManager says when (after the fade-in / intro)
            ShowModel(true);
            yield return PlayAndWait(moveFinishState);
            EnterIdle();
        }

        IEnumerator DepartThenMove()
        {
            IsBusy = true;
            StopVfx(idleVfx);
            yield return PlayAndWait(moveStartState);
            ShowModel(false);
            PlayVfx(travelVfx);
            ScheduleBurst();
            IsMoving = true;
            AudioManager.SetTravelling(true);
            IsBusy = false;
        }

        IEnumerator ArriveSequence(bool goal)
        {
            ShowModel(true);
            yield return PlayAndWait(moveFinishState);
            if (goal)
            {
                AudioManager.Play(Sfx.Win); // with the Win animation
                yield return PlayAndWait(winState); // plays once and holds its last frame; the level is over
                WinFinished?.Invoke();
                yield break;
            }
            EnterIdle();
        }

        IEnumerator FlipSequence()
        {
            IsBusy = true;
            HideArrows();
            StopVfx(idleVfx);
            yield return PlayAndWait(moveStartState);
            ShowModel(false);
            BeginFlip();
            while (IsTurning || wasTurning) yield return null;
            FaceSide();
            ShowModel(true);
            yield return PlayAndWait(moveFinishState);
            EnterIdle();
        }

        void EnterIdle()
        {
            if (animator && HasState(idleState)) animator.Play(idleState, 0, 0f);
            PlayVfx(idleVfx);
            IsBusy = false;
        }

        /// <summary>Plays an Animator state from the start and waits until it has played once.</summary>
        IEnumerator PlayAndWait(string state)
        {
            if (!animator || !HasState(state)) yield break;
            animator.Play(state, 0, 0f);
            yield return null; // the Animator switches state on its next update
            yield return new WaitForSeconds(Mathf.Max(0f, animator.GetCurrentAnimatorStateInfo(0).length - Time.deltaTime));
        }

        bool HasState(string state)
        {
            if (string.IsNullOrEmpty(state)) return false;
            if (animator.HasState(0, Animator.StringToHash(state))) return true;
            Debug.LogWarning($"[PCB] Spark animator has no state named '{state}' (layer 0).", this);
            return false;
        }

        /// <summary>The prefab's pose is the front side; on the back, mirror it so the character still faces the camera.</summary>
        void FaceSide()
        {
            bool back = Layer == PcbLayer.Back;
            modelBase = back ? new Vector3(-modelPosition.x, modelPosition.y, -modelPosition.z) : modelPosition;
            model.localPosition = modelBase;
            model.localRotation = back ? Quaternion.Euler(0f, 180f, 0f) * modelRotation : modelRotation;
        }

        void ShowModel(bool show)
        {
            foreach (var r in modelRenderers) if (r) r.enabled = show;
        }

        static void PlayVfx(GameObject vfx)
        {
            if (!vfx) return;
            vfx.SetActive(true);
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(false);
            foreach (var tr in vfx.GetComponentsInChildren<TrailRenderer>(true)) tr.emitting = true;
        }

        /// <summary>Stops emitting but lets live particles fade out.</summary>
        static void StopVfx(GameObject vfx)
        {
            if (!vfx) return;
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            foreach (var tr in vfx.GetComponentsInChildren<TrailRenderer>(true)) tr.emitting = false;
        }

        void SpawnBurst() => SpawnBurstAt(transform.position);

        /// <summary>Plays the burst VFX at a world position (character only; e.g. collecting data, unlocking the goal).</summary>
        public void SpawnBurstAt(Vector3 position)
        {
            if (!burstVfx || !Board) return;
            // Parented to the board so it follows the tilt/turn, and goes away with it on restart.
            var fx = Instantiate(burstVfx, position, transform.rotation, Board.transform);
            float lifetime = 0.5f;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
                lifetime = Mathf.Max(lifetime, ps.main.duration + ps.main.startLifetime.constantMax);
            Destroy(fx, lifetime);
        }

        void ScheduleBurst() =>
            nextBurst = travelBurstInterval.y > 0f
                ? Time.time + UnityEngine.Random.Range(Mathf.Max(0.05f, travelBurstInterval.x), travelBurstInterval.y)
                : float.MaxValue;

        // ---------------------------------------------------------------- visuals

        void BuildVisuals()
        {
            if (built) return;
            built = true;
            var theme = Board.theme;
            block = new MaterialPropertyBlock();

            if (!HasCharacter)
            {
                coreRenderer = BoardVisuals.Part(transform, BoardVisuals.Sphere, Vector3.zero, Quaternion.identity,
                    Vector3.one * theme.sparkSize, theme.sparkMaterial, null);
                coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                core = coreRenderer.transform;
                core.name = "Core";

                trail = gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = theme.spriteMaterial;
                trail.time = 0.2f;
                trail.minVertexDistance = 0.03f;
                trail.numCapVertices = 2;
                trail.widthMultiplier = theme.sparkSize * 0.6f;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(theme.spark, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = gradient;
            }

            if (!HasCharacter || glowLight)
            {
                glow = new GameObject("Glow").AddComponent<Light>();
                glow.transform.SetParent(transform, false);
                glow.type = LightType.Point;
                glow.range = theme.sparkLightRange;
                glow.intensity = theme.sparkLightIntensity;
                glow.color = theme.spark;
                glow.shadows = LightShadows.None;
            }
        }

        void RefreshArrows()
        {
            if (HasCharacter && !directionArrows) return;
            var theme = Board.theme;
            int count = 0;
            float radius = theme.capacitorSize * 0.5f + 0.18f;
            // Arrows lie on the board surface; the spark floats a little above it.
            float dz = BoardVisuals.Out(Layer) * (theme.traceHeight + 0.006f - (theme.traceHeight + theme.sparkSize * 0.5f));
            foreach (var e in Board.GetExits(CurrentNode))
            {
                if (e.layer != Layer) continue;
                if (count == arrows.Count)
                {
                    var go = new GameObject("Arrow");
                    go.transform.SetParent(transform, false);
                    go.transform.localScale = Vector3.one * 0.16f;
                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = theme.triangle;
                    sr.sharedMaterial = theme.spriteMaterial;
                    arrows.Add(sr);
                }
                var arrow = arrows[count++];
                arrow.transform.localPosition = new Vector3(e.direction.x * radius, e.direction.y * radius, dz);
                arrow.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(e.direction.y, e.direction.x) * Mathf.Rad2Deg);
            }
            for (int i = 0; i < arrows.Count; i++) arrows[i].enabled = i < count;
        }

        void HideArrows()
        {
            foreach (var a in arrows) a.enabled = false;
        }

        void AnimateVisuals()
        {
            var theme = Board.theme;
            blockedFlash = Mathf.MoveTowards(blockedFlash, 0f, Time.deltaTime * 4f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
            Vector3 shake = UnityEngine.Random.insideUnitSphere * (0.05f * blockedFlash);

            // The character shakes when blocked but doesn't turn red (a burst VFX plays instead).
            Color c = HasCharacter ? theme.spark : Color.Lerp(theme.spark, theme.sparkBlocked, blockedFlash);
            if (HasCharacter) model.localPosition = modelBase + shake;
            else
            {
                coreRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                block.SetColor(EmissionColorId, c * (theme.sparkGlow * (0.8f + 0.4f * pulse)));
                coreRenderer.SetPropertyBlock(block);
                core.localPosition = shake;
                core.localScale = Vector3.one * theme.sparkSize * (1f + 0.08f * pulse);
            }

            if (glow)
            {
                glow.color = c;
                glow.intensity = theme.sparkLightIntensity * (0.85f + 0.3f * pulse);
            }

            var arrowColor = theme.spark;
            arrowColor.a = 0.5f + 0.5f * pulse;
            foreach (var a in arrows) a.color = arrowColor;
        }
    }
}
