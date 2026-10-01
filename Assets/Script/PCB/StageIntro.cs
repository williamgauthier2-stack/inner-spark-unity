using System.Collections;
using UnityEngine;
using UnityEngine.Playables;

namespace Pcb
{
    /// <summary>
    /// Root of a stage's intro cinematic prefab (assigned on the Board's Intro field). The LevelManager places it
    /// at the centre of the board, lined up with it, while the screen is black, so the device is already there
    /// when the screen fades in; then it plays the Timeline, moving the real camera along Camera Pose, blends the
    /// camera into the gameplay view, and removes the prefab. Build the device around the prefab's origin
    /// (= the board's centre) and fade its casing with a FadeGroup animated in the Timeline.
    /// </summary>
    public class StageIntro : MonoBehaviour
    {
        [Tooltip("The Timeline to play. Its Play On Awake is ignored: the level starts it after the fade-in.")]
        public PlayableDirector director;
        [Tooltip("Animate this object's position/rotation in the Timeline: the game camera follows it during the intro.")]
        public Transform cameraPose;
        [Tooltip("Camera field of view during the intro (animatable). 0 = leave the camera's as it is.")]
        public float fieldOfView = 35f;
        [Tooltip("Seconds to blend from the Timeline's last camera pose into the normal gameplay view.")]
        [Min(0f)] public float blendToGameplay = 0.6f;

        Camera cam;

        void Awake()
        {
            if (!director) return;
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.None;
            director.Stop();
            director.time = 0;
            director.Evaluate(); // the first frame's pose while the screen fades in
        }

        /// <summary>Starts driving the camera (from the first frame of the Timeline) - call while the screen is black.</summary>
        public void Begin(Camera camera)
        {
            cam = camera;
            FollowPose();
        }

        /// <summary>Plays the Timeline to the end, blends the camera into the gameplay view, then removes the intro.</summary>
        public IEnumerator Play(BoardRig rig)
        {
            if (director && director.playableAsset)
            {
                director.Play();
                while (director.state == PlayState.Playing && director.time < director.duration) yield return null;
            }
            FollowPose();
            var from = cam ? cam.transform : null;
            cam = null; // stop following

            if (from && rig)
            {
                Vector3 p0 = from.position; Quaternion r0 = from.rotation; float f0 = from.GetComponent<Camera>().fieldOfView;
                var camera = from.GetComponent<Camera>();
                rig.Refit(); // puts the camera in the gameplay view: read it as the blend target
                Vector3 p1 = from.position; Quaternion r1 = from.rotation; float f1 = camera.fieldOfView;
                for (float t = 0f; t < blendToGameplay; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / blendToGameplay);
                    from.SetPositionAndRotation(Vector3.Lerp(p0, p1, k), Quaternion.Slerp(r0, r1, k));
                    camera.fieldOfView = Mathf.Lerp(f0, f1, k);
                    yield return null;
                }
                rig.Refit();
            }
            Destroy(gameObject);
        }

        // After the Timeline has animated Camera Pose this frame.
        void LateUpdate() => FollowPose();

        void FollowPose()
        {
            if (!cam || !cameraPose) return;
            cam.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
            if (fieldOfView > 0f) cam.fieldOfView = fieldOfView;
        }
    }
}
