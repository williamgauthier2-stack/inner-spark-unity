using UnityEngine;
using UnityEngine.InputSystem;

namespace Pcb
{
    /// <summary>
    /// Lives in the Ending scene (shown after the last stage). Plays the ending music; after a short wait,
    /// any click / key / gamepad button fades back to the Main Menu. The artwork and text are set up in the scene.
    /// </summary>
    public class EndingScreen : MonoBehaviour
    {
        [Tooltip("Scene to return to.")]
        public string mainMenuScene = "MainMenu";
        [Tooltip("Seconds before a click counts, so a stray click doesn't skip the ending.")]
        [Min(0f)] public float minimumTime = 2f;
        [Tooltip("Optional. Shown once a click counts (e.g. a 'click to continue' text). Start it disabled.")]
        public GameObject continueHint;

        float shownAt;
        bool leaving;

        void Start()
        {
            shownAt = Time.unscaledTime;
            if (continueHint) continueHint.SetActive(false);
            AudioManager.PlayMusic(Music.Ending, restart: true);
        }

        void Update()
        {
            if (leaving || Time.unscaledTime - shownAt < minimumTime) return;
            if (continueHint && !continueHint.activeSelf) continueHint.SetActive(true);
            if (!AnyPress()) return;
            leaving = true;
            AudioManager.Play(Sfx.ButtonClick);
            ScreenFader.LoadScene(mainMenuScene);
        }

        static bool AnyPress()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            return (mouse != null && mouse.leftButton.wasPressedThisFrame)
                || (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                || (gamepad != null && (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame));
        }
    }
}
