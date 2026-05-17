using Shared.Profiling;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_Together.Networking.Components
{
	public class PingMarker : MonoBehaviour
	{
		private const float Duration = 5f;
		private const float FadeStart = 3.5f;
		private const float MarkerSize = 28f;
		private const float ArrowSize = 20f;
		private const float ScreenEdgeMargin = 40f;
		private const float PulseSpeed = 3f;
		private const float PulseMin = 0.8f;
		private const float PulseMax = 1.2f;

		private static Sprite cachedRingSprite;
		private static Sprite cachedArrowSprite;

		private Image ringImage;
		private Image arrowImage;
		private TextMeshProUGUI nameLabel;
		private Color baseColor;
		private float spawnTime;
		private Vector3 worldPosition;
		private Camera uiCamera;

		public void Init(Vector3 worldPos, Color color, string playerName)
		{
			using var _ = Profiler.Scope();

			worldPosition = worldPos;
			baseColor = color;
			spawnTime = Time.unscaledTime;
			uiCamera = GameScreenManager.Instance.GetCamera(GameScreenManager.UIRenderTarget.ScreenSpaceCamera);

			var rectTransform = gameObject.AddComponent<RectTransform>();
			rectTransform.sizeDelta = new Vector2(MarkerSize, MarkerSize);

			var ringGO = new GameObject("Ring");
			ringGO.transform.SetParent(transform, false);
			var ringRect = ringGO.AddComponent<RectTransform>();
			ringRect.sizeDelta = new Vector2(MarkerSize, MarkerSize);
			ringImage = ringGO.AddComponent<Image>();
			if (cachedRingSprite == null)
				cachedRingSprite = CreateRingSprite();
			ringImage.sprite = cachedRingSprite;
			ringImage.color = baseColor;
			ringImage.raycastTarget = false;

			var arrowGO = new GameObject("Arrow");
			arrowGO.transform.SetParent(transform, false);
			var arrowRect = arrowGO.AddComponent<RectTransform>();
			arrowRect.sizeDelta = new Vector2(ArrowSize, ArrowSize);
			arrowImage = arrowGO.AddComponent<Image>();
			if (cachedArrowSprite == null)
				cachedArrowSprite = CreateArrowSprite();
			arrowImage.sprite = cachedArrowSprite;
			arrowImage.color = baseColor;
			arrowImage.raycastTarget = false;
			arrowImage.enabled = false;

			var labelGO = new GameObject("Label");
			labelGO.transform.SetParent(transform, false);
			var labelRect = labelGO.AddComponent<RectTransform>();
			labelRect.sizeDelta = new Vector2(100, 20);
			labelRect.anchoredPosition = new Vector2(0, MarkerSize * 0.5f + 4f);
			nameLabel = labelGO.AddComponent<TextMeshProUGUI>();
			nameLabel.text = playerName;
			nameLabel.fontSize = 12;
			nameLabel.font = Localization.FontAsset;
			nameLabel.color = baseColor;
			nameLabel.alignment = TextAlignmentOptions.Center;
			nameLabel.raycastTarget = false;
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			float elapsed = Time.unscaledTime - spawnTime;
			if (elapsed >= Duration)
			{
				Destroy(gameObject);
				return;
			}

			UpdateScreenPosition();

			float pulse = Mathf.Lerp(PulseMin, PulseMax, (Mathf.Sin(elapsed * PulseSpeed) + 1f) * 0.5f);
			ringImage.rectTransform.localScale = new Vector3(pulse, pulse, 1f);

			float alpha = 1f;
			if (elapsed > FadeStart)
				alpha = 1f - (elapsed - FadeStart) / (Duration - FadeStart);

			ringImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
			arrowImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
			nameLabel.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
		}

		private void UpdateScreenPosition()
		{
			using var _ = Profiler.Scope();

			if (uiCamera == null || Camera.main == null)
				return;

			var canvas = GameScreenManager.Instance.ssCameraCanvas?.GetComponent<Canvas>();
			float planeZ = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera
				? canvas.planeDistance
				: 10f;

			Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPosition);
			bool onScreen = screenPos.x >= 0 && screenPos.x <= Screen.width
				&& screenPos.y >= 0 && screenPos.y <= Screen.height;

			if (onScreen)
			{
				ringImage.enabled = true;
				arrowImage.enabled = false;
				nameLabel.enabled = true;

				screenPos.z = planeZ;
				transform.position = uiCamera.ScreenToWorldPoint(screenPos);
				arrowImage.rectTransform.localRotation = Quaternion.identity;
			}
			else
			{
				ringImage.enabled = false;
				arrowImage.enabled = true;
				nameLabel.enabled = true;

				Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
				Vector2 dir = (new Vector2(screenPos.x, screenPos.y) - screenCenter).normalized;

				float tX = dir.x != 0
					? Mathf.Min(
						Mathf.Abs((ScreenEdgeMargin - screenCenter.x) / dir.x),
						Mathf.Abs((Screen.width - ScreenEdgeMargin - screenCenter.x) / dir.x))
					: float.MaxValue;
				float tY = dir.y != 0
					? Mathf.Min(
						Mathf.Abs((ScreenEdgeMargin - screenCenter.y) / dir.y),
						Mathf.Abs((Screen.height - ScreenEdgeMargin - screenCenter.y) / dir.y))
					: float.MaxValue;

				float t = Mathf.Min(tX, tY);
				Vector2 edgePos = screenCenter + dir * t;

				Vector3 edgeScreenPos = new Vector3(edgePos.x, edgePos.y, planeZ);
				transform.position = uiCamera.ScreenToWorldPoint(edgeScreenPos);

				float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
				arrowImage.rectTransform.localRotation = Quaternion.Euler(0, 0, angle - 90f);
			}
		}

		private static Sprite CreateRingSprite()
		{
			using var _ = Profiler.Scope();

			int size = 64;
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			float center = size * 0.5f;
			float outerRadius = center - 1f;
			float innerRadius = outerRadius - 4f;

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
					if (dist <= outerRadius && dist >= innerRadius)
					{
						float edgeSoft = 1f - Mathf.Clamp01(Mathf.Abs(dist - (innerRadius + outerRadius) * 0.5f) / 2.5f);
						tex.SetPixel(x, y, new Color(1f, 1f, 1f, edgeSoft));
					}
					else
					{
						tex.SetPixel(x, y, Color.clear);
					}
				}
			}

			tex.Apply();
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
		}

		private static Sprite CreateArrowSprite()
		{
			using var _ = Profiler.Scope();

			int size = 32;
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			var pixels = new Color[size * size];

			Vector2 top = new Vector2(size * 0.5f, size - 2);
			Vector2 bottomLeft = new Vector2(4, 2);
			Vector2 bottomRight = new Vector2(size - 4, 2);

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					Vector2 p = new Vector2(x, y);
					if (PointInTriangle(p, top, bottomLeft, bottomRight))
						pixels[y * size + x] = Color.white;
					else
						pixels[y * size + x] = Color.clear;
				}
			}

			tex.SetPixels(pixels);
			tex.Apply();
			return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
		}

		private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
		{
			float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
			float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
			float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
			bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
			bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
			return !(hasNeg && hasPos);
		}
	}
}
