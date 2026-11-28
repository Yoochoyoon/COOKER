using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// 한 판 전체 흐름: 타이틀 -> 장보기 -> (하루 선택 -> 조리 -> 식사) x 7 -> 결산.
// 씬에 아무것도 놓지 않아도 재생하면 이 스크립트가 스스로 만들어지고 UI를 코드로 그린다.

public partial class GameFlow : MonoBehaviour
{
    // ---------------------------------------------------------------- 도감
    // 만들 수 있는 모든 조합(재료 1~2개 x 방식 3개)을 칸으로 보여 주고, 만든 것만 채운다.
    // 칸의 key는 요리를 만들 때 쓰는 Dish.key와 같은 규칙이다. 재료 3개 이상으로 만든 요리는 만든 뒤에 칸이 덧붙는다.
    class DexSlot { public string key; public Ingredient[] ings; public Method method; }

    static List<DexSlot> DexSlots()
    {

        var dexSlots = new List<DexSlot>(); // 해금된 재료·방식만. 튀김 칸은 해금 후에 나타난다
        var combos = new List<Ingredient[]>();
        var pool = Data.Active;
        foreach (var a in pool) combos.Add(new[] { a });
        for (int i = 0; i < pool.Length; i++)
            for (int j = i + 1; j < pool.Length; j++) combos.Add(new[] { pool[i], pool[j] });
        foreach (var md in Data.ActiveMethods)
            foreach (var c in combos)
                dexSlots.Add(new DexSlot
                {
                    ings = c,
                    method = md.id,
                    key = string.Join("+", c.Select(i => i.name).OrderBy(s => s)) + "/" + md.id,
                });
        return dexSlots;
    }

    // 기본 칸 + 칸이 없는 조합으로 만든 요리(재료 3개 이상 등)
    static List<DexSlot> AllDexSlots()
    {
        var keys = new HashSet<string>(DexSlots().Select(s => s.key));
        return DexSlots().Concat(dex.Values.Where(d => !keys.Contains(d.key))
            .Select(d => new DexSlot { key = d.key, ings = d.ings, method = d.method })).ToList();
    }

    static readonly string[] StageName = { "날것", "덜 익음", "알맞음", "너무 익음", "탐" };
    // ---------------------------------------------------------------- 칭호
    // 칭호 목록. 도감 요리로 조건을 채우면 열리고, 하나만 장착한다. 장착은 타이틀·결산 화면에서만 바꿀 수 있다(주가 끝난 뒤).
    void ShowTitles()
    {
        CloseModal();
        bool canEquip = scr == Scr.Title || scr == Scr.Lobby || scr == Scr.Result;
        var dishes = dex.Values.ToList();
        modal = UIKit.Box(canvasRT, "Titles", 0.06f, 0.10f, 0.94f, 0.90f, UIKit.Card, true).gameObject;
        modal.transform.localScale = Vector3.one * 0.97f; // 가장자리 여백
        var m = modal.transform;
        // 아직 해금되지 않은 튀김 관련 칭호는 목록에 보이지 않는다
        var shownTitles = Titles.All.Where(t => t.id != "frypro" || Data.Unlocked(Method.Fry)).ToArray();
        UIKit.Label(m, "칭호 (" + Titles.EarnedCount + " / " + shownTitles.Length + ")", 0.04f, 0.88f, 0.5f, 0.98f, 60, TextAnchor.MiddleLeft, UIKit.Accent);
        var rep = Titles.Representative;
        UIKit.Label(m, rep == null ? "대표 칭호가 없다" : "대표 칭호: [" + rep.name + "]", 0.45f, 0.88f, 0.74f, 0.98f, 30, TextAnchor.MiddleRight, UIKit.Muted);
        UIX.CloseX(m, 0.915f, 0.895f, 0.975f, 0.975f, CloseModal);
        for (int i = 0; i < shownTitles.Length; i++)
        {
            var t = shownTitles[i];
            bool has = Titles.Has(t.id);
            float top = 0.86f - i * 0.1f, bot = top - 0.08f;
            var row = UIKit.Box(m, "Row" + i, 0.03f, bot, 0.97f, top, has ? Color.white : new Color(0.88f, 0.8f, 0.64f), true);
            UIKit.Label(row, t.name, 0.02f, 0.05f, 0.22f, 0.95f, 34, TextAnchor.MiddleLeft, has ? UIKit.Ink : UIKit.Muted);
            UIKit.Label(row, t.cond + "\n" + (has ? "달성!" : "진행 " + Titles.ProgressOf(t, dishes)), 0.23f, 0.05f, 0.58f, 0.95f, 24, TextAnchor.MiddleLeft, UIKit.Muted);
            // 튀김 해금 보상은 얻기 전에는 무엇인지 숨긴다
            string rewardText = !has && t.id == Titles.Survivor ? "???" : t.reward;
            UIKit.Label(row, "보상: " + rewardText, 0.59f, 0.05f, 0.80f, 0.95f, 24, TextAnchor.MiddleLeft, has && t.reward != "없음" ? UIKit.Good : UIKit.Muted);
            bool on = Titles.IsRepresentative(t);
            var tt = t;
            UIKit.IconBtn(row, !has ? "icon_lock" : on ? "icon_star" : "icon_star_off", !has ? "잠김" : on ? "해제" : "대표로", 0.80f, 0.12f, 0.98f, 0.88f, () => { Titles.SetRepresentative(tt); ShowTitles(); Lobby.RefreshTitle(); },
                on ? new Color(0.95f, 0.7f, 0.05f) : UIKit.Good, 30, has && canEquip); // 대표 칭호는 금빛 채운 별
        }
        UIKit.Label(m, canEquip ? "[대표로]를 누르면 그 칭호가 로비에 표시된다. 대표 칭호는 하나만 고를 수 있다." : "대표 칭호는 로비·주 결산 화면에서 바꿀 수 있다.", 0.04f, 0.005f, 0.96f, 0.055f, 24, TextAnchor.MiddleCenter, UIKit.Muted);
    }

    // 결산 때 새로 얻은 칭호를 알린다. "자취 생존자"를 얻으면 튀김 해금도 함께 알린다.
    // 냉장고: 지금 가진 재료와 남은 유통기한 (로비의 냉장고 그림을 누르면)
    void ShowFridge()
    {
        CloseModal();
        modal = UIKit.Box(canvasRT, "Fridge", 0.25f, 0.15f, 0.75f, 0.85f, UIKit.Card, true).gameObject;
        modal.transform.localScale = Vector3.one * 0.97f; // 가장자리 여백
        var m = modal.transform;
        UIKit.Label(m, "냉장고", 0.05f, 0.84f, 0.95f, 0.98f, 64, TextAnchor.MiddleCenter, UIKit.Accent);
        // 재료마다 한 줄: 재료 그림, 이름 × 개수, 남은 유통기한 (급한 것부터)
        var have = run.inv.Where(kv => kv.Value > 0)
            .Select(kv => { int last; return new { ing = kv.Key, qty = kv.Value, left = run.expire.TryGetValue(kv.Key, out last) ? Mathf.Max(0, last - run.Abs + 1) : 99 }; })
            .OrderBy(x => x.left).ToList();
        if (have.Count == 0)
            UIKit.Label(m, "텅 비었다.\n장보기에서 재료를 사 오자.", 0.08f, 0.2f, 0.92f, 0.82f, 44, TextAnchor.MiddleCenter, UIKit.Ink);
        float rowStep = have.Count > 6 ? 0.092f : 0.105f;
        for (int k = 0; k < have.Count; k++)
        {
            var x = have[k];
            float top = 0.82f - k * rowStep, bot = top - (rowStep - 0.024f);
            var row = UIKit.Box(m, "Row", 0.06f, bot, 0.94f, top, new Color(1f, 0.98f, 0.92f), false);
            var rowLine = row.gameObject.AddComponent<Outline>();
            rowLine.effectColor = new Color(0.96f, 0.88f, 0.84f);
            rowLine.effectDistance = new Vector2(2f, 2f);
            SetIcon(UIKit.Box(row, "Icon", 0.02f, 0.06f, 0.14f, 0.94f).gameObject.AddComponent<Image>(), x.ing);
            UIKit.Label(row, x.ing.name + "  ×" + x.qty, 0.17f, 0f, 0.62f, 1f, 38, TextAnchor.MiddleLeft, UIKit.Ink);
            UIKit.Label(row, x.left >= 99 ? "" : x.left <= 1 ? "오늘까지!" : x.left + "일 남음", 0.60f, 0f, 0.97f, 1f, 32, TextAnchor.MiddleRight, x.left <= 1 ? UIKit.Bad : UIKit.Muted);
        }
        UIX.CloseX(m, 0.86f, 0.90f, 0.97f, 0.985f, CloseModal); // 냉장고 닫기도 오른쪽 위 X
    }

    void ShowTitleGain(List<TitleDef> gained)
    {
        CloseModal();
        modal = UIKit.Box(canvasRT, "TitleGain", 0.15f, 0.22f, 0.85f, 0.78f, UIKit.Card, true).gameObject;
        modal.transform.localScale = Vector3.one * 0.97f; // 가장자리 여백
        var m = modal.transform;
        UIKit.Label(m, "칭호 획득!", 0.05f, 0.76f, 0.95f, 0.97f, 80, TextAnchor.MiddleCenter, UIKit.Good);
        // 얻은 칭호는 알약 모양 이름표로 (로비 머리 위 칭호칸과 같은 느낌)
        int n = gained.Count;
        float pillW = Mathf.Min(0.28f, 0.9f / n), gap = 0.02f;
        float startX = 0.5f - (n * pillW + (n - 1) * gap) / 2f;
        for (int i = 0; i < n; i++)
        {
            float px = startX + i * (pillW + gap);
            var pill = UIKit.Box(m, "Pill", px, 0.52f, px + pillW, 0.70f, new Color(0.93f, 0.4f, 0.3f), false);
            var po = pill.gameObject.AddComponent<Outline>();
            po.effectColor = new Color(0.72f, 0.27f, 0.2f);
            po.effectDistance = new Vector2(3f, 3f);
            UIKit.Label(pill, gained[i].name, 0.05f, 0.05f, 0.95f, 0.95f, 44, TextAnchor.MiddleCenter, Color.white);
        }
        bool fry = gained.Any(t => t.id == Titles.Survivor);
        if (fry)
        {
            // 해금 안내 띠
            var banner = UIKit.Box(m, "Unlock", 0.07f, 0.27f, 0.93f, 0.47f, new Color(1f, 0.9f, 0.78f), false);
            UIKit.Label(banner, "튀김 해금!", 0.03f, 0.50f, 0.97f, 0.97f, 44, TextAnchor.MiddleCenter, UIKit.Accent);
            UIKit.Label(banner, "다음 장보기부터 새우가 나오고, 튀기기를 고를 수 있다.", 0.03f, 0.05f, 0.97f, 0.52f, 32, TextAnchor.MiddleCenter, UIKit.Ink);
        }
        UIKit.PrimaryBtn(m, "확인 [Enter]", 0.33f, 0.05f, 0.67f, 0.23f, CloseModal, 40);
    }

    // 도감 저장: 게임을 껐다 켜도 남도록 persistentDataPath/dex.json에 기록한다. 실패해도 게임은 계속된다.
    [System.Serializable]
    class DexEntry
    {
        public string key, name, note;
        public int method, satisfaction;
        public string[] ings;
        public int[] stages;
    }

    [System.Serializable]
    class DexSave
    {
        public List<DexEntry> dishes = new List<DexEntry>();
        public List<string> isNew = new List<string>();
    }

    static string DexPath { get { return SaveDir.File("dex.json"); } } // 에디터 테스트 기록과 빌드 기록을 나눈다 (SaveDir)

    static void SaveDex()
    {
        try
        {
            var save = new DexSave();
            foreach (var d in dex.Values)
                save.dishes.Add(new DexEntry
                {
                    key = d.key, name = d.name, note = d.note,
                    method = (int)d.method, satisfaction = d.satisfaction,
                    ings = d.ings.Select(i => i.name).ToArray(),
                    stages = d.stages == null ? new int[0] : d.stages.Select(s => (int)s).ToArray(),
                });
            save.isNew.AddRange(dexNew);
            System.IO.File.WriteAllText(DexPath, JsonUtility.ToJson(save));
        }
        catch (System.Exception e) { Debug.LogWarning("[도감] 저장 실패: " + e.Message); }
    }

    static void LoadDex()
    {
        try
        {
            if (!System.IO.File.Exists(DexPath)) return;
            var save = JsonUtility.FromJson<DexSave>(System.IO.File.ReadAllText(DexPath));
            if (save == null) return;
            dex.Clear();
            dexNew.Clear();
            foreach (var e in save.dishes)
            {
                var ings = e.ings.Select(n => Data.All.FirstOrDefault(i => i.name == n)).Where(i => i != null).ToArray();
                if (ings.Length == 0 || ings.Length != e.ings.Length) continue; // 재료가 바뀌어 읽을 수 없는 기록은 건너뛴다
                var d = new Dish
                {
                    key = e.key, name = e.name, note = e.note, ings = ings,
                    method = (Method)e.method, satisfaction = e.satisfaction,
                    stages = e.stages.Select(s => (Doneness)s).ToArray(),
                };
                dex[d.key] = d;
            }
            foreach (var k in save.isNew) if (dex.ContainsKey(k)) dexNew.Add(k);
        }
        catch (System.Exception e) { Debug.LogWarning("[도감] 불러오기 실패: " + e.Message); }
    }


    static string Stars(int sat)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(sat / 20f), 0, 5);
        return new string('★', n) + new string('☆', 5 - n);
    }

    // 요리 그림. 먼저 재료 그림을 가로로 나란히 놓고, 만든 요리면 미리 만들어 둔 요리 사진(Resources/AiDishes)이 있으면 바꿔 끼운다.
    // 그림 파일이 없으면 그 재료의 색 사각형으로 대신한다. silhouette이면 아직 못 만든 요리라서 어두운 실루엣으로 보여 준다.
    static void BuildDexPic(Transform parent, Ingredient[] ings, Dish d, bool silhouette, System.Action<bool> photoState = null)
    {
        for (int k = parent.childCount - 1; k >= 0; k--)
        {
            var c = parent.GetChild(k);
            c.SetParent(null);
            Destroy(c.gameObject);
        }
        bool any = false;
        int n = ings.Length;
        for (int k = 0; k < n; k++)
        {
            var ing = ings[k];
            var stage = d != null && d.stages != null && k < d.stages.Length ? d.stages[k] : Doneness.Good;
            Sprite s = silhouette ? (Art.ShopIcon(ing) ?? Art.Get(ing, Doneness.Good)) : Art.Get(ing, stage);
            if (s == null && !silhouette) s = Art.ShopIcon(ing);
            any |= s != null;
            float x0 = k / (float)n + 0.01f, x1 = (k + 1) / (float)n - 0.01f;
            var im = UIKit.Box(parent, "Pic", x0, 0, x1, 1).gameObject.AddComponent<Image>();
            im.sprite = s;
            im.preserveAspect = true;
            im.raycastTarget = false;
            if (silhouette) im.color = new Color(0.25f, 0.2f, 0.15f, s != null ? 0.55f : 0.25f);
            else im.color = s != null ? Color.white : ing.cooked;
        }
        if (silhouette && !any) UIKit.Label(parent, "?", 0, 0, 1, 1, 120, TextAnchor.MiddleCenter, UIKit.Muted);
        if (silhouette || d == null || d.stages == null || d.ings.Length == 0) return;

        // 미리 만든 요리 사진이 있으면 재료 그림 대신 그 사진을 보여 준다 (없으면 재료 그림 그대로)
        var tex = DishArt.Load(d.ings, d.stages, d.method);
        if (tex == null) return;
        for (int k = parent.childCount - 1; k >= 0; k--)
        {
            var c = parent.GetChild(k);
            c.SetParent(null);
            Destroy(c.gameObject);
        }
        var pic = UIKit.Box(parent, "Photo", 0, 0, 1, 1);
        var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = 1f;
        var raw = pic.gameObject.AddComponent<RawImage>();
        raw.texture = tex;
        raw.raycastTarget = false;
        if (photoState != null) photoState(true);
    }

    // 도감 화면의 정렬·필터·검색 상태. 도감을 열 때마다 초기화한다.
    int dexSort;                                   // 0 기본, 1 만족도 높은 순, 2 만족도 낮은 순
    readonly HashSet<Ingredient> dexIngs = new HashSet<Ingredient>(); // 고른 재료가 모두 들어간 요리만
    int dexMethod = -1;                            // -1 전체, 아니면 Method 값
    string dexQuery = "";
    InputField dexSearch;
    Transform dexContent;
    Text dexInfo;
    readonly List<System.Action> dexRestyle = new List<System.Action>();

    void ShowDex()
    {
        CloseModal();
        dexSort = 0; dexIngs.Clear(); dexMethod = -1; dexQuery = ""; dexRestyle.Clear();
        var slots = AllDexSlots();
        int found = slots.Count(s => dex.ContainsKey(s.key));
        modal = UIKit.Box(canvasRT, "Dex", 0.08f, 0.08f, 0.92f, 0.92f, UIKit.Card).gameObject;
        modal.transform.localScale = Vector3.one * 0.97f; // 가장자리 여백
        var m = modal.transform;
        UIKit.Label(m, "도감 (" + found + " / " + slots.Count + ")", 0.05f, 0.88f, 0.45f, 0.98f, 60, TextAnchor.MiddleLeft, UIKit.Accent);
        dexInfo = UIKit.Label(m, "", 0.40f, 0.88f, 0.74f, 0.98f, 28, TextAnchor.MiddleRight, UIKit.Muted);
        UIX.CloseX(m, 0.915f, 0.895f, 0.975f, 0.975f, CloseModal);

        // 1줄: 검색창 + 필터 토글. 정렬·재료·방식 필터는 토글을 켜야 펼쳐진다 (켠 필터 수가 버튼에 보인다)
        System.Action applyFilter = null;
        bool filterOpen = false;
        var fieldRT = UIKit.Box(m, "Search", 0.02f, 0.80f, 0.72f, 0.87f, Color.white);
        dexSearch = fieldRT.gameObject.AddComponent<InputField>();
        var txt = UIKit.Label(fieldRT, "", 0.02f, 0.05f, 0.98f, 0.95f, 32, TextAnchor.MiddleLeft);
        txt.resizeTextForBestFit = false;
        txt.supportRichText = false;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        var ph = UIKit.Label(fieldRT, "요리 이름·재료 검색", 0.02f, 0.05f, 0.98f, 0.95f, 32, TextAnchor.MiddleLeft, UIKit.Muted);
        ph.resizeTextForBestFit = false;
        dexSearch.textComponent = txt;
        dexSearch.placeholder = ph;
        dexSearch.lineType = InputField.LineType.SingleLine;
        dexSearch.onValueChanged.AddListener(v => { dexQuery = v; RebuildDexGrid(); });

        var toggleBtn = UIKit.IconBtn(m, "icon_filter", "필터", 0.74f, 0.80f, 0.98f, 0.87f, () => { filterOpen = !filterOpen; applyFilter(); }, UIKit.Muted, 30);
        var toggleImg = toggleBtn.GetComponent<Image>();
        var toggleText = toggleBtn.GetComponentInChildren<Text>();

        // 펼쳐지는 필터 칸. 칩은 이 칸 안의 비율(0~1)로 놓는다 (x는 도감 창 기준 값을 칸 기준으로 바꾼다)
        var panel = UIKit.Box(m, "Filters", 0.02f, 0.625f, 0.98f, 0.795f, new Color(1f, 1f, 1f, 0.55f), false);
        System.Func<float, float> rx = x => (x - 0.02f) / 0.96f;
        // 윗줄: 재료 칩 (여러 개 고르면 모두 들어간 요리)
        UIKit.Label(panel, "재료", rx(0.02f), 0.54f, rx(0.065f), 0.95f, 28, TextAnchor.MiddleCenter, UIKit.Muted);
        var chipIngs = Data.Active; // 해금 전에는 새우 칩이 없다
        for (int i = 0; i < chipIngs.Length; i++)
        {
            var ing = chipIngs[i];
            float x = 0.07f + i * 0.08f;
            DexChip(panel, ing.name, rx(x), 0.54f, rx(x + 0.075f), 0.94f, () => dexIngs.Contains(ing),
                () => { if (!dexIngs.Remove(ing)) dexIngs.Add(ing); });
        }
        // 아랫줄: 방식 칩 + 정렬 칩
        UIKit.Label(panel, "방식", rx(0.02f), 0.06f, rx(0.065f), 0.46f, 28, TextAnchor.MiddleCenter, UIKit.Muted);
        var chipMethods = Data.ActiveMethods; // 해금 전에는 튀기기 칩이 없다
        for (int i = 0; i < chipMethods.Length; i++)
        {
            int mi = (int)chipMethods[i].id;
            float x = 0.07f + i * 0.072f;
            DexChip(panel, chipMethods[i].name, rx(x), 0.06f, rx(x + 0.068f), 0.46f, () => dexMethod == mi, () => dexMethod = dexMethod == mi ? -1 : mi);
        }
        UIKit.Label(panel, "정렬", rx(0.45f), 0.06f, rx(0.495f), 0.46f, 28, TextAnchor.MiddleCenter, UIKit.Muted);
        DexChip(panel, "만족도 높은 순", rx(0.50f), 0.06f, rx(0.65f), 0.46f, () => dexSort == 1, () => dexSort = dexSort == 1 ? 0 : 1);
        DexChip(panel, "만족도 낮은 순", rx(0.655f), 0.06f, rx(0.805f), 0.46f, () => dexSort == 2, () => dexSort = dexSort == 2 ? 0 : 2);

        // 스크롤 영역: 5열 그리드 (필터를 접으면 넓어진다)
        var view = UIKit.Box(m, "Scroll", 0.02f, 0.02f, 0.98f, 0.79f);
        System.Action refreshToggle = () =>
        {
            int n = dexIngs.Count + (dexMethod >= 0 ? 1 : 0) + (dexSort != 0 ? 1 : 0);
            toggleText.text = (filterOpen ? "필터 접기" : "필터") + (n > 0 ? " (" + n + ")" : "");
            toggleImg.color = filterOpen || n > 0 ? UIKit.Accent : UIKit.Muted;
        };
        dexRestyle.Add(refreshToggle);
        applyFilter = () =>
        {
            panel.gameObject.SetActive(filterOpen);
            view.anchorMax = new Vector2(view.anchorMax.x, filterOpen ? 0.615f : 0.79f);
            refreshToggle();
        };
        applyFilter();
        var sr = view.gameObject.AddComponent<ScrollRect>();
        var vp = UIKit.Box(view, "Viewport", 0, 0, 1, 1, new Color(1f, 1f, 1f, 0.01f), false);
        vp.gameObject.AddComponent<RectMask2D>();
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(vp, false);
        var crt = (RectTransform)content.transform;
        crt.anchorMin = new Vector2(0, 1);
        crt.anchorMax = new Vector2(1, 1);
        crt.pivot = new Vector2(0.5f, 1);
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;
        var grid = content.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(270, 320);
        grid.spacing = new Vector2(28, 28);
        grid.padding = new RectOffset(20, 20, 10, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.childAlignment = TextAnchor.UpperCenter;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        content.AddComponent<DexGridFit>(); // 화면 비율이 달라도 5열이 팝업 안에 들어오게 칸 크기를 맞춘다
        sr.content = crt;
        sr.viewport = vp;
        sr.horizontal = false;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 40f;
        dexContent = content.transform;
        RebuildDexGrid();
    }

    // 켜짐/꺼짐이 색으로 보이는 작은 버튼. 누르면 상태를 바꾸고 칸을 다시 만든다.
    void DexChip(Transform parent, string text, float x0, float y0, float x1, float y1, System.Func<bool> on, System.Action click)
    {
        var b = UIKit.Btn(parent, text, x0, y0, x1, y1, () =>
        {
            click();
            foreach (var r in dexRestyle) r();
            RebuildDexGrid();
        }, UIKit.Muted, 28);
        var img = b.GetComponent<Image>();
        System.Action restyle = () => img.color = on() ? UIKit.Accent : UIKit.Muted;
        dexRestyle.Add(restyle);
        restyle();
    }

    // 정렬·필터·검색을 적용해 카드를 다시 만든다. 못 만든 요리는 이름을 모르니 검색에서는 뺀다.
    void RebuildDexGrid()
    {
        if (dexContent == null) return;
        for (int k = dexContent.childCount - 1; k >= 0; k--)
        {
            var c = dexContent.GetChild(k);
            c.SetParent(null);
            Destroy(c.gameObject);
        }
        IEnumerable<DexSlot> list = AllDexSlots();
        foreach (var ing in dexIngs.ToArray()) { var want = ing; list = list.Where(s => s.ings.Contains(want)); }
        if (dexMethod >= 0) list = list.Where(s => (int)s.method == dexMethod);
        string q = (dexQuery ?? "").Trim();
        if (q.Length > 0)
            list = list.Where(s =>
            {
                Dish d;
                if (!dex.TryGetValue(s.key, out d)) return false;
                return d.name.Contains(q) || Data.GetMethod(s.method).name.Contains(q) || s.ings.Any(i => i.name.Contains(q));
            });
        if (dexSort == 1) list = list.OrderBy(s => dex.ContainsKey(s.key) ? 0 : 1).ThenByDescending(s => dex.ContainsKey(s.key) ? dex[s.key].satisfaction : 0);
        else if (dexSort == 2) list = list.OrderBy(s => dex.ContainsKey(s.key) ? 0 : 1).ThenBy(s => dex.ContainsKey(s.key) ? dex[s.key].satisfaction : 0);
        var shown = list.ToList();
        foreach (var s in shown) DexCard(dexContent, s);
        ((RectTransform)dexContent).anchoredPosition = Vector2.zero;
        if (dexInfo != null)
            dexInfo.text = shown.Count == 0 ? "조건에 맞는 요리가 없다." : "표시 " + shown.Count + "칸 · 요리를 누르면 자세히";
    }

    void DexCard(Transform parent, DexSlot s)
    {
        Dish d;
        bool has = dex.TryGetValue(s.key, out d);
        var card = UIKit.Box(parent, "Card", 0, 0, 1, 1, has ? Color.white : new Color(0.88f, 0.8f, 0.64f));
        BuildDexPic(UIKit.Box(card, "Pic", 0.08f, 0.40f, 0.92f, 0.96f), s.ings, d, !has);
        UIKit.Label(card, has ? d.name : "???", 0.04f, 0.20f, 0.96f, 0.40f, 40, TextAnchor.MiddleCenter, has ? UIKit.Ink : UIKit.Muted);
        string method = Data.GetMethod(s.method).name;
        UIKit.Label(card, has ? Stars(d.satisfaction) + "  " + d.satisfaction : method, 0.04f, 0.02f, 0.96f, 0.20f, 30,
            TextAnchor.MiddleCenter, has ? UIKit.Accent : UIKit.Muted);
        if (!has) return;
        GameObject badge = null;
        if (dexNew.Contains(s.key)) // 새로 등록된 요리: 카드를 열어 보면 표시가 사라진다
        {
            var bg = UIKit.Box(card, "New", 0.60f, 0.88f, 1.0f, 1.0f, UIKit.Bad, true);
            UIKit.Label(bg, "NEW", 0.05f, 0.05f, 0.95f, 0.95f, 28, TextAnchor.MiddleCenter, Color.white);
            badge = bg.gameObject;
        }
        var b = card.gameObject.AddComponent<Button>();
        b.targetGraphic = card.GetComponent<Image>();
        b.transition = Selectable.Transition.None; // 카드 전체를 물들이면 누끼 없는 사진의 흰 바탕이 드러난다. 안쪽 테두리 하이라이트로 대신한다
        var ringRt = UIKit.Box(card, "HoverRing", 0.025f, 0.015f, 0.975f, 0.985f);
        var ringImg = ringRt.gameObject.AddComponent<Image>();
        ringImg.sprite = HudStyle.Ring(new Color(0.976f, 0.72f, 0.2f));
        ringImg.type = Image.Type.Sliced;
        ringImg.raycastTarget = false;
        ringRt.gameObject.SetActive(false);
        card.gameObject.AddComponent<HoverRing>().ring = ringRt.gameObject;
        b.onClick.AddListener(() =>
        {
            if (dexNew.Remove(s.key)) SaveDex();
            if (badge != null) Destroy(badge);
            ShowDexDetail(d);
        });
    }

    // 도감 상세 팝업. AI 이름이 늦게 오면 RefreshDexDetail이 열려 있는 팝업을 갱신한다.
    GameObject dexDetail;
    Dish detailDish;
    Text dtName, dtImgNote;
    Transform dtPic;

    void ShowDexDetail(Dish d)
    {
        CloseDexDetail();
        detailDish = d;
        dexDetail = UIKit.Box(canvasRT, "DexDetail", 0, 0, 1, 1, new Color(0f, 0f, 0f, 0.55f), false).gameObject;
        dexDetail.AddComponent<Button>().onClick.AddListener(CloseDexDetail); // 바깥을 누르면 닫는다
        var card = UIKit.Box(dexDetail.transform, "Card", 0.14f, 0.10f, 0.86f, 0.90f, UIKit.Card);
        card.localScale = Vector3.one * 0.97f;
        var panel = UIKit.Box(card, "Pic", 0.04f, 0.18f, 0.44f, 0.94f, Color.white);
        dtPic = UIKit.Box(panel, "Img", 0.04f, 0.10f, 0.96f, 0.96f);
        dtImgNote = UIKit.Label(panel, "", 0.04f, 0.01f, 0.96f, 0.10f, 26, TextAnchor.MiddleCenter, UIKit.Muted);
        dtName = UIKit.Label(card, "", 0.48f, 0.80f, 0.89f, 0.95f, 60, TextAnchor.MiddleLeft);

        string ings = string.Join(", ", d.ings.Select((i, k) =>
            i.name + (d.stages != null && k < d.stages.Length ? "(" + StageName[(int)d.stages[k]] + ")" : "")).ToArray());
        UIKit.Label(card, "재료  " + ings + "   |   방식  " + Data.GetMethod(d.method).name, 0.48f, 0.74f, 0.96f, 0.81f, 30, TextAnchor.MiddleLeft);
        // 레시피: 실제로 이 요리를 만드는 순서 (게임 요령은 엄마의 전언에서)
        var steps = Recipes.Real(d.ings, d.method);
        var sb = new System.Text.StringBuilder("레시피\n");
        for (int k = 0; k < steps.Length; k++) sb.Append(k + 1).Append(". ").Append(steps[k]).Append('\n');
        UIKit.Label(card, sb.ToString().TrimEnd(), 0.48f, 0.30f, 0.96f, 0.74f, 28, TextAnchor.UpperLeft, UIKit.Muted);
        UIKit.Label(card, "최고 만족도  " + d.satisfaction + "   " + Stars(d.satisfaction), 0.48f, 0.19f, 0.96f, 0.30f, 38, TextAnchor.MiddleLeft, UIKit.Accent);
        if (!string.IsNullOrEmpty(d.note)) UIKit.Label(card, d.note, 0.04f, 0.08f, 0.62f, 0.18f, 28, TextAnchor.MiddleLeft, UIKit.Bad);
        UIX.CloseX(card, 0.90f, 0.88f, 0.98f, 0.98f, CloseDexDetail);
        BuildDexPic(dtPic, d.ings, d, false);
        RefreshDexDetail(d);
    }

    void RefreshDexDetail(Dish d)
    {
        if (dexDetail == null || d != detailDish) return;
        dtName.text = d.name;
    }

    void CloseDexDetail()
    {
        if (dexDetail != null) Destroy(dexDetail);
        dexDetail = null;
        detailDish = null;
    }
}

// 도감 격자: 보이는 너비에 5열이 딱 맞도록 칸 크기를 계산한다 (4:3처럼 좁은 화면에서 잘리지 않게).
public class DexGridFit : UIBehaviour
{
    protected override void OnRectTransformDimensionsChange() { Fit(); }
    protected override void Start() { Fit(); }

    void Fit()
    {
        var grid = GetComponent<GridLayoutGroup>();
        var rt = transform as RectTransform;
        if (grid == null || rt == null) return;
        float w = rt.rect.width;
        if (w < 100f) return;
        int n = Mathf.Max(1, grid.constraintCount);
        float cw = Mathf.Min(270f, (w - grid.padding.left - grid.padding.right - grid.spacing.x * (n - 1)) / n);
        grid.cellSize = new Vector2(cw, cw * 320f / 270f);
    }
}
