using System.Collections.Generic;
using UnityEngine;

// 그림 파일을 찾아 주는 도우미. 파일이 없으면 null을 돌려주고, 게임은 단색으로 대신 그린다.
// 규칙: Assets/Resources/Art/<재료id>_<단계>.png
// 단계: raw=날것, under=적게 익힘, good=알맞음, over=많이 익힘, burnt=탄 것
// 재료 id: egg, tofu, onion, potato, pork, kimchi  (자세한 건 docs/13아트가이드.md)
public static class Art
{
    static readonly Dictionary<string, string> ids = new Dictionary<string, string>
    {
        { "달걀", "egg" }, { "두부", "tofu" }, { "양파", "onion" },
        { "감자", "potato" }, { "돼지고기", "pork" }, { "김치", "kimchi" }, { "새우", "shrimp" },
    };

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // 에디터에서 도메인 리로드를 끄고 Play하면 static이 남는다. 이전 실행에서 "없음"으로 기억한 그림이 남지 않게 Play마다 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() { cache.Clear(); }

    public static string Id(Ingredient i)
    {
        string id;
        return ids.TryGetValue(i.name, out id) ? id : null;
    }

    public static Sprite Load(string file)
    {
        Sprite s;
        if (cache.TryGetValue(file, out s)) return s;
        s = Resources.Load<Sprite>("Art/" + file);
        cache[file] = s; // 없는 파일(null)도 기억해서 매번 찾지 않는다
        return s;
    }

    // 5단계 파일 이름 (Doneness 순서와 같다)
    static readonly string[] stages = { "raw", "under", "good", "over", "burnt" };

    public static Sprite Get(Ingredient i, Doneness stage)
    {
        string id = Id(i);
        return id == null ? null : Load(id + "_" + stages[(int)stage]);
    }

    // 장보기 줄·장바구니 전용 아이콘(<id>_shop.png). 조리 중 쓰는 5단계 그림과는 다른 그림이다.
    public static Sprite ShopIcon(Ingredient i)
    {
        string id = Id(i);
        return id == null ? null : Load(id + "_shop");
    }

    // 다섯 장이 모두 있어야 그림으로 그린다 (일부만 있으면 단색으로 대신해 어색한 화면을 막는다)
    public static bool HasSet(Ingredient i)
    {
        for (int s = 0; s < stages.Length; s++)
            if (Get(i, (Doneness)s) == null) return false;
        return true;
    }
}
