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
	        public static void Prefix()
	        {
		        using var _ = Profiler.Scope();

		        // Add to the DiscordRichPresence.clusterWorldNames list
		        InspectLocString(typeof(CLUSTER_NAMES), ref DiscordRichPresence.clusterWorldNames, addString: true);
		        InspectLocString(typeof(WORLDS), ref DiscordRichPresence.clusterWorldNames, addString: true);
	        }
	        
			public static void Postfix()
            {
	            using var _ = Profiler.Scope();
	            
				Translate(typeof(STRINGS), true);
				// Update DiscordRichPresence.clusterWorldNames
				InspectLocString(typeof(CLUSTER_NAMES), ref DiscordRichPresence.clusterWorldNames, addString: false);
				InspectLocString(typeof(WORLDS), ref DiscordRichPresence.clusterWorldNames, addString: false);
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
			
			private static void InspectLocString(Type type, ref Dictionary<string, string> clusterWorldNames, string parent_path = "STRINGS.", bool addString = true)
			{
				string text = parent_path + type.Name + ".";

				FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
				foreach (FieldInfo fieldInfo in fields)
				{
					if (fieldInfo.FieldType == typeof(LocString) && fieldInfo.IsStatic)
					{
						string fullFieldPath = text + fieldInfo.Name;

						if (fullFieldPath.StartsWith("STRINGS.") && fullFieldPath.EndsWith(".NAME"))
						{
							var locString = (LocString)fieldInfo.GetValue(null);

							if (addString)
							{
								clusterWorldNames.Add(fullFieldPath, locString.text);
							}
							else
							{
								var foundPair = clusterWorldNames.FirstOrDefault(pair => pair.Key == fullFieldPath);
								if (!string.IsNullOrEmpty(foundPair.Key))
								{
									clusterWorldNames[foundPair.Key] = locString.text;
								}
							}
						}
					}
				}

				Type[] nestedTypes = type.GetNestedTypes(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
				foreach (Type nestedType in nestedTypes)
				{
					InspectLocString(nestedType, ref clusterWorldNames, text, addString);
				}
			}
		}
	}
}
