// 조리 시간 감점 (docs/30). 제한 시간을 넘기면 1초마다 맛 점수를 깎는다.
// 이 파일은 계산만 맡는다. 타이머 표시와 강조는 유초윤 님이 맡는다.
// 수치는 아직 BalanceData에 없으므로 아래 임시값을 쓴다. 확정되면 Balance로 옮긴다.
public partial class Cook
{
    // 임시값 (docs/30 제안). TODO: BalanceData로 옮기고 Balance에서 읽는다.
    const float PenaltyPerSecond = 0.5f;   // 초과 1초당 맛 점수 감점
    const float PenaltyCap = 30f;          // 감점 상한

    // 방식별 제한 시간 (초, docs/30 제안, 미확정)
    public float TimeLimit
    {
        get
        {
            switch (m == null ? Method.Grill : m.id)
            {
                case Method.Grill: return 40f;
                case Method.Stirfry: return 35f;
                case Method.Boil: return 50f;
                default: return 40f;
            }
        }
    }

    // 제한 시간을 넘긴 초
    public float OverTime { get { return t > TimeLimit ? t - TimeLimit : 0f; } }

    // 지금까지 쌓인 감점 (상한 적용). 간 고르기 시간은 포함하지 않는다 (조리 완료 전까지만 센다).
    public float TimePenalty
    {
        get { return System.Math.Min(OverTime * PenaltyPerSecond, PenaltyCap); }
    }
}
