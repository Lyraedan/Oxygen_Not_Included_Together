using TMPro;
using UnityEngine;

namespace UI.lib
{
	internal class TMPImportFix : KMonoBehaviour
	{
		[SerializeField]
		public TextOverflowModes textOverflow;
		[SerializeField]
		public TextAlignmentOptions alignment;

		[SerializeField]
		public float fontSizeMin;
		[SerializeField]
		public float fontSizeMax;

		[SerializeField]
		public bool autoResize;

		[MyCmpReq]
#pragma warning disable IDE0044
		private LocText text;
#pragma warning restore IDE0044

		public override void OnSpawn()
		{
			base.OnSpawn();
			text.alignment = alignment;
			text.overflowMode = textOverflow;

			text.fontSizeMax = fontSizeMax;
			text.fontSizeMin = fontSizeMin;
			text.enableAutoSizing = autoResize;
			Destroy(this);
		}
	}
}
