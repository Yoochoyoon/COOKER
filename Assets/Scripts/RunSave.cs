using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 이어하기 저장 (docs/19 10번). 주 결산이 끝나 로비로 돌아올 때 그 주의 시작 상태를 저장한다.
// 저장되는 것: 주차, 예산, 쓴 돈, 냉장고 재료와 개수, 재료별 유통기한. 일차·먹은 요리·장바구니는 주가 시작될 때 원래 비어 있으므로 저장하지 않는다.
// 이어하기 = 저장된 주차의 로비에서 다시 시작한다. 굶어서 끝난 판은 지운다.
// 재료는 이름(달걀, 두부 …)으로 저장한다. 이름은 바꾸지 않는다 (CLAUDE.md).
[Serializable]
class RunSaveData
{
    public int week;
    public int budget;
    public int bought;
    public string[] invName;
    public int[] invCount;
    public string[] expName;
    public int[] expDay;
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
                budget = run.budget,
                bought = run.bought,
                invName = run.inv.Keys.Select(i => i.name).ToArray(),
                invCount = run.inv.Values.ToArray(),
                expName = run.expire.Keys.Select(i => i.name).ToArray(),
                expDay = run.expire.Values.ToArray(),
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
                day = 1,
                strikes = 0,
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
