using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

// 요리 그림 생성. 어떤 이미지 모델을 쓸지는 docs/15의 비교 시험으로 정한다.
// 모델이 정해지면 IImageProvider를 구현한 클래스를 하나 만들고 Provider()에 이름을 추가하면 된다.
public interface IImageProvider
{
    // PNG 바이트를 ok로 넘기거나, 실패하면 fail로 이유를 넘긴다. 제한 시간은 AiConfig.ImageTimeout.
    IEnumerator Generate(string prompt, Action<byte[]> ok, Action<string> fail);
}

public static class AiDishImage
{
    // 모델이 정해지면 여기에 추가: case "이름": return new 새Provider();
    static IImageProvider Provider()
    {
        switch (AiConfig.Get().imageProvider)
        {
            default: return null;
        }
    }

    public static bool Ready { get { return Provider() != null; } }

    // 캐시와 미리 만든 그림의 파일 이름. 예: egg-good+kimchi-good_stirfry
    public static string KeyOf(Dish d)
    {
        var pairs = d.ings.Select((ing, i) => (Art.Id(ing) ?? ing.name) + "-" + (d.stages != null && i < d.stages.Length ? d.stages[i] : Doneness.Good).ToString().ToLower())
            .OrderBy(s => s).ToArray();
        return string.Join("+", pairs) + "_" + d.method.ToString().ToLower();
    }

    static string CacheDir { get { return Path.Combine(Application.persistentDataPath, "ai_dishes"); } }

    // 미리 만들어 넣은 그림(Resources/AiDishes/<키>.png)을 먼저, 그다음 이전에 생성해 저장한 그림을 찾는다.
    public static Sprite LoadCached(string key)
    {
        var pre = Resources.Load<Sprite>("AiDishes/" + key);
        if (pre != null) return pre;
        try
        {
            string path = Path.Combine(CacheDir, key + ".png");
            if (File.Exists(path)) return ToSprite(File.ReadAllBytes(path));
        }
        catch (Exception e) { Debug.LogWarning("[AI] 저장된 그림을 읽지 못했다: " + e.Message); }
        return null;
    }

    public static IEnumerator Request(Dish d, Action<Sprite> ok, Action<string> fail)
    {
        var provider = Provider();
        if (provider == null) { fail("이미지 모델이 설정되지 않았다"); yield break; }

        byte[] bytes = null;
        string error = null;
        yield return provider.Generate(Prompt(d), b => bytes = b, e => error = e);
        if (bytes == null) { fail(error ?? "빈 응답"); yield break; }

        var sprite = ToSprite(bytes);
        if (sprite == null) { fail("그림 파일을 읽지 못했다"); yield break; }
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(Path.Combine(CacheDir, KeyOf(d) + ".png"), bytes);
        }
        catch (Exception e) { Debug.LogWarning("[AI] 그림을 저장하지 못했다: " + e.Message); }
        ok(sprite);
    }

    // docs/15의 프롬프트 틀
    public static string Prompt(Dish d)
    {
        var worst = d.stages == null || d.stages.Length == 0 ? Doneness.Good : d.stages.OrderBy(s => Math.Abs((int)s - (int)Doneness.Good)).Last();
        string state = worst == Doneness.Over ? "overcooked, dark edges" : worst == Doneness.Under ? "undercooked, pale" : "perfectly cooked";
        string how = d.method == Method.Grill ? "pan-fried" : d.method == Method.Stirfry ? "stir-fried" : "boiled in a soup";
        string food = string.Join(" and ", d.ings.Select(i => Art.Id(i) ?? i.name).ToArray());
        return "Hand-drawn 2D cartoon food illustration, thick outlines, warm flat colors, Korean home cooking, " +
               "simple white background, no text or letters. " +
               "Dish: " + how + " " + food + ", " + state + ", one plate, centered, no hands.";
    }

    static Sprite ToSprite(byte[] png)
    {
        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(png)) return null;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }
}
