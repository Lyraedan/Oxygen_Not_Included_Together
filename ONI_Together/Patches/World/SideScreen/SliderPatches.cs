using HarmonyLib;
using ONI_Together.Networking.Components;
using System;
using System.Collections;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;
using ONI_Together.DebugTools;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for slider-based side screens: SingleSliderSideScreen, IntSliderSideScreen, SingleCheckboxSideScreen
	/// </summary>

	/// <summary>
	/// Tracks the handler last registered on each side-screen control so that re-wiring
	/// a side screen (SetTarget runs every time a building is selected) removes the
	/// previous handler instead of accumulating a new one per call.
	/// The previous code used `evt -= () => ...; evt += () => ...`, but two
	/// separately-written lambdas are distinct delegate instances, so the `-=` removed
	/// nothing and every SetTarget leaked one more handler holding a stale target
	/// reference (issues #57 and #58).
	/// Side-screen controls are game-lifetime singletons (created once and reused by
	/// DetailsScreen), so a plain dictionary is safe: entries are replaced on every
	/// SetTarget and the keys are never destroyed mid-session.
	/// </summary>
	internal static class SideScreenHandlerCache
	{
		public static readonly Dictionary<KSlider, System.Action> ReleaseHandlers = new();
		public static readonly Dictionary<KNumberInputField, System.Action> EndEditHandlers = new();
		public static readonly Dictionary<KToggle, System.Action<bool>> CheckboxHandlers = new();
	}

	[HarmonyPatch(typeof(SingleSliderSideScreen), "SetTarget")]
	public static class SingleSliderSideScreen_SetTarget_Patch
	{
		public static void Postfix(SingleSliderSideScreen __instance, GameObject new_target)
		{
			using var _ = Profiler.Scope();

			if (new_target == null) return;

			var identity = new_target.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var sliderSets = Traverse.Create(__instance).Field("sliderSets").GetValue() as IList;
			if (sliderSets != null)
			{
				for (int i = 0; i < sliderSets.Count; i++)
				{
					var sliderSet = sliderSets[i];
					var slider = Traverse.Create(sliderSet).Field("valueSlider").GetValue<KSlider>();
					var numberInput = Traverse.Create(sliderSet).Field("numberInput").GetValue<KNumberInputField>();

					int index = i;
					if (slider != null)
					{
						if (SideScreenHandlerCache.ReleaseHandlers.TryGetValue(slider, out var previousRelease))
							slider.onReleaseHandle -= previousRelease;

						System.Action releaseHandler = () => OnSliderReleased(new_target, slider, index);
						SideScreenHandlerCache.ReleaseHandlers[slider] = releaseHandler;
						slider.onReleaseHandle += releaseHandler;
					}
					if (numberInput != null)
					{
						if (SideScreenHandlerCache.EndEditHandlers.TryGetValue(numberInput, out var previousEndEdit))
							numberInput.onEndEdit -= previousEndEdit;

						System.Action endEditHandler = () => OnInputEndEdit(new_target, numberInput, index);
						SideScreenHandlerCache.EndEditHandlers[numberInput] = endEditHandler;
						numberInput.onEndEdit += endEditHandler;
					}
				}
			}
		}

		private static void OnSliderReleased(GameObject target, KSlider slider, int index)
		{
			using var _ = Profiler.Scope();

			float value = slider.value;
			// Rounding for generators that use integer percentages
			if (ShouldRoundValue(target)) value = Mathf.Round(value);
			Send(target, value, index);
		}

		private static void OnInputEndEdit(GameObject target, KNumberInputField input, int index)
		{
			using var _ = Profiler.Scope();

			float value = input.currentValue;
			if (ShouldRoundValue(target)) value = Mathf.Round(value);
			Send(target, value, index);
		}

		private static bool ShouldRoundValue(GameObject target)
		{
			using var _ = Profiler.Scope();

			if (target == null)
			{
                DebugConsole.LogError("Target is null on SliderPatch->ShouldRoundValue defaulting to false");
				return false;
            }

			// ManualGenerator, EnergyGenerator (Coal), WoodGasGenerator, SpaceHeater all need rounding
			return target.GetComponent<ManualGenerator>() != null ||
			       target.GetComponent<EnergyGenerator>() != null ||
			       target.GetComponent<SpaceHeater>() != null;
		}

		private static void Send(GameObject target, float value, int index)
		{
			using var _ = Profiler.Scope();

			if (target == null) return;

			var comp = target.GetComponent<ISliderControl>() as Component;
			if (comp == null) comp = target.GetComponent<ISingleSliderControl>() as Component;
			if (comp != null) SideScreenSyncHelper.SyncSliderChange(comp, value, index);
		}
	}

	[HarmonyPatch(typeof(IntSliderSideScreen), "SetTarget")]
	public static class IntSliderSideScreen_SetTarget_Patch
	{
		public static void Postfix(IntSliderSideScreen __instance, GameObject new_target)
		{
			using var _ = Profiler.Scope();

			if (new_target == null) return;

			var identity = new_target.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var sliderSets = Traverse.Create(__instance).Field("sliderSets").GetValue() as IList;
			if (sliderSets != null)
			{
				for (int i = 0; i < sliderSets.Count; i++)
				{
					var sliderSet = sliderSets[i];
					var slider = Traverse.Create(sliderSet).Field("valueSlider").GetValue<KSlider>();
					var numberInput = Traverse.Create(sliderSet).Field("numberInput").GetValue<KNumberInputField>();

					int index = i;
					if (slider != null)
					{
						if (SideScreenHandlerCache.ReleaseHandlers.TryGetValue(slider, out var previousRelease))
							slider.onReleaseHandle -= previousRelease;

						System.Action releaseHandler = () => OnSliderReleased(new_target, slider, index);
						SideScreenHandlerCache.ReleaseHandlers[slider] = releaseHandler;
						slider.onReleaseHandle += releaseHandler;
					}
					if (numberInput != null)
					{
						if (SideScreenHandlerCache.EndEditHandlers.TryGetValue(numberInput, out var previousEndEdit))
							numberInput.onEndEdit -= previousEndEdit;

						System.Action endEditHandler = () => OnInputEndEdit(new_target, numberInput, index);
						SideScreenHandlerCache.EndEditHandlers[numberInput] = endEditHandler;
						numberInput.onEndEdit += endEditHandler;
					}
				}
			}
		}

		private static void OnSliderReleased(GameObject target, KSlider slider, int index)
		{
			using var _ = Profiler.Scope();

			Send(target, Mathf.Round(slider.value), index);
		}

		private static void OnInputEndEdit(GameObject target, KNumberInputField input, int index)
		{
			using var _ = Profiler.Scope();

			Send(target, Mathf.Round(input.currentValue), index);
		}

		private static void Send(GameObject target, float value, int index)
		{
			using var _ = Profiler.Scope();

			var comp = target.GetComponent<ISliderControl>() as Component;
			if (comp == null) comp = target.GetComponent<ISingleSliderControl>() as Component;
			if (comp != null) SideScreenSyncHelper.SyncSliderChange(comp, value, index);
		}
	}

	[HarmonyPatch(typeof(SingleCheckboxSideScreen), nameof(SingleCheckboxSideScreen.SetTarget))]
	public static class SingleCheckboxSideScreen_SetTarget_Patch
	{
		public static void Postfix(SingleCheckboxSideScreen __instance, GameObject target)
		{
			using var _ = Profiler.Scope();

			if (target == null) return;

			var identity = target.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var checkboxToggle = __instance.toggle;
			if (checkboxToggle != null)
			{
				if (SideScreenHandlerCache.CheckboxHandlers.TryGetValue(checkboxToggle, out var previousCheckbox))
					checkboxToggle.onValueChanged -= previousCheckbox;

				System.Action<bool> checkboxHandler = (value) => OnCheckboxClicked(target, value);
				SideScreenHandlerCache.CheckboxHandlers[checkboxToggle] = checkboxHandler;
				checkboxToggle.onValueChanged += checkboxHandler;
			}
		}

		private static void OnCheckboxClicked(GameObject target, bool value)
		{
			using var _ = Profiler.Scope();

			SideScreenSyncHelper.SyncCheckboxChange(target, value);
		}
	}
}
