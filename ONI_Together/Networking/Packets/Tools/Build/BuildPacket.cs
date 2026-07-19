using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Database;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Build
{
	public sealed class BuildPacket : IPacket, IClientRelayable
	{
		public string PrefabID = string.Empty;
		public int Cell;
		public Orientation Orientation;
		public List<string> MaterialTags = [];
		public int PriorityClass;
		public int PriorityValue;
		public int ObjectLayer;
		public string FacadeID = BuildAuthority.DefaultFacade;

		public BuildPacket()
		{
		}

		internal BuildPacket(
			BuildingDef def,
			int cell,
			Orientation orientation,
			IEnumerable<Tag> materials,
			PrioritySetting priority,
			string facadeId)
		{
			PrefabID = def?.PrefabID ?? string.Empty;
			Cell = cell;
			Orientation = orientation;
			MaterialTags = materials?.Select(tag => tag.ToString()).ToList() ?? [];
			PriorityClass = (int)priority.priority_class;
			PriorityValue = priority.priority_value;
			ObjectLayer = (int)(def?.ObjectLayer ?? global::ObjectLayer.NumLayers);
			FacadeID = BuildAuthority.NormalizeFacade(facadeId);
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			ValidateWire();
			BuildWire.WriteCommon(writer, this);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			BuildWire.ReadCommon(reader, this);
			ValidateWire();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			DispatchContext context = PacketHandler.CurrentContext;
			bool verified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, verified))
				return;

			bool instantBuild = BuildAuthority.GetHostInstantBuildPolicy();
			if (!BuildAuthority.TryExecuteHost(this, instantBuild,
				    state => PacketSender.SendToAllClients(state), out string error))
				throw new InvalidDataException("Rejected build request: " + error);
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;

		internal void ValidateWire()
		{
			if (!BuildAuthority.IsBoundedId(PrefabID) || !BuildAuthority.IsWireCell(Cell) ||
			    !BuildAuthority.IsKnownOrientationValue(Orientation) ||
			    !BuildAuthority.IsPriorityAllowed(PriorityClass, PriorityValue) ||
			    ObjectLayer < 0 || ObjectLayer >= (int)global::ObjectLayer.NumLayers ||
			    !BuildAuthority.IsBoundedFacade(FacadeID) ||
			    !BuildAuthority.AreMaterialTagsWireValid(MaterialTags))
				throw new InvalidDataException("Invalid build request payload");
		}
	}

	internal static class BuildWire
	{
		internal static void WriteCommon(BinaryWriter writer, BuildPacket packet)
		{
			writer.Write(packet.PrefabID);
			writer.Write(packet.Cell);
			writer.Write((int)packet.Orientation);
			writer.Write(packet.MaterialTags.Count);
			foreach (string tag in packet.MaterialTags)
				writer.Write(tag);
			writer.Write(packet.PriorityClass);
			writer.Write(packet.PriorityValue);
			writer.Write(packet.ObjectLayer);
			writer.Write(BuildAuthority.NormalizeFacade(packet.FacadeID));
		}

		internal static void ReadCommon(BinaryReader reader, BuildPacket packet)
		{
			packet.PrefabID = BuildAuthority.ReadBoundedString(reader, BuildAuthority.MaxIdLength);
			packet.Cell = reader.ReadInt32();
			packet.Orientation = (Orientation)reader.ReadInt32();
			packet.MaterialTags = BuildAuthority.ReadMaterialTags(reader);
			packet.PriorityClass = reader.ReadInt32();
			packet.PriorityValue = reader.ReadInt32();
			packet.ObjectLayer = reader.ReadInt32();
			packet.FacadeID = BuildAuthority.ReadBoundedString(reader, BuildAuthority.MaxIdLength);
		}
	}

	internal sealed class BuildCapture
	{
		internal BuildPacket Request;
		internal GameObject ObjectBefore;
		internal GameObject TileBefore;
		internal GameObject ReplacementBefore;
	}

	internal static partial class BuildAuthority
	{
		internal const int MaxIdLength = 256;
		internal const int MaxWireCell = 4 * 1024 * 1024;
		internal const int MaxMaterialTagCount = 16;
		internal const int MaxMaterialTagLength = 128;
		internal const string DefaultFacade = "DEFAULT_FACADE";

		internal static bool IsKnownOrientationValue(Orientation orientation)
			=> orientation is Orientation.Neutral or Orientation.R90 or Orientation.R180 or
			   Orientation.R270 or Orientation.FlipH or Orientation.FlipV;

		internal static bool IsOrientationAllowed(Orientation orientation, PermittedRotations permitted)
		{
			if (orientation == Orientation.Neutral)
				return true;
			return permitted switch
			{
				PermittedRotations.R360 => orientation is Orientation.R90 or Orientation.R180 or Orientation.R270,
				PermittedRotations.R90 => orientation == Orientation.R90,
				PermittedRotations.FlipH => orientation == Orientation.FlipH,
				PermittedRotations.FlipV => orientation == Orientation.FlipV,
				_ => false
			};
		}

		internal static bool IsPriorityAllowed(int priorityClass, int priorityValue)
		{
			PriorityScreen.PriorityClass value = (PriorityScreen.PriorityClass)priorityClass;
			return value switch
			{
				PriorityScreen.PriorityClass.basic or PriorityScreen.PriorityClass.high
					=> priorityValue is >= 1 and <= 9,
				PriorityScreen.PriorityClass.topPriority => priorityValue == 1,
				_ => false
			};
		}

		internal static bool IsFacadeAllowed(
			string facadeId,
			IEnumerable<string> available,
			bool facadeExists,
			bool prefabSupportsFacade)
		{
			string normalized = NormalizeFacade(facadeId);
			return normalized == DefaultFacade ||
			       prefabSupportsFacade && facadeExists && available != null && available.Contains(normalized);
		}

		internal static bool IsMaterialAllowed(Tag selected, IEnumerable<Tag> validMaterials)
			=> validMaterials != null && validMaterials.Contains(selected);

		internal static bool DeriveInstantBuild(bool debugInstant, bool sandboxActive, bool sandboxInstant)
			=> debugInstant || sandboxActive && sandboxInstant;

		internal static bool GetHostInstantBuildPolicy()
		{
			bool sandboxActive = Game.Instance != null && Game.Instance.SandboxModeActive;
			bool sandboxInstant = SandboxToolParameterMenu.instance?.settings?.InstantBuild == true;
			return DeriveInstantBuild(DebugHandler.InstantBuildMode, sandboxActive, sandboxInstant);
		}

		internal static string NormalizeFacade(string facadeId)
			=> string.IsNullOrWhiteSpace(facadeId) ? DefaultFacade : facadeId;

		internal static bool IsBoundedId(string value)
			=> !string.IsNullOrWhiteSpace(value) && value.Length <= MaxIdLength;

		internal static bool IsWireCell(int cell)
			=> cell >= 0 && cell < MaxWireCell;

		internal static bool IsBoundedFacade(string value)
			=> value != null && value.Length <= MaxIdLength;

		internal static bool AreMaterialTagsWireValid(IReadOnlyList<string> tags)
		{
			if (tags == null || tags.Count == 0 || tags.Count > MaxMaterialTagCount)
				return false;
			foreach (string tag in tags)
			{
				if (string.IsNullOrWhiteSpace(tag) || tag.Length > MaxMaterialTagLength)
					return false;
			}
			return true;
		}

		internal static string ReadBoundedString(BinaryReader reader, int maxLength)
		{
			string value = reader.ReadString();
			if (value.Length > maxLength)
				throw new InvalidDataException("Build string exceeds limit");
			return value;
		}

		internal static List<string> ReadMaterialTags(BinaryReader reader)
		{
			int count = reader.ReadInt32();
			if (count <= 0 || count > MaxMaterialTagCount)
				throw new InvalidDataException("Invalid build material count");
			var tags = new List<string>(count);
			for (int index = 0; index < count; index++)
				tags.Add(ReadBoundedString(reader, MaxMaterialTagLength));
			return tags;
		}

		internal static bool TryResolve(
			BuildPacket packet,
			out BuildingDef def,
			out List<Tag> materials,
			out PrioritySetting priority,
			out string error)
		{
			def = null;
			materials = null;
			priority = default;
			error = string.Empty;
			try
			{
				packet.ValidateWire();
			}
			catch (InvalidDataException exception)
			{
				error = exception.Message;
				return false;
			}

			def = Assets.GetBuildingDef(packet.PrefabID);
			if (def == null || packet.ObjectLayer != (int)def.ObjectLayer)
				return Fail("unknown prefab or object-layer mismatch", ref error);
			if (!Grid.IsValidCell(packet.Cell) || !Grid.IsVisible(packet.Cell) ||
			    !IsOrientationAllowed(packet.Orientation, def.PermittedRotations))
				return Fail("cell is hidden or orientation is unavailable", ref error);
			if (!TryResolveMaterials(def, packet.MaterialTags, out materials))
				return Fail("material selection does not match recipe categories", ref error);
			if (!TryValidateFacade(def, packet.FacadeID))
				return Fail("facade is unavailable for prefab", ref error);

			priority = new PrioritySetting((PriorityScreen.PriorityClass)packet.PriorityClass, packet.PriorityValue);
			return true;
		}

		internal static bool TryResolveMaterials(BuildingDef def, IReadOnlyList<string> wireTags, out List<Tag> tags)
		{
			tags = null;
			if (def.MaterialCategory == null || wireTags.Count != def.MaterialCategory.Length)
				return false;
			var selected = new List<Tag>(wireTags.Count);
			for (int index = 0; index < wireTags.Count; index++)
			{
				Tag tag = TagManager.Create(wireTags[index]);
				if (!IsMaterialAllowed(tag, MaterialSelector.GetValidMaterials(def.MaterialCategory[index])))
					return false;
				selected.Add(tag);
			}
			tags = selected;
			return true;
		}

		internal static bool TryValidateFacade(BuildingDef def, string facadeId)
		{
			string normalized = NormalizeFacade(facadeId);
			BuildingFacadeResource facade = normalized == DefaultFacade
				? null
				: Db.GetBuildingFacades()?.TryGet(normalized);
			return IsFacadeAllowed(
				normalized,
				def.AvailableFacades,
				facade != null,
				def.BuildingComplete?.GetComponent<BuildingFacade>() != null);
		}

	}
}
