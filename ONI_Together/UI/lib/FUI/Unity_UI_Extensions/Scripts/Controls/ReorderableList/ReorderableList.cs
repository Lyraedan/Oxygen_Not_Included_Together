/// Credit Ziboo
/// Sourced from - http://forum.unity3d.com/threads/free-reorderable-list.364600/

/* # Unity UI Extensions License (BSD3)

Copyright (c) 2019

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer
   in the documentation and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived
   from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY,
OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE. */
using System;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;

namespace UI.lib.UI.FUI.Unity_UI_Extensions.Scripts.Controls.ReorderableList
{
	[RequireComponent(typeof(RectTransform)), DisallowMultipleComponent]
	[AddComponentMenu("UI/Extensions/Re-orderable list")]
	public class ReorderableList : MonoBehaviour
	{
		[Tooltip("Child container with re-orderable items in a layout group")]
		public LayoutGroup ContentLayout;
		[Tooltip("Parent area to draw the dragged element on top of containers. Defaults to the root Canvas")]
		public RectTransform DraggableArea;

		[Tooltip("Can items be dragged from the container?")]
		public bool IsDraggable = true;

		[Tooltip("Should the draggable components be removed or cloned?")]
		public bool CloneDraggedObject = false;

		[Tooltip("Can new draggable items be dropped in to the container?")]
		public bool IsDropable = true;

		[Tooltip("Should dropped items displace a current item if the list is full?\n " +
			"Depending on the dropped items origin list, the displaced item may be added, dropped in space or deleted.")]
		public bool IsDisplacable = false;

		// This sets every item size (when being dragged over this list) to the current size of the first element of this list
		[Tooltip("Should items being dragged over this list have their sizes equalized?")]
		public bool EqualizeSizesOnDrag = false;

		public int maxItems = int.MaxValue;


		[Header("UI Re-orderable Events")]
		public ReorderableListHandler OnElementDropped = new ReorderableListHandler();
		public ReorderableListHandler OnElementGrabbed = new ReorderableListHandler();
		public ReorderableListHandler OnElementRemoved = new ReorderableListHandler();
		public ReorderableListHandler OnElementAdded = new ReorderableListHandler();
		public ReorderableListHandler OnElementDisplacedFrom = new ReorderableListHandler();
		public ReorderableListHandler OnElementDisplacedTo = new ReorderableListHandler();
		public ReorderableListHandler OnElementDisplacedFromReturned = new ReorderableListHandler();
		public ReorderableListHandler OnElementDisplacedToReturned = new ReorderableListHandler();
		public ReorderableListHandler OnElementDroppedWithMaxItems = new ReorderableListHandler();

		private RectTransform _content;
		private ReorderableListContent _listContent;

		public RectTransform Content
		{
			get
			{
				if (_content == null)
				{
					_content = ContentLayout.GetComponent<RectTransform>();
				}
				return _content;
			}
		}

		private Canvas GetCanvas()
		{
			var t = transform;
			Canvas canvas = null;


			var lvlLimit = 100;
			var lvl = 0;

			while (canvas == null && lvl < lvlLimit)
			{
				canvas = t.gameObject.GetComponent<Canvas>();
				if (canvas == null)
				{
					t = t.parent;
				}

				lvl++;
			}
			return canvas;
		}

		/// <summary>
		/// Refresh related list content
		/// </summary>
		public void Refresh()
		{
			_listContent = ContentLayout.gameObject.AddOrGet<ReorderableListContent>();
			_listContent.Init(this);
		}
		private void Start()
		{

			if (ContentLayout == null)
			{
				Debug.LogError("You need to have a child LayoutGroup content set for the list: " + name, gameObject);
				return;
			}
			if (DraggableArea == null)
			{
				DraggableArea = transform.root.GetComponentInChildren<Canvas>().GetComponent<RectTransform>();
			}
			if (IsDropable && !GetComponent<Graphic>())
			{
				Debug.LogError("You need to have a Graphic control (such as an Image) for the list [" + name + "] to be droppable", gameObject);
				return;
			}

			Refresh();
		}


		#region Nested type: ReorderableListEventStruct

		[Serializable]
		public struct ReorderableListEventStruct
		{
			public GameObject DroppedObject;
			public int FromIndex;
			public ReorderableList FromList;
			public bool IsAClone;
			public GameObject SourceObject;
			public int ToIndex;
			public ReorderableList ToList;

			public void Cancel()
			{
				SourceObject.GetComponent<ReorderableListElement>().isValid = false;
			}
		}

		#endregion


		#region Nested type: ReorderableListHandler

		[Serializable]
		public class ReorderableListHandler : UnityEvent<ReorderableListEventStruct>
		{
		}

		public void TestReOrderableListTarget(ReorderableListEventStruct item)
		{
			Debug.Log("Event Received");
			Debug.Log("Hello World, is my item a clone? [" + item.IsAClone + "]");
		}

		#endregion
	}
}
