using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC
{
	public sealed class LargeImpactorPoiOutcome
	{
		public string PrefabId;
		public int Q;
		public int R;
	}

	public sealed class LargeImpactorElementData
	{
		public SimHashes Element;
		public float Mass;
	}

	public sealed class LargeImpactorResearchData
	{
		public string Description;
		public int DataValue;
		public bool Completed;
		public SimHashes DiscoveredRareResource;
		public string DiscoveredRareItem;
	}

	public sealed class LargeImpactorDestinationData
	{
		public int Id;
		public string Type;
		public bool StartAnalyzed;
		public int Distance;
		public float ActivePeriod;
		public float InactivePeriod;
		public float StartingOrbitPercentage;
		public float AvailableMass;
		public List<LargeImpactorElementData> RecoverableElements = new();
		public List<LargeImpactorResearchData> ResearchOpportunities = new();

		internal static LargeImpactorDestinationData FromDestination(SpaceDestination destination)
		{
			var data = new LargeImpactorDestinationData
			{
				Id = destination.id,
				Type = destination.type,
				StartAnalyzed = destination.startAnalyzed,
				Distance = destination.distance,
				ActivePeriod = destination.activePeriod,
				InactivePeriod = destination.inactivePeriod,
				StartingOrbitPercentage = destination.startingOrbitPercentage,
				AvailableMass = destination.availableMass
			};

			if (destination.recoverableElements != null)
			{
				foreach (var pair in destination.recoverableElements)
				{
					data.RecoverableElements.Add(new LargeImpactorElementData
					{
						Element = pair.Key,
						Mass = pair.Value
					});
				}
			}

			if (destination.researchOpportunities != null)
			{
				foreach (var opportunity in destination.researchOpportunities)
				{
					data.ResearchOpportunities.Add(new LargeImpactorResearchData
					{
						Description = opportunity.description,
						DataValue = opportunity.dataValue,
						Completed = opportunity.completed,
						DiscoveredRareResource = opportunity.discoveredRareResource,
						DiscoveredRareItem = opportunity.discoveredRareItem
					});
				}
			}

			return data;
		}

		internal SpaceDestination ToDestination()
		{
			var destination = (SpaceDestination)FormatterServices.GetUninitializedObject(typeof(SpaceDestination));
			destination.id = Id;
			destination.type = Type;
			destination.startAnalyzed = StartAnalyzed;
			destination.distance = Distance;
			destination.activePeriod = ActivePeriod;
			destination.inactivePeriod = InactivePeriod;
			destination.startingOrbitPercentage = StartingOrbitPercentage;
			destination.availableMass = AvailableMass;
			destination.recoverableElements = new Dictionary<SimHashes, float>();
			destination.researchOpportunities = new List<SpaceDestination.ResearchOpportunity>();

			foreach (var element in RecoverableElements)
				destination.recoverableElements[element.Element] = element.Mass;

			foreach (var data in ResearchOpportunities)
			{
				var opportunity = new SpaceDestination.ResearchOpportunity(data.Description, data.DataValue)
				{
					completed = data.Completed,
					discoveredRareResource = data.DiscoveredRareResource,
					discoveredRareItem = data.DiscoveredRareItem
				};
				destination.researchOpportunities.Add(opportunity);
			}

			return destination;
		}
	}

	public class LargeImpactorOutcomePacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxPoiCount = 8;
		internal const int MaxDestinationCount = 8;
		internal const int MaxElementsPerDestination = 64;
		internal const int MaxResearchPerDestination = 64;
		private const int MaxIdLength = 256;
		private const int MaxDescriptionLength = 1024;

		private static readonly HashSet<string> AppliedPoiKeys = new(StringComparer.Ordinal);
		private static readonly HashSet<string> CompletedEventKeys = new(StringComparer.Ordinal);

		public static bool IsApplying { get; private set; }

		public static void ResetSessionState()
		{
			AppliedPoiKeys.Clear();
			CompletedEventKeys.Clear();
			IsApplying = false;
		}

		internal static bool TryClaimOutcome(string key) => AppliedPoiKeys.Add(key);
		public int EventId;
		public int WorldId;
		public List<LargeImpactorPoiOutcome> Pois = new();
		public List<LargeImpactorDestinationData> Destinations = new();
		internal bool IsValid = true;

		public void Serialize(BinaryWriter writer)
		{
			ValidateCount(Pois?.Count ?? -1, MaxPoiCount, "POI");
			ValidateCount(Destinations?.Count ?? -1, MaxDestinationCount, "destination");

			writer.Write(EventId);
			writer.Write(WorldId);
			writer.Write(Pois.Count);
			foreach (var poi in Pois)
			{
				WriteCappedString(writer, poi.PrefabId, MaxIdLength);
				writer.Write(poi.Q);
				writer.Write(poi.R);
			}

			writer.Write(Destinations.Count);
			foreach (var destination in Destinations)
				WriteDestination(writer, destination);
		}

		public void Deserialize(BinaryReader reader)
		{
			IsValid = false;
			Pois = new List<LargeImpactorPoiOutcome>();
			Destinations = new List<LargeImpactorDestinationData>();

			try
			{
				EventId = reader.ReadInt32();
				WorldId = reader.ReadInt32();
				if (EventId == 0 || WorldId < 0)
					return;

				int poiCount = reader.ReadInt32();
				ValidateCount(poiCount, MaxPoiCount, "POI");
				var poiKeys = new HashSet<string>(StringComparer.Ordinal);
				for (int i = 0; i < poiCount; i++)
				{
					var poi = new LargeImpactorPoiOutcome
					{
						PrefabId = ReadCappedString(reader, MaxIdLength),
						Q = reader.ReadInt32(),
						R = reader.ReadInt32()
					};
					if (string.IsNullOrEmpty(poi.PrefabId) || !poiKeys.Add(BuildPoiKey(EventId, WorldId, poi)))
						throw new InvalidDataException("Invalid or duplicate impactor POI outcome");
					Pois.Add(poi);
				}

				int destinationCount = reader.ReadInt32();
				ValidateCount(destinationCount, MaxDestinationCount, "destination");
				var destinationIds = new HashSet<int>();
				for (int i = 0; i < destinationCount; i++)
				{
					LargeImpactorDestinationData destination = ReadDestination(reader);
					if (!destinationIds.Add(destination.Id))
						throw new InvalidDataException("Duplicate impactor destination ID");
					Destinations.Add(destination);
				}

				IsValid = true;
			}
			catch (Exception e)
			{
				DebugConsole.LogWarning($"[LargeImpactorOutcomePacket] Rejected malformed outcome: {e.Message}");
				Pois.Clear();
				Destinations.Clear();
			}
		}

		public void OnDispatched()
		{
			if (!IsValid || !ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				return;

			bool previousApplying = IsApplying;
			IsApplying = true;
			try
			{
				ApplyPois();
				ApplyDestinations();
				ApplyEventCompletion();
			}
			finally
			{
				IsApplying = previousApplying;
			}
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
		{
			return !localIsHost && senderIsHost;
		}

		internal static string BuildPoiKey(int eventId, int worldId, LargeImpactorPoiOutcome poi)
		{
			return $"{eventId}:{worldId}:{poi.PrefabId}:{poi.Q}:{poi.R}";
		}

		internal static bool ContainsDestinationId(IReadOnlyList<int> destinationIds, int id)
		{
			if (destinationIds == null)
				return false;
			for (int i = 0; i < destinationIds.Count; i++)
			{
				if (destinationIds[i] == id)
					return true;
			}
			return false;
		}

		internal static bool InsertDestination(
			List<SpaceDestination> destinations,
			LargeImpactorDestinationData data,
			Action<SpaceDestination> onInserted)
		{
			if (destinations == null || data == null)
				return false;
			foreach (var existing in destinations)
			{
				if (existing.id == data.Id)
					return false;
			}

			SpaceDestination destination = data.ToDestination();
			destinations.Add(destination);
			onInserted?.Invoke(destination);
			return true;
		}

		private void ApplyPois()
		{
			foreach (var poi in Pois)
			{
				string key = BuildPoiKey(EventId, WorldId, poi);
				if (AppliedPoiKeys.Contains(key))
					continue;

				LargeImpactorEvent.SpawnPOI(
					poi.PrefabId, AxialCoordinateSync.FromQr(poi.Q, poi.R));
				TryClaimOutcome(key);
			}
		}

		private void ApplyDestinations()
		{
			if (Destinations.Count == 0 || SpacecraftManager.instance == null)
				return;

			SpacecraftManager manager = SpacecraftManager.instance;
			foreach (var destination in Destinations)
				InsertDestination(manager.destinations, destination,
					inserted => manager.Trigger((int)GameHashes.SpaceDestinationAdded, inserted));
		}

		private void ApplyEventCompletion()
		{
			string key = EventId + ":" + WorldId;
			if (CompletedEventKeys.Contains(key))
				return;

			GameplayEventInstance eventInstance = GameplayEventManager.Instance?.GetGameplayEventInstance(
				new HashedString(EventId), WorldId);
			var eventSmi = eventInstance?.smi as LargeImpactorEvent.StatesInstance;
			if (eventSmi == null)
			{
				DebugConsole.LogWarning($"[LargeImpactorOutcomePacket] Event {EventId} in world {WorldId} was not found");
				return;
			}

			if (!eventSmi.IsInsideState(eventSmi.sm.finished))
				eventSmi.GoTo(eventSmi.sm.finished);
			CompletedEventKeys.Add(key);
		}

		private static void WriteDestination(BinaryWriter writer, LargeImpactorDestinationData destination)
		{
			ValidateCount(destination.RecoverableElements?.Count ?? -1, MaxElementsPerDestination, "element");
			ValidateCount(destination.ResearchOpportunities?.Count ?? -1, MaxResearchPerDestination, "research");
			ValidateFinite(destination.ActivePeriod, destination.InactivePeriod,
				destination.StartingOrbitPercentage, destination.AvailableMass);

			writer.Write(destination.Id);
			WriteCappedString(writer, destination.Type, MaxIdLength);
			writer.Write(destination.StartAnalyzed);
			writer.Write(destination.Distance);
			writer.Write(destination.ActivePeriod);
			writer.Write(destination.InactivePeriod);
			writer.Write(destination.StartingOrbitPercentage);
			writer.Write(destination.AvailableMass);
			writer.Write(destination.RecoverableElements.Count);
			foreach (var element in destination.RecoverableElements)
			{
				ValidateFinite(element.Mass);
				writer.Write((int)element.Element);
				writer.Write(element.Mass);
			}

			writer.Write(destination.ResearchOpportunities.Count);
			foreach (var research in destination.ResearchOpportunities)
			{
				WriteCappedString(writer, research.Description, MaxDescriptionLength);
				writer.Write(research.DataValue);
				writer.Write(research.Completed);
				writer.Write((int)research.DiscoveredRareResource);
				WriteCappedString(writer, research.DiscoveredRareItem, MaxIdLength);
			}
		}

		private static LargeImpactorDestinationData ReadDestination(BinaryReader reader)
		{
			var destination = new LargeImpactorDestinationData
			{
				Id = reader.ReadInt32(),
				Type = ReadCappedString(reader, MaxIdLength),
				StartAnalyzed = reader.ReadBoolean(),
				Distance = reader.ReadInt32(),
				ActivePeriod = reader.ReadSingle(),
				InactivePeriod = reader.ReadSingle(),
				StartingOrbitPercentage = reader.ReadSingle(),
				AvailableMass = reader.ReadSingle()
			};
			if (string.IsNullOrEmpty(destination.Type) || destination.Distance < 0)
				throw new InvalidDataException("Invalid impactor destination identity");
			ValidateFinite(destination.ActivePeriod, destination.InactivePeriod,
				destination.StartingOrbitPercentage, destination.AvailableMass);

			int elementCount = reader.ReadInt32();
			ValidateCount(elementCount, MaxElementsPerDestination, "element");
			var elementIds = new HashSet<SimHashes>();
			for (int i = 0; i < elementCount; i++)
			{
				var element = new LargeImpactorElementData
				{
					Element = (SimHashes)reader.ReadInt32(),
					Mass = reader.ReadSingle()
				};
				ValidateFinite(element.Mass);
				if (!elementIds.Add(element.Element))
					throw new InvalidDataException("Duplicate recoverable element");
				destination.RecoverableElements.Add(element);
			}

			int researchCount = reader.ReadInt32();
			ValidateCount(researchCount, MaxResearchPerDestination, "research");
			for (int i = 0; i < researchCount; i++)
			{
				destination.ResearchOpportunities.Add(new LargeImpactorResearchData
				{
					Description = ReadCappedString(reader, MaxDescriptionLength),
					DataValue = reader.ReadInt32(),
					Completed = reader.ReadBoolean(),
					DiscoveredRareResource = (SimHashes)reader.ReadInt32(),
					DiscoveredRareItem = ReadCappedString(reader, MaxIdLength)
				});
			}

			return destination;
		}

		private static void ValidateCount(int count, int max, string name)
		{
			if (count < 0 || count > max)
				throw new InvalidDataException($"Invalid {name} count {count}");
		}

		private static void ValidateFinite(params float[] values)
		{
			foreach (float value in values)
			{
				if (float.IsNaN(value) || float.IsInfinity(value))
					throw new InvalidDataException("Non-finite impactor outcome value");
			}
		}

		private static void WriteCappedString(BinaryWriter writer, string value, int maxLength)
		{
			value ??= string.Empty;
			if (value.Length > maxLength)
				throw new InvalidDataException("Impactor outcome string is too long");
			writer.Write(value);
		}

		private static string ReadCappedString(BinaryReader reader, int maxLength)
		{
			string value = reader.ReadString();
			if (value.Length > maxLength)
				throw new InvalidDataException("Impactor outcome string is too long");
			return value;
		}
	}
}
