using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// Put on the root of a switch model prefab, with its ON and OFF looks as two children.
    /// The switch shows one or the other: its starting state when the board is built, then after every press.
    /// </summary>
    public class SwitchVisual : MonoBehaviour
    {
        [Tooltip("Child shown while the switch is on (normal switch: one lever side; AND switch: pressed down).")]
        public GameObject on;
        [Tooltip("Child shown while the switch is off.")]
        public GameObject off;

        public void Show(bool isOn)
        {
            if (on) on.SetActive(isOn);
            if (off) off.SetActive(!isOn);
        }
    }
}
