using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// Saved progress: the furthest stage unlocked (play-order index in the LevelList, stage 1 = 0).
    /// Finishing a stage unlocks the next; kept between sessions (PlayerPrefs). Stage Select only shows
    /// unlocked stages, Main Menu > Play continues from the furthest one. Reset: Tools > PCB > Reset Progress.
    /// </summary>
    public static class Progress
    {
        const string Key = "Progress.Unlocked";

        /// <summary>Highest unlocked stage index (0 = only stage 1). Can be one past the last stage once the game is finished.</summary>
        public static int Unlocked => Mathf.Max(0, PlayerPrefs.GetInt(Key, 0));

        public static bool IsUnlocked(int stageIndex) => stageIndex <= Unlocked;

        /// <summary>Stage 'stageIndex' was finished: the next one is unlocked.</summary>
        public static void Completed(int stageIndex)
        {
            if (stageIndex < 0 || stageIndex + 1 <= Unlocked) return;
            PlayerPrefs.SetInt(Key, stageIndex + 1);
            PlayerPrefs.Save();
        }

        /// <summary>The stage Main Menu > Play continues from (the last stage once everything is finished).</summary>
        public static int ContinueIndex(int stageCount) => Mathf.Clamp(Unlocked, 0, Mathf.Max(0, stageCount - 1));

        public static void Reset()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        /// <summary>Testing: every stage unlocked.</summary>
        public static void UnlockAll(int stageCount)
        {
            PlayerPrefs.SetInt(Key, Mathf.Max(0, stageCount - 1));
            PlayerPrefs.Save();
        }
    }
}
