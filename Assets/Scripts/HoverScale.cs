using UnityEngine;
using UnityEngine.EventSystems;

// 마우스를 올리면 아주 살짝 커진다 (로비의 캐릭터·냉장고). 다른 반응은 없다.
public class HoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public float scale = 1.02f;
    float target = 1f;

    public void OnPointerEnter(PointerEventData e) { target = scale; }
    public void OnPointerExit(PointerEventData e) { target = 1f; }

    void Update()
    {
        float s = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
        transform.localScale = new Vector3(s, s, 1f);
    }

    void OnDisable() { target = 1f; transform.localScale = Vector3.one; }
}
