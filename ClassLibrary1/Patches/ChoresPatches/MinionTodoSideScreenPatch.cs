using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Networking.Packets.Chores;
using Shared.Profiling;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ONI_MP.Patches.Chores
{
	public static class MinionTodoSideScreenPatch
	{
		private static readonly FieldInfo _priorityGroupsField =
			AccessTools.Field(typeof(MinionTodoSideScreen), "priorityGroups");
		private static readonly FieldInfo _choreEntriesField =
			AccessTools.Field(typeof(MinionTodoSideScreen), "choreEntries");
		private static readonly FieldInfo _refreshHandleField =
			AccessTools.Field(typeof(MinionTodoSideScreen), "refreshHandle");
		private static readonly MethodInfo _populateElementsMethod =
			AccessTools.Method(typeof(MinionTodoSideScreen), "PopulateElements");
		private static readonly FieldInfo _buttonColorCurrentField =
			AccessTools.Field(typeof(MinionTodoSideScreen), "buttonColorSettingCurrent");
		private static readonly FieldInfo _buttonColorStandardField =
			AccessTools.Field(typeof(MinionTodoSideScreen), "buttonColorSettingStandard");

		private static int _subscribedNetId;

		[HarmonyPatch(typeof(MinionTodoSideScreen), "SetTarget")]
		public static class SetTarget_Patch
		{
			public static void Postfix(GameObject target)
			{
				using var _ = Profiler.Scope();
				if (!MultiplayerSession.IsClient || target == null) return;
				var identity = target.GetComponent<NetworkIdentity>();
				if (identity == null) return;
				SubscribeTo(identity.NetId);
			}
		}

		[HarmonyPatch(typeof(MinionTodoSideScreen), "ClearTarget")]
		public static class ClearTarget_Patch
		{
			public static void Postfix()
			{
				using var _ = Profiler.Scope();
				if (!MultiplayerSession.IsClient) return;
				Unsubscribe();
			}
		}

		private static void SubscribeTo(int netId)
		{
			if (_subscribedNetId == netId) return;
			if (_subscribedNetId != 0)
				PacketSender.SendToHost(new ChoreErrandsSubscribePacket { DupeNetId = _subscribedNetId, Subscribe = false });
			_subscribedNetId = netId;
			PacketSender.SendToHost(new ChoreErrandsSubscribePacket { DupeNetId = netId, Subscribe = true });
		}

		private static void Unsubscribe()
		{
			if (_subscribedNetId == 0) return;
			PacketSender.SendToHost(new ChoreErrandsSubscribePacket { DupeNetId = _subscribedNetId, Subscribe = false });
			_subscribedNetId = 0;
		}

		[HarmonyPatch(typeof(MinionTodoSideScreen), "PopulateElements")]
		public static class PopulateElements_Patch
		{
			public static bool Prefix(MinionTodoSideScreen __instance)
			{
				using var _ = Profiler.Scope();
				if (!MultiplayerSession.IsClient) return true;

				RescheduleRefresh(__instance);

				var target = DetailsScreen.Instance.target;
				if (target == null) return false;

				var receiver = target.GetComponent<ClientReceiver_ChoreErrands>();
				if (receiver == null) return false;

				RenderCurrent(__instance, receiver.Current, target);
				RenderUpcoming(__instance, receiver.Upcoming, target);
				return false;
			}
		}

		private static void RescheduleRefresh(MinionTodoSideScreen screen)
		{
			var handle = (SchedulerHandle)_refreshHandleField.GetValue(screen);
			handle.ClearScheduler();
			handle = UIScheduler.Instance.Schedule("RefreshToDoList", 0.1f,
				_ => _populateElementsMethod.Invoke(screen, new object[] { null }));
			_refreshHandleField.SetValue(screen, handle);
		}

		private static void RenderCurrent(MinionTodoSideScreen screen, ErrandEntry? current, GameObject target)
		{
			if (!current.HasValue)
			{
				screen.currentTask.gameObject.SetActive(false);
				return;
			}
			ApplyEntry(screen.currentTask, current.Value, target);
			ApplyButtonColor(screen, screen.currentTask, current.Value);
			screen.currentTask.gameObject.SetActive(true);
		}

		private static void RenderUpcoming(MinionTodoSideScreen screen, List<ErrandEntry> entries, GameObject target)
		{
			var priorityGroups = _priorityGroupsField.GetValue(screen)
				as List<Tuple<PriorityScreen.PriorityClass, int, HierarchyReferences>>;
			var choreEntries = _choreEntriesField.GetValue(screen) as List<MinionTodoChoreEntry>;
			if (priorityGroups == null || choreEntries == null) return;

			int activeCount = 0;
			if (entries != null)
			{
				for (int i = 0; i < entries.Count; i++)
				{
					var entry = entries[i];
					var container = FindEntriesContainer(priorityGroups, entry);
					if (container == null) continue;

					var uiEntry = GetOrCreateEntry(screen, choreEntries, activeCount);
					uiEntry.transform.SetParent(container);
					uiEntry.transform.SetAsLastSibling();
					ApplyEntry(uiEntry, entry, target);
					ApplyButtonColor(screen, uiEntry, entry);
					uiEntry.gameObject.SetActive(true);
					activeCount++;
				}
			}

			for (int i = activeCount; i < choreEntries.Count; i++)
				choreEntries[i].gameObject.SetActive(false);

			foreach (var group in priorityGroups)
			{
				var container = group.third.GetReference<RectTransform>("EntriesContainer");
				group.third.gameObject.SetActive(container.childCount > 0);
			}
		}

		private static RectTransform FindEntriesContainer(
			List<Tuple<PriorityScreen.PriorityClass, int, HierarchyReferences>> priorityGroups,
			ErrandEntry entry)
		{
			var entryClass = (PriorityScreen.PriorityClass)entry.PriorityClass;
			foreach (var group in priorityGroups)
			{
				if (group.first != entryClass) continue;
				if (entryClass != PriorityScreen.PriorityClass.basic)
					return group.third.GetReference<RectTransform>("EntriesContainer");
				if (group.second == entry.PersonalPriority)
					return group.third.GetReference<RectTransform>("EntriesContainer");
			}
			return null;
		}

		private static MinionTodoChoreEntry GetOrCreateEntry(
			MinionTodoSideScreen screen, List<MinionTodoChoreEntry> pool, int index)
		{
			if (index < pool.Count) return pool[index];
			var created = Util.KInstantiateUI<MinionTodoChoreEntry>(
				screen.taskEntryPrefab.gameObject, screen.taskEntryContainer);
			pool.Add(created);
			return created;
		}

		private static void ApplyEntry(MinionTodoChoreEntry uiEntry, ErrandEntry data, GameObject target)
		{
			var choreType = Db.Get().ChoreTypes.TryGet(data.ChoreTypeId);
			string label = choreType != null ? choreType.Name : data.ChoreTypeId;

			uiEntry.label?.SetText(label);
			uiEntry.subLabel?.SetText(data.TargetLabel ?? string.Empty);

			if (uiEntry.icon != null)
			{
				if (!string.IsNullOrEmpty(data.IconSpriteName))
					uiEntry.icon.sprite = Assets.GetSprite(data.IconSpriteName);
				else
					uiEntry.icon.sprite = ResolveDupeMiniIcon(target);
			}

			bool isBasic = data.PriorityClass == (int)PriorityScreen.PriorityClass.basic;
			if (isBasic)
			{
				uiEntry.priorityLabel?.SetText(data.Priority.ToString());
				if (uiEntry.priorityIcon != null && uiEntry.prioritySprites != null
					&& data.Priority >= 1 && data.Priority <= uiEntry.prioritySprites.Count)
				{
					uiEntry.priorityIcon.sprite = uiEntry.prioritySprites[data.Priority - 1];
				}
			}
			else
			{
				uiEntry.priorityLabel?.SetText(string.Empty);
				if (uiEntry.priorityIcon != null)
					uiEntry.priorityIcon.sprite = null;
			}
			if (uiEntry.moreLabel != null)
				uiEntry.moreLabel.gameObject.SetActive(true);
			if (data.MoreAmount > 0)
				uiEntry.SetMoreAmount(data.MoreAmount);
			else if (uiEntry.moreLabel != null)
				uiEntry.moreLabel.text = string.Empty;

			BindClickFocus(uiEntry, data.TargetCell);
		}

		private static void ApplyButtonColor(MinionTodoSideScreen screen, MinionTodoChoreEntry uiEntry, ErrandEntry data)
		{
			var button = uiEntry.GetComponentInChildren<KButton>();
			if (button == null || button.bgImage == null) return;
			var color = data.IsCurrent
				? _buttonColorCurrentField.GetValue(screen) as ColorStyleSetting
				: _buttonColorStandardField.GetValue(screen) as ColorStyleSetting;
			if (color == null) return;
			button.bgImage.colorStyleSetting = color;
			button.bgImage.ApplyColorStyleSetting();
		}

		private static Sprite ResolveDupeMiniIcon(GameObject target)
		{
			var identity = target?.GetComponent<MinionIdentity>();
			if (identity == null) return null;
			return Db.Get().Personalities.Get(identity.personalityResourceId)?.GetMiniIcon();
		}

		private static void BindClickFocus(MinionTodoChoreEntry uiEntry, int cell)
		{
			var button = uiEntry.GetComponentInChildren<KButton>();
			if (button == null) return;
			button.ClearOnClick();
			if (cell < 0 || !Grid.IsValidCell(cell)) return;
			button.onClick += delegate
			{
				var pos = Grid.CellToPosCCC(cell, Grid.SceneLayer.Move);
				GameUtil.FocusCamera(new Vector3(pos.x, pos.y + 1f, CameraController.Instance.transform.position.z));
			};
		}
	}
}
