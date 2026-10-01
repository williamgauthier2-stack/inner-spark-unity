using System.Collections.Generic;
using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// A gate on a trace: blocks the spark (both directions) while closed. A normal gate flips open/closed
    /// each time ANY of its switches is pressed. Link switches with Level Editor > Link tool. (Wiring a
    /// switch's onToggle to SetOpen by hand, as in Level 04, still works.)
    /// Its model is built by the Board (theme Gate Prefab / level Look / this gate's Model), standing across
    /// the middle of the trace, and shows its CLOSED or OPEN look (LockVisual).
    /// </summary>
    public class GateMechanic : TraceMechanic
    {
        [Tooltip("Normal gate: open at the start? Each press of any of its switches flips it.")]
        public bool isOpen = false;
        [Tooltip("The switches controlling this gate: normal switches for a normal gate, AND switches for an AND gate.")]
        public List<SwitchMechanic> switches = new List<SwitchMechanic>();
        [Tooltip("Optional: this gate's own model (Level Editor > Paint > Gates). Empty = the level's default (Board > Look), then the theme's.")]
        public GameObject model;

        /// <summary>Open/closed as the model should show it (an AND gate works out its starting state in the editor).</summary>
        public virtual bool ShownOpen => isOpen;

        protected virtual void OnEnable()
        {
            foreach (var s in switches) if (s) s.onToggle.AddListener(OnSwitchToggled);
        }

        protected virtual void OnDisable()
        {
            foreach (var s in switches) if (s) s.onToggle.RemoveListener(OnSwitchToggled);
        }

        /// <summary>Normal gate: any press of any of its switches flips it.</summary>
        protected virtual void OnSwitchToggled(bool _) => SetOpen(!isOpen);

        /// <summary>Opens/closes the gate; plays the gate open/close sound when that's a change.</summary>
        public void SetOpen(bool open) => SetOpen(open, playSound: true);

        /// <summary>playSound false: setting the starting state when the level loads.</summary>
        protected void SetOpen(bool open, bool playSound)
        {
            bool changed = open != isOpen;
            isOpen = open;
            Debug.Log($"Gate {gameObject.name} set to {(isOpen ? "Open" : "Closed")}");
            if (changed && playSound) AudioManager.Play(open ? Sfx.GateOpen : Sfx.GateClose);
            UpdateVisual();
        }

        public override bool CanEnter(Spark spark, bool reversed)
        {
            if (!isOpen) Debug.Log($"Spark blocked by closed gate on {gameObject.name}");
            return isOpen;
        }

        /// <summary>Swaps the generated model to its OPEN / CLOSED look.</summary>
        void UpdateVisual()
        {
            var board = GetComponentInParent<Board>();
            if (!board) return; // before the board built its look, it reads isOpen itself
            foreach (var look in board.VisualsOf(this)) BoardVisuals.ShowGateState(look, isOpen);
        }
    }
}
