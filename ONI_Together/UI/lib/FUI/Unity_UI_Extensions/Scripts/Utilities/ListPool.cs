using System.Collections.Generic;

namespace UI.lib.UI.FUI.Unity_UI_Extensions.Scripts.Utilities
{
	internal static class ListPool<T>
	{
		// Object pool to avoid allocations.
		private static readonly ObjectPool<List<T>> s_ListPool = new ObjectPool<List<T>>(null, Clear);
		static void Clear(List<T> l) { l.Clear(); }

		public static List<T> Get()
		{
			return s_ListPool.Get();
		}

		public static void Release(List<T> toRelease)
		{
			s_ListPool.Release(toRelease);
		}
	}
}
