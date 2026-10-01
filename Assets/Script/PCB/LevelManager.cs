using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pcb
{
    /// <summary>
    /// One per scene. Plays the levels from the LevelList: spawns the board and the spark,
    /// detects the Goal, and handles restart and the win pop-up.
    /// If a Board is already in the scene (the one you are editing), play starts on it.
    /// Entering a stage: black → fade in → music → [intro cinematic] → spark appears → [dialog] → play.
    /// Restart: quick fade → spark appears → play (no cinematic, no dialog; the music keeps going).
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public LevelList levels;
        [Tooltip("Level to start on when the scene has no Board in it.")]
        public int startLevel;
        [Tooltip("Optional. If empty, a default spark is created.")]
        public Spark sparkPrefab;
        [Tooltip("Optional. Esc opens Resume / Quit to Menu / Quit App through this instead of quitting straight to desktop.")]
        public PauseMenu pauseMenu;
        [Tooltip("Optional. Shown before play if the current level has a DialogSequence assigned.")]
        public DialogController dialogController;
        [Tooltip("Optional. Shown when the current level is won (not after the last one: that goes to the ending).")]
        public WinPanel winPanel;
        [Tooltip("Scene loaded (with a fade) after the last stage in the Level List is won.")]
        public string endingScene = "Ending";

        [Header("Staging")]
        [Tooltip("Optional. Every stage's board is centred on this point (a fixed spot in the room).")]
        public Transform boardAnchor;
        [Tooltip("Camera far clip at least this far (0 = just fit the board). Raise it if the room gets cut off.")]
        public float cameraFarClip = 0f;
        [Tooltip("Seconds to fade to / from black when entering a stage.")]
        [Min(0f)] public float fadeTime = ScreenFader.DefaultDuration;
        [Tooltip("Seconds to fade to / from black on a restart.")]
        [Min(0f)] public float restartFadeTime = 0.25f;

        GameObject template; // what Restart re-creates: a level prefab, or the disabled scene board
        int index = -1;
        Board current;
        BoardRig rig;
        Spark spark;
        bool won;
        float wonAt;
        bool transitioning; // stage start / restart sequence running: no restart, next or tilting
        InputAction restartAction, confirmAction;

        public Board CurrentBoard => current;

        void Awake()
        {
            restartAction = Button("<Keyboard>/r", "<Gamepad>/select");
            confirmAction = Button("<Keyboard>/space", "<Keyboard>/enter", "<Gamepad>/buttonSouth");
        }

        static InputAction Button(params string[] bindings)
        {
            var action = new InputAction(type: InputActionType.Button);
            foreach (var b in bindings) action.AddBinding(b);
            return action;
        }

        void OnEnable() { restartAction.Enable(); confirmAction.Enable(); }
        void OnDisable() { restartAction.Disable(); confirmAction.Disable(); }
        void OnDestroy() { restartAction.Dispose(); confirmAction.Dispose(); }

        void Start()
        {
            ScreenFader.Claim();    // this scene does its own fade-in, at the right point of the sequence
            ScreenFader.SetBlack();
            var allSceneBoards = FindObjectsByType<Board>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var b in allSceneBoards) b.gameObject.SetActive(false);
            
            var sceneBoard = allSceneBoards.Length > 0 ? allSceneBoards[0] : null;
            bool levelsAvailable = levels && levels.Count > 0;

            // Arrived via Main Menu / Stage Select: that choice always wins, even if a Board happens
            // to be sitting in this scene for editing - otherwise testing through the menu in the
            // Editor would silently ignore Stage Select and just play whatever's in the scene.
            if (levelsAvailable && GameFlow.HasPendingRequest)
            {
                GoTo(GameFlow.TakeRequestedLevel(startLevel));
            }
            else if (sceneBoard && (Application.isEditor || !levelsAvailable))
            {
                // Editor: play the board being edited, keeping an untouched copy for restarts.
                template = sceneBoard.gameObject;
                index = levels ? levels.IndexOf(sceneBoard.levelName) : -1;
                StartCoroutine(EnterStage());
            }
            else if (levelsAvailable)
            {
                GoTo(GameFlow.TakeRequestedLevel(startLevel));
            }
            else Debug.LogError("[PCB] No Board in the scene and no levels in the Level List.", this);
        }

        /// <summary>Enters a stage (fade, intro cinematic, spark, dialog - see the class summary).</summary>
        public void GoTo(int levelIndex)
        {
            if (!levels || levels.Count == 0 || transitioning) return;
            index = Mathf.Clamp(levelIndex, 0, levels.Count - 1);
            template = levels[index] ? levels[index].gameObject : null;
            StartCoroutine(EnterStage());
        }

        public void Next() => GoTo(index + 1); // advances after a win; see OnArrived/Update

        /// <summary>Replays the level: quick fade, no cinematic or dialog (already seen), the music keeps going.</summary>
        public void Restart()
        {
            if (!transitioning) StartCoroutine(RestartStage());
        }

        IEnumerator EnterStage()
        {
            transitioning = true;
            if (pauseMenu) pauseMenu.SetAvailable(false); // no pause / restart during the start sequence + dialog
            yield return ScreenFader.FadeOut(fadeTime);
            if (!Spawn()) { transitioning = false; if (pauseMenu) pauseMenu.SetAvailable(true); yield return ScreenFader.FadeIn(fadeTime); yield break; }

            // The intro's device is already in place (around the board) while the screen is still black.
            StageIntro intro = null;
            if (current.intro)
            {
                intro = Instantiate(current.intro, rig.transform.position, rig.transform.rotation);
                intro.Begin(Camera.main);
            }
            yield return ScreenFader.FadeIn(fadeTime);
            AudioManager.PlayMusic(Music.Gameplay, restart: true);
            if (intro) yield return intro.Play(rig);

            yield return AppearSpark();
            if (current.dialogSequence && dialogController)
            {
                bool done = false;
                dialogController.Show(current.dialogSequence, () => done = true);
                while (!done) yield return null;
            }
            BeginPlay();
        }

        IEnumerator RestartStage()
        {
            transitioning = true;
            if (pauseMenu) pauseMenu.SetAvailable(false);
            yield return ScreenFader.FadeOut(restartFadeTime);
            if (!Spawn()) { transitioning = false; if (pauseMenu) pauseMenu.SetAvailable(true); yield return ScreenFader.FadeIn(restartFadeTime); yield break; }
            yield return ScreenFader.FadeIn(restartFadeTime);
            yield return AppearSpark();
            BeginPlay();
        }

        IEnumerator AppearSpark()
        {
            spark.Appear();
            while (spark && spark.IsBusy) yield return null; // the character's appear animation
        }

        void BeginPlay()
        {
            if (spark) spark.InputLocked = false;
            if (rig) rig.InspectLocked = false;
            if (pauseMenu) pauseMenu.SetAvailable(true);
            transitioning = false;
        }

        /// <summary>Builds the level's board, rig and spark (hidden and locked until the sequence lets it go).</summary>
        bool Spawn()
        {
            if (!template) { Debug.LogError($"[PCB] Level {index + 1} is missing from the Level List.", this); return false; }
            if (spark)
            {
                spark.Arrived -= OnArrived;
                spark.WinFinished -= OnWinFinished;
            }
            if (rig) Destroy(rig.gameObject); // takes the board and the spark with it
            won = false;
            if (winPanel) winPanel.Hide();

            var go = Instantiate(template);
            go.name = template.name;
            go.SetActive(true); // Board.Awake builds the graph and the 3D look
            current = go.GetComponent<Board>();
            rig = BoardRig.Create(current, Camera.main, boardAnchor, cameraFarClip);
            rig.InspectLocked = true;

            PcbNode start = null;
            foreach (var n in current.Nodes)
                if (n.type == NodeType.Start) { start = n; break; }
            if (!start)
            {
                Debug.LogError($"[PCB] Level '{current.levelName}' has no Start node.", this);
                return false;
            }

            spark = sparkPrefab ? Instantiate(sparkPrefab) : new GameObject("Spark").AddComponent<Spark>();
            spark.InputLocked = true;
            spark.Init(current, start, rig);
            spark.Arrived += OnArrived;
            spark.WinFinished += OnWinFinished;
            return true;
        }

        void OnArrived(PcbNode node)
        {
            if (node.type != NodeType.Goal || current.GoalLocked) return; // locked goal: data still to collect
            spark.InputLocked = true; // no more input; the win screen waits for the spark's win animation (OnWinFinished)
        }

        void OnWinFinished()
        {
            Progress.Completed(index); // unlocks the next stage (saved)
            if (levels && index >= 0 && index == levels.Count - 1)
            {
                // The last stage: straight to the ending, no win pop-up.
                transitioning = true; // no restart meanwhile
                if (pauseMenu) pauseMenu.SetAvailable(false);
                ScreenFader.LoadScene(endingScene);
                return;
            }
            won = true;
            wonAt = Time.time;
            if (winPanel) winPanel.Show(); // input is already locked since the goal (OnArrived)
        }

        void Update()
        {
            if (pauseMenu && pauseMenu.IsPaused) return;
            if (dialogController && dialogController.IsShowing) return;
            if (transitioning) return;

            if (restartAction.WasPressedThisFrame()) Restart();
            else if (won && Time.time - wonAt > 0.4f && confirmAction.WasPressedThisFrame()) Next();
        }
        // No on-screen text during play: controls are taught in each stage's intro dialog.
    }
}
