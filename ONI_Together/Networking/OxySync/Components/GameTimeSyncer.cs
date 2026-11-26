using KSerialization;
using ONI_Together.Patches.GamePatches;
using Shared.OxySync;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    [FixedInterestGroup]
    public class GameTimeSyncer : NetworkBehaviour
    {
        public static GameTimeSyncer? Instance { get; private set; }

        [SyncVar(Hook = nameof(OnCycleChanged))]
        private int _cycle;

        [SyncVar(Hook = nameof(OnCycleTimeChanged))]
        private float _cycleTime;

        private bool _pendingTimeUpdate;

        public override void OnSpawn()
        {
            // Set NetId and InterestGroup BEFORE base.OnSpawn() so OxySyncManager registers
            // this behaviour under the deterministic GameClock NetId instead of 0.
            NetId = nameof(GameClock).GetHashCode();
            InterestGroup = -1;
            base.OnSpawn();
            Instance = this;
            SyncInterval = 0.5f;
        }

        public override void OnCleanUp()
        {
            if (Instance == this)
                Instance = null;
            base.OnCleanUp();
        }

        public void BroadcastTime(int cycle, float cycleTime)
        {
            _cycle = cycle;
            _cycleTime = cycleTime;
        }

        private void OnCycleChanged(int oldValue, int newValue)
        {
            _pendingTimeUpdate = true;
        }

        private void OnCycleTimeChanged(float oldValue, float newValue)
        {
            _pendingTimeUpdate = true;
        }

        private void LateUpdate()
        {
            if (!_pendingTimeUpdate)
                return;

            _pendingTimeUpdate = false;

            if (GameClock.Instance == null)
                return;

            float hostTotalTime = _cycle * 600f + _cycleTime;
            float clientTime = GameClock.Instance.GetTime();
            float diff = hostTotalTime - clientTime;

            // If time is significantly off (> 1.0s) or cycle differs, snap time
            if (UnityEngine.Mathf.Abs(diff) > 1.0f || GameClock.Instance.GetCycle() != _cycle)
            {
                GameClockPatch.allowAddTimeForSetTime = true;
                GameClock.Instance.SetTime(hostTotalTime);
                GameClockPatch.allowAddTimeForSetTime = false;
            }
            else if (UnityEngine.Mathf.Abs(diff) > 0.05f)
            {
                // Smoothly nudge clock towards host time without teleporting
                float correctedTime = UnityEngine.Mathf.Lerp(clientTime, hostTotalTime, 0.5f);
                GameClockPatch.allowAddTimeForSetTime = true;
                GameClock.Instance.SetTime(correctedTime);
                GameClockPatch.allowAddTimeForSetTime = false;
            }
        }
    }
}
