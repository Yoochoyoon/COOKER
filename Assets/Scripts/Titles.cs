using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 칭호 시스템 (docs/08). 도감에 쌓인 요리로 조건을 채우면 칭호를 얻고, 칭호 하나를 골라 장착하면 그 버프를 받는다.
// 기본 칭호 6개(방식 장인 4 + 재료 탐험가 + 미식가)와 상위 칭호 2개. 버프 종류는 08번 문서의
// "적정 구간 넓히기", "유통기한 연장", "만족도 상한 상승" 세 가지다(알러지 해소는 프로토타입에서 뺀다).
// 화면과 무관한 순수 로직이라 수치 검증이 쉽다. 얻은 칭호와 장착은 titles.json에 저장한다.

public class TitleBuff
{
    public Method? only;   // 이 방식에만 적용(없으면 모든 방식)
    public float band;     // 적정 구간을 양쪽으로 넓히는 폭
    public float basePts;  // 재료 기본 점수에 더하는 값(만족도 상한 상승)
    public int shelfDays;  // 모든 재료 유통기한 연장(일)
}

public class TitleDef
{
    public string id, name, cond, buffText;
    public bool advanced;
    public TitleBuff buff;
    public Func<IList<Dish>, int, bool> done;       // (도감 요리 목록, 얻은 기본 칭호 수) -> 조건 달성
    public Func<IList<Dish>, int, string> progress; // 진행 상황 문구
}

public static class Titles
{
    public const int NamedNeeded = 3;   // 방식 장인: 그 방식의 대표(이름 있는) 레시피 수
    public const int GourmetNeeded = 3; // 미식가: 랭크 B 이상 요리 수

    // 난이도 랭크 D·C·B·A. 방식 난이도 + 재료 수 + 비싼 재료 여부를 합쳐 규칙으로 계산한다.
    public static int Rank(Dish d)
    {
        int score = d.method == Method.Fry ? 2 : d.method == Method.Boil ? 0 : 1;
        score += Mathf.Max(0, d.ings.Length - 1);
        if (d.ings.Any(i => i.price >= 1500)) score++;
        return Mathf.Clamp(score - 1, 0, 3);
    }
    public static string RankName(int r) { return "DCBA"[Mathf.Clamp(r, 0, 3)].ToString(); }

    static int Named(IList<Dish> dishes, Method m) { return dishes.Count(d => d.method == m && Recipes.IsNamed(d.key)); }
    static int RankAtLeast(IList<Dish> dishes, int r) { return dishes.Count(d => Rank(d) >= r); }
    static int Used(IList<Dish> dishes) { return dishes.SelectMany(d => d.ings).Select(i => i.name).Distinct().Count(); }

    static TitleDef MethodTitle(string id, string name, Method m)
    {
        string mn = Data.GetMethod(m).name;
        return new TitleDef
        {
            id = id, name = name,
            cond = mn + " 대표 레시피 " + NamedNeeded + "종 만들기",
            buffText = mn + " 적정 구간 ±4",
            buff = new TitleBuff { only = m, band = 4f },
            done = (dishes, basics) => Named(dishes, m) >= NamedNeeded,
            progress = (dishes, basics) => Mathf.Min(NamedNeeded, Named(dishes, m)) + " / " + NamedNeeded,
        };
    }

    public static readonly TitleDef[] All =
    {
        MethodTitle("grill", "구이 장인", Method.Grill),
        MethodTitle("stirfry", "볶음 장인", Method.Stirfry),
        MethodTitle("boil", "국물 장인", Method.Boil),
        MethodTitle("fry", "튀김 장인", Method.Fry),
        new TitleDef
        {
            id = "explorer", name = "재료 탐험가",
            cond = "재료 " + Data.All.Length + "종을 모두 요리에 써 보기",
            buffText = "모든 재료 유통기한 +2일",
            buff = new TitleBuff { shelfDays = 2 },
            done = (dishes, basics) => Used(dishes) >= Data.All.Length,
            progress = (dishes, basics) => Used(dishes) + " / " + Data.All.Length,
        },
        new TitleDef
        {
            id = "gourmet", name = "미식가",
            cond = "랭크 B 이상 요리 " + GourmetNeeded + "개",
            buffText = "모든 요리 기본 점수 +5",
            buff = new TitleBuff { basePts = 5f },
            done = (dishes, basics) => RankAtLeast(dishes, 2) >= GourmetNeeded,
            progress = (dishes, basics) => Mathf.Min(GourmetNeeded, RankAtLeast(dishes, 2)) + " / " + GourmetNeeded,
        },
        new TitleDef
        {
            id = "pro", name = "자취 고수", advanced = true,
            cond = "기본 칭호 3개 + 랭크 A 요리 1개",
            buffText = "모든 방식 구간 ±3, 기본 점수 +3",
            buff = new TitleBuff { band = 3f, basePts = 3f },
            done = (dishes, basics) => basics >= 3 && RankAtLeast(dishes, 3) >= 1,
            progress = (dishes, basics) => "기본 칭호 " + Mathf.Min(3, basics) + "/3, 랭크 A " + Mathf.Min(1, RankAtLeast(dishes, 3)) + "/1",
        },
        new TitleDef
        {
            id = "master", name = "요리 달인", advanced = true,
            cond = "기본 칭호 6개 모두",
            buffText = "모든 방식 구간 ±5, 기본 점수 +5, 유통기한 +2일",
            buff = new TitleBuff { band = 5f, basePts = 5f, shelfDays = 2 },
            done = (dishes, basics) => basics >= 6,
            progress = (dishes, basics) => "기본 칭호 " + basics + "/6",
        },
    };

    static readonly HashSet<string> earned = new HashSet<string>();
    static string equipped;

    public static bool Has(string id) { return earned.Contains(id); }
    public static int EarnedCount { get { return earned.Count; } }
    public static TitleDef Equipped { get { return All.FirstOrDefault(t => t.id == equipped && earned.Contains(t.id)); } }
    public static bool IsEquipped(TitleDef t) { return Equipped == t; }

    // 도감이 바뀔 때마다 부른다. 새로 얻은 칭호의 이름을 돌려준다.
    public static List<string> Check(IList<Dish> dishes)
    {
        var gained = new List<string>();
        for (int pass = 0; pass < 2; pass++) // 상위 칭호는 같은 판정에서 얻은 기본 칭호에 기대므로 한 번 더 돈다
        {
            int basics = All.Count(t => !t.advanced && earned.Contains(t.id));
            foreach (var t in All)
                if (!earned.Contains(t.id) && t.done(dishes, basics))
                {
                    earned.Add(t.id);
                    gained.Add(t.name);
                }
        }
        if (gained.Count > 0) Save();
        return gained;
    }

    public static string ProgressOf(TitleDef t, IList<Dish> dishes)
    {
        return t.progress(dishes, All.Count(x => !x.advanced && earned.Contains(x.id)));
    }

    // 장착은 하나만. 이미 장착한 칭호를 다시 누르면 해제한다.
    public static void Equip(TitleDef t)
    {
        if (t == null || !earned.Contains(t.id)) return;
        equipped = equipped == t.id ? null : t.id;
        Save();
    }

    // ---- 버프 (장착한 칭호가 없으면 모두 0)
    static TitleBuff BuffFor(Method m)
    {
        var e = Equipped;
        if (e == null || (e.buff.only.HasValue && e.buff.only.Value != m)) return null;
        return e.buff;
    }
    public static float Band(Method m) { var b = BuffFor(m); return b == null ? 0f : b.band; }
    public static float BasePts(Method m) { var b = BuffFor(m); return b == null ? 0f : b.basePts; }
    public static int ShelfDays { get { var e = Equipped; return e == null ? 0 : e.buff.shelfDays; } }

    // ---- 저장
    [Serializable]
    class SaveData
    {
        public List<string> earned = new List<string>();
        public string equipped;
    }

    static string Path { get { return System.IO.Path.Combine(Application.persistentDataPath, "titles.json"); } }

    static void Save()
    {
        try
        {
            var s = new SaveData { earned = earned.ToList(), equipped = equipped };
            System.IO.File.WriteAllText(Path, JsonUtility.ToJson(s));
        }
        catch (Exception e) { Debug.LogWarning("[칭호] 저장 실패: " + e.Message); }
    }

    public static void Load()
    {
        try
        {
            earned.Clear();
            equipped = null;
            if (!System.IO.File.Exists(Path)) return;
            var s = JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(Path));
            if (s == null) return;
            foreach (var id in s.earned) if (All.Any(t => t.id == id)) earned.Add(id);
            equipped = s.equipped;
        }
        catch (Exception e) { Debug.LogWarning("[칭호] 불러오기 실패: " + e.Message); }
    }
}
