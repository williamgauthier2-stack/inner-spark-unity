using System;
using UnityEngine;

namespace Pcb
{
    /// <summary>One sound: a clip plus its balance volume (set in the editor; the player's sliders scale on top).</summary>
    [Serializable]
    public struct Sound
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
    }

    public enum Sfx { ButtonClick, StartGame, Win, Switch, Pickup, GoalUnlock, GateOpen, GateClose, Fail }

    /// <summary>
    /// Every sound in the game (Create > PCB > Sound Bank), assigned to the Audio Manager.
    /// An empty clip is simply silent, so slots can be filled in later.
    /// </summary>
    [CreateAssetMenu(menuName = "PCB/Sound Bank", fileName = "SoundBank")]
    public class SoundBank : ScriptableObject
    {
        [Header("Music (loops)")]
        public Sound menuMusic = Default;
        [Tooltip("Restarts each time a stage is entered; keeps playing through a level restart.")]
        public Sound gameplayMusic = Default;
        [Tooltip("The Ending screen after the last stage.")]
        public Sound endingMusic = Default;
        [Tooltip("Music volume multiplier while the game is paused.")]
        [Range(0f, 1f)] public float pausedMusicVolume = 0.4f;

        [Header("UI")]
        public Sound buttonClick = Default;
        [Tooltip("Main Menu / Stage Select into gameplay (not when going to the next level).")]
        public Sound startGame = Default;

        [Header("Gameplay")]
        [Tooltip("Played as the spark starts its Win animation.")]
        public Sound win = Default;
        [Tooltip("Pressing a switch (normal or AND).")]
        public Sound switchToggle = Default;
        [Tooltip("Collecting a data pickup.")]
        public Sound pickup = Default;
        [Tooltip("The last data collected: the goal unlocks.")]
        public Sound goalUnlock = Default;
        public Sound gateOpen = Default;
        public Sound gateClose = Default;
        [Tooltip("Blocked move, flipping off a via, reaching a locked goal.")]
        public Sound fail = Default;

        [Header("Loops")]
        [Tooltip("Loops while the spark travels along a trace.")]
        public Sound travel = Default;
        [Tooltip("Loops while a dialog line is being typed out.")]
        public Sound dialogTalking = Default;

        static Sound Default => new Sound { volume = 1f };

        public Sound GetMusic(Music track) => track switch
        {
            Music.Menu => menuMusic,
            Music.Gameplay => gameplayMusic,
            Music.Ending => endingMusic,
            _ => default
        };

        public Sound Get(Sfx sfx) => sfx switch
        {
            Sfx.ButtonClick => buttonClick,
            Sfx.StartGame => startGame,
            Sfx.Win => win,
            Sfx.Switch => switchToggle,
            Sfx.Pickup => pickup,
            Sfx.GoalUnlock => goalUnlock,
            Sfx.GateOpen => gateOpen,
            Sfx.GateClose => gateClose,
            _ => fail
        };
    }
}
