using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 코드로 UI를 만드는 작은 도우미. 좌표는 부모 기준 0~1 비율이라 해상도와 상관없이 배치된다.
// 게임 느낌: 둥근 모서리 + 진한 외곽선 + 아래 그림자, 버튼은 눌리는 두께와 마우스 반응이 있다.
public static class UIKit
{
    public static readonly Color Bg = new Color(1f, 0.82f, 0.36f);
    public static readonly Color Ink = new Color(0.23f, 0.12f, 0.06f);
    public static readonly Color Accent = new Color(0.91f, 0.35f, 0.05f);
    public static readonly Color Card = new Color(1f, 0.95f, 0.8f);
    public static readonly Color Muted = new Color(0.62f, 0.45f, 0.3f);
    public static readonly Color Good = new Color(0.12f, 0.55f, 0.2f);
    public static readonly Color Bad = new Color(0.8f, 0.1f, 0.1f);

    // 게임 글씨는 주아체(Assets/Resources/Fonts, OFL 라이선스). 파일이 없으면 OS 폰트로 대신한다.
    static Font font;
    public static Font GetFont()
    {
        if (font == null) font = Resources.Load<Font>("Fonts/Jua-Regular");
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "NanumGothic", "Arial" }, 32);
        return font;
    }

    // 둥근 사각형 그림 (9-slice). 그림 파일 없이 코드로 한 번 만들어 모든 칸과 버튼이 같이 쓴다.
    const int Radius = 22;
    static Sprite round;
    static Sprite Round()
    {
        if (round != null) return round;
        int s = Radius * 2 + 4;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                // 모서리 원 중심까지의 거리로 가장자리를 부드럽게 깎는다
                float cx = Mathf.Clamp(x + 0.5f, Radius, s - Radius), cy = Mathf.Clamp(y + 0.5f, Radius, s - Radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(Radius - d + 0.5f)));
            }
        tex.Apply();
        round = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(Radius, Radius, Radius, Radius));
        return round;
    }

    static Color Darker(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, c.a); }

    // deco=false면 꾸밈(둥근 모서리·외곽선·그림자) 없이 단색 사각형만 그린다 (진행 바 채움 등)
    public static RectTransform Box(Transform parent, string name, float x0, float y0, float x1, float y1, Color? color = null, bool deco = true)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        if (color.HasValue)
        {
            var img = go.AddComponent<Image>();
            img.color = color.Value;
            img.sprite = Round();
            img.type = Image.Type.Sliced;
            if (deco)
            {
                var o = go.AddComponent<Outline>();
                o.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.85f);
                o.effectDistance = new Vector2(3f, 3f);
                var sh = go.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.22f);
                sh.effectDistance = new Vector2(0f, -8f);
            }
        }
        return rt;
    }

    // 화면 전체 배경: 노란 바탕에 옅은 물방울 무늬
    public static RectTransform Backdrop(Transform parent)
    {
        var rt = Box(parent, "Background", 0, 0, 1, 1);
        var img = rt.gameObject.AddComponent<Image>();
        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var dot = Color.Lerp(Bg, Color.white, 0.28f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d1 = Vector2.Distance(new Vector2(x, y), new Vector2(S * 0.25f, S * 0.25f));
                float d2 = Vector2.Distance(new Vector2(x, y), new Vector2(S * 0.75f, S * 0.75f));
                tex.SetPixel(x, y, Color.Lerp(Bg, dot, Mathf.Clamp01(9f - Mathf.Min(d1, d2))));
            }
        tex.Apply();
        img.sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        img.type = Image.Type.Tiled;
        img.raycastTarget = false;
        return rt;
    }

    // 식탁: 타이틀 그림과 같은 분홍 체크무늬 식탁보. 둥근 칸 모양으로 잘라 그린다.
    static Sprite gingham;
    public static RectTransform Tablecloth(Transform parent, float x0, float y0, float x1, float y1)
    {
        var rt = Box(parent, "Table", x0, y0, x1, y1, Color.white);
        rt.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        if (gingham == null) gingham = Art.Load("Kitchen/tablecloth"); // 그림 파일이 있으면 그걸 쓴다
        if (gingham == null)
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var white = new Color(1f, 0.97f, 0.94f);
            var pink = new Color(1f, 0.80f, 0.80f);
            var red = new Color(0.96f, 0.60f, 0.64f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    bool v = x < S / 2, h = y < S / 2;
                    tex.SetPixel(x, y, v && h ? red : (v || h ? pink : white));
                }
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            gingham = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 70f, 0, SpriteMeshType.FullRect);
        }
        var cloth = Box(rt, "Cloth", 0, 0, 1, 1);
        var img = cloth.gameObject.AddComponent<Image>();
        img.sprite = gingham;
        img.type = Image.Type.Tiled;
        img.raycastTarget = false;
        return rt;
    }

    // 도마: 나뭇결이 있는 둥근 나무판 + 오른쪽 손잡이와 구멍. 외곽선은 아이콘과 같은 진갈색.
    static Sprite cuttingBoard;
    public static RectTransform CuttingBoard(Transform parent, float x0, float y0, float x1, float y1)
    {
        var rt = Box(parent, "Board", x0, y0, x1, y1);
        var img = rt.gameObject.AddComponent<Image>();
        if (cuttingBoard == null) cuttingBoard = Art.Load("Kitchen/cutting_board"); // 그림 파일이 있으면 그걸 쓴다
        if (cuttingBoard == null)
        {
            const int W = 640, H = 200;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var line = new Color(0.35f, 0.19f, 0.11f);       // #59301C
            var wood = new Color(0.93f, 0.74f, 0.49f);
            var grain = new Color(0.82f, 0.60f, 0.36f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // 몸통·손잡이를 합치고 구멍을 뺀 모양까지의 거리 (안쪽이 음수)
                    float body = RoundRect(x, y, 6, 6, 562, H - 6, 46);
                    float handle = RoundRect(x, y, 530, 62, W - 6, H - 62, 34);
                    float hole = Vector2.Distance(new Vector2(x, y), new Vector2(596, H / 2f)) - 13f;
                    float d = Mathf.Max(Mathf.Min(body, handle), -hole);
                    Color c;
                    if (d > 0f) c = new Color(line.r, line.g, line.b, Mathf.Clamp01(1f - d));
                    else if (d > -6f) c = line;
                    else
                    {
                        float g = Mathf.Sin(y * 0.16f + Mathf.Sin(x * 0.012f) * 2.5f + Mathf.Sin(x * 0.05f) * 0.4f);
                        c = g > 0.86f ? grain : wood;
                        if (y > H - 40 && d < -6f) c = Color.Lerp(c, Color.white, 0.12f); // 위쪽 빛 받는 면
                    }
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            cuttingBoard = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }
        img.sprite = cuttingBoard;
        var sh = rt.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.25f);
        sh.effectDistance = new Vector2(0f, -8f);
        return rt;
    }

    // ---- 조리 도구 그림 (위에서 내려다본 모습). 모양까지의 거리(안쪽 음수)로 외곽선과 색을 칠한다.
    static readonly Color Line = new Color(0.35f, 0.19f, 0.11f); // #59301C

    // 프라이팬: 왼쪽에 둥근 팬, 오른쪽으로 손잡이. 팬 중심은 그림의 (230/645, 150/300).
    public const float PanCX = 230f / 645f, PanCY = 0.5f, PanR = 140f / 300f;
    static Sprite pan, pot, plate;

    public static RectTransform Pan(Transform parent, float x0, float y0, float x1, float y1)
    {
        if (pan == null) pan = Art.Load("Kitchen/pan"); // 그림 파일이 있으면 그걸 쓴다
        if (pan == null) pan = Paint(645, 300, (x, y) =>
        {
            var c = new Vector2(230, 150);
            float body = Vector2.Distance(new Vector2(x, y), c) - 140f;
            float handle = RoundRect(x, y, 350, 128, 636, 172, 22);
            float d = Mathf.Min(body, handle);
            if (d > 0f) return new Color(Line.r, Line.g, Line.b, Mathf.Clamp01(1f - d));
            if (d > -6f) return Line;
            if (handle < -6f && body > 0f) return x > 560 ? new Color(0.20f, 0.13f, 0.09f) : new Color(0.55f, 0.32f, 0.18f); // 나무 손잡이 + 끝 마개
            if (body > -20f) return new Color(0.45f, 0.46f, 0.50f);                                                         // 테두리
            float hl = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), c + new Vector2(-50, 50)) / 160f);
            return Color.Lerp(new Color(0.22f, 0.22f, 0.25f), new Color(0.38f, 0.38f, 0.43f), hl * 0.8f);               // 바닥 (왼쪽 위가 밝게)
        });
        return Tool(parent, "Pan", x0, y0, x1, y1, pan);
    }

    // 양은 냄비: 노란 냄비 + 양옆 손잡이, 안에 국물. 중심과 크기는 팬과 같다.
    public static RectTransform Pot(Transform parent, float x0, float y0, float x1, float y1)
    {
        if (pot == null) pot = Art.Load("Kitchen/pot"); // 그림 파일이 있으면 그걸 쓴다
        if (pot == null) pot = Paint(645, 300, (x, y) =>
        {
            var c = new Vector2(230, 150);
            float body = Vector2.Distance(new Vector2(x, y), c) - 140f;
            float ears = Mathf.Min(RoundRect(x, y, 62, 128, 120, 172, 14), RoundRect(x, y, 340, 128, 398, 172, 14));
            float d = Mathf.Min(body, ears);
            if (d > 0f) return new Color(Line.r, Line.g, Line.b, Mathf.Clamp01(1f - d));
            if (d > -6f) return Line;
            if (body > 0f) return new Color(0.80f, 0.62f, 0.25f);                          // 손잡이
            if (body > -22f) return body > -10f ? new Color(0.98f, 0.82f, 0.40f) : new Color(0.88f, 0.68f, 0.28f); // 노란 테두리
            float hl = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), c + new Vector2(-45, 45)) / 150f);
            return Color.Lerp(new Color(0.96f, 0.62f, 0.32f), new Color(1f, 0.80f, 0.55f), hl * 0.7f); // 국물
        });
        return Tool(parent, "Pot", x0, y0, x1, y1, pot);
    }

    // 접시: 흰 타원 접시와 안쪽 테
    public static RectTransform Plate(Transform parent, float x0, float y0, float x1, float y1)
    {
        if (plate == null) plate = Art.Load("Kitchen/plate"); // 그림 파일이 있으면 그걸 쓴다
        if (plate == null) plate = Paint(620, 200, (x, y) =>
        {
            float e = (new Vector2((x - 310f) / 300f, (y - 100f) / 92f).magnitude - 1f) * 92f;
            float inner = (new Vector2((x - 310f) / 230f, (y - 100f) / 66f).magnitude - 1f) * 66f;
            if (e > 0f) return new Color(Line.r, Line.g, Line.b, Mathf.Clamp01(1f - e));
            if (e > -5f) return Line;
            if (Mathf.Abs(inner) < 2f) return new Color(0.85f, 0.82f, 0.78f);
            return inner < 0f ? Color.white : new Color(0.95f, 0.94f, 0.92f);
        });
        return Tool(parent, "Plate", x0, y0, x1, y1, plate);
    }

    // ---- 손에 드는 도구 (끌어서 쓴다). 가로로 누운 모습: 왼쪽 손잡이, 오른쪽 머리.
    static Sprite spatula, tongs, lid;

    // 뒤집개: 나무 손잡이 + 구멍 뚫린 넓은 금속 머리
    public static Sprite Spatula()
    {
        if (spatula == null) spatula = Art.Load("Kitchen/spatula"); // 그림 파일이 있으면 그걸 쓴다
        if (spatula == null) spatula = Paint(360, 120, (x, y) =>
        {
            float handle = RoundRect(x, y, 6, 48, 200, 72, 12);
            float neck = RoundRect(x, y, 190, 54, 236, 66, 6);
            float head = RoundRect(x, y, 228, 14, 354, 106, 18);
            float slots = Mathf.Min(RoundRect(x, y, 256, 32, 340, 44, 6), Mathf.Min(RoundRect(x, y, 256, 54, 340, 66, 6), RoundRect(x, y, 256, 76, 340, 88, 6)));
            float d = Mathf.Max(Mathf.Min(handle, Mathf.Min(neck, head)), -slots);
            if (d > 0f) return new Color(Line.r, Line.g, Line.b, Mathf.Clamp01(1f - d));
            if (d > -5f) return Line;
            if (handle < -5f && x < 200) return new Color(0.60f, 0.36f, 0.20f);
            return y > 80 ? new Color(0.88f, 0.90f, 0.94f) : new Color(0.72f, 0.74f, 0.80f); // 금속 (위쪽 밝게)
        });
        return spatula;
    }

    // 집게: 두 갈래 금속 다리가 손잡이 쪽에서 모인다
    public static Sprite Tongs()
    {
        if (tongs == null) tongs = Art.Load("Kitchen/tongs"); // 그림 파일이 있으면 그걸 쓴다
        if (tongs == null) tongs = Paint(360, 120, (x, y) =>
        {
            // 위·아래 다리: 왼쪽(모이는 곳)에서 오른쪽으로 벌어진다
            float spread = Mathf.Lerp(6f, 34f, Mathf.Clamp01((x - 20f) / 320f));
            float top = Mathf.Abs(y - (60f + spread)) - 9f, bottom = Mathf.Abs(y - (60f - spread)) - 9f;
            float len = Mathf.Max(14f - x, x - 350f);
            float arms = Mathf.Max(Mathf.Min(top, bottom), len);
            float ring = Mathf.Abs(Vector2.Distance(new Vector2(x, y), new Vector2(22, 60)) - 16f) - 5f;
            float tips = Mathf.Min(RoundRect(x, y, 318, 86, 352, 104, 6), RoundRect(x, y, 318, 16, 352, 34, 6));
            float d = Mathf.Min(arms, Mathf.Min(ring, tips));
            if (d > 0f) return new Color(Line.r, Line.g, Line.b, Mathf.Clamp01(1f - d));
            if (d > -4f) return Line;
            if (tips < -4f) return new Color(0.30f, 0.30f, 0.34f); // 끝 고무
            return new Color(0.82f, 0.84f, 0.88f);
        });
        return tongs;
    }

    // 유리 뚜껑: 반투명 원 + 테두리 + 가운데 손잡이 (위에서 본 모습)
    public static Sprite GlassLid()
    {
        if (lid == null) lid = Art.Load("Kitchen/lid"); // 그림 파일이 있으면 그걸 쓴다
        if (lid == null) lid = Paint(300, 300, (x, y) =>
        {
            var c = new Vector2(150, 150);
            float r = Vector2.Distance(new Vector2(x, y), c);
            float body = r - 140f, knob = r - 26f;
            if (body > 0f) return new Color(Line.r, Line.g, Line.b, Mathf.Clamp01(1f - body));
            if (body > -6f) return Line;
            if (body > -16f) return new Color(0.70f, 0.72f, 0.76f);                     // 금속 테
            if (knob < 0f) return knob > -5f ? Line : new Color(0.25f, 0.22f, 0.22f);   // 손잡이
            float shine = Mathf.Abs((x - y) - (-60f)) < 14f && r < 120f ? 0.35f : 0f;   // 비스듬한 유리 반사
            return new Color(0.80f + shine * 0.5f, 0.92f, 1f, 0.28f + shine);
        });
        return lid;
    }

    // 가스레인지 밸브 손잡이: 둥근 금속 손잡이 + 위쪽을 가리키는 빨간 표시
    static Sprite knob, flame;
    public static Sprite Knob()
    {
        if (knob == null) knob = Art.Load("Kitchen/knob"); // 그림 파일이 있으면 그걸 쓴다
        if (knob == null) knob = Paint(200, 200, (x, y) =>
        {
            var c = new Vector2(100, 100);
            float r = Vector2.Distance(new Vector2(x, y), c);
            float body = r - 92f;
            float grip = Mathf.Max(Mathf.Abs(x - 100f) - 18f, Mathf.Abs(y - 100f) - 86f); // 가운데 세로 손잡이
            if (body > 0f) return new Color(Line.r, Line.g, Line.b, Mathf.Clamp01(1f - body));
            if (body > -5f) return Line;
            if (grip < 0f)
            {
                if (grip > -4f) return Line;
                return y > 150f ? Bad : new Color(0.95f, 0.95f, 0.97f);              // 위쪽 끝이 빨간 표시
            }
            float hl = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), c + new Vector2(-30, 30)) / 110f);
            return Color.Lerp(new Color(0.55f, 0.56f, 0.60f), new Color(0.82f, 0.83f, 0.87f), hl);
        });
        return knob;
    }

    // 불꽃 고리: 팬 둘레로 삐져나오는 파란·주황 불꽃 (팬 뒤에 깔고 크기로 불 세기를 보여 준다)
    public static Sprite FlameRing()
    {
        if (flame == null) flame = Art.Load("Kitchen/flame"); // 그림 파일이 있으면 그걸 쓴다
        if (flame == null) flame = Paint(300, 300, (x, y) =>
        {
            var p = new Vector2(x - 150f, y - 150f);
            float r = p.magnitude, a = Mathf.Atan2(p.y, p.x);
            float tongue = 0.5f + 0.5f * Mathf.Cos(a * 14f);                // 불꽃 혀 14개
            float outer = 128f + 20f * tongue;
            if (r < 110f || r > outer) return new Color(0, 0, 0, 0);
            float k = Mathf.Clamp01((r - 110f) / (outer - 110f));          // 안쪽 파랑 -> 바깥 주황·노랑
            var col = k < 0.45f ? Color.Lerp(new Color(0.25f, 0.45f, 1f), new Color(0.45f, 0.7f, 1f), k / 0.45f)
                : Color.Lerp(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.3f), (k - 0.45f) / 0.55f);
            col.a = Mathf.Clamp01((outer - r) / 4f) * 0.95f;
            return col;
        });
        return flame;
    }

    // 도구 그림 하나 (끌 수 있게 마우스를 받는다)
    public static RectTransform ToolImage(Transform parent, string name, Sprite s, float x0, float y0, float x1, float y1)
    {
        var rt = Box(parent, name, x0, y0, x1, y1);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = s;
        img.preserveAspect = true;
        var sh = rt.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.25f);
        sh.effectDistance = new Vector2(0f, -6f);
        return rt;
    }

    static RectTransform Tool(Transform parent, string name, float x0, float y0, float x1, float y1, Sprite s)
    {
        var rt = Box(parent, name, x0, y0, x1, y1);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = s;
        img.raycastTarget = false;
        var sh = rt.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.25f);
        sh.effectDistance = new Vector2(0f, -8f);
        return rt;
    }

    static Sprite Paint(int w, int h, Func<int, int, Color> f)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    // 둥근 사각형까지의 거리 (안쪽 음수)
    static float RoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
    {
        float cx = Mathf.Clamp(x, x0 + r, x1 - r), cy = Mathf.Clamp(y, y0 + r, y1 - r);
        return Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) - r;
    }

    public static Text Label(Transform parent, string text, float x0, float y0, float x1, float y1, int size,
        TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null)
    {
        var rt = Box(parent, "Label", x0, y0, x1, y1);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = GetFont();
        t.text = text;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color ?? Ink;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 10;
        t.resizeTextMaxSize = size;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
        // 큰 제목은 흰 테두리 + 그림자로 스티커처럼
        if (size >= 60)
        {
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = Color.white;
            o.effectDistance = new Vector2(4f, 4f);
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.25f);
            sh.effectDistance = new Vector2(0f, -6f);
        }
        return t;
    }

    // 버튼. 기본은 장보기 시안 스타일(색 바탕 + 아래로 두꺼운 그림자, 눌리면 내려앉고, 못 누르면 연하게).
    // classic이 true면 예전 스타일(외곽선 + 글자 테두리)이다. 로비·타이틀처럼 그림으로 꾸민 화면의 대체 버튼이 쓴다.
    public static Button Btn(Transform parent, string text, float x0, float y0, float x1, float y1, Action onClick,
        Color? bg = null, int size = 44, bool enabled = true, bool classic = false)
    {
        var c = bg ?? Accent;
        if (!classic && !enabled) c = new Color(c.r, c.g, c.b, 0.35f); // 못 누르는 버튼은 35% 투명
        var rt = Box(parent, "Btn_" + text, x0, y0, x1, y1, c, false);
        if (classic || enabled)
        {
            // 버튼 두께: 아래로 진한 같은 색 그림자
            var lip = rt.gameObject.AddComponent<Shadow>();
            lip.effectColor = Darker(c, classic ? 0.6f : 0.62f);
            lip.effectDistance = new Vector2(0f, classic ? -9f : -7f);
        }
        if (classic)
        {
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = Ink;
            o.effectDistance = new Vector2(3f, 3f);
        }

        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        var cb = b.colors;
        cb.highlightedColor = new Color(1f, 1f, 1f, 1f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.disabledColor = classic ? new Color(0.6f, 0.6f, 0.6f, 0.6f) : Color.white; // 새 스타일은 색에 이미 35%가 들어 있다
        b.colors = cb;
        var label = Label(rt, text, 0.05f, 0.08f, 0.95f, 0.92f, size, TextAnchor.MiddleCenter, !classic && !enabled ? new Color(1f, 1f, 1f, 0.4f) : Color.white);
        if (classic)
        {
            var to = label.gameObject.AddComponent<Outline>();
            to.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.8f);
            to.effectDistance = new Vector2(2f, 2f);
        }
        if (onClick != null) b.onClick.AddListener(() => onClick());
        b.interactable = enabled;
        rt.gameObject.AddComponent<PressDown>().depth = 4f; // 효과는 눌렀을 때 살짝 내려앉는 것뿐 (커지고 줄어드는 반응은 없다)
        return b;
    }

    // 도감 버튼: 로비의 도감 그림(lobby_dex.png)을 어디서나 쓴다. 그림 비율(380x207)에 맞춰 높이가 정해진다.
    // x0,y0은 왼쪽 아래 모서리, w는 화면 너비에 대한 비율. 그림이 없으면 글자 버튼으로 대신한다.
    public static void DexBtn(Transform parent, float x0, float y0, float w, Action onClick)
    {
        var s = Art.Load("lobby_dex");
        float h = w * (1920f / 1080f) / (380f / 207f);
        if (s == null) { Btn(parent, "도감 [D]", x0, y0, x0 + w, y0 + h, onClick, Muted, 36); return; }
        var rt = Box(parent, "DexBtn", x0, y0, x0 + w, y0 + h);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = s;
        img.preserveAspect = true;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<LobbyHover>().img = img;
        rt.gameObject.AddComponent<PressDown>().depth = 5f;
    }

    // 투명한 클릭 영역 (그림에 이미 그려진 버튼 위에 놓는다). 마우스를 올리면 살짝 밝아진다.
    public static Button HitArea(Transform parent, float x0, float y0, float x1, float y1, Action onClick)
    {
        var rt = Box(parent, "Hit", x0, y0, x1, y1, Color.white, false); // 실제 투명도는 아래 버튼 색이 정한다
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        var cb = b.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 0f);
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.25f);
        cb.selectedColor = cb.normalColor;
        cb.pressedColor = new Color(1f, 1f, 1f, 0.4f);
        b.colors = cb;
        b.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<Bouncy>();
        return b;
    }
}
