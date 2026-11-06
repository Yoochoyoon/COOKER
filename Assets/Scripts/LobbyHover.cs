using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 로비 그림 버튼 효과: 마우스를 올리면 살짝 커지고, 누르면 쏙 작아지며 어두워졌다가 놓으면 통통 튀어 돌아온다.
public class LobbyHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public Image img;
    public float hoverScale = 1.08f;

    bool over, down;
    float scale = 1f, vel, pop; // pop: 클릭 직후 튀는 힘

    public void OnPointerEnter(PointerEventData e) { over = true; }
    public void OnPointerExit(PointerEventData e) { over = false; down = false; }
    public void OnPointerDown(PointerEventData e) { down = true; }
    public void OnPointerUp(PointerEventData e) { down = false; }
    public void OnPointerClick(PointerEventData e) { pop = 1f; } // 놓는 순간 살짝 튄다

    void Update()
    {
        float target = down ? 0.94f : over ? hoverScale : 1f;
        if (pop > 0f) { target = hoverScale * 1.06f; pop -= Time.unscaledDeltaTime * 6f; }
        // 용수철처럼 목표로 따라간다 (끝에서 살짝 출렁인다)
        float k = 260f, d = 22f, dt = Mathf.Min(Time.unscaledDeltaTime, 0.033f);
        vel += (target - scale) * k * dt;
        vel *= Mathf.Exp(-d * dt);
        scale += vel * dt;
        transform.localScale = Vector3.one * scale;
        if (img != null)
        {
            float c = down ? 0.82f : over ? 0.95f : 1f;
            img.color = Color.Lerp(img.color, new Color(c, c, c, 1f), 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
        }
    }

    void OnDisable() { over = down = false; scale = 1f; vel = 0f; transform.localScale = Vector3.one; if (img != null) img.color = Color.white; }
}
