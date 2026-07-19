using HarmonyLib;
using ONI_Together.Networking.Components;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Shared.Profiling;
using UnityEngine;
using ONI_Together.DebugTools;

namespace ONI_Together.Patches.World.SideScreen
{
	internal sealed class SliderUiBinding
	{
		public KSlider Slider;
		public KNumberInputField NumberInput;
		public System.Action Release;
		public System.Action EndEdit;

		public void Unbind()
		{
			if (Slider != null && Release != null) Slider.onReleaseHandle -= Release;
			if (NumberInput != null && EndEdit != null) NumberInput.onEndEdit -= EndEdit;
		}
	}

	[HarmonyPatch(typeof(SingleSliderSideScreen), "SetTarget")]
	public static class SingleSliderSideScreen_SetTarget_Patch
	{
		private static readonly ConditionalWeakTable<SingleSliderSideScreen, List<SliderUiBinding>> Bindings = new();

		public static void Postfix(SingleSliderSideScreen __instance, GameObject new_target)
		{
			using var _ = Profiler.Scope();

			ClearBindings(__instance);
			if (new_target == null) return;

			new_target.AddOrGet<NetworkIdentity>().RegisterIdentity();
			var bindings = BindControls(__instance.sliderSets, new_target, SendSlider, SendInput);
			Bindings.Add(__instance, bindings);
		}

		private static void SendSlider(GameObject target, KSlider slider, int index)
		{
			float value = slider.value;
			if (ShouldRoundValue(target)) value = Mathf.Round(value);
			Send(target, value, index);
		}

		private static void SendInput(GameObject target, KNumberInputField input, int index)
		{
			float value = input.currentValue;
			if (ShouldRoundValue(target)) value = Mathf.Round(value);
			Send(target, value, index);
		}

		private static bool ShouldRoundValue(GameObject target)
		{
			if (target == null)
			{
				DebugConsole.LogError("Target is null on SliderPatch->ShouldRoundValue defaulting to false");
				return false;
			}

			return target.GetComponent<ManualGenerator>() != null
			       || target.GetComponent<EnergyGenerator>() != null
			       || target.GetComponent<SpaceHeater>() != null;
		}

		private static void Send(GameObject target, float value, int index)
		{
			if (target == null) return;
			Component component = target.GetComponent<ISliderControl>() as Component
			                      ?? target.GetComponent<ISingleSliderControl>() as Component;
			if (component != null) SideScreenSyncHelper.SyncSliderChange(component, value, index);
		}

		private static void ClearBindings(SingleSliderSideScreen screen)
		{
			if (!Bindings.TryGetValue(screen, out List<SliderUiBinding> bindings)) return;
			foreach (SliderUiBinding binding in bindings) binding.Unbind();
			Bindings.Remove(screen);
		}

		internal static List<SliderUiBinding> BindControls(
			IList sliderSets,
			GameObject target,
			System.Action<GameObject, KSlider, int> sliderAction,
			System.Action<GameObject, KNumberInputField, int> inputAction)
		{
			var bindings = new List<SliderUiBinding>();
			if (sliderSets == null) return bindings;

			for (int i = 0; i < sliderSets.Count; i++)
			{
				object sliderSet = sliderSets[i];
				KSlider slider = Traverse.Create(sliderSet).Field("valueSlider").GetValue<KSlider>();
				KNumberInputField input = Traverse.Create(sliderSet).Field("numberInput").GetValue<KNumberInputField>();
				int index = i;
				var binding = new SliderUiBinding { Slider = slider, NumberInput = input };
				if (slider != null)
				{
					binding.Release = () => sliderAction(target, slider, index);
					slider.onReleaseHandle += binding.Release;
				}
				if (input != null)
				{
					binding.EndEdit = () => inputAction(target, input, index);
					input.onEndEdit += binding.EndEdit;
				}
				bindings.Add(binding);
			}

			return bindings;
		}
	}

	[HarmonyPatch(typeof(IntSliderSideScreen), "SetTarget")]
	public static class IntSliderSideScreen_SetTarget_Patch
	{
		private static readonly ConditionalWeakTable<IntSliderSideScreen, List<SliderUiBinding>> Bindings = new();

		public static void Postfix(IntSliderSideScreen __instance, GameObject new_target)
		{
			using var _ = Profiler.Scope();

			ClearBindings(__instance);
			if (new_target == null) return;

			new_target.AddOrGet<NetworkIdentity>().RegisterIdentity();
			var bindings = SingleSliderSideScreen_SetTarget_Patch.BindControls(
				__instance.sliderSets,
				new_target,
				(target, slider, index) => Send(target, Mathf.Round(slider.value), index),
				(target, input, index) => Send(target, Mathf.Round(input.currentValue), index));
			Bindings.Add(__instance, bindings);
		}

		private static void Send(GameObject target, float value, int index)
		{
			Component component = target?.GetComponent<ISliderControl>() as Component
			                      ?? target?.GetComponent<ISingleSliderControl>() as Component;
			if (component != null) SideScreenSyncHelper.SyncSliderChange(component, value, index);
		}

		private static void ClearBindings(IntSliderSideScreen screen)
		{
			if (!Bindings.TryGetValue(screen, out List<SliderUiBinding> bindings)) return;
			foreach (SliderUiBinding binding in bindings) binding.Unbind();
			Bindings.Remove(screen);
		}
	}

	[HarmonyPatch(typeof(SingleCheckboxSideScreen), nameof(SingleCheckboxSideScreen.SetTarget))]
	public static class SingleCheckboxSideScreen_SetTarget_Patch
	{
		private sealed class Binding
		{
			public KToggle Toggle;
			public System.Action<bool> Handler;
		}

		private static readonly ConditionalWeakTable<SingleCheckboxSideScreen, Binding> Bindings = new();

		public static void Postfix(SingleCheckboxSideScreen __instance, GameObject target)
		{
			using var _ = Profiler.Scope();

			ClearBinding(__instance);
			if (target == null) return;

			target.AddOrGet<NetworkIdentity>().RegisterIdentity();
			KToggle toggle = __instance.toggle;
			if (toggle == null) return;

			System.Action<bool> handler = value => SideScreenSyncHelper.SyncCheckboxChange(target, value);
			toggle.onValueChanged += handler;
			Bindings.Add(__instance, new Binding { Toggle = toggle, Handler = handler });
		}

		private static void ClearBinding(SingleCheckboxSideScreen screen)
		{
			if (!Bindings.TryGetValue(screen, out Binding binding)) return;
			if (binding.Toggle != null && binding.Handler != null)
				binding.Toggle.onValueChanged -= binding.Handler;
			Bindings.Remove(screen);
		}
	}
}
