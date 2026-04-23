using System;
using HarmonyLib;
using ONI_MP.Networking;
using Shared.Profiling;

namespace ONI_MP.Patches.World
{
	[HarmonyPatch]
	public static class SaveLoaderPatch
	{

		[HarmonyPostfix]
		[HarmonyPatch(typeof(SaveLoader), nameof(SaveLoader.LoadFromWorldGen))]
		public static void Postfix_LoadFromWorldGen(bool __result)
		{
			using var _ = Profiler.Scope();

			if (__result)
				TryCreateLobbyAfterLoad("[Multiplayer] Lobby created after new world gen.");

		}

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveLoader),
					  nameof(SaveLoader.Load),
					  new Type[] { typeof(IReader) })]
        public static void Postfix(IReader reader, ref bool __result)
        {
	        using var _ = Profiler.Scope();

            // __result == true means the save loaded successfully
            if (!__result)
                return;

            OnSaveLoaded();
        }

        private static void OnSaveLoaded()
        {
	        using var _ = Profiler.Scope();

            // Your logic here
            TryCreateLobbyAfterLoad("[Multiplayer] Lobby created after world load.");
            if (MultiplayerSession.InSession)
            {
				//SpeedControlScreen.Instance?.Unpause(true); // Force pause the game
			}
            //ReadyManager.SendReadyStatusPacket(Networking.States.ClientReadyState.Ready);
        }

        private static void TryCreateLobbyAfterLoad(string logMessage)
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.ShouldHostAfterLoad)
			{
				MultiplayerSession.ShouldHostAfterLoad = false;

				NetworkConfig.StartServer();
			}
		}
	}
}

