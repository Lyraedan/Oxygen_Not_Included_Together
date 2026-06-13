using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Animation
{
	internal class MultiToolSyncPacket : IPacket
	{
		int WorkerNetId, WorkableNetId;
		string HitEffectPrefabId;
		HashedString Context;

		public MultiToolSyncPacket() { }
		public MultiToolSyncPacket(StandardWorker worker, MultitoolController.Instance smi)
		{
			using var _ = Profiler.Scope();

			WorkerNetId = worker.GetNetId();
			WorkableNetId = smi.workable?.GetNetId() ?? 0;
			HitEffectPrefabId = smi.hitEffectPrefab.PrefabID().ToString();
			Context = worker.GetComponent<AnimEventHandler>().context;
		}


		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(WorkerNetId);
			writer.Write(WorkableNetId);
			writer.Write(HitEffectPrefabId);
			writer.Write(Context.hash);
		}
		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			WorkerNetId = reader.ReadInt32();
			WorkableNetId = reader.ReadInt32();
			HitEffectPrefabId = reader.ReadString();
			Context = new HashedString(reader.ReadInt32());
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGetComponent<StandardWorker>(WorkerNetId, out var worker)
			|| !NetworkIdentityRegistry.TryGetComponent<Workable>(WorkableNetId, out var workable))
				return;

			var hiteffect = Assets.TryGetPrefab(HitEffectPrefabId);
			if (hiteffect == null)
			{
				DebugConsole.LogWarning("[MultiToolSyncPacket] " + HitEffectPrefabId + " was not found");
				return;
			}

			worker.smi = new MultitoolController.Instance(workable, worker, Context, hiteffect);
			worker.smi.StartSM();
			//DebugConsole.Log("[MultiToolSyncPacket] Started multitool smi for " + workable.name + " on worker " + worker.name + " with context " + Context);
		}
	}
}
