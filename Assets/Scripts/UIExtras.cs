using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 작은 UI 도우미 모음: 9칸 늘이기 보정, 반짝이는 움직임, 호버 테두리, 공용 닫기(X) 버튼.

// 9칸 늘이기 그림(Sliced)은 버튼이 테두리 두께보다 작아지면 모서리가 서로 겹쳐 깨진다.
// 칸 크기에 맞춰 테두리 배율(pixelsPerUnitMultiplier)을 올려서 항상 온전히 보이게 한다.
public class SlicedFit : MonoBehaviour
{
    Image img;
    Vector2 last;

    void Awake() { img = GetComponent<Image>(); }

    void LateUpdate()
    {
        if (img == null || img.sprite == null || img.type != Image.Type.Sliced) return;
        var r = ((RectTransform)transform).rect;
        if (r.width <= 1f || r.height <= 1f) return;
        var cur = new Vector2(r.width, r.height);
        if ((cur - last).sqrMagnitude < 0.25f) return;
        last = cur;
        var b = img.sprite.border; // 왼쪽, 아래, 오른쪽, 위
        float need = Mathf.Max((b.x + b.z) / (r.width * 0.92f), (b.y + b.w) / (r.height * 0.92f));
        img.pixelsPerUnitMultiplier = Mathf.Max(1f, need);
    }
}

// 누를 수 있다는 것을 알리는 느린 두근거림(크기)과 살랑거림(기울기). 화면이 다시 그려지면 같이 사라진다.
public class Pulse : MonoBehaviour
{
    public float scaleAmp = 0.02f, rotAmp = 0f, speed = 3f;
    Vector3 baseScale;

    void Awake() { baseScale = transform.localScale; }

    void Update()
    {
        float s = Mathf.Sin(Time.unscaledTime * speed);
        transform.localScale = baseScale * (1f + scaleAmp * s);
        if (rotAmp > 0f) transform.localRotation = Quaternion.Euler(0f, 0f, rotAmp * s);
    }
}

// 마우스를 올리면 안쪽 테두리(ring)를 켠다. 그림 전체를 물들이지 않아서 누끼를 따지 않은 사진도 어색하지 않다.
public class HoverRing : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public GameObject ring;
    public void OnPointerEnter(PointerEventData e) { if (ring != null) ring.SetActive(true); }
    public void OnPointerExit(PointerEventData e) { if (ring != null) ring.SetActive(false); }
    void OnDisable() { if (ring != null) ring.SetActive(false); }
}

public static class UIX
{
    static Sprite circle;

    // 동그란 버튼 바탕(테두리가 어두운 흰 원). 색은 Image.color로 입힌다.
    static Sprite Circle()
    {
        if (circle != null) return circle;
        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[S * S];
        float c = S / 2f, rOut = S / 2f - 1f, rIn = rOut - 5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                float a = Mathf.Clamp01(rOut - d + 0.5f);               // 바깥 모서리
                float rim = Mathf.Clamp01(d - rIn + 0.5f);              // 테두리(안쪽은 0, 바깥은 1)
                float v = Mathf.Lerp(1f, 0.55f, rim);                   // 안쪽은 밝게, 테두리는 어둡게
                float hi = 1f - Mathf.Clamp01((y - S * 0.55f) / (S * 0.35f)) * 0f; // 자리 표시(밝기 변화 없음)
                px[y * S + x] = new Color(v * hi, v * hi, v * hi, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        circle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return circle;
    }

    // 모든 팝업·화면의 닫기: 작은 빨간 동그라미 X. 호버하면 살짝 어두워진다.
    // 칸이 직사각형이어도 동그라미는 짧은 변에 맞춰 가운데에 그려진다 (preserveAspect).
    public static Button CloseX(Transform parent, float x0, float y0, float x1, float y1, Action onClick)
    {
        var rt = UIKit.Box(parent, "CloseX", x0, y0, x1, y1);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Circle();
        img.preserveAspect = true;
        img.color = new Color(0.88f, 0.29f, 0.27f);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.82f, 0.82f, 0.82f);
        cb.pressedColor = new Color(0.66f, 0.66f, 0.66f);
        cb.selectedColor = Color.white;
        b.colors = cb;
        b.transition = Selectable.Transition.ColorTint;
        if (onClick != null) b.onClick.AddListener(() => onClick());
        var glyph = Art.Load("icon_close");
        var gr = UIKit.Box(rt, "X", 0.27f, 0.27f, 0.73f, 0.73f);
        if (glyph != null)
        {
            var gi = gr.gameObject.AddComponent<Image>();
            gi.sprite = glyph; gi.preserveAspect = true; gi.raycastTarget = false;
        }
        else
        {
            var t = UIKit.Label(gr, "X", 0, 0, 1, 1, 36, TextAnchor.MiddleCenter, Color.white);
            t.raycastTarget = false;
        }
        rt.gameObject.AddComponent<PressDown>().depth = 3f;
        return b;
    }
}
