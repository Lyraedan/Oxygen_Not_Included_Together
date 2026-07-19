using System.IO;
using System.Text;

namespace ONI_Together.Networking.Synchronization
{
	internal static class DuplicantDeathWire
	{
		private static readonly UTF8Encoding StrictUtf8 = new(false, true);
		internal const int MaxDeathIdBytes = DuplicantDeathSync.MaxDeathIdLength * 4;

		internal static void WriteDeathId(BinaryWriter writer, string deathId)
		{
			if (!DuplicantDeathSync.IsValidDeathId(deathId))
				throw new InvalidDataException("Invalid duplicant death id");
			byte[] bytes = StrictUtf8.GetBytes(deathId);
			if (bytes.Length > MaxDeathIdBytes)
				throw new InvalidDataException("Duplicant death id exceeds byte limit");
			writer.Write(bytes.Length);
			writer.Write(bytes);
		}

		internal static string ReadDeathId(BinaryReader reader)
		{
			int byteCount = reader.ReadInt32();
			if (byteCount <= 0 || byteCount > MaxDeathIdBytes)
				throw new InvalidDataException("Invalid duplicant death id byte length");
			byte[] bytes = reader.ReadBytes(byteCount);
			if (bytes.Length != byteCount)
				throw new EndOfStreamException("Truncated duplicant death id");
			string deathId;
			try
			{
				deathId = StrictUtf8.GetString(bytes);
			}
			catch (DecoderFallbackException exception)
			{
				throw new InvalidDataException("Duplicant death id is not valid UTF-8", exception);
			}
			if (!DuplicantDeathSync.IsValidDeathId(deathId))
				throw new InvalidDataException("Invalid duplicant death id");
			return deathId;
		}
	}
}
