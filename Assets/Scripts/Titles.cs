using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 칭호 시스템 (docs/17). 한 주가 끝나는 결산 때, 도감에 모인 요리 수와 방식별 수집 수로 칭호를 판정한다.
// 칭호를 얻으면 파일(titles.json)에 저장돼서 다음 판에도 유지된다. 대표 칭호 하나를 골라 보여 줄 수 있고, 버프는 없다.
// "자취 생존자"를 얻으면 새우와 튀김 조리가 해금된다 (Data.Unlocked, Data.Active가 이 상태를 읽는다).
// 화면과 무관한 순수 로직이라 수치 검증이 쉽다. 수치는 초안이며 플레이하며 조정한다.

public class TitleDef
{
    public string id, name, cond, reward;
    public Func<IList<Dish>, bool> done;       // 조건 달성 여부 (도감 요리 목록 기준)
    public Func<IList<Dish>, string> progress; // 진행 상황 문구
}

public static class Titles
{
    public const string Survivor = "survivor"; // 도감 10종 -> 튀김 해금

    static int OfMethod(IList<Dish> dishes, Method m) { return dishes.Count(d => d.method == m); }

    static TitleDef Count(string id, string name, int n, string reward = "없음")
    {
        return new TitleDef
        {
            id = id, name = name, reward = reward,
            cond = "도감 " + n + "종 수집",
            done = dishes => dishes.Count >= n,
            progress = dishes => Mathf.Min(n, dishes.Count) + " / " + n,
        };
    }

    static TitleDef ByMethod(string id, string name, Method m, int n)
    {
        string mn = Data.GetMethod(m).name;
        return new TitleDef
        {
            id = id, name = name, reward = "없음",
            cond = mn + " 요리 " + n + "종 수집",
            done = dishes => OfMethod(dishes, m) >= n,
            progress = dishes => Mathf.Min(n, OfMethod(dishes, m)) + " / " + n,
        };
    }

    public static readonly TitleDef[] All =
    {
        Count("intro", "집밥 입문", 5),
        Count(Survivor, "자취 생존자", 10, "튀김 해금 (새우 + 튀기기)"),
        Count("shopowner", "동네 밥집 사장", 20),
        Count("hansik", "한식 달인", 35),
        ByMethod("grillpro", "굽기 장인", Method.Grill, 5),
        ByMethod("stirpro", "볶기 장인", Method.Stirfry, 5),
        ByMethod("boilpro", "끓이기 장인", Method.Boil, 5),
        ByMethod("frypro", "튀김 명인", Method.Fry, 3),
    };

    static readonly HashSet<string> earned = new HashSet<string>();
    static string representative;

    public static bool Has(string id) { return earned.Contains(id); }
    public static int EarnedCount { get { return earned.Count; } }
    public static bool FryUnlocked { get { return earned.Contains(Survivor); } }
    public static TitleDef Representative { get { return All.FirstOrDefault(t => t.id == representative && earned.Contains(t.id)); } }
    public static bool IsRepresentative(TitleDef t) { return Representative == t; }

    // 결산 때 부른다. 새로 얻은 칭호를 돌려준다. 처음 얻은 칭호는 대표 칭호가 비어 있으면 대표가 된다.
    public static List<TitleDef> Check(IList<Dish> dishes)
    {
        var gained = new List<TitleDef>();
        foreach (var t in All)
            if (!earned.Contains(t.id) && t.done(dishes))
            {
                earned.Add(t.id);
                gained.Add(t);
            }
        if (gained.Count > 0)
        {
            if (Representative == null) representative = gained.Last().id;
            Save();
        }
        return gained;
    }

    public static string ProgressOf(TitleDef t, IList<Dish> dishes) { return t.progress(dishes); }

    // 대표 칭호 변경. 이미 대표인 칭호를 다시 누르면 해제한다.
    public static void SetRepresentative(TitleDef t)
    {
        if (t == null || !earned.Contains(t.id)) return;
        representative = representative == t.id ? null : t.id;
        Save();
    }

    // ---- 저장
    [Serializable]
    class SaveData
    {
        public List<string> earned = new List<string>();
        public string representative;
    }

    static string Path { get { return System.IO.Path.Combine(Application.persistentDataPath, "titles.json"); } }

    static void Save()
    {
        try
        {
            var s = new SaveData { earned = earned.ToList(), representative = representative };
            System.IO.File.WriteAllText(Path, JsonUtility.ToJson(s));
        }
        catch (Exception e) { Debug.LogWarning("[칭호] 저장 실패: " + e.Message); }
    }

    public static void Load()
    {
        try
        {
            earned.Clear();
            representative = null;
            if (!System.IO.File.Exists(Path)) return;
            var s = JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(Path));
            if (s == null) return;
            foreach (var id in s.earned) if (All.Any(t => t.id == id)) earned.Add(id);
            representative = s.representative;
        }
        catch (Exception e) { Debug.LogWarning("[칭호] 불러오기 실패: " + e.Message); }
    }
}
