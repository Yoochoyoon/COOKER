using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 끌어다 놓기. 마우스를 따라 움직이다가 놓으면 놓은 자리를 알려 준다.
// 놓은 뒤의 배치는 부르는 쪽이 화면을 다시 그리거나 자리를 다시 잡아서 정한다.
public class DragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public RectTransform target;        // 이 칸 안에 놓았는지 검사한다 (예: 도마)
    public Action<bool> onDrop;         // true = target 안에 놓음
    public Action<Vector2> onDropAt;    // 놓은 화면 좌표 (여러 칸 중 어디에 놓았는지 직접 볼 때)
    public Func<bool> canDrag;          // false면 지금은 끌 수 없다 (예: 냄비 속 재료는 집게로만)
    public bool dragging;

    RectTransform rt;
    Canvas canvas;
    Vector2 start;
    CanvasGroup group;

    void Awake()
    {
        rt = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        group = gameObject.AddComponent<CanvasGroup>();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (canDrag != null && !canDrag()) { e.pointerDrag = null; return; } // 이번 끌기는 취소
        dragging = true;
        start = rt.anchoredPosition;
        rt.SetAsLastSibling();              // 다른 재료 위로 그린다
        group.blocksRaycasts = false;       // 끄는 동안 아래 칸이 마우스를 받게
        transform.localScale = Vector3.one * 1.1f;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!dragging) return;
        float k = canvas != null ? canvas.scaleFactor : 1f;
        rt.anchoredPosition += e.delta / k;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        dragging = false;
        group.blocksRaycasts = true;
        transform.localScale = Vector3.one;
        bool inside = target != null && RectTransformUtility.RectangleContainsScreenPoint(target, e.position, null);
        rt.anchoredPosition = start;        // 일단 제자리로. 놓은 결과는 onDrop이 다시 그린다
        if (onDrop != null) onDrop(inside);
        if (onDropAt != null) onDropAt(e.position);
    }
}
