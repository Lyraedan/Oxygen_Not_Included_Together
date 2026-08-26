using ONI_Together.Networking;
using Shared.OxySync;

namespace ONI_Together.Networking.OxySync.Packets
{
	internal static class OxySyncDispatchResolver
	{
		internal static NetworkBehaviour FindSyncVarBehaviour(int netId, int fieldHash)
		{
			if (!NetworkIdentityRegistry.TryGet(netId, out var identity))
				return null;

			var behaviours = identity.GetComponents<NetworkBehaviour>();
			foreach (var behaviour in behaviours)
			{
				var fields = behaviour.SyncVarFields;
				for (int i = 0; i < fields.Count; i++)
				{
					if (fields[i].Hash == fieldHash)
						return behaviour;
				}
			}

			return null;
		}

		internal static NetworkBehaviour FindCommandBehaviour(int netId, int methodHash)
		{
			if (!NetworkIdentityRegistry.TryGet(netId, out var identity))
				return null;

			foreach (var behaviour in identity.GetComponents<NetworkBehaviour>())
			{
				if (behaviour.Commands.ContainsKey(methodHash))
					return behaviour;
			}

			return null;
		}

		internal static NetworkBehaviour FindClientRpcBehaviour(int netId, int methodHash)
		{
			if (!NetworkIdentityRegistry.TryGet(netId, out var identity))
				return null;

			foreach (var behaviour in identity.GetComponents<NetworkBehaviour>())
			{
				if (behaviour.ClientRpcs.ContainsKey(methodHash))
					return behaviour;
			}

			return null;
		}

		internal static NetworkBehaviour FindTargetRpcBehaviour(int netId, int methodHash)
		{
			if (!NetworkIdentityRegistry.TryGet(netId, out var identity))
				return null;

			foreach (var behaviour in identity.GetComponents<NetworkBehaviour>())
			{
				if (behaviour.TargetRpcs.ContainsKey(methodHash))
					return behaviour;
			}

			return null;
		}
	}
}
