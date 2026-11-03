using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 요리 이름과 한 줄 설명을 Claude에게 받는다. 실패하면 fail이 불리고, 게임은 규칙 이름을 그대로 쓴다.
public static class AiNamer
{
    [Serializable] class Msg { public string role; public string content; }
    [Serializable] class Req { public string model; public int max_tokens; public string system; public Msg[] messages; }
    [Serializable] class Block { public string type; public string text; }
    [Serializable] class Res { public Block[] content; }

    const string SystemPrompt =
        "너는 자취생 요리 게임의 작명가다. 주어진 재료, 조리법, 익힘 상태로 재치 있는 요리 이름과 한 줄 설명을 지어라.\n" +
        "규칙: 이름은 12자 이내, 설명은 40자 이내의 한국어. 재료에 없는 재료는 넣지 마라. 욕설과 비하는 쓰지 마라.\n" +
        "반드시 아래 두 줄만 출력해라.\n이름: ...\n설명: ...";

    public static IEnumerator Request(Dish dish, Action<string, string> ok, Action<string> fail)
    {
        var cfg = AiConfig.Get();
        if (string.IsNullOrEmpty(cfg.anthropicKey)) { fail("키 없음"); yield break; }

        var req = new Req
        {
            model = cfg.anthropicModel,
            max_tokens = 200,
            system = SystemPrompt,
            messages = new[] { new Msg { role = "user", content = Describe(dish) } },
        };
        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(req));

        using (var w = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST"))
        {
            w.uploadHandler = new UploadHandlerRaw(body);
            w.downloadHandler = new DownloadHandlerBuffer();
            w.SetRequestHeader("content-type", "application/json");
            w.SetRequestHeader("x-api-key", cfg.anthropicKey);
            w.SetRequestHeader("anthropic-version", "2023-06-01");
            w.timeout = AiConfig.NameTimeout;
            yield return w.SendWebRequest();

            if (w.result != UnityWebRequest.Result.Success) { fail(w.responseCode + " " + w.error); yield break; }

            string text = null;
            try
            {
                var res = JsonUtility.FromJson<Res>(w.downloadHandler.text);
                var b = res.content == null ? null : res.content.FirstOrDefault(x => x.type == "text");
                if (b != null) text = b.text;
            }
            catch (Exception e) { fail("응답 해석 실패: " + e.Message); yield break; }

            string name, desc;
            if (!Parse(text, out name, out desc)) { fail("형식 오류: " + text); yield break; }
            ok(name, desc);
        }
    }

    static string Describe(Dish d)
    {
        var parts = d.ings.Select((ing, i) => ing.name + "(" + StageText(d.stages != null && i < d.stages.Length ? d.stages[i] : Doneness.Good) + ")");
        return "재료: " + string.Join(", ", parts.ToArray()) + "\n조리법: " + Data.GetMethod(d.method).name + "\n만족도: " + d.satisfaction + "/100";
    }

    static string StageText(Doneness s)
    {
        switch (s)
        {
            case Doneness.Under: return "덜 익음";
            case Doneness.Over: return "푹 익음";
            default: return "알맞게 익음";
        }
    }

    // "이름: ..." "설명: ..." 두 줄을 읽는다.
    static bool Parse(string text, out string name, out string desc)
    {
        name = desc = null;
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("이름:")) name = line.Substring(3).Trim();
            else if (line.StartsWith("설명:")) desc = line.Substring(3).Trim();
        }
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Length > 20) name = name.Substring(0, 20);
        if (desc == null) desc = "";
        if (desc.Length > 60) desc = desc.Substring(0, 60);
        return true;
    }
}
