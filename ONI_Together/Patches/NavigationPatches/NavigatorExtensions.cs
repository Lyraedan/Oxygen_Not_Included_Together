using System.Collections.Generic;
using System.Reflection;
using Shared.Profiling;

namespace ONI_Together.Patches.Navigation
{
	public static class NavigatorExtensions
	{
		private static readonly Dictionary<Navigator, bool> canAdvanceMap = new Dictionary<Navigator, bool>();

		private static readonly FieldInfo tacticField =
				typeof(Navigator).GetField("tactic", BindingFlags.NonPublic | BindingFlags.Instance);

		private static readonly FieldInfo targetOffsetsField =
				typeof(Navigator).GetField("<targetOffsets>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

		private static readonly MethodInfo clearReservedCellMethod =
				typeof(Navigator).GetMethod("ClearReservedCell", BindingFlags.NonPublic | BindingFlags.Instance);

		public static bool GetCanAdvance(this Navigator navigator)
		{
			using var _ = Profiler.Scope();

			bool value;
			return canAdvanceMap.TryGetValue(navigator, out value) && value;
		}

		public static void SetCanAdvance(this Navigator navigator, bool value)
		{
			using var _ = Profiler.Scope();

			if (value)
				canAdvanceMap[navigator] = true;
			else
				canAdvanceMap.Remove(navigator);
		}

		/// <summary>
		/// Safe alternative to Navigator.GoTo that clients can use.
		/// Avoids Harmony-blocked methods.
		/// </summary>
		public static bool ClientGoTo(this Navigator navigator, KMonoBehaviour target, CellOffset[] offsets, NavTactic tactic = null)
		{
			using var _ = Profiler.Scope();

			if (navigator == null || target == null || navigator.smi == null || navigator.smi.sm == null)
				return false;

			if (tactic == null)
			{
				tactic = NavigationTactics.ReduceTravelDistance;
			}

			navigator.smi.GoTo(navigator.smi.sm.normal.moving);
			navigator.smi.sm.moveTarget.Set(target.gameObject, navigator.smi);

			tacticField?.SetValue(navigator, tactic);
			navigator.target = target;
			targetOffsetsField?.SetValue(navigator, offsets);
			clearReservedCellMethod?.Invoke(navigator, null);

			navigator.SetCanAdvance(true);
			navigator.AdvancePath();

			return navigator.IsMoving();
		}
	}
}
