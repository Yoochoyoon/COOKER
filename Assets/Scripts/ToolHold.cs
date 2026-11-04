using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 손에 드는 조리 도구(뒤집개·집게·뚜껑). 한 번 클릭하면 손에 집혀 마우스를 따라다니고(버튼을 누르고 있을 필요 없음),
// 들고 있는 동안 클릭하면 그 자리에서 쓴다. 오른쪽 클릭이나 Esc는 내려놓기. 한 번에 하나만 든다.
public class ToolHold : MonoBehaviour, IPointerClickHandler
{
    public static ToolHold Held;             // 지금 손에 든 도구

    public Action onPickUp;
    public Action<Vector2, Vector2> onMove;  // 들고 있는 동안 매 프레임: 화면 좌표, 이동량
    public Func<Vector2, bool> onUse;        // 들고 클릭: true를 돌려주면 내려놓는다
    public Action onPutDown;

    RectTransform rt, parent;
    CanvasGroup group;
    Vector2 homeAnchored, homeLocal;
    int pickFrame;

    public bool IsHeld { get { return Held == this; } }

    void Awake()
    {
        rt = (RectTransform)transform;
        parent = (RectTransform)rt.parent;
        group = gameObject.AddComponent<CanvasGroup>();
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (Held != null || e.button != PointerEventData.InputButton.Left) return;
        Held = this;
        pickFrame = Time.frameCount;     // 집은 그 클릭이 바로 "쓰기"로 처리되지 않게
        homeAnchored = rt.anchoredPosition;
        homeLocal = rt.localPosition;
        group.blocksRaycasts = false;    // 들고 있는 도구가 아래 칸을 가리지 않게
        rt.SetAsLastSibling();
        if (onPickUp != null) onPickUp();
    }

    public void PutDown()
    {
        if (Held == this) Held = null;
        group.blocksRaycasts = true;
        rt.anchoredPosition = homeAnchored;
        rt.localRotation = Quaternion.identity;
        if (onPutDown != null) onPutDown();
    }

    void Update()
    {
        if (Held != this) return;
        var m = Mouse.current;
        if (m == null) return;
        var pos = m.position.ReadValue();
        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pos, null, out local))
            rt.anchoredPosition = homeAnchored + (local - homeLocal); // 도구 가운데가 마우스를 따라온다
        if (onMove != null) onMove(pos, m.delta.ReadValue());
        if (Time.frameCount == pickFrame) return;

        var kb = Keyboard.current;
        if (m.rightButton.wasPressedThisFrame || (kb != null && kb.escapeKey.wasPressedThisFrame)) { PutDown(); return; }
        if (m.leftButton.wasPressedThisFrame && (onUse == null || onUse(pos))) PutDown();
    }

    void OnDestroy()
    {
        if (Held == this) Held = null;
    }
}
