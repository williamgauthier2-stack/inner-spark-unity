using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pcb
{
    /// <summary>Lives in the MainMenu scene. Wires the Play / Stage Select / Quit buttons.</summary>
    public class MainMenuController : MonoBehaviour
    {
        [Tooltip("Scene that contains the LevelManager / gameplay.")]
        public string gameplayScene = "SampleScene";
        [Header("Wiring")]
        public GameObject mainPanel;
        public GameObject stageSelectPanel;

        void Start() => AudioManager.PlayMusic(Music.Menu);

        /// <summary>Continues from the furthest unlocked stage (the LevelManager keeps it within the Level List).</summary>
        public void Play()
        {
            AudioManager.Play(Sfx.StartGame);
            GameFlow.RequestLevel(Progress.Unlocked);
            ScreenFader.LoadScene(gameplayScene);
        }

        public void OpenStageSelect()
        {
            if (mainPanel) mainPanel.SetActive(false);
            if (stageSelectPanel) stageSelectPanel.SetActive(true);
        }

        public void Quit()
        {
            Application.Quit(); // no effect in the editor or WebGL
        }
    }
}
