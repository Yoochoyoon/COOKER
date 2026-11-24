using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 튀기기 연출: 웍 안의 기름, 보글보글 기름 거품, 재료를 넣을 때 튀는 기름방울, 김, 지글지글 소리.
// 그림·소리 파일 없이 코드로 그리고 합성한다 (실행 중 AI도 쓰지 않는다).
// 불 세기에 반응한다: 강불은 거품과 기름방울이 많고 소리가 크다. 약불로 오래 두면 기름이 탁해지고 거품이 줄어든다
// (눅눅해지는 규칙은 Cook.cs의 hadSoggy와 같은 기준 Balance.OilGrace를 따라 맞춘다).
// GameFlow.StartCook에서 튀기기일 때 Attach 한 줄로 붙인다 (FireFlame.cs와 같은 방식). 만든 사람: 윤혁진.
public class FryFx : MonoBehaviour
{
    const float RefW = 1920f, RefH = 1080f;                 // 기준 해상도 (픽셀로 계산한 뒤 화면 비율로 바꾼다)
    static readonly Vector2 Center = new Vector2(960f, 580f); // 웍 안쪽 중심 (기준 픽셀, 아래가 0). 불 자리와 같다
    const float OilW = 330f, OilH = 170f;                    // 기름 막 크기

    static Sprite disk, ring;
    static AudioClip sizzleClip, splashClip;

    class P
    {
        public RectTransform rt;
        public Image img;
        public float x, y, vx, vy, life, max, s0, s1, a;
        public bool alive;
    }

    Cook cook;
    Image oil, shine;
    AudioSource sizzle, oneShot;
    Spot[] prev;
    readonly List<P> bubbles = new List<P>(), drops = new List<P>(), steam = new List<P>();
    float bubbleT, dropT, steamT, lowT, vol;

    public static void Attach(Transform root, Cook cook)
    {
        if (cook == null || root == null) return;
        EnsureAssets();
        var go = new GameObject("FryFx", typeof(RectTransform));
        go.transform.SetParent(root, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var fx = go.AddComponent<FryFx>();
        fx.cook = cook;
        fx.prev = new Spot[cook.items.Length];
        for (int i = 0; i < fx.prev.Length; i++) fx.prev[i] = cook.items[i].spot;

        // 기름 막: 웍 그림 위, 재료 그림 아래에 깔린다
        fx.oil = Ellipse(root, "Oil", Center, OilW, OilH, new Color(1f, 0.80f, 0.22f, 0.40f));
        fx.shine = Ellipse(root, "OilShine", Center + new Vector2(-40f, 30f), OilW * 0.45f, OilH * 0.35f, new Color(1f, 1f, 0.9f, 0.14f));
        var item0 = root.Find("Item0");
        int at = item0 != null ? item0.GetSiblingIndex() : root.childCount - 1;
        fx.oil.transform.SetSiblingIndex(at);
        fx.shine.transform.SetSiblingIndex(at + 1);

        // 거품·기름방울·김은 재료 위에 보인다 (이 오브젝트가 마지막에 만들어져 위에 그려진다)
        go.transform.SetAsLastSibling();
        for (int i = 0; i < 16; i++) fx.bubbles.Add(Particle(go.transform, "Bubble", ring));
        for (int i = 0; i < 22; i++) fx.drops.Add(Particle(go.transform, "Drop", disk));
        for (int i = 0; i < 8; i++) fx.steam.Add(Particle(go.transform, "Steam", disk));

        fx.sizzle = go.AddComponent<AudioSource>();
        fx.sizzle.playOnAwake = false;
        fx.sizzle.loop = true;
        fx.sizzle.clip = sizzleClip;
        fx.sizzle.volume = 0f;
        fx.sizzle.Play();
        fx.oneShot = go.AddComponent<AudioSource>();
        fx.oneShot.playOnAwake = false;
    }

    // ---------------------------------------------------------------- 매 프레임
    void Update()
    {
        if (cook == null) return;
        float dt = Time.deltaTime;
        bool active = !cook.finished;
        int inPan = 0;
        for (int i = 0; i < cook.items.Length; i++)
        {
            var sp = cook.items[i].spot;
            if (sp == Spot.Pan) inPan++;
            if (prev[i] == Spot.Tray && sp == Spot.Pan && active) Splash();
            prev[i] = sp;
        }

        // 기름 온도: 약불로 오래 두면 식는다 (눅눅해지는 규칙과 같은 기준)
        if (active && inPan > 0 && cook.fire == 0) lowT += dt;
        float cool = cook.hadSoggy ? 1f : Mathf.Clamp01(lowT / Mathf.Max(0.1f, Balance.OilGrace));
        int fire = Mathf.Clamp(cook.fire, 0, 2);

        // 기름 막 색: 뜨거울수록 맑은 금색, 식으면 탁하고 흐려진다
        var hot = new Color(1f, 0.80f + 0.04f * fire, 0.22f, 0.36f + 0.05f * fire);
        var cold = new Color(0.72f, 0.68f, 0.52f, 0.28f);
        oil.color = Color.Lerp(hot, cold, cool);
        oil.enabled = active;
        float tw = Time.time * (3f + fire * 2f);
        shine.color = new Color(1f, 1f, 0.9f, active ? (0.10f + 0.07f * Mathf.Sin(tw)) * (1f - 0.6f * cool) : 0f);
        float rimWob = 1f + 0.012f * Mathf.Sin(tw * 1.7f) * (fire + 1) * (inPan > 0 ? 1f : 0.3f); // 끓는 기름이 일렁인다
        oil.rectTransform.localScale = new Vector3(rimWob, rimWob, 1f);

        if (active)
        {
            // 거품: 재료가 들어 있으면 많이, 불이 셀수록 많이, 식으면 적게
            float rate = (inPan > 0 ? 3f + 5f * fire : 0.4f + 0.8f * fire) * (1f - 0.7f * cool);
            bubbleT += dt * rate;
            while (bubbleT >= 1f) { bubbleT -= 1f; SpawnBubble(fire); }
            // 기름 튐: 강불에서 재료가 있을 때 가끔 톡톡 튄다
            float dropRate = inPan > 0 ? (fire == 2 ? 2.5f : fire == 1 ? 0.8f : 0f) * (1f - cool) : 0f;
            dropT += dt * dropRate;
            while (dropT >= 1f) { dropT -= 1f; SpawnDrop(1, 1f); }
            // 김: 재료를 튀기는 동안 위로 모락모락
            float steamRate = inPan > 0 ? (fire >= 1 ? 0.8f + fire : 0.4f) : 0f;
            steamT += dt * steamRate;
            while (steamT >= 1f) { steamT -= 1f; SpawnSteam(); }
        }

        Step(bubbles, dt, false);
        Step(drops, dt, true);
        Step(steam, dt, false);

        // 지글지글 소리: 재료가 팬에 있을 때만, 불이 셀수록 크게
        float target = active && inPan > 0 && Time.timeScale > 0f ? Sound.SfxVolume * (0.10f + 0.10f * fire) * (1f - 0.5f * cool) : 0f;
        vol = Mathf.MoveTowards(vol, target, dt * 0.6f);
        sizzle.volume = vol;
    }

    // ---------------------------------------------------------------- 입자
    void SpawnBubble(int fire)
    {
        var p = Free(bubbles);
        if (p == null) return;
        var o = InOil(0.40f);
        p.x = o.x; p.y = o.y; p.vx = Random.Range(-8f, 8f); p.vy = Random.Range(10f, 28f);
        p.life = 0f; p.max = Random.Range(0.45f, 0.9f);
        p.s0 = Random.Range(12f, 20f) + 4f * fire; p.s1 = p.s0 * 1.5f;
        p.a = 0.85f; p.alive = true;
        p.img.color = new Color(1f, 0.96f, 0.72f, 0f);
    }

    // n개의 기름방울. power는 튀는 세기 (재료를 넣을 때 크게)
    void SpawnDrop(int n, float power)
    {
        for (int k = 0; k < n; k++)
        {
            var p = Free(drops);
            if (p == null) return;
            var o = InOil(0.35f);
            p.x = o.x; p.y = o.y;
            p.vx = Random.Range(-170f, 170f) * power; p.vy = Random.Range(260f, 520f) * power;
            p.life = 0f; p.max = 1.2f;
            p.s0 = Random.Range(7f, 13f); p.s1 = p.s0 * 0.7f;
            p.a = 0.95f; p.alive = true;
            p.img.color = new Color(1f, 0.84f, 0.28f, 0f);
        }
    }

    void SpawnSteam()
    {
        var p = Free(steam);
        if (p == null) return;
        var o = InOil(0.30f);
        p.x = o.x; p.y = o.y + OilH * 0.3f; p.vx = Random.Range(-10f, 10f); p.vy = Random.Range(45f, 75f);
        p.life = 0f; p.max = Random.Range(1.0f, 1.6f);
        p.s0 = 40f; p.s1 = 120f;
        p.a = 0.20f; p.alive = true;
        p.img.color = new Color(1f, 1f, 1f, 0f);
    }

    // 재료를 팬에 넣는 순간: 기름이 확 튀고 치익 소리가 난다
    void Splash()
    {
        SpawnDrop(10, 1.15f);
        for (int i = 0; i < 4; i++) SpawnBubble(2);
        if (oneShot != null && splashClip != null && Time.timeScale > 0f) oneShot.PlayOneShot(splashClip, Sound.SfxVolume * 0.7f);
    }

    static Vector2 InOil(float spread)
    {
        float a = Random.value * Mathf.PI * 2f, r = Mathf.Sqrt(Random.value);
        return new Vector2(Center.x + Mathf.Cos(a) * OilW * spread * r, Center.y + Mathf.Sin(a) * OilH * spread * r);
    }

    static P Free(List<P> list)
    {
        foreach (var p in list) if (!p.alive) return p;
        return null;
    }

    static void Step(List<P> list, float dt, bool gravity)
    {
        foreach (var p in list)
        {
            if (!p.alive) continue;
            p.life += dt;
            if (gravity) p.vy -= 1500f * dt;
            p.x += p.vx * dt;
            p.y += p.vy * dt;
            float k = p.life / p.max;
            bool land = gravity && p.vy < 0f && p.y < Center.y - 15f; // 기름방울은 기름 위로 떨어지면 사라진다
            if (k >= 1f || land)
            {
                p.alive = false;
                p.img.enabled = false;
                continue;
            }
            p.img.enabled = true;
            float s = Mathf.Lerp(p.s0, p.s1, k);
            p.rt.anchorMin = p.rt.anchorMax = new Vector2(p.x / RefW, p.y / RefH);
            p.rt.anchoredPosition = Vector2.zero;
            p.rt.sizeDelta = new Vector2(s, s);
            var c = p.img.color;
            c.a = p.a * (k < 0.15f ? k / 0.15f : 1f - Mathf.Clamp01((k - 0.15f) / 0.85f)); // 잠깐 나타났다 서서히 사라진다
            p.img.color = c;
        }
    }

    static P Particle(Transform parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.enabled = false;
        return new P { rt = (RectTransform)go.transform, img = img };
    }

    static Image Ellipse(Transform root, string name, Vector2 c, float w, float h, Color col)
    {
        var rt = UIKit.Box(root, name, (c.x - w / 2f) / RefW, (c.y - h / 2f) / RefH, (c.x + w / 2f) / RefW, (c.y + h / 2f) / RefH);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = disk;
        img.type = Image.Type.Simple;
        img.raycastTarget = false;
        img.color = col;
        return img;
    }

    // ---------------------------------------------------------------- 그림·소리를 코드로 만든다
    static void EnsureAssets()
    {
        if (disk == null) disk = MakeCircle(64, 0f);
        if (ring == null) ring = MakeCircle(64, 0.28f);
        if (sizzleClip == null) sizzleClip = MakeSizzle();
        if (splashClip == null) splashClip = MakeSplash();
    }

    // hole = 0이면 속이 찬 둥근 점(가장자리 부드럽게), 0보다 크면 가운데가 빈 고리(거품)
    static Sprite MakeCircle(int s, float hole)
    {
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = new Vector2(x - s / 2f + 0.5f, y - s / 2f + 0.5f).magnitude / (s / 2f);
                float a = Mathf.Clamp01((1f - d) / 0.12f);
                if (hole > 0f) a *= Mathf.Clamp01((d - (1f - hole)) / 0.10f); // 가장자리만 남긴다
                px[y * s + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
    }

    // 지글지글: 높은 소리의 잡음에 가끔 톡톡 튀는 소리를 섞은 2초짜리 반복 소리
    static AudioClip MakeSizzle()
    {
        const int sr = 22050;
        int n = sr * 2;
        var d = new float[n];
        float prevW = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = Random.value * 2f - 1f;
            float crackle = Random.value < 0.004f ? Random.Range(0.8f, 1.6f) : 0f;
            d[i] = Mathf.Clamp((w - prevW * 0.7f) * 0.45f + crackle * (Random.value * 2f - 1f), -1f, 1f);
            prevW = w;
        }
        var clip = AudioClip.Create("fry_sizzle", n, 1, sr, false);
        clip.SetData(d, 0);
        return clip;
    }

    // 치익: 재료가 기름에 들어갈 때 짧게 터지는 소리
    static AudioClip MakeSplash()
    {
        const int sr = 22050;
        int n = (int)(sr * 0.4f);
        var d = new float[n];
        float prevW = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)sr;
            float env = Mathf.Exp(-t * 9f) * Mathf.Clamp01(t * 200f);
            float w = Random.value * 2f - 1f;
            d[i] = Mathf.Clamp((w - prevW * 0.5f) * 0.8f * env, -1f, 1f);
            prevW = w;
        }
        var clip = AudioClip.Create("fry_splash", n, 1, sr, false);
        clip.SetData(d, 0);
        return clip;
    }
}
