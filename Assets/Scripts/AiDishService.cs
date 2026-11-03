using System;
using System.Collections.Generic;
using UnityEngine;

// 요리가 끝나면 이름·설명 요청과 그림 요청을 동시에 시작한다. 결과가 오면 changed로 알린다.
// 모든 실패(키 없음, 인터넷 없음, 시간 초과)는 조용히 넘어가고, 게임은 규칙 이름과 임시 그림을 그대로 쓴다.
public static class AiDishService
{
    class Named { public string name, desc; }
    static readonly Dictionary<string, Named> names = new Dictionary<string, Named>();
    static readonly Dictionary<string, Sprite> images = new Dictionary<string, Sprite>();

    public static void Start(MonoBehaviour host, Dish d, Action<Dish> changed)
    {
        if (d.ings == null || d.ings.Length == 0) return;
        string key = AiDishImage.KeyOf(d);

        // 이름과 설명: 같은 조합은 다시 묻지 않는다.
        Named n;
        if (names.TryGetValue(key, out n)) { d.name = n.name; d.desc = n.desc; }
        else if (AiConfig.NameReady)
        {
            d.namePending = true;
            host.StartCoroutine(AiNamer.Request(d,
                (nm, ds) => { names[key] = new Named { name = nm, desc = ds }; d.name = nm; d.desc = ds; d.namePending = false; changed(d); },
                err => { Debug.LogWarning("[AI] 이름 실패: " + err); d.namePending = false; changed(d); }));
        }

        // 그림: 메모리, 미리 만든 그림, 저장된 그림 순으로 찾고 없으면 생성한다.
        Sprite sp;
        if (images.TryGetValue(key, out sp)) { d.image = sp; return; }
        sp = AiDishImage.LoadCached(key);
        if (sp != null) { images[key] = sp; d.image = sp; return; }
        if (!AiDishImage.Ready) return;
        d.imagePending = true;
        host.StartCoroutine(AiDishImage.Request(d,
            s => { images[key] = s; d.image = s; d.imagePending = false; changed(d); },
            err => { Debug.LogWarning("[AI] 그림 실패: " + err); d.imagePending = false; changed(d); }));
    }
}
