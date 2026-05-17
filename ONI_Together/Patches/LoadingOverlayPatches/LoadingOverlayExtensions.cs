using System.Reflection;
using Shared.Profiling;

namespace ONI_Together.Patches.LoadingOverlayPatch
{
	public static class LoadingOverlayExtensions
	{
		public static LoadingOverlay GetSingleton()
		{
			using var _ = Profiler.Scope();

			var type = typeof(LoadingOverlay);
			var field = type.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
			return (LoadingOverlay)field.GetValue(null);
		}
	}
}
