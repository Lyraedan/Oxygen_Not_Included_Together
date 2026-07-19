using System;
using System.Globalization;
using System.IO;

public static partial class SaveHelper
{
	internal static string GetMultiplayerSnapshotPath(
		string temporaryRoot,
		ulong hostId,
		long snapshotGeneration,
		string name)
	{
		if (string.IsNullOrEmpty(temporaryRoot) || hostId == 0 || snapshotGeneration <= 0)
			throw new ArgumentException("Invalid multiplayer snapshot identity");
		string baseName = Path.GetFileNameWithoutExtension(name);
		if (string.IsNullOrEmpty(baseName))
			throw new ArgumentException("Invalid multiplayer snapshot name", nameof(name));

		return Path.Combine(
			temporaryRoot, "ONI_Together", "MultiplayerSnapshots",
			hostId.ToString(CultureInfo.InvariantCulture),
			snapshotGeneration.ToString(CultureInfo.InvariantCulture), $"{baseName}.sav");
	}
}
