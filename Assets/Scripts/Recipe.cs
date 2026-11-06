using System;
using System.Collections.Generic;
using System.Linq;

// 엄마의 전언: 보유 재료로 만들 수 있는 추천 요리와 간단한 레시피. 화면과 무관한 순수 로직.
// 고르는 기준: 재료와 방식이 잘 맞는지(만족도) + 유통기한이 급한 재료를 먼저 쓰는지.
public class Recipe
{
    public string name;
    public Ingredient[] ings;
    public Method method;
    public int expected;     // 잘 익혔을 때 예상 만족도
    public string[] steps;
    public string why;       // 엄마가 이걸 추천하는 이유 (유통기한 등)
}

public static class Recipes
{
    // 자주 쓰는 조합은 진짜 요리 이름으로 부른다. 없으면 "재료 + 방식 이름"으로 붙인다.
    static readonly Dictionary<string, string> names = new Dictionary<string, string>
    {
        { "달걀/Grill", "달걀프라이" }, { "달걀/Stirfry", "스크램블 에그" },
        { "두부/Grill", "두부부침" }, { "두부/Boil", "두부국" },
        { "양파/Stirfry", "양파볶음" }, { "양파/Boil", "양파국" },
        { "감자/Grill", "감자구이" }, { "감자/Boil", "감잣국" },
        { "돼지고기/Grill", "돼지고기구이" }, { "돼지고기/Stirfry", "돼지고기볶음" },
        { "김치/Stirfry", "김치볶음" }, { "김치/Boil", "김칫국" },
        { "김치+돼지고기/Stirfry", "김치제육볶음" }, { "김치+두부/Boil", "김치두부찌개" },
        { "김치+양파/Boil", "김치찌개" }, { "김치+양파/Stirfry", "김치양파볶음" },
        { "달걀+양파/Stirfry", "양파달걀볶음" }, { "달걀+김치/Stirfry", "김치달걀볶음" },
        { "감자+두부/Boil", "감자두부국" }, { "감자+두부/Grill", "감자두부구이" },
        { "돼지고기+양파/Stirfry", "양파돼지볶음" }, { "감자+양파/Boil", "감자양파국" },
        { "달걀+돼지고기/Stirfry", "돼지고기달걀볶음" }, { "달걀+두부/Grill", "두부달걀부침" },
        { "감자/Fry", "감자튀김" }, { "두부/Fry", "두부튀김" }, { "양파/Fry", "양파튀김" }, { "돼지고기/Fry", "돈가스" },
        { "새우/Fry", "새우튀김" }, { "감자+새우/Fry", "새우감자튀김" }, { "새우+양파/Fry", "새우양파튀김" }, { "두부+새우/Fry", "새우두부튀김" },
        { "감자+돼지고기/Fry", "감자돈가스" }, { "두부+양파/Fry", "채소두부튀김" }, { "감자+양파/Fry", "감자양파튀김" },
    };

    static readonly Dictionary<Method, string> suffix = new Dictionary<Method, string>
    {
        { Method.Grill, "구이" }, { Method.Stirfry, "볶음" }, { Method.Boil, "국" }, { Method.Fry, "튀김" },
    };

    // 이름이 정해진 대표 요리인지 (칭호의 "대표 레시피 수집" 조건). key는 Dish.key와 같은 형식이다.
    public static bool IsNamed(string key) { return names.ContainsKey(key); }

    public static string NameOf(Ingredient[] ings, Method m)
    {
        string key = string.Join("+", ings.Select(i => i.name).OrderBy(s => s)) + "/" + m;
        string n;
        return names.TryGetValue(key, out n) ? n : string.Join("", ings.Select(i => i.name)) + suffix[m];
    }

    // daysLeft: 오늘 포함 며칠 더 쓸 수 있는지 (1이면 오늘까지)
    public static List<Recipe> Suggest(IEnumerable<Ingredient> owned, Func<Ingredient, int> daysLeft, int count = 3, Func<Method, bool> allowed = null)
    {
        var have = owned.ToArray();
        var combos = new List<Ingredient[]>();
        foreach (var a in have) combos.Add(new[] { a });
        for (int i = 0; i < have.Length; i++)
            for (int j = i + 1; j < have.Length; j++)
                combos.Add(new[] { have[i], have[j] });

        var scored = new List<KeyValuePair<float, Recipe>>();
        foreach (var c in combos)
            foreach (Method m in Enum.GetValues(typeof(Method)))
            {
                if (allowed != null && !allowed(m)) continue; // 아직 열리지 않은 방식은 권하지 않는다
                if (!c.All(i => i.fits.Contains(m))) continue; // 엄마는 안 맞는 방식은 권하지 않는다
                int expected = (int)Math.Round(c.Average(i => (double)i.baseScore));
                int soonest = c.Min(daysLeft);
                float urgency = soonest <= 1 ? 30f : soonest == 2 ? 15f : 0f;
                // 2개 조합은 재료를 두 개 쓰니 하루 끼니 수를 생각해 살짝 깎는다
                float score = expected + urgency - (c.Length - 1) * 5f;
                scored.Add(new KeyValuePair<float, Recipe>(score, new Recipe
                {
                    name = NameOf(c, m), ings = c, method = m, expected = expected,
                    steps = Steps(c, m),
                    why = soonest <= 1 ? J(c.First(i => daysLeft(i) <= 1).name, "이", "가") + " 오늘까지야! 이것부터 써라."
                        : soonest == 2 ? J(c.First(i => daysLeft(i) == 2).name, "은", "는") + " 내일까지니까 서두르렴."
                        : null,
                }));
            }

        // 같은 재료 조합이 여러 방식으로 겹치지 않게, 점수 높은 것부터 재료 조합당 하나씩
        // 한 재료는 추천 두 개까지만 나와서 고를 거리가 다양하게 남는다
        var picked = new List<Recipe>();
        var used = new HashSet<string>();
        var perIng = new Dictionary<Ingredient, int>();
        foreach (var kv in scored.OrderByDescending(kv => kv.Key))
        {
            string k = string.Join("+", kv.Value.ings.Select(i => i.name).OrderBy(s => s));
            if (used.Contains(k) || kv.Value.ings.Any(i => perIng.ContainsKey(i) && perIng[i] >= 2)) continue;
            used.Add(k);
            foreach (var i in kv.Value.ings) perIng[i] = (perIng.ContainsKey(i) ? perIng[i] : 0) + 1;
            picked.Add(kv.Value);
            if (picked.Count >= count) break;
        }
        return picked;
    }

    // 받침 유무에 맞는 조사를 붙인다 (달걀은 / 두부는)
    static string J(string w, string withFinal, string without)
    {
        char ch = w[w.Length - 1];
        bool final = ch >= 0xAC00 && ch <= 0xD7A3 && (ch - 0xAC00) % 28 != 0;
        return w + (final ? withFinal : without);
    }

    public static string[] Steps(Ingredient[] c, Method m)
    {
        var s = new List<string>();
        var bySpeed = c.OrderBy(i => i.speedMod).ToArray();
        if (c.Length == 2 && Math.Abs(bySpeed[0].speedMod - bySpeed[1].speedMod) >= 0.2f)
            s.Add(J(bySpeed[0].name, "은", "는") + " 늦게 익으니 먼저 넣고, " + J(bySpeed[1].name, "은", "는") + " 조금 있다가 넣어라.");
        else
            s.Add(c.Length == 2 ? J(c[0].name, "이랑", "랑") + " " + J(c[1].name, "을", "를") + " 같이 팬에 넣어라." : J(c[0].name, "을", "를") + " 팬에 넣어라.");

        float fastest = c.Max(i => i.speedMod), slowest = c.Min(i => i.speedMod);
        if (fastest >= 1.2f) s.Add("불은 약불~중불. " + J(c.First(i => i.speedMod == fastest).name, "은", "는") + " 성질이 급해서 금방 탄다.");
        else if (slowest <= 0.7f) s.Add("불은 중불~강불. 속까지 익으려면 시간이 좀 걸려.");
        else s.Add("불은 중불이면 충분해.");

        if (m == Method.Grill) s.Add("뒤집기 버튼이 뜨면 바로 뒤집어라. 놓치면 한쪽만 익는다.");
        else if (m == Method.Stirfry) s.Add("손 쉬지 말고 계속 저어라. 잠깐만 멈춰도 눌어붙는다.");
        else if (m == Method.Fry) s.Add("기름 온도를 유지해라. 약불로 오래 두면 눅눅해진다.");
        else s.Add("거품이 올라오면 바로 뚜껑을 꾹 눌러라. 넘치면 맛이 줄어든다.");

        s.Add("게이지가 주황색일 때 바로 꺼내기! 빨개지면 늦은 거다.");
        return s.ToArray();
    }

    // ---- 진짜 레시피 (도감 상세): 실제로 이 요리를 만들 때 하는 순서. 게임 요령(Steps)과는 다르다.
    // 재료 손질 → 조리 → 간 → 담기. 재료·방식 조합마다 손질과 조리 문장을 이어 붙인다.
    static string Prep(string ing, Method m)
    {
        switch (ing)
        {
            case "감자":
                return m == Method.Grill ? "감자는 껍질을 벗겨 0.5cm 두께로 썰고, 찬물에 5분 담가 전분기를 뺀 뒤 물기를 닦는다."
                    : m == Method.Fry ? "감자는 껍질을 벗겨 길쭉하게 썰고, 찬물에 담가 전분기를 뺀 뒤 물기를 완전히 닦는다."
                    : "감자는 껍질을 벗겨 한입 크기로 깍둑썬다.";
            case "두부":
                return m == Method.Boil ? "두부는 큼직하게 깍둑썬다."
                    : m == Method.Fry ? "두부는 한입 크기로 썰어 물기를 빼고 전분가루를 얇게 묻힌다."
                    : "두부는 1cm 두께로 썰어 키친타월로 물기를 꼼꼼히 닦고 소금을 살짝 뿌린다.";
            case "양파":
                return m == Method.Boil ? "양파는 큼직하게 썬다."
                    : m == Method.Fry ? "양파는 1cm 두께로 둥글게 썰어 튀김가루를 묻힌다."
                    : "양파는 껍질을 벗겨 0.5cm 두께로 채 썬다.";
            case "달걀":
                return m == Method.Stirfry ? "달걀은 그릇에 풀어 소금을 한 꼬집 넣는다." : "달걀은 껍데기 조각이 안 들어가게 깨서 그릇에 담아 둔다.";
            case "김치":
                return m == Method.Boil ? "김치는 한입 크기로 썬다." : "김치는 송송 썰어 국물을 살짝 짠다.";
            case "돼지고기":
                return m == Method.Fry ? "돼지고기는 방망이로 두들겨 얇게 펴고 소금·후추로 밑간한 뒤, 밀가루 → 달걀물 → 빵가루 순서로 입힌다."
                    : m == Method.Stirfry ? "돼지고기는 얇게 썰어 간장 1숟가락과 후추로 10분 재운다."
                    : "돼지고기는 1cm 두께로 썰어 소금·후추로 밑간한다.";
            case "새우":
                return "새우는 껍질과 내장을 빼고 소금·후추로 밑간한 뒤, 튀김옷(튀김가루+찬물)을 입힌다.";
        }
        return ing + "을(를) 먹기 좋게 손질한다.";
    }

    // 실제 요리 레시피. 한 줄에 한 단계.
    public static string[] Real(Ingredient[] c, Method m)
    {
        var s = new List<string>();
        foreach (var i in c) s.Add(Prep(i.name, m));

        bool egg = c.Any(i => i.name == "달걀"), kimchi = c.Any(i => i.name == "김치");
        var slow = c.OrderBy(i => i.speedMod).First();   // 늦게 익는 재료는 먼저 넣는다
        var fast = c.OrderBy(i => i.speedMod).Last();
        bool order = c.Length == 2 && Math.Abs(slow.speedMod - fast.speedMod) >= 0.2f;
        string orderTip = order ? " " + J(slow.name, "을", "를") + " 먼저 넣고 " + J(fast.name, "은", "는") + " 조금 뒤에 넣는다." : "";

        switch (m)
        {
            case Method.Grill:
                s.Add("팬에 식용유를 얇게 두르고 중불로 달군다. 재료를 올려 한쪽 면이 노릇해지면(2~3분) 뒤집어 반대쪽도 굽는다." + orderTip);
                s.Add("소금이나 후추로 간을 맞춘다.");
                break;
            case Method.Stirfry:
                if (egg)
                {
                    s.Add("팬에 기름을 두르고 중불에서 " + (c.Length == 2 ? J(c.First(i => i.name != "달걀").name, "을", "를") + " 먼저 볶는다. 반쯤 익으면 " : "") + "달걀물을 부어 젓가락으로 저으며 몽글하게 익힌다.");
                }
                else s.Add("팬을 센 불로 달궈 기름을 두르고, 재료를 넣어 쉬지 않고 저으며 2~3분 볶는다." + orderTip);
                s.Add(kimchi ? "김치가 들어가 간이 되니, 싱거우면 설탕 한 꼬집과 참기름을 넣는다." : "간장 1숟가락이나 소금으로 간하고 불을 끈다.");
                break;
            case Method.Boil:
                if (kimchi) s.Add("냄비에 참기름 한 숟가락을 두르고 김치를 2분 먼저 볶아 신맛을 눌러 준다.");
                s.Add("냄비에 물 2컵(약 400ml)을 붓고 끓으면 재료를 넣어 중불에서 8~10분 끓인다." + orderTip + " 끓어넘치면 뚜껑을 열거나 불을 줄인다.");
                s.Add(kimchi ? "김치 국물로 간이 되니 싱거울 때만 소금을 더한다." : "국간장이나 소금으로 간을 맞춘다.");
                break;
            case Method.Fry:
                s.Add("냄비에 기름을 재료가 잠길 만큼(3~4cm) 붓고 170~180℃로 달군다. 튀김옷을 떨어뜨려 바로 떠오르면 알맞다.");
                s.Add("재료를 하나씩 넣어 노릇해질 때까지 3~4분 튀긴다. 한꺼번에 많이 넣으면 기름이 식으니 나눠서 튀긴다." + orderTip);
                s.Add("건져서 기름을 빼고 소금을 살짝 뿌린다.");
                break;
        }
        s.Add("접시에 담아 뜨거울 때 먹는다.");
        return s.ToArray();
    }
}
