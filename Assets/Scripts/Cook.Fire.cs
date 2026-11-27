using UnityEngine;

// 불 바늘 게이지 (docs/26). 바늘은 저절로 오르내리고, 플레이어는 불 밸브(약·중·강)로 바늘을 적정 구간에 머물게 한다.
// 구간 밖에 오래 있으면 경고가 뜨고, 구간 밖에서는 익는 속도가 느려진다.
// 수치는 Balance.asset(BalanceData의 "불 바늘" 항목)에서 읽는다. 바꾸면 docs/09에 이유를 남긴다.
// 밸브를 돌리면 바늘이 순간이동하지 않고 옮겨 간다(순간이동하면 바늘이 아니라 숫자처럼 느껴진다).
// 큰 흔들림에 잔물결을 섞는다(규칙적인 사인파 하나면 금방 외워져서 밸브를 안 봐도 된다).
public partial class Cook
{
    const float GaugeMinPeriod = 0.5f;      // 에셋에 0이 들어가도 바늘 계산이 0으로 나누지 않게

    float gaugeOutTime;
    float gaugeValveOffset;                 // 지금 바늘에 더해지는 밸브 몫. (fire - 1) * 밸브 한 단계를 향해 따라간다

    // 방식별 바늘이 한 번 왕복하는 시간(초)과 적정 구간의 절반 폭 (굽기는 좁고 끓이기는 넓다)
    float GaugePeriod { get { return Mathf.Max(GaugeMinPeriod, Balance.GaugePeriod(m.id)); } }
    public float GaugeZoneHalf { get { return Balance.GaugeZoneHalf(m.id); } }
    public float GaugeZoneLo { get { return 0.5f - GaugeZoneHalf; } }
    public float GaugeZoneHi { get { return 0.5f + GaugeZoneHalf; } }

    // 바늘 위치 0~1 (밸브 중불이면 가운데가 기준)
    public float GaugeValue
    {
        get
        {
            float main = Mathf.Sin(Mathf.PI * 2f * t / GaugePeriod);
            float wobble = Mathf.Sin(Mathf.PI * 2f * t / Mathf.Max(GaugeMinPeriod, GaugePeriod * Balance.GaugeWobblePeriodRatio) + 1f);
            float wr = Mathf.Clamp01(Balance.GaugeWobbleRatio);
            float drift = Balance.GaugeDriftAmp * ((1f - wr) * main + wr * wobble); // 폭은 흔들림 폭을 넘지 않는다
            return Mathf.Clamp01(0.5f + drift + gaugeValveOffset);
        }
    }

    public bool GaugeInZone { get { float v = GaugeValue; return v >= GaugeZoneLo && v <= GaugeZoneHi; } }
    public float GaugeOutTime { get { return gaugeOutTime; } }
    // -1: 바늘이 구간 위에 있다(내려야 한다), +1: 구간 아래에 있다(올려야 한다), 0: 구간 안
    public int GaugeDir { get { float v = GaugeValue; return v > GaugeZoneHi ? -1 : v < GaugeZoneLo ? 1 : 0; } }
    // 0 정상, 1 경고, 2 위험
    public int GaugeState { get { return gaugeOutTime >= Balance.GaugeDangerAfter ? 2 : gaugeOutTime >= Balance.GaugeWarnAfter ? 1 : 0; } }

    // 익는 속도에 곱하는 보정 (구간 안 1, 밖 Balance.GaugeOutCoef)
    public float GaugeSpeedCoef { get { return GaugeInZone ? 1f : Balance.GaugeOutCoef; } }

    // 매 프레임: 바늘의 밸브 몫을 따라가게 하고, 팬에 재료가 있을 때만 구간 밖 시간을 센다
    void UpdateGauge(float dt)
    {
        gaugeValveOffset = Mathf.MoveTowards(gaugeValveOffset, (fire - 1) * Balance.GaugeValveStep, Balance.GaugeValveSpeed * dt);
        if (!AnyInPan || GaugeInZone) { gaugeOutTime = 0f; return; }
        gaugeOutTime += dt;
    }
}
