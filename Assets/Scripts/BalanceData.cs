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

    // 방식별 값은 Method 순서(굽기, 볶기, 끓이기, 튀기기)로 넣는다.
    // 아래 초기값은 에셋에 이 항목이 아직 없을 때 쓰인다 (팀원 에셋이 옛 버전이어도 0이 되지 않게).

    [Header("불 바늘 (docs/26)")]
    public float gaugeDriftAmp = 0.22f;          // 바늘이 저절로 흔들리는 폭 (0~1 눈금)
    public float gaugeValveStep = 0.20f;         // 밸브 한 단계가 바늘 중심을 옮기는 만큼
    public float gaugeValveSpeed = 1.0f;         // 밸브를 돌린 뒤 바늘이 옮겨 가는 속도 (눈금/초)
    public float gaugeWobbleRatio = 0.25f;       // 큰 흔들림에 얹는 잔물결 비율
    public float gaugeWobblePeriodRatio = 0.37f; // 잔물결 주기 = 큰 흔들림 주기 x 이 값
    public float gaugeOutCoef = 0.6f;            // 구간 밖일 때 익는 속도 보정
    public float gaugeWarnAfter = 0.6f;          // 구간 밖 이 시간(초)부터 경고 (T1)
    public float gaugeDangerAfter = 2.0f;        // 구간 밖 이 시간(초)부터 위험 (T2)
    public float[] gaugePeriod = { 7f, 6f, 8f, 7f };          // 큰 흔들림 한 번 왕복 시간(초)
    public float[] gaugeZoneHalf = { 0.08f, 0.10f, 0.14f, 0.08f }; // 적정 구간 절반 폭

    [Header("조리 시간 감점 (docs/30)")]
    public float[] cookTimeLimit = { 40f, 45f, 50f, 40f };    // 제한 시간(초)
    public float timePenaltyPerSecond = 0.5f;    // 초과 1초당 맛 점수 감점
    public float timePenaltyCap = 30f;           // 감점 상한
    public float cookOverGrace = 25f;            // 제한 시간 뒤 강제로 끝나기까지 더 할 수 있는 시간(초)
}
