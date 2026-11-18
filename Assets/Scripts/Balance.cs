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

    // 난이도: 1주차는 쉽고 4주차에 원래 값이 된다
    public static float Ease(int week) { return Mathf.Clamp01(1f - (week - 1) / 3f); }
}
