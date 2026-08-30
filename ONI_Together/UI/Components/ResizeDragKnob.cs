using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ONI_Together.UI.Components
{

	internal class ResizeDragKnob : KMonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		public Transform Target;
		public Vector2 MinSize = new Vector2(205, 300);
		internal System.Action OnResized;
		private Vector3 ownStartPosition;

		private Vector3 startMousePosition;
		private Vector3 startPosition;

		public void OnBeginDrag(PointerEventData eventData)
		{
			ownStartPosition = transform.position;
			startPosition = Target.position;
			startMousePosition = eventData.position;
		}

		public void OnDrag(PointerEventData eventData)
		{
			Vector3 currentPosition = eventData.position;
			Vector3 diff = currentPosition - startMousePosition;
			Vector3 pos = ownStartPosition + diff;

			transform.position = pos;
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			transform.SetPosition(ownStartPosition);

			Vector3 endPosition = eventData.position;
			Vector2 diff = endPosition - startMousePosition;

			var scaler = Target.GetComponentInParent<CanvasScaler>();
			float uiscale = scaler.scaleFactor;
			var rect = Target.rectTransform();
			var size = rect.sizeDelta;
			size.y += (diff.y / uiscale);
			size.x -= (diff.x / uiscale);
			size.y = Mathf.Max(size.y, MinSize.y);
			size.x = Mathf.Max(size.x, MinSize.x);
			rect.sizeDelta = size;
			OnResized?.Invoke();
		}
	}
}
