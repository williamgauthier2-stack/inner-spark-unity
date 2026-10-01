using UnityEngine;
using UnityEngine.UI;

namespace Pcb
{
    /// <summary>Put on a UI Slider (Main Menu, pause panel) to control the Music or SFX volume. Saved between sessions.</summary>
    [RequireComponent(typeof(Slider))]
    public class VolumeSlider : MonoBehaviour
    {
        public enum Channel { Music, Sfx }
        public Channel channel;

        Slider slider;

        void Awake()
        {
            slider = GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.onValueChanged.AddListener(Set);
        }

        void OnEnable() => slider.SetValueWithoutNotify(channel == Channel.Music ? AudioManager.MusicVolume : AudioManager.SfxVolume);

        void Set(float value)
        {
            if (channel == Channel.Music) AudioManager.MusicVolume = value;
            else AudioManager.SfxVolume = value;
        }
    }
}
