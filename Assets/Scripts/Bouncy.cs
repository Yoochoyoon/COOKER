using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 버튼 반응: 마우스를 올리면 커지고, 누르면 줄어든다.
public class Bouncy : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    float target = 1f;
    bool over;
    Selectable sel;

    void Awake() { sel = GetComponent<Selectable>(); }
    bool Active { get { return sel == null || sel.interactable; } }

    public void OnPointerEnter(PointerEventData e) { over = true; target = Active ? 1.06f : 1f; }
    public void OnPointerExit(PointerEventData e) { over = false; target = 1f; }
    public void OnPointerDown(PointerEventData e) { if (Active) target = 0.94f; }
    public void OnPointerUp(PointerEventData e) { target = over && Active ? 1.06f : 1f; }

    void Update()
    {
        float s = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
        transform.localScale = new Vector3(s, s, 1f);
    }
}
