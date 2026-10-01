using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Pcb
{
    /// <summary>Esc toggles a pause overlay with Resume / Quit to Menu / Quit App. Lives in the gameplay scene.</summary>
    public class PauseMenu : MonoBehaviour
    {
        [Tooltip("Scene loaded by 'Quit to Menu'.")]
        public string mainMenuScene = "MainMenu";
        public GameObject panel;
        [Tooltip("Optional. Grayed out while paused (so it can't be clicked from underneath the pause panel) and during the stage start / dialog.")]
        public Button restartButton;
        [Tooltip("Optional. The on-screen Pause button: grayed out during the stage start / dialog, when pausing isn't allowed.")]
        public Button pauseButton;

        InputAction pauseAction;
        bool available = true;

        public bool IsPaused => panel && panel.activeSelf;
        /// <summary>For gameplay scripts without a PauseMenu reference (Spark, BoardRig): ignore input while true.</summary>
        public static bool GamePaused { get; private set; }

        void Awake()
        {
            pauseAction = new InputAction("Pause", InputActionType.Button);
            pauseAction.AddBinding("<Keyboard>/escape");
            if (panel) panel.SetActive(false);
        }

        void OnEnable() => pauseAction.Enable();
        void OnDisable() => pauseAction.Disable();
        void OnDestroy()
        {
            pauseAction.Dispose();
            GamePaused = false; // static: don't carry a stale pause into the next scene
        }

        void Update()
        {
            if (pauseAction.WasPressedThisFrame()) Toggle();
        }

        public void Toggle()
        {
            if (IsPaused) Resume(); else Pause();
        }

        /// <summary>
        /// False while pausing / restarting isn't allowed (LevelManager: stage start sequence, dialog):
        /// Esc does nothing and the Pause / Restart buttons are grayed out.
        /// </summary>
        public void SetAvailable(bool value)
        {
            available = value;
            if (pauseButton) pauseButton.interactable = value;
            if (restartButton && !IsPaused) restartButton.interactable = value;
        }

        public void Pause()
        {
            if (!available) return;
            if (panel) panel.SetActive(true);
            if (restartButton) restartButton.interactable = false;
            Time.timeScale = 0f;
            GamePaused = true;
            AudioManager.SetPaused(true);
        }

        public void Resume()
        {
            if (panel) panel.SetActive(false);
            if (restartButton) restartButton.interactable = available;
            Time.timeScale = 1f;
            GamePaused = false;
            AudioManager.SetPaused(false);
        }

        public void QuitToMenu()
        {
            Time.timeScale = 1f;
            GamePaused = false;
            AudioManager.SetPaused(false);
            ScreenFader.LoadScene(mainMenuScene);
        }

        public void QuitApp()
        {
            Application.Quit(); // no effect in the editor or WebGL
        }
    }
}
