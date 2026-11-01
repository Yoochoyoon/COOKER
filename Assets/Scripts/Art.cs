using System.Collections.Generic;
using UnityEngine;

// 그림 파일을 찾아 주는 도우미. 파일이 없으면 null을 돌려주고, 게임은 단색으로 대신 그린다.
// 규칙: Assets/Resources/Art/<재료id>_<상태>.png  (상태: raw=날것, cooked=알맞게 익음, burnt=탄 것)
// 재료 id: egg, tofu, onion, potato, pork, kimchi  (자세한 건 docs/13아트가이드.md)
public static class Art
{
    static readonly Dictionary<string, string> ids = new Dictionary<string, string>
    {
        { "달걀", "egg" }, { "두부", "tofu" }, { "양파", "onion" },
        { "감자", "potato" }, { "돼지고기", "pork" }, { "김치", "kimchi" },
    };

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

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

    // state: raw / cooked / burnt
    public static Sprite Get(Ingredient i, string state)
    {
        string id = Id(i);
        return id == null ? null : Load(id + "_" + state);
    }

    // 세 장이 모두 있어야 그림으로 그린다 (일부만 있으면 단색으로 대신해 어색한 화면을 막는다)
    public static bool HasSet(Ingredient i)
    {
        return Get(i, "raw") != null && Get(i, "cooked") != null && Get(i, "burnt") != null;
    }
}
