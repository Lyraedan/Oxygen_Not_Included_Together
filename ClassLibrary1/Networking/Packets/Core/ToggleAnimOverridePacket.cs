using Klei.AI;
using ONI_MP.DebugTools;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Patches.KleiPatches;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking;

namespace ONI_MP.Networking.Packets.Core
{
	internal class ToggleAnimOverridePacket : IPacket
	{
		public int EntityNetId;
		public string Kanim;
		public bool AddingOverride;
		public float Priority = 0f;

		public ToggleAnimOverridePacket() { }
		public ToggleAnimOverridePacket(GameObject instance, KAnimFile kanim)
		{
			using var _ = Profiler.Scope();

			EntityNetId = instance.AddOrGet<NetworkIdentity>().NetId;
			AddingOverride = false;
			Kanim = kanim.name;
		}
		public ToggleAnimOverridePacket(GameObject instance, KAnimFile kanim, float priority)
		{
			using var _ = Profiler.Scope();

			EntityNetId = instance.AddOrGet<NetworkIdentity>().NetId;
			AddingOverride = true;
			Kanim = kanim.name;
			Priority = priority;
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			EntityNetId = reader.ReadInt32();
			Kanim = reader.ReadString();
			AddingOverride = reader.ReadBoolean();
			Priority = reader.ReadSingle();
		}
		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(EntityNetId);
			writer.Write(Kanim);
			writer.Write(AddingOverride);
			writer.Write(Priority);
		}
		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGet(EntityNetId, out var networkEntity))
			{
				DebugConsole.LogWarning("Could not find entity with net id " + EntityNetId + " to toggle AnimationOverride " + Kanim + " to " + (AddingOverride ? "on" : "off"));
			}
			if (!networkEntity.TryGetComponent<KAnimControllerBase>(out var kbac))
			{
				DebugConsole.LogWarning("Could not find KAnimControllerBaseon entity " + networkEntity.gameObject.GetProperName());
			}
			if (AddingOverride)
			{
				KAnimControllerBase_Patches.AddKanimOverride(kbac, Kanim, Priority);
			}
			else
			{
				KAnimControllerBase_Patches.RemoveKanimOverride(kbac, Kanim);
			}
		}
	}
}
