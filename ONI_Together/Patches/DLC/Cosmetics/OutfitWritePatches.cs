using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Cosmetics;

namespace ONI_Together.Patches.DLC.Cosmetics
{
	internal static class CosmeticsSyncGuard
	{
		private static int _applyDepth;
		internal static bool IsApplying => _applyDepth > 0;

		public static void ResetSessionState() => _applyDepth = 0;

		internal static void Run(System.Action action)
		{
			_applyDepth++;
			try
			{
				action();
			}
			finally
			{
				_applyDepth--;
			}
		}
	}

	internal static class OutfitWriteSync
	{
		internal static bool TryBuildData(
			ClothingOutfitTarget target,
			ClothingOutfitUtility.OutfitType outfitType,
			IEnumerable<string> itemIds,
			out OutfitStateData data)
		{
			data = null;
			if (!TryIdentifyTarget(target, out OutfitTargetKind targetKind, out int targetNetId, out string outfitId))
				return false;
			data = new OutfitStateData
			{
				TargetKind = targetKind,
				TargetNetId = targetNetId,
				OutfitId = outfitId,
				OutfitType = outfitType,
				ItemIds = itemIds == null ? new List<string>() : new List<string>(itemIds)
			};
			return data.IsWireValid();
		}

		internal static bool TryCapture(ClothingOutfitTarget target, out OutfitStateData data)
			=> TryBuildData(target, target.OutfitType, target.ReadItems(), out data);

		internal static bool TryApply(OutfitStateData data)
		{
			if (data == null || !data.IsWireValid() || !ValidateItems(data) ||
			    !TryResolveTarget(data, createTemplate: true, out ClothingOutfitTarget target) ||
			    !target.CanWriteItems)
				return false;

			string[] current = target.ReadItems();
			if (ItemsEqual(current, data.ItemIds))
				return true;
			string[] items = data.ItemIds.ToArray();
			CosmeticsSyncGuard.Run(() => target.WriteItems(data.OutfitType, items));
			return true;
		}

		internal static bool TryResolveTarget(
			OutfitStateData data,
			bool createTemplate,
			out ClothingOutfitTarget target)
		{
			target = default;
			if (data.TargetKind == OutfitTargetKind.Minion)
			{
				if (!NetworkIdentityRegistry.TryGet(data.TargetNetId, out var identity) ||
				    identity?.gameObject == null || identity.GetComponent<WearableAccessorizer>() == null)
					return false;
				target = ClothingOutfitTarget.FromMinion(data.OutfitType, identity.gameObject);
				return true;
			}

			Option<ClothingOutfitTarget> existing = ClothingOutfitTarget.TryFromTemplateId(data.OutfitId);
			if (existing.HasValue)
			{
				target = existing.Value;
				return target.OutfitType == data.OutfitType && target.CanWriteItems;
			}
			if (!createTemplate)
				return false;
			target = ClothingOutfitTarget.ForNewTemplateOutfit(data.OutfitType, data.OutfitId);
			return true;
		}

		internal static bool ItemsEqual(IReadOnlyCollection<string> current, IReadOnlyCollection<string> target)
		{
			if (current == null || target == null || current.Count != target.Count)
				return false;
			var currentSet = new HashSet<string>(current, System.StringComparer.Ordinal);
			foreach (string item in target)
			{
				if (!currentSet.Contains(item))
					return false;
			}
			return true;
		}

		internal static void SendRequest(
			ClothingOutfitTarget target,
			ClothingOutfitUtility.OutfitType outfitType,
			string[] items)
		{
			if (TryBuildData(target, outfitType, items, out OutfitStateData data))
				PacketSender.SendToAllOtherPeers(new OutfitWriteRequestPacket(data));
		}

		internal static void SendState(ClothingOutfitTarget target)
		{
			if (CosmeticsSyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !TryCapture(target, out OutfitStateData data))
				return;
			PacketSender.SendToAllClients(new OutfitStatePacket(data));
		}

		private static bool TryIdentifyTarget(
			ClothingOutfitTarget target,
			out OutfitTargetKind targetKind,
			out int targetNetId,
			out string outfitId)
		{
			targetKind = OutfitTargetKind.Template;
			targetNetId = 0;
			outfitId = "";
			if (target.Is<ClothingOutfitTarget.MinionInstance>(out var minion))
			{
				targetKind = OutfitTargetKind.Minion;
				targetNetId = minion.minionInstance?.GetNetIdentity()?.NetId ?? 0;
				return targetNetId != 0;
			}
			if (!target.Is<ClothingOutfitTarget.UserAuthoredTemplate>())
				return false;
			outfitId = target.OutfitId;
			return !string.IsNullOrEmpty(outfitId);
		}

		private static bool ValidateItems(OutfitStateData data)
		{
			foreach (string itemId in data.ItemIds)
			{
				Database.ClothingItemResource item = Db.Get().Permits.ClothingItems.TryGet(itemId);
				if (item == null || item.outfitType != data.OutfitType)
					return false;
			}
			return true;
		}
	}

	internal static class OutfitTemplateMutationSync
	{
		internal static bool TryApply(OutfitTemplateMutationData data)
		{
			if (data == null || !data.IsWireValid())
				return false;
			Option<ClothingOutfitTarget> option = ClothingOutfitTarget.TryFromTemplateId(data.OutfitId);
			if (!option.HasValue)
				return IsAlreadyApplied(data);
			ClothingOutfitTarget target = option.Value;
			if (!target.Is<ClothingOutfitTarget.UserAuthoredTemplate>() ||
			    target.OutfitType != data.OutfitType)
				return false;

			if (data.Kind == OutfitTemplateMutationKind.Rename &&
			    ClothingOutfitTarget.DoesTemplateExist(data.NewOutfitId))
				return false;
			CosmeticsSyncGuard.Run(() => Apply(target, data));
			return true;
		}

		internal static void SendRequest(OutfitTemplateMutationData data)
		{
			if (data.IsWireValid())
				PacketSender.SendToAllOtherPeers(new OutfitTemplateMutationRequestPacket(data));
		}

		internal static void SendState(OutfitTemplateMutationData data)
		{
			if (!CosmeticsSyncGuard.IsApplying && MultiplayerSession.IsHostInSession &&
			    data?.IsWireValid() == true)
				PacketSender.SendToAllClients(new OutfitTemplateMutationStatePacket(data));
		}

		private static void Apply(ClothingOutfitTarget target, OutfitTemplateMutationData data)
		{
			if (data.Kind == OutfitTemplateMutationKind.Rename)
				target.WriteName(data.NewOutfitId);
			else
				target.Delete();
		}

		private static bool IsAlreadyApplied(OutfitTemplateMutationData data)
		{
			if (data.Kind == OutfitTemplateMutationKind.Delete)
				return true;
			Option<ClothingOutfitTarget> renamed = ClothingOutfitTarget.TryFromTemplateId(data.NewOutfitId);
			return renamed.HasValue && renamed.Value.OutfitType == data.OutfitType &&
			       renamed.Value.Is<ClothingOutfitTarget.UserAuthoredTemplate>();
		}
	}

	[HarmonyPatch(typeof(ClothingOutfitTarget), nameof(ClothingOutfitTarget.WriteItems))]
	internal static class ClothingOutfitWriteItemsPatch
	{
		internal static bool Prefix(
			ClothingOutfitTarget __instance,
			ClothingOutfitUtility.OutfitType outfitType,
			string[] items)
		{
			if (CosmeticsSyncGuard.IsApplying || !MultiplayerSession.InSession || MultiplayerSession.IsHost)
				return true;
			OutfitWriteSync.SendRequest(__instance, outfitType, items);
			return false;
		}

		internal static void Postfix(ClothingOutfitTarget __instance)
		{
			if (MultiplayerSession.IsHost)
				OutfitWriteSync.SendState(__instance);
		}
	}

	internal sealed class OutfitTemplateMutationPatchState
	{
		internal OutfitTemplateMutationData Data;
	}

	[HarmonyPatch(typeof(ClothingOutfitTarget), nameof(ClothingOutfitTarget.WriteName))]
	internal static class ClothingOutfitWriteNamePatch
	{
		internal static bool Prefix(
			ClothingOutfitTarget __instance,
			string name,
			out OutfitTemplateMutationPatchState __state)
		{
			__state = OutfitTemplateMutationPatchHelper.BuildState(
				__instance, OutfitTemplateMutationKind.Rename, name);
			if (CosmeticsSyncGuard.IsApplying || !MultiplayerSession.InSession || MultiplayerSession.IsHost)
				return true;
			if (__state?.Data != null)
				OutfitTemplateMutationSync.SendRequest(__state.Data);
			return false;
		}

		internal static void Postfix(OutfitTemplateMutationPatchState __state)
			=> OutfitTemplateMutationSync.SendState(__state?.Data);
	}

	[HarmonyPatch(typeof(ClothingOutfitTarget), nameof(ClothingOutfitTarget.Delete))]
	internal static class ClothingOutfitDeletePatch
	{
		internal static bool Prefix(
			ClothingOutfitTarget __instance,
			out OutfitTemplateMutationPatchState __state)
		{
			__state = OutfitTemplateMutationPatchHelper.BuildState(
				__instance, OutfitTemplateMutationKind.Delete, "");
			if (CosmeticsSyncGuard.IsApplying || !MultiplayerSession.InSession || MultiplayerSession.IsHost)
				return true;
			if (__state?.Data != null)
				OutfitTemplateMutationSync.SendRequest(__state.Data);
			return false;
		}

		internal static void Postfix(OutfitTemplateMutationPatchState __state)
			=> OutfitTemplateMutationSync.SendState(__state?.Data);
	}

	internal static class OutfitTemplateMutationPatchHelper
	{
		internal static OutfitTemplateMutationPatchState BuildState(
			ClothingOutfitTarget target,
			OutfitTemplateMutationKind kind,
			string newId)
		{
			if (!target.Is<ClothingOutfitTarget.UserAuthoredTemplate>())
				return null;
			return new OutfitTemplateMutationPatchState
			{
				Data = new OutfitTemplateMutationData
				{
					Kind = kind,
					OutfitType = target.OutfitType,
					OutfitId = target.OutfitId,
					NewOutfitId = newId ?? ""
				}
			};
		}
	}
}
