using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace RotationAlert
{
    // Publishes the derived timer state as VRChat global shader variables (the "_Udon" prefix
    // is required by VRChat) so shaders on avatars in this instance can render the countdown.
    // Local only: every client derives the same values from the synced schedule.
    //
    //   _UdonRotAlertState = (remaining sec, phase 0..3, rotation index 1-based, rotation count)
    //   _UdonRotAlertFlags = (paused 0/1, warn sec, phase length sec, heartbeat = timeSinceLevelLoad)
    //
    // Global shader values outlive the world, so the heartbeat lets a shader hide itself once the
    // wearer has moved on: compare against _Time.y (also seconds since level load).
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RotationAlertShaderBridge : UdonSharpBehaviour
    {
        public RotationAlertCore core;

        private int stateId;
        private int flagsId;

        private void Start()
        {
            stateId = VRCShader.PropertyToID("_UdonRotAlertState");
            flagsId = VRCShader.PropertyToID("_UdonRotAlertFlags");
        }

        private void Update()
        {
            if (core == null) return;
            int phase = core.GetPhase();
            float remaining = core.GetRemaining();
            VRCShader.SetGlobalVector(stateId,
                new Vector4(remaining, phase, core.GetRotationIndex(), core.rotationCount));
            VRCShader.SetGlobalVector(flagsId,
                new Vector4(core.IsPaused() ? 1f : 0f, core.warnSec, core.GetPhaseLength(), Time.timeSinceLevelLoad));
        }
    }
}
