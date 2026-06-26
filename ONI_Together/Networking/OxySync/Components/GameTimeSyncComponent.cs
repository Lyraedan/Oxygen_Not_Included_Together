using KSerialization;
using ONI_Together.Patches.GamePatches;
using Shared.OxySync;
using Shared.OxySync.Attributes;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class GameTimeSyncComponent : NetworkBehaviour
    {
        public static GameTimeSyncComponent? Instance { get; private set; }

        [SyncVar(Hook = nameof(OnCycleChanged))]
        private int _cycle;

        [SyncVar(Hook = nameof(OnCycleTimeChanged))]
        private float _cycleTime;

        private bool _pendingTimeUpdate;

        public override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
            SyncInterval = 1f; // Every 1 second
            NetId = nameof(GameClock).GetHashCode();
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

            float totalTime = _cycle * 600f + _cycleTime;
            GameClockPatch.allowAddTimeForSetTime = true;
            GameClock.Instance.SetTime(totalTime);
            GameClockPatch.allowAddTimeForSetTime = false;
        }
    }
}
