using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DuplicantActions;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.DuplicantActions
{
    [HarmonyPatch(typeof(ConsumablesTableScreen), nameof(ConsumablesTableScreen.set_value_consumable_info))]
    public static class ConsumablesTableScreenPatch
    {
        public static void Postfix(GameObject widget_go, TableScreen.ResultValues new_value, ConsumablesTableScreen __instance)
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.InSession)
                return;


            TableRow                  widgetRow    = __instance.GetWidgetRow(widget_go);
            ConsumableInfoTableColumn widgetColumn = __instance.GetWidgetColumn(widget_go) as ConsumableInfoTableColumn;
            if (widgetRow == null || widgetColumn == null)
                return;

            IConsumableUIItem consumableInfo = widgetColumn.consumable_info;
            MinionIdentity    minionIdentity = widgetRow.GetIdentity() as MinionIdentity;
            NetworkIdentity   identity       = minionIdentity?.GetComponent<NetworkIdentity>();

            int id = -1;
            if (identity != null)
                id = identity.NetId;

            PacketSender.SendToAllOtherPeers(new ConsumablePermissionPacket(widgetRow.rowType, consumableInfo.ConsumableId, new_value, id));
        }
    }
}