// 조리 시간 감점 (docs/30). 제한 시간을 넘기면 1초마다 맛 점수를 깎는다.
// 이 파일은 계산만 맡는다. 타이머 표시와 강조는 유초윤 님이 맡는다.
// 수치는 Balance.asset(BalanceData의 "조리 시간 감점" 항목)에서 읽는다. 바꾸면 docs/09에 이유를 남긴다.
public partial class Cook
{
    // 방식별 제한 시간 (초)
    public float TimeLimit { get { return Balance.CookTimeLimit(m == null ? Method.Grill : m.id); } }

    // 제한 시간이 지나도 조리는 바로 끝나지 않고 이만큼 더 할 수 있다. 이 시간이 지나면 강제로 끝난다 (그동안 감점이 쌓인다).
    public float HardLimit { get { return TimeLimit + Balance.CookOverGrace; } }

    // 제한 시간을 넘긴 초
    public float OverTime { get { return t > TimeLimit ? t - TimeLimit : 0f; } }

    // 지금까지 쌓인 감점 (상한 적용). 간 고르기 시간은 포함하지 않는다 (조리 완료 전까지만 센다).
    public float TimePenalty
    {
        get { return System.Math.Min(OverTime * Balance.TimePenaltyPerSecond, Balance.TimePenaltyCap); }
    }
}
