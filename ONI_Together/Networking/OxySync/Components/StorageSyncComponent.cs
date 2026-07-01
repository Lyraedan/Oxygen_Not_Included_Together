using KSerialization;
using ONI_Together.Misc;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class StorageSyncComponent : NetworkBehaviour
    {
        private Storage _storage;
        private bool _storageDirty;
        private float _syncTimer;
        private const float STORAGE_SYNC_DELAY = 0.2f;

        [SyncVar(Hook = nameof(OnStorageChanged), SendMode = PacketSendMode.ReliableImmediate)]
        private byte[] _storageBlob;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _storage = GetComponent<Storage>();

            if (_storage != null)
                _storage.OnStorageChange += OnLocalStorageChanged;
        }

        public override void OnCleanUp()
        {
            if (_storage != null)
                _storage.OnStorageChange -= OnLocalStorageChanged;

            base.OnCleanUp();
        }

        private void OnLocalStorageChanged(GameObject _)
        {
            _storageDirty = true;
        }

        private void Update()
        {
            if (isClient)
                return;

            if (!isServer || !inSession || _storage == null)
                return;

            if (!_storageDirty)
                return;

            _syncTimer += Time.unscaledDeltaTime;
            if (_syncTimer < STORAGE_SYNC_DELAY)
                return;

            _syncTimer = 0f;
            _storageDirty = false;
            _storageBlob = BuildingUtils.EncodeStorageToBytes(_storage);
        }

        private void OnStorageChanged(byte[] oldValue, byte[] newValue)
        {
            if (_storage == null || newValue == null)
                return;

            BuildingUtils.RebuildStorageFromBytes(_storage, newValue);
        }
    }
}
