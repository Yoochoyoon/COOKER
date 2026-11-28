using UnityEngine;
using UnityEngine.UI;

// 불 바늘 게이지 화면 (docs/26). 조리 화면 왼쪽에 세로 막대를 두고, 바늘이 오르내리며 적정 구간(초록)에 머물게 한다.
// 판정과 수치는 Cook.Fire.cs가 맡고, 이 파일은 그리기와 경고 효과만 맡는다.
//   정상: 화살표 숨김, 테두리 얇게
//   경고(T1): 바늘 옆 화살표가 가야 할 방향을 가리키며 깜빡임, 테두리 빨갛고 굵게, 밸브가 은은하게 깜빡임
//   위험(T2): 위에 더해 게이지가 빨갛게 맥박, 밸브 둘레 점선 링이 돌며 깜빡임, 밸브가 크게 두근, 경고음 한 번
public partial class GameFlow
{
    Image gaugeTrack;
    Outline gaugeBorder;
    RectTransform gaugeNeedle;
    Image gaugeNeedleImg;
    RectTransform gaugeArrow;
    Image gaugeArrowImg;
    Image valveRing;
    Text gaugeLabel;
    Image overallFill; // 전체 익힘 막대 (docs/27)
    int gaugePrevState;

    // 게이지 막대 자리 (화면 비율). 화살표가 바늘 높이를 따라가려면 같은 값을 써야 해서 한 곳에 둔다
    const float GaugeX0 = 0.010f, GaugeX1 = 0.030f, GaugeY0 = 0.22f, GaugeY1 = 0.80f;
    const float ArrowGap = 0.004f, ArrowW = 0.020f;
    const float BlinkPeriod = 0.5f;          // docs/26 5번: 약 0.5초 주기 깜빡임
    const float BorderThin = 0.5f, BorderThick = 2f;
    const float ValveRingSize = 190f, ValveRingSpin = 60f; // 밸브(145px)보다 조금 크게, 초당 60도 돈다
    static readonly Color TrackBase = new Color(0.35f, 0.25f, 0.2f, 0.55f);
    static readonly Color TrackDanger = new Color(0.85f, 0.2f, 0.15f, 0.85f);
    static readonly Color BorderBase = new Color(0.25f, 0.17f, 0.12f, 0.8f);

    void BuildGaugeUI()
    {
        gaugeTrack = null; gaugeNeedle = null; gaugeLabel = null; overallFill = null;
        gaugeArrow = null; valveRing = null; gaugeBorder = null; gaugePrevState = 0;
        if (!cookArt || cook == null) return; // 새 조리 화면에서만 그린다
        var track = UIKit.Box(root, "GaugeTrack", GaugeX0, GaugeY0, GaugeX1, GaugeY1, TrackBase, false);
        gaugeTrack = track.GetComponent<Image>();
        gaugeTrack.raycastTarget = false;
        gaugeBorder = track.GetComponent<Outline>(); // Unity 오브젝트는 ??로 null을 거르면 안 된다
        if (gaugeBorder == null) gaugeBorder = track.gameObject.AddComponent<Outline>();
        gaugeBorder.effectColor = BorderBase;
        gaugeBorder.effectDistance = new Vector2(BorderThin, BorderThin);
        var zone = UIKit.Box(track, "GaugeZone", 0f, cook.GaugeZoneLo, 1f, cook.GaugeZoneHi, UIKit.Good, false);
        zone.GetComponent<Image>().raycastTarget = false;
        gaugeNeedle = UIKit.Box(track, "GaugeNeedle", -0.7f, 0.49f, 1.7f, 0.51f, UIKit.Ink, false);
        gaugeNeedleImg = gaugeNeedle.GetComponent<Image>();
        gaugeNeedleImg.raycastTarget = false;

        // 화살표: 바늘 오른쪽에 붙어 다니며 바늘이 가야 할 방향(위/아래)을 가리킨다
        gaugeArrow = UIKit.Box(root, "GaugeArrow", 0f, 0f, 0f, 0f);
        gaugeArrowImg = gaugeArrow.gameObject.AddComponent<Image>();
        gaugeArrowImg.sprite = TriangleSprite();
        gaugeArrowImg.raycastTarget = false;
        gaugeArrowImg.color = new Color(UIKit.Bad.r, UIKit.Bad.g, UIKit.Bad.b, 0f);

        gaugeLabel = UIKit.Label(root, "", 0.100f, 0.030f, 0.30f, 0.088f, 34, TextAnchor.MiddleLeft, UIKit.Bad); // 상태 메시지도 밸브 오른쪽, 불 세기 글자 아래
        gaugeLabel.raycastTarget = false;

        // 밸브 둘레 점선 링 (위험일 때만 보인다). 밸브와 같은 자리, 같은 중심
        if (knobRT != null)
        {
            var ringRT = UIKit.Box(root, "ValveRing", 0f, 0f, 0f, 0f);
            SetSquare(ringRT, KnobCenter, ValveRingSize);
            valveRing = ringRT.gameObject.AddComponent<Image>();
            valveRing.sprite = DashedRingSprite();
            valveRing.raycastTarget = false; // 밸브를 돌리는 드래그를 가로막지 않게
            valveRing.color = new Color(UIKit.Bad.r, UIKit.Bad.g, UIKit.Bad.b, 0f);
        }

        // 전체 익힘 막대(docs/27): 팬 전체의 익힘을 막대 하나로 보여 준다. 재료별 게이지는 숨긴다.
        foreach (var r in itemRects) { var gb = r.Find("GaugeBg"); if (gb != null) gb.gameObject.SetActive(false); }
        var bar = UIKit.Box(root, "OverallBar", 0.36f, 0.185f, 0.64f, 0.215f, new Color(0f, 0f, 0f, 0.45f), false);
        bar.GetComponent<Image>().raycastTarget = false;
        overallFill = UIKit.Box(bar, "OverallFill", 0f, 0f, 0f, 1f, Color.white, false).GetComponent<Image>();
        overallFill.raycastTarget = false;
        // 조리 완료 버튼: 팬의 재료를 한꺼번에 접시에 담고 끝낸다 (Enter도 같다)
        UIKit.PrimaryBtn(root, "조리 완료 [Enter]", 0.70f, 0.03f, 0.945f, 0.15f, () => { if (cook != null) cook.Complete(); }, 38);
    }

    // 밸브 손잡이 중심 (StartCook의 SetSquare(knobRT, ...)와 같은 값)
    // 밸브 중심을 세로 타이밍 바의 가운데(GaugeX0과 GaugeX1의 가운데)에 맞춰 바 바로 아래 같은 줄에 놓는다
    const float KnobFx = (GaugeX0 + GaugeX1) * 0.5f, KnobFy = 0.095f;
    static Vector2 KnobCenter { get { return new Vector2(KnobFx * RefW, KnobFy * RefH); } }

    // 매 프레임: 바늘 위치, 구간 밖 경고, 밸브 강조
    void UpdateGaugeUI()
    {
        if (cook == null || gaugeNeedle == null) return;
        if (overallFill != null)
        {
            float od = cook.OverallD;
            overallFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(od / 100f), 1f);
            overallFill.color = cook.WholeInRange ? new Color(1f, 0.55f, 0.1f) : od < cook.Lo ? new Color(1f, 0.85f, 0.2f) : new Color(0.85f, 0.15f, 0.1f);
        }
        float v = cook.GaugeValue;
        gaugeNeedle.anchorMin = new Vector2(-0.7f, Mathf.Clamp01(v - 0.012f));
        gaugeNeedle.anchorMax = new Vector2(1.7f, Mathf.Clamp01(v + 0.012f));
        int state = cook.GaugeState;
        int dir = cook.GaugeDir;
        float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / BlinkPeriod); // 0~1

        if (state == 2 && gaugePrevState < 2) Sound.Play("warn"); // 위험에 들어가는 순간 한 번만
        gaugePrevState = state;

        gaugeNeedleImg.color = state >= 1 ? Color.Lerp(UIKit.Ink, UIKit.Bad, blink) : UIKit.Ink;
        gaugeTrack.color = state == 2 ? Color.Lerp(TrackBase, TrackDanger, blink) : TrackBase;
        if (gaugeBorder != null)
        {
            bool warn = state >= 1;
            gaugeBorder.effectColor = warn ? UIKit.Bad : BorderBase;
            float bw = warn ? BorderThick : BorderThin;
            gaugeBorder.effectDistance = new Vector2(bw, bw);
        }

        // 화살표: 바늘 높이에 맞춰 따라다닌다. 구간 위면 아래 화살표, 아래면 위 화살표
        if (gaugeArrow != null)
        {
            bool show = state >= 1 && dir != 0;
            float y = GaugeY0 + v * (GaugeY1 - GaugeY0);
            float h = ArrowW * RefW / RefH; // 정사각형으로 보이게 세로 비율을 화면 비율로 맞춘다
            gaugeArrow.anchorMin = new Vector2(GaugeX1 + ArrowGap, y - h / 2f);
            gaugeArrow.anchorMax = new Vector2(GaugeX1 + ArrowGap + ArrowW, y + h / 2f);
            gaugeArrow.offsetMin = Vector2.zero; gaugeArrow.offsetMax = Vector2.zero;
            gaugeArrow.localRotation = Quaternion.Euler(0f, 0f, dir < 0 ? 180f : 0f);
            float a = show ? 0.35f + 0.65f * blink : 0f;
            gaugeArrowImg.color = new Color(UIKit.Bad.r, UIKit.Bad.g, UIKit.Bad.b, a);
        }

        // 바늘이 위에 있으면 불을 내리고, 아래에 있으면 올리라고 글자로도 알린다
        gaugeLabel.text = state >= 1 ? (dir < 0 ? "불 줄여요!" : dir > 0 ? "불 키워요!" : "") : "";

        // 밸브: 경고면 은은하게, 위험이면 세게 깜빡이고 두근거린다
        if (knobRT != null)
        {
            var ki = knobRT.GetComponent<Image>();
            if (ki != null)
                ki.color = state == 2 ? Color.Lerp(Color.white, UIKit.Bad, 0.5f + 0.5f * blink)
                    : state == 1 ? Color.Lerp(Color.white, UIKit.Bad, 0.15f + 0.25f * blink)
                    : Color.white;
            knobRT.localScale = Vector3.one * (state == 2 ? 1f + 0.06f * blink : 1f);
        }
        if (valveRing != null)
        {
            valveRing.color = new Color(UIKit.Bad.r, UIKit.Bad.g, UIKit.Bad.b, state == 2 ? 0.4f + 0.6f * blink : 0f);
            valveRing.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Time.time * ValveRingSpin);
        }
    }

    // 위를 가리키는 세모 (화살표). 그림 파일 없이 한 번 만들어 계속 쓴다
    static Sprite triangleSprite;
    static Sprite TriangleSprite()
    {
        if (triangleSprite != null) return triangleSprite;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float fy = (y + 0.5f) / S, fx = Mathf.Abs((x + 0.5f) / S - 0.5f) * 2f; // 가운데 0 ~ 가장자리 1
                float edge = 1f - fy;                                                  // 위로 갈수록 좁아진다
                float a = Mathf.Clamp01((edge - fx) * S * 0.5f);                       // 가장자리를 1~2px 부드럽게
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, fy < 0.08f ? 0f : a));
            }
        tex.Apply();
        triangleSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return triangleSprite;
    }

    // 점선 고리 (밸브 위험 표시). 한 번 만들어 계속 쓴다
    static Sprite dashedRingSprite;
    static Sprite DashedRingSprite()
    {
        if (dashedRingSprite != null) return dashedRingSprite;
        const int S = 256, Dashes = 16;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / (S / 2f);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.06f);
                float ang = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f) * Dashes;
                bool on = Mathf.Repeat(ang, 1f) < 0.55f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, on ? ring : 0f));
            }
        tex.Apply();
        dashedRingSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return dashedRingSprite;
    }
}
