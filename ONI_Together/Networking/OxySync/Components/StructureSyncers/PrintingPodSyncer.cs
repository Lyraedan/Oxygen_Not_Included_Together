using System;
using System.Collections.Generic;
using System.IO;
using KSerialization;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Patches.GamePatches;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    [FixedInterestGroup]
    public class PrintingPodSyncer : NetworkBehaviour
    {
        public static PrintingPodSyncer? Instance { get; private set; }

        private Telepad _telepad;

        [SyncVar(Hook = nameof(OnTimeBeforeSpawnChanged))]
        private float _timeBeforeSpawn;

        public override void OnSpawn()
        {
            base.OnSpawn();
            Instance = this;
            _telepad = GetComponent<Telepad>();
            SyncInterval = 0.2f;
            NetId = (nameof(Telepad) + "_" + gameObject.GetMyWorldId()).GetHashCode();
            InterestGroup = -1;
        }

        public override void OnCleanUp()
        {
            if (Instance == this)
                Instance = null;
            base.OnCleanUp();
        }

        public void RequestBroadcastOptions(byte[] optionsBlob, int worldIndex)
        {
            CallCommand(nameof(CmdBroadcastOptions), optionsBlob, worldIndex);
        }

        public void RequestMakeSelection(byte[] selectionBlob, int worldIndex)
        {
            CallCommand(nameof(CmdMakeSelection), selectionBlob, worldIndex);
        }

        public void RequestRejectAll(int worldIndex)
        {
            CallCommand(nameof(CmdRejectAll), worldIndex);
        }

        public void BroadcastCloseScreen(int reason)
        {
            CallClientRpc(nameof(RpcCloseScreen), reason);
        }

        private void Update()
        {
            if (!isServer || !inSession) return;
            if (Immigration.Instance == null) return;

            float current = Immigration.Instance.GetTimeRemaining();
            if (Math.Abs(current - _timeBeforeSpawn) > 0.001f)
                _timeBeforeSpawn = current;
        }

        private void OnTimeBeforeSpawnChanged(float oldValue, float newValue)
        {
            if (Immigration.Instance != null)
                Immigration.Instance.timeBeforeSpawn = newValue;
        }

        [Command(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void CmdBroadcastOptions(byte[] optionsBlob, int worldIndex)
        {
            if (ImmigrantScreenPatch.OptionsLocked) return;

            var options = DeserializeOptionsBlob(optionsBlob);
            if (options == null || options.Count == 0) return;

            ImmigrantScreenPatch.AvailableOptions = options;
            ImmigrantScreenPatch.OptionsLocked = true;

            DebugConsole.Log($"[PrintingPodSync] Server: Locked options, broadcasting {options.Count} options to clients");

            CallClientRpc(nameof(RpcOptionsReceived), optionsBlob);
        }

        [ClientRpc(InterestGroup = -1, SendMode = (int)PacketSendMode.Reliable)]
        private void RpcOptionsReceived(byte[] optionsBlob)
        {
            if (ImmigrantScreenPatch.OptionsLocked) return;

            var options = DeserializeOptionsBlob(optionsBlob);
            if (options == null || options.Count == 0) return;

            ImmigrantScreenPatch.AvailableOptions = options;
            ImmigrantScreenPatch.OptionsLocked = true;

            DebugConsole.Log($"[PrintingPodSync] Client: Received {options.Count} options, applying to screen");

            if (ImmigrantScreen.instance != null
                && ImmigrantScreen.instance.gameObject.activeInHierarchy)
                ImmigrantScreenPatch.ApplyOptionsToScreen(ImmigrantScreen.instance);
        }

        [Command(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void CmdMakeSelection(byte[] selectionBlob, int worldIndex)
        {
            var opt = DeserializeSelectionBlob(selectionBlob);
            if (opt.EntryType < 0) return;

            Telepad telepad = FindTelepad(worldIndex);
            if (telepad == null) return;

            var deliverable = opt.ToGameDeliverable();
            if (deliverable == null) return;

            telepad.OnAcceptDelivery(deliverable);

            ImmigrantScreenPatch.ClearOptionsLock();
            CallClientRpc(nameof(RpcCloseScreen), 0);
        }

        [Command(SendMode = (int)PacketSendMode.ReliableImmediate)]
        private void CmdRejectAll(int worldIndex)
        {
            Telepad telepad = FindTelepad(worldIndex);
            if (telepad == null) return;

            telepad.RejectAll();

            ImmigrantScreenPatch.ClearOptionsLock();
            CallClientRpc(nameof(RpcCloseScreen), -1);
        }

        [ClientRpc(InterestGroup = -1, SendMode = (int)PacketSendMode.Reliable)]
        private void RpcCloseScreen(int reason)
        {
            ImmigrantScreenPatch.ClearOptionsLock();

            if (ImmigrantScreen.instance != null
                && ImmigrantScreen.instance.gameObject.activeInHierarchy)
                ImmigrantScreen.instance.Deactivate();

            if (Immigration.Instance != null)
                Immigration.Instance.EndImmigration();
        }

        private static Telepad FindTelepad(int worldIndex)
        {
            foreach (Telepad existing in global::Components.Telepads)
                if (existing.GetMyWorldId() == worldIndex)
                    return existing;
            return null;
        }

        public static byte[] SerializeOptionsBlob(List<ImmigrantOptionEntry> options)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(options.Count);
            foreach (var opt in options)
                opt.Serialize(writer);
            return ms.ToArray();
        }

        public static List<ImmigrantOptionEntry> DeserializeOptionsBlob(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
            int count = reader.ReadInt32();
            var result = new List<ImmigrantOptionEntry>(count);
            for (int i = 0; i < count; i++)
                result.Add(ImmigrantOptionEntry.Deserialize(reader));
            return result;
        }

        public static byte[] SerializeSelectionBlob(ImmigrantOptionEntry option)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            option.Serialize(writer);
            return ms.ToArray();
        }

        public static ImmigrantOptionEntry DeserializeSelectionBlob(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
            return ImmigrantOptionEntry.Deserialize(reader);
        }
    }
}
