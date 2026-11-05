using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 끓어넘칠 때(cook.bubble) 냄비 안에 거품이 보글보글 올라오고, 가장자리로 넘칠 듯 부풀어 오른다.
// 뚜껑을 떼서 가라앉으면(cook.bubble = false) 거품이 금방 사그라든다.
// CookTools가 뚜껑을 만들 때 냄비 입구 위치(중심·지름, 기준 픽셀)로 붙인다.
public class PotBubbles : MonoBehaviour
{
    const float RefW = 1920f, RefH = 1080f;
    const int Count = 22;   // 안쪽 거품 수
    const int Foam = 14;    // 가장자리 거품 수
    static Sprite bubble;

    class B { public RectTransform rt; public Image img; public Vector2 pos; public float size, phase, speed; public bool rim; }
    readonly List<B> bs = new List<B>();
    Cook cook;
    float amount; // 0 = 거품 없음, 1 = 넘치기 직전

    // center·diameter: 냄비 입구 원 (기준 해상도 픽셀)
    public static void Attach(Transform root, Cook cook, Vector2 center, float diameter)
    {
        if (bubble == null) bubble = MakeBubble(); // 다시 플레이하면 지워져 있을 수 있어서 Unity null 검사로 다시 만든다
        var area = UIKit.Box(root, "PotBubbles", (center.x - diameter / 2f) / RefW, (center.y - diameter / 2f) / RefH,
            (center.x + diameter / 2f) / RefW, (center.y + diameter / 2f) / RefH);
        var fit = UIKit.Box(area, "Fit", 0, 0, 1, 1); // 정사각형으로 맞춰 원이 찌그러지지 않게
        var f = fit.gameObject.AddComponent<AspectRatioFitter>();
        f.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        f.aspectRatio = 1f;
        var p = area.gameObject.AddComponent<PotBubbles>();
        p.cook = cook;
        var rnd = new System.Random(7);
        for (int i = 0; i < Count + Foam; i++)
        {
            bool rim = i >= Count;
            // 안쪽 거품은 원 안 아무 곳, 가장자리 거품은 테두리를 따라 고르게
            float a = rim ? Mathf.PI * 2f * (i - Count) / Foam + (float)rnd.NextDouble() * 0.3f : (float)rnd.NextDouble() * Mathf.PI * 2f;
            float r = rim ? 0.40f : Mathf.Sqrt((float)rnd.NextDouble()) * 0.33f;
            var rt = UIKit.Box(fit, "Bubble", 0, 0, 0, 0);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = bubble;
            img.raycastTarget = false;
            p.bs.Add(new B
            {
                rt = rt, img = img, rim = rim,
                pos = new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r),
                size = rim ? 0.10f + (float)rnd.NextDouble() * 0.06f : 0.04f + (float)rnd.NextDouble() * 0.06f,
                phase = (float)rnd.NextDouble(),
                speed = 0.8f + (float)rnd.NextDouble() * 0.9f,
            });
        }
        p.Update();
    }

    void Update()
    {
        if (cook == null) return;
        // 넘칠 때는 빠르게 차오르고, 뚜껑을 떼면 더 빠르게 사그라든다
        float target = cook.bubble && !cook.finished ? 1f : 0f;
        amount = Mathf.MoveTowards(amount, target, Time.deltaTime * (target > amount ? 2.5f : 4f));
        foreach (var b in bs)
        {
            // 거품 하나: 생겨서 커지다가 톡 터지고 다시 생긴다
            float life = (Time.time * b.speed + b.phase) % 1f;
            float grow = b.rim ? 0.75f + 0.25f * Mathf.Sin((Time.time * b.speed + b.phase) * 6.28f) : Mathf.Clamp01(life / 0.8f);
            float pop = b.rim ? 1f : life < 0.8f ? 1f : 1f - (life - 0.8f) / 0.2f;
            float s = b.size * grow * (b.rim ? Mathf.Lerp(0.3f, 1.15f, amount) : amount); // 가장자리 거품은 넘칠수록 부푼다
            var rise = b.rim ? Vector2.zero : new Vector2(0f, life * 0.03f);
            b.rt.anchorMin = b.pos + rise - new Vector2(s, s) * 0.5f;
            b.rt.anchorMax = b.pos + rise + new Vector2(s, s) * 0.5f;
            b.rt.offsetMin = b.rt.offsetMax = Vector2.zero;
            b.img.color = new Color(1f, 1f, 1f, amount * pop * (b.rim ? 0.95f : 0.85f));
            b.img.enabled = amount > 0.01f;
        }
    }

    // 거품 그림: 하얀 반투명 원 + 연한 파란 테두리 + 왼쪽 위 반짝임
    static Sprite MakeBubble()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color[S * S];
        var c = new Vector2(S / 2f, S / 2f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                float edge = Mathf.Clamp01(30f - d);                                     // 바깥 부드럽게
                float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 27f) / 3f);                 // 테두리
                float shine = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), c + new Vector2(-9, 9)) / 6f);
                var col = Color.Lerp(new Color(1f, 1f, 1f, 0.55f), new Color(0.75f, 0.88f, 1f, 1f), rim);
                col = Color.Lerp(col, Color.white, shine);
                col.a = Mathf.Max(col.a, shine) * edge;
                px[y * S + x] = col;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }
}
