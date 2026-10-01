using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pcb
{
    /// <summary>
    /// Fades a whole model in or out with one value (animate Alpha in a Timeline, e.g. a device casing fading
    /// away). While partly faded it uses transparent copies of the materials (URP Lit / Unlit), so the original
    /// materials are never changed; fully visible it's back on the originals, fully faded its renderers are off.
    /// In the editor (Timeline preview) it only shows / hides.
    /// </summary>
    [ExecuteAlways]
    public class FadeGroup : MonoBehaviour
    {
        [Range(0f, 1f)] public float alpha = 1f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Material[]> originals = new List<Material[]>();
        readonly List<Material[]> faded = new List<Material[]>();
        float applied = -1f;

        void LateUpdate()
        {
            if (!Mathf.Approximately(alpha, applied)) Apply();
        }

        void Collect()
        {
            if (renderers.Count > 0) return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                renderers.Add(r);
                originals.Add(r.sharedMaterials);
                faded.Add(null);
            }
        }

        void Apply()
        {
            applied = alpha;
            Collect();
            bool visible = alpha > 0.001f, opaque = alpha >= 0.999f;
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (!r) continue;
                r.enabled = visible;
                if (!Application.isPlaying || !visible) continue;
                if (opaque) { r.sharedMaterials = originals[i]; continue; }

                if (faded[i] == null)
                {
                    var copies = new Material[originals[i].Length];
                    for (int m = 0; m < copies.Length; m++) copies[m] = originals[i][m] ? Transparent(originals[i][m]) : null;
                    faded[i] = copies;
                }
                foreach (var mat in faded[i])
                {
                    if (!mat) continue;
                    if (mat.HasProperty(BaseColorId)) { var c = mat.GetColor(BaseColorId); c.a = alpha; mat.SetColor(BaseColorId, c); }
                    if (mat.HasProperty(ColorId)) { var c = mat.GetColor(ColorId); c.a = alpha; mat.SetColor(ColorId, c); }
                }
                r.sharedMaterials = faded[i];
            }
        }

        /// <summary>A copy of a URP material switched to alpha-blended transparency.</summary>
        static Material Transparent(Material source)
        {
            var m = new Material(source) { name = source.name + " (Fade)" };
            m.SetFloat("_Surface", 1f); // transparent
            m.SetFloat("_Blend", 0f);   // alpha
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        void OnDestroy()
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] && Application.isPlaying) renderers[i].sharedMaterials = originals[i];
                if (faded[i] != null) foreach (var m in faded[i]) if (m) Destroy(m);
            }
        }

        void OnDisable()
        {
            // Editor: leave the model visible when the component is switched off or the Timeline preview ends.
            if (!Application.isPlaying) foreach (var r in renderers) if (r) r.enabled = true;
            applied = -1f;
        }
    }
}
