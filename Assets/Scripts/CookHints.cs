using UnityEngine;
using UnityEngine.UI;

// 조리 중 안내를 크게 보이게 한다.
// - 타이밍 알림: 뒤집을 때(굽기), 끓어넘칠 때(끓이기)는 팬/냄비 위에 큰 빨간 알림이 통통 튀며 뜬다.
// - 엄마 말풍선: 오른쪽 아래 말풍선(TipBg)과 글씨를 키운다. 그림 비율(694x216)은 그대로 둔다.
// GameFlow.StartCook 끝에서 Attach 한 줄로 붙인다.
public class CookHints : MonoBehaviour
{
    const float RefW = 1920f, RefH = 1080f;
    const float TipRatio = 694f / 216f; // 말풍선 그림 비율

    public static void Attach(Transform root, Cook cook, RectTransform panRect, bool cookArt)
    {
        if (cook == null || panRect == null) return;
        if (cookArt) BigMomBubble(root);

        // 팬/냄비 칸 위쪽에 큰 알림 띠
        float px0 = panRect.anchorMin.x, px1 = panRect.anchorMax.x, py1 = panRect.anchorMax.y, ph = py1 - panRect.anchorMin.y;
        float top = cookArt ? 0.80f : py1 - ph * 0.02f; // 그림 화면은 위 안내 칸(0.83~) 바로 아래
        var banner = UIKit.Box(root, "TimingAlert", px0 + (px1 - px0) * 0.08f, top - 0.10f, px1 - (px1 - px0) * 0.08f, top, UIKit.Bad);
        var text = UIKit.Label(banner, "", 0.03f, 0.06f, 0.97f, 0.94f, 52, TextAnchor.MiddleCenter, Color.white);
        var o = text.gameObject.AddComponent<Outline>();
        o.effectColor = UIKit.Ink;
        o.effectDistance = new Vector2(3f, 3f);
        foreach (var g in banner.GetComponentsInChildren<Graphic>()) g.raycastTarget = false; // 아래 재료 클릭을 막지 않게

        // 알림 띠를 껐다 켜야 해서, 갱신하는 쪽은 늘 켜져 있는 따로 된 오브젝트에 둔다
        var holder = new GameObject("CookHints", typeof(RectTransform));
        holder.transform.SetParent(root, false);
        var h = holder.AddComponent<CookHints>();
        h.cook = cook;
        h.banner = banner;
        h.text = text;
        h.Refresh();
    }

    // 엄마의 조언 말풍선을 키운다 (오른쪽 아래 귀퉁이에 붙인 채로 더 크게)
    static void BigMomBubble(Transform root)
    {
        var tip = root.Find("TipBg") as RectTransform;
        if (tip == null) return;
        float w = 0.40f;                              // 화면 폭 비율
        float h = w * RefW / TipRatio / RefH;         // 그림 비율을 지킨 높이
        tip.anchorMin = new Vector2(0.995f - w, 0.012f);
        tip.anchorMax = new Vector2(0.995f, 0.012f + h);
        tip.offsetMin = tip.offsetMax = Vector2.zero;
        foreach (var t in tip.GetComponentsInChildren<Text>())
        {
            t.fontSize = 36;
            t.resizeTextMaxSize = 36;
            t.resizeTextMinSize = 20;
        }
    }

    Cook cook;
    RectTransform banner;
    Text text;

    void Update() { Refresh(); }

    void Refresh()
    {
        if (cook == null) return;
        string msg = cook.finished ? null
            : cook.m.id == Method.Boil && cook.bubble ? "끓어넘친다! 뚜껑을 떼!"
            : cook.m.id == Method.Grill && cook.flipAvailable ? "지금 뒤집어!"
            : null;
        bool on = msg != null;
        if (banner.gameObject.activeSelf != on) banner.gameObject.SetActive(on);
        if (!on) return;
        text.text = msg;
        float s = 1f + Mathf.Abs(Mathf.Sin(Time.time * 7f)) * 0.08f; // 통통 튀어서 눈에 띄게
        banner.localScale = new Vector3(s, s, 1f);
    }
}
