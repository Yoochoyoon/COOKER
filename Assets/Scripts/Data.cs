using UnityEngine;

// 재료/방식 데이터와 밸런스 수치. 수치는 docs/09재료와방식수치초안.md 기준이며, 플레이하면서 여기만 고치면 된다.
public enum Method { Grill, Stirfry, Boil }

public class Ingredient
{
    public string name;
    public int price;
    public int shelf;        // 구매일(1일차) 포함 며칠까지 쓸 수 있는지
    public Method[] fits;    // 적합 방식
    public int baseScore;
    public float speedMod;
    public Color raw, cooked, burnt;
}

public class MethodDef
{
    public Method id;
    public string name;      // 굽기
    public string verb;      // 구운
    public float speed;      // 중불 기준 초당 익힘
    public float lo, hi;     // 적정 구간
    public string guide;
}

public static class Balance
{
    public const int Budget = 16000;
    public const int MinUnits = 6;                 // 첫날 한 끼는 엄마가 보내준 달걀이라 6끼분이 필요
    public const int Days = 7;
    public const float MaxCookTime = 40f;
    public static readonly float[] Fire = { 0.6f, 1.0f, 1.5f };
    public static readonly string[] FireName = { "약불", "중불", "강불" };
    public const float NonFit = 0.6f;
    public const float RawLimit = 40f;             // 이 미만에 꺼내면 날것(실패)
    public const float BurnLimit = 100f;           // 이 이상이면 탄 것(실패)
    public const float BubbleInterval = 5f;
    public const float LidHoldTime = 0.5f;
    public const float StirSlow = 0.7f;
    public const float BubbleFast = 1.2f;
    public const float FlipBonus = 5f;
    // 등급 컷 (임시)
    public const float GradeS = 85f, GradeA = 70f, GradeB = 55f;
}

public static class Data
{
    static Color C(float r, float g, float b) { return new Color(r, g, b); }

    public static readonly MethodDef[] Methods =
    {
        new MethodDef { id = Method.Grill,   name = "굽기",   verb = "구운", speed = 6f,   lo = 60, hi = 75,
            guide = "굽기: 빨리 익는다. 익는 도중 [뒤집기!] 버튼이 잠깐 뜨면 눌러 보자." },
        new MethodDef { id = Method.Stirfry, name = "볶기",   verb = "볶은", speed = 4f,   lo = 55, hi = 80,
            guide = "볶기: 마우스를 누른 채 좌우로 흔들면 천천히 익는다." },
        new MethodDef { id = Method.Boil,    name = "끓이기", verb = "끓인", speed = 2.5f, lo = 50, hi = 90,
            guide = "끓이기: 거품이 올라오면 [뚜껑]을 꾹 눌러 가라앉히자." },
    };

    public static MethodDef GetMethod(Method m) { return Methods[(int)m]; }

    public static readonly Ingredient Egg = new Ingredient
    {
        name = "달걀", price = 800, shelf = 4, fits = new[] { Method.Grill, Method.Stirfry }, baseScore = 60, speedMod = 1.2f,
        raw = C(0.97f, 0.97f, 0.88f), cooked = C(1f, 0.8f, 0.1f), burnt = C(0.2f, 0.1f, 0.03f)
    };

    public static readonly Ingredient[] All =
    {
        Egg,
        new Ingredient { name = "두부", price = 1000, shelf = 3, fits = new[] { Method.Grill, Method.Boil }, baseScore = 55, speedMod = 1.0f,
            raw = C(0.98f, 0.98f, 0.98f), cooked = C(0.9f, 0.65f, 0.25f), burnt = C(0.15f, 0.08f, 0.03f) },
        new Ingredient { name = "양파", price = 600, shelf = 7, fits = new[] { Method.Stirfry, Method.Boil }, baseScore = 40, speedMod = 0.9f,
            raw = C(0.95f, 0.88f, 0.9f), cooked = C(0.85f, 0.5f, 0.15f), burnt = C(0.15f, 0.07f, 0.03f) },
        new Ingredient { name = "감자", price = 700, shelf = 6, fits = new[] { Method.Grill, Method.Boil }, baseScore = 50, speedMod = 0.8f,
            raw = C(0.85f, 0.8f, 0.5f), cooked = C(0.95f, 0.7f, 0.1f), burnt = C(0.15f, 0.08f, 0.03f) },
        new Ingredient { name = "돼지고기", price = 2500, shelf = 3, fits = new[] { Method.Grill, Method.Stirfry }, baseScore = 90, speedMod = 1.0f,
            raw = C(1f, 0.55f, 0.6f), cooked = C(0.6f, 0.33f, 0.15f), burnt = C(0.1f, 0.05f, 0.02f) },
        new Ingredient { name = "김치", price = 900, shelf = 5, fits = new[] { Method.Stirfry, Method.Boil }, baseScore = 65, speedMod = 1.0f,
            raw = C(1f, 0.3f, 0.15f), cooked = C(0.7f, 0.1f, 0.05f), burnt = C(0.12f, 0.05f, 0.03f) },
    };

    public static string MomHint(Ingredient i)
    {
        switch (i.name)
        {
            case "달걀": return "달걀은 성질이 급하단다. 한눈팔면 금방이야.";
            case "두부": return "두부는 물렁해 보여도 은근 고집이 있어.";
            case "양파": return "양파는 느긋한 애야. 천천히 달래줘야지.";
            case "감자": return "감자는 속까지 익으려면 시간이 좀 걸려.";
            case "돼지고기": return "고기는 비싼 만큼 정성이 필요하단다.";
            case "김치": return "김치는 어디에 넣어도 중심은 잡아줘.";
        }
        return "";
    }
}
