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

				string code = Localization.GetLocale()?.Code;

				if (code.IsNullOrWhiteSpace()) return;

				string path = Path.Combine(ModPath, "translations", Localization.GetLocale().Code + ".po");

				if (File.Exists(path))
				{
					Localization.OverloadStrings(Localization.LoadStringsFile(path, false));
					DebugConsole.Log($"Found translation file for {code}.");
				}
			}
		}
	}
}
