using ONI_Together.DebugTools;
using Shared.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIDragHandler : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] public RectTransform target;
    public bool WasDragged { get; private set; }
    public System.Action OnDragEnded;

    private Vector2 offset;

    private void Awake()
    {
        using var _ = Profiler.Scope();

        if (target == null)
            target = GetComponent<RectTransform>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        using var _ = Profiler.Scope();

        RectTransform parent = target.parent as RectTransform;

        WasDragged = false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out Vector2 localMousePosition);
        offset = target.anchoredPosition - localMousePosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        using var _ = Profiler.Scope();

        if (target == null)
            return;

        WasDragged = true;
        RectTransform parent = target.parent as RectTransform;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out Vector2 localMousePosition))
        {
            Vector2 newPos = localMousePosition + offset;

            // Clamp to parent/canvas bounds
            RectTransform canvasRect = GameScreenManager.Instance.ssOverlayCanvas.GetComponent<RectTransform>();

            float padding = 10f;
            float leftLimit = -canvasRect.rect.width * 0.5f + target.rect.width * target.pivot.x + padding;
            float rightLimit = canvasRect.rect.width * 0.5f - target.rect.width * (1f - target.pivot.x) - padding;
            float bottomLimit = -canvasRect.rect.height * 0.5f + target.rect.height * target.pivot.y + padding;
            float topLimit = canvasRect.rect.height * 0.5f - target.rect.height * (1f - target.pivot.y) - padding;

            newPos.x = Mathf.Clamp(newPos.x, leftLimit, rightLimit);
            newPos.y = Mathf.Clamp(newPos.y, bottomLimit, topLimit);

            target.anchoredPosition = newPos;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        OnDragEnded?.Invoke();
    }

}
