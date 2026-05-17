
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
using UnityEngine;
using UnityEngine.UI;

/// Credit Ziboo
/// Sourced from - http://forum.unity3d.com/threads/free-reorderable-list.364600/
namespace UI.lib.UI.FUI.Unity_UI_Extensions.Scripts.Controls.ReorderableList
{
	public class ReorderableListDebug : MonoBehaviour
	{
		public Text DebugLabel;

		private void Awake()
		{
			foreach (var list in FindObjectsByType<ReorderableList>(FindObjectsSortMode.None))
			{
				list.OnElementDropped.AddListener(ElementDropped);
			}
		}

		private void ElementDropped(ReorderableList.ReorderableListEventStruct droppedStruct)
		{
			DebugLabel.text = "";
			DebugLabel.text += "Dropped Object: " + droppedStruct.DroppedObject.name + "\n";
			DebugLabel.text += "Is Clone ?: " + droppedStruct.IsAClone + "\n";
			if (droppedStruct.IsAClone)
			{
				DebugLabel.text += "Source Object: " + droppedStruct.SourceObject.name + "\n";
			}

			DebugLabel.text += string.Format("From {0} at Index {1} \n", droppedStruct.FromList.name, droppedStruct.FromIndex);
			DebugLabel.text += string.Format("To {0} at Index {1} \n", droppedStruct.ToList == null ? "Empty space" : droppedStruct.ToList.name, droppedStruct.ToIndex);
		}
	}
}
