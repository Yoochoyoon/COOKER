using UnityEngine;
using UnityEngine.UI;

// 불 바늘 게이지 화면 (docs/26). 조리 화면 왼쪽에 세로 막대를 두고, 바늘이 오르내리며 적정 구간(초록)에 머물게 한다.
// 판정과 수치는 Cook.Fire.cs가 맡고, 이 파일은 그리기와 경고 효과만 맡는다.
public partial class GameFlow
{
    Image gaugeTrack;
    RectTransform gaugeNeedle;
    Image gaugeNeedleImg;
    Text gaugeLabel;
    Image overallFill; // 전체 익힘 막대 (docs/27)

    void BuildGaugeUI()
    {
        gaugeTrack = null; gaugeNeedle = null; gaugeLabel = null; overallFill = null;
        if (!cookArt || cook == null) return; // 새 조리 화면에서만 그린다
        var track = UIKit.Box(root, "GaugeTrack", 0.010f, 0.22f, 0.030f, 0.80f, new Color(0.35f, 0.25f, 0.2f, 0.55f), false);
        gaugeTrack = track.GetComponent<Image>();
        gaugeTrack.raycastTarget = false;
        var zone = UIKit.Box(track, "GaugeZone", 0f, cook.GaugeZoneLo, 1f, cook.GaugeZoneHi, UIKit.Good, false);
        zone.GetComponent<Image>().raycastTarget = false;
        gaugeNeedle = UIKit.Box(track, "GaugeNeedle", -0.7f, 0.49f, 1.7f, 0.51f, UIKit.Ink, false);
        gaugeNeedleImg = gaugeNeedle.GetComponent<Image>();
        gaugeNeedleImg.raycastTarget = false;
        gaugeLabel = UIKit.Label(root, "", 0.0f, 0.165f, 0.14f, 0.225f, 34, TextAnchor.MiddleCenter, UIKit.Bad);
        gaugeLabel.raycastTarget = false;

        // 전체 익힘 막대(docs/27): 팬 전체의 익힘을 막대 하나로 보여 준다. 재료별 게이지는 숨긴다.
        foreach (var r in itemRects) { var gb = r.Find("GaugeBg"); if (gb != null) gb.gameObject.SetActive(false); }
        var bar = UIKit.Box(root, "OverallBar", 0.36f, 0.185f, 0.64f, 0.215f, new Color(0f, 0f, 0f, 0.45f), false);
        bar.GetComponent<Image>().raycastTarget = false;
        overallFill = UIKit.Box(bar, "OverallFill", 0f, 0f, 0f, 1f, Color.white, false).GetComponent<Image>();
        overallFill.raycastTarget = false;
        // 조리 완료 버튼: 팬의 재료를 한꺼번에 접시에 담고 끝낸다 (Enter도 같다)
        UIKit.PrimaryBtn(root, "조리 완료 [Enter]", 0.70f, 0.03f, 0.945f, 0.15f, () => { if (cook != null) cook.Complete(); }, 38);
    }

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
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);
        gaugeNeedleImg.color = state >= 1 ? Color.Lerp(UIKit.Ink, UIKit.Bad, pulse) : UIKit.Ink;
        gaugeTrack.color = state == 2 ? Color.Lerp(new Color(0.35f, 0.25f, 0.2f, 0.55f), new Color(0.85f, 0.2f, 0.15f, 0.85f), pulse)
            : new Color(0.35f, 0.25f, 0.2f, 0.55f);
        // 바늘이 위에 있으면 불을 내리고, 아래에 있으면 올리라고 알린다
        gaugeLabel.text = state >= 1 ? (cook.GaugeDir < 0 ? "내려요!" : cook.GaugeDir > 0 ? "올려요!" : "") : "";
        // 밸브 손잡이도 같이 깜빡여서 얼른 돌리라고 알린다
        if (knobRT != null)
        {
            var ki = knobRT.GetComponent<Image>();
            if (ki != null) ki.color = state >= 1 ? Color.Lerp(Color.white, UIKit.Bad, 0.35f + 0.4f * pulse) : Color.white;
        }
    }
}
