using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 간 고르기 화면 (docs/29). 조리가 끝나면 조리 화면 위를 어둡게 덮고, 선택지 네 개를 크게 띄운다.
// 판정은 Cook.Season.cs가 맡고, 이 파일은 화면과 키(Q W E R)만 맡는다.
public partial class GameFlow
{
    GameObject seasonUI;

    static string SeasonName(SeasonOption o)
    {
        switch (o)
        {
            case SeasonOption.Salt: return "소금";
            case SeasonOption.Pepper: return "후추";
            case SeasonOption.SoySauce: return "간장";
            case SeasonOption.Sugar: return "설탕";
            case SeasonOption.Gochugaru: return "고춧가루";
            case SeasonOption.SoupSoy: return "국간장";
        }
        return o.ToString();
    }

    // true = 간 고르기를 마쳤거나 건너뛴다(다음 단계로), false = 아직 고르는 중
    bool SeasonStep()
    {
        if (cook.SeasonDone) { CloseSeasonUI(); return true; }
        if (cook.GoodIngredients.Length == 0 || guideMeal) return true; // 만든 요리가 없거나 첫날 가이드는 건너뛴다
        if (seasonUI == null) BuildSeasonUI();
        var kb = Keyboard.current;
        if (kb != null)
        {
            var keys = new[] { kb.qKey, kb.wKey, kb.eKey, kb.rKey };
            var choices = cook.SeasonChoices;
            for (int i = 0; i < keys.Length && i < choices.Length; i++)
                if (keys[i].wasPressedThisFrame) { PickSeasonOption(choices[i]); break; }
        }
        if (cook.SeasonDone) { CloseSeasonUI(); return true; }
        return false;
    }

    void PickSeasonOption(SeasonOption o)
    {
        cook.PickSeason(o);
        Sound.Play("click");
    }

    void CloseSeasonUI()
    {
        if (seasonUI != null) { Destroy(seasonUI); seasonUI = null; }
    }

    void BuildSeasonUI()
    {
        // 조리 화면 위를 어둡게 덮는다(뒤 화면은 보이지만 눌리지 않는다)
        var dim = UIKit.Box(root, "SeasonDim", -0.2f, -0.2f, 1.2f, 1.2f, new Color(0f, 0f, 0f, 0.6f), false);
        seasonUI = dim.gameObject;
        // 어둡게 덮는 칸은 여백까지 넓게(-0.2~1.2) 깔지만, 창과 버튼은 내용 영역(root) 기준으로 놓는다 (여백 침범 방지)
        const float a0 = 0.2f / 1.4f, a1 = 1.2f / 1.4f;
        var area = UIKit.Box(dim, "SeasonArea", a0, a0, a1, a1);
        UIKit.Box(area, "SeasonPanel", 0.20f, 0.10f, 0.80f, 0.90f, UIKit.Card);
        UIKit.Label(area, "간 고르기", 0.30f, 0.76f, 0.70f, 0.86f, 56, TextAnchor.MiddleCenter, UIKit.Ink);
        // 처음 두 주는 엄마가 알맞은 간을 알려 준다. 그 뒤로는 스스로 고른다 (어느 간이 맞는지 단서가 없어 막막했다)
        string seasonNote = run.week <= 2 ? "엄마 힌트: " + cook.m.name + " 요리엔 " + SeasonName(cook.SeasonAnswer()) : "이 요리에 어울리는 간을 골라요";
        var noteTxt = UIKit.Label(area, seasonNote, 0.25f, 0.69f, 0.75f, 0.76f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
        noteTxt.resizeTextForBestFit = true; noteTxt.resizeTextMinSize = 18; noteTxt.resizeTextMaxSize = 30;
        var choices = cook.SeasonChoices;
        string[] keyNames = { "Q", "W", "E", "R" };
        for (int i = 0; i < choices.Length && i < 4; i++)
        {
            var opt = choices[i];
            float x0 = i % 2 == 0 ? 0.24f : 0.51f;
            float y0 = i < 2 ? 0.43f : 0.15f;
            UIKit.Btn(area, SeasonName(opt) + " [" + keyNames[i] + "]", x0, y0, x0 + 0.25f, y0 + 0.22f, () => PickSeasonOption(opt), UIKit.Accent, 44);
        }
    }
}
