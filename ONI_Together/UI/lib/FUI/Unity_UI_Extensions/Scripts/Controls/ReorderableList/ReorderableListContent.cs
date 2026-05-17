/// Credit Ziboo
/// Sourced from - http://forum.unity3d.com/threads/free-reorderable-list.364600/
/// 
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


using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UI.lib.UI.FUI.Unity_UI_Extensions.Scripts.Controls.ReorderableList
{
	[DisallowMultipleComponent]
	public class ReorderableListContent : MonoBehaviour
	{
		private List<Transform> _cachedChildren;
		private List<ReorderableListElement> _cachedListElement;
		private ReorderableListElement _ele;
		private ReorderableList _extList;
		private RectTransform _rect;
		private bool _started = false;

		private void OnEnable()
		{
			if (_rect)
			{
				StartCoroutine(RefreshChildren());
			}
		}


		public void OnTransformChildrenChanged()
		{
			if (isActiveAndEnabled)
			{
				StartCoroutine(RefreshChildren());
			}
		}

		public void Init(ReorderableList extList)
		{
			if (_started)
			{ StopCoroutine(RefreshChildren()); }

			_extList = extList;
			_rect = GetComponent<RectTransform>();
			_cachedChildren = new List<Transform>();
			_cachedListElement = new List<ReorderableListElement>();

			StartCoroutine(RefreshChildren());
			_started = true;
		}

		private IEnumerator RefreshChildren()
		{
			//Handle new children
			for (var i = 0; i < _rect.childCount; i++)
			{
				if (_cachedChildren.Contains(_rect.GetChild(i)))
				{
					continue;
				}

				//Get or Create ReorderableListElement
				_ele = _rect.GetChild(i).gameObject.GetComponent<ReorderableListElement>() ??
					   _rect.GetChild(i).gameObject.AddComponent<ReorderableListElement>();
				_ele.Init(_extList);

				_cachedChildren.Add(_rect.GetChild(i));
				_cachedListElement.Add(_ele);
			}

			//HACK a little hack, if I don't wait one frame I don't have the right deleted children
			yield return 0;

			//Remove deleted child
			for (var i = _cachedChildren.Count - 1; i >= 0; i--)
			{
				if (_cachedChildren[i] == null)
				{
					_cachedChildren.RemoveAt(i);
					_cachedListElement.RemoveAt(i);
				}
			}
		}
	}
}
