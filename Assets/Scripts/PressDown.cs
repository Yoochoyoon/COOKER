using UnityEngine;
using UnityEngine.EventSystems;

// 눌린 동안 버튼이 아래로 내려앉는다 (장보기 시안의 active:translateY 효과). 놓으면 제자리로.
public class PressDown : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public float depth = 3f; // 내려앉는 픽셀 (기준 해상도 기준이 아니라 화면 픽셀)
    RectTransform rt;
    bool pressed;

    void Awake() { rt = (RectTransform)transform; }

    public void OnPointerDown(PointerEventData e) { Set(true); }
    public void OnPointerUp(PointerEventData e) { Set(false); }
    public void OnPointerExit(PointerEventData e) { Set(false); }
    void OnDisable() { Set(false); }

    void Set(bool down)
    {
        if (rt == null || pressed == down) return;
        pressed = down;
        rt.anchoredPosition += new Vector2(0f, down ? -depth : depth);
    }
}
