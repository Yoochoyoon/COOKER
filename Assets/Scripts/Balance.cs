using UnityEngine;

// 밸런스 수치 읽기 전용 창구. 값은 Assets/Resources/Data/Balance.asset에서 읽는다 (인스펙터에서 고친다).
// 코드는 이름으로만 읽는다. 이름을 바꾸면 여러 파일이 깨진다.
public static class Balance
{
    static BalanceData d;
    static BalanceData D
    {
        get
        {
            if (d == null)
            {
                d = Resources.Load<BalanceData>("Data/Balance");
                if (d == null) Debug.LogError("밸런스 에셋이 없다: Assets/Resources/Data/Balance.asset");
            }
            return d;
        }
    }

    public static int Budget => D.budget;
    public static int MinUnits => D.minUnits;
    public static int Days => D.days;
    // 이번 주에 한 요리에 넣을 수 있는 재료 수: 1주차 2개, 2주차부터 3개 (값은 Balance.asset의 maxIngFromWeek/maxIngCount)
    public static int MaxIngredients(int week)
    {
        var from = D.maxIngFromWeek; var cnt = D.maxIngCount;
        int n = 2;
        for (int i = 0; i < from.Length && i < cnt.Length; i++) if (week >= from[i]) n = cnt[i];
        return Mathf.Clamp(n, 1, 7);
    }
    public static float MaxCookTime => D.maxCookTime;
    public static float NonFit => D.nonFit;
    public static float MixBonus => D.mixBonus;
    public static float RawLimit => D.rawLimit;
    public static float BurnLimit => D.burnLimit;
    public static float BubbleInterval => D.bubbleInterval;
    public static float LidHoldTime => D.lidHoldTime;
    public static float StirSlow => D.stirSlow;
    public static float BubbleFast => D.bubbleFast;
    public static float FlipPenalty => D.flipPenalty;
    public static float StirGrace => D.stirGrace;
    public static float NoStirCoef => D.noStirCoef;
    public static float OverflowGrace => D.overflowGrace;
    public static float BoilOverCoef => D.boilOverCoef;
    public static float OilGrace => D.oilGrace;
    public static float SoggyCoef => D.soggyCoef;
    public static float EaseBand => D.easeBand;
    public static float EaseSpeed => D.easeSpeed;
    public static float PriceGrowth => D.priceGrowth;
    public static int BudgetBase => D.budgetBase;
    public static int BudgetMin => D.budgetMin;
    public static int BudgetMax => D.budgetMax;
    public static int BudgetPerSat => D.budgetPerSat;
    public static int Lifelines => D.lifelines;
    public static int RamenSat => D.ramenSat;
    public static float GradeS => D.gradeS;
    public static float GradeA => D.gradeA;
    public static float GradeB => D.gradeB;
    public static float[] Fire => D.fire;
    public static string[] FireName => D.fireName;

    // 불 바늘 (docs/26)
    public static float GaugeDriftAmp => D.gaugeDriftAmp;
    public static float GaugeHoldSharpness => D.gaugeHoldSharpness;
    public static float GaugeValveStep => D.gaugeValveStep;
    public static float GaugeValveSpeed => D.gaugeValveSpeed;
    public static float GaugeWobbleAmp => D.gaugeWobbleAmp;
    public static float GaugeWobblePeriodRatio => D.gaugeWobblePeriodRatio;
    public static float GaugeOutCoef => D.gaugeOutCoef;
    public static float GaugeWarnAfter => D.gaugeWarnAfter;
    public static float GaugeDangerAfter => D.gaugeDangerAfter;
    public static float GaugePenaltyPerSecond => D.gaugePenaltyPerSecond;
    public static float GaugePenaltyCap => D.gaugePenaltyCap;
    public static float GaugePeriod(Method m) { return ByMethod(D.gaugePeriod, m); }
    public static float GaugeZoneHalf(Method m) { return ByMethod(D.gaugeZoneHalf, m); }

    // 조리 시간 감점 (docs/30)
    public static float CookTimeLimit(Method m) { return ByMethod(D.cookTimeLimit, m); }
    public static float TimePenaltyPerSecond => D.timePenaltyPerSecond;
    public static float TimePenaltyCap => D.timePenaltyCap;
    public static float CookOverGrace => D.cookOverGrace;

    // 방식별 배열에서 값을 꺼낸다. 인스펙터에서 배열 길이를 줄여도 멈추지 않게 마지막 값을 쓴다
    static float ByMethod(float[] a, Method m)
    {
        if (a == null || a.Length == 0) { Debug.LogError("밸런스 에셋에 방식별 값이 비어 있다"); return 0f; }
        return a[Mathf.Clamp((int)m, 0, a.Length - 1)];
    }

    // 난이도: 1주차는 쉽고 4주차에 원래 값이 된다
    public static float Ease(int week) { return Mathf.Clamp01(1f - (week - 1) / 3f); }
}
