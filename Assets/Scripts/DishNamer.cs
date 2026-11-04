using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 완성 요리의 진짜 음식 이름을 AI에게 받는다 (예: 달걀+양파 볶기 -> "양파달걀볶음").
// 먼저 Recipes.NameOf의 이름을 보여 주고, AI 이름이 오면 바꿔 끼운다. 실패하면 그 이름을 그대로 쓴다.
// 서비스: Claude(ANTHROPIC_API_KEY) -> OpenAI(OPENAI_API_KEY) -> Pollinations 텍스트(키 없이) 순서로 한 번씩 시도한다.
// 키는 DishPhoto와 같은 곳(환경 변수 또는 UserSettings/CookerApiKeys.txt)에서 읽는다.
public static class DishNamer
{
    const int TimeoutSec = 15;
    static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
    static string CachePath { get { return Path.Combine(Application.persistentDataPath, "DishNames.txt"); } }

    static readonly string[] stageWord = { "날것", "덜 익음", "알맞게 익음", "많이 익음", "탐" };

    public static void Request(MonoBehaviour host, Ingredient[] ings, Doneness[] stages, Method m, Action<string> done)
    {
        string key = string.Join("+", ings.Select((i, n) => i.name + "-" + stages[n]).OrderBy(s => s)) + "/" + m;
        LoadCache();
        string name;
        if (cache.TryGetValue(key, out name)) { done(name); return; } // 같은 요리는 같은 이름
        host.StartCoroutine(Work(key, Prompt(ings, stages, m), done));
    }

    static string Prompt(Ingredient[] ings, Doneness[] stages, Method m)
    {
        var parts = ings.Select((i, n) => i.name + "(" + stageWord[(int)stages[n]] + ")");
        return "자취생이 " + string.Join(", ", parts) + "를 " + Data.GetMethod(m).name + "로 만든 요리가 있다. "
            + "한국에서 실제로 부르는 음식 이름 하나로 답해라 (예: 달걀프라이, 김치볶음, 감잣국, 김치두부찌개). "
            + "많이 익었으면 '바삭한', '푹 익은' 같은 말을 앞에 붙여도 된다. "
            + "이름만, 따옴표·설명·마침표 없이 12자 이내로.";
    }

    static IEnumerator Work(string key, string prompt, Action<string> done)
    {
        string result = null;
        if (!string.IsNullOrEmpty(DishPhoto.Setting("ANTHROPIC_API_KEY")))
            yield return Claude(prompt, r => result = r);
        if (result == null && !string.IsNullOrEmpty(DishPhoto.Setting("OPENAI_API_KEY")))
            yield return OpenAi(prompt, r => result = r);
        if (result == null)
            yield return Pollinations(prompt, r => result = r);
        if (result == null) yield break; // 실패: 처음 보여 준 이름을 그대로 쓴다
        cache[key] = result;
        try { File.AppendAllText(CachePath, key + "\t" + result + "\n"); } catch (Exception) { }
        done(result);
    }

    // 모델 답에서 이름만 남긴다 (첫 줄, 따옴표·마침표·"이름:" 같은 머리말 제거)
    static string Clean(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        string s = raw.Trim().Split('\n')[0].Trim();
        int colon = s.IndexOf(':');
        if (colon >= 0 && colon < s.Length - 1) s = s.Substring(colon + 1);
        s = s.Trim(' ', '"', '\'', '“', '”', '‘', '’', '.', '*', '`', '「', '」');
        if (s.Length == 0 || s.Length > 16) return null;
        return s;
    }

    static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    [Serializable] class ClaudeText { public string text; }
    [Serializable] class ClaudeResp { public ClaudeText[] content; }

    static IEnumerator Claude(string prompt, Action<string> done)
    {
        string body = "{\"model\":\"claude-haiku-5-5\",\"max_tokens\":40,\"messages\":[{\"role\":\"user\",\"content\":" + Json(prompt) + "}]}";
        using (var req = Post("https://api.anthropic.com/v1/messages", body))
        {
            req.SetRequestHeader("x-api-key", DishPhoto.Setting("ANTHROPIC_API_KEY"));
            req.SetRequestHeader("anthropic-version", "2023-06-01");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning("요리 이름 실패 (Claude): " + req.responseCode + " " + req.error); yield break; }
            var r = JsonUtility.FromJson<ClaudeResp>(req.downloadHandler.text);
            if (r != null && r.content != null && r.content.Length > 0) done(Clean(r.content[0].text));
        }
    }

    [Serializable] class OaMsg { public string content; }
    [Serializable] class OaChoice { public OaMsg message; }
    [Serializable] class OaResp { public OaChoice[] choices; }

    static IEnumerator OpenAi(string prompt, Action<string> done)
    {
        string body = "{\"model\":\"gpt-4o-mini\",\"max_tokens\":40,\"messages\":[{\"role\":\"user\",\"content\":" + Json(prompt) + "}]}";
        using (var req = Post("https://api.openai.com/v1/chat/completions", body))
        {
            req.SetRequestHeader("Authorization", "Bearer " + DishPhoto.Setting("OPENAI_API_KEY"));
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning("요리 이름 실패 (OpenAI): " + req.responseCode + " " + req.error); yield break; }
            var r = JsonUtility.FromJson<OaResp>(req.downloadHandler.text);
            if (r != null && r.choices != null && r.choices.Length > 0 && r.choices[0].message != null) done(Clean(r.choices[0].message.content));
        }
    }

    // 키 없이 쓰는 무료 텍스트 서비스 (답이 그냥 글자로 온다)
    static IEnumerator Pollinations(string prompt, Action<string> done)
    {
        string url = "https://text.pollinations.ai/" + UnityWebRequest.EscapeURL(prompt).Replace("+", "%20");
        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning("요리 이름 실패 (Pollinations): " + req.responseCode + " " + req.error); yield break; }
            done(Clean(req.downloadHandler.text));
        }
    }

    static UnityWebRequest Post(string url, string json)
    {
        var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("content-type", "application/json");
        req.timeout = TimeoutSec;
        return req;
    }

    static bool loaded;
    static void LoadCache()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            if (!File.Exists(CachePath)) return;
            foreach (var line in File.ReadAllLines(CachePath))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0) cache[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
        }
        catch (Exception) { }
    }
}
