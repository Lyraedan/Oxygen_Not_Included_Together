using Database;
using Klei.AI;
using KSerialization;
using ONI_Together.Networking.Components;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
    [SkipSaveFileSerialization]
    public class StatusItemsSyncComponent : NetworkBehaviour
    {
        public enum StatusRecieverType
        {
            DUPLICANT,
            CREATURE,
            MISC,
            BUILDING,
            ROBOT
        }

        public StatusRecieverType recieverType = StatusRecieverType.DUPLICANT;

        private const int MAX_ENTRIES = 12;
        private const char FIELD_SEP = '\x1F';

        [MyCmpGet]
        private KSelectable _selectable;

        [SyncVar] private string _e00, _e01, _e02, _e03;
        [SyncVar] private string _e04, _e05, _e06, _e07;
        [SyncVar] private string _e08, _e09, _e10, _e11;

        [SyncVar] private int _entriesCount;

        [SyncVar(Hook = nameof(OnStatusItemsChanged))]
        private int _version;

        private float _syncTimer;

        public override void OnSpawn()
        {
            base.OnSpawn();
        }

        private void Update()
        {
            if (!isServer) return;
            ServerSync();
        }

        [Server]
        private void ServerSync()
        {
            _syncTimer += Time.unscaledDeltaTime;
            if (_syncTimer < 0.5f) return;
            _syncTimer = 0f;

            if (_selectable == null) return;

            var group = _selectable.GetStatusItemGroup();
            int idx = 0;
            if (group != null)
            {
                foreach (var entry in group)
                {
                    if (idx >= MAX_ENTRIES) break;
                    SetSlot(idx, Pack(entry));
                    idx++;
                }
            }

            for (int i = idx; i < MAX_ENTRIES; i++)
                SetSlot(i, "");

            _entriesCount = idx;
            _version++;
            MarkAllDirty();
        }

        private void OnStatusItemsChanged(int oldVersion, int newVersion)
        {
            if (_selectable.IsNullOrDestroyed()) return;
            Apply();
        }

        private void Apply()
        {
            using var _ = Profiler.Scope();

            var group = _selectable.GetStatusItemGroup();
            if (group == null) return;

            var toRemove = new List<Guid>();
            foreach (var entry in group)
                toRemove.Add(entry.id);
            foreach (var guid in toRemove)
                group.RemoveStatusItem(guid, immediate: true);

            for (int i = 0; i < _entriesCount; i++)
            {
                var packed = GetSlot(i);
                if (string.IsNullOrEmpty(packed)) continue;

                var parsed = Unpack(packed);
                if (parsed == null) continue;

                var syncedItem = BuildSyncedItem(
                    parsed.Value.ItemId,
                    parsed.Value.CategoryId,
                    parsed.Value.DisplayName,
                    parsed.Value.Tooltip
                );
                if (syncedItem == null) continue;

                var category = ResolveCategory(parsed.Value.CategoryId);
                group.AddStatusItem(syncedItem, null, category);
            }
        }

        private static string Pack(StatusItemGroup.Entry entry)
        {
            var item = entry.item;
            if (item == null) return "";
            return item.Id + FIELD_SEP +
                   (entry.category?.Id ?? "") + FIELD_SEP +
                   entry.GetName() + FIELD_SEP +
                   item.GetTooltip(entry.data);
        }

        private static (string ItemId, string CategoryId, string DisplayName, string Tooltip)? Unpack(string packed)
        {
            if (string.IsNullOrEmpty(packed)) return null;
            var parts = packed.Split(FIELD_SEP);
            if (parts.Length < 4) return null;
            return (parts[0], parts[1], parts[2], parts[3]);
        }

        private string GetSlot(int i) => i switch
        {
            0 => _e00, 1 => _e01, 2 => _e02, 3 => _e03,
            4 => _e04, 5 => _e05, 6 => _e06, 7 => _e07,
            8 => _e08, 9 => _e09, 10 => _e10, 11 => _e11,
            _ => "",
        };

        private void SetSlot(int i, string value)
        {
            switch (i)
            {
                case 0:  _e00 = value; break;
                case 1:  _e01 = value; break;
                case 2:  _e02 = value; break;
                case 3:  _e03 = value; break;
                case 4:  _e04 = value; break;
                case 5:  _e05 = value; break;
                case 6:  _e06 = value; break;
                case 7:  _e07 = value; break;
                case 8:  _e08 = value; break;
                case 9:  _e09 = value; break;
                case 10: _e10 = value; break;
                case 11: _e11 = value; break;
            }
        }

        private void ClearAllSlots()
        {
            _e00 = _e01 = _e02 = _e03 = "";
            _e04 = _e05 = _e06 = _e07 = "";
            _e08 = _e09 = _e10 = _e11 = "";
        }

        private StatusItem BuildSyncedItem(string itemId, string categoryId, string displayName, string tooltip)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            StatusItem original = recieverType switch
            {
                StatusRecieverType.DUPLICANT => Db.Get().DuplicantStatusItems.TryGet(itemId),
                StatusRecieverType.CREATURE => Db.Get().CreatureStatusItems.TryGet(itemId),
                StatusRecieverType.MISC => Db.Get().MiscStatusItems.TryGet(itemId),
                StatusRecieverType.BUILDING => Db.Get().BuildingStatusItems.TryGet(itemId),
                StatusRecieverType.ROBOT => Db.Get().RobotStatusItems.TryGet(itemId),
                _ => Db.Get().DuplicantStatusItems.TryGet(itemId),
            };

            if (original != null)
            {
                var item = new StatusItem(
                    "ONIT_Sync_" + itemId,
                    displayName ?? original.Name,
                    tooltip ?? original.tooltipText,
                    original.iconName,
                    original.iconType,
                    original.notificationType,
                    false,
                    original.render_overlay,
                    original.status_overlays,
                    false
                );
                item.sprite = original.sprite;
                item.showInHoverCardOnly = original.showInHoverCardOnly;
                return item;
            }

            var effect = Db.Get().effects.TryGet(itemId);
            if (effect != null)
                return BuildFromEffect(itemId, displayName, tooltip, effect);

            return null;
        }

        private static StatusItem BuildFromEffect(string itemId, string displayName, string tooltip, Effect effect)
        {
            var iconType = StatusItem.IconType.Info;
            var notifType = NotificationType.Neutral;
            var iconName = "dash";

            if (effect.isBad)
            {
                iconType = StatusItem.IconType.Exclamation;
                notifType = NotificationType.Bad;
                iconName = "status_item_exclamation";
            }

            if (!effect.customIcon.IsNullOrWhiteSpace())
            {
                iconType = StatusItem.IconType.Custom;
                iconName = effect.customIcon;
            }

            return new StatusItem(
                "ONIT_Sync_" + itemId,
                displayName ?? effect.Name,
                tooltip ?? effect.description,
                iconName,
                iconType,
                notifType,
                false,
                OverlayModes.None.ID,
                2,
                false
            );
        }

        private static StatusItemCategory ResolveCategory(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Db.Get().StatusItemCategories.TryGet(id);
        }
    }
}
