using System.IO;
using Shared.Profiling;

namespace ONI_Together.Misc
{
	public static class SecurePath
	{
		public static string Combine(string root, params string[] paths)
		{
			using var _ = Profiler.Scope();

			var absoluteRoot = Path.GetFullPath(root);
			var relativePath = Path.Combine(absoluteRoot, Path.Combine(paths));
			var absolutePath = Path.GetFullPath(relativePath);
			if (absolutePath == absoluteRoot || absolutePath.StartsWith(EnsureEndsWithSeparator(absoluteRoot)))
				return absolutePath;

			throw new IOException($"Unable to access \"{relativePath}\" as it's outside of \"{absoluteRoot}\"");
		}

		private static string EnsureEndsWithSeparator(string value) =>
				value.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

	}
}
