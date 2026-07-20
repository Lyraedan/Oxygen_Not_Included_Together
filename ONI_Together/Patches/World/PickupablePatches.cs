using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	public static class PickupablePatches
	{
        [HarmonyPatch(typeof(Pickupable), nameof(Pickupable.Take))]
        public static class PickupableTakePatch
        {
            public static void Postfix(Pickupable __instance)
            {
                using var _ = Profiler.Scope();
                try
                {
                    if (!MultiplayerSession.IsHost || !MultiplayerSession.InActiveSession)
                        return;

                    var identity = __instance.GetNetIdentity();
                    if (identity == null || identity.NetId == 0)
                        return;
                    PacketSender.SendToAllClients(new PickupItemPacket { NetId = identity.NetId });
                }
                catch (System.Exception ex)
                {
                    DebugConsole.LogError($"[PickupableTakePatch] Exception: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(Pickupable), nameof(Pickupable.TakeUnit))]
        public static class PickupableTakeUnitPatch
        {
            public static void Postfix(Pickupable __instance)
            {
                using var _ = Profiler.Scope();
                try
                {
                    if (!MultiplayerSession.IsHost || !MultiplayerSession.InActiveSession)
                        return;

                    var identity = __instance.GetNetIdentity();
                    if (identity == null || identity.NetId == 0)
                        return;
                    PacketSender.SendToAllClients(new PickupItemPacket { NetId = identity.NetId });
                }
                catch (System.Exception ex)
                {
                    DebugConsole.LogError($"[PickupableTakePatch] Exception: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(Pickupable), nameof(Pickupable.OnCleanUp))]
        public static class PickupableCleanedUpPatch
        {
            private static long _skipCount;

            public static void Postfix(Pickupable __instance)
            {
                using var _ = Profiler.Scope();
                try
                {
                    if (__instance == null)
                        return;

                    if (!MultiplayerSession.IsHost || !MultiplayerSession.InActiveSession)
                        return;

                    var identity = __instance.GetNetIdentity();
                    if (identity == null || identity.NetId == 0)
                    {
                        long n = ++_skipCount;
                        if (n <= 5 || n % 100 == 0)
                        {
                            string name = __instance != null && __instance.gameObject != null ? __instance.gameObject.name : "<null>";
                            DebugConsole.Log($"[GroundPickup] skip NetId=0 name={name} #{n}");
                        }
                        return;
                    }

                    PacketSender.SendToAllClients(new GroundItemPickedUpPacket { NetId = identity.NetId });
                    //PacketSender.SendToAllClients(new PickupItemPacket { NetId = identity.NetId }); // Display FX for object
                }
                catch (System.Exception ex)
                {
                    DebugConsole.LogError($"[PickupableCleanedUpPatch] Exception: {ex}");
                }
            }
        }
    }
}
