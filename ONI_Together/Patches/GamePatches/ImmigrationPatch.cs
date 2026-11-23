using HarmonyLib;

namespace ONI_Together.Patches.GamePatches
{
    [HarmonyPatch(typeof(Immigration), nameof(Immigration.EndImmigration))]
    public static class ImmigrationEndPatch
    {
        public static void Postfix()
        {
            ImmigrantScreenPatch.ClearOptionsLock();
        }
    }

    [HarmonyPatch(typeof(Telepad), nameof(Telepad.OnAcceptDelivery))]
    public static class TelepadAcceptDeliveryPatch
    {
        public static void Postfix()
        {
            ImmigrantScreenPatch.ClearOptionsLock();
        }
    }

    [HarmonyPatch(typeof(Telepad), nameof(Telepad.RejectAll))]
    public static class TelepadRejectAllPatch
    {
        public static void Postfix()
        {
            ImmigrantScreenPatch.ClearOptionsLock();
        }
    }
}
