using HarmonyLib;
using KMod;
using System.Collections;
using UnityEngine;
using static UI.lib.UIcmp.Helper;

namespace UI.lib.UIcmp //Source: Aki
{
	public class ModMenuButton
	{
		/// <summary> Clones the Subscription button to add a Settings button next to it. </summary>
		public static void AddModSettingsButton(object displayedMods, string modName, System.Action OnClick, string label = "Settings")
		{
			foreach (var modEntry in (IEnumerable)displayedMods)
			{
				if (TryAddButton(modEntry, modName, OnClick, label))
				{
					break;
				}
			}
		}

		public static void AddCustomModSettingsButton(object displayedMods, string modPath, System.Action OnClick, GameObject prefab)
		{
			foreach (var modEntry in (IEnumerable)displayedMods)
			{
				var index = Traverse.Create(modEntry).Field("mod_index").GetValue<int>();
				var mod = Global.Instance.modManager.mods[index];

				if (IsThisMyMod(index, mod, modPath))
				{
					var transform = Traverse.Create(modEntry).Field("rect_transform").GetValue<RectTransform>();
					if (transform != null)
					{
						var kbutton = Util.KInstantiateUI<KButton>(prefab, transform.Find("ManageButton").parent.gameObject, true);
						kbutton.transform.position = Vector3.zero;
						kbutton.onClick += OnClick;
						kbutton.transform.SetSiblingIndex(index);
					}
				}
			}
		}

		private static bool TryAddButton(object modEntry, string modID, System.Action action, string label)
		{
			var index = Traverse.Create(modEntry).Field("mod_index").GetValue<int>();
			var mod = Global.Instance.modManager.mods[index];

			if (IsThisMyMod(index, mod, modID))
			{
				MakeButton(modEntry, action, label);
				return true;
			}

			return false;
		}

		private static bool IsThisMyMod(int index, Mod mod, string ID)
		{
			//return index >= 0 && FileSystem.Normalize(mod.file_source.GetRoot()) == FileSystem.Normalize(path);
			return mod.staticID == ID && mod.IsEnabledForActiveDlc();
		}

		private static void MakeButton(object modEntry, System.Action action, string label)
		{
			var transform = Traverse.Create(modEntry).Field("rect_transform").GetValue<RectTransform>();

			if (transform != null)
			{
				CloneButton(transform.Find("ManageButton"), action, label);
			}
		}

		private static void CloneButton(Transform prefab, System.Action action, string label)
		{
			if (prefab == null)
			{
				return;
			}

			var info = new ButtonInfo(label, action, 14);
			MakeKButton(info, prefab.gameObject, prefab.parent.gameObject, prefab.GetSiblingIndex() - 1);
		}
	}
}
