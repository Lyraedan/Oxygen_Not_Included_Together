using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using PeterHan.PLib.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ONI_Together.Networking.Overlay
{
	public static class NetworkingOverlayPatches
	{
		private const BindingFlags INSTANCE_ALL = BindingFlags.Public | BindingFlags.NonPublic |
			BindingFlags.Instance;

		private static readonly Type OVERLAY_TOGGLE_TYPE = typeof(OverlayMenu).GetNestedType(
			"OverlayToggleInfo", INSTANCE_ALL);

		private static readonly Type OVERLAY_TYPE = typeof(OverlayLegend).GetNestedType(
			"OverlayInfo", INSTANCE_ALL);

		private static readonly Type OVERLAY_INFO_UNIT_TYPE = typeof(OverlayLegend).GetNestedType(
			"OverlayInfoUnit", INSTANCE_ALL);

		[HarmonyPatch(typeof(OverlayMenu), nameof(OverlayMenu.InitializeToggles))]
		public static class OverlayMenu_InitializeToggles_Patch
		{
			internal static void Postfix(ICollection<KIconToggleMenu.ToggleInfo> ___overlayToggleInfos)
			{
				var info = CreateOverlayToggleInfo(
					"Network",
					NetworkingOverlayMode.ID,
					Action.NumActions,
					"Display Network Activity"
				);
				if (info != null)
					___overlayToggleInfos?.Add(info);
			}
		}

		private static bool _prevF5;

		[HarmonyPatch(typeof(OverlayScreen), nameof(OverlayScreen.LateUpdate))]
		public static class OverlayScreen_LateUpdate_Patch
		{
			internal static void Postfix()
			{
				bool f5 = Input.GetKey(KeyCode.F5);
				bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
				if (shift && f5 && !_prevF5)
				{
					var screen = OverlayScreen.Instance;
					if (screen != null)
					{
						bool isActive = screen.mode == NetworkingOverlayMode.ID;
						screen.ToggleOverlay(isActive ? OverlayModes.None.ID : NetworkingOverlayMode.ID);
					}
				}
				_prevF5 = f5;
			}
		}

		[HarmonyPatch(typeof(OverlayScreen), nameof(OverlayScreen.RegisterModes))]
		public static class OverlayScreen_RegisterModes_Patch
		{
			internal static void Postfix(OverlayScreen __instance)
			{
				var registerMethod = typeof(OverlayScreen).GetMethod("RegisterMode",
					INSTANCE_ALL, null, new[] { typeof(OverlayModes.Mode) }, null);
				if (registerMethod != null)
				{
					PUtil.LogDebug("Creating NetworkingOverlayMode");
					registerMethod.Invoke(__instance, new object[] { new NetworkingOverlayMode() });
				}
			}
		}

		[HarmonyPatch(typeof(SimDebugView), nameof(SimDebugView.OnPrefabInit))]
		public static class SimDebugView_OnPrefabInit_Patch
		{
			internal static void Postfix(IDictionary<HashedString, Func<SimDebugView, int, Color>> ___getColourFuncs)
			{
				___getColourFuncs[NetworkingOverlayMode.ID] = NetworkingOverlayMode.GetCellColor;
			}
		}

		[HarmonyPatch(typeof(OverlayLegend), nameof(OverlayLegend.OnSpawn))]
		public static class OverlayLegend_OnSpawn_Patch
		{
			internal static void Prefix(ICollection<OverlayLegend.OverlayInfo> ___overlayInfoList)
			{
				var info = CreateOverlayInfo();
				if (info != null)
					___overlayInfoList.Add(info);
			}
		}

		private static KIconToggleMenu.ToggleInfo CreateOverlayToggleInfo(
			string text, HashedString simView, Action hotKey, string tooltip)
		{
			if (OVERLAY_TOGGLE_TYPE == null)
			{
				PUtil.LogWarning("Unable to add NetworkingOverlay - OverlayToggleInfo type not found");
				return null;
			}

			var constructors = OVERLAY_TOGGLE_TYPE.GetConstructors(INSTANCE_ALL);
			if (constructors.Length != 1)
			{
				PUtil.LogWarning("Unable to add NetworkingOverlay - unexpected constructor count");
				return null;
			}

			var cons = constructors[0];
			var parameters = cons.GetParameters();
			int paramCount = parameters.Length;
			var args = new object[paramCount];

			if (paramCount < 7)
			{
				PUtil.LogWarning("Unable to add NetworkingOverlay - too few parameters");
				return null;
			}

			args[0] = text;
			args[1] = "network_activity";
			args[2] = simView;
			args[3] = "";
			args[4] = hotKey;
			args[5] = tooltip;
			args[6] = text;

			for (int i = 7; i < paramCount; i++)
			{
				var param = parameters[i];
				if (param.IsOptional)
					args[i] = param.DefaultValue;
				else
					args[i] = null;
			}

			var info = cons.Invoke(args) as KIconToggleMenu.ToggleInfo;
			if (info != null)
			{
				info.getSpriteCB = () => NetworkingOverlayMode.OverlayIcon;
				info.getTooltipText = () =>
				{
					var list = new List<Tuple<string, TextStyleSetting>>();
					list.Add(new Tuple<string, TextStyleSetting>(STRINGS.UI.OVERLAYS.NETWORKACTIVITY.NAME,
						ToolTipScreen.Instance.defaultTooltipHeaderStyle));
					string hotkey = "<b><color=#F44A4A>[SHIFT + F5]</b></color>";
					list.Add(new Tuple<string, TextStyleSetting>($"{STRINGS.UI.OVERLAYS.NETWORKACTIVITY.TOOLTIP} {hotkey}",
						ToolTipScreen.Instance.defaultTooltipBodyStyle));
					if (MultiplayerSession.IsClient && NetworkConfig.TransportClient != null)
					{
						int ping = NetworkConfig.TransportClient.GetPing();
						if (ping >= 0)
							list.Add(new Tuple<string, TextStyleSetting>($"Ping: {ping}ms",
								ToolTipScreen.Instance.defaultTooltipBodyStyle));
					}
					return list;
				};
			}
			return info;
		}

		private static OverlayLegend.OverlayInfo CreateOverlayInfo()
		{
			if (OVERLAY_TYPE == null || OVERLAY_INFO_UNIT_TYPE == null)
			{
				PUtil.LogWarning("Unable to add NetworkingOverlay legend - types not found");
				return null;
			}

			var infoUnitCons = OVERLAY_INFO_UNIT_TYPE.GetConstructors(INSTANCE_ALL);
			if (infoUnitCons.Length == 0)
			{
				PUtil.LogWarning("Unable to add NetworkingOverlay legend - no unit constructors");
				return null;
			}

			object infoUnit = null;
			var sprite = NetworkingOverlayMode.OverlayIcon;
			foreach (var cons in infoUnitCons)
			{
				var ps = cons.GetParameters();
				if (ps.Length == 6 &&
					ps[0].ParameterType == typeof(Sprite) &&
					ps[1].ParameterType == typeof(string) &&
					ps[2].ParameterType == typeof(Color) &&
					ps[3].ParameterType == typeof(Color) &&
					ps[4].ParameterType == typeof(object) &&
					ps[5].ParameterType == typeof(bool))
				{
					infoUnit = cons.Invoke(new object[] {
						sprite,
						"STRINGS.UI.OVERLAYS.NETWORKACTIVITY.DESCRIPTION",
						Color.white,
						Color.white,
						null,
						false
					});
					break;
				}
			}

			if (infoUnit == null)
			{
				foreach (var cons in infoUnitCons)
				{
					var ps = cons.GetParameters();
					if (ps.Length >= 4 &&
						ps[0].ParameterType == typeof(Sprite) &&
						ps[1].ParameterType == typeof(string) &&
						ps[2].ParameterType == typeof(Color) &&
						ps[3].ParameterType == typeof(Color))
					{
						var args = new object[ps.Length];
						args[0] = sprite;
						args[1] = STRINGS.UI.OVERLAYS.NETWORKACTIVITY.DESCRIPTION;
						args[2] = Color.white;
						args[3] = Color.white;
						for (int i = 4; i < ps.Length; i++)
						{
							if (ps[i].IsOptional)
								args[i] = ps[i].DefaultValue;
							else
								args[i] = null;
						}
						infoUnit = cons.Invoke(args);
						break;
					}
				}
			}

			if (infoUnit == null)
			{
				PUtil.LogWarning("Unable to add NetworkingOverlay legend - could not create unit");
				return null;
			}

			try
			{
				var overlayInfo = Activator.CreateInstance(OVERLAY_TYPE);
				var listType = typeof(List<>).MakeGenericType(OVERLAY_INFO_UNIT_TYPE);
				var unitList = Activator.CreateInstance(listType) as System.Collections.IList;
				unitList?.Add(infoUnit);

				var modeField = OVERLAY_TYPE.GetField("mode", INSTANCE_ALL);
				var nameField = OVERLAY_TYPE.GetField("name", INSTANCE_ALL);
				var infoUnitsField = OVERLAY_TYPE.GetField("infoUnits", INSTANCE_ALL);
				var progField = OVERLAY_TYPE.GetField("isProgrammaticallyPopulated", INSTANCE_ALL);

				modeField?.SetValue(overlayInfo, NetworkingOverlayMode.ID);
				nameField?.SetValue(overlayInfo, "STRINGS.UI.OVERLAYS.NETWORKACTIVITY.NAME");
				infoUnitsField?.SetValue(overlayInfo, unitList);
				progField?.SetValue(overlayInfo, true);

				return overlayInfo as OverlayLegend.OverlayInfo;
			}
			catch (Exception e)
			{
				PUtil.LogWarning("Unable to add NetworkingOverlay legend: " + e.Message);
				return null;
			}
		}

		[HarmonyPatch(typeof(SelectToolHoverTextCard), nameof(SelectToolHoverTextCard.OnSpawn))]
		public static class SelectToolHoverTextCard_OnSpawn_Patch
		{
			internal static void Postfix(SelectToolHoverTextCard __instance)
			{
				__instance.modeFilters[NetworkingOverlayMode.ID] =
					(KSelectable sel) => sel.GetComponent<NetworkIdentity>() != null;
				__instance.overlayFilterMap[NetworkingOverlayMode.ID] = () => false;
			}
		}

		[HarmonyPatch(typeof(SelectToolHoverTextCard), nameof(SelectToolHoverTextCard.UpdateHoverElements))]
		public static class SelectToolHoverTextCard_UpdateHoverElements_Patch
		{
			// Use a transpiler to insert this just before "EndDrawing" is called
			internal static IEnumerable<CodeInstruction> Transpiler(
				IEnumerable<CodeInstruction> instructions)
			{
				var codes = new List<CodeInstruction>(instructions);

				int endDrawingIdx = -1;
				for (int i = 0; i < codes.Count; i++)
				{
					if (codes[i].opcode == OpCodes.Callvirt &&
						codes[i].operand is MethodInfo mi &&
						mi.DeclaringType == typeof(HoverTextDrawer) &&
						mi.Name == nameof(HoverTextDrawer.EndDrawing))
					{
						endDrawingIdx = i;
						break;
					}
				}

				if (endDrawingIdx < 0)
				{
					PUtil.LogWarning("NetworkOverlay: Could not find HoverTextDrawer.EndDrawing(), skipping transpiler");
					return codes;
				}

				var inject = new List<CodeInstruction>
				{
					new CodeInstruction(OpCodes.Ldarg_0),
					new CodeInstruction(OpCodes.Call, AccessTools.Method(
						typeof(SelectToolHoverTextCard_UpdateHoverElements_Patch),
						nameof(DrawNetworkHoverText),
						new[] { typeof(SelectToolHoverTextCard) })),
				};

				codes.InsertRange(endDrawingIdx, inject);
				return codes;
			}

			private static void DrawNetworkHoverText(SelectToolHoverTextCard card)
			{
				if (SimDebugView.Instance?.GetMode() != NetworkingOverlayMode.ID)
					return;

				var hover = SelectTool.Instance?.hover;
				if (hover == null) return;
				var identity = hover.GetComponent<NetworkIdentity>();
				if (identity == null || identity.NetId == 0) return;

				var tracker = NetIdActivityTracker.Instance;
				if (tracker == null) return;

				float bps = tracker.GetBytesPerSecond(identity.NetId);
				var drawer = HoverTextScreen.Instance.drawer;

				drawer.BeginShadowBar(false);
				drawer.DrawText(STRINGS.UI.OVERLAYS.NETWORKACTIVITY.NAME,
					card.Styles_Title.Standard);
				drawer.NewLine();

				string usageStr;
				if (bps > 0f)
				{
					string formatted = Utils.FormatBytes((long)bps);
					if (bps >= NetworkingOverlayMode.HIGH_ACTIVITY_THRESHOLD)
						usageStr = string.Format(STRINGS.UI.OVERLAYS.NETWORKACTIVITY.HOVER_HIGH, formatted);
					else if (bps >= NetworkingOverlayMode.MEDIUM_ACTIVITY_THRESHOLD)
						usageStr = string.Format(STRINGS.UI.OVERLAYS.NETWORKACTIVITY.HOVER_MEDIUM, formatted);
					else
						usageStr = string.Format(STRINGS.UI.OVERLAYS.NETWORKACTIVITY.HOVER_LOW, formatted);
				}
				else
				{
					usageStr = STRINGS.UI.OVERLAYS.NETWORKACTIVITY.HOVER_IDLE;
				}

				drawer.DrawIcon(card.iconDash, Color.white);
				drawer.DrawText(
					string.Format(STRINGS.UI.OVERLAYS.NETWORKACTIVITY.HOVER_TOOLTIP, usageStr, identity.NetId),
					card.Styles_BodyText.Standard);
				drawer.EndShadowBar();
			}
		}
	}
}
