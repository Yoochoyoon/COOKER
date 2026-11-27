using UnityEngine;

// 불 바늘 게이지 (docs/26). 바늘은 저절로 오르내리고, 플레이어는 불 밸브(약·중·강)로 바늘을 적정 구간에 머물게 한다.
// 구간 밖에 오래 있으면 경고가 뜨고, 구간 밖에서는 익는 속도가 느려진다.
// 수치는 아직 BalanceData에 없으므로 아래 임시값을 쓴다. 확정되면 Balance로 옮긴다.
public partial class Cook
{
    // 임시값 (docs/26 제안). TODO: BalanceData로 옮기고 Balance에서 읽는다.
    const float GaugeDriftAmp = 0.22f;      // 바늘이 저절로 흔들리는 폭 (0~1 눈금 기준)
    const float GaugeValveStep = 0.20f;     // 밸브 한 단계가 바늘을 옮기는 만큼 (약불 -, 강불 +)
    const float GaugeOutCoef = 0.6f;        // 구간 밖일 때 익는 속도 보정
    const float GaugeWarnAfter = 0.6f;      // 구간 밖이 이 시간(초) 이상 계속되면 경고 (T1)
    const float GaugeDangerAfter = 2.0f;    // 이 시간 이상이면 위험 (T2)
    const float GaugeValveSpeed = 1.0f;     // 밸브를 돌린 뒤 바늘이 새 자리로 옮겨 가는 속도 (눈금/초). 순간이동하면 바늘이 아니라 숫자처럼 느껴진다
    const float GaugeWobbleRatio = 0.25f;   // 큰 흔들림 위에 얹는 잔물결 비율 (규칙적인 사인파만 있으면 금방 외워져서 밸브를 안 봐도 된다)
    const float GaugeWobblePeriodRatio = 0.37f;

    float gaugeOutTime;
    float gaugeValveOffset;                 // 지금 바늘에 더해지는 밸브 몫. (fire - 1) * GaugeValveStep을 향해 따라간다

    // 방식별 바늘이 한 바퀴 도는 시간(초)과 적정 구간의 절반 폭 (굽기는 좁고 끓이기는 넓다)
    float GaugePeriod { get { return m.id == Method.Stirfry ? 6f : m.id == Method.Boil ? 8f : 7f; } }
    public float GaugeZoneHalf { get { return m.id == Method.Stirfry ? 0.10f : m.id == Method.Boil ? 0.14f : 0.08f; } }
    public float GaugeZoneLo { get { return 0.5f - GaugeZoneHalf; } }
    public float GaugeZoneHi { get { return 0.5f + GaugeZoneHalf; } }

    // 바늘 위치 0~1 (밸브 중불이면 가운데가 기준)
    public float GaugeValue
    {
        get
        {
            float main = Mathf.Sin(Mathf.PI * 2f * t / GaugePeriod);
            float wobble = Mathf.Sin(Mathf.PI * 2f * t / (GaugePeriod * GaugeWobblePeriodRatio) + 1f);
            float drift = GaugeDriftAmp * ((1f - GaugeWobbleRatio) * main + GaugeWobbleRatio * wobble); // 폭은 GaugeDriftAmp를 넘지 않는다
            return Mathf.Clamp01(0.5f + drift + gaugeValveOffset);
        }
    }

    public bool GaugeInZone { get { float v = GaugeValue; return v >= GaugeZoneLo && v <= GaugeZoneHi; } }
    public float GaugeOutTime { get { return gaugeOutTime; } }
    // -1: 바늘이 구간 위에 있다(내려야 한다), +1: 구간 아래에 있다(올려야 한다), 0: 구간 안
    public int GaugeDir { get { float v = GaugeValue; return v > GaugeZoneHi ? -1 : v < GaugeZoneLo ? 1 : 0; } }
    // 0 정상, 1 경고, 2 위험
    public int GaugeState { get { return gaugeOutTime >= GaugeDangerAfter ? 2 : gaugeOutTime >= GaugeWarnAfter ? 1 : 0; } }

    // 익는 속도에 곱하는 보정 (구간 안 1, 밖 GaugeOutCoef)
    public float GaugeSpeedCoef { get { return GaugeInZone ? 1f : GaugeOutCoef; } }

    // 매 프레임: 바늘의 밸브 몫을 따라가게 하고, 팬에 재료가 있을 때만 구간 밖 시간을 센다
    void UpdateGauge(float dt)
    {
        gaugeValveOffset = Mathf.MoveTowards(gaugeValveOffset, (fire - 1) * GaugeValveStep, GaugeValveSpeed * dt);
        if (!AnyInPan || GaugeInZone) { gaugeOutTime = 0f; return; }
        gaugeOutTime += dt;
    }
}
