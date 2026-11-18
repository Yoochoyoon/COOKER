using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 요리 그림 도우미. 실행 중에는 AI를 호출하지 않는다 (CLAUDE.md).
// - Load: Assets/Resources/AiDishes/ 에 미리 만들어 둔 요리 사진을 이름으로 읽는다. 없으면 null.
// - Placeholder: 그림이 없을 때 접시 위에 재료를 익힘 단계 색으로 그리는 대체 그림.
public static class DishArt
{
    const string Style = "cute2"; // 파일 이름 앞부분. 미리 만든 그림 파일도 이 이름을 쓴다

    // 요리 키: 재료 id와 익힘을 알파벳 순서로 이어 붙인다. 예: cute2_egg-good_tofu-good_Grill
    public static string Key(Ingredient[] ings, Doneness[] stages, Method m)
    {
        var parts = ings.Select((i, n) => (Art.Id(i) ?? "x") + "-" + stages[n].ToString().ToLower()).OrderBy(s => s);
        return Style + "_" + string.Join("_", parts) + "_" + m;
    }

    // 미리 만든 요리 사진. 없으면 null (호출하는 쪽이 대체 그림을 쓴다)
    public static Texture2D Load(Ingredient[] ings, Doneness[] stages, Method m)
    {
        return Resources.Load<Texture2D>("AiDishes/" + Key(ings, stages, m));
    }

    // 대체 그림: 접시 위에 재료를 익힘 단계 색으로 그린다. 탄 재료는 연기도 그린다.
    public static Texture2D Placeholder(Ingredient[] ings, Doneness[] stages)
    {
        const int S = 256;
        var tex = new Texture2D(S, S);
        var px = new Color[S * S];
        var bg = UIKit.Card;
        var plate = Color.white;
        var rim = new Color(0.85f, 0.82f, 0.75f);
        var c = new Vector2(S / 2f, S / 2f);
        // 재료 덩어리 위치를 원 둘레에 고르게 놓는다
        var blobs = new List<KeyValuePair<Vector2, Color>>();
        var rnd = new System.Random(Key(ings, stages, Method.Grill).GetHashCode());
        for (int i = 0; i < ings.Length; i++)
        {
            float a = Mathf.PI * 2f * i / Mathf.Max(1, ings.Length);
            float r = ings.Length == 1 ? 0f : 38f;
            for (int k = 0; k < 5; k++)
            {
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r + new Vector2(rnd.Next(-26, 27), rnd.Next(-26, 27));
                blobs.Add(new KeyValuePair<Vector2, Color>(p, Cook.ToneOf(ings[i], stages[i])));
            }
        }
        bool smoke = stages.Any(s => s == Doneness.Burnt);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var p = new Vector2(x, y);
                float dc = Vector2.Distance(p, c);
                Color col = dc < 110f ? (dc > 98f ? rim : plate) : bg;
                foreach (var b in blobs)
                    if (Vector2.Distance(p, b.Key) < 22f) col = b.Value;
                if (smoke && y > 200 && Mathf.Abs(x - c.x - Mathf.Sin(y * 0.12f) * 14f) < 6f) col = Color.Lerp(col, Color.gray, 0.7f);
                px[y * S + x] = col;
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}
