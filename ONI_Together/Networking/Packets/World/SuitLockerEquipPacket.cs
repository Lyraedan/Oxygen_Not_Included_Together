using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.World
{
	/// <summary>
	/// A duplicant took its suit from a suit locker (SuitLocker.EquipTo) or returned it (UnequipFrom) on the host.
	/// Clients do not walk their dupes through the checkpoint, so the reactables that do this never run there.
	/// </summary>
	public class SuitLockerEquipPacket : IPacket
	{
		public int LockerNetId;
		public int MinionNetId;
		public bool Equip;

		/// <summary>
		/// Above zero while the host runs EquipTo / UnequipFrom. The storage moves and the (un)assignment
		/// inside are covered by this packet: the client replays the whole call, in the same order.
		/// </summary>
		public static int InHostCall { get; private set; }
		private static bool _assignmentWasApplying;

		public static void BeginHostCall()
		{
			if (InHostCall++ == 0)
			{
				_assignmentWasApplying = AssignmentPacket.IsApplying;
				AssignmentPacket.IsApplying = true; // its patches send nothing while this is set
			}
		}

		public static void EndHostCall()
		{
			if (--InHostCall == 0)
				AssignmentPacket.IsApplying = _assignmentWasApplying;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(LockerNetId);
			writer.Write(MinionNetId);
			writer.Write(Equip);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			LockerNetId = reader.ReadInt32();
			MinionNetId = reader.ReadInt32();
			Equip = reader.ReadBoolean();
		}

		public static void Send(SuitLocker locker, Equipment equipment, bool equip)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHostInSession || locker == null || equipment == null)
				return;

			var minion = equipment.GetTargetGameObject();
			var minionIdentity = minion != null ? minion.GetComponent<NetworkIdentity>() : null;
			if (minionIdentity == null || minionIdentity.NetId == 0)
				return;

			var lockerIdentity = locker.gameObject.AddOrGet<NetworkIdentity>();
			lockerIdentity.RegisterIdentity();

			PacketSender.SendToAllClients(new SuitLockerEquipPacket
			{
				LockerNetId = lockerIdentity.NetId,
				MinionNetId = minionIdentity.NetId,
				Equip = equip
			});
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGetComponent<SuitLocker>(LockerNetId, out var locker) || locker == null
				|| !NetworkIdentityRegistry.TryGetComponent<MinionIdentity>(MinionNetId, out var minion) || minion == null)
			{
				DebugConsole.LogWarning($"[SuitLockerEquipPacket] Locker {LockerNetId} or dupe {MinionNetId} not found");
				return;
			}

			var equipment = minion.GetEquipment();
			if (equipment == null)
				return;

			// The calls below assign, unassign and may request a new suit; the host does that on its side.
			AssignmentPacket.IsApplying = true;
			BuildingConfigPacket.IsApplyingPacket = true;
			try
			{
				if (Equip)
					locker.EquipTo(equipment); // returns early when this locker holds no suit here
				else if (equipment.GetAssignable(Db.Get().AssignableSlots.Suit) != null)
					locker.UnequipFrom(equipment);
			}
			finally
			{
				AssignmentPacket.IsApplying = false;
				BuildingConfigPacket.IsApplyingPacket = false;
			}
		}
	}
}
