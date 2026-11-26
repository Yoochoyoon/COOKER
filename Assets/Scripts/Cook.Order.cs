using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 재료 넣는 순서 (docs/28). 방식마다 맞는 순서가 있고, 틀리게 넣으면 만족도 보정이 곱해진다 (실패로 끝나지는 않는다).
// 수치는 아직 BalanceData에 없으므로 아래 임시값을 쓴다. 확정되면 Balance로 옮긴다.
public partial class Cook
{
    // 임시값 (docs/28 제안). TODO: BalanceData로 옮기고 Balance에서 읽는다.
    const float OrderCoefPerViolation = 0.9f; // 순서를 한 번 틀릴 때마다 곱하는 보정
    const int OrderHintRuns = 3;              // 처음 이 판 수까지 힌트를 보여 준다
    const string OrderHintKey = "cooker.orderHintRuns";
    const float OrderMessageSeconds = 3f;     // 틀렸을 때 안내를 보여 주는 시간

    public int OrderViolations { get; private set; }
    public bool OrderHintOn { get; private set; }   // 이번 판에 순서 힌트를 보여 줄지 (처음 몇 판만)
    float orderMsgAt = -99f;
    string orderMsg;

    // 순서 단계(작을수록 먼저). 같은 단계끼리는 어떤 순서로 넣어도 맞다. 목록에 없으면 순서 제한 없음(-1).
    static int Tier(Method id, string ingName)
    {
        switch (id)
        {
            case Method.Grill:
                switch (ingName) { case "감자": return 0; case "돼지고기": return 1; case "양파": return 2; case "김치": return 3; case "달걀": return 4; }
                break;
            case Method.Stirfry:
                switch (ingName) { case "감자": case "양파": return 0; case "돼지고기": return 1; case "김치": return 2; case "달걀": return 3; }
                break;
            case Method.Boil:
                switch (ingName) { case "감자": return 0; case "돼지고기": return 1; case "두부": return 2; case "달걀": return 3; }
                break;
        }
        return -1;
    }

    // 이 재료의 순서 단계 (작을수록 먼저, 순서 제한이 없으면 -1). 화면의 순서 그림 줄이 쓴다.
    public int TierOf(Ingredient ing) { return Tier(m.id, ing.name); }

    // 지금 이 판의 순서 판정 보정 (틀린 횟수만큼 곱한다)
    public float OrderMultiplier { get { return Mathf.Pow(OrderCoefPerViolation, OrderViolations); } }

    // 생성자에서 한 번 부른다: 힌트를 보여 줄 판인지 정한다
    void InitOrder()
    {
        // 순서를 따질 거리가 있는 요리(서로 다른 단계가 둘 이상)만 힌트 판 수로 센다. 첫날 달걀 하나짜리 요리로 힌트가 줄지 않게.
        bool ordered = items.Select(it => Tier(m.id, it.ing.name)).Where(x => x >= 0).Distinct().Count() >= 2;
        int runs = PlayerPrefs.GetInt(OrderHintKey, 0);
        OrderHintOn = runs < OrderHintRuns;
        if (ordered) PlayerPrefs.SetInt(OrderHintKey, runs + 1);
    }

    // 트레이의 재료를 팬에 넣을 때 부른다. 이미 더 늦은 단계의 재료가 들어가 있으면 순서 위반.
    void CheckOrderOnPut(CookItem putting)
    {
        int tier = Tier(m.id, putting.ing.name);
        if (tier < 0) return;
        bool late = items.Any(it => it != putting && it.spot != Spot.Tray && Tier(m.id, it.ing.name) > tier);
        if (!late) return;
        OrderViolations++;
        orderMsgAt = t;
        // 올바른 다음 재료도 알려 준다 (docs/28 4장). 지금 넣는 재료는 빼고 찾는다.
        var next = items.Where(it => it != putting && it.spot == Spot.Tray && Tier(m.id, it.ing.name) >= 0)
            .OrderBy(it => Tier(m.id, it.ing.name)).FirstOrDefault();
        orderMsg = next == null ? "순서가 틀렸어! 오래 익는 재료부터 넣어야 해."
            : "순서가 틀렸어! 다음은 " + next.ing.name + " 차례야.";
    }

    // 트레이에 남은 재료 중 지금 넣을 차례인 재료 이름 (순서가 없는 방식이면 null)
    public string NextIngredientName()
    {
        var best = items.Where(it => it.spot == Spot.Tray && Tier(m.id, it.ing.name) >= 0)
            .OrderBy(it => Tier(m.id, it.ing.name)).FirstOrDefault();
        return best == null ? null : best.ing.name;
    }

    // 말풍선에 띄울 순서 안내 한 줄. 지금 안내할 게 없으면 null.
    public string OrderLine()
    {
        if (orderMsg != null && t - orderMsgAt < OrderMessageSeconds) return orderMsg;
        if (!OrderHintOn) return null;
        string next = NextIngredientName();
        return next == null ? null : "먼저 " + next + "부터 넣어 봐.";
    }
}
