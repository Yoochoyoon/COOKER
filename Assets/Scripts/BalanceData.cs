using UnityEngine;

// 밸런스 수치를 담는 에셋. 인스펙터에서 고친다. 파일: Assets/Resources/Data/Balance.asset (CLAUDE.md: 수치는 여기서만 고친다)
[CreateAssetMenu(menuName = "COOKER/Balance", fileName = "Balance")]
public class BalanceData : ScriptableObject
{
    public int budget;
    public int minUnits;
    public int days;
    public float maxCookTime;
    public float nonFit;
    public float mixBonus;
    public float rawLimit;
    public float burnLimit;
    public float bubbleInterval;
    public float lidHoldTime;
    public float stirSlow;
    public float bubbleFast;
    public float flipPenalty;
    public float stirGrace;
    public float noStirCoef;
    public float overflowGrace;
    public float boilOverCoef;
    public float oilGrace;
    public float soggyCoef;
    public float easeBand;
    public float easeSpeed;
    public float priceGrowth;
    public int budgetBase;
    public int budgetMin;
    public int budgetMax;
    public int budgetPerSat;
    public int lifelines;
    public int ramenSat;
    public float gradeS;
    public float gradeA;
    public float gradeB;
    public float[] fire = { 0.6f, 1.0f, 1.5f };
    public string[] fireName = { "약불", "중불", "강불" };
}
