using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using System.IO;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DuplicantActions
{
	public class DuplicantCarryItemPacket : IPacket, IBulkablePacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		public int NetId;
		public int PickupableNetId;
		public string AnimFileName;
		public int ItemPrefabHash;
		public bool IsCarrying;

		public int MaxPackSize => 500;
		public uint IntervalMs => 50;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			writer.Write(NetId);
			writer.Write(PickupableNetId);
			writer.Write(AnimFileName ?? string.Empty);
			writer.Write(ItemPrefabHash);
			writer.Write(IsCarrying);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			NetId = reader.ReadInt32();
			PickupableNetId = reader.ReadInt32();
			AnimFileName = reader.ReadString();
			ItemPrefabHash = reader.ReadInt32();
			IsCarrying = reader.ReadBoolean();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (MultiplayerSession.IsHost)
				return;

			if (IsCarrying)
				HandlePickUp();
			else
				HandlePutDown();
		}

		private const string PROXY_NAME_SUFFIX = "_CarriedItem";

		private void HandlePickUp()
		{
			if (!NetworkIdentityRegistry.TryGetComponent<KBatchedAnimController>(NetId, out var dupeAnim))
			{
				DebugConsole.LogWarning($"[DuplicantCarryItemPacket] Dupe KBatchedAnimController {NetId} not found");
				return;
			}

			if (string.IsNullOrEmpty(AnimFileName))
			{
				DebugConsole.LogWarning($"[DuplicantCarryItemPacket] Empty anim file name for dupe {NetId}");
				return;
			}

			string proxyName = $"{NetId}_{PickupableNetId}{PROXY_NAME_SUFFIX}";

			// Remove any existing carried item proxy for this dupe
			CleanupExistingProxy(dupeAnim.transform);

			var kanim = ResolveAnim(AnimFileName);
			if (kanim == null)
			{
				DebugConsole.LogWarning($"[DuplicantCarryItemPacket] Anim file not found: {AnimFileName}");
				return;
			}

			//DebugConsole.Log($"[DuplicantCarryItemPacket] Creating carry proxy: dupe={NetId} anim={AnimFileName} prefabHash={ItemPrefabHash}");

			// Create inactive so KBatchedAnimController.Awake() doesn't fire before AnimFiles is set
			var proxy = new GameObject(proxyName);
			proxy.SetActive(false);
			proxy.transform.SetParent(dupeAnim.transform, false);
			proxy.transform.localPosition = Vector3.zero;

			var animData = kanim.GetData();

			var animCtrl = proxy.AddComponent<KBatchedAnimController>();
			animCtrl.AnimFiles = new[] { kanim };

			if (animData != null && animData.animCount > 0)
			{
				var firstAnim = animData.GetAnim(0);
				//DebugConsole.Log($"[DuplicantCarryItemPacket] Playing anim: {firstAnim.name} (index 0 of {animData.animCount})");
				animCtrl.initialAnim = firstAnim.name.ToString();
			}
			else
			{
				DebugConsole.LogWarning($"[DuplicantCarryItemPacket] No animations found in kanim: {AnimFileName}");
			}

			var tracker = proxy.AddComponent<KBatchedAnimTracker>();
			tracker.symbol = new HashedString("snapTo_chest");
			tracker.useTargetPoint = false;
			tracker.fadeOut = false;
			tracker.forceAlwaysVisible = true;

			proxy.SetActive(true);

			if (animData != null && animData.animCount > 0)
			{
				var firstAnim = animData.GetAnim(0);
				animCtrl.Play(firstAnim.hash, KAnim.PlayMode.Loop);
			}

			//DebugConsole.Log($"[DuplicantCarryItemPacket] Carry proxy created: {proxyName}");
			ShowPopFX(dupeAnim.transform);
		}

		private static KAnimFile ResolveAnim(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;

			var anim = Assets.GetAnim(name);
			if (anim != null)
				return anim;

			// Try with _kanim suffix
			if (!name.EndsWith("_kanim"))
				return Assets.GetAnim(name + "_kanim");

			// Try without _kanim suffix
			return Assets.GetAnim(name.Substring(0, name.Length - 6));
		}

		private void HandlePutDown()
		{
			if (!NetworkIdentityRegistry.TryGet(NetId, out var netIdentity))
			{
				DebugConsole.LogWarning($"[DuplicantCarryItemPacket] Dupe {NetId} not found for put-down");
				return;
			}

			string proxyName = $"{NetId}_{PickupableNetId}{PROXY_NAME_SUFFIX}";
			var proxy = netIdentity.transform.Find(proxyName);
			if (proxy != null)
			{
				Object.Destroy(proxy.gameObject);
			}

			// If PickupableNetId is 0 (unknown item), clean up any proxy on this dupe
			if (PickupableNetId == 0)
				CleanupExistingProxy(netIdentity.transform);
		}

		private static void CleanupExistingProxy(Transform dupeTransform)
		{
			for (int i = dupeTransform.childCount - 1; i >= 0; i--)
			{
				var child = dupeTransform.GetChild(i);
				if (child.name.EndsWith(PROXY_NAME_SUFFIX))
					Object.Destroy(child.gameObject);
			}
		}

		private void ShowPopFX(Transform target)
		{
			if (PopFXManager.Instance == null || ItemPrefabHash == 0)
				return;

			var prefab = Assets.GetPrefab(new Tag(ItemPrefabHash));
			if (prefab == null)
				return;

			string text = string.Format(
				global::STRINGS.UI.PICKEDUP,
				GameUtil.GetFormattedMass(1),
				prefab.GetProperName());

			PopFXManager.Instance.SpawnFX(
				Def.GetUISprite(prefab).first,
				PopFXManager.Instance.sprite_Negative,
				text,
				target,
				Vector3.zero);
		}
	}
}
