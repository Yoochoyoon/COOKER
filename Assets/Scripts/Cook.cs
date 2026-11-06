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
    public float hiBonus;    // 뒤집기 전이면 음수(페널티), 뒤집으면 0으로 풀린다
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

    public bool flipped, flipAvailable, flipMissed;
    public bool stirring, stirLapsed; // stirLapsed: 안 젓고 너무 오래 버텨서 눌어붙은 적이 있다(계속 저어야 한다)
    public bool bubble, hadOverflow;
    public bool hadSoggy;    // 튀기기: 약불로 오래 둬서 기름이 식어 눅눅해졌다
    public float lidProgress;

    float bubbleTimer, bubbleActiveTime, stirIdleTime, oilLowTime;
    float lo, hi, speed; // 이번 주 난이도(ease)를 반영한 적정 구간과 기본 속도

    // ease 1 = 가장 쉬움(1주차), 0 = 원래 값. 구간을 넓히고 속도를 늦춘다.
    public Cook(Ingredient[] ings, MethodDef m, float ease = 0f)
    {
        this.m = m;
        // 굽기는 뒤집기 전까지 적정 구간 윗선이 좁다(한쪽만 익은 상태). 뒤집으면 0으로 풀린다.
        float startHiBonus = m.id == Method.Grill ? -Balance.FlipPenalty : 0f;
        items = ings.Select(i => new CookItem { ing = i, hiBonus = startHiBonus }).ToArray();
        lo = m.lo - Balance.EaseBand * ease;
        hi = m.hi + Balance.EaseBand * ease;
        speed = m.speed * (1f - Balance.EaseSpeed * ease);
    }

    public float Lo { get { return lo; } }
    public float StirIdle { get { return stirIdleTime; } } // 볶기: 안 저은 채로 흐른 시간 (화면 경고용)
    public float Hi(CookItem it) { return hi + it.hiBonus; }

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

    // 팬의 재료 전부에서 뒤집기 페널티를 없앤다(적정 구간이 원래 너비로 돌아온다).
    public void Flip()
    {
        if (!flipAvailable) return;
        flipped = true;
        flipAvailable = false;
        foreach (var it in items) if (it.spot == Spot.Pan) it.hiBonus = 0f;
    }

    public bool AnyInPan { get { return items.Any(it => it.spot == Spot.Pan); } }
    public bool AnyInTray { get { return items.Any(it => it.spot == Spot.Tray); } }

    public void Tick(float dt, bool stir, bool lidHeld)
    {
        if (finished) return;
        t += dt;
        stirring = m.id == Method.Stirfry && stir;
        if (m.id == Method.Stirfry && AnyInPan)
        {
            if (stirring) stirIdleTime = 0f;
            else
            {
                stirIdleTime += dt;
                if (stirIdleTime >= Balance.StirGrace) stirLapsed = true; // 쉬는 동안 눌어붙었다
            }
        }

        if (m.id == Method.Boil)
        {
            if (!bubble)
            {
                bubbleTimer += dt;
                lidProgress = 0f;
                bubbleActiveTime = 0f;
                if (bubbleTimer >= Balance.BubbleInterval) bubble = true;
            }
            else if (lidHeld)
            {
                lidProgress += dt;
                if (lidProgress >= Balance.LidHoldTime) { bubble = false; bubbleTimer = 0f; lidProgress = 0f; bubbleActiveTime = 0f; }
            }
            else
            {
                lidProgress = 0f;
                bubbleActiveTime += dt;
                if (bubbleActiveTime >= Balance.OverflowGrace) hadOverflow = true; // 거품을 반응 안 하고 너무 오래 둬서 끓어넘쳤다
            }
        }

        float common = speed * Balance.Fire[fire];
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
        if (m.id == Method.Grill)
        {
            bool wasAvailable = flipAvailable;
            flipAvailable = windowOpen && !flipped;
            if (wasAvailable && !flipAvailable && !flipped) flipMissed = true; // 창이 닫힐 때까지 안 눌렀다
        }
        else if (m.id == Method.Fry && AnyInPan && fire == 0)
        {
            // 기름 온도 유지: 팬에 재료가 있는 채로 약불에 오래 두면 기름이 식어 눅눅해진다(누적).
            oilLowTime += dt;
            if (oilLowTime >= Balance.OilGrace) hadSoggy = true;
        }

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
        if (m.id == Method.Stirfry && stirLapsed) coef *= Balance.NoStirCoef; // 쉬지 않고 계속 저어야 한다. 한 번이라도 너무 오래 쉬면 눌어붙는다
        if (m.id == Method.Boil && hadOverflow) coef *= Balance.BoilOverCoef;   // 끓어넘치게 두면 양·맛이 깎인다
        if (m.id == Method.Fry && hadSoggy) coef *= Balance.SoggyCoef;          // 기름이 식으면 눅눅해져 맛이 깎인다
        return true;
    }

    // ---- 결과 (finished 이후에 읽는다)
    // 접시에 올라간 재료 = 소비되는 재료. 트레이에 남은 재료는 쓰지 않은 것이다.
    public Ingredient[] Used { get { return items.Where(it => it.spot == Spot.Plate).Select(it => it.ing).ToArray(); } }
    public Ingredient[] GoodIngredients { get { return items.Where(it => it.spot == Spot.Plate && it.ok).Select(it => it.ing).ToArray(); } }
    public Ingredient[] LostIngredients { get { return items.Where(it => it.spot == Spot.Plate && !it.ok).Select(it => it.ing).ToArray(); } }
    // 위 배열과 순서가 같은 익힘 단계 (요리 그림에 쓴다)
    public Doneness[] UsedStages { get { return items.Where(it => it.spot == Spot.Plate).Select(Stage).ToArray(); } }
    public Doneness[] GoodStages { get { return items.Where(it => it.spot == Spot.Plate && it.ok).Select(Stage).ToArray(); } }
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
        float avg = sum / plated.Length;
        // 재료 2개를 섞어 모두 성공시키면 보너스
        if (plated.Length >= 2 && plated.All(it => it.ok)) avg *= 1f + Balance.MixBonus;
        return Mathf.Min(100, Mathf.RoundToInt(avg));
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
    public Color Tone(CookItem it) { return ToneOf(it.ing, Stage(it)); }

    public static Color ToneOf(Ingredient i, Doneness stage)
    {
        switch (stage)
        {
            case Doneness.Raw: return i.raw;
            case Doneness.Under: return Color.Lerp(i.raw, i.cooked, 0.5f);
            case Doneness.Good: return i.cooked;
            case Doneness.Over: return Color.Lerp(i.cooked, i.burnt, 0.5f);
            default: return i.burnt;
        }
    }
}
