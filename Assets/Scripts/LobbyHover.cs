using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 그림 버튼 효과: 마우스를 올리면 살짝 어두워지고, 누르면 조금 더 어두워진다. (내려앉는 움직임은 PressDown이 맡는다)
// 커지거나 튀는 움직임은 없다. +/− 버튼처럼 단순하게.
public class LobbyHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public Image img;
    bool over, down;

    public void OnPointerEnter(PointerEventData e) { over = true; }
    public void OnPointerExit(PointerEventData e) { over = false; down = false; }
    public void OnPointerDown(PointerEventData e) { down = true; }
    public void OnPointerUp(PointerEventData e) { down = false; }

    void Update()
    {
        if (img == null) return;
        float c = down ? 0.84f : over ? 0.94f : 1f;
        img.color = Color.Lerp(img.color, new Color(c, c, c, 1f), 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime));
    }

    void OnDisable() { over = down = false; if (img != null) img.color = Color.white; }
}
