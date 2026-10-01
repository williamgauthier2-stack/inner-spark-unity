using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Pcb
{
    /// <summary>
    /// Modal name/portrait/text box shown once when a stage starts, if its Board has a DialogSequence assigned.
    /// Each line types out letter by letter with the "talking" sound. Continue (click, or keyboard/gamepad Submit
    /// once Continue is the selected UI element) first finishes a line that's still typing, then goes to the next.
    /// </summary>
    public class DialogController : MonoBehaviour
    {
        [Header("Wiring")]
        public GameObject panel;
        public TMP_Text speakerLabel;
        public Image portraitImage;
        public TMP_Text bodyLabel;
        public Button continueButton;

        [Header("Typing")]
        [Tooltip("Letters per second while a line types out. 0 = show the whole line at once.")]
        [Min(0f)] public float lettersPerSecond = 40f;

        DialogSequence.Line[] lines;
        int index;
        Action onComplete;
        Coroutine typing;

        public bool IsShowing => panel && panel.activeSelf;

        void Awake()
        {
            // Don't force-hide 'panel' here: if this script lives on the panel itself (the natural
            // place to put it) and the panel starts disabled in the scene - as it should - Awake only
            // fires the first time something reactivates it (i.e. the first Show()), and disabling it
            // again from inside that same Awake would immediately undo the activation that triggered it.
            // The panel's saved inactive state already covers "starts hidden".
            if (continueButton) continueButton.onClick.AddListener(Advance);
        }

        /// <summary>Starts showing 'sequence'; calls onDone once every line has been dismissed.</summary>
        public void Show(DialogSequence sequence, Action onDone)
        {
            lines = sequence ? sequence.lines : null;
            onComplete = onDone;
            index = -1;
            if (panel) panel.SetActive(true);
            Advance();
        }

        void Advance()
        {
            if (typing != null) { FinishTyping(); return; } // first press: show the rest of this line

            index++;
            if (lines == null || index >= lines.Length) { Close(); return; }

            var line = lines[index];
            if (speakerLabel) speakerLabel.text = line.speakerName;
            if (bodyLabel)
            {
                bodyLabel.text = line.text;
                if (lettersPerSecond > 0f) typing = StartCoroutine(Type());
                else bodyLabel.maxVisibleCharacters = int.MaxValue;
            }
            if (portraitImage)
            {
                portraitImage.sprite = line.portrait;
                portraitImage.enabled = line.portrait;
            }
            if (continueButton && EventSystem.current) EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
        }

        IEnumerator Type()
        {
            bodyLabel.maxVisibleCharacters = 0;
            bodyLabel.ForceMeshUpdate();
            int total = bodyLabel.textInfo.characterCount;
            AudioManager.SetTalking(true);
            float shown = 0f;
            while (shown < total)
            {
                shown += lettersPerSecond * Time.deltaTime; // stops while paused
                bodyLabel.maxVisibleCharacters = Mathf.Min(total, (int)shown);
                yield return null;
            }
            typing = null;
            AudioManager.SetTalking(false);
        }

        void FinishTyping()
        {
            if (typing != null) StopCoroutine(typing);
            typing = null;
            if (bodyLabel) bodyLabel.maxVisibleCharacters = int.MaxValue;
            AudioManager.SetTalking(false);
        }

        void Close()
        {
            FinishTyping();
            if (panel) panel.SetActive(false);
            var callback = onComplete;
            onComplete = null;
            callback?.Invoke();
        }
    }
}
