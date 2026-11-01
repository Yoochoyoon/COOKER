using System.Linq;
using UnityEngine;

// 조리 한 번의 상태. 화면과 무관한 순수 로직이라 수치 검증이 쉽다.
public enum Doneness { Raw, Under, Good, Over, Burnt }

public class Cook
{
    public Ingredient[] ings;
    public MethodDef m;
    public float d;          // 익힘 0~100
    public float t;          // 경과 시간
    public int fire = 1;     // 0 약, 1 중, 2 강
    public bool finished;

    public bool flipped, flipAvailable;
    public bool stirring;
    public bool bubble;
    public float lidProgress;

    float bubbleTimer, avgMod;
    float hiBonus;

    public Cook(Ingredient[] ings, MethodDef m)
    {
        this.ings = ings;
        this.m = m;
        avgMod = (float)ings.Average(i => i.speedMod);
    }

    public float Lo { get { return m.lo; } }
    public float Hi { get { return m.hi + hiBonus; } }

    public void SetFire(int level) { fire = Mathf.Clamp(level, 0, 2); }
    public void TakeOut() { finished = true; }

    public void Flip()
    {
        if (!flipAvailable) return;
        flipped = true;
        flipAvailable = false;
        hiBonus = Balance.FlipBonus;
    }

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

        float rate = m.speed * avgMod * Balance.Fire[fire];
        if (stirring) rate *= Balance.StirSlow;
        if (bubble) rate *= Balance.BubbleFast;
        d = Mathf.Min(100f, d + rate * dt);

        flipAvailable = m.id == Method.Grill && !flipped && d >= 30f && d <= 40f;

        if (d >= Balance.BurnLimit || t >= Balance.MaxCookTime) finished = true;
    }

    // 꺼낸 시점의 판정. 실패면 false.
    public bool Evaluate(out float coef, out string fail)
    {
        coef = 0f; fail = null;
        if (d < Balance.RawLimit) { fail = "날것"; return false; }
        if (d >= Balance.BurnLimit) { fail = "탄 것"; return false; }
        if (d < Lo) coef = Mathf.Lerp(0.5f, 1f, (d - Balance.RawLimit) / (Lo - Balance.RawLimit));
        else if (d <= Hi) coef = 1f;
        else coef = Mathf.Lerp(1f, 0.5f, (d - Hi) / (99f - Hi));
        return true;
    }

    public int Satisfaction(float coef)
    {
        float baseAvg = (float)ings.Average(i => (float)i.baseScore);
        float compat = (float)ings.Average(i => i.fits.Contains(m.id) ? 1f : Balance.NonFit);
        return Mathf.RoundToInt(baseAvg * compat * coef);
    }

    // 익힘 5단계. 색이 서서히 변하지 않고 단계가 바뀔 때 한 번에 바뀐다. 기준은 꺼내기 판정(Evaluate)과 같다.
    public Doneness Stage
    {
        get
        {
            if (d < Balance.RawLimit) return Doneness.Raw;        // 날것 (꺼내면 실패)
            if (d >= Balance.BurnLimit) return Doneness.Burnt;    // 탄 것 (꺼내면 실패)
            if (d < Lo) return Doneness.Under;                    // 적게 익힘
            if (d <= Hi) return Doneness.Good;                    // 알맞음
            return Doneness.Over;                                 // 많이 익힘
        }
    }

    // 그림이 없을 때 쓰는 단계별 단색
    public Color Tone(Ingredient i)
    {
        switch (Stage)
        {
            case Doneness.Raw: return i.raw;
            case Doneness.Under: return Color.Lerp(i.raw, i.cooked, 0.5f);
            case Doneness.Good: return i.cooked;
            case Doneness.Over: return Color.Lerp(i.cooked, i.burnt, 0.5f);
            default: return i.burnt;
        }
    }
}
