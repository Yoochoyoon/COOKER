using System.Linq;
using UnityEngine;

// 재료/방식 데이터와 밸런스 수치. 수치는 docs/09재료와방식수치초안.md 기준이며, 플레이하면서 여기만 고치면 된다.
public enum Method { Grill, Stirfry, Boil, Fry }

public class Ingredient
{
    public string name;
    public int price;        // 1끼분 기준 실제 소매가 근사(docs/14 참고)
    public int shelf;        // 구매일 포함 냉장 보관으로 며칠까지 먹을 수 있는지(실제 소비기한 근사)
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

public static class Data
{
    // 재료와 조리 방식은 Assets/Resources/Data/ 아래 에셋에서 읽는다. 수치는 인스펙터에서 고친다 (CLAUDE.md).
    static MethodDef[] LoadMethods()
    {
        var list = Resources.LoadAll<MethodData>("Data/Methods").OrderBy(d => (int)d.id).Select(d => d.ToMethodDef()).ToArray();
        if (list.Length != 4) Debug.LogError("조리 방식 에셋이 4개가 아니다: " + list.Length + "개 (Assets/Resources/Data/Methods)");
        return list;
    }

    static Ingredient[] LoadIngredients()
    {
        var list = Resources.LoadAll<IngredientData>("Data/Ingredients").OrderBy(d => d.order).Select(d => d.ToIngredient()).ToArray();
        if (list.Length == 0) Debug.LogError("재료 에셋이 없다 (Assets/Resources/Data/Ingredients)");
        return list;
    }

    public static readonly MethodDef[] Methods = LoadMethods();

    public static MethodDef GetMethod(Method m) { return Methods[(int)m]; }

    // 전체 재료 목록 (순서는 IngredientData.order). 달걀과 새우는 이름으로 찾는다.
    public static readonly Ingredient[] All = LoadIngredients();
    public static readonly Ingredient Egg = All.First(i => i.name == "달걀");
    // 새우: 칭호 "자취 생존자"를 얻으면 상점에 나오고 튀김에만 어울린다 (docs/17). 해금 전에는 Active에 들어가지 않는다.
    public static readonly Ingredient Shrimp = All.First(i => i.name == "새우");

    // 해금 여부. 튀기기와 새우는 칭호 "자취 생존자"를 얻은 뒤에 열린다.
    public static bool Unlocked(Method m) { return m != Method.Fry || Titles.FryUnlocked; }
    public static bool Unlocked(Ingredient i) { return i != Shrimp || Titles.FryUnlocked; }

    // 지금 쓸 수 있는 재료와 방식 (장보기, 도감 칸, 방식 선택은 이 목록을 쓴다)
    public static Ingredient[] Active { get { return All.Where(Unlocked).ToArray(); } }
    public static MethodDef[] ActiveMethods { get { return Methods.Where(m => Unlocked(m.id)).ToArray(); } }

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
            case "새우": return "새우는 금방 익는다. 기름 앞에서 눈 떼지 마라.";
        }
        return "";
    }
}
