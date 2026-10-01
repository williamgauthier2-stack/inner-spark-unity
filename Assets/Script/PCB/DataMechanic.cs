using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// A data pickup on a Capacitor node (Level Editor > Data tool). The spark collects it by stopping on the
    /// node. While any data on the board is uncollected the Goal is locked: grayed out, and reaching it doesn't win.
    /// </summary>
    public class DataMechanic : NodeMechanic
    {
        /// <summary>Runtime only: every level (re)start begins with all data uncollected.</summary>
        public bool Collected { get; private set; }

        public override void OnSparkArrive(Spark spark)
        {
            if (Collected) return;
            Collected = true;

            var board = GetComponentInParent<Board>();
            if (!board) return;
            foreach (var look in board.VisualsOf(GetComponent<PcbNode>()))
                foreach (var data in look.GetComponentsInChildren<HoverVisual>()) data.Dismiss(); // shrinks away
            spark.SpawnBurstAt(spark.transform.position);
            AudioManager.Play(Sfx.Pickup);

            if (!board.GoalLocked) // that was the last one
            {
                AudioManager.Play(Sfx.GoalUnlock);
                foreach (var goal in board.UnlockGoals()) spark.SpawnBurstAt(goal.position);
            }
        }
    }
}
