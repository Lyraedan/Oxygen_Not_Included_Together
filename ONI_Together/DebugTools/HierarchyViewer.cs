using Shared.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ONI_Together.DebugTools
{
	public class HierarchyViewer : MonoBehaviour
	{
		private bool showWindow = false;
		private Vector2 scrollPos = Vector2.zero;

		public void Toggle()
		{
			using var _ = Profiler.Scope();

			showWindow = !showWindow;
		}

		void OnGUI()
		{
			using var _ = Profiler.Scope();

			if (!showWindow) return;

			GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
			{
				fontSize = 12,
				padding = new RectOffset(8, 8, 8, 8)
			};

			GUILayout.BeginArea(new Rect(10, 10, 400, Screen.height - 20), "Hierarchy", boxStyle);

			if (GUILayout.Button("Copy to Clipboard", GUILayout.Height(25)))
			{
				string hierarchyText = "";
				foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
				{
					hierarchyText += BuildHierarchyStringRecursive(root, 0);
				}
				GUIUtility.systemCopyBuffer = hierarchyText;
			}

			scrollPos = GUILayout.BeginScrollView(scrollPos, false, true);

			foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
			{
				DrawGameObjectRecursive(root, 0);
			}

			GUILayout.EndScrollView();
			GUILayout.EndArea();
		}

		private void DrawGameObjectRecursive(GameObject obj, int indent)
		{
			using var _ = Profiler.Scope();

			Component[] components = obj.GetComponents<Component>();
			string typeInfo = "GameObject";

			foreach (Component comp in components)
			{
				if (!(comp is Transform))
				{
					typeInfo = comp.GetType().Name;
					break;
				}
			}

			GUILayout.BeginHorizontal();
			GUILayout.Space(indent * 16);

			// Create a label style for measuring
			GUIContent labelContent = new GUIContent(obj.name);
			GUIStyle labelStyle = GUI.skin.label;
			Vector2 labelSize = labelStyle.CalcSize(labelContent);

			Rect labelRect = GUILayoutUtility.GetRect(labelContent, labelStyle);
			GUI.Label(labelRect, labelContent); // Draw the GameObject name

			// Check mouse hover
			if (labelRect.Contains(Event.current.mousePosition))
			{
				GUIStyle typeStyle = new GUIStyle(GUI.skin.label);
				typeStyle.normal.textColor = Color.cyan;

				GUIContent typeContent = new GUIContent($" [{typeInfo}]");
				Vector2 typeSize = typeStyle.CalcSize(typeContent);

				Rect typeRect = new Rect(labelRect.xMax, labelRect.y, typeSize.x, typeSize.y);
				GUI.Label(typeRect, typeContent, typeStyle);
			}

			GUILayout.EndHorizontal();

			foreach (Transform child in obj.transform)
			{
				DrawGameObjectRecursive(child.gameObject, indent + 1);
			}
		}

		private string BuildHierarchyStringRecursive(GameObject obj, int indent)
		{
			using var _ = Profiler.Scope();

			string indentStr = new string(' ', indent * 2);
			string typeInfo = "GameObject";

			foreach (Component comp in obj.GetComponents<Component>())
			{
				if (!(comp is Transform))
				{
					typeInfo = comp.GetType().Name;
					break;
				}
			}

			string result = $"{indentStr}- {obj.name} [{typeInfo}]\n";

			foreach (Transform child in obj.transform)
			{
				result += BuildHierarchyStringRecursive(child.gameObject, indent + 1);
			}

			return result;
		}
	}
}
