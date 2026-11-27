using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 이어하기 저장 (docs/19 10번). 두 때에 자동 저장한다: ① 주 결산이 끝나 로비로 돌아올 때(그 주의 시작 상태), ② 하루 식사가 끝나 다음 날로 넘어갈 때.
// 저장되는 것: 주차, 일차, 비상 라면 횟수, 이번 주에 먹은 요리, 예산, 쓴 돈, 냉장고 재료와 개수, 재료별 유통기한. 장바구니는 저장하지 않는다.
// 이어하기 = 일차가 1이면 로비에서, 2 이상이면 그날의 하루 선택 화면에서 다시 시작한다. 굶어서 끝난 판은 지운다.
// 재료는 이름(달걀, 두부 …)으로 저장한다. 이름은 바꾸지 않는다 (CLAUDE.md).
[Serializable]
class RunSaveData
{
    public int week;
    public int day;          // 없으면(예전 저장) 0 -> 1일 차로 본다
    public int strikes;
    public int budget;
    public int bought;
    public string[] invName;
    public int[] invCount;
    public string[] expName;
    public int[] expDay;
    public DishSave[] dishes;
}

// 이번 주에 먹은 요리 하나. 재료는 이름, 익힘과 방식은 enum 숫자로 저장한다 (결산·칭호 판정에 쓴다).
[Serializable]
class DishSave
{
    public string name, key, note;
    public string[] ings;
    public int[] stages;
    public int method, satisfaction;
}

public static class RunSave
{
    static string Path { get { return SaveDir.File("run.json"); } } // 에디터 테스트 기록과 빌드 기록을 나눈다 (SaveDir)

    public static bool Has()
    {
        try { return System.IO.File.Exists(Path); }
        catch (Exception) { return false; }
    }

    public static void Save(Run run)
    {
        try
        {
            var s = new RunSaveData
            {
                week = run.week,
                day = run.day,
                strikes = run.strikes,
                budget = run.budget,
                bought = run.bought,
                invName = run.inv.Keys.Select(i => i.name).ToArray(),
                invCount = run.inv.Values.ToArray(),
                expName = run.expire.Keys.Select(i => i.name).ToArray(),
                expDay = run.expire.Values.ToArray(),
                dishes = run.dishes.Select(d => new DishSave
                {
                    name = d.name, key = d.key, note = d.note, method = (int)d.method, satisfaction = d.satisfaction,
                    ings = (d.ings ?? new Ingredient[0]).Select(i => i.name).ToArray(),
                    stages = (d.stages ?? new Doneness[0]).Select(x => (int)x).ToArray(),
                }).ToList().ToArray(),
            };
            System.IO.File.WriteAllText(Path, JsonUtility.ToJson(s));
        }
        catch (Exception e) { Debug.LogWarning("이어하기 저장 실패: " + e.Message); }
    }

    // 저장된 주차의 시작 상태를 돌려준다. 없거나 깨졌으면 null.
    public static Run Load()
    {
        try
        {
            if (!System.IO.File.Exists(Path)) return null;
            var s = JsonUtility.FromJson<RunSaveData>(System.IO.File.ReadAllText(Path));
            if (s == null || s.week < 1) return null;
            var run = new Run
            {
                week = s.week,
                day = Mathf.Clamp(s.day, 1, Balance.Days),
                strikes = Mathf.Max(0, s.strikes),
                budget = s.budget,
                bought = s.bought,
            };
            var all = Data.All;
            for (int k = 0; k < s.invName.Length && k < s.invCount.Length; k++)
            {
                var ing = all.FirstOrDefault(i => i.name == s.invName[k]);
                if (ing != null) run.inv[ing] = s.invCount[k];
            }
            for (int k = 0; k < s.expName.Length && k < s.expDay.Length; k++)
            {
                var ing = all.FirstOrDefault(i => i.name == s.expName[k]);
                if (ing != null) run.expire[ing] = s.expDay[k];
            }
            if (s.dishes != null)
                foreach (var d in s.dishes)
                    run.dishes.Add(new Dish
                    {
                        name = d.name, key = d.key, note = d.note, method = (Method)d.method, satisfaction = d.satisfaction,
                        ings = d.ings.Select(n => all.FirstOrDefault(i => i.name == n)).Where(i => i != null).ToArray(),
                        stages = d.stages.Select(x => (Doneness)x).ToArray(),
                    });
            return run;
        }
        catch (Exception e)
        {
            Debug.LogWarning("이어하기 불러오기 실패: " + e.Message);
            return null;
        }
    }

    public static void Delete()
    {
        try { if (System.IO.File.Exists(Path)) System.IO.File.Delete(Path); }
        catch (Exception) { /* 지우지 못해도 게임은 계속 돈다 */ }
    }
}
