using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ONI_Together_API
{
	public static class MP_Mod_Info
	{
		/// <summary>
		///     True if the multiplayer mod has been detected, false otherwise.
		///     Safe to access even if the multiplayer mod is not installed or active.
		/// </summary>
		[PublicAPI]
		public static bool MultiplayerModPresent => MainMpModType != null;

		/// <summary>
		///     The Type for the main multiplayer mod's UserMod2, if it exists. null if it cannot be found.
		///     Safe to access even if the multiplayer mod is not installed or active.
		/// </summary>
		[PublicAPI]
		[CanBeNull]
		public static Type MainMpModType => mainMpModType ??= Type.GetType(MPModTypeName);

		private static Type mainMpModType;

		private const string MPModTypeName = "ONI_Together.MultiplayerMod, ONI_Together";
	}
}
