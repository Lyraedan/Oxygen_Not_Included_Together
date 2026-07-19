using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.IO;
using System.Linq;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DuplicantActions
{
	public class ToolEquipPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		public int TargetNetId;
		public string PrefabName;
		public string ParentBoneName;
		public string AnimName; // Optional
		public bool LoopAnim = true;
		public bool Equip;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(TargetNetId);
			writer.Write(PrefabName ?? "");
			writer.Write(ParentBoneName ?? "");
			writer.Write(AnimName ?? "");
			writer.Write(LoopAnim);
			writer.Write(Equip);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			TargetNetId = reader.ReadInt32();
			PrefabName = reader.ReadString();
			ParentBoneName = reader.ReadString();
			AnimName = reader.ReadString();
			LoopAnim = reader.ReadBoolean();
			Equip = reader.ReadBoolean();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!NetworkIdentityRegistry.TryGet(TargetNetId, out var target))
			{
				DebugConsole.LogWarning($"[ToolEquipPacket] Unknown NetId: {TargetNetId}");
				return;
			}

			string equippedToolName = $"{TargetNetId}_EquippedTool";

			if (!Equip)
			{
				var existing = target.transform.Find(equippedToolName);
				if (existing != null)
					UnityEngine.Object.Destroy(existing.gameObject);

				return;
			}

			var animFileName = GetAnimFileFor(PrefabName);
			if (string.IsNullOrEmpty(animFileName))
			{
				DebugConsole.LogWarning($"[ToolEquipPacket] Unknown prefab: {PrefabName}");
				return;
			}

			var bone = FindHandTransform(target.transform);
			if (bone == null)
			{
				DebugConsole.LogWarning($"[ToolEquipPacket] Could not find hand bone on NetId {TargetNetId}");
				return;
			}

			var tool = new GameObject(equippedToolName);
			tool.transform.SetParent(bone, false);
			tool.transform.localPosition = Vector3.zero;

			var anim = tool.AddComponent<KBatchedAnimController>();
			anim.AnimFiles = new[] { Assets.GetAnim(animFileName) };
			if (!string.IsNullOrEmpty(AnimName))
				anim.Play(AnimName, LoopAnim ? KAnim.PlayMode.Loop : KAnim.PlayMode.Once);

			tool.SetActive(true);
			DebugConsole.Log($"[ToolEquipPacket] Spawned tool: {PrefabName} for NetId {TargetNetId}");
		}

		private string GetAnimFileFor(string prefabId)
		{
			using var _ = Profiler.Scope();

			switch (prefabId)
			{
				case "DigEffect": return "laser_kanim";
				case "BuildEffect": return "construct_beam_kanim";
				case "FetchLiquidEffect": return "hose_fx_kanim";
				case "PaintEffect": return "paint_beam_kanim";
				case "HarvestEffect": return "plant_harvest_beam_kanim";
				case "CaptureEffect": return "net_gun_fx_kanim";
				case "AttackEffect": return "attack_beam_fx_kanim";
				case "PickupEffect": return "vacuum_fx_kanim";
				case "StoreEffect": return "vacuum_reverse_fx_kanim";
				case "DisinfectEffect": return "plant_spray_beam_kanim";
				case "TendEffect": return "plant_tending_beam_fx_kanim";
				case "PowerTinkerEffect": return "electrician_beam_fx_kanim";
				case "SpecialistDigEffect": return "senior_miner_beam_fx_kanim";
				case "DemolishEffect": return "poi_demolish_fx_kanim";
				default: return "laser_kanim";
			}
		}

		public static Transform FindHandTransform(Transform root)
		{
			using var _ = Profiler.Scope();

			// Common ONI rigs use a KBatchedAnimTracker with symbol snapTo_rgtHand
			var trackers = root.GetComponentsInChildren<KBatchedAnimTracker>(true);
			foreach (var tracker in trackers)
			{
				if (tracker.symbol == new HashedString("snapTo_rgtHand"))
					return tracker.transform;
			}

			// Fallback: search transform hierarchy for naming clue
			var fallback = root.GetComponentsInChildren<Transform>(true)
						 .FirstOrDefault(t => t.name.IndexOf("rgtHand", StringComparison.OrdinalIgnoreCase) >= 0);

			return fallback;
		}

		private Transform FindBoneTransform(GameObject go, string boneName)
		{
			using var _ = Profiler.Scope();

			var trackers = go.GetComponentsInChildren<KBatchedAnimTracker>(true);
			foreach (var tracker in trackers)
			{
				if (tracker.symbol == boneName || tracker.symbol.ToString() == boneName)
				{
					return tracker.transform;
				}
			}

			var animController = go.GetComponent<KBatchedAnimController>();
			if (animController != null)
			{
				// fallback to just attaching to the anim controller
				return animController.transform;
			}

			return null;
		}

	}
}
