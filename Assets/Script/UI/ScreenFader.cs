using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Pcb
{
    /// <summary>
    /// Fade to / from black over everything, and scene loading with a fade. Creates itself the first time it's
    /// used and survives scene loads - nothing to set up in the scenes. Runs on real time (pause doesn't stop it)
    /// and blocks UI clicks while the screen isn't clear.
    /// A scene loaded with LoadScene fades back in on its own, unless something in it calls Claim() in
    /// Awake/Start to fade in itself at the right moment (LevelManager does, for the stage start sequence).
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        public const float DefaultDuration = 0.5f;

        static ScreenFader instance;
        CanvasGroup group;
        bool claimed;

        static ScreenFader Instance
        {
            get
            {
                if (instance) return instance;
                var go = new GameObject("Screen Fader");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<ScreenFader>();
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = short.MaxValue; // above every other canvas
                var scaler = go.AddComponent<CanvasScaler>(); // same 1920x1080 design size as the scenes' canvases
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                go.AddComponent<GraphicRaycaster>();
                instance.group = go.AddComponent<CanvasGroup>();
                var image = new GameObject("Black").AddComponent<Image>();
                image.transform.SetParent(go.transform, false);
                image.color = Color.black;
                var rect = image.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                instance.SetAlpha(0f);
                return instance;
            }
        }

        public static bool IsBlack => instance && instance.group.alpha >= 1f;

        /// <summary>Instantly black (e.g. a scene that starts with its own fade-in).</summary>
        public static void SetBlack() => Instance.SetAlpha(1f);

        /// <summary>Call in Awake/Start of a freshly loaded scene to do its own fade-in (see class summary).</summary>
        public static void Claim() => Instance.claimed = true;

        public static IEnumerator FadeOut(float duration = DefaultDuration) => Instance.FadeTo(1f, duration);
        public static IEnumerator FadeIn(float duration = DefaultDuration) => Instance.FadeTo(0f, duration);

        /// <summary>Fade out, load the scene, fade back in (unless the new scene claimed the fade-in).</summary>
        public static void LoadScene(string sceneName) => Instance.StartCoroutine(Instance.Load(sceneName));

        IEnumerator Load(string sceneName)
        {
            yield return FadeTo(1f, DefaultDuration);
            claimed = false;
            yield return SceneManager.LoadSceneAsync(sceneName);
            yield return null; // let the new scene's Awake/Start run (and maybe Claim)
            if (!claimed) yield return FadeTo(0f, DefaultDuration);
        }

        IEnumerator FadeTo(float target, float duration)
        {
            float start = group.alpha;
            for (float t = 0f; t < duration && !Mathf.Approximately(start, target); t += Time.unscaledDeltaTime)
            {
                SetAlpha(Mathf.Lerp(start, target, t / duration));
                yield return null;
            }
            SetAlpha(target);
        }

        void SetAlpha(float alpha)
        {
            group.alpha = alpha;
            group.blocksRaycasts = alpha > 0f; // no clicking through while fading / black
            group.interactable = false;
        }
    }
}
