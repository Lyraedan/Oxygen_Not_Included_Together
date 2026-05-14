using KSerialization;
using ONI_MP.DebugTools;
using System.IO;
using Shared.Profiling;

namespace ONI_MP.Networking.Components
{
	[SerializationConfig(MemberSerialization.OptIn)]
	public class NetworkIdentity : KMonoBehaviour
	{
		[Serialize]
		public int NetId = 0;

		[SkipSaveFileSerialization]
		private bool IsRegistered = false;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();
			RegisterIdentity();
		}

		public void RegisterIdentity()
		{
			using var _ = Profiler.Scope();

			if (IsRegistered)
				return;

			if (Grid.WidthInCells == 0)
			{
				// DebugConsole.LogWarning($"[NetworkIdentity] Skipping registration for {gameObject.name} - Grid not ready");
				return;
			}

			// Try to handle deterministic ID for buildings first
			if (NetId == 0)
			{
				if (TryGetComponent<Building>(out var building))
				{
					int detId = NetIdHelper.GetDeterministicBuildingId(gameObject);
					if (detId != 0)
					{
						NetId = detId;
						// DebugConsole.Log($"[NetworkIdentity] Generated Deterministic NetId {detId} for building {gameObject.name}");
					}
				}
				else if(TryGetComponent<Workable>(out var workable))
				{
					int detId = NetIdHelper.GetDeterministicWorkableId(gameObject);
					if (detId != 0)
					{
						NetId = detId;
					}
				}
				else
				{
					int detId = NetIdHelper.GetDeterministicEntityId(gameObject);
					if (detId != 0)
					{
						NetId = detId;
						// DebugConsole.Log($"[NetworkIdentity] Generated Deterministic NetId {detId} for building {gameObject.name}");
					}
				}
				//DebugConsole.Log($"[NetworkIdentity] Generated Deterministic NetId {NetId} for {gameObject.name}");
			}

			if (NetId == 0)
			{
				NetId = NetworkIdentityRegistry.Register(this);
				//DebugConsole.Log($"[NetworkIdentity] Generated Random NetId {NetId} for {gameObject.name}");
			}
			else
			{
				NetworkIdentityRegistry.RegisterExisting(this, NetId);
				// DebugConsole.Log($"[NetworkIdentity] Registered Existing NetId {NetId} for {gameObject.name}");
			}
			IsRegistered = true;
		}

		/// <summary>
		/// This will be primarily used when the host spawns in an object and the client and host need to sync the netid
		/// </summary>
		/// <param name="netIdOverride"></param>
		public void OverrideNetId(int netIdOverride)
		{
			using var _ = Profiler.Scope();

			// Unregister old NetId
			NetworkIdentityRegistry.Unregister(NetId);

			// Override internal value
			NetId = netIdOverride;

			// Re-register with new NetId
			NetworkIdentityRegistry.RegisterOverride(this, netIdOverride);

			//DebugConsole.Log($"[NetworkIdentity] Overridden NetId. New NetId = {NetId} for {gameObject.name}");
		}


		public override void OnCleanUp()
		{
			using var _ = Profiler.Scope();

			RemoteProgressRegistry.Clear(NetId);
			NetworkIdentityRegistry.Unregister(NetId);
			//DebugConsole.Log($"[NetworkIdentity] Unregistered NetId {NetId} for {gameObject.name}");
			base.OnCleanUp();
		}
	}
}
