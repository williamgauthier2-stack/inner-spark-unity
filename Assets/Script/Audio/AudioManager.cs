using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Pcb
{
    public enum Music { None, Menu, Gameplay, Ending }

    /// <summary>
    /// Plays all music and sound effects from a SoundBank. Put one in every scene (Main Menu and gameplay):
    /// the first one survives scene changes, so music doesn't cut out, and any later copy removes itself.
    /// Everything is static and silent when there's no Audio Manager, so game code can call it freely.
    /// Every UI Button in a loaded scene gets the click sound automatically.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public SoundBank bank;

        const string MusicKey = "Volume.Music", SfxKey = "Volume.Sfx";

        static AudioManager instance;
        AudioSource music, sfx, travel, talk;
        Music currentMusic;
        bool paused;
        readonly HashSet<Button> hooked = new HashSet<Button>();

        /// <summary>Player setting 0..1, saved between sessions (Music / SFX sliders).</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, 1f);
            set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); if (instance) instance.ApplyVolumes(); }
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(SfxKey, 1f);
            set { PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value)); if (instance) instance.ApplyVolumes(); }
        }

        void Awake()
        {
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            music = NewSource(loop: true);
            sfx = NewSource(loop: false);
            travel = NewSource(loop: true);
            talk = NewSource(loop: true);
            SceneManager.sceneLoaded += OnSceneLoaded;
            HookButtons();
        }

        void OnDestroy()
        {
            if (instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
        }

        AudioSource NewSource(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f; // 2D
            return source;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SetLoop(travel, default, false);
            SetLoop(talk, default, false);
            HookButtons();
        }

        // ---------------------------------------------------------------- API

        /// <summary>One-shot sound effect.</summary>
        public static void Play(Sfx id)
        {
            if (!instance || !instance.bank) return;
            var s = instance.bank.Get(id);
            if (s.clip) instance.sfx.PlayOneShot(s.clip, s.volume * SfxVolume);
        }

        /// <summary>Switches the music. restart = start the track over even if it's already playing.</summary>
        public static void PlayMusic(Music track, bool restart = false)
        {
            if (!instance || !instance.bank) return;
            var m = instance;
            if (track == m.currentMusic && !restart && m.music.isPlaying) return;
            m.currentMusic = track;
            var s = m.bank.GetMusic(track);
            m.music.Stop();
            m.music.clip = s.clip;
            m.ApplyVolumes();
            if (s.clip) m.music.Play();
        }

        /// <summary>Lowers the music and holds the travel / talking loops while the pause menu is open.</summary>
        public static void SetPaused(bool isPaused)
        {
            if (!instance) return;
            instance.paused = isPaused;
            instance.ApplyVolumes();
            if (isPaused) { instance.travel.Pause(); instance.talk.Pause(); }
            else { instance.travel.UnPause(); instance.talk.UnPause(); }
        }

        /// <summary>The travel loop, while the spark runs along a trace.</summary>
        public static void SetTravelling(bool on)
        {
            if (instance && instance.bank) instance.SetLoop(instance.travel, instance.bank.travel, on);
        }

        /// <summary>The dialog "talking" loop, while a line is being typed out.</summary>
        public static void SetTalking(bool on)
        {
            if (instance && instance.bank) instance.SetLoop(instance.talk, instance.bank.dialogTalking, on);
        }

        /// <summary>Adds the click sound to a button created at runtime (e.g. Stage Select's level buttons).</summary>
        public static void HookButton(Button button)
        {
            if (!instance || !button || !instance.hooked.Add(button)) return;
            button.onClick.AddListener(() => Play(Sfx.ButtonClick));
        }

        // ---------------------------------------------------------------- internals

        void SetLoop(AudioSource source, Sound sound, bool on)
        {
            if (!on || !sound.clip) { source.Stop(); return; }
            if (source.isPlaying && source.clip == sound.clip) return;
            source.clip = sound.clip;
            source.volume = sound.volume * SfxVolume;
            source.Play();
        }

        void ApplyVolumes()
        {
            if (!bank) return;
            var s = bank.GetMusic(currentMusic);
            music.volume = s.volume * MusicVolume * (paused ? bank.pausedMusicVolume : 1f);
            travel.volume = bank.travel.volume * SfxVolume;
            talk.volume = bank.dialogTalking.volume * SfxVolume;
        }

        void HookButtons()
        {
            hooked.RemoveWhere(b => !b); // buttons of unloaded scenes
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                HookButton(b);
        }
    }
}
