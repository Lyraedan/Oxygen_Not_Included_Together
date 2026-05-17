using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Steamworks;
using Shared.Profiling;
using Steamworks;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	public class NetworkingComponent : MonoBehaviour
	{
		public static UnityTaskScheduler scheduler = new UnityTaskScheduler();

		/*
		 * TODO:
		 * Update this class now that we can have different relay types. This is not steam specific anymore
		 *
		 * **/

		private void Start()
		{
			//SteamNetworkingUtils.InitRelayNetworkAccess();
			//GameClient.Init();

			// NOTE: Client reconnection after world load is now handled in
			// GamePatch.OnSpawnPostfix which triggers AFTER the world is fully loaded.
			// This is safer than OnPostSceneLoaded which fires during scene unload.
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			scheduler.Tick();

			if (NetworkConfig.transport.Equals(NetworkConfig.NetworkTransport.STEAMWORKS))
			{
				if (!SteamManager.Initialized)
					return;
			}

			//if (!MultiplayerSession.InSession)
			//	return;

            if (MultiplayerSession.IsHost)
			{
				GameServer.Update();
			}
			else if (MultiplayerSession.IsClient && MultiplayerSession.HostUserID.IsValid())
			{
				GameClient.Poll();

				// Check for inactive transfers and request missing chunks
				ONI_Together.Misc.World.SaveChunkAssembler.CheckInactiveTransfers();
			}
        }

        private void OnApplicationQuit()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession)
				return;

			NetworkConfig.Stop();
		}
	}
}
