using Database;
using Klei.AI;
using KSerialization;
using ONI_Together.Networking.Packets.World;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class StatusItemsSyncer : NetworkBehaviour
    {
        public enum StatusRecieverType
        {
            DUPLICANT,
            CREATURE,
            MISC,
            BUILDING,
            ROBOT
        }

        public StatusRecieverType recieverType = StatusRecieverType.MISC;

        [MyCmpGet]
        private KSelectable _selectable;

        [SyncVar(Hook = nameof(OnStatusItemsChanged), SendMode = (int)PacketSendMode.ReliableImmediate)]
        private byte[] _statusBlob;

        private float _syncTimer;

        private void Update()
        {
            if (!isServer || !inSession)
                return;

            _syncTimer += UnityEngine.Time.unscaledDeltaTime;
            if (_syncTimer < 0.5f)
                return;
            _syncTimer = 0f;

            if (_selectable == null)
                return;

            // Off-screen chunk entities receive a reliable full snapshot when a
            // player subscribes, so do not continually rebuild status strings.
            if (InterestGroup != -1 && InterestGroupManager.GetPlayersInGroup(InterestGroup).Count == 0)
                return;

            byte[] next = Encode(_selectable.GetStatusItemGroup());
            if (!ByteArraysEqual(_statusBlob, next))
                _statusBlob = next;
        }

        private void OnStatusItemsChanged(byte[] oldValue, byte[] newValue)
        {
			if (_selectable == null || _selectable.IsNullOrDestroyed() || newValue == null)
                return;

            Apply(Decode(newValue));
        }

        internal static byte[] Encode(StatusItemGroup group)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);

            var entries = new List<StatusItemGroup.Entry>();
            if (group != null)
            {
                foreach (var entry in group)
                {
                    if (entry.item == null)
                        continue;
                    entries.Add(entry);
                    if (entries.Count >= StatusItemsPacket.MaxEntries)
                        break;
                }
            }

            writer.Write((byte)entries.Count);
            foreach (var entry in entries)
            {
                writer.Write(entry.item.Id ?? string.Empty);
                writer.Write(entry.category?.Id ?? string.Empty);
                writer.Write(entry.GetName() ?? string.Empty);
                writer.Write(entry.item.GetTooltip(entry.data) ?? string.Empty);
            }

            return stream.ToArray();
        }

        internal static List<StatusItemEntry> Decode(byte[] blob)
        {
            var result = new List<StatusItemEntry>();
            if (blob == null || blob.Length == 0)
                return result;

            try
            {
                using var stream = new MemoryStream(blob, false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                int count = Math.Min((int)reader.ReadByte(), StatusItemsPacket.MaxEntries);
                for (int i = 0; i < count; i++)
                {
                    result.Add(new StatusItemEntry
                    {
                        ItemId = reader.ReadString(),
                        CategoryId = reader.ReadString(),
                        DisplayName = reader.ReadString(),
                        Tooltip = reader.ReadString(),
                    });
                }
            }
            catch (EndOfStreamException)
            {
                result.Clear();
            }

            return result;
        }

        private void Apply(List<StatusItemEntry> entries)
        {
            using var _ = Profiler.Scope();

            var group = _selectable.GetStatusItemGroup();
            if (group == null)
                return;

            var toRemove = new List<Guid>();
            foreach (var entry in group)
                toRemove.Add(entry.id);
            foreach (var guid in toRemove)
                group.RemoveStatusItem(guid, immediate: true);

            foreach (var entry in entries)
            {
                var syncedItem = BuildSyncedItem(entry);
                if (syncedItem == null)
                    continue;

                group.AddStatusItem(syncedItem, null, ResolveCategory(entry.CategoryId));
            }
        }

        private StatusItem BuildSyncedItem(StatusItemEntry entry)
        {
            if (string.IsNullOrEmpty(entry.ItemId))
                return null;

            StatusItem original = ResolveOriginal(entry.ItemId);
            if (original != null)
            {
                var item = new StatusItem(
                    "ONIT_Sync_" + entry.ItemId,
                    entry.DisplayName ?? original.Name,
                    entry.Tooltip ?? original.tooltipText,
                    original.iconName,
                    original.iconType,
                    original.notificationType,
                    original.allowMultiples,
                    original.render_overlay,
                    original.status_overlays,
                    original.showShowWorldIcon
                );
                item.sprite = original.sprite;
                item.showInHoverCardOnly = original.showInHoverCardOnly;
                return item;
            }

            var effect = Db.Get().effects.TryGet(entry.ItemId);
            return effect != null ? BuildFromEffect(entry, effect) : null;
        }

        private StatusItem ResolveOriginal(string itemId)
        {
            StatusItem preferred = recieverType switch
            {
                StatusRecieverType.DUPLICANT => Db.Get().DuplicantStatusItems.TryGet(itemId),
                StatusRecieverType.CREATURE => Db.Get().CreatureStatusItems.TryGet(itemId),
                StatusRecieverType.BUILDING => Db.Get().BuildingStatusItems.TryGet(itemId),
                StatusRecieverType.ROBOT => Db.Get().RobotStatusItems.TryGet(itemId),
                _ => Db.Get().MiscStatusItems.TryGet(itemId),
            };

            return preferred
                ?? Db.Get().MiscStatusItems.TryGet(itemId)
                ?? Db.Get().BuildingStatusItems.TryGet(itemId)
                ?? Db.Get().CreatureStatusItems.TryGet(itemId)
                ?? Db.Get().DuplicantStatusItems.TryGet(itemId)
                ?? Db.Get().RobotStatusItems.TryGet(itemId);
        }

        private static StatusItem BuildFromEffect(StatusItemEntry entry, Effect effect)
        {
            var iconType = effect.isBad ? StatusItem.IconType.Exclamation : StatusItem.IconType.Info;
            var notificationType = effect.isBad ? NotificationType.Bad : NotificationType.Neutral;
            string iconName = effect.isBad ? "status_item_exclamation" : "dash";

            if (!effect.customIcon.IsNullOrWhiteSpace())
            {
                iconType = StatusItem.IconType.Custom;
                iconName = effect.customIcon;
            }

            return new StatusItem(
                "ONIT_Sync_" + entry.ItemId,
                entry.DisplayName ?? effect.Name,
                entry.Tooltip ?? effect.description,
                iconName,
                iconType,
                notificationType,
                false,
                OverlayModes.None.ID,
                2,
                false
            );
        }

        private static StatusItemCategory ResolveCategory(string id)
        {
            return string.IsNullOrEmpty(id) ? null : Db.Get().StatusItemCategories.TryGet(id);
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }
    }
}
