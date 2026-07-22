using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ONI_Together.UI.Components
{
	internal class DraggablePanel : KMonoBehaviour, IBeginDragHandler, IDragHandler
	{
		public Transform Target;
		public System.Action OnDragged;

		private Vector3 startMousePosition;
		private Vector3 startPosition;


		public void OnBeginDrag(PointerEventData eventData)
		{
			startPosition = Target.position;
			startMousePosition = eventData.position;
		}

		public void OnDrag(PointerEventData eventData)
		{
			Vector3 currentPosition = eventData.position;
			Vector3 diff = currentPosition - startMousePosition;

			Vector3 pos = startPosition + diff;

			Target.position = pos;
			OnDragged?.Invoke();
		}

	}
}
