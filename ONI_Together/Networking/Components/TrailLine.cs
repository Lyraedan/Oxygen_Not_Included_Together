using Shared.Profiling;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ONI_Together.Networking.Components
{
	public class TrailLine : Graphic
	{
		private const float BaseLineWidth = 0.04f;
		private const float DeleteProximity = 0.8f;
		private const float PulseSpeed = 5f;

		private readonly List<Vector2> points = new List<Vector2>();
		private Color baseColor;
		private bool highlighted;
		private Camera uiCamera;
		private float canvasPlaneZ = 10f;

		protected override void Awake()
		{
			using var _ = Profiler.Scope();

			base.Awake();
			uiCamera = GameScreenManager.Instance.GetCamera(GameScreenManager.UIRenderTarget.ScreenSpaceCamera);
			var canvas = GameScreenManager.Instance.ssCameraCanvas?.GetComponent<Canvas>();
			if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
				canvasPlaneZ = canvas.planeDistance;
		}

		private void Update()
		{
			SetVerticesDirty();
		}

		public void AddPoints(List<Vector2> worldPositions, Color col)
		{
			using var _ = Profiler.Scope();

			baseColor = col;
			color = col;
			for (int i = 0; i < worldPositions.Count; i++)
				points.Add(worldPositions[i]);
		}

		public void SetHighlight(bool on)
		{
			highlighted = on;
		}

		public bool IsNearWorldPosition(Vector2 worldPos)
		{
			return GetDistanceToLine(worldPos) <= DeleteProximity;
		}

		public float GetDistanceToLine(Vector2 worldPos)
		{
			using var _ = Profiler.Scope();

			float minDist = float.MaxValue;
			for (int i = 0; i < points.Count - 1; i++)
			{
				float dist = DistanceToSegment(worldPos, points[i], points[i + 1]);
				if (dist < minDist)
					minDist = dist;
			}
			return minDist;
		}

		private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float lengthSq = ab.sqrMagnitude;
			if (lengthSq == 0f)
				return Vector2.Distance(p, a);

			float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
			Vector2 projection = a + t * ab;
			return Vector2.Distance(p, projection);
		}

		protected override void OnPopulateMesh(VertexHelper vh)
		{
			using var _ = Profiler.Scope();

			vh.Clear();

			if (points.Count < 2 || uiCamera == null || Camera.main == null)
				return;

			Color drawColor = baseColor;

			if (highlighted)
			{
				float pulse = (Mathf.Sin(Time.unscaledTime * PulseSpeed) + 1f) * 0.5f;
				float alpha = Mathf.Lerp(0.2f, 1f, pulse);
				drawColor = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
			}

			Vector2 refA = transform.InverseTransformPoint(WorldToUI(Vector2.zero));
			Vector2 refB = transform.InverseTransformPoint(WorldToUI(new Vector2(BaseLineWidth, 0f)));
			float lineWidth = Vector2.Distance(refA, refB);

			var uiPoints = new Vector2[points.Count];
			for (int i = 0; i < points.Count; i++)
				uiPoints[i] = transform.InverseTransformPoint(WorldToUI(points[i]));

			for (int i = 0; i < uiPoints.Length - 1; i++)
			{
				Vector2 dir = (uiPoints[i + 1] - uiPoints[i]).normalized;
				Vector2 perp = new Vector2(-dir.y, dir.x) * lineWidth;

				int idx = vh.currentVertCount;
				vh.AddVert(uiPoints[i] + perp, drawColor, Vector2.zero);
				vh.AddVert(uiPoints[i] - perp, drawColor, Vector2.zero);
				vh.AddVert(uiPoints[i + 1] - perp, drawColor, Vector2.zero);
				vh.AddVert(uiPoints[i + 1] + perp, drawColor, Vector2.zero);
				vh.AddTriangle(idx, idx + 1, idx + 2);
				vh.AddTriangle(idx, idx + 2, idx + 3);

				if (i < uiPoints.Length - 2)
				{
					Vector2 nextDir = (uiPoints[i + 2] - uiPoints[i + 1]).normalized;
					Vector2 nextPerp = new Vector2(-nextDir.y, nextDir.x) * lineWidth;

					int ji = vh.currentVertCount;
					vh.AddVert(uiPoints[i + 1], drawColor, Vector2.zero);
					vh.AddVert(uiPoints[i + 1] + perp, drawColor, Vector2.zero);
					vh.AddVert(uiPoints[i + 1] + nextPerp, drawColor, Vector2.zero);
					vh.AddTriangle(ji, ji + 1, ji + 2);

					int ji2 = vh.currentVertCount;
					vh.AddVert(uiPoints[i + 1], drawColor, Vector2.zero);
					vh.AddVert(uiPoints[i + 1] - perp, drawColor, Vector2.zero);
					vh.AddVert(uiPoints[i + 1] - nextPerp, drawColor, Vector2.zero);
					vh.AddTriangle(ji2, ji2 + 1, ji2 + 2);
				}
			}
		}

		private Vector3 WorldToUI(Vector2 worldPos)
		{
			Vector3 screenPos = Camera.main.WorldToScreenPoint(new Vector3(worldPos.x, worldPos.y, 0f));
			screenPos.z = canvasPlaneZ;
			return uiCamera.ScreenToWorldPoint(screenPos);
		}
	}
}
