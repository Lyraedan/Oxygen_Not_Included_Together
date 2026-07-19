#if DEBUG
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace ONI_Together.DebugTools
{
	internal static partial class SoakStateHash
	{
		private static byte[] HashCells(IEnumerable<SoakCellState> cells)
			=> HashCellsInOrder(cells.OrderBy(state => state.Cell));

		private static byte[] HashCellsInOrder(IEnumerable<SoakCellState> cells)
		{
			using var stream = new MemoryStream();
			using (var writer = CreateWriter(stream))
			{
				foreach (SoakCellState cell in cells)
				{
					writer.Write(cell.Cell);
					writer.Write(cell.ElementIdx);
					writer.Write(cell.Mass);
					writer.Write(cell.Temperature);
					writer.Write(cell.DiseaseIdx);
					writer.Write(cell.DiseaseCount);
				}
			}
			return ComputeSha256(stream);
		}

		private static byte[] HashEntityLifecycle(IEnumerable<SoakEntityState> entities)
		{
			using var stream = new MemoryStream();
			using (var writer = CreateWriter(stream))
			{
				foreach (SoakEntityState entity in entities.OrderBy(state => state.NetId))
				{
					writer.Write(entity.NetId);
					writer.Write(entity.PrefabHash);
					writer.Write(entity.Active);
					writer.Write(entity.Revision);
					writer.Write(entity.Tombstoned);
					writer.Write(entity.IsDuplicant);
					writer.Write(entity.IsDead);
					writer.Write(entity.HasDeadTag);
					writer.Write(entity.MonitorIsDead);
					writer.Write(entity.IsCorpse);
					writer.Write(entity.IsInLiveRoster);
					writer.Write(entity.IsInLiveRosterByModel);
					writer.Write(entity.DeathId ?? string.Empty);
				}
			}
			return ComputeSha256(stream);
		}

		private static byte[] HashWorldMembership(IEnumerable<SoakWorldMembershipState> states)
		{
			using var stream = new MemoryStream();
			using (var writer = CreateWriter(stream))
			{
				foreach (SoakWorldMembershipState state in states.OrderBy(value => value.NetId))
					WriteWorldMembership(writer, state);
			}
			return ComputeSha256(stream);
		}

		private static void WriteWorldMembership(
			BinaryWriter writer, SoakWorldMembershipState state)
		{
			writer.Write(state.NetId);
			writer.Write(state.WorldId);
			writer.Write(state.Cell);
			writer.Write(state.PositionX);
			writer.Write(state.PositionY);
			writer.Write(state.PositionZ);
			writer.Write(state.HasPositionHandler);
			writer.Write(state.FlipX);
			writer.Write(state.FlipY);
			writer.Write((byte)state.NavType);
		}

		private static byte[] HashStorageMembership(
			IEnumerable<SoakStorageMembershipState> states)
		{
			using var stream = new MemoryStream();
			using (var writer = CreateWriter(stream))
			{
				foreach (SoakStorageMembershipState state in OrderedStorage(states))
					WriteStorageMembership(writer, state);
			}
			return ComputeSha256(stream);
		}

		private static IOrderedEnumerable<SoakStorageMembershipState> OrderedStorage(
			IEnumerable<SoakStorageMembershipState> states)
		{
			return states.OrderBy(value => value.StorageNetId)
				.ThenBy(value => value.StorageIndex).ThenBy(value => value.Capacity)
				.ThenBy(value => value.HasItem).ThenBy(value => value.ItemIndex)
				.ThenBy(value => value.ItemNetId)
				.ThenBy(value => value.LinkedStorageNetId)
				.ThenBy(value => value.LinkedStorageIndex).ThenBy(value => value.PrefabHash)
				.ThenBy(value => value.Mass).ThenBy(value => value.Temperature)
				.ThenBy(value => value.DiseaseIdx).ThenBy(value => value.DiseaseCount);
		}

		private static void WriteStorageMembership(
			BinaryWriter writer, SoakStorageMembershipState state)
		{
			writer.Write(state.StorageNetId);
			writer.Write(state.StorageIndex);
			writer.Write(state.Capacity);
			writer.Write(state.HasItem);
			writer.Write(state.ItemIndex);
			writer.Write(state.ItemNetId);
			writer.Write(state.LinkedStorageNetId);
			writer.Write(state.LinkedStorageIndex);
			writer.Write(state.PrefabHash);
			writer.Write(state.Mass);
			writer.Write(state.Temperature);
			writer.Write(state.DiseaseIdx);
			writer.Write(state.DiseaseCount);
		}

		private static byte[] HashClusterRocket(IEnumerable<SoakClusterRocketState> states)
		{
			using var stream = new MemoryStream();
			using (var writer = CreateWriter(stream))
			{
				foreach (SoakClusterRocketState state in states.OrderBy(value => value.NetId))
					WriteClusterRocket(writer, state);
			}
			return ComputeSha256(stream);
		}

		private static void WriteClusterRocket(BinaryWriter writer, SoakClusterRocketState state)
		{
			writer.Write(state.NetId);
			writer.Write(state.HasClusterLocation);
			writer.Write(state.ClusterQ);
			writer.Write(state.ClusterR);
			writer.Write(state.HasDestinationSelector);
			writer.Write(state.HasDestination);
			writer.Write(state.DestinationQ);
			writer.Write(state.DestinationR);
			writer.Write(state.PadNetId);
			writer.Write(state.Repeat);
			writer.Write(state.HasCraftState);
			writer.Write(state.CraftLocationQ);
			writer.Write(state.CraftLocationR);
			writer.Write((byte)state.CraftPhase);
			writer.Write(state.HasCurrentPad);
			writer.Write(state.CurrentPadNetId);
			writer.Write(state.HasControlStation);
			writer.Write(state.RestrictWhenGrounded);
		}

		private static BinaryWriter CreateWriter(MemoryStream stream)
			=> new(stream, System.Text.Encoding.UTF8, leaveOpen: true);

		private static byte[] ComputeSha256(MemoryStream stream)
		{
			stream.Position = 0;
			using var sha256 = SHA256.Create();
			return sha256.ComputeHash(stream);
		}
	}
}
#endif
