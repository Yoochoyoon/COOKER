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
    };

    static readonly Dictionary<Method, string> suffix = new Dictionary<Method, string>
    {
        { Method.Grill, "구이" }, { Method.Stirfry, "볶음" }, { Method.Boil, "국" },
    };

    public static string NameOf(Ingredient[] ings, Method m)
    {
        string key = string.Join("+", ings.Select(i => i.name).OrderBy(s => s)) + "/" + m;
        string n;
        return names.TryGetValue(key, out n) ? n : string.Join("", ings.Select(i => i.name)) + suffix[m];
    }

    // daysLeft: 오늘 포함 며칠 더 쓸 수 있는지 (1이면 오늘까지)
    public static List<Recipe> Suggest(IEnumerable<Ingredient> owned, Func<Ingredient, int> daysLeft, int count = 3)
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

    static string[] Steps(Ingredient[] c, Method m)
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

        if (m == Method.Grill) s.Add("뒤집을 때가 오면 뒤집개로 꼭 뒤집어라. 덜 탄다.");
        else if (m == Method.Stirfry) s.Add("뒤집개로 계속 휘저으면서 볶아야 안 탄다.");
        else s.Add("넘칠 것 같으면 뚜껑을 열고, 다 익으면 집게로 건져라.");

        s.Add("색이 먹음직스럽게 변하면 바로 꺼내기! 까맣게 되면 늦은 거다.");
        return s.ToArray();
    }
}
