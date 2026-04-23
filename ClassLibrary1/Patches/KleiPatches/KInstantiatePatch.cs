using HarmonyLib;
using ONI_MP.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Patches.KleiPatches
{
[HarmonyPatch(typeof(Util), nameof(Util.KInstantiate),
		new[] {
				typeof(GameObject),
				typeof(Vector3),
				typeof(Quaternion),
				typeof(GameObject),
				typeof(string),
				typeof(bool),
				typeof(int)
		})]
public static class KInstantiatePatch
{
	public static bool Prefix(GameObject original, Vector3 position, Quaternion rotation, GameObject parent, string name, bool initialize_id, int gameLayer)
	{
		using var _ = Profiler.Scope();

		if (MultiplayerSession.IsClient)
		{
			//DebugConsole.Log($"[MP] Blocked KInstantiate on client for prefab '{original?.name}'");
			return true; // Prevent instantiation
		}

		return true; // Allow host to instantiate
	}

	// Queue instantiation into batcher on host
	public static void Postfix(GameObject __result, GameObject original, Vector3 position, Quaternion rotation, GameObject parent, string name, bool initialize_id, int gameLayer)
	{
		using var _ = Profiler.Scope();

		if (__result == null || original == null)
			return;

		if (MultiplayerSession.IsHost)
		{
			/*
			var entry = new InstantiationsPacket.InstantiationEntry
			{
					PrefabName = original.name,
					Position = position,
					Rotation = rotation,
					ObjectName = name,
					InitializeId = initialize_id,
					GameLayer = gameLayer
			};

			InstantiationBatcher.Queue(entry);
			*/
		}
	}
}

}