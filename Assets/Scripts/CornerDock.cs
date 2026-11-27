using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 도움말 [?]과 설정 톱니를 어느 화면에서든 다른 글자·그림·버튼을 가리지 않는 가장자리에 놓는다.
// 두 아이콘을 한 쌍으로 묶어 화면 네 변을 따라 후보 자리를 훑고, 겹침이 가장 적은 자리를 고른다 (겹침이 없으면 왼쪽 위에 가까운 자리).
// 지금 자리가 비어 있으면 그대로 둔다 (자꾸 옮겨 다니지 않게).
public class CornerDock : MonoBehaviour
{
    public static bool Active { get; private set; }

    const float Icon = 70f;       // 아이콘 한 변 (기준 1920x1080 픽셀)
    const float Gap = 14f;        // 두 아이콘 사이
    const float Margin = 102f;    // 화면 가장자리 100픽셀은 비워 둔다 (여백 안에는 아무것도 두지 않는다)
    const float Step = 24f;       // 후보 자리를 훑는 간격 (기준 픽셀)
    const float CheckEvery = 0.3f;
    const float ObstaclePad = 10f; // 장애물 둘레 여유 (기준 픽셀)

    static int forceFrames;   // 화면이 바뀐 직후 몇 프레임은 기다리지 않고 바로 다시 자리를 잡는다
    public static void Recheck() { forceFrames = 3; }

    float next;
    bool placed;
    Rect curRect;            // 지금 놓인 쌍의 영역 (화면 픽셀)
    bool curVertical;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<CornerDock>() != null) return;
        var go = new GameObject("COOKER_CornerDock");
        DontDestroyOnLoad(go);
        go.AddComponent<CornerDock>();
    }

    void Update()
    {
        if (Help.IconRect == null || SettingsButton.GearRect == null) return;
        Active = true;
        if (forceFrames > 0) forceFrames--;
        else if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + CheckEvery;
        Place();
    }

    void Place()
    {
        float W = Screen.width, H = Screen.height;
        if (W <= 0f || H <= 0f) return;
        float s = W / 1920f;                  // 기준 픽셀 → 화면 픽셀
        float ic = Icon * s, gap = Gap * s, mg = Margin * s, step = Step * s;

        var obstacles = Obstacles(W, H);

        // 지금 자리가 비어 있으면 그대로 둔다
        if (placed && Overlap(curRect, obstacles) <= 0f) { Apply(curRect, curVertical, ic, W, H); return; }

        float pairLong = ic * 2f + gap;
        float best = float.MaxValue; Rect bestRect = new Rect(); bool bestVert = false;
        void Try(Rect r, bool vert)
        {
            // 겹침이 적을수록 좋고, 겹침이 같으면 화면 모서리에 가까울수록 좋다 (왼쪽 위 > 왼쪽 아래 > 오른쪽 위 > 오른쪽 아래 순으로 살짝 선호).
            // 다른 것들 사이(바 한가운데 등)에 끼지 않고 가장자리 모서리 쪽에 놓이게 하려는 것이다.
            var c = r.center;
            float dTL = Vector2.Distance(c, new Vector2(0f, H)) + 0f * s;
            float dBL = Vector2.Distance(c, new Vector2(0f, 0f)) + 300f * s;
            float dTR = Vector2.Distance(c, new Vector2(W, H)) + 450f * s;
            float dBR = Vector2.Distance(c, new Vector2(W, 0f)) + 600f * s;
            float score = Overlap(r, obstacles) * 1000f + Mathf.Min(Mathf.Min(dTL, dBL), Mathf.Min(dTR, dBR));
            if (score < best) { best = score; bestRect = r; bestVert = vert; }
        }
        // 위·아래 변(가로로 나란히)
        for (float x = mg; x + pairLong <= W - mg + 0.1f; x += step)
        {
            Try(new Rect(x, H - mg - ic, pairLong, ic), false);   // 위
            Try(new Rect(x, mg, pairLong, ic), false);            // 아래
        }
        // 왼쪽·오른쪽 변(세로로 나란히)
        for (float y = mg; y + pairLong <= H - mg + 0.1f; y += step)
        {
            Try(new Rect(mg, y, ic, pairLong), true);             // 왼쪽
            Try(new Rect(W - mg - ic, y, ic, pairLong), true);    // 오른쪽
        }
        curRect = bestRect; curVertical = bestVert; placed = true;
        Apply(curRect, curVertical, ic, W, H);
    }

    // 쌍의 영역에 맞춰 두 아이콘의 앵커를 옮긴다. 도움말이 앞, 설정이 뒤.
    void Apply(Rect r, bool vertical, float ic, float W, float H)
    {
        float gap = Gap * (W / 1920f);
        Rect help, gear;
        if (vertical)
        {
            help = new Rect(r.x, r.yMax - ic, ic, ic);
            gear = new Rect(r.x, r.yMax - ic * 2f - gap, ic, ic);
        }
        else
        {
            help = new Rect(r.x, r.y, ic, ic);
            gear = new Rect(r.x + ic + gap, r.y, ic, ic);
        }
        SetRect(Help.IconRect, help, W, H);
        SetRect(SettingsButton.GearRect, gear, W, H);
    }

    static void SetRect(RectTransform rt, Rect px, float W, float H)
    {
        rt.anchorMin = new Vector2(px.xMin / W, px.yMin / H);
        rt.anchorMax = new Vector2(px.xMax / W, px.yMax / H);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static float Overlap(Rect a, List<Rect> obs)
    {
        float sum = 0f;
        foreach (var o in obs)
        {
            float w = Mathf.Min(a.xMax, o.xMax) - Mathf.Max(a.xMin, o.xMin);
            float h = Mathf.Min(a.yMax, o.yMax) - Mathf.Max(a.yMin, o.yMin);
            if (w > 0f && h > 0f) sum += w * h;
        }
        return sum;
    }

    // 화면에 보이는 글자·그림·버튼의 영역 (화면 픽셀). 배경처럼 화면 절반 넘게 덮는 것과 우리 아이콘의 캔버스는 뺀다.
    static List<Rect> Obstacles(float W, float H)
    {
        var list = new List<Rect>();
        float limit = W * H * 0.5f;
        foreach (var g in FindObjectsByType<Graphic>(FindObjectsSortMode.None))
        {
            if (!g.isActiveAndEnabled || g.color.a < 0.08f || g.canvas == null) continue;
            var root = g.canvas.rootCanvas;
            if (root == null || root.gameObject.name == "HelpCanvas" || root.gameObject.name == "SettingsCanvas") continue;
            var t = g as Text;
            Rect r;
            if (t != null)
            {
                if (string.IsNullOrEmpty(t.text)) continue;
                r = TextRect(t);
            }
            else
            {
                if (g is Image img && img.sprite == null && g.color.a < 0.2f) continue;
                r = ScreenRect(g.rectTransform);
            }
            if (r.width <= 1f || r.height <= 1f || r.width * r.height >= limit) continue;
            float pad = ObstaclePad * (W / 1920f); // 아이콘이 다른 것에 딱 붙지 않게 약간 여유를 둔다
            list.Add(new Rect(r.x - pad, r.y - pad, r.width + pad * 2f, r.height + pad * 2f));
        }
        return list;
    }

    static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return Rect.MinMaxRect(Mathf.Min(c[0].x, c[2].x), Mathf.Min(c[0].y, c[2].y), Mathf.Max(c[0].x, c[2].x), Mathf.Max(c[0].y, c[2].y));
    }

    // 글자는 칸 전체가 아니라 실제로 글자가 차지하는 만큼만 장애물로 친다
    static Rect TextRect(Text t)
    {
        var r = ScreenRect(t.rectTransform);
        float scale = t.canvas != null && t.canvas.rootCanvas != null ? t.canvas.rootCanvas.scaleFactor : 1f;
        float w = Mathf.Min(r.width, t.preferredWidth * scale), h = Mathf.Min(r.height, t.preferredHeight * scale);
        float x = r.xMin, y = r.yMin;
        switch (t.alignment)
        {
            case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: x = r.center.x - w / 2f; break;
            case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: x = r.xMax - w; break;
        }
        switch (t.alignment)
        {
            case TextAnchor.UpperLeft: case TextAnchor.UpperCenter: case TextAnchor.UpperRight: y = r.yMax - h; break;
            case TextAnchor.MiddleLeft: case TextAnchor.MiddleCenter: case TextAnchor.MiddleRight: y = r.center.y - h / 2f; break;
        }
        return new Rect(x, y, w, h);
    }
}
