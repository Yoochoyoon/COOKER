using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// 완성 요리 그림을 AI로 생성한다 (docs/07: 재료·방식·익힘 결과 -> 요리 그림 한 장).
// - 서비스는 OpenAI(OPENAI_API_KEY) -> Pollinations(POLLINATIONS_API_KEY) -> Pollinations 무료 순서로 시도한다.
//   키는 환경 변수나 UserSettings/CookerApiKeys.txt("이름=값")에 넣는다. 무료는 사용량이 차면 거절하므로 키가 있어야 안정적이다.
// - 실패하면 성공할 때까지 뒤에서 계속 다시 시도한다 (Request).
// - 같은 결과(재료+익힘+방식)는 같은 그림을 다시 쓴다: 메모리와 디스크(persistentDataPath/DishPhotos)에 저장.
// - 생성 중이거나 실패하면 재료 색으로 그린 대체 그림을 보여준다.
public static class DishPhoto
{
    const string OpenAiModel = "gpt-image-1";
    const int TimeoutSec = 90;
    const string Style = "cute2"; // 화풍(프롬프트)을 바꾸면 이 값도 바꿔서 예전 저장 그림을 다시 쓰지 않게 한다

    // 게임 속 그림(재료 아이콘·배경)과 같은 귀여운 일러스트 화풍. 키 없는 무료 서비스는 이 화풍을 잘 안 따르고, OpenAI/Pollinations 키가 있으면 잘 따른다.
    const string Cute = "cute kawaii flat 2D cartoon food illustration for a mobile cooking game, thick dark brown outline, soft pastel colors, "
        + "simple clean shading with a small white highlight, centered on a plain solid pale peach background, top-down view of the whole dish, "
        + "no text, no people, no realistic photo.";
    const int Size = 768;
    const float Crop = 0.84f;    // Pollinations 그림 오른쪽 아래의 워터마크를 잘라내려고 가운데만 쓴다

    static readonly Dictionary<string, string> en = new Dictionary<string, string>
    {
        { "egg", "egg" }, { "tofu", "tofu" }, { "onion", "sliced onion" },
        { "potato", "potato" }, { "pork", "pork" }, { "kimchi", "kimchi" },
    };

    static readonly string[] look =
    {
        "completely raw and uncooked",
        "slightly undercooked and pale",
        "perfectly cooked, golden and appetizing",
        "overcooked, dark brown with several blackened burnt spots and charred edges",
        "the food is entirely jet-black charcoal, carbonized, no yellow or white left, covered in a black burnt crust",
    };

    // 이미지 모델이 익힘 상태를 무시하지 않도록 가장 나쁜 상태를 문장 맨 앞에 강조한다
    static readonly string[] lead =
    {
        "A failed cooking attempt, the food is still raw: ",
        "A slightly undercooked homemade dish: ",
        "A delicious homemade dish: ",
        "A slightly burnt, overcooked homemade dish: ",
        "A ruined, completely BURNT inedible dish, everything is black charcoal, smoking, burnt to a crisp: ",
    };
    // 로딩바용 예상 소요 시간(초). 서버가 진행률을 알려 주지 않아 이 값으로 진행을 추정한다.
    public static float ExpectedSeconds
    {
        get { return PaidAllowed && (!string.IsNullOrEmpty(Setting("BAZE_API_KEY")) || !string.IsNullOrEmpty(Setting("OPENAI_API_KEY"))) ? 30f : 15f; }
    }

    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    // 같은 결과면 같은 이름. 파일 이름으로도 쓴다. 예: egg-over_onion-good_Stirfry
    public static string Key(Ingredient[] ings, Doneness[] stages, Method m)
    {
        var parts = ings.Select((i, n) => (Art.Id(i) ?? "x") + "-" + stages[n].ToString().ToLower()).OrderBy(s => s);
        return Style + (PaidAllowed ? "" : "-free") + "_" + string.Join("_", parts) + "_" + m; // 무료로 만든 그림이 유료 그림 자리에 재사용되지 않게 이름을 나눈다
    }

    // 실제 요리 이름이 있는 조합은 그 요리로 그리게 한다 (예: 볶은 달걀 -> 스크램블 에그). 키: 재료 id 정렬 + 방식
    static readonly Dictionary<string, string> dishes = new Dictionary<string, string>
    {
        { "egg/Stirfry", "Korean-style soft scrambled eggs" },
        { "egg/Grill", "a sunny-side up fried egg" },
        { "tofu/Grill", "pan-fried tofu slices (dubu buchim)" },
        { "tofu/Boil", "clear Korean tofu soup" },
        { "onion/Stirfry", "stir-fried sliced onions" },
        { "potato/Grill", "pan-roasted potato slices" },
        { "potato/Boil", "Korean potato soup (gamja guk)" },
        { "pork/Grill", "grilled pork belly slices" },
        { "pork/Stirfry", "stir-fried pork" },
        { "kimchi/Stirfry", "stir-fried kimchi (kimchi bokkeum)" },
        { "kimchi/Boil", "Korean kimchi soup" },
        { "kimchi-pork/Stirfry", "kimchi pork stir-fry (kimchi jeyuk bokkeum)" },
        { "kimchi-tofu/Boil", "kimchi tofu stew (kimchi jjigae)" },
        { "kimchi-onion/Boil", "kimchi stew with onion (kimchi jjigae)" },
        { "egg-onion/Stirfry", "scrambled eggs with stir-fried onion" },
        { "egg-kimchi/Stirfry", "scrambled eggs with stir-fried kimchi" },
        { "egg-tofu/Grill", "pan-fried tofu with a fried egg" },
        { "egg-pork/Stirfry", "stir-fried pork with scrambled eggs" },
    };

    public static string Prompt(Ingredient[] ings, Doneness[] stages, Method m)
    {
        string dishKey = string.Join("-", ings.Select(i => Art.Id(i) ?? "x").OrderBy(s => s)) + "/" + m;
        string dish;
        if (dishes.TryGetValue(dishKey, out dish))
        {
            var worstStage = stages.Contains(Doneness.Burnt) ? Doneness.Burnt : stages.Contains(Doneness.Raw) ? Doneness.Raw : stages.Max();
            return lead[(int)worstStage] + dish + ", " + look[(int)worstStage] + ". "
                + (m == Method.Boil ? "Served in a simple ceramic bowl. " : "Served on a plain white round plate. ") + Cute;
        }
        string how = m == Method.Grill ? "pan-grilled" : m == Method.Stirfry ? "stir-fried" : "boiled, served in a small bowl of soup";
        var what = ings.Select((i, n) =>
        {
            string id = Art.Id(i);
            string name = id != null && en.ContainsKey(id) ? en[id] : i.name;
            return name + " that is " + look[(int)stages[n]];
        });
        var worst = stages.Contains(Doneness.Burnt) ? Doneness.Burnt : stages.Contains(Doneness.Raw) ? Doneness.Raw : stages.Max();
        return lead[(int)worst] + how + " " + string.Join(", and ", what) + ". Served on a plain white round plate. " + Cute;
    }
    // ---- 생성 대기열
    // 같은 그림을 여러 곳에서 기다리면 한 번만 만든다. 요청은 한 번에 하나씩 보내 서비스 제한에 덜 걸리게 하고,
    // 실패하면 성공할 때까지 기다렸다가 다시 시도한다(화면을 넘겨도 뒤에서 계속 만든다). 만들어지면 디스크에 저장된다.
    class Job
    {
        public string key, prompt, path;
        public List<Action<Texture2D>> done = new List<Action<Texture2D>>();
        public List<Action<string>> status = new List<Action<string>>();
    }
    static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
    static readonly Queue<Job> queue = new Queue<Job>();
    static Runner runner;
    class Runner : MonoBehaviour { }

    // 그림을 요청한다. done(그림)은 성공했을 때 한 번 불린다. status(문구)는 기다리는 동안의 상태(재시도 등)를 알려 준다.
    public static void Request(Ingredient[] ings, Doneness[] stages, Method m, Action<Texture2D> done, Action<string> status = null)
    {
        string key = Key(ings, stages, m);
        Texture2D tex;
        if (cache.TryGetValue(key, out tex) && tex != null) { done(tex); return; }

        string path = Path.Combine(Application.persistentDataPath, "DishPhotos", key + ".png");
        if (File.Exists(path))
        {
            tex = new Texture2D(2, 2);
            if (tex.LoadImage(File.ReadAllBytes(path))) { cache[key] = tex; done(tex); return; }
        }

        Job job;
        if (!jobs.TryGetValue(key, out job))
        {
            job = new Job { key = key, prompt = Prompt(ings, stages, m), path = path };
            jobs[key] = job;
            queue.Enqueue(job);
        }
        job.done.Add(done);
        if (status != null) job.status.Add(status);

        if (runner == null)
        {
            var go = new GameObject("DishPhotoRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
            runner.StartCoroutine(Work());
        }
    }

    static IEnumerator Work()
    {
        while (true)
        {
            if (queue.Count == 0) { yield return null; continue; }
            var job = queue.Peek();
            int attempt = 0;
            Texture2D tex = null;
            while (tex == null)
            {
                attempt++;
                float wait = 0f;
                // 쓸 수 있는 서비스를 차례로 시도한다: OpenAI(키) -> Pollinations(키) -> Pollinations(키 없이)
                foreach (var provider in Providers())
                {
                    byte[] bytes = null;
                    float retryAfter = 0f;
                    yield return provider(job, (b, ra) => { bytes = b; retryAfter = ra; });
                    wait = Mathf.Max(wait, retryAfter);
                    if (bytes == null) continue;
                    var t = new Texture2D(2, 2);
                    if (t.LoadImage(bytes)) { tex = t; break; }
                    UnityEngine.Object.Destroy(t);
                }
                if (tex != null) break;
                // 모두 실패: 점점 길게 기다렸다가 다시 (최대 60초, 서버가 알려 준 대기 시간이 더 길면 그만큼)
                wait = Mathf.Max(wait, Mathf.Min(60f, 5f * Mathf.Pow(2f, Mathf.Min(attempt - 1, 4))));
                for (float left = wait; left > 0f; left -= Time.unscaledDeltaTime)
                {
                    Status(job, "그림 서버가 바빠서 다시 시도하는 중... (" + attempt + "번 실패, " + Mathf.CeilToInt(left) + "초 뒤)");
                    yield return null;
                }
                Status(job, "다시 그리는 중... (" + (attempt + 1) + "번째 시도)");
            }

            cache[job.key] = tex;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(job.path));
                File.WriteAllBytes(job.path, tex.EncodeToPNG());
            }
            catch (Exception e) { Debug.LogWarning("요리 그림 저장 실패: " + e.Message); }
            queue.Dequeue();
            jobs.Remove(job.key);
            foreach (var d in job.done) { try { d(tex); } catch (Exception e) { Debug.LogException(e); } }
        }
    }

    static void Status(Job job, string msg)
    {
        foreach (var s in job.status) { try { s(msg); } catch (Exception) { } }
    }

    delegate IEnumerator Provider(Job job, Action<byte[], float> done);

    // 유료 서비스를 써도 되는지. 유니티 에디터에서 테스트하는 동안은 크레딧을 아끼려고 무료 서비스만 쓴다.
    // 에디터에서도 유료 결과를 보고 싶으면 키 파일에 COOKER_PAID_IN_EDITOR=1 을 적는다. 빌드(실제 플레이)는 항상 유료를 쓴다.
    static bool PaidAllowed
    {
        get { return !Application.isEditor || Setting("COOKER_PAID_IN_EDITOR") == "1"; }
    }

    static IEnumerable<Provider> Providers()
    {
        if (PaidAllowed)
        {
            if (!string.IsNullOrEmpty(Setting("BAZE_API_KEY"))) yield return Baze;
            if (!string.IsNullOrEmpty(Setting("OPENAI_API_KEY"))) yield return OpenAi;
            if (!string.IsNullOrEmpty(Setting("HF_TOKEN"))) yield return HuggingFace;
            if (!string.IsNullOrEmpty(Setting("POLLINATIONS_API_KEY"))) yield return PollinationsKey;
        }
        yield return PollinationsFree;
    }

    // BAZE API Gateway. 모델은 BAZE_IMAGE_MODEL로 바꿀 수 있고, 기본은 gpt-image-2.5-sunburst(한 장 약 11크레딧, 탄 표현·화풍이 가장 좋았다).
    // 가장 싼 모델은 fal-ai/fast-sdxl(5크레딧)이지만 "탄 요리"를 잘 못 그린다.
    // 응답의 data[0].url은 base64 data URL이거나 https 주소다. fal 계열은 오래 걸리면 processing + operation_id로 오므로 완성될 때까지 물어본다.
    const string BazeBase = "https://factchat-cloud.mindlogic.ai/v1/gateway/images/generate/";
    [Serializable] class BazeItem { public string url; }
    [Serializable] class BazeResp { public BazeItem[] data; public string status; public string operation_id; }

    static IEnumerator Baze(Job job, Action<byte[], float> done)
    {
        string model = Setting("BAZE_IMAGE_MODEL");
        if (string.IsNullOrEmpty(model)) model = "gpt-image-2.5-sunburst";
        // GPT 이미지 모델은 size로, 나머지는 aspect_ratio로 정사각형을 요청한다 (기본은 가로로 긴 그림이라 사진 틀에서 찌그러진다)
        string square = model.Contains("gpt-image") ? "\"size\":\"1024x1024\"" : "\"aspect_ratio\":\"1:1\"";
        string body = "{\"model\":" + JsonString(model) + ",\"prompt\":" + JsonString(job.prompt) + "," + square + "}";
        BazeResp resp = null;
        using (var req = BazeRequest(BazeBase, "POST", body))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("요리 그림 생성 실패 (BAZE): " + req.responseCode + " " + req.error);
                done(null, RetryAfter(req));
                yield break;
            }
            resp = JsonUtility.FromJson<BazeResp>(req.downloadHandler.text);
        }
        for (int poll = 0; poll < 30 && resp != null && (resp.data == null || resp.data.Length == 0) && !string.IsNullOrEmpty(resp.operation_id); poll++)
        {
            yield return new WaitForSecondsRealtime(2f);
            using (var req = BazeRequest(BazeBase + resp.operation_id + "/?model=" + UnityWebRequest.EscapeURL(model), "GET", null))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) resp = JsonUtility.FromJson<BazeResp>(req.downloadHandler.text);
            }
        }
        string url = resp != null && resp.data != null && resp.data.Length > 0 ? resp.data[0].url : null;
        if (string.IsNullOrEmpty(url)) { Debug.LogWarning("요리 그림 생성 실패 (BAZE): 그림이 오지 않았다"); done(null, 0f); yield break; }
        if (url.StartsWith("data:"))
        {
            int comma = url.IndexOf(',');
            byte[] bytes = null;
            try { bytes = Convert.FromBase64String(url.Substring(comma + 1)); } catch (Exception) { }
            done(bytes, 0f);
            yield break;
        }
        using (var req = UnityWebRequest.Get(url))
            yield return Download(req, "BAZE 그림 내려받기", done);
    }

    static UnityWebRequest BazeRequest(string url, string method, string json)
    {
        var req = new UnityWebRequest(url, method);
        if (json != null)
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.SetRequestHeader("Content-Type", "application/json");
        }
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Authorization", "Bearer " + Setting("BAZE_API_KEY"));
        req.SetRequestHeader("User-Agent", "cooker-game/1.0");
        req.timeout = TimeoutSec;
        return req;
    }

    // Hugging Face (무료 토큰): FLUX.1-schnell. 화풍 지시를 잘 따른다. 무료는 속도 제한이 있어 실패하면 다음 서비스로 넘어간다.
    static IEnumerator HuggingFace(Job job, Action<byte[], float> done)
    {
        string body = "{\"inputs\":" + JsonString(job.prompt) + ",\"parameters\":{\"width\":" + Size + ",\"height\":" + Size + ",\"seed\":" + Seed(job.key) + "}}";
        using (var req = new UnityWebRequest("https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-schnell", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "image/png");
            req.SetRequestHeader("Authorization", "Bearer " + Setting("HF_TOKEN"));
            yield return Download(req, "Hugging Face", done);
        }
    }

    static string JsonString(string s)
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

    // API 키: 환경 변수, 또는 키 파일(에디터: UserSettings/CookerApiKeys.txt, 빌드: persistentDataPath/CookerApiKeys.txt)의 "이름=값" 줄
    static Dictionary<string, string> fileKeys;
    public static string Setting(string name)
    {
        string v = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(v)) return v.Trim();
        if (fileKeys == null)
        {
            fileKeys = new Dictionary<string, string>();
            var files = new[]
            {
                Path.Combine(Path.GetDirectoryName(Application.dataPath), "UserSettings", "CookerApiKeys.txt"),
                Path.Combine(Application.persistentDataPath, "CookerApiKeys.txt"),
            };
            foreach (var f in files)
            {
                if (!File.Exists(f)) continue;
                foreach (var line in File.ReadAllLines(f))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.TrimStart().StartsWith("#")) continue;
                    string k = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1).Trim();
                    if (val.Length > 0 && !fileKeys.ContainsKey(k)) fileKeys[k] = val;
                }
            }
        }
        string fv;
        return fileKeys.TryGetValue(name, out fv) ? fv : null;
    }

    static int Seed(string key)
    {
        int seed = 0;
        foreach (char c in key) seed = unchecked(seed * 31 + c); // 같은 결과는 같은 그림이 나오도록 시드 고정
        return seed & 0x7fffffff;
    }

    static string Escape(string prompt) { return UnityWebRequest.EscapeURL(prompt).Replace("+", "%20"); }

    // 서버가 "몇 초 뒤에 다시"라고 알려 주면(Retry-After) 그만큼 기다린다
    static float RetryAfter(UnityWebRequest req)
    {
        float s;
        var h = req.GetResponseHeader("Retry-After");
        return h != null && float.TryParse(h, out s) ? s : 0f;
    }

    static IEnumerator Download(UnityWebRequest req, string label, Action<byte[], float> done)
    {
        req.timeout = TimeoutSec;
        yield return req.SendWebRequest();
        if (req.result == UnityWebRequest.Result.Success && req.downloadHandler.data != null && req.downloadHandler.data.Length > 1000)
            done(req.downloadHandler.data, 0f);
        else
        {
            Debug.LogWarning("요리 그림 생성 실패 (" + label + "): " + req.responseCode + " " + req.error);
            done(null, RetryAfter(req));
        }
    }

    // Pollinations 새 API (키 필요, 가장 안정적)
    static IEnumerator PollinationsKey(Job job, Action<byte[], float> done)
    {
        string url = "https://gen.pollinations.ai/image/" + Escape(job.prompt) + "?width=" + Size + "&height=" + Size + "&seed=" + Seed(job.key);
        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Authorization", "Bearer " + Setting("POLLINATIONS_API_KEY"));
            yield return Download(req, "Pollinations 키", done);
        }
    }

    // Pollinations 옛 주소 (키 없이). 무료 사용량이 차면 402로 거절하므로 옵션을 줄여 가며 두 번 시도한다
    static IEnumerator PollinationsFree(Job job, Action<byte[], float> done)
    {
        string[] options =
        {
            "&model=flux&nologo=true&enhance=false",
            "", // 기본 모델, 워터마크 허용 (가운데를 잘라 쓴다)
        };
        float wait = 0f;
        foreach (var opt in options)
        {
            string url = "https://image.pollinations.ai/prompt/" + Escape(job.prompt) + "?width=" + Size + "&height=" + Size + "&seed=" + Seed(job.key) + opt;
            byte[] got = null;
            using (var req = UnityWebRequest.Get(url))
                yield return Download(req, "Pollinations 무료", (b, ra) => { got = b; wait = Mathf.Max(wait, ra); });
            if (got == null) continue;
            var t = new Texture2D(2, 2);
            if (t.LoadImage(got)) { t = CropCenter(t, Crop); got = t.EncodeToPNG(); }
            UnityEngine.Object.Destroy(t);
            done(got, 0f);
            yield break;
        }
        done(null, wait);
    }
    static Texture2D CropCenter(Texture2D src, float keep)
    {
        int w = Mathf.RoundToInt(src.width * keep), h = Mathf.RoundToInt(src.height * keep);
        var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
        dst.SetPixels(src.GetPixels((src.width - w) / 2, (src.height - h) / 2, w, h));
        dst.Apply();
        UnityEngine.Object.Destroy(src);
        return dst;
    }

    [Serializable] class OpenAiReq { public string model; public string prompt; public string size; public int n = 1; }
    [Serializable] class OpenAiRes { public OpenAiImg[] data; }
    [Serializable] class OpenAiImg { public string b64_json; }

    static IEnumerator OpenAi(Job job, Action<byte[], float> done)
    {
        var body = JsonUtility.ToJson(new OpenAiReq { model = OpenAiModel, prompt = job.prompt, size = "1024x1024" });
        using (var req = new UnityWebRequest("https://api.openai.com/v1/images/generations", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + Setting("OPENAI_API_KEY"));
            req.timeout = TimeoutSec;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("요리 그림 생성 실패 (OpenAI): " + req.responseCode + " " + req.error + " " + req.downloadHandler.text);
                done(null, RetryAfter(req));
                yield break;
            }
            var res = JsonUtility.FromJson<OpenAiRes>(req.downloadHandler.text);
            if (res == null || res.data == null || res.data.Length == 0 || string.IsNullOrEmpty(res.data[0].b64_json)) { done(null, 0f); yield break; }
            done(Convert.FromBase64String(res.data[0].b64_json), 0f);
        }
    }

    // 생성 전/실패 시 대체 그림: 접시 위에 재료를 익힘 단계 색으로 그린다. 탄 재료는 연기도 그린다.
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
