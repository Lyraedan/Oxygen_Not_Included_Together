using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Patches.GamePatches
{
    public class StatusItemGroupsPatches
    {
        [HarmonyPatch(typeof(StatusItemGroup), nameof(StatusItemGroup.AddStatusItem))]
        public static class StatusItemGroup_AddStatusItem_Patch
        {
            public static void Postfix(StatusItemGroup __instance, StatusItem item, object data, StatusItemCategory category, Guid __result)
            {
                // Disabled for now
                return;

                if (__instance.IsNullOrDestroyed())
                    return;

                if (!MultiplayerSession.InSession) return;
                if (MultiplayerSession.IsClient) return; // Do nothing on clients

                if (__result == Guid.Empty)
                    return;

                DebugConsole.Log($"Adding status item: {item.Id}");

                int netId = __instance.gameObject.GetNetIdentity().NetId;
                if (netId == 0)
                    return;

                StatusItemGroupPacket packet = new StatusItemGroupPacket()
                {
                    NetId = netId,
                    StatusItemId = item.Id,
                    Action = StatusItemGroupPacket.ItemGroupPacketAction.Add
                };
                PacketSender.SendToAllClients(packet);
            }
        }

        [HarmonyPatch(typeof(StatusItemGroup), nameof(StatusItemGroup.RemoveStatusItemInternal))]
        public static class StatusItemGroup_RemoveStatusItemInternal_Patch
        {
            private static StatusItemGroup.Entry? _removedEntry;

            public static bool Prefix(StatusItemGroup __instance, int itemIdx)
            {
                // Disabled for now
                return true;

                // Capture the entry BEFORE it is removed
                if (itemIdx >= 0 && itemIdx < __instance.items.Count)
                {
                    _removedEntry = __instance.items[itemIdx];
                }
                else
                {
                    _removedEntry = null;
                }
                return true;
            }

            public static void Postfix(StatusItemGroup __instance, Guid guid, bool immediate)
            {
                // Disabled for now
                return;

                if (__instance.IsNullOrDestroyed())
                    return;

                if (!MultiplayerSession.InSession) return;
                if (MultiplayerSession.IsClient) return; // Do nothing on clients

                if (!_removedEntry.HasValue || _removedEntry == null)
                    return;

                DebugConsole.Log($"Removed StatusItem {_removedEntry.Value.item.Id} from {__instance.gameObject.name} (immediate={immediate})");
                
                int netId = __instance.gameObject.GetNetIdentity().NetId;
                if (netId == 0)
                    return;

                StatusItemGroupPacket packet = new StatusItemGroupPacket()
                {
                    NetId = netId,
                    StatusItemId = _removedEntry.Value.item.Id,
                    Action = StatusItemGroupPacket.ItemGroupPacketAction.Remove,
                    Immediate = immediate
                };
                PacketSender.SendToAllClients(packet);
                _removedEntry = null; // cleanup
            }
        }
    }
}
