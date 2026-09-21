using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace RotationAlert
{
    // Sync model: only absolute time + knobs are synced. Countdown values are derived locally.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RotationAlertCore : UdonSharpBehaviour
    {
        // --- synced fields ---
        [UdonSynced] public bool running;
        [UdonSynced] public double scheduleStart;
        [UdonSynced] public bool paused;
        [UdonSynced] public double pausedAt;
        [UdonSynced] public int rotationSec;
        [UdonSynced] public int intervalSec;
        [UdonSynced] public int rotationCount;
        [UdonSynced] public int warnSec;
        [UdonSynced] public int revision;

        // --- inspector defaults (not synced) ---
        public int defaultRotationMinutes = 15;
        public int defaultIntervalMinutes = 2;
        public int defaultRotationCount = 3;
        public int defaultWarnMinutes = 3;
        public int preStartSec = 30;
        public UdonBehaviour[] listeners;

        // --- local reset-arm state ---
        private float resetArmedUntil = -1f;

        private void Start()
        {
            // Only the owner seeds the knobs from the inspector defaults. A late joiner must not
            // overwrite synced values that may already have arrived before Start ran.
            if (!Networking.IsOwner(gameObject)) return;
            rotationSec = ClampInt(defaultRotationMinutes * 60, 60, 3600);
            intervalSec = ClampInt(defaultIntervalMinutes * 60, 0, 1800);
            rotationCount = ClampInt(defaultRotationCount, 1, 30);
            int maxWarn = rotationSec - 60;
            if (maxWarn < 0) maxWarn = 0;
            warnSec = ClampInt(defaultWarnMinutes * 60, 0, maxWarn);
            RequestSerialization();
        }

        // ---------------------------------------------------------------
        // Elapsed-based internals (take a pre-computed elapsed/phase so a
        // single operation only reads the network clock once).
        // ---------------------------------------------------------------

        private int PhaseFor(double elapsed)
        {
            if (!running) return 0; // Idle
            int cycle = Cycle();
            int idx = (int)System.Math.Floor(elapsed / cycle);
            double within = elapsed - idx * (double)cycle;
            if (idx >= rotationCount) return 3; // Finished
            if (idx == rotationCount - 1 && within >= rotationSec) return 3; // Finished
            if (within < rotationSec) return 1; // Rotation
            return 2; // Interval
        }

        private float RemainingFor(double elapsed, int phase)
        {
            if (phase == 0) return rotationSec; // Idle
            if (phase == 3) return 0f; // Finished
            int cycle = Cycle();
            int idx = (int)System.Math.Floor(elapsed / cycle);
            double within = elapsed - idx * (double)cycle;
            if (phase == 1) return (float)(rotationSec - within);
            return (float)(cycle - within);
        }

        private float PhaseLengthFor(int phase)
        {
            if (phase == 0) return rotationSec; // Idle
            if (phase == 3) return 1f; // Finished (avoid 0-length)
            if (phase == 1) return rotationSec;
            return Cycle() - rotationSec; // Interval length == intervalSec
        }

        private int RotationIndexFor(double elapsed, int phase)
        {
            if (phase == 0) return 0; // Idle
            if (phase == 3) return rotationCount; // Finished
            int cycle = Cycle();
            int idx = (int)System.Math.Floor(elapsed / cycle);
            return idx + 1;
        }

        // elapsed value (measured from scheduleStart) at which the given phase began.
        private double PhaseStartElapsedFor(double elapsed, int phase)
        {
            int cycle = Cycle();
            int idx = (int)System.Math.Floor(elapsed / cycle);
            double phaseStartElapsed = idx * (double)cycle;
            if (phase != 1) phaseStartElapsed += rotationSec; // Interval starts after the rotation portion
            return phaseStartElapsed;
        }

        // ---------------------------------------------------------------
        // Derived API (public, compute-only, no state writes). Each call
        // reads the network clock at most once via EffectiveElapsed().
        // ---------------------------------------------------------------

        public double EffectiveElapsed()
        {
            if (!running) return 0;
            double now = paused ? pausedAt : Networking.GetServerTimeInSeconds();
            return now - scheduleStart;
        }

        public int Cycle()
        {
            return rotationSec + intervalSec;
        }

        public int GetPhase()
        {
            return PhaseFor(EffectiveElapsed());
        }

        public bool IsPaused()
        {
            return running && paused;
        }

        public int GetRotationIndex()
        {
            double elapsed = EffectiveElapsed();
            return RotationIndexFor(elapsed, PhaseFor(elapsed));
        }

        public float GetRemaining()
        {
            double elapsed = EffectiveElapsed();
            return RemainingFor(elapsed, PhaseFor(elapsed));
        }

        public float GetPhaseLength()
        {
            return PhaseLengthFor(GetPhase());
        }

        // ---------------------------------------------------------------
        // Ownership
        // ---------------------------------------------------------------

        public void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        public bool IsResetArmed()
        {
            return resetArmedUntil > 0f && Time.time <= resetArmedUntil;
        }

        // ---------------------------------------------------------------
        // Common helper for "shift remaining seconds" operations.
        // scheduleStart = anchor - (elapsedAtPhaseStart + (phaseLength - targetRemaining))
        // Avoids accumulating float error and lands Skip exactly on the boundary.
        // ---------------------------------------------------------------

        private void ApplyShift(double anchor, double phaseStartElapsed, float phaseLength, double targetRemaining)
        {
            scheduleStart = anchor - (phaseStartElapsed + (phaseLength - targetRemaining));
        }

        // ---------------------------------------------------------------
        // Operations. Each reads Networking.GetServerTimeInSeconds() exactly
        // once so guard checks and the resulting write agree on "now".
        // ---------------------------------------------------------------

        public void OpStart()
        {
            double now = Networking.GetServerTimeInSeconds();
            double elapsed = running ? (paused ? pausedAt : now) - scheduleStart : 0;
            int phase = PhaseFor(elapsed);
            if (running && phase != 3) return;
            TakeOwnership();
            scheduleStart = now;
            paused = false;
            pausedAt = 0;
            running = true;
            revision++;
            RequestSerialization();
        }

        public void OpPauseResume()
        {
            double now = Networking.GetServerTimeInSeconds();
            double anchor = paused ? pausedAt : now;
            double elapsed = running ? anchor - scheduleStart : 0;
            int phase = PhaseFor(elapsed);
            if (!running || phase == 3) return;
            TakeOwnership();
            if (!paused)
            {
                paused = true;
                pausedAt = now;
            }
            else
            {
                scheduleStart += now - pausedAt;
                paused = false;
                pausedAt = 0;
            }
            revision++;
            RequestSerialization();
        }

        public void OpSkip()
        {
            double now = Networking.GetServerTimeInSeconds();
            double anchor = paused ? pausedAt : now;
            double elapsed = running ? anchor - scheduleStart : 0;
            int phase = PhaseFor(elapsed);
            if (!running || phase == 3) return;
            TakeOwnership();
            double phaseStartElapsed = PhaseStartElapsedFor(elapsed, phase);
            float phaseLength = PhaseLengthFor(phase);
            ApplyShift(anchor, phaseStartElapsed, phaseLength, 0);
            revision++;
            RequestSerialization();
        }

        public void OpPlusMinute()
        {
            double now = Networking.GetServerTimeInSeconds();
            double anchor = paused ? pausedAt : now;
            double elapsed = running ? anchor - scheduleStart : 0;
            int phase = PhaseFor(elapsed);
            if (!running || phase == 3) return;
            TakeOwnership();
            float remaining = RemainingFor(elapsed, phase);
            float phaseLength = PhaseLengthFor(phase);
            double target = remaining + 60.0;
            if (target > phaseLength) target = phaseLength;
            double phaseStartElapsed = PhaseStartElapsedFor(elapsed, phase);
            ApplyShift(anchor, phaseStartElapsed, phaseLength, target);
            revision++;
            RequestSerialization();
        }

        public void OpMinusMinute()
        {
            double now = Networking.GetServerTimeInSeconds();
            double anchor = paused ? pausedAt : now;
            double elapsed = running ? anchor - scheduleStart : 0;
            int phase = PhaseFor(elapsed);
            if (!running || phase == 3) return;
            TakeOwnership();
            float remaining = RemainingFor(elapsed, phase);
            float phaseLength = PhaseLengthFor(phase);
            double target = remaining <= 60.0 ? 0.0 : remaining - 60.0;
            double phaseStartElapsed = PhaseStartElapsedFor(elapsed, phase);
            ApplyShift(anchor, phaseStartElapsed, phaseLength, target);
            revision++;
            RequestSerialization();
        }

        public void OpResetRequest()
        {
            if (IsResetArmed())
            {
                TakeOwnership();
                running = false;
                paused = false;
                pausedAt = 0;
                resetArmedUntil = -1f;
                revision++;
                RequestSerialization();
            }
            else
            {
                resetArmedUntil = Time.time + 4f;
            }
        }

        public void OpRotationPlus()
        {
            if (running) return;
            TakeOwnership();
            rotationSec = ClampInt(rotationSec + 60, 60, 3600);
            if (warnSec > rotationSec - 60) warnSec = ClampInt(rotationSec - 60, 0, rotationSec - 60);
            revision++;
            RequestSerialization();
        }

        public void OpRotationMinus()
        {
            if (running) return;
            TakeOwnership();
            rotationSec = ClampInt(rotationSec - 60, 60, 3600);
            if (warnSec > rotationSec - 60) warnSec = ClampInt(rotationSec - 60, 0, rotationSec - 60);
            revision++;
            RequestSerialization();
        }

        public void OpIntervalPlus()
        {
            if (running) return;
            TakeOwnership();
            intervalSec = ClampInt(intervalSec + 60, 0, 1800);
            revision++;
            RequestSerialization();
        }

        public void OpIntervalMinus()
        {
            if (running) return;
            TakeOwnership();
            intervalSec = ClampInt(intervalSec - 60, 0, 1800);
            revision++;
            RequestSerialization();
        }

        public void OpCountPlus()
        {
            if (running) return;
            TakeOwnership();
            rotationCount = ClampInt(rotationCount + 1, 1, 30);
            revision++;
            RequestSerialization();
        }

        public void OpCountMinus()
        {
            if (running) return;
            TakeOwnership();
            rotationCount = ClampInt(rotationCount - 1, 1, 30);
            revision++;
            RequestSerialization();
        }

        public void OpWarnPlus()
        {
            if (running) return;
            TakeOwnership();
            int maxWarn = rotationSec - 60;
            if (maxWarn < 0) maxWarn = 0;
            warnSec = ClampInt(warnSec + 60, 0, maxWarn);
            revision++;
            RequestSerialization();
        }

        public void OpWarnMinus()
        {
            if (running) return;
            TakeOwnership();
            int maxWarn = rotationSec - 60;
            if (maxWarn < 0) maxWarn = 0;
            warnSec = ClampInt(warnSec - 60, 0, maxWarn);
            revision++;
            RequestSerialization();
        }

        private int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // ---------------------------------------------------------------
        // Listener fan-out
        // ---------------------------------------------------------------

        public void NotifyListeners(string eventName)
        {
            if (listeners == null) return;
            for (int i = 0; i < listeners.Length; i++)
            {
                UdonBehaviour target = listeners[i];
                if (target == null) continue;
                target.SendCustomEvent(eventName);
            }
        }
    }
}
