using Newtonsoft.Json;
using ONI_Together.DebugTools;
using System;
using Shared.Profiling;

namespace ONI_Together.Misc
{
	public static class SafeSerializer
	{
		private static readonly JsonSerializerSettings SafeSettings = new JsonSerializerSettings
		{
			ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
			Error = (sender, args) =>
			{
				args.ErrorContext.Handled = true; // Ignore individual property failures
			}
		};

		/// <summary>
		/// Safely serializes any object, skipping Unity objects, loops, and broken callbacks.
		/// </summary>
		public static string ToJson(object obj)
		{
			using var _ = Profiler.Scope();

			try
			{
				return JsonConvert.SerializeObject(obj, SafeSettings);
			}
			catch (Exception e)
			{
				DebugConsole.LogWarning($"[SafeSerializer] Failed to serialize object: {e.Message}");
				return null;
			}
		}

		/// <summary>
		/// Attempts to deserialize to the specified type safely.
		/// </summary>
		public static object FromJson(string json, Type type)
		{
			using var _ = Profiler.Scope();

			try
			{
				return JsonConvert.DeserializeObject(json, type);
			}
			catch (Exception e)
			{
				DebugConsole.LogWarning($"[SafeSerializer] Failed to deserialize to {type}: {e.Message}");
				return null;
			}
		}

		/// <summary>
		/// Attempts to deserialize to the specified generic type safely.
		/// </summary>
		public static T FromJson<T>(string json)
		{
			using var _ = Profiler.Scope();

			try
			{
				return JsonConvert.DeserializeObject<T>(json);
			}
			catch (Exception e)
			{
				DebugConsole.LogWarning($"[SafeSerializer] Failed to deserialize to {typeof(T)}: {e.Message}");
				return default;
			}
		}
	}
}
