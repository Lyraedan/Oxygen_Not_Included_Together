using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking;

namespace ONI_Together.Patches.World
{
    public class MeterScreenPatches
    {
        [HarmonyPatch(typeof(MeterScreen), nameof(MeterScreen.OnRedAlertClick))]
        public static class MeterScreen_RedAlertPatch
        {
            public static bool IsSyncing = false;

            [HarmonyPostfix]
            public static void Postfix()
            {
                if (IsSyncing) return;
                if (!MultiplayerSession.InActiveSession) return;

                bool state = ClusterManager.Instance.activeWorld.AlertManager.IsRedAlertToggledOn();
                var packet = new RedAlertStatePacket { 
                    ActiveWorldID = ClusterManager.Instance.activeWorldId,
                    IsRedAlert = state
                };
                PacketSender.SendToAllOtherPeers(packet);
            }
        }
    }
}
