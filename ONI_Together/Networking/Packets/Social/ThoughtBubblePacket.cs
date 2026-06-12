using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Social
{
	public class ThoughtBubblePacket : IPacket
	{
		public int NetId;
		public bool IsVisible;
		public bool IsConvo;
		public string HoverText;
		public string BubbleSpriteName;
		public string TopicSpriteName;
		public string ModeSpriteName;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(IsVisible);
			writer.Write(IsConvo);
			writer.Write(HoverText ?? string.Empty);
			writer.Write(BubbleSpriteName ?? string.Empty);
			writer.Write(TopicSpriteName ?? string.Empty);
			writer.Write(ModeSpriteName ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			IsVisible = reader.ReadBoolean();
			IsConvo = reader.ReadBoolean();
			HoverText = reader.ReadString();
			BubbleSpriteName = reader.ReadString();
			TopicSpriteName = reader.ReadString();
			ModeSpriteName = reader.ReadString();
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
				NameDisplayScreen.Instance.SetThoughtBubbleDisplay(go, false, null, null, null);
				NameDisplayScreen.Instance.SetThoughtBubbleConvoDisplay(go, false, null, null, null, null);
				return;
			}

			Sprite bubbleSprite = !string.IsNullOrEmpty(BubbleSpriteName) ? Assets.GetSprite(BubbleSpriteName) : null;
			Sprite topicSprite = !string.IsNullOrEmpty(TopicSpriteName) ? Assets.GetSprite(TopicSpriteName) : null;

			if (IsConvo)
			{
				Sprite modeSprite = !string.IsNullOrEmpty(ModeSpriteName) ? Assets.GetSprite(ModeSpriteName) : null;
				NameDisplayScreen.Instance.SetThoughtBubbleConvoDisplay(go, true, HoverText, bubbleSprite, topicSprite, modeSprite);
			}
			else
			{
				NameDisplayScreen.Instance.SetThoughtBubbleDisplay(go, true, HoverText, bubbleSprite, topicSprite);
			}
		}
	}
}
