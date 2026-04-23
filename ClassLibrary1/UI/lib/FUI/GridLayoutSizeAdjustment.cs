using ONI_MP.DebugTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_MP.UI.lib.FUI
{
	public class GridLayoutSizeAdjustment : KMonoBehaviour
	{
		[MyCmpReq]
#pragma warning disable IDE0044 
		GridLayoutGroup referencedLayoutGroup;
		[MyCmpGet]
		RectTransform rectTransform;
#pragma warning restore IDE0044 

		int paddingTop, paddingBottom, paddingLeft, paddingRight;
		float WidthToHeightRatio;

		public int minSize = 80, maxSize = 120;

		public bool allignWithWidth = true; //otherwise allign with height
		float spacingX, spacingY;

		public override void OnSpawn()
		{
			base.OnSpawn();
			paddingBottom = referencedLayoutGroup.padding.bottom;
			paddingLeft = referencedLayoutGroup.padding.left;
			paddingTop = referencedLayoutGroup.padding.top;
			paddingRight = referencedLayoutGroup.padding.right;
			spacingX = referencedLayoutGroup.spacing.x;
			spacingY = referencedLayoutGroup.spacing.y;

			StartCoroutine(GetRatio());
		}
		bool sizeRatioSet = false;
		IEnumerator GetRatio()
		{
			yield return new WaitForEndOfFrame();
			WidthToHeightRatio = referencedLayoutGroup.cellSize.x / referencedLayoutGroup.cellSize.y;
			sizeRatioSet = true;
			RequestGridResize();
		}

		public void SetValues(int minSize, int maxSize, bool allignWithWidth = true)
		{
			this.minSize = Math.Min(minSize, maxSize);
			this.maxSize = Math.Max(maxSize, minSize);
			this.allignWithWidth = allignWithWidth;
			RequestGridResize();
		}

		public void RequestGridResize()
		{
			if (!sizeRatioSet)
			{
				return;
			}

			//targetGridLayout.padding.left - (float)targetGridLayout.padding.right
			float SizeToFit = allignWithWidth
				// Total Size                 minus paddings          plus 1x spacing as there is always 1 spacing less than there are items in a row/column
				? rectTransform.rect.width - paddingLeft - paddingRight + spacingX - 1
				: rectTransform.rect.height - paddingTop - paddingBottom + spacingY - 1;

			DebugConsole.Log(referencedLayoutGroup.padding.left + "," + referencedLayoutGroup.padding.right + "," + rectTransform.rect.width);
			List<Tuple<int, float>> outputValues = new List<Tuple<int, float>>();

			for (int newSize = minSize; newSize <= maxSize; newSize++)
			{
				float relevantCellSizeParam = allignWithWidth ? newSize + spacingX : newSize + spacingY;
				float amountItFitsIn = SizeToFit / relevantCellSizeParam;

				float trunctuated = (float)Math.Truncate(amountItFitsIn);

				float percentageLeft = amountItFitsIn - trunctuated;
				outputValues.Add(new Tuple<int, float>(newSize, percentageLeft));
				DebugConsole.Log(relevantCellSizeParam + " : " + amountItFitsIn + " : " + trunctuated + " : " + percentageLeft);
			}
			outputValues = outputValues.OrderBy(tu => tu.second).ToList();
			foreach (var va in outputValues)
			{
				DebugConsole.Log(va.second.ToString());
			}
			var newSizeValue = outputValues.First().first;
			DebugConsole.Log(newSizeValue.ToString() + ", sizeRatio " + WidthToHeightRatio);


			var NewCellSize = new Vector2(
				(allignWithWidth ? newSizeValue : newSizeValue * WidthToHeightRatio),
				(!allignWithWidth ? newSizeValue : newSizeValue / WidthToHeightRatio)
				);

			DebugConsole.Log(NewCellSize.ToString());

			referencedLayoutGroup.cellSize = NewCellSize;

			//return;
			//if (allignWithWidth)
			//	referencedLayoutGroup.SetLayoutHorizontal();
			//else
			//	referencedLayoutGroup.SetLayoutVertical();
		}
	}
}
