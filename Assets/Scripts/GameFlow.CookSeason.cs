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
        var dim = UIKit.Box(root, "SeasonDim", 0, 0, 1, 1, new Color(0f, 0f, 0f, 0.6f), false);
        seasonUI = dim.gameObject;
        UIKit.Box(dim, "SeasonPanel", 0.20f, 0.10f, 0.80f, 0.90f, UIKit.Card);
        UIKit.Label(dim, "간 고르기", 0.30f, 0.76f, 0.70f, 0.86f, 56, TextAnchor.MiddleCenter, UIKit.Ink);
        UIKit.Label(dim, "맛을 고르면 완성돼요", 0.30f, 0.69f, 0.70f, 0.76f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
        var choices = cook.SeasonChoices;
        string[] keyNames = { "Q", "W", "E", "R" };
        for (int i = 0; i < choices.Length && i < 4; i++)
        {
            var opt = choices[i];
            float x0 = i % 2 == 0 ? 0.24f : 0.51f;
            float y0 = i < 2 ? 0.37f : 0.15f;
            UIKit.Btn(dim, SeasonName(opt) + " [" + keyNames[i] + "]", x0, y0, x0 + 0.25f, y0 + 0.28f, () => PickSeasonOption(opt), UIKit.Accent, 44);
        }
    }
}
