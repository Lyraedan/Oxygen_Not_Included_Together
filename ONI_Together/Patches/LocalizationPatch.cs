using HarmonyLib;
using ONI_Together.DebugTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using STRINGS;

namespace ONI_Together.Patches
{
	internal class LocalizationPatch
	{

        [HarmonyPatch(typeof(Localization), nameof(Localization.Initialize))]
        public class Localization_Initialize_Patch
        {
			public static void Postfix()
            {
	            using var _ = Profiler.Scope();
				Translate(typeof(STRINGS), true);
            }

			static string ModPath => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

			public static void Translate(Type root, bool generateTemplate = false)
			{
				using var _ = Profiler.Scope();

				Localization.RegisterForTranslation(root);
				OverLoadStrings();
				LocString.CreateLocStringKeys(root, null);

				if (generateTemplate)
				{
					var translationFolder = Path.Combine(ModPath, "translations");
					Directory.CreateDirectory(translationFolder);
					Localization.GenerateStringsTemplate(root.Namespace, Assembly.GetExecutingAssembly(), Path.Combine(translationFolder, "translation_template.pot"), null);
				}
			}

			// Loads user created translations
			private static void OverLoadStrings()
			{
				using var _ = Profiler.Scope();

				var locale = Localization.GetLocale();
				string code = locale?.Code;

				if (code.IsNullOrWhiteSpace())
					return;

				string[] candidateCodes = new[]
				{
					code,
					code.ToLowerInvariant(),
					code.Split('-')[0],
					code.Split('_')[0],
					(code.ToLowerInvariant().Contains("de") || code.ToLowerInvariant().Contains("german")) ? "de" : null,
					(code.ToLowerInvariant().Contains("pl") || code.ToLowerInvariant().Contains("polish")) ? "pl" : null
				}.Where(c => !string.IsNullOrEmpty(c)).Distinct().ToArray();

				string[] candidateDirs = new[]
				{
					Path.Combine(ModPath, "translations"),
					Path.Combine(ModPath, "ModAssets", "translations")
				};

				foreach (var dir in candidateDirs)
				{
					if (!Directory.Exists(dir))
						continue;

					foreach (var cand in candidateCodes)
					{
						string path = Path.Combine(dir, cand + ".po");
						if (File.Exists(path))
						{
							try
							{
								var strings = Localization.LoadStringsFile(path, false);
								if (strings != null && strings.Count > 0)
								{
									Localization.OverloadStrings(strings);
									DebugConsole.Log($"[Localization] Loaded translation file for {code} from {path} ({strings.Count} strings).");
									return;
								}
							}
							catch (Exception ex)
							{
								DebugConsole.LogError($"[Localization] Failed to load translation file {path}: {ex}");
							}
						}
					}
				}
			}
		}
	}
}
