using Database;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Social
{
	public class DreamBubblePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxIconCount = 64;
		private const int MaxIdLength = 256;
		public int NetId;
		public bool IsVisible;
		public string DreamId;
		public string BackgroundAnim;
		public string[] IconSpriteNames;
		public float SecondPerImage;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(IsVisible);
			writer.Write(DreamId ?? string.Empty);
			writer.Write(BackgroundAnim ?? string.Empty);

			if (IconSpriteNames != null)
			{
				writer.Write(IconSpriteNames.Length);
				foreach (var name in IconSpriteNames)
					writer.Write(name ?? string.Empty);
			}
			else
			{
				writer.Write(0);
			}

			writer.Write(SecondPerImage);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			IsVisible = reader.ReadBoolean();
			DreamId = reader.ReadString();
			BackgroundAnim = reader.ReadString();
			if (DreamId.Length > MaxIdLength || BackgroundAnim.Length > MaxIdLength)
				throw new InvalidDataException("Dream bubble ID is too long");

			int iconCount = reader.ReadInt32();
			if (iconCount < 0 || iconCount > MaxIconCount)
				throw new InvalidDataException($"Invalid dream icon count: {iconCount}");
			IconSpriteNames = new string[iconCount];
			for (int i = 0; i < iconCount; i++)
			{
				IconSpriteNames[i] = reader.ReadString();
				if (IconSpriteNames[i].Length > MaxIdLength)
					throw new InvalidDataException("Dream icon ID is too long");
			}

			SecondPerImage = reader.ReadSingle();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGet(NetId, out var identity))
				return;

			var go = identity.gameObject;
			if (go.IsNullOrDestroyed())
				return;

			if (!IsVisible)
			{
				NameDisplayScreen.Instance.StopDreaming(go);
				return;
			}

			Dream dream = null;
			if (!string.IsNullOrEmpty(DreamId))
				dream = Db.Get().Dreams.TryGet(DreamId);

			if (dream == null && !string.IsNullOrEmpty(BackgroundAnim) && IconSpriteNames != null && IconSpriteNames.Length > 0)
			{
				dream = new Dream(DreamId ?? "Dream", Db.Get().Dreams, BackgroundAnim, IconSpriteNames, SecondPerImage);
			}

			if (dream != null)
				NameDisplayScreen.Instance.SetDream(go, dream);
		}
	}
}
