using System;
using UnityEngine;
using UnityEngine.EventSystems;

// 돌리는 손잡이 (가스레인지 밸브). 누르거나 끌면 손잡이 가운데에서 마우스까지의 방향을 각도로 알려 준다.
// 각도는 위쪽이 0도, 시계 방향(오른쪽)이 +, 반시계 방향(왼쪽)이 -.
public class KnobDrag : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public Action<float> onAngle;

    public void OnPointerDown(PointerEventData e) { Report(e.position); }
    public void OnDrag(PointerEventData e) { Report(e.position); }

    void Report(Vector2 screenPos)
    {
        var center = RectTransformUtility.WorldToScreenPoint(null, transform.position);
        var d = screenPos - center;
        if (d.sqrMagnitude < 4f) return; // 정가운데는 방향이 없다
        float deg = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        if (onAngle != null) onAngle(deg);
    }
}
