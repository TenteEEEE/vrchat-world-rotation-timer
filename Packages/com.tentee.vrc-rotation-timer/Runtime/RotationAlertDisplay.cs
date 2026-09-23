using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace RotationAlert
{
    // Local-only presentation. No sync. Any number of these can point at one Core.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RotationAlertDisplay : UdonSharpBehaviour
    {
        public RotationAlertCore core;
        public RotationAlertAudio alertAudio;

        public TextMeshProUGUI phaseText;
        public TextMeshProUGUI timeText;
        public TextMeshProUGUI subText;
        public TextMeshProUGUI settingsText;
        public TextMeshProUGUI pauseButtonLabel;
        public TextMeshProUGUI skipButtonLabel;
        public TextMeshProUGUI resetButtonLabel;
        public TextMeshProUGUI muteButtonLabel;

        public Image progressFill;
        public Image[] accentImages;

        public Button startButton;
        public Button[] runControlButtons;
        public Button[] settingButtons;

        public GraphicRaycaster uiRaycaster;
        public float interactRange = 1f;

        // Colors are built on demand instead of via field initializers so the
        // UdonSharp compiler never has to evaluate a constructor at field-init time.
        private Color ColorIdle() { return new Color(0x6B / 255f, 0x7A / 255f, 0x85 / 255f); }
        private Color ColorRotation() { return new Color(0x2E / 255f, 0xD3 / 255f, 0xC6 / 255f); }
        private Color ColorWarn() { return new Color(0xF4 / 255f, 0xB9 / 255f, 0x42 / 255f); }
        private Color ColorDanger() { return new Color(0xE0 / 255f, 0x5A / 255f, 0x4E / 255f); }
        private Color ColorInterval() { return new Color(0x4C / 255f, 0x8D / 255f, 0xFF / 255f); }
        private Color ColorFinished() { return new Color(0x3C / 255f, 0xC4 / 255f, 0x6B / 255f); }

        private float textAccum;
        private const float TextInterval = 0.1f;
        private float rangeAccum;
        private const float RangeInterval = 0.25f;
        private const float RangeHysteresis = 0.5f;

        private string cachedPhaseText = "";
        private string cachedTimeText = "";
        private string cachedSubText = "";
        private string cachedSettingsText = "";
        private string cachedPauseLabel = "";
        private string cachedSkipLabel = "";
        private string cachedResetLabel = "";
        private string cachedMuteLabel = "";

        private void Update()
        {
            UpdateInteractionRange();

            if (core == null) return;

            textAccum += Time.deltaTime;
            bool rebuildText = textAccum >= TextInterval;
            if (rebuildText) textAccum = 0f;

            int phase = core.GetPhase();
            bool paused = core.IsPaused();
            float remaining = core.GetRemaining();
            float phaseLength = core.GetPhaseLength();

            if (rebuildText)
            {
                UpdatePhaseText(phase, paused);
                UpdateTimeText(phase, remaining);
                UpdateSubText(phase);
                UpdateSettingsText();
                UpdatePauseLabel(paused);
                UpdateSkipLabel();
                UpdateResetLabel();
                UpdateMuteLabel();
            }

            UpdateColors(phase, paused, remaining);
            UpdateFill(remaining, phaseLength);
            UpdateInteractable(phase);
        }

        private void UpdatePhaseText(int phase, bool paused)
        {
            string text;
            if (paused)
            {
                text = "一時停止中";
            }
            else if (phase == 0)
            {
                text = "待機中";
            }
            else if (phase == 1)
            {
                text = "ローテーション " + core.GetRotationIndex() + " / " + core.rotationCount;
            }
            else if (phase == 2)
            {
                int next = core.GetRotationIndex() + 1;
                text = "インターバル（次: " + next + " / " + core.rotationCount + "）";
            }
            else
            {
                text = "全ローテ終了";
            }
            if (text == cachedPhaseText) return;
            cachedPhaseText = text;
            if (phaseText != null) phaseText.text = text;
        }

        private void UpdateTimeText(int phase, float remaining)
        {
            int secs = phase == 3 ? 0 : Mathf.CeilToInt(remaining);
            if (secs < 0) secs = 0;
            int mm = secs / 60;
            int ss = secs % 60;
            string text = mm.ToString("00") + ":" + ss.ToString("00");
            if (text == cachedTimeText) return;
            cachedTimeText = text;
            if (timeText != null) timeText.text = text;
        }

        private void UpdateSubText(int phase)
        {
            string text;
            if (phase == 0)
            {
                text = (core.rotationSec / 60) + "分 × " + core.rotationCount + "回 ／ 休憩 " + (core.intervalSec / 60) + "分 ／ 予告 " + (core.warnSec / 60) + "分前";
            }
            else if (phase == 1)
            {
                text = "終了まで";
                if (core.warnSec > 0) text += " ／ 予告 " + (core.warnSec / 60) + "分前";
            }
            else if (phase == 2)
            {
                text = "次のローテ開始まで";
            }
            else
            {
                text = "おつかれさまでした";
            }
            if (text == cachedSubText) return;
            cachedSubText = text;
            if (subText != null) subText.text = text;
        }

        private void UpdateSettingsText()
        {
            string text = "ローテ " + (core.rotationSec / 60) + "分 ／ 休憩 " + (core.intervalSec / 60) + "分 ／ 回数 " + core.rotationCount + " ／ 予告 " + (core.warnSec / 60) + "分前";
            if (text == cachedSettingsText) return;
            cachedSettingsText = text;
            if (settingsText != null) settingsText.text = text;
        }

        private void UpdatePauseLabel(bool paused)
        {
            string text = paused ? "再開" : "一時停止";
            if (text == cachedPauseLabel) return;
            cachedPauseLabel = text;
            if (pauseButtonLabel != null) pauseButtonLabel.text = text;
        }

        private void UpdateSkipLabel()
        {
            string text = core.IsSkipArmed() ? "本当に？" : "次へ";
            if (text == cachedSkipLabel) return;
            cachedSkipLabel = text;
            if (skipButtonLabel != null) skipButtonLabel.text = text;
        }

        private void UpdateResetLabel()
        {
            string text = core.IsResetArmed() ? "本当に？" : "リセット";
            if (text == cachedResetLabel) return;
            cachedResetLabel = text;
            if (resetButtonLabel != null) resetButtonLabel.text = text;
        }

        private void UpdateMuteLabel()
        {
            bool muted = alertAudio != null && alertAudio.muted;
            string text = muted ? "ローカル音: OFF" : "ローカル音: ON";
            if (text == cachedMuteLabel) return;
            cachedMuteLabel = text;
            if (muteButtonLabel != null) muteButtonLabel.text = text;
        }

        private void UpdateInteractionRange()
        {
            if (uiRaycaster == null) return;

            rangeAccum += Time.deltaTime;
            if (rangeAccum < RangeInterval) return;
            rangeAccum = 0f;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null)
            {
                uiRaycaster.enabled = true;
                return;
            }

            float distance = Vector3.Distance(localPlayer.GetPosition(), uiRaycaster.transform.position);
            if (uiRaycaster.enabled)
            {
                if (distance > interactRange + RangeHysteresis) uiRaycaster.enabled = false;
            }
            else if (distance <= interactRange)
            {
                uiRaycaster.enabled = true;
            }
        }

        private void UpdateColors(int phase, bool paused, float remaining)
        {
            Color color;
            if (paused)
            {
                color = ColorIdle();
            }
            else if (phase == 0)
            {
                color = ColorIdle();
            }
            else if (phase == 1)
            {
                color = ColorRotation();
                if (core.warnSec > 0 && remaining <= core.warnSec) color = ColorWarn();
                if (remaining <= 60f) color = ColorDanger();
            }
            else if (phase == 2)
            {
                color = ColorInterval();
            }
            else
            {
                color = ColorFinished();
            }

            if (accentImages != null)
            {
                for (int i = 0; i < accentImages.Length; i++)
                {
                    Image img = accentImages[i];
                    if (img == null) continue;
                    img.color = color;
                }
            }

            if (timeText != null)
            {
                Color textColor = timeText.color;
                if (paused)
                {
                    float alpha = Mathf.Lerp(1f, 0.35f, (Mathf.Sin(Time.time * (2f * Mathf.PI / 0.5f)) * 0.5f) + 0.5f);
                    textColor.a = alpha;
                }
                else
                {
                    textColor.a = 1f;
                }
                timeText.color = textColor;
            }
        }

        private void UpdateFill(float remaining, float phaseLength)
        {
            if (progressFill == null) return;
            float length = phaseLength <= 0f ? 1f : phaseLength;
            float ratio = 1f - (remaining / length);
            if (ratio < 0f) ratio = 0f;
            if (ratio > 1f) ratio = 1f;
            progressFill.fillAmount = ratio;
        }

        private void UpdateInteractable(int phase)
        {
            bool running = core.running;
            bool finished = phase == 3;

            if (startButton != null) startButton.interactable = !running || finished;

            bool runControlsOn = running && !finished;
            if (runControlButtons != null)
            {
                for (int i = 0; i < runControlButtons.Length; i++)
                {
                    Button b = runControlButtons[i];
                    if (b == null) continue;
                    b.interactable = runControlsOn;
                }
            }

            bool settingsOn = !running;
            if (settingButtons != null)
            {
                for (int i = 0; i < settingButtons.Length; i++)
                {
                    Button b = settingButtons[i];
                    if (b == null) continue;
                    b.interactable = settingsOn;
                }
            }
        }
    }
}
