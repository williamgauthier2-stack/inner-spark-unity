using UnityEngine;
using UnityEngine.InputSystem;

namespace Pcb
{
    /// <summary>
    /// Pivot at the centre of a board (created by LevelManager). Frames it with a tilted perspective camera,
    /// turns the board over when the spark changes side, and lets the mouse tilt it slightly for inspection.
    /// </summary>
    public class BoardRig : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("Degrees the camera looks up at the board from below (tabletop feel).")]
        public float cameraTilt = 18f;
        public float fieldOfView = 35f;
        [Tooltip("Extra space around the board, 1 = board touches the screen edge.")]
        public float framePadding = 1.15f;

        [Header("Turn over")]
        public float flipDuration = 0.6f;

        [Header("Mouse inspection (hold left button and drag)")]
        [Tooltip("Max tilt in degrees. Keep well under 90 so the hidden side can never be seen.")]
        public float inspectMaxAngle = 22f;
        [Tooltip("Degrees per pixel dragged.")]
        public float inspectSensitivity = 0.12f;
        [Tooltip("How fast the board springs back after releasing the mouse.")]
        public float returnSpeed = 8f;

        Board board;
        Camera cam;
        float flipAngle, flipFrom, flipTo, flipT = 1f;
        PcbLayer flipTarget;
        Vector2 inspect;
        Vector2Int lastScreen;

        [Tooltip("Camera far clip at least this far (0 = just fit the board). Raise it if the room behind the board gets cut off.")]
        public float minFarClip = 0f;
        /// <summary>No mouse tilting (e.g. during the stage start sequence).</summary>
        public bool InspectLocked { get; set; }

        public bool IsTurning => flipT < 1f;
        /// <summary>0..1 eased progress of the current turn (1 when not turning).</summary>
        public float TurnProgress => Mathf.SmoothStep(0f, 1f, flipT);

        /// <param name="anchor">Optional: the board's centre is placed here (a fixed spot in the room).</param>
        public static BoardRig Create(Board board, Camera cam, Transform anchor = null, float minFarClip = 0f)
        {
            var go = new GameObject(board.name + " Rig");
            go.transform.position = board.Center;
            board.transform.SetParent(go.transform, true);
            if (anchor) go.transform.position = anchor.position; // moves the board with it
            var rig = go.AddComponent<BoardRig>();
            rig.board = board;
            rig.cam = cam;
            rig.minFarClip = minFarClip;
            rig.Frame();
            return rig;
        }

        /// <summary>Puts the camera back in the gameplay view (e.g. after an intro cinematic moved it).</summary>
        public void Refit() => Frame();

        /// <summary>Instantly show a side (level start).</summary>
        public void SnapTo(PcbLayer layer)
        {
            flipAngle = layer == PcbLayer.Back ? 180f : 0f;
            flipT = 1f;
            board.SetView(layer);
            Apply();
        }

        /// <summary>Animate the board turning over to show 'layer'.</summary>
        public void TurnTo(PcbLayer layer)
        {
            flipTarget = layer;
            flipFrom = flipAngle;
            flipTo = layer == PcbLayer.Back ? 180f : 0f;
            flipT = 0f;
            board.ShowBothSides();
        }

        void Update()
        {
            if (flipT < 1f)
            {
                flipT = Mathf.Min(1f, flipT + Time.deltaTime / Mathf.Max(0.01f, flipDuration));
                flipAngle = Mathf.Lerp(flipFrom, flipTo, TurnProgress);
                if (flipT >= 1f) board.SetView(flipTarget); // hide the side we turned away from
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed && GUIUtility.hotControl == 0 && !PauseMenu.GamePaused && !InspectLocked)
            {
                inspect += mouse.delta.ReadValue() * inspectSensitivity;
                inspect = Vector2.ClampMagnitude(inspect, inspectMaxAngle);
            }
            else inspect = Vector2.Lerp(inspect, Vector2.zero, 1f - Mathf.Exp(-returnSpeed * Time.deltaTime));

            if ((lastScreen.x != Screen.width || lastScreen.y != Screen.height) && !InspectLocked) Frame(); // not while an intro drives the camera
            Apply();
        }

        void Apply()
        {
            Transform c = cam ? cam.transform : null;
            Vector3 right = c ? c.right : Vector3.right, up = c ? c.up : Vector3.up;
            var tilt = Quaternion.AngleAxis(inspect.y, right) * Quaternion.AngleAxis(-inspect.x, up);
            transform.rotation = tilt * Quaternion.Euler(0f, flipAngle, 0f);
        }

        void Frame()
        {
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (!cam || !board) return;
            FitCamera(cam, board, cameraTilt, fieldOfView, framePadding);
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, minFarClip);
        }

        /// <summary>Places a perspective camera in front of the board, tilted up from below, so it all fits.</summary>
        public static void FitCamera(Camera cam, Board board, float tilt = 18f, float fov = 35f, float padding = 1.15f)
        {
            var theme = board.theme;
            float margin = theme ? theme.boardMargin : 0.5f;
            Vector2 size = board.Size + Vector2.one * margin * 2f;
            cam.orthographic = false;
            cam.fieldOfView = fov;
            float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(size.y * 0.5f / tanHalf, size.x * 0.5f / (tanHalf * Mathf.Max(cam.aspect, 0.01f))) * padding;
            Vector3 center = board.Center;
            Vector3 dir = Quaternion.Euler(-tilt, 0f, 0f) * Vector3.back; // below and in front
            cam.transform.position = center + dir * distance;
            cam.transform.LookAt(center, Vector3.up);
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = distance * 4f + 10f;
            if (theme)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = theme.background;
            }
        }
    }
}
