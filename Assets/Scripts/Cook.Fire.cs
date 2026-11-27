using UnityEngine;

// 불 바늘 게이지 (docs/26). 플레이어는 불 밸브(약·중·강)로 바늘을 적정 구간에 머물게 한다.
// 수치는 Balance.asset(BalanceData의 "불 바늘" 항목)에서 읽는다. 바꾸면 docs/09에 이유를 남긴다.
//
// 바늘 움직임: 불길이 저절로 "세졌다(위) → 약해졌다(아래)"를 오가고, 한쪽에 한동안 머문다.
// 머무는 자리는 적정 구간 밖이라, 밸브를 반대로 돌려 맞추지 않으면 바늘은 저절로 구간에 들어오지 않는다.
// (예전에는 사인파로 구간을 가로질러 다녀서, 가만히 둬도 자주 구간 안에 들어왔다)
//   - 위에 머물 때는 약불, 아래에 머물 때는 강불로 맞추면 구간 안에 들어온다 (머무는 높이 = 밸브 한 단계)
//   - 밸브를 돌리면 바늘이 순간이동하지 않고 옮겨 간다 (순간이동하면 바늘이 아니라 숫자처럼 느껴진다)
//   - 잔물결을 얹어 바늘이 살아 있게 한다 (구간 안에 맞춰도 구간을 벗어나지 않을 만큼만)
// 구간 밖에 GaugeDangerAfter초 넘게 있으면 그때부터 맛 점수가 1초마다 깎인다 (GaugePenalty).
public partial class Cook
{
    const float GaugeMinPeriod = 0.5f;      // 에셋에 0이 들어가도 바늘 계산이 0으로 나누지 않게

    float gaugeOutTime;
    float gaugeDangerTime;                  // 위험(구간 밖 T2 이상) 상태로 보낸 시간. 점수 감점에 쓴다
    float gaugeValveOffset;                 // 지금 바늘에 더해지는 밸브 몫. (fire - 1) * 밸브 한 단계를 향해 따라간다
    float gaugePhase = -1f;                 // 판마다 처음 흔들리는 방향이 달라지게 (-1이면 아직 안 정함)

    // 방식별 바늘이 한 번 왕복(위에 머묾 + 아래에 머묾)하는 시간(초)과 적정 구간의 절반 폭
    float GaugePeriod { get { return Mathf.Max(GaugeMinPeriod, Balance.GaugePeriod(m.id)); } }
    public float GaugeZoneHalf { get { return Balance.GaugeZoneHalf(m.id); } }
    public float GaugeZoneLo { get { return 0.5f - GaugeZoneHalf; } }
    public float GaugeZoneHi { get { return 0.5f + GaugeZoneHalf; } }

    // 밸브를 중불에 두었을 때 바늘이 저절로 가는 자리 (0.5 = 구간 한가운데)
    float GaugeDrift
    {
        get
        {
            float phase = Mathf.PI * 2f * t / GaugePeriod + (gaugePhase < 0f ? 0f : gaugePhase);
            // tanh(sharp·sin): 위·아래에 오래 머물고 그 사이는 빨리 지나간다 (사인파는 가운데를 천천히 지나서 구간 안에 오래 있었다)
            float sharp = Mathf.Max(0.1f, Balance.GaugeHoldSharpness);
            float hold = Tanh(sharp * Mathf.Sin(phase)) / Tanh(sharp);
            float wobble = Mathf.Sin(Mathf.PI * 2f * t / Mathf.Max(GaugeMinPeriod, GaugePeriod * Balance.GaugeWobblePeriodRatio) + 1f);
            return Balance.GaugeDriftAmp * hold + Balance.GaugeWobbleAmp * wobble;
        }
    }

    static float Tanh(float x) { float e = Mathf.Exp(2f * x); return (e - 1f) / (e + 1f); }

    // 바늘 위치 0~1
    public float GaugeValue { get { return Mathf.Clamp01(0.5f + GaugeDrift + gaugeValveOffset); } }

    public bool GaugeInZone { get { float v = GaugeValue; return v >= GaugeZoneLo && v <= GaugeZoneHi; } }
    public float GaugeOutTime { get { return gaugeOutTime; } }
    // -1: 바늘이 구간 위에 있다(불을 줄여야 한다), +1: 구간 아래에 있다(불을 키워야 한다), 0: 구간 안
    public int GaugeDir { get { float v = GaugeValue; return v > GaugeZoneHi ? -1 : v < GaugeZoneLo ? 1 : 0; } }
    // 0 정상, 1 경고, 2 위험
    public int GaugeState { get { return gaugeOutTime >= Balance.GaugeDangerAfter ? 2 : gaugeOutTime >= Balance.GaugeWarnAfter ? 1 : 0; } }

    // 익는 속도에 곱하는 보정 (구간 안 1, 밖 Balance.GaugeOutCoef)
    public float GaugeSpeedCoef { get { return GaugeInZone ? 1f : Balance.GaugeOutCoef; } }

    // 맛 점수에서 빼는 감점: 위험 상태(구간 밖 2초 이상)로 보낸 1초마다, 상한까지
    public float GaugePenalty { get { return Mathf.Min(gaugeDangerTime * Balance.GaugePenaltyPerSecond, Balance.GaugePenaltyCap); } }

    // 매 프레임: 바늘의 밸브 몫을 따라가게 하고, 팬에 재료가 있을 때만 구간 밖 시간과 위험 시간을 센다
    void UpdateGauge(float dt)
    {
        if (gaugePhase < 0f) gaugePhase = Random.value < 0.5f ? 0f : Mathf.PI; // 처음에 위로 갈지 아래로 갈지
        gaugeValveOffset = Mathf.MoveTowards(gaugeValveOffset, (fire - 1) * Balance.GaugeValveStep, Balance.GaugeValveSpeed * dt);
        if (!AnyInPan || GaugeInZone) { gaugeOutTime = 0f; return; }
        gaugeOutTime += dt;
        if (gaugeOutTime >= Balance.GaugeDangerAfter) gaugeDangerTime += dt;
    }
}
