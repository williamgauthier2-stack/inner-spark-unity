using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// A gate that only opens while every one of its switches (AND switches) is on - an AND gate.
    /// The switches are the inherited 'switches' list (Level Editor > Link tool), no UnityEvent wiring needed.
    /// For a gate that simply flips on every press, use GateMechanic with normal switches.
    /// </summary>
    public class AndGateMechanic : GateMechanic
    {
        [Tooltip("Open until every switch is on, then close (instead of opening once they're all on).")]
        public bool inverted;

        protected override void OnEnable()
        {
            base.OnEnable();
            Recompute(playSound: false); // starting state, not a change the player caused
        }

        protected override void OnSwitchToggled(bool _) => Recompute(playSound: true);

        void Recompute(bool playSound) => SetOpen(ComputeOpen(), playSound);

        bool ComputeOpen()
        {
            bool allOn = switches.Count > 0;
            foreach (var s in switches)
                if (!s || !s.isOn) { allOn = false; break; }
            return inverted ? !allOn : allOn;
        }

        // In the editor the gate hasn't run yet: show the state it will start in.
        public override bool ShownOpen => Application.isPlaying ? isOpen : ComputeOpen();
    }
}
