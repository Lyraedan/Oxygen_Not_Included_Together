using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using TemplateClasses;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	public class WorldDamageSpawnResourcePacket : IPacket, IHostOnlyPacket
	{
		public int NetId;
		public Vector3 Position;
		public float Mass;
		public float Temperature;
		public ushort ElementIndex;
		public byte DiseaseIndex;
		public int DiseaseCount;

		public WorldDamageSpawnResourcePacket() { }

		public WorldDamageSpawnResourcePacket(int netId, Vector3 pos, float mass, float temp, ushort elementIdx, byte diseaseIdx, int diseaseCount)
		{
			using var _ = Profiler.Scope();

			NetId = netId;
			Position = pos;
			Mass = mass;
			Temperature = temp;
			ElementIndex = elementIdx;
			DiseaseIndex = diseaseIdx;
			DiseaseCount = diseaseCount;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(Position.x);
			writer.Write(Position.y);
			writer.Write(Position.z);
			writer.Write(Mass);
			writer.Write(Temperature);
			writer.Write(ElementIndex);
			writer.Write(DiseaseIndex);
			writer.Write(DiseaseCount);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			Position = new Vector3(
					reader.ReadSingle(),
					reader.ReadSingle(),
					reader.ReadSingle()
			);
			Mass = reader.ReadSingle();
			Temperature = reader.ReadSingle();
			ElementIndex = reader.ReadUInt16();
			DiseaseIndex = reader.ReadByte();
			DiseaseCount = reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (MultiplayerSession.IsHost || !PacketHandler.CurrentContext.SenderIsHost)
				return;
			NetworkIdentityRegistry.ReleaseUnavailableRegistration(NetId);
			if (!ShouldApply(
				    localIsHost: false, senderIsHost: true,
				    entityExists: NetworkIdentityRegistry.Exists(NetId),
				    lifecycleTombstoned: NetworkIdentityRegistry.IsLifecycleTombstoned(NetId)))
				return;

			Element element = ElementLoader.elements[ElementIndex];

			InvokePlaySoundForSubstance(element, Position);

			float dropMass = Mass;
			if (dropMass <= 0f)
				return;

			GameObject dropped = element.substance.SpawnResource(Position, dropMass, Temperature, DiseaseIndex, DiseaseCount);
			NetworkIdentity identity = dropped.AddOrGet<NetworkIdentity>();
			if (!identity.OverrideNetId(NetId))
			{
				Util.KDestroyGameObject(dropped);
				return;
			}
			DebugConsole.Log("[WorldDamageSpawnResourcePacket] Synchronized Network ID");

            if (GroundItemPickedUpPacket.TryConsumePending(NetId))
            {
                DebugConsole.Log($"[WorldDamageSpawnResourcePacket] Consumed pending ground-item pickup for NetId {NetId}");
                Util.KDestroyGameObject(dropped);
                return;
            }

			StorageItemPacket.TryApplyPending(NetId, dropped);

			Pickupable pickup = dropped.GetComponent<Pickupable>();
			if (pickup != null && pickup.GetMyWorld()?.worldInventory.IsReachable(pickup) == true)
			{
				PopFXManager.Instance.SpawnFX(
						PopFXManager.Instance.sprite_Resource,
						Mathf.RoundToInt(dropMass) + " " + element.name,
						dropped.transform
				);
			}
		}

		internal static bool ShouldApply(
			bool localIsHost, bool senderIsHost, bool entityExists, bool lifecycleTombstoned)
			=> !localIsHost && senderIsHost && !entityExists && !lifecycleTombstoned;

		private static void InvokePlaySoundForSubstance(Element element, Vector3 position)
		{
			using var _ = Profiler.Scope();

			var method = typeof(WorldDamage).GetMethod("PlaySoundForSubstance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

			if (method == null)
			{
				DebugConsole.LogWarning("[Multiplayer] Could not find PlaySoundForSubstance via reflection.");
				return;
			}

			var worldDamage = WorldDamage.Instance;

			if (worldDamage == null)
			{
				DebugConsole.LogWarning("[Multiplayer] WorldDamage.Instance is null.");
				return;
			}

			method.Invoke(worldDamage, new object[] { element, position });
		}

	}
}
