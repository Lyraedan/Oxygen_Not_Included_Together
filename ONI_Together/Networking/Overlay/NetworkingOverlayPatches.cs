using HarmonyLib;
using ONI_Together.DebugTools;
using PeterHan.PLib.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
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

		[HarmonyPatch(typeof(OverlayMenu), "InitializeToggles")]
		public static class OverlayMenu_InitializeToggles_Patch
		{
			internal static void Postfix(ICollection<KIconToggleMenu.ToggleInfo> ___overlayToggleInfos)
			{
				var info = CreateOverlayToggleInfo(
					"Network",
					NetworkingOverlayMode.OVERLAY_ICON,
					NetworkingOverlayMode.ID,
					Action.NumActions,
					"Display network activity of synced objects"
				);
				if (info != null)
					___overlayToggleInfos?.Add(info);
			}
		}

		[HarmonyPatch(typeof(OverlayScreen), "RegisterModes")]
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

		[HarmonyPatch(typeof(SimDebugView), "OnPrefabInit")]
		public static class SimDebugView_OnPrefabInit_Patch
		{
			internal static void Postfix(IDictionary<HashedString, Func<SimDebugView, int, Color>> ___getColourFuncs)
			{
				___getColourFuncs[NetworkingOverlayMode.ID] = NetworkingOverlayMode.GetCellColor;
			}
		}

		[HarmonyPatch(typeof(OverlayLegend), "OnSpawn")]
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
			string text, string iconName, HashedString simView,
			Action hotKey, string tooltip)
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
			args[1] = iconName;
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

			return cons.Invoke(args) as KIconToggleMenu.ToggleInfo;
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
			var sprite = Assets.GetSprite(NetworkingOverlayMode.OVERLAY_ICON);
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
	}
}
