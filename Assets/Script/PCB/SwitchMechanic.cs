using UnityEngine;
using UnityEngine.Events;

namespace Pcb
{
    /// <summary>
    /// On a Switch / AndSwitch node. The spark's flip input (Space) toggles it. Gates that list this
    /// switch react on their own (Level Editor > Link tool); onToggle is for any extra wiring.
    /// </summary>
    public class SwitchMechanic : NodeMechanic
    {
        [Tooltip("Starting state. Normal switch: which way the lever points. AND switch: pressed down = on.")]
        public bool isOn = false;
        public UnityEvent<bool> onToggle;

        public void Toggle()
        {
            isOn = !isOn;
            Debug.Log($"Switch {gameObject.name} toggled. New state: (isOn: {isOn})");
            AudioManager.Play(Sfx.Switch);
            ShowState();
            onToggle?.Invoke(isOn);
        }

        /// <summary>Shows the ON or OFF model on this switch's generated look (see SwitchVisual).</summary>
        public void ShowState()
        {
            var node = GetComponent<PcbNode>();
            var board = GetComponentInParent<Board>();
            if (!node || !board) return;
            foreach (var owner in board.GetComponentsInChildren<PcbVisualOwner>(true))
                if (owner.owner == node)
                    foreach (var look in owner.GetComponentsInChildren<SwitchVisual>(true)) look.Show(isOn);
        }
    }
}
