using System;
using System.IO;
using Klei.AI;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.Duplicant;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.DuplicantActions
{
	internal class ToggleEffectPacket : IPacket, IBulkablePacket, IHostOnlyPacket
	{
		internal const int MaxEffectIdLength = 128;
		internal const float MaxAbsTimeRemaining = 1_000_000_000f;
		public int MinionNetId;
		public string EffectId;
		public bool IsAdding;
		public bool ShouldSave;
		public float TimeRemaining;

		public int MaxPackSize => 500;
		public uint IntervalMs => 50;

		public ToggleEffectPacket() { }

		public ToggleEffectPacket(NetworkIdentity identity, HashedString toRemove)
		{
			MinionNetId = identity?.NetId ?? 0;
			EffectId = toRemove.ToString();
		}

		public ToggleEffectPacket(NetworkIdentity identity, EffectInstance toAdd)
		{
			MinionNetId = identity?.NetId ?? 0;
			IsAdding = true;
			EffectId = toAdd?.effect?.Id;
			ShouldSave = toAdd?.shouldSave ?? false;
			TimeRemaining = toAdd?.timeRemaining ?? 0f;
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			MinionNetId = reader.ReadInt32();
			EffectId = reader.ReadString();
			IsAdding = reader.ReadBoolean();
			ShouldSave = reader.ReadBoolean();
			TimeRemaining = reader.ReadSingle();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid entity effect packet");
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid entity effect packet");
			writer.Write(MinionNetId);
			writer.Write(EffectId);
			writer.Write(IsAdding);
			writer.Write(ShouldSave);
			writer.Write(TimeRemaining);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				return;
			if (!NetworkIdentityRegistry.TryGet(MinionNetId, out var identity))
			{
				DebugConsole.LogWarning($"Could not find entity {MinionNetId} for effect {EffectId}");
				return;
			}
			if (!identity.TryGetComponent(out Effects effects))
			{
				DebugConsole.LogWarning($"Could not find Effects on entity {MinionNetId}");
				return;
			}
			if (IsAdding)
				EffectsPatch.AddEffect(effects, EffectId, ShouldSave, TimeRemaining);
			else
				EffectsPatch.RemoveEffect(effects, EffectId);
		}

		internal bool IsWireValid()
			=> MinionNetId != 0 && !string.IsNullOrEmpty(EffectId) &&
			   EffectId.Length <= MaxEffectIdLength && IsValidTime(TimeRemaining) &&
			   (IsAdding || (TimeRemaining == 0f && !ShouldSave));

		private static bool IsValidTime(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value) &&
			   Math.Abs(value) <= MaxAbsTimeRemaining;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
