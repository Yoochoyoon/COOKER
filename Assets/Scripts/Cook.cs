using System.Linq;
using UnityEngine;

// 조리 한 번의 상태. 화면과 무관한 순수 로직이라 수치 검증이 쉽다.
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

    public Color Tone(Ingredient i)
    {
        if (d <= Lo) return Color.Lerp(i.raw, i.cooked, Mathf.Clamp01(d / Lo));
        if (d <= Hi) return i.cooked;
        return Color.Lerp(i.cooked, i.burnt, Mathf.Clamp01((d - Hi) / (100f - Hi)));
    }
}
