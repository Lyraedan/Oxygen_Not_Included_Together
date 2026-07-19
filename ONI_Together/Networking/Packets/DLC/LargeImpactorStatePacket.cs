using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC
{
	public enum LargeImpactorPhase : byte
	{
		Alive,
		Landing,
		Destroyed
	}

	public class LargeImpactorStatePacket : IPacket, IHostOnlyPacket
	{
		private const int MaxHealth = 1_000_000;

		public int EventId;
		public int WorldId;
		public int Health;
		public bool HasArrived;
		public LargeImpactorPhase Phase;
		internal bool IsValid = true;

		public void Serialize(BinaryWriter writer)
		{
			writer.Write(EventId);
			writer.Write(WorldId);
			writer.Write(Health);
			writer.Write(HasArrived);
			writer.Write((byte)Phase);
		}

		public void Deserialize(BinaryReader reader)
		{
			EventId = reader.ReadInt32();
			WorldId = reader.ReadInt32();
			Health = reader.ReadInt32();
			HasArrived = reader.ReadBoolean();
			Phase = (LargeImpactorPhase)reader.ReadByte();
			IsValid = EventId != 0 && WorldId >= 0 && Health >= 0 && Health <= MaxHealth &&
			          (byte)Phase <= (byte)LargeImpactorPhase.Destroyed;
		}

		public void OnDispatched()
		{
			if (!IsValid || !ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				return;

			LargeImpactorStatus.Instance impactor = FindImpactor(EventId, WorldId);
			if (impactor == null)
			{
				DebugConsole.LogWarning($"[LargeImpactorStatePacket] Event {EventId} in world {WorldId} was not found");
				return;
			}

			LargeImpactorPhase currentPhase = GetPhase(impactor);
			bool currentHasArrived = impactor.sm.HasArrived.Get(impactor);
			if (!NeedsApply(impactor.Health, currentHasArrived, currentPhase, Health, HasArrived, Phase))
				return;

			if (impactor.Health != Health)
				impactor.sm.Health.Set(Health, impactor);
			if (currentHasArrived != HasArrived)
				impactor.sm.HasArrived.Set(HasArrived, impactor);

			ApplyPhase(impactor, Phase);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
		{
			return !localIsHost && senderIsHost;
		}

		internal static bool NeedsApply(
			int currentHealth,
			bool currentHasArrived,
			LargeImpactorPhase currentPhase,
			int targetHealth,
			bool targetHasArrived,
			LargeImpactorPhase targetPhase)
		{
			return currentHealth != targetHealth || currentHasArrived != targetHasArrived || currentPhase != targetPhase;
		}

		internal static LargeImpactorStatus.Instance FindImpactor(int eventId, int worldId)
		{
			GameplayEventInstance eventInstance = GameplayEventManager.Instance?.GetGameplayEventInstance(
				new HashedString(eventId), worldId);
			var eventSmi = eventInstance?.smi as LargeImpactorEvent.StatesInstance;
			return eventSmi?.impactorInstance?.GetSMI<LargeImpactorStatus.Instance>();
		}

		internal static LargeImpactorPhase GetPhase(LargeImpactorStatus.Instance impactor)
		{
			if (impactor.IsInsideState(impactor.sm.destroyed))
				return LargeImpactorPhase.Destroyed;
			if (impactor.IsInsideState(impactor.sm.landing))
				return LargeImpactorPhase.Landing;
			return LargeImpactorPhase.Alive;
		}

		private static void ApplyPhase(LargeImpactorStatus.Instance impactor, LargeImpactorPhase phase)
		{
			switch (phase)
			{
				case LargeImpactorPhase.Alive:
					if (!impactor.IsInsideState(impactor.sm.alive))
						impactor.GoTo(impactor.sm.alive);
					break;
				case LargeImpactorPhase.Landing:
					if (!impactor.IsInsideState(impactor.sm.landing))
						impactor.GoTo(impactor.sm.landing);
					break;
				case LargeImpactorPhase.Destroyed:
					if (!impactor.IsInsideState(impactor.sm.destroyed))
						impactor.GoTo(impactor.sm.destroyed);
					break;
			}
		}
	}
}
