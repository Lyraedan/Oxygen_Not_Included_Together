using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_Together.UI.Components
{
	internal class ChatMessageContainer : KMonoBehaviour
	{
		[SerializeField]
		LocText Sender, TimeStamp, Text;
		bool init = false;
		string _sender, _stamp, _text;
		bool spawned = false;
		bool frozen = false;
		Image ColorableBorder;

		static Color defaultBG = Util.ColorFromHex("b0b0b0");

		void Init()
		{
			if (init)
				return;
			init = true;
			Sender = transform.Find("Label").gameObject.GetComponent<LocText>();
			Sender.key = string.Empty;
			TimeStamp = transform.Find("TimeStamp").gameObject.GetComponent<LocText>();
			TimeStamp.key = string.Empty;
			Text = transform.Find("Message").gameObject.GetComponent<LocText>();
			ColorableBorder = GetComponent<Image>();
			Text.key = string.Empty;
			Text.AllowLinks = true;
		}
		public void SetValues(string sender, string stamp, string text, Color bgColor = default)
		{
			if(bgColor == default)
				bgColor = Color.white;

			Init();
			_sender = sender;
			_stamp = stamp;
			_text = text;
			if (spawned)
				ApplyText();
			ColorableBorder.color = defaultBG * bgColor;
		}
		void ApplyText()
		{
			Init();
			Sender.text = _sender;
			TimeStamp.text = _stamp;
			Text.text = _text;


			//if (!frozen && gameObject.activeInHierarchy)
			//	StartCoroutine(FreezeLayoutEnumerator());
		}
		public override void OnPrefabInit()
		{
			base.OnPrefabInit();
			Init();
		}
		public override void OnSpawn()
		{
			base.OnSpawn();
			spawned = true;
			ApplyText();

		}

		IEnumerator FreezeLayoutEnumerator()
		{
			yield return null;
			FreezeDimensions();
			frozen = true;
		}

		void FreezeDimensions()
		{
			if (transform.TryGetComponent(out LayoutGroup group))
			{
				gameObject.AddOrGet<LayoutElement>().CopyFrom(group);
				Destroy(group);
			}
		}
	}
	static class LayoutElementHelper
	{
		/// <summary>
		/// Copies layout information to a fixed layout element. Useful for freezing a UI
		/// object.
		/// </summary>
		/// <param name="dest">The fixed layout component that will replace it.</param>
		/// <param name="src">The current layout component.</param>
		public static void CopyFrom(this LayoutElement dest, ILayoutElement src)
		{
			dest.flexibleHeight = src.flexibleHeight;
			dest.flexibleWidth = src.flexibleWidth;
			dest.preferredHeight = src.preferredHeight;
			dest.preferredWidth = src.preferredWidth;
			dest.minHeight = src.minHeight;
			dest.minWidth = src.minWidth;
		}
	}
}
