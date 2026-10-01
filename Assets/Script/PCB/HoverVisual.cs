using UnityEngine;

namespace Pcb
{
    /// <summary>
    /// Added by the Board to a hovering pickup look (data pickup, goal lock): bobs gently up and down in play mode,
    /// and Dismiss() makes it shrink away while floating up, then hides it.
    /// </summary>
    [AddComponentMenu("")]
    public class HoverVisual : MonoBehaviour
    {
        public float bobHeight = 0.03f;
        public float bobSpeed = 2.5f;
        public float dismissTime = 0.4f;
        public float dismissRise = 0.25f;

        Vector3 basePosition, baseScale, up;
        bool started, dismissing;
        float dismissT;

        void Start() => Init();

        void Init()
        {
            if (started) return;
            started = true;
            basePosition = transform.localPosition;
            baseScale = transform.localScale;
            // Up on screen = the board's +Y, expressed in the (possibly rotated) node group's space.
            up = transform.parent ? Quaternion.Inverse(transform.parent.localRotation) * Vector3.up : Vector3.up;
        }

        /// <summary>Shrink away while floating up, then hide.</summary>
        public void Dismiss()
        {
            Init();
            dismissing = true;
        }

        void Update()
        {
            float bob = bobHeight * Mathf.Sin(Time.time * bobSpeed);
            if (!dismissing)
            {
                transform.localPosition = basePosition + up * bob;
                return;
            }
            dismissT += Time.deltaTime / Mathf.Max(0.01f, dismissTime);
            float k = Mathf.SmoothStep(0f, 1f, dismissT);
            transform.localScale = baseScale * (1f - k);
            transform.localPosition = basePosition + up * (bob + dismissRise * k);
            if (dismissT >= 1f) gameObject.SetActive(false);
        }
    }
}
