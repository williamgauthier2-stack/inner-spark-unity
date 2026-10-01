using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// Shown when the current level is won. Lives in the gameplay scene's Canvas, panel starts
    /// disabled there in the scene - not force-hidden in code. If this script sits on the panel
    /// itself (the natural place for it), force-hiding in Awake would fight the very Show() call
    /// that first reactivates it (see the DialogController bug this avoids repeating).
    /// </summary>
    public class WinPanel : MonoBehaviour
    {
        public GameObject panel;
        [Tooltip("Optional. Played each time the panel is shown (a burst/confetti effect, for example).")]
        public ParticleSystem celebrationEffect;

        public void Show()
        {
            if (panel) panel.SetActive(true);
            if (celebrationEffect) celebrationEffect.Play();
        }

        public void Hide()
        {
            if (panel) panel.SetActive(false);
        }
    }
}
