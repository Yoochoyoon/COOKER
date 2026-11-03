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
public class Dish
{
    public string name;
    public string key;
    public Ingredient[] ings;
    public Method method;
    public int satisfaction;
    public string note;      // 일부 재료를 망쳤을 때의 안내
    public Doneness[] stages; // ings와 같은 순서의 익힘 상태 (AI 이름·그림 요청에 쓴다)
    public string desc;       // AI가 지은 한 줄 설명 (없으면 비어 있다)
    public Sprite image;      // AI가 그린 요리 그림 (없으면 null)
    public bool namePending, imagePending; // AI 응답을 기다리는 중
}

public class Run
{
    public int week = 1;
    public int day = 1;
    public int strikes;      // 이번 주에 쓴 비상 라면 횟수
    public Dictionary<Ingredient, int> expire = new Dictionary<Ingredient, int>(); // 재료 더미가 먹을 수 있는 마지막 날(전체 기준 일차)
    public int Abs { get { return (week - 1) * Balance.Days + day; } } // 판 전체에서 몇 일차인지
    public int budget = Balance.Budget;
    public int bought;
    public Dictionary<Ingredient, int> inv = new Dictionary<Ingredient, int>();
    public List<Dish> dishes = new List<Dish>();
}

public class GameFlow : MonoBehaviour
{
    static GameFlow inst;
    static readonly Dictionary<string, Dish> dex = new Dictionary<string, Dish>(); // 도감은 판이 바뀌어도 유지

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("COOKER");
        inst = go.AddComponent<GameFlow>();
    }

    // 키보드 조작: 화면마다 Enter/Space로 누를 기본 동작을 정해 둔다.
    enum Scr { None, Title, Shop, Day, Cook, Meal, Result, Other }
    Scr scr;
    System.Action confirm;
    readonly List<Ingredient> dayList = new List<Ingredient>();

    RectTransform canvasRT, root;
    GameObject modal;
    Run run;
    readonly Dictionary<Ingredient, int> cart = new Dictionary<Ingredient, int>();

    // 하루 선택 상태
    readonly List<Ingredient> sel = new List<Ingredient>();
    Method? selMethod;

    // 조리 상태
    Cook cook;
    bool guideMeal;
    float stirTimer;
    RectTransform lidRect;
    Image lidImage;
    readonly List<RectTransform> itemRects = new List<RectTransform>(); // 재료 아이템 (트레이/팬/접시를 오감)
    readonly List<Image> itemImgs = new List<Image>();
    readonly List<Text> itemLabels = new List<Text>();
    readonly List<bool> swArt = new List<bool>(); // 이 재료가 그림(5단계)으로 그려지는지
    readonly List<Image> fireBtns = new List<Image>();
    Button flipBtn;
    Text timeLabel, stateLabel;

    // 배경 그림, 장보기 연출
    Image bgImg;
    RectTransform flyLayer, basketRT;
    readonly HashSet<Ingredient> flyingIn = new HashSet<Ingredient>(); // 장바구니에 처음 담겨 날아가는 중인 재료(도착 전엔 바구니에 숨긴다)

    void Awake()
    {
        if (Camera.main != null) Camera.main.backgroundColor = UIKit.Bg;
        BuildCanvas();
        ShowTitle();
    }

    void BuildCanvas()
    {
        var cgo = new GameObject("Canvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        canvasRT = (RectTransform)cgo.transform;

        bgImg = UIKit.Box(canvasRT, "Background", 0, 0, 1, 1, UIKit.Bg).GetComponent<Image>();
        root = UIKit.Box(canvasRT, "Screen", 0, 0, 1, 1);
        flyLayer = UIKit.Box(canvasRT, "Fly", 0, 0, 1, 1); // 화면이 다시 그려져도 남는, 날아가는 재료용 층

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var ego = new GameObject("EventSystem", typeof(EventSystem));
            var module = ego.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }

    void Clear()
    {
        CloseModal();
        SetBg(null);
        cook = null;
        scr = Scr.Other;
        confirm = null;
        dayList.Clear();
        lidRect = null;
        itemRects.Clear();
        itemImgs.Clear();
        itemLabels.Clear();
        swArt.Clear();
        fireBtns.Clear();
        flipBtn = null;
        foreach (Transform c in root)
        {
            c.gameObject.SetActive(false);
            Destroy(c.gameObject);
        }
    }

    // ---------------------------------------------------------------- 타이틀
    void ShowTitle()
    {
        Clear();
        SetBg("bg_title");
        var logo = Art.Load("title_logo");
        var start = Art.Load("title_start");
        var exit = Art.Load("title_exit");
        if (logo != null && start != null && exit != null)
        {
            // 그림 배치는 원본 타이틀 이미지(1671x941)의 위치를 화면 비율로 옮긴 것이다. 겹치는 START와 EXIT는 원본처럼 EXIT가 위에 그려진다.
            // 창 비율이 16:9가 아니어도 배치가 눌리지 않도록, 원본과 같은 비율의 영역 안에 놓는다.
            var area = UIKit.Box(root, "TitleArea", 0, 0, 1, 1);
            var fit = area.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1671f / 941f;
            TitleImage(area, logo, 0.1125f, 0.3921f, 0.9539f, 0.9341f);
            var startImg = TitleImage(area, start, 0.2202f, 0.2136f, 0.7828f, 0.4262f);
            var exitImg = TitleImage(area, exit, 0.2513f, 0.0839f, 0.7756f, 0.3411f);
            // 두 그림이 겹치므로 눌리는 영역은 글자 높이에서 위아래로 나눈다.
            TitleHit(area, "StartHit", 0.23f, 0.2667f, 0.78f, 0.4208f, startImg, StartRun);
            TitleHit(area, "ExitHit", 0.26f, 0.09f, 0.775f, 0.2667f, exitImg, QuitGame);
            UIKit.Btn(root, "도감 [D]", 0.03f, 0.03f, 0.17f, 0.11f, ShowDex, UIKit.Muted, 30);
        }
        else
        {
            UIKit.Label(root, "COOKER", 0.1f, 0.58f, 0.9f, 0.88f, 150, TextAnchor.MiddleCenter, UIKit.Accent);
            UIKit.Label(root, "일주일치 장바구니로 최대한 오래 버텨라", 0.1f, 0.46f, 0.9f, 0.58f, 54);
            UIKit.Btn(root, "시작  [Enter]", 0.35f, 0.26f, 0.65f, 0.40f, StartRun);
            UIKit.Btn(root, "도감  [D]", 0.35f, 0.10f, 0.65f, 0.22f, ShowDex, UIKit.Muted, 40);
        }
        scr = Scr.Title;
        confirm = StartRun;
    }

    Image TitleImage(Transform parent, Sprite s, float x0, float y0, float x1, float y1)
    {
        var im = UIKit.Box(parent, "TitleArt", x0, y0, x1, y1).gameObject.AddComponent<Image>();
        im.sprite = s;
        im.preserveAspect = true;
        im.raycastTarget = false;
        return im;
    }

    // 눈에 안 보이는 눌림 영역. 눌리거나 마우스를 올리면 그림(visual)이 어두워진다.
    void TitleHit(Transform parent, string name, float x0, float y0, float x1, float y1, Image visual, System.Action onClick)
    {
        var rt = UIKit.Box(parent, name, x0, y0, x1, y1, new Color(1f, 1f, 1f, 0f));
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = visual;
        var cb = b.colors;
        cb.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
        cb.pressedColor = new Color(0.75f, 0.75f, 0.75f);
        b.colors = cb;
        b.onClick.AddListener(() => onClick());
    }

    void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void StartRun()
    {
        run = new Run();
        flyingIn.Clear();
        cart.Clear();
        sel.Clear();
        selMethod = null;
        ShowShop();
    }

    // ---------------------------------------------------------------- 장보기
    void ShowShop()
    {
        Clear();
        SetBg("bg_shop");
        PurgeExpired();
        int spent = cart.Sum(kv => Price(kv.Key) * kv.Value);
        int units = cart.Values.Sum();
        int left = run.budget - spent;

        // 왼쪽: 장보기 표 (둥근 모서리 + 테두리)
        UIKit.Box(root, "PanelBorder", 0.02f, 0.03f, 0.545f, 0.97f, new Color(0.55f, 0.38f, 0.14f), true);
        UIKit.Box(root, "Panel", 0.026f, 0.036f, 0.539f, 0.964f, new Color(1f, 0.88f, 0.45f, 0.97f), true);
        UIKit.Label(root, run.week + "주차 장보기", 0.04f, 0.88f, 0.30f, 0.96f, 52, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Label(root, "남은 예산 " + left.ToString("N0") + "원", 0.30f, 0.88f, 0.525f, 0.96f, 44, TextAnchor.MiddleRight);
        UIKit.Label(root, run.week == 1 ? "6끼분 담기 · 첫날 달걀은 무료" : "7끼분 채우기 · 이월 재료 있음, 가격 인상",
            0.04f, 0.825f, 0.525f, 0.875f, 28, TextAnchor.MiddleLeft, UIKit.Ink);
        UIKit.Label(root, "1~6 담기 · Shift+1~6 빼기 · Enter 확정",
            0.04f, 0.785f, 0.525f, 0.822f, 20, TextAnchor.MiddleLeft, UIKit.Muted);

        UIKit.Label(root, "재료", 0.10f, 0.725f, 0.235f, 0.785f, 30, TextAnchor.MiddleLeft, UIKit.Muted);
        UIKit.Label(root, "가격", 0.235f, 0.725f, 0.325f, 0.785f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(root, "유통기한", 0.325f, 0.725f, 0.395f, 0.785f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(root, "수량", 0.405f, 0.725f, 0.525f, 0.785f, 30, TextAnchor.MiddleCenter, UIKit.Muted);

        for (int r = 0; r < Data.All.Length; r++)
        {
            var ing = Data.All[r];
            float top = RowTop(r);
            float bot = top - 0.08f;
            UIKit.Box(root, "Row", 0.035f, bot, 0.53f, top, UIKit.Card, true);
            int qty;
            cart.TryGetValue(ing, out qty);
            int own;
            run.inv.TryGetValue(ing, out own);
            SetIcon(UIKit.Box(root, "Icon", 0.045f, bot + 0.008f, 0.095f, top - 0.008f).gameObject.AddComponent<Image>(), ing);
            UIKit.Label(root, "[" + (r + 1) + "] " + ing.name + (own > 0 ? " (보유 " + own + ")" : ""), 0.10f, bot, 0.235f, top, 34, TextAnchor.MiddleLeft);
            UIKit.Label(root, Price(ing).ToString("N0") + "원", 0.235f, bot, 0.325f, top, 34);
            UIKit.Label(root, ing.shelf + "일", 0.325f, bot, 0.395f, top, 34);
            UIKit.Btn(root, "-", 0.405f, bot + 0.01f, 0.445f, top - 0.01f, () => ChangeCart(ing, -1), UIKit.Muted, 44, qty > 0, true);
            UIKit.Label(root, qty.ToString(), 0.445f, bot, 0.485f, top, 38);
            UIKit.Btn(root, "+", 0.485f, bot + 0.01f, 0.525f, top - 0.01f, () => ChangeCart(ing, 1), UIKit.Accent, 44, Price(ing) <= left, true);
        }

        UIKit.Label(root, "담은 재료 " + units + "개", 0.04f, 0.04f, 0.25f, 0.16f, 38, TextAnchor.MiddleLeft);
        bool ok = units >= Need();
        UIKit.Btn(root, ok ? "구매 확정  [Enter]" : "최소 " + Need() + "개 필요", 0.27f, 0.04f, 0.525f, 0.16f, ConfirmShop, UIKit.Good, 40, ok, true);

        DrawBasket();
        scr = Scr.Shop;
        confirm = ok ? (System.Action)ConfirmShop : null;
    }

    // ---- 장바구니 연출
    // 재료 줄의 위쪽 위치(0~1). 날아가는 재료의 출발점도 이 값으로 정한다.
    static float RowTop(int r) { return 0.715f - r * 0.09f; }
    static readonly Vector2 BasketMouth = new Vector2(0.785f, 0.42f); // 재료가 떨어지는 장바구니 입구 (화면 비율)

    // 장바구니: 그림(Resources/Art/shop_basket.png)이 있으면 그걸, 없으면 빨간 상자로 그린다.
    // 담은 재료는 장바구니 안에 작은 그림으로 쌓인다(최대 12개). 날아오는 중인 재료는 도착해야 보인다.
    void DrawBasket()
    {
        basketRT = UIKit.Box(root, "Basket", 0.60f, 0.12f, 0.97f, 0.62f);
        // shop_basket_back/front가 둘 다 있으면 재료가 뒤-앞 사이에 끼인 것처럼 보이게 그린다.
        var back = Art.Load("shop_basket_back");
        var front = Art.Load("shop_basket_front");
        if (back != null && front != null)
        {
            var bim = basketRT.gameObject.AddComponent<Image>();
            bim.sprite = back;
            bim.preserveAspect = true;
            bim.raycastTarget = false;
        }
        else
        {
            var spr = Art.Load("shop_basket");
            if (spr != null)
            {
                var im = basketRT.gameObject.AddComponent<Image>();
                im.sprite = spr;
                im.preserveAspect = true;
                im.raycastTarget = false;
            }
            else
            {
                UIKit.Box(basketRT, "Body", 0.04f, 0.0f, 0.96f, 0.72f, new Color(0.85f, 0.25f, 0.22f));
                UIKit.Box(basketRT, "Rim", 0.0f, 0.62f, 1.0f, 0.80f, new Color(0.65f, 0.15f, 0.15f));
                UIKit.Label(basketRT, "장바구니", 0.1f, 0.03f, 0.9f, 0.28f, 54, TextAnchor.MiddleCenter, Color.white);
            }
        }
        // 담은 개수만큼 쌓지 않고, 담긴 재료 종류마다 하나씩만 보여준다 (최대 6종)
        var list = cart.Where(kv => kv.Value > 0 && !flyingIn.Contains(kv.Key)).Select(kv => kv.Key).ToList();
        int shown = Mathf.Min(list.Count, 6);
        for (int i = 0; i < shown; i++)
        {
            float x = 0.05f + i * 0.15f;
            float y = 0.46f;
            SetIcon(UIKit.Box(basketRT, "Item", x, y, x + 0.18f, y + 0.36f).gameObject.AddComponent<Image>(), list[i]);
        }
        if (back != null && front != null)
        {
            var fim = UIKit.Box(basketRT, "BasketFront", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            fim.sprite = front;
            fim.preserveAspect = true;
            fim.raycastTarget = false;
        }
    }

    // 장보기 줄·장바구니에 쓰는 아이콘(조리 중 그림과는 다른 그림). 없으면 그 재료의 색 사각형으로 대신한다.
    void SetIcon(Image img, Ingredient ing)
    {
        var s = Art.ShopIcon(ing);
        img.sprite = s;
        img.color = s != null ? Color.white : ing.raw;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    // 재료 하나가 from에서 to로 포물선을 그리며 날아간다. 장바구니에 담길 때는 도착하면 장바구니가 통통 튄다.
    System.Collections.IEnumerator FlyRoutine(Ingredient ing, Vector2 from, Vector2 to, bool intoBasket)
    {
        var go = new GameObject("Fly", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(flyLayer, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(100f, 100f);
        rt.anchorMin = rt.anchorMax = from;
        SetIcon(go.GetComponent<Image>(), ing);
        const float dur = 0.45f;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            if (rt == null) yield break;
            float k = t / dur;
            float e = k * k * (3f - 2f * k);
            var p = Vector2.Lerp(from, to, e);
            p.y += Mathf.Sin(k * Mathf.PI) * 0.10f;
            rt.anchorMin = rt.anchorMax = p;
            rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, k);
            yield return null;
        }
        if (go != null) Destroy(go);
        if (!intoBasket) yield break;
        flyingIn.Remove(ing);
        if (scr == Scr.Shop) ShowShop();                 // 도착한 재료가 장바구니 안에 나타난다
        if (basketRT != null) StartCoroutine(BumpRoutine(basketRT));
    }

    System.Collections.IEnumerator BumpRoutine(RectTransform rt)
    {
        const float dur = 0.2f;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            if (rt == null) yield break;
            rt.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(t / dur * Mathf.PI));
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    // 화면마다 배경 그림(Resources/Art/<파일>.png)을 깐다. 파일이 없으면 단색 배경이다. 16:9 그림을 권장한다.
    void SetBg(string file)
    {
        if (bgImg == null) return;
        var s = file == null ? null : Art.Load(file);
        bgImg.sprite = s;
        bgImg.preserveAspect = s != null; // 창 비율이 달라도 그림이 늘어나지 않는다(남는 곳은 배경색)
        bgImg.color = s != null ? Color.white : UIKit.Bg;
    }

    // 이번 주 재료 가격: 매주 오른다
    int Price(Ingredient i) { return Mathf.RoundToInt(i.price * Mathf.Pow(Balance.PriceGrowth, run.week - 1) / 10f) * 10; }
    // 이번 주 장보기에서 최소로 담아야 하는 개수. 이월된 재료만큼 줄어든다.
    int Need() { return run.week == 1 ? Balance.MinUnits : Mathf.Max(0, Balance.Days - run.inv.Values.Sum()); }

    void ChangeCart(Ingredient ing, int delta)
    {
        int qty;
        cart.TryGetValue(ing, out qty);
        int before = qty;
        if (delta > 0 && Price(ing) > run.budget - cart.Sum(kv => Price(kv.Key) * kv.Value)) return; // 예산 초과는 담지 않는다
        qty = Mathf.Max(0, qty + delta);
        if (qty == 0) cart.Remove(ing); else cart[ing] = qty;
        if (qty != before)
        {
            int r = System.Array.IndexOf(Data.All, ing);
            var rowPos = new Vector2(0.07f, RowTop(r) - 0.04f);
            if (delta > 0)
            {
                if (before == 0) flyingIn.Add(ing); // 이 재료가 바구니에 처음 들어갈 때만 도착할 때까지 숨긴다
                StartCoroutine(FlyRoutine(ing, rowPos, BasketMouth, true));
            }
            else StartCoroutine(FlyRoutine(ing, BasketMouth, rowPos, false));
        }
        ShowShop();
    }

    void ConfirmShop()
    {
        flyingIn.Clear();
        run.budget -= cart.Sum(kv => Price(kv.Key) * kv.Value);
        int carried = run.inv.Values.Sum();
        foreach (var kv in cart)
        {
            int have;
            run.inv.TryGetValue(kv.Key, out have);
            run.inv[kv.Key] = have + kv.Value;
            int last = run.Abs + kv.Key.shelf - 1;
            int old;
            // 이월된 재료와 새로 산 재료가 섞이면 더 먼저 상하는 쪽을 따른다
            run.expire[kv.Key] = run.expire.TryGetValue(kv.Key, out old) && have > 0 ? Mathf.Min(old, last) : last;
        }
        run.bought = carried + cart.Values.Sum();
        ShowDay();
    }

    // ---------------------------------------------------------------- 하루 메인
    int DaysLeft(Ingredient i) { return run.expire[i] - run.Abs + 1; } // 오늘 포함 며칠 더 쓸 수 있는지

    // 유통기한 지난 재료는 폐기
    void PurgeExpired()
    {
        foreach (var i in run.inv.Keys.ToList())
            if (!run.expire.ContainsKey(i) || DaysLeft(i) <= 0) { run.inv.Remove(i); run.expire.Remove(i); }
    }

    void ShowDay()
    {
        // 유통기한 지난 재료는 폐기
        PurgeExpired();
        sel.RemoveAll(i => !run.inv.ContainsKey(i));

        guideMeal = run.week == 1 && run.day == 1;
        if (!guideMeal && run.inv.Count == 0)
        {
            Starve("먹을 재료가 하나도 없다.");
            return;
        }
        if (guideMeal)
        {
            sel.Clear();
            sel.Add(Data.Egg);
            selMethod = Method.Grill;
        }

        Clear();
        UIKit.Label(root, run.week + "주차 " + run.day + "일차 / " + Balance.Days + "일", 0.03f, 0.88f, 0.55f, 0.98f, 56, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Label(root, "비상 라면 " + (Balance.Lifelines - run.strikes) + "/" + Balance.Lifelines + "   먹은 끼니 " + run.dishes.Count + "개", 0.55f, 0.88f, 0.97f, 0.98f, 40, TextAnchor.MiddleRight);

        // 왼쪽: 재료
        UIKit.Box(root, "Left", 0.03f, 0.25f, 0.50f, 0.87f, UIKit.Card);
        UIKit.Label(root, guideMeal ? "엄마가 보낸 달걀 (첫날 가이드)" : "보유 재료 (최대 2개 선택)", 0.05f, 0.80f, 0.48f, 0.86f, 34, TextAnchor.MiddleLeft, UIKit.Muted);
        if (guideMeal)
        {
            UIKit.Btn(root, "달걀 x1", 0.06f, 0.66f, 0.47f, 0.77f, null, UIKit.Accent, 44);
            UIKit.Label(root, "첫날은 달걀을 굽는다.\n불 세기와 꺼내기를 익혀 보자.\n실패해도 굶지 않으니 다시 하면 돼!",
                0.06f, 0.30f, 0.47f, 0.64f, 38, TextAnchor.UpperLeft);
        }
        else
        {
            int r = 0;
            foreach (var kv in run.inv.OrderBy(k => DaysLeft(k.Key)))
            {
                var ing = kv.Key;
                float top = 0.79f - r * 0.092f;
                float bot = top - 0.082f;
                int dl = DaysLeft(ing);
                string tag = dl == 1 ? "오늘까지!" : "D-" + (dl - 1);
                bool on = sel.Contains(ing);
                dayList.Add(ing);
                UIKit.Btn(root, "[" + (r + 1) + "] " + ing.name + " x" + kv.Value + "   (" + tag + ")", 0.06f, bot, 0.47f, top,
                    () => ToggleIngredient(ing), on ? UIKit.Good : (dl == 1 ? UIKit.Bad : UIKit.Accent), 38);
                r++;
            }
        }

        // 오른쪽: 방식
        UIKit.Box(root, "Right", 0.52f, 0.47f, 0.97f, 0.87f, UIKit.Card);
        UIKit.Label(root, "조리 방식", 0.54f, 0.80f, 0.95f, 0.86f, 34, TextAnchor.MiddleLeft, UIKit.Muted);
        for (int k = 0; k < Data.Methods.Length; k++)
        {
            var md = Data.Methods[k];
            float top = 0.79f - k * 0.105f;
            float bot = top - 0.095f;
            bool on = selMethod == md.id;
            UIKit.Btn(root, "[" + "QWE"[k] + "] " + md.name, 0.55f, bot, 0.94f, top, () => { selMethod = md.id; ShowDay(); },
                on ? UIKit.Good : UIKit.Accent, 44, !guideMeal);
        }

        // 엄마 힌트
        UIKit.Box(root, "Hint", 0.52f, 0.25f, 0.97f, 0.45f, UIKit.Card);
        string hint = sel.Count == 0
            ? "밥은 잘 챙겨 먹고 있니? 아껴 쓰되, 굶지는 마라."
            : string.Join("\n", sel.Select(i => Data.MomHint(i)));
        UIKit.Label(root, "엄마의 전화", 0.54f, 0.39f, 0.95f, 0.45f, 28, TextAnchor.MiddleLeft, UIKit.Muted);
        UIKit.Label(root, hint, 0.54f, 0.26f, 0.95f, 0.40f, 34, TextAnchor.MiddleLeft);

        // 하단
        string summary = sel.Count == 0 ? "재료를 골라 주세요" : string.Join(" + ", sel.Select(i => i.name));
        summary += selMethod.HasValue ? "  /  " + Data.GetMethod(selMethod.Value).name : "  /  방식을 골라 주세요";
        UIKit.Label(root, summary, 0.03f, 0.07f, 0.55f, 0.20f, 40, TextAnchor.MiddleLeft);
        UIKit.Btn(root, "도감 [D]", 0.57f, 0.07f, 0.70f, 0.20f, ShowDex, UIKit.Muted, 30);
        bool canCook = sel.Count > 0 && selMethod.HasValue;
        UIKit.Btn(root, "조리 시작 [Enter]", 0.72f, 0.05f, 0.97f, 0.22f, StartCook, UIKit.Good, 40, canCook);
        scr = Scr.Day;
        confirm = canCook ? (System.Action)StartCook : null;
    }

    void ToggleIngredient(Ingredient ing)
    {
        if (sel.Contains(ing)) sel.Remove(ing);
        else
        {
            if (sel.Count >= 2) sel.RemoveAt(0);
            sel.Add(ing);
        }
        ShowDay();
    }

    // ---------------------------------------------------------------- 조리
    void StartCook()
    {
        var chosen = sel.ToArray();
        var md = Data.GetMethod(selMethod.Value);
        Clear();
        cook = new Cook(chosen, md, Balance.Ease(run.week));
        stirTimer = 0f;

        UIKit.Label(root, md.name + " 중", 0.03f, 0.88f, 0.5f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        timeLabel = UIKit.Label(root, "", 0.6f, 0.88f, 0.97f, 0.98f, 40, TextAnchor.MiddleRight, UIKit.Muted);
        UIKit.Label(root, guideMeal ? "[첫날 가이드] 아래 재료를 눌러 팬에 넣고, 색이 알맞게 변했을 때 다시 눌러 꺼내! 너무 오래 두면 탄다. " + md.guide : md.guide,
            0.03f, 0.78f, 0.97f, 0.88f, 32, TextAnchor.MiddleLeft, UIKit.Muted);

        // 자리: 아래 왼쪽 트레이 / 가운데 팬 / 아래 오른쪽 접시. 색(그림) 변화가 유일한 단서
        UIKit.Box(root, "Pan", 0.03f, 0.30f, 0.60f, 0.77f, new Color(0.25f, 0.2f, 0.18f));
        UIKit.Label(root, "팬", 0.03f, 0.70f, 0.60f, 0.77f, 28, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.5f));
        UIKit.Box(root, "Tray", 0.03f, 0.05f, 0.60f, 0.24f, new Color(0.45f, 0.33f, 0.22f));
        UIKit.Box(root, "Plate", 0.64f, 0.05f, 0.97f, 0.24f, new Color(0.45f, 0.5f, 0.55f));
        UIKit.Label(root, "접시", 0.64f, 0.19f, 0.97f, 0.24f, 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
        for (int i = 0; i < chosen.Length; i++)
        {
            int idx = i;
            var rt = UIKit.Box(root, "Item" + i, 0, 0, 0.1f, 0.1f, chosen[i].raw);
            var img = rt.GetComponent<Image>();
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => ItemClick(idx));
            bool hasArt = Art.HasSet(chosen[i]);
            img.preserveAspect = true;
            itemRects.Add(rt);
            itemImgs.Add(img);
            itemLabels.Add(UIKit.Label(rt, "", 0f, 0f, 1f, 0.26f, 30, TextAnchor.MiddleCenter, Color.white));
            swArt.Add(hasArt);
            PlaceItem(i);
        }

        // 불 조절
        UIKit.Label(root, "불 조절 (숫자키 1·2·3)", 0.64f, 0.70f, 0.97f, 0.77f, 30, TextAnchor.MiddleLeft, UIKit.Muted);
        for (int f = 0; f < 3; f++)
        {
            int level = f;
            float x0 = 0.64f + f * 0.11f;
            var b = UIKit.Btn(root, "[" + (f + 1) + "] " + Balance.FireName[f], x0, 0.57f, x0 + 0.10f, 0.69f, () => cook.SetFire(level), UIKit.Muted, 26);
            fireBtns.Add(b.GetComponent<Image>());
        }

        // 방식별 고유 조작
        if (md.id == Method.Grill)
        {
            flipBtn = UIKit.Btn(root, "뒤집기! [F]", 0.64f, 0.34f, 0.97f, 0.53f, () => cook.Flip(), UIKit.Good, 48);
            flipBtn.gameObject.SetActive(false);
        }
        else if (md.id == Method.Stirfry)
        {
            UIKit.Box(root, "StirArea", 0.64f, 0.34f, 0.97f, 0.53f, UIKit.Card);
            UIKit.Label(root, "마우스를 누른 채 좌우로 흔들기\n또는 ←→ / A·D 키를 번갈아", 0.65f, 0.35f, 0.96f, 0.52f, 30);
        }
        else
        {
            lidRect = UIKit.Box(root, "Lid", 0.64f, 0.34f, 0.97f, 0.53f, UIKit.Card);
            lidImage = lidRect.GetComponent<Image>();
            UIKit.Label(lidRect, "뚜껑 (꾹 누르기)\n또는 S 키를 누른 채", 0.05f, 0.05f, 0.95f, 0.95f, 32);
        }
        stateLabel = UIKit.Label(root, "", 0.64f, 0.27f, 0.97f, 0.34f, 30, TextAnchor.MiddleLeft, UIKit.Bad);

        UIKit.Btn(root, "팬의 재료 모두 꺼내기 [Space]", 0.03f, 0.245f, 0.60f, 0.295f, () => { if (cook != null) cook.TakeOutAll(); }, UIKit.Bad, 26);
        scr = Scr.Cook;
    }

    void ItemClick(int i)
    {
        if (cook == null) return;
        var it = cook.items[i];
        if (it.spot == Spot.Tray) cook.Put(i);
        else if (it.spot == Spot.Pan) cook.TakeOut(i);
    }

    // 재료 아이템을 지금 자리(트레이/팬/접시)의 칸으로 옮긴다.
    void PlaceItem(int i)
    {
        var it = cook.items[i];
        int n = cook.items.Length;
        float x0, x1, y0, y1;
        if (it.spot == Spot.Pan)
        {
            float w = 0.57f / n;
            x0 = 0.03f + i * w + w * 0.15f; x1 = 0.03f + (i + 1) * w - w * 0.15f; y0 = 0.33f; y1 = 0.69f;
        }
        else if (it.spot == Spot.Tray)
        {
            float w = 0.57f / n;
            x0 = 0.03f + i * w + w * 0.2f; x1 = 0.03f + (i + 1) * w - w * 0.2f; y0 = 0.06f; y1 = 0.23f;
        }
        else
        {
            float w = 0.33f / n;
            x0 = 0.64f + i * w + w * 0.1f; x1 = 0.64f + (i + 1) * w - w * 0.1f; y0 = 0.06f; y1 = 0.19f;
        }
        var rt = itemRects[i];
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        string key = i == 0 ? "Z" : "X";
        itemLabels[i].text = it.spot == Spot.Tray ? "[" + key + "] " + it.ing.name + " 넣기"
            : it.spot == Spot.Pan ? "[" + key + "] " + it.ing.name + " 꺼내기"
            : it.ing.name;
    }

    // 1~9 숫자키 (윗줄·숫자패드). 눌린 게 없으면 0.
    static int DigitPressed(Keyboard kb)
    {
        for (int i = 0; i < 9; i++)
        {
            if (((KeyControl)kb[(Key)((int)Key.Digit1 + i)]).wasPressedThisFrame) return i + 1;
            if (((KeyControl)kb[(Key)((int)Key.Numpad1 + i)]).wasPressedThisFrame) return i + 1;
        }
        return 0;
    }

    // 조리 중이 아닐 때의 키보드 입력
    void HandleMenuKeys(Keyboard kb)
    {
        if (modal != null)
        {
            if (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                kb.spaceKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) CloseModal();
            return;
        }
        if (kb.dKey.wasPressedThisFrame && (scr == Scr.Title || scr == Scr.Day || scr == Scr.Result)) { ShowDex(); return; }
        if (confirm != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
        {
            var act = confirm;
            confirm = null; // 한 번의 키 입력이 두 번 처리되지 않게
            act();
            return;
        }

        int n = DigitPressed(kb);
        bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        if (scr == Scr.Shop && n >= 1 && n <= Data.All.Length) ChangeCart(Data.All[n - 1], shift ? -1 : 1);
        else if (scr == Scr.Day && !guideMeal)
        {
            if (n >= 1 && n <= dayList.Count) ToggleIngredient(dayList[n - 1]);
            else if (kb.qKey.wasPressedThisFrame) { selMethod = Method.Grill; ShowDay(); }
            else if (kb.wKey.wasPressedThisFrame) { selMethod = Method.Stirfry; ShowDay(); }
            else if (kb.eKey.wasPressedThisFrame) { selMethod = Method.Boil; ShowDay(); }
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (cook == null)
        {
            if (kb != null) HandleMenuKeys(kb);
            return;
        }
        if (!cook.finished) // [꺼내기] 버튼은 finished만 켜므로, 아래 FinishCook은 항상 한 번 실행된다
        {
            var mouse = Mouse.current;
            bool held = mouse != null && mouse.leftButton.isPressed;
            bool lid = held && lidRect != null &&
                       RectTransformUtility.RectangleContainsScreenPoint(lidRect, mouse.position.ReadValue(), null);
            if (held && Mathf.Abs(mouse.delta.ReadValue().x) > 2f) stirTimer = 0.25f;
            else stirTimer -= Time.deltaTime;

            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ||
                    kb.aKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) stirTimer = 0.25f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) lid = true;
                if (kb.fKey.wasPressedThisFrame && cook.flipAvailable) cook.Flip();
                int n = DigitPressed(kb);
                if (n >= 1 && n <= 3) cook.SetFire(n - 1);
                if (kb.zKey.wasPressedThisFrame) ItemClick(0);
                if (kb.xKey.wasPressedThisFrame) ItemClick(1);
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) cook.TakeOutAll();
            }

            cook.Tick(Time.deltaTime, stirTimer > 0f, lid);
            RefreshCookUI();
        }
        if (cook.finished) FinishCook();
    }

    void RefreshCookUI()
    {
        for (int i = 0; i < itemImgs.Count; i++)
        {
            var it = cook.items[i];
            if (it.spot == Spot.Tray)
            {
                // 대기열(트레이)에 있는 동안은 장보기 아이콘을 쓴다. 팬에 넣어야 조리 중 그림으로 바뀐다.
                var shopS = Art.ShopIcon(it.ing);
                itemImgs[i].sprite = shopS;
                itemImgs[i].color = shopS != null ? Color.white : it.ing.raw;
            }
            else if (swArt[i])
            {
                itemImgs[i].sprite = Art.Get(it.ing, cook.Stage(it));
                itemImgs[i].color = Color.white;
            }
            else itemImgs[i].color = cook.Tone(it);
            PlaceItem(i);
        }
        for (int f = 0; f < fireBtns.Count; f++) fireBtns[f].color = f == cook.fire ? UIKit.Accent : UIKit.Muted;
        if (flipBtn != null) flipBtn.gameObject.SetActive(cook.flipAvailable);
        timeLabel.text = "남은 시간 " + Mathf.Max(0, Mathf.CeilToInt(Balance.MaxCookTime - cook.t)) + "초";
        if (lidImage != null) lidImage.color = cook.bubble ? new Color(1f, 0.6f, 0.4f) : UIKit.Card;
        stateLabel.text = cook.bubble ? "거품이 넘친다! 뚜껑을 눌러!" : (cook.stirring ? "젓는 중..." : "");
    }

    void FinishCook()
    {
        var used = cook.Used;                 // 접시에 올린 재료(소비됨). 트레이에 남은 재료는 쓰지 않은 것
        var ings = cook.GoodIngredients;      // 성공한 재료만 요리가 된다
        var lost = cook.LostIngredients;
        var stages = cook.items.Where(it => it.spot == Spot.Plate && it.ok).Select(it => cook.Stage(it)).ToArray();
        string fail = cook.FailText;
        var md = cook.m;
        int sat = cook.Satisfaction();
        cook = null;

        if (ings.Length == 0)
        {
            string why = used.Length == 0 ? null : fail;
            if (guideMeal)
            {
                ShowMessage(why == null ? "엄마: 재료를 팬에 넣고 꺼내야지! 다시 해 봐." : "엄마: " + why + "이 됐네! 첫날은 봐줄게. 다시 해 봐.", "다시 하기", ShowDay);
                return;
            }
            Consume(used);
            Starve(why == null ? "아무것도 만들지 못했다." : "요리가 " + why + "이 되어 못 먹었다.");
            return;
        }

        if (!guideMeal) Consume(used);
        var names = string.Join(" ", ings.Select(i => i.name));
        var dish = new Dish
        {
            name = md.verb + " " + names,
            key = string.Join("+", ings.Select(i => i.name).OrderBy(s => s)) + "/" + md.id,
            ings = ings,
            stages = stages,
            method = md.id,
            satisfaction = sat,
            note = lost.Length == 0 ? null : string.Join(", ", lost.Select(i => i.name)) + "은(는) 망쳐서 버렸다...",
        };
        run.dishes.Add(dish);
        bool isNew = !dex.ContainsKey(dish.key);
        if (isNew || dex[dish.key].satisfaction < sat) dex[dish.key] = dish;
        AiDishService.Start(this, dish, RefreshMeal); // 이름·설명·그림 요청. 실패해도 규칙 이름으로 계속 진행한다
        ShowMeal(dish, isNew);
    }

    void Consume(Ingredient[] ings)
    {
        if (guideMeal) return; // 첫날 달걀은 공짜
        foreach (var i in ings)
        {
            int q;
            if (!run.inv.TryGetValue(i, out q)) continue;
            if (q <= 1) { run.inv.Remove(i); run.expire.Remove(i); } else run.inv[i] = q - 1;
        }
    }

    // ---------------------------------------------------------------- 식사
    void ShowMeal(Dish dish, bool isNew)
    {
        Clear();
        string reaction = dish.satisfaction >= 80 ? "엄청 맛있다! 자취 만세!"
            : dish.satisfaction >= 60 ? "나쁘지 않은데?"
            : dish.satisfaction >= 40 ? "먹을 만은 하다..."
            : "일단 배는 채웠다.";
        UIKit.Label(root, run.week + "주차 " + run.day + "일차 식사", 0.03f, 0.88f, 0.5f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        // 왼쪽: 이름, 설명, 만족도. 오른쪽: 요리 그림 (재료가 있는 요리만)
        bool hasPic = dish.ings != null && dish.ings.Length > 0;
        float right = hasPic ? 0.64f : 0.97f;
        mealDish = dish;
        mealName = UIKit.Label(root, dish.name, 0.03f, 0.70f, right, 0.86f, 84);
        if (isNew) UIKit.Label(root, "도감에 새로 등록!", 0.03f, 0.64f, right, 0.70f, 36, TextAnchor.MiddleCenter, UIKit.Good);
        mealDesc = UIKit.Label(root, "", 0.03f, 0.50f, right, 0.64f, 34, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(root, "만족도 " + dish.satisfaction, 0.03f, 0.38f, right, 0.50f, 68, TextAnchor.MiddleCenter, UIKit.Accent);
        UIKit.Label(root, reaction, 0.03f, 0.29f, right, 0.38f, 44);
        if (!string.IsNullOrEmpty(dish.note)) UIKit.Label(root, dish.note, 0.03f, 0.21f, right, 0.29f, 30, TextAnchor.MiddleCenter, UIKit.Bad);
        mealImg = null; mealImgNote = null;
        if (hasPic)
        {
            var panel = UIKit.Box(root, "DishImage", 0.67f, 0.27f, 0.97f, 0.86f, UIKit.Card);
            mealImg = UIKit.Box(panel, "Img", 0.04f, 0.10f, 0.96f, 0.96f).gameObject.AddComponent<Image>();
            mealImg.preserveAspect = true;
            mealImg.raycastTarget = false;
            mealImgNote = UIKit.Label(panel, "", 0.04f, 0.01f, 0.96f, 0.10f, 26, TextAnchor.MiddleCenter, UIKit.Muted);
        }
        bool last = run.day >= Balance.Days;
        UIKit.Btn(root, (last ? "결산 보기" : "다음 날") + " [Enter]", 0.30f, 0.07f, 0.70f, 0.21f, NextDay, UIKit.Good, 48);
        scr = Scr.Meal;
        confirm = NextDay;
        RefreshMeal(dish); // 이미 받아 둔 이름·그림이 있으면 바로 채운다
    }

    // 식사 화면의 AI 영역. 응답이 늦게 와도 지금 그 요리의 식사 화면일 때만 바꾼다.
    Dish mealDish;
    Text mealName, mealDesc, mealImgNote;
    Image mealImg;

    void RefreshMeal(Dish d)
    {
        if (d != mealDish || scr != Scr.Meal) return;
        if (mealName != null) mealName.text = d.name;
        if (mealDesc != null) mealDesc.text = d.namePending ? "AI가 이름을 짓는 중..." : (d.desc ?? "");
        if (mealImg == null) return;
        if (d.image != null)
        {
            mealImg.sprite = d.image;
            mealImg.color = Color.white;
        }
        else
        {
            // 임시 그림: 첫 재료의 그림, 없으면 그 재료의 색
            var first = d.ings[0];
            var tmp = Art.Get(first, d.stages != null && d.stages.Length > 0 ? d.stages[0] : Doneness.Good);
            mealImg.sprite = tmp;
            mealImg.color = tmp != null ? Color.white : first.cooked;
        }
        if (mealImgNote != null) mealImgNote.text = d.imagePending ? "AI가 그림을 그리는 중..." : "";
    }

    // 굶음. 주마다 한 번은 엄마 몰래 비상 라면으로 넘어가고, 두 번째부터는 종료.
    void Starve(string reason)
    {
        if (run.strikes < Balance.Lifelines)
        {
            run.strikes++;
            var ramen = new Dish
            {
                name = "비상 라면", key = "비상 라면", ings = new Ingredient[0], method = Method.Boil,
                satisfaction = Balance.RamenSat,
                note = reason + " 결국 비상 라면으로 때웠다. (이번 주 비상 라면 사용)",
            };
            run.dishes.Add(ramen);
            ShowMeal(ramen, false);
        }
        else ShowFail(reason + " 비상 라면도 이미 먹었다. 굶었다...");
    }

    int NextBudget(float sat)
    {
        return Mathf.Clamp(Mathf.RoundToInt((Balance.BudgetBase + sat * Balance.BudgetPerSat) / 100f) * 100, Balance.BudgetMin, Balance.BudgetMax);
    }

    void NextWeek()
    {
        float sat = run.dishes.Count == 0 ? 0f : (float)run.dishes.Average(d => (float)d.satisfaction);
        run.week++;
        run.day = 1;
        run.strikes = 0;
        run.dishes.Clear();
        run.budget = NextBudget(sat);
        cart.Clear();
        sel.Clear();
        selMethod = null;
        ShowShop();
    }

    void NextDay()
    {
        run.day++;
        sel.Clear();          // 첫날 가이드의 달걀·굽기가 다음 날 미리 선택된 채로 남지 않게 한다
        selMethod = null;
        if (run.day > Balance.Days) ShowResult();
        else ShowDay();
    }

    // ---------------------------------------------------------------- 결산 / 실패
    void ShowResult()
    {
        Clear();
        float sat = (float)run.dishes.Average(d => (float)d.satisfaction);
        int combos = run.dishes.Where(d => d.key != "비상 라면").Select(d => d.key).Distinct().Count();
        float div = 100f * combos / Balance.Days;
        int leftover = run.inv.Values.Sum();
        float left = Mathf.Min(100f, 100f * leftover / Mathf.Max(1, run.bought));
        float total = sat * 0.5f + div * 0.3f + left * 0.2f;
        string grade = total >= Balance.GradeS ? "S" : total >= Balance.GradeA ? "A" : total >= Balance.GradeB ? "B" : "C";

        UIKit.Label(root, run.week + "주차 생존!", 0.05f, 0.84f, 0.95f, 0.98f, 90, TextAnchor.MiddleCenter, UIKit.Good);
        UIKit.Label(root, "지금까지 " + (run.week * Balance.Days) + "일 버텼다", 0.05f, 0.76f, 0.95f, 0.84f, 44, TextAnchor.MiddleCenter, UIKit.Ink);
        UIKit.Label(root, grade, 0.05f, 0.56f, 0.35f, 0.76f, 150, TextAnchor.MiddleCenter, UIKit.Accent);
        UIKit.Label(root, "이번 주 평가 (참고) " + Mathf.RoundToInt(total) + "점\n"
            + "만족도 " + Mathf.RoundToInt(sat) + " (50%)\n"
            + "다양성 " + Mathf.RoundToInt(div) + " (30%)  " + combos + "종류\n"
            + "남은 재료 " + Mathf.RoundToInt(left) + " (20%)  " + leftover + "개",
            0.40f, 0.52f, 0.97f, 0.84f, 44, TextAnchor.MiddleLeft);
        UIKit.Label(root, DishList(), 0.05f, 0.24f, 0.95f, 0.50f, 34, TextAnchor.UpperLeft);
        UIKit.Btn(root, "도감 [D]", 0.2f, 0.05f, 0.45f, 0.19f, ShowDex, UIKit.Muted, 40);
        UIKit.Label(root, "다음 주 예산 " + NextBudget(sat).ToString("N0") + "원 (재료값 +" + Mathf.RoundToInt((Balance.PriceGrowth - 1f) * 100f) + "%)", 0.05f, 0.195f, 0.95f, 0.245f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Btn(root, "다음 주 [Enter]", 0.52f, 0.05f, 0.85f, 0.19f, NextWeek, UIKit.Good, 40);
        scr = Scr.Result;
        confirm = NextWeek;
    }

    void ShowFail(string reason)
    {
        Clear();
        UIKit.Label(root, "굶었다...", 0.05f, 0.76f, 0.95f, 0.96f, 120, TextAnchor.MiddleCenter, UIKit.Bad);
        UIKit.Label(root, ((run.week - 1) * Balance.Days + run.day - 1) + "일 버텼다  (" + run.week + "주차 " + run.day + "일차에 실패)", 0.05f, 0.66f, 0.95f, 0.76f, 52, TextAnchor.MiddleCenter, UIKit.Ink);
        UIKit.Label(root, reason, 0.05f, 0.54f, 0.95f, 0.66f, 40, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(root, run.dishes.Count == 0 ? "먹은 요리가 없다." : "그때까지 먹은 요리\n" + DishList(),
            0.05f, 0.24f, 0.95f, 0.52f, 34, TextAnchor.UpperLeft);
        UIKit.Btn(root, "다시 하기 [Enter]", 0.30f, 0.05f, 0.70f, 0.19f, ShowTitle, UIKit.Good, 48);
        confirm = ShowTitle;
    }

    string DishList()
    {
        return string.Join("\n", run.dishes.Select((d, i) => (i + 1) + "일차  " + d.name + "  (" + d.satisfaction + ")"));
    }

    // ---------------------------------------------------------------- 공용
    void ShowMessage(string msg, string btn, System.Action onClick)
    {
        Clear();
        UIKit.Label(root, msg, 0.1f, 0.45f, 0.9f, 0.75f, 56);
        UIKit.Btn(root, btn + " [Enter]", 0.30f, 0.2f, 0.70f, 0.34f, onClick, UIKit.Good, 48);
        confirm = onClick;
    }

    void ShowDex()
    {
        CloseModal();
        modal = UIKit.Box(canvasRT, "Dex", 0.08f, 0.08f, 0.92f, 0.92f, UIKit.Card).gameObject;
        var m = modal.transform;
        UIKit.Label(m, "도감 (" + dex.Count + "종류)", 0.05f, 0.86f, 0.7f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Btn(m, "닫기 [Esc]", 0.74f, 0.88f, 0.95f, 0.98f, CloseModal, UIKit.Muted, 34);
        string body = dex.Count == 0
            ? "아직 만든 요리가 없다."
            : string.Join("\n", dex.Values.OrderByDescending(d => d.satisfaction)
                .Select(d => d.name + "   [" + Data.GetMethod(d.method).name + "]  최고 만족도 " + d.satisfaction));
        UIKit.Label(m, body, 0.05f, 0.05f, 0.95f, 0.84f, 40, TextAnchor.UpperLeft);
    }

    void CloseModal()
    {
        if (modal != null) Destroy(modal);
        modal = null;
    }
}
