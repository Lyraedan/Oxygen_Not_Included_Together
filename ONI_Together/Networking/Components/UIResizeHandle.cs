using System;
using Shared.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[Flags]
public enum ResizeEdge
{
	None = 0,
	Left = 1,
	Right = 2,
	Top = 4,
	Bottom = 8,
}

public class UIResizeHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler
{
	public RectTransform target;
	public ResizeEdge edges;
	public Vector2 minSize = new Vector2(200, 120);
	public Vector2 maxSize = new Vector2(800, 600);

	private Vector2 dragStart;
	private Vector2 startSize;
	private Vector2 startPos;
	private Image image;

	private static readonly Color hoverColor = new Color(1f, 1f, 1f, 0.25f);

	private void Awake()
	{
		using var _ = Profiler.Scope();

		image = GetComponent<Image>();
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		using var _ = Profiler.Scope();

		if (image != null)
			image.color = hoverColor;
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		using var _ = Profiler.Scope();

		if (image != null)
			image.color = Color.clear;
	}

	public void OnPointerDown(PointerEventData eventData)
	{
		using var _ = Profiler.Scope();

		RectTransformUtility.ScreenPointToLocalPointInRectangle(
			target.parent as RectTransform, eventData.position, eventData.pressEventCamera, out dragStart);
		startSize = target.sizeDelta;
		startPos = target.anchoredPosition;
	}

	public void OnDrag(PointerEventData eventData)
	{
		using var _ = Profiler.Scope();

		if (target == null)
			return;

		RectTransform parent = target.parent as RectTransform;
		if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out Vector2 current))
			return;

		Vector2 delta = current - dragStart;
		Vector2 newSize = startSize;
		Vector2 newPos = startPos;

		if ((edges & ResizeEdge.Right) != 0)
		{
			newSize.x = startSize.x + delta.x;
			newPos.x = startPos.x + delta.x * 0.5f;
		}
		else if ((edges & ResizeEdge.Left) != 0)
		{
			newSize.x = startSize.x - delta.x;
			newPos.x = startPos.x + delta.x * 0.5f;
		}

		if ((edges & ResizeEdge.Top) != 0)
		{
			newSize.y = startSize.y + delta.y;
		}
		else if ((edges & ResizeEdge.Bottom) != 0)
		{
			newSize.y = startSize.y - delta.y;
			newPos.y = startPos.y + delta.y;
		}

		Vector2 clampedSize = new Vector2(
			Mathf.Clamp(newSize.x, minSize.x, maxSize.x),
			Mathf.Clamp(newSize.y, minSize.y, maxSize.y));

		if ((edges & ResizeEdge.Right) != 0)
			newPos.x = startPos.x + (clampedSize.x - startSize.x) * 0.5f;
		else if ((edges & ResizeEdge.Left) != 0)
			newPos.x = startPos.x - (clampedSize.x - startSize.x) * 0.5f;

		if ((edges & ResizeEdge.Bottom) != 0)
			newPos.y = startPos.y - (clampedSize.y - startSize.y);

		target.sizeDelta = clampedSize;
		target.anchoredPosition = newPos;
	}

}
