using UdonSharp;
using UnityEngine;

namespace RotationAlert
{
    // Local-only cue player. Never fires over the network; every client derives cues
    // from the same synced schedule and plays them locally so they land in sync.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RotationAlertAudio : UdonSharpBehaviour
    {
        public RotationAlertCore core;
        public AudioSource source;

        // Index order matches §3: 0 RotationStart, 1 Warning, 2 RotationEnd,
        // 3 Countdown, 4 AllFinished. Countdown is intentionally silent (null clip).
        public AudioClip[] cueClips = new AudioClip[5];
        public float[] cueVolumes = { 1f, 1f, 1f, 0f, 1f };
        public int[] cueRepeats = { 1, 1, 1, 1, 1 };
        public float repeatGap = 0.6f;
        public bool muted;

        public AudioClip uiClick;
        public float uiClickVolume = 0.35f;

        private const double Grace = 2.0;
        private const int MaxCuesPerFrame = 32;

        private bool armed;
        private int lastRevision = int.MinValue;
        private double lastElapsed;
        // Monotonic floor: a cue already played never re-fires when a pause/resume/±1min
        // re-opens the 2 s grace window right after it (Skip still lands on a later boundary).
        private double lastFiredCueTime = -1e9;

        private double[] scratchTimes = new double[MaxCuesPerFrame];
        private int[] scratchIds = new int[MaxCuesPerFrame];

        private AudioClip pendingClip;
        private float pendingVolume;
        private int pendingCount;

        private void Update()
        {
            if (core == null) return;

            if (!core.running)
            {
                armed = false;
                lastRevision = core.revision;
                lastFiredCueTime = -1e9;
                return;
            }

            double elapsed = core.EffectiveElapsed();

            if (!armed || core.revision != lastRevision)
            {
                lastElapsed = elapsed - Grace;
                armed = true;
                lastRevision = core.revision;
                core.NotifyListeners("OnScheduleChanged");
            }

            int count = FindFiringCues(lastElapsed, elapsed);
            lastElapsed = elapsed;
            if (count <= 0) return;

            // Insertion sort by time (small n).
            for (int i = 1; i < count; i++)
            {
                double t = scratchTimes[i];
                int id = scratchIds[i];
                int j = i - 1;
                while (j >= 0 && scratchTimes[j] > t)
                {
                    scratchTimes[j + 1] = scratchTimes[j];
                    scratchIds[j + 1] = scratchIds[j];
                    j--;
                }
                scratchTimes[j + 1] = t;
                scratchIds[j + 1] = id;
            }

            int audibleId = -1;
            for (int i = 0; i < count; i++)
            {
                int id = scratchIds[i];
                core.NotifyListeners(EventNameForId(id));
                // Countdown is listener-only. Audible priority is AllFinished > RotationEnd
                // > Warning > RotationStart; RotationEnd is never emitted for the last rotation.
                if (id != 3 && id > audibleId) audibleId = id;
                if (scratchTimes[i] > lastFiredCueTime) lastFiredCueTime = scratchTimes[i];
            }

            if (audibleId >= 0) PlayCue(audibleId);
        }

        private int FindFiringCues(double windowStart, double windowEnd)
        {
            int cycle = core.Cycle();
            if (cycle <= 0) cycle = 1;
            int count = core.rotationCount;
            int rotationSec = core.rotationSec;
            int intervalSec = core.intervalSec;
            int warnSec = core.warnSec;
            int preStartSec = core.preStartSec;

            int rFrom = (int)System.Math.Floor(windowStart / cycle) - 1;
            if (rFrom < 0) rFrom = 0;
            int rTo = (int)System.Math.Floor(windowEnd / cycle) + 1;
            if (rTo > count - 1) rTo = count - 1;

            int found = 0;
            for (int r = rFrom; r <= rTo; r++)
            {
                if (found >= MaxCuesPerFrame) break;
                double baseT = (double)r * cycle;

                // id 0: RotationStart
                found = TryAddCue(baseT, 0, windowStart, windowEnd, found);

                // id 1: Warning
                if (warnSec > 0 && warnSec < rotationSec)
                {
                    found = TryAddCue(baseT + rotationSec - warnSec, 1, windowStart, windowEnd, found);
                }

                // id 2: RotationEnd (not after the last rotation)
                if (r < count - 1)
                {
                    found = TryAddCue(baseT + rotationSec, 2, windowStart, windowEnd, found);
                }

                // id 3: Countdown (not after the last rotation)
                if (preStartSec > 0 && preStartSec < intervalSec && r < count - 1)
                {
                    found = TryAddCue(baseT + rotationSec + intervalSec - preStartSec, 3, windowStart, windowEnd, found);
                }

                // id 4: AllFinished (only at the last rotation)
                if (r == count - 1)
                {
                    found = TryAddCue(baseT + rotationSec, 4, windowStart, windowEnd, found);
                }
            }
            return found;
        }

        private int TryAddCue(double t, int id, double windowStart, double windowEnd, int found)
        {
            if (found >= MaxCuesPerFrame) return found;
            if (t > windowStart && t > lastFiredCueTime && t <= windowEnd + 1e-3)
            {
                scratchTimes[found] = t;
                scratchIds[found] = id;
                found++;
            }
            return found;
        }

        private string EventNameForId(int id)
        {
            if (id == 0) return "OnRotationStart";
            if (id == 1) return "OnRotationWarning";
            if (id == 2) return "OnRotationEnd";
            if (id == 3) return "OnIntervalCountdown";
            return "OnAllFinished";
        }

        private void PlayCue(int id)
        {
            if (muted) return;
            if (cueClips == null || id < 0 || id >= cueClips.Length) return;
            AudioClip clip = cueClips[id];
            if (clip == null || source == null) return;

            float volume = (cueVolumes != null && id < cueVolumes.Length) ? cueVolumes[id] : 1f;
            int repeats = (cueRepeats != null && id < cueRepeats.Length) ? cueRepeats[id] : 1;
            if (repeats < 1) repeats = 1;
            if (repeats > 3) repeats = 3;

            source.PlayOneShot(clip, volume);

            if (repeats > 1)
            {
                pendingClip = clip;
                pendingVolume = volume;
                pendingCount = repeats - 1;
                SendCustomEventDelayedSeconds(nameof(PlayPendingRepeat), repeatGap);
            }
        }

        public void PlayPendingRepeat()
        {
            if (pendingCount <= 0) return;
            if (!muted && pendingClip != null && source != null)
            {
                source.PlayOneShot(pendingClip, pendingVolume);
            }
            pendingCount--;
            if (pendingCount > 0)
            {
                SendCustomEventDelayedSeconds(nameof(PlayPendingRepeat), repeatGap);
            }
        }

        public void ToggleMute()
        {
            muted = !muted;
        }

        public void PlayUiClick()
        {
            if (muted) return;
            if (uiClick == null || source == null) return;
            source.PlayOneShot(uiClick, uiClickVolume);
        }
    }
}
