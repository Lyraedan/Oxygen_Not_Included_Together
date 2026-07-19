using Database;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Social
{
	public class ThoughtBubblePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		public int NetId;
		public bool IsVisible;
		public bool IsConvo;
		public string ThoughtId;
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
			writer.Write(ThoughtId ?? string.Empty);
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
			ThoughtId = reader.ReadString();
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

			Sprite bubbleSprite = null;
			Sprite topicSprite = null;
			Sprite modeSprite = null;
			string hoverText = HoverText;

			if (!string.IsNullOrEmpty(ThoughtId))
			{
				var thought = Db.Get().Thoughts.TryGet(ThoughtId);
				if (thought != null)
				{
					bubbleSprite = thought.bubbleSprite;
					topicSprite = thought.sprite;
					modeSprite = thought.modeSprite;
					hoverText = (string)thought.hoverText;
				}
			}

			if (bubbleSprite == null && !string.IsNullOrEmpty(BubbleSpriteName))
				bubbleSprite = Assets.GetSprite(BubbleSpriteName);

			if (topicSprite == null && !string.IsNullOrEmpty(TopicSpriteName))
				topicSprite = Assets.GetSprite(TopicSpriteName);
			
			if (topicSprite == null && !string.IsNullOrEmpty(ThoughtId) && ThoughtId.StartsWith("Topic_"))
			{
				var uiData = Def.GetUISprite(ThoughtId.Substring(6), "ui", centered: true);
				if (uiData != null)
					topicSprite = uiData.first;
			}

			if (IsConvo)
			{
				if (modeSprite == null && !string.IsNullOrEmpty(ModeSpriteName))
					modeSprite = Assets.GetSprite(ModeSpriteName);
				NameDisplayScreen.Instance.SetThoughtBubbleConvoDisplay(go, true, hoverText ?? string.Empty, bubbleSprite, topicSprite, modeSprite);
			}
			else
			{
				NameDisplayScreen.Instance.SetThoughtBubbleDisplay(go, true, hoverText ?? string.Empty, bubbleSprite, topicSprite);
			}
		}
	}
}
