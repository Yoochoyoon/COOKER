using UnityEngine;
using UnityEngine.UI;

// 불꽃: 팬/냄비 둘레로 삐져나오는 파란·주황 불꽃. 불 세기(약불·중불·강불)에 따라 불꽃 혀 길이가 달라지고 살랑거린다.
// 안쪽은 비어 있어서 팬 그림은 그대로 보이고, 가장자리에서만 불꽃이 핥듯이 보인다.
// GameFlow.StartCook 끝에서 Attach 한 줄로 붙인다.
public class FireFlame : MonoBehaviour
{
    const float RefW = 1920f, RefH = 1080f; // 기준 해상도 (픽셀로 계산한 뒤 화면 비율로 바꾼다)
    const int S = 300;                       // 불꽃 그림 한 변
    const float Inner = 118f;                // 그림 안에서 팬 가장자리에 맞출 반지름 (이 안쪽은 비움)
    static readonly float[] Tongue = { 10f, 24f, 40f }; // 약불·중불·강불의 불꽃 혀 길이 (그림 픽셀)
    static Sprite[] sprites;

    public static void Attach(Transform root, Cook cook, RectTransform panRect, bool cookArt)
    {
        if (cook == null || panRect == null) return;
        // 팬 바닥 원의 중심과 반지름(기준 픽셀). 조리 배경 그림(팬·웍·냄비)은 원이 냄비 칸 가운데보다 오른쪽 위에 있다
        Vector2 c; float r;
        if (cookArt) { c = new Vector2(0.326f * RefW, 0.61f * RefH); r = 285f; }
        else
        {
            c = new Vector2((panRect.anchorMin.x + panRect.anchorMax.x) * 0.5f * RefW, (panRect.anchorMin.y + panRect.anchorMax.y) * 0.5f * RefH);
            r = Mathf.Min((panRect.anchorMax.x - panRect.anchorMin.x) * RefW, (panRect.anchorMax.y - panRect.anchorMin.y) * RefH) * 0.5f;
        }
        float size = r * S / Inner; // 그림의 Inner 반지름이 팬 가장자리에 오도록
        var rt = UIKit.Box(root, "FireFlame", (c.x - size / 2f) / RefW, (c.y - size / 2f) / RefH, (c.x + size / 2f) / RefW, (c.y + size / 2f) / RefH);
        rt.SetAsFirstSibling(); // 팬 그림(배경) 바로 위, 재료·안내문·버튼보다는 아래에 그린다
        // 플레이를 다시 시작하면 예전 그림이 지워져 있을 수 있어서(도메인 리로드 끔) 비었으면 새로 만든다
        if (sprites == null || sprites[0] == null || sprites[1] == null || sprites[2] == null)
            sprites = new[] { Make(Tongue[0]), Make(Tongue[1]), Make(Tongue[2]) };
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprites[Mathf.Clamp(cook.fire, 0, 2)]; // 그림 없이 그려지면 하얀 네모가 되므로 만들자마자 넣는다
        img.raycastTarget = false;
        var f = rt.gameObject.AddComponent<FireFlame>();
        f.cook = cook;
        f.img = img;
        f.rt = rt;
    }

    Cook cook;
    Image img;
    RectTransform rt;

    void Update()
    {
        if (cook == null || sprites == null) return;
        img.sprite = sprites[Mathf.Clamp(cook.fire, 0, 2)];
        img.enabled = !cook.finished;
        // 살랑거림: 조금씩 커졌다 작아지고 천천히 돈다
        float wob = 1f + Mathf.Sin(Time.time * 17f) * 0.008f * (cook.fire + 1);
        rt.localScale = new Vector3(wob, wob, 1f);
        rt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 1.3f) * 4f);
    }

    static Sprite Make(float tongue)
    {
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var p = new Vector2(x - S / 2f, y - S / 2f);
                float d = p.magnitude, a = Mathf.Atan2(p.y, p.x);
                // 불꽃 혀 16개, 길이를 조금씩 다르게 해서 덜 기계적으로
                float wave = 0.5f + 0.5f * Mathf.Cos(a * 16f);
                float jitter = 0.75f + 0.25f * Mathf.Sin(a * 5f + 1.3f);
                float outer = Inner + 4f + tongue * wave * jitter;
                var col = new Color(0, 0, 0, 0);
                if (d >= Inner - 6f && d <= outer)
                {
                    float k = Mathf.Clamp01((d - (Inner - 6f)) / (outer - (Inner - 6f))); // 안쪽 파랑 -> 바깥 주황·노랑
                    col = k < 0.4f ? Color.Lerp(new Color(0.25f, 0.45f, 1f), new Color(0.5f, 0.75f, 1f), k / 0.4f)
                        : Color.Lerp(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.88f, 0.35f), (k - 0.4f) / 0.6f);
                    col.a = Mathf.Clamp01((outer - d) / 3f) * Mathf.Clamp01((d - (Inner - 6f)) / 3f) * 0.95f;
                }
                px[y * S + x] = col;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }
}
