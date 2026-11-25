using System.Linq;

// 전체 익힘 (docs/27). 재료마다 따로 꺼내지 않고, 조리 완료 때 팬 전체의 익힘 값 하나로 한 번에 판정한다.
// 재료별 d는 계산에만 쓰고(재료마다 익는 속도가 다르다), 판정과 단계는 전체 값 하나를 쓴다.
public partial class Cook
{
    float wholeD = -1f; // 조리 완료 때 확정되는 전체 익힘 (-1이면 아직 확정 전)

    // 지금 팬에 있는 재료들의 평균 익힘 (docs/27 제안: 평균). 팬이 비면 0.
    public float OverallD
    {
        get
        {
            if (wholeD >= 0f) return wholeD;
            var pan = items.Where(it => it.spot == Spot.Pan).ToArray();
            return pan.Length == 0 ? 0f : pan.Average(it => it.d);
        }
    }

    // 전체 값이 적정 구간 안인지 (알맞게 익었는지). 화면 안내와 접시 빛에 쓴다.
    public bool WholeInRange
    {
        get
        {
            var first = items.FirstOrDefault(it => it.spot == Spot.Pan);
            if (first == null) return false;
            float d = OverallD;
            return d >= Lo && d <= Hi(first);
        }
    }

    // 조리 완료: 전체 익힘을 확정하고 팬의 재료를 한꺼번에 접시로 옮긴다. 판정은 확정한 전체 값 하나로 한다.
    // 팬이 비어 있으면 아무 일도 하지 않는다 (실수로 끝내지 않게).
    public void Complete()
    {
        if (finished || !AnyInPan) return;
        wholeD = OverallD;
        TakeOutAll();
        finished = true; // 트레이에 남은 재료는 쓰지 않은 것이다
    }
}
