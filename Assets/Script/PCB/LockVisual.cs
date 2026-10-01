using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// Optional, on the root of a goal model prefab with its Locked and Unlocked looks as two children.
    /// Without it, a locked goal is simply tinted gray (theme Locked Goal Tint).
    /// </summary>
    public class LockVisual : MonoBehaviour
    {
        [Tooltip("Child shown while the goal is locked (data still to collect).")]
        public GameObject locked;
        [Tooltip("Child shown once the goal is unlocked.")]
        public GameObject unlocked;

        public void Show(bool isLocked)
        {
            if (locked) locked.SetActive(isLocked);
            if (unlocked) unlocked.SetActive(!isLocked);
        }
    }
}
