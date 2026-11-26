using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 로비 위쪽 바·칭호 말풍선·시작 버튼의 그림에서 뽑은 모양을 코드로 그린다 (그림 파일은 쓰지 않는다).
// 특징: ① 따뜻한 모래색·노란색 바탕 ② 큰 둥근 모서리 ③ 진한 갈색 바깥 선, 아래쪽만 두껍게(눌린 듯한 두께)
//       ④ 바탕 안쪽의 얇은 크림색 선 ⑤ 위쪽이 살짝 밝은 바탕, 노란 버튼은 윗면에 빛 한 줄
// 장보기 같은 화면이 이 모양을 같이 쓰도록 9칸 늘이기(Sliced) 그림을 한 번 만들어 두고 재사용한다.
public static class HudStyle
{
    public static readonly Color Outline = new Color(0.29f, 0.20f, 0.14f);   // 진한 갈색 바깥 선 (아래 두께도 같은 색)
    public static readonly Color Cream = new Color(1f, 0.965f, 0.86f);       // 안쪽 얇은 선
    public static readonly Color Sand = new Color(0.91f, 0.835f, 0.71f);     // 위쪽 바 같은 큰 판
    public static readonly Color SandSoft = new Color(0.965f, 0.915f, 0.81f); // 판 위의 줄 칸
    public static readonly Color Gold = new Color(0.976f, 0.80f, 0.25f);     // 노란 버튼
    public static readonly Color Tan = new Color(0.949f, 0.80f, 0.49f);      // 칭호 말풍선 같은 중간 톤
    public static readonly Color Text = new Color(0.29f, 0.20f, 0.14f);      // 글자: 진한 갈색
    public static readonly Color TextSoft = new Color(0.52f, 0.40f, 0.30f);  // 덜 중요한 글자

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // 스타일이 입혀진 상자 하나. small이면 모서리와 선을 작게 (줄 칸·작은 버튼), gloss면 윗면에 빛 한 줄 (버튼)
    public static RectTransform Panel(Transform parent, string name, float x0, float y0, float x1, float y1,
        Color fill, bool small = false, bool gloss = false)
    {
        var rt = UIKit.Box(parent, name, x0, y0, x1, y1);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Make(fill, small, gloss);
        img.type = Image.Type.Sliced;
        img.color = Color.white; // 색은 그림 안에 이미 들어 있다
        return rt;
    }

    public static Sprite Make(Color fill, bool small, bool gloss)
    {
        string key = ColorUtility.ToHtmlStringRGB(fill) + (small ? "s" : "b") + (gloss ? "g" : "n");
        Sprite sp;
        if (cache.TryGetValue(key, out sp) && sp != null) return sp;

        // 크기·반지름·선 두께·아래 두께·안쪽 선 두께
        int size = small ? 48 : 96;
        float r = small ? 12f : 26f, t = small ? 2.5f : 4f, lip = small ? 4f : 8f, inner = small ? 2f : 3f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f; // y는 아래가 0
                // 바깥 모양(갈색), 바탕 모양(아래 두께만큼 위로 올림), 안쪽 크림 선 안쪽
                float dOuter = RoundDist(fx, fy, 0f, 0f, size, size, r);
                float dFace = RoundDist(fx, fy, t, t + lip, size - t, size - t, Mathf.Max(2f, r - t));
                float dIn = RoundDist(fx, fy, t + inner, t + lip + inner, size - t - inner, size - t - inner, Mathf.Max(2f, r - t - inner));

                Color c = Outline;
                // 바탕: 위가 살짝 밝고 아래가 살짝 어둡다
                float v = Mathf.InverseLerp(t + lip, size - t, fy);
                Color body = Color.Lerp(Shade(fill, 0.93f), Shade(fill, 1.06f), v);
                if (gloss && v > 0.55f && v < 0.82f && dIn < -inner) body = Color.Lerp(body, Color.white, 0.22f); // 윗면 빛 한 줄
                c = Over(c, Cream, Cover(dFace));
                c = Over(c, body, Cover(dIn));
                c.a = Cover(dOuter);
                px[y * size + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        float bx = r + t, bt = r + t, bb = r + t + lip;
        sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(bx, bb, bx, bt));
        cache[key] = sp;
        return sp;
    }

    // 둥근 사각형까지의 부호 있는 거리 (안쪽이 음수)
    static float RoundDist(float x, float y, float x0, float y0, float x1, float y1, float r)
    {
        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
        float hx = (x1 - x0) * 0.5f - r, hy = (y1 - y0) * 0.5f - r;
        float qx = Mathf.Abs(x - cx) - hx, qy = Mathf.Abs(y - cy) - hy;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    static float Cover(float d) { return Mathf.Clamp01(0.5f - d); } // 1픽셀 부드럽게
    static Color Over(Color under, Color over, float a) { return Color.Lerp(under, new Color(over.r, over.g, over.b, 1f), a); }
    static Color Shade(Color c, float k) { return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f); }
}
