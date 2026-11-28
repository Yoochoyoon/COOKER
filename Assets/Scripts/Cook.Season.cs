using System.Collections.Generic;
using System.Linq;

// 간 맞추기 (docs/29). 조리가 끝난 뒤 간 선택지 네 개 중 하나를 고른다.
// 이 파일은 판정만 맡는다. 화면(패널, 어두운 막, 키 입력)은 유초윤 님이 맡는다.
// 수치는 아직 BalanceData에 없으므로 아래 임시값을 쓴다. 확정되면 Balance로 옮긴다.
public enum SeasonOption { Salt, Pepper, SoySauce, Sugar, Gochugaru, SoupSoy }

public partial class Cook
{
    // 임시값 (docs/29 제안). TODO: BalanceData로 옮기고 Balance에서 읽는다.
    const float SeasonGainCorrect = 15f;   // 정답일 때 맛 점수 가산
    const float SeasonLossWrong = 5f;      // 오답일 때 맛 점수 감산

    public SeasonOption[] SeasonChoices { get { return ChoicesOf(m == null ? Method.Grill : m.id); } }
    public SeasonOption? SeasonPicked { get; private set; }
    public bool SeasonDone { get { return SeasonPicked.HasValue; } }
    // 간 고르기 결과 보정 (고르기 전에는 0). 만족도 계산에 더한다.
    public float SeasonDelta { get; private set; }

    // 방식별 네 개 선택지 (docs/29 제안, 미확정)
    public static SeasonOption[] ChoicesOf(Method id)
    {
        switch (id)
        {
            case Method.Grill: return new[] { SeasonOption.Salt, SeasonOption.Pepper, SeasonOption.SoySauce, SeasonOption.Sugar };
            case Method.Stirfry: return new[] { SeasonOption.SoySauce, SeasonOption.Sugar, SeasonOption.Salt, SeasonOption.Gochugaru };
            case Method.Boil: return new[] { SeasonOption.SoupSoy, SeasonOption.Gochugaru, SeasonOption.Salt, SeasonOption.Pepper };
            default: return new[] { SeasonOption.Salt, SeasonOption.Pepper, SeasonOption.SoySauce, SeasonOption.Sugar };
        }
    }

    // 정답 (docs/29 제안, 미확정). 끓이기는 재료에 따라 정답이 바뀐다.
    public SeasonOption SeasonAnswer()
    {
        var names = items == null ? new List<string>() : items.Select(i => i.ing.name).ToList();
        switch (m == null ? Method.Grill : m.id)
        {
            case Method.Grill: return SeasonOption.Salt;
            case Method.Stirfry: return SeasonOption.SoySauce;
            case Method.Boil:
                // 김치가 들어가면 고춧가루, 두부나 달걀이 들어가면 국간장 (docs/29 제안)
                if (names.Contains("김치")) return SeasonOption.Gochugaru;
                return SeasonOption.SoupSoy;
            default: return SeasonOption.Salt;
        }
    }

    // 선택지를 고른다. 한 번 고르면 바꿀 수 없다. 맛 보정량을 돌려준다 (가산은 +, 감산은 -).
    public float PickSeason(SeasonOption choice)
    {
        if (SeasonDone) return 0f;
        SeasonPicked = choice;
        SeasonDelta = choice == SeasonAnswer() ? SeasonGainCorrect : -SeasonLossWrong;
        return SeasonDelta;
    }
}
