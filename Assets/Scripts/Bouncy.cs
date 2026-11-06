using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 버튼 반응: 누르는 동안만 살짝 줄어든다. (마우스를 올릴 때 커지는 반응은 없다. 단순하게.)
public class Bouncy : MonoBehaviour, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    float target = 1f;
    Selectable sel;

    void Awake() { sel = GetComponent<Selectable>(); }
    bool Active { get { return sel == null || sel.interactable; } }

    public void OnPointerExit(PointerEventData e) { target = 1f; }
    public void OnPointerDown(PointerEventData e) { if (Active) target = 0.96f; }
    public void OnPointerUp(PointerEventData e) { target = 1f; }

    void Update()
    {
        float s = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime));
        transform.localScale = new Vector3(s, s, 1f);
    }
}
