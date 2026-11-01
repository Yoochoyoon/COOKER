using System.Linq;
using UnityEngine;

// 조리 한 번의 상태. 화면과 무관한 순수 로직이라 수치 검증이 쉽다.
public enum Doneness { Raw, Under, Good, Over, Burnt }

// 재료가 놓인 자리. 트레이에 있는 재료는 익지 않고, 팬에 넣은 순간부터 익는다.
public enum Spot { Tray, Pan, Plate }

public class CookItem
{
    public Ingredient ing;
    public Spot spot = Spot.Tray;
    public float d;          // 익힘 0~100 (팬에 있는 동안만 오른다)
    public float hiBonus;    // 뒤집기 보너스
    // 접시에 올린 순간 확정되는 판정
    public bool ok;
    public string fail;
    public float coef;
}

public class Cook
{
    public CookItem[] items;
    public MethodDef m;
    public float t;          // 경과 시간
    public int fire = 1;     // 0 약, 1 중, 2 강 (팬 전체 공유)
    public bool finished;

    public bool flipped, flipAvailable;
    public bool stirring;
    public bool bubble;
    public float lidProgress;

    float bubbleTimer;

    public Cook(Ingredient[] ings, MethodDef m)
    {
        this.m = m;
        items = ings.Select(i => new CookItem { ing = i }).ToArray();
    }

    public float Lo { get { return m.lo; } }
    public float Hi(CookItem it) { return m.hi + it.hiBonus; }

    public void SetFire(int level) { fire = Mathf.Clamp(level, 0, 2); }

    // 트레이의 재료를 팬에 넣는다.
    public void Put(int i)
    {
        if (finished || i < 0 || i >= items.Length) return;
        if (items[i].spot == Spot.Tray) items[i].spot = Spot.Pan;
    }

    // 팬의 재료를 접시로 꺼낸다. 이때 판정이 확정된다.
    public void TakeOut(int i)
    {
        if (i < 0 || i >= items.Length) return;
        var it = items[i];
        if (it.spot != Spot.Pan) return;
        it.spot = Spot.Plate;
        it.ok = Judge(it, out it.coef, out it.fail);
    }

    public void TakeOutAll()
    {
        for (int i = 0; i < items.Length; i++) TakeOut(i);
    }

    // 팬의 재료 전부에 뒤집기 보너스를 준다.
    public void Flip()
    {
        if (!flipAvailable) return;
        flipped = true;
        flipAvailable = false;
        foreach (var it in items) if (it.spot == Spot.Pan) it.hiBonus = Balance.FlipBonus;
    }

    public bool AnyInPan { get { return items.Any(it => it.spot == Spot.Pan); } }
    public bool AnyInTray { get { return items.Any(it => it.spot == Spot.Tray); } }

    public void Tick(float dt, bool stir, bool lidHeld)
    {
        if (finished) return;
        t += dt;
        stirring = m.id == Method.Stirfry && stir;

        if (m.id == Method.Boil)
        {
            if (!bubble)
            {
                bubbleTimer += dt;
                lidProgress = 0f;
                if (bubbleTimer >= Balance.BubbleInterval) bubble = true;
            }
            else if (lidHeld)
            {
                lidProgress += dt;
                if (lidProgress >= Balance.LidHoldTime) { bubble = false; bubbleTimer = 0f; lidProgress = 0f; }
            }
            else lidProgress = 0f;
        }

        float common = m.speed * Balance.Fire[fire];
        if (stirring) common *= Balance.StirSlow;
        if (bubble) common *= Balance.BubbleFast;

        bool windowOpen = false;
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (it.spot != Spot.Pan) continue;
            it.d = Mathf.Min(100f, it.d + common * it.ing.speedMod * dt);
            if (m.id == Method.Grill && it.d >= 30f && it.d <= 40f) windowOpen = true;
            if (it.d >= Balance.BurnLimit) TakeOut(i); // 탄 재료는 그 재료만 접시로 넘어간다
        }
        flipAvailable = windowOpen && !flipped;

        if (t >= Balance.MaxCookTime) { TakeOutAll(); finished = true; }
        else if (!AnyInPan && !AnyInTray) finished = true;
    }

    // 꺼낸 시점의 판정. 실패면 false.
    bool Judge(CookItem it, out float coef, out string fail)
    {
        coef = 0f; fail = null;
        float d = it.d;
        if (d < Balance.RawLimit) { fail = "날것"; return false; }
        if (d >= Balance.BurnLimit) { fail = "탄 것"; return false; }
        float hi = Hi(it);
        if (d < Lo) coef = Mathf.Lerp(0.5f, 1f, (d - Balance.RawLimit) / (Lo - Balance.RawLimit));
        else if (d <= hi) coef = 1f;
        else coef = Mathf.Lerp(1f, 0.5f, (d - hi) / (99f - hi));
        return true;
    }

    // ---- 결과 (finished 이후에 읽는다)
    // 접시에 올라간 재료 = 소비되는 재료. 트레이에 남은 재료는 쓰지 않은 것이다.
    public Ingredient[] Used { get { return items.Where(it => it.spot == Spot.Plate).Select(it => it.ing).ToArray(); } }
    public Ingredient[] GoodIngredients { get { return items.Where(it => it.spot == Spot.Plate && it.ok).Select(it => it.ing).ToArray(); } }
    public Ingredient[] LostIngredients { get { return items.Where(it => it.spot == Spot.Plate && !it.ok).Select(it => it.ing).ToArray(); } }
    public string FailText { get { var f = items.FirstOrDefault(it => it.spot == Spot.Plate && !it.ok); return f == null ? null : f.fail; } }

    // 접시에 올린 재료의 평균 만족도. 망친 재료는 0점으로 평균에 들어간다.
    public int Satisfaction()
    {
        var plated = items.Where(it => it.spot == Spot.Plate).ToArray();
        if (plated.Length == 0) return 0;
        float sum = 0f;
        foreach (var it in plated)
        {
            if (!it.ok) continue;
            float compat = it.ing.fits.Contains(m.id) ? 1f : Balance.NonFit;
            sum += it.ing.baseScore * compat * it.coef;
        }
        return Mathf.RoundToInt(sum / plated.Length);
    }

    // 익힘 5단계. 색이 서서히 변하지 않고 단계가 바뀔 때 한 번에 바뀐다. 기준은 꺼내기 판정(Judge)과 같다.
    public Doneness Stage(CookItem it)
    {
        float d = it.d;
        if (d < Balance.RawLimit) return Doneness.Raw;        // 날것 (꺼내면 실패)
        if (d >= Balance.BurnLimit) return Doneness.Burnt;    // 탄 것 (꺼내면 실패)
        if (d < Lo) return Doneness.Under;                    // 적게 익힘
        if (d <= Hi(it)) return Doneness.Good;                // 알맞음
        return Doneness.Over;                                 // 많이 익힘
    }

    // 그림이 없을 때 쓰는 단계별 단색
    public Color Tone(CookItem it)
    {
        var i = it.ing;
        switch (Stage(it))
        {
            case Doneness.Raw: return i.raw;
            case Doneness.Under: return Color.Lerp(i.raw, i.cooked, 0.5f);
            case Doneness.Good: return i.cooked;
            case Doneness.Over: return Color.Lerp(i.cooked, i.burnt, 0.5f);
            default: return i.burnt;
        }
    }
}
