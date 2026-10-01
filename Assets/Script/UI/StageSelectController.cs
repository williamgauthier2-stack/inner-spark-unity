using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Pcb
{
    /// <summary>
    /// Lives in the MainMenu scene. Builds one button per level from the LevelList, in play order,
    /// so this never needs manual upkeep as levels are added, removed or reordered.
    /// </summary>
    public class StageSelectController : MonoBehaviour
    {
        [Tooltip("Same Level List the game plays from.")]
        public LevelList levels;
        [Tooltip("Scene that contains the LevelManager / gameplay.")]
        public string gameplayScene = "SampleScene";

        [Header("Wiring")]
        public GameObject panel;
        public GameObject mainMenuPanel;
        [Tooltip("Parent the generated stage buttons are placed under.")]
        public Transform buttonContainer;
        [Tooltip("Inactive button under buttonContainer, cloned once per level.")]
        public Button buttonTemplate;

        void OnEnable() => Populate();

        void Populate()
        {
            if (!buttonContainer || !buttonTemplate || !levels) return;

            for (int i = buttonContainer.childCount - 1; i >= 0; i--)
            {
                var child = buttonContainer.GetChild(i);
                if (child != buttonTemplate.transform) Destroy(child.gameObject);
            }
            buttonTemplate.gameObject.SetActive(false);

            // Only unlocked stages are listed (finishing a stage unlocks the next - see Progress).
            for (int i = 0; i < levels.Count && Progress.IsUnlocked(i); i++)
            {
                var level = levels[i];
                var button = Instantiate(buttonTemplate, buttonContainer);
                button.gameObject.SetActive(true);

                var label = button.GetComponentInChildren<TMP_Text>();
                if (label) label.text = level ? level.levelName : $"Level {i + 1}";

                int index = i; // capture for the closure
                button.onClick.AddListener(() => Play(index));
                AudioManager.HookButton(button); // click sound (created after the scene loaded)
            }
        }

        void Play(int index)
        {
            AudioManager.Play(Sfx.StartGame);
            GameFlow.RequestLevel(index);
            ScreenFader.LoadScene(gameplayScene);
        }

        public void Back()
        {
            if (panel) panel.SetActive(false);
            if (mainMenuPanel) mainMenuPanel.SetActive(true);
        }
    }
}
