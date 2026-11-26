using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// 재료 넣는 순서 그림 줄 (docs/28). 조리 화면 위쪽에 이번 요리의 재료를 넣을 순서대로 사진으로 늘어놓는다.
// 같은 단계끼리는 순서가 상관없으니 가까이 묶고, 단계 사이에는 화살표를 둔다. 항상 보여 준다.
// 이미 넣은 재료는 흐려지고, 지금 넣을 차례인 재료는 두근거리며, 순서를 틀리면 줄 전체가 잠깐 빨갛게 번쩍인다.
public partial class GameFlow
{
    class OrderIcon { public Image img; public RectTransform rt; public int tier; public int itemIndex; public Color baseColor; }
    readonly List<OrderIcon> orderIcons = new List<OrderIcon>();
    int orderSeenViolations;
    float orderFlashUntil;

    void BuildOrderRow()
    {
        orderIcons.Clear();
        orderSeenViolations = 0; orderFlashUntil = -1f;
        if (!cookArt || cook == null) return;

        // 순서가 있는 재료는 단계순, 순서 제한이 없는 재료는 맨 뒤에 따로 둔다
        var order = Enumerable.Range(0, cook.items.Length)
            .Select(i => new { i, tier = cook.TierOf(cook.items[i].ing) })
            .OrderBy(x => x.tier < 0 ? 99 : x.tier).ThenBy(x => x.i).ToList();
        if (order.Count == 0) return;

        const float x0 = 0.52f, x1 = 0.885f, y0 = 0.90f, y1 = 0.975f;
        float w = (x1 - x0) * RefW, h = (y1 - y0) * RefH;       // 줄 칸의 픽셀 크기
        float icon = Mathf.Min(h - 10f, 62f), near = 6f, arrow = 34f, gap = 8f;
        var row = UIKit.Box(root, "OrderRow", x0, y0, x1, y1, Color.clear, false);
        var title = UIKit.Label(row, "넣는 순서", 0f, 0f, 90f / w, 1f, 24, TextAnchor.MiddleLeft, new Color(0.3f, 0.2f, 0.1f));
        title.raycastTarget = false;

        float cx = 96f;
        for (int k = 0; k < order.Count; k++)
        {
            var o = order[k];
            if (k > 0)
            {
                bool sameGroup = order[k - 1].tier == o.tier;
                bool unorderedStart = o.tier < 0 && order[k - 1].tier >= 0;
                if (sameGroup) cx += near;
                else
                {
                    if (unorderedStart) { cx += gap; }
                    else
                    {
                        var ar = UIKit.Label(row, "▶", cx / w, 0f, (cx + arrow) / w, 1f, 24, TextAnchor.MiddleCenter, new Color(0.55f, 0.35f, 0.15f));
                        ar.raycastTarget = false;
                        cx += arrow;
                    }
                }
            }
            var ing = cook.items[o.i].ing;
            var rt = UIKit.Box(row, "OrderIcon" + o.i, cx / w, (h - icon) / 2f / h, (cx + icon) / w, (h + icon) / 2f / h, Color.white, false);
            var img = rt.GetComponent<Image>();
            var sp = Art.ShopIcon(ing);
            if (sp != null) { img.sprite = sp; img.type = Image.Type.Simple; img.preserveAspect = true; img.color = Color.white; }
            else img.color = ing.raw;
            img.raycastTarget = false;
            orderIcons.Add(new OrderIcon { img = img, rt = rt, tier = o.tier, itemIndex = o.i, baseColor = img.color });
            cx += icon;
        }
    }

    // 매 프레임: 넣은 재료 흐리게, 다음 차례 두근, 틀리면 빨갛게
    void UpdateOrderRow()
    {
        if (cook == null || orderIcons.Count == 0) return;
        if (cook.OrderViolations > orderSeenViolations) { orderSeenViolations = cook.OrderViolations; orderFlashUntil = Time.time + 0.7f; }
        bool flash = Time.time < orderFlashUntil;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);

        // 아직 트레이에 있는 재료 중 가장 이른 단계가 "지금 차례"
        int nextTier = int.MaxValue;
        foreach (var oi in orderIcons)
            if (oi.tier >= 0 && cook.items[oi.itemIndex].spot == Spot.Tray) nextTier = Mathf.Min(nextTier, oi.tier);

        foreach (var oi in orderIcons)
        {
            var it = cook.items[oi.itemIndex];
            bool placed = it.spot != Spot.Tray;
            bool isNext = !placed && oi.tier >= 0 && oi.tier == nextTier;
            Color c = oi.baseColor;
            if (placed) c = new Color(c.r, c.g, c.b, 0.3f);
            else if (flash) c = Color.Lerp(c, UIKit.Bad, 0.6f * pulse + 0.2f);
            oi.img.color = c;
            oi.rt.localScale = Vector3.one * (isNext ? 1.12f + 0.08f * pulse : 1f);
        }
    }
}
