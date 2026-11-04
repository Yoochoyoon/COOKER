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
    public Doneness[] stages; // ings와 같은 순서의 익힘 단계 (요리 그림에 쓴다)
    public Method method;
    public int satisfaction;
    public string note;      // 일부 재료를 망쳤을 때의 안내
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
    System.Action<int> modalPick; // 모달에서 숫자키로 고를 수 있을 때의 동작 (엄마의 전언)
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
    RectTransform panRect, trayRect, plateRect; // 재료를 놓는 자리이자, 끌어다 놓을 때 어디에 놓았는지 검사하는 칸
    bool cookArt; // 조리 화면을 그림(팬/웍/냄비 배경 + 버너바 + 팁)으로 그리는지
    readonly List<RectTransform> itemRects = new List<RectTransform>(); // 재료 아이템 (트레이/팬/접시를 오감)
    readonly List<Image> itemImgs = new List<Image>();
    readonly List<Text> itemLabels = new List<Text>();
    readonly List<Image> gaugeFills = new List<Image>(); // 재료별 익힘 게이지 (하양→노랑→주황→빨강, 주황일 때 꺼내면 된다)
    readonly List<bool> swArt = new List<bool>(); // 이 재료가 그림(5단계)으로 그려지는지
    readonly List<DragItem> itemDrags = new List<DragItem>();
    readonly List<Image> fireBtns = new List<Image>();
    RectTransform knobRT; // 가스 밸브 손잡이
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

        UIKit.Backdrop(canvasRT);
        bgImg = UIKit.Box(canvasRT, "BackgroundArt", 0, 0, 1, 1).gameObject.AddComponent<Image>(); // 화면별 배경 그림 (없으면 꺼 둔다)
        bgImg.raycastTarget = false;
        bgImg.enabled = false;
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
        lidImage = null;
        panRect = trayRect = plateRect = null;
        cookArt = false;
        mealDish = null;
        mealName = null;
        itemRects.Clear();
        itemImgs.Clear();
        itemLabels.Clear();
        gaugeFills.Clear();
        swArt.Clear();
        itemDrags.Clear();
        fireBtns.Clear();
        knobRT = null;
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
            TitleHit(area, "ExitHit", 0.26f, 0.09f, 0.775f, 0.2667f, exitImg, Quit);
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
        var rt = UIKit.Box(parent, name, x0, y0, x1, y1, new Color(1f, 1f, 1f, 0f), false);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = visual;
        var cb = b.colors;
        cb.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
        cb.pressedColor = new Color(0.75f, 0.75f, 0.75f);
        b.colors = cb;
        b.onClick.AddListener(() => onClick());
    }

    void Quit()
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

        // 왼쪽: 장보기 표
        UIKit.Box(root, "Panel", 0.02f, 0.03f, 0.545f, 0.97f, new Color(1f, 0.88f, 0.45f, 0.97f));
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
            UIKit.Box(root, "Row", 0.035f, bot, 0.53f, top, UIKit.Card, false);
            int qty;
            cart.TryGetValue(ing, out qty);
            int own;
            run.inv.TryGetValue(ing, out own);
            SetIcon(UIKit.Box(root, "Icon", 0.045f, bot + 0.008f, 0.095f, top - 0.008f).gameObject.AddComponent<Image>(), ing);
            UIKit.Label(root, "[" + (r + 1) + "] " + ing.name + (own > 0 ? " (보유 " + own + ")" : ""), 0.10f, bot, 0.235f, top, 34, TextAnchor.MiddleLeft);
            UIKit.Label(root, Price(ing).ToString("N0") + "원", 0.235f, bot, 0.325f, top, 34);
            UIKit.Label(root, ing.shelf + "일", 0.325f, bot, 0.395f, top, 34);
            UIKit.Btn(root, "-", 0.405f, bot + 0.012f, 0.445f, top - 0.012f, () => ChangeCart(ing, -1), UIKit.Muted, 44, qty > 0);
            UIKit.Label(root, qty.ToString(), 0.445f, bot, 0.485f, top, 38);
            UIKit.Btn(root, "+", 0.485f, bot + 0.012f, 0.525f, top - 0.012f, () => ChangeCart(ing, 1), UIKit.Accent, 44, Price(ing) <= left);
        }

        UIKit.Label(root, "담은 재료 " + units + "개", 0.04f, 0.04f, 0.25f, 0.16f, 38, TextAnchor.MiddleLeft);
        bool ok = units >= Need();
        UIKit.Btn(root, ok ? "구매 확정  [Enter]" : "최소 " + Need() + "개 필요", 0.27f, 0.05f, 0.525f, 0.15f, ConfirmShop, UIKit.Good, 40, ok);

        DrawBasket();
        scr = Scr.Shop;
        confirm = ok ? (System.Action)ConfirmShop : null;
    }

    // ---- 장바구니 연출
    // 재료 줄의 위쪽 위치(0~1). 날아가는 재료의 출발점도 이 값으로 정한다.
    static float RowTop(int r) { return 0.715f - r * 0.09f; }
    static readonly Vector2 BasketMouth = new Vector2(0.785f, 0.42f); // 재료가 떨어지는 장바구니 입구 (화면 비율)

    // 장바구니: shop_basket_back/front 그림이 둘 다 있으면 재료가 그 사이에 끼인 것처럼 그린다.
    // 하나짜리 shop_basket만 있으면 그 위에 얹고, 그림이 없으면 빨간 상자로 그린다.
    void DrawBasket()
    {
        basketRT = UIKit.Box(root, "Basket", 0.60f, 0.12f, 0.97f, 0.62f);
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
            float x = 0.13f + i * 0.125f; // 바구니 입구 안쪽에만 들어가게 작게
            float y = 0.50f;
            SetIcon(UIKit.Box(basketRT, "Item", x, y, x + 0.12f, y + 0.24f).gameObject.AddComponent<Image>(), list[i]);
        }
        if (back != null && front != null)
        {
            var fim = UIKit.Box(basketRT, "BasketFront", 0, 0, 1, 1).gameObject.AddComponent<Image>();
            fim.sprite = front;
            fim.preserveAspect = true;
            fim.raycastTarget = false;
        }
    }

    // 장보기 줄·장바구니·조리 대기열에 쓰는 아이콘(조리 중 그림과는 다른 그림). 없으면 그 재료의 색 사각형으로 대신한다.
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

    // 화면마다 배경 그림(Resources/Art/<파일>.png)을 깐다. 파일이 없으면 기본 물방울 무늬 배경이 보인다. 16:9 그림을 권장한다.
    void SetBg(string file)
    {
        if (bgImg == null) return;
        var s = file == null ? null : Art.Load(file);
        bgImg.sprite = s;
        bgImg.preserveAspect = true; // 창 비율이 달라도 그림이 늘어나지 않는다
        bgImg.enabled = s != null;
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

        // 왼쪽: 식탁(보유 재료) -> 도마(요리할 재료). 끌어다 놓아서 고르고, 도마에서 식탁으로 끌어 빼낸다
        UIKit.Tablecloth(root, 0.03f, 0.25f, 0.50f, 0.87f);
        var board = UIKit.CuttingBoard(root, 0.05f, 0.27f, 0.48f, 0.50f);
        if (sel.Count == 0) UIKit.Label(board, "재료를 여기로 끌어다 놓기", 0.05f, 0.2f, 0.85f, 0.8f, 34, TextAnchor.MiddleCenter, new Color(0.45f, 0.28f, 0.14f, 0.6f));
        if (guideMeal)
        {
            IngredientToken(Data.Egg, 1, null, 0.09f, 0.30f, 0.23f, 0.47f, board, true, false);
            UIKit.Box(root, "GuideNote", 0.06f, 0.54f, 0.47f, 0.84f, UIKit.Card); // 체크무늬 위에서도 읽히게 쪽지에 쓴다
            UIKit.Label(root, "엄마가 보낸 달걀!\n첫날은 달걀을 굽는다. 불 세기와 꺼내기를 익혀 보자.\n실패해도 굶지 않으니 다시 하면 돼!",
                0.08f, 0.56f, 0.45f, 0.82f, 34, TextAnchor.MiddleLeft);
        }
        else
        {
            int r = 0, onTable = 0, onBoard = 0;
            foreach (var kv in run.inv.OrderBy(k => DaysLeft(k.Key)))
            {
                var ing = kv.Key;
                dayList.Add(ing);
                r++;
                if (sel.Contains(ing))
                {
                    // 도마 위: 개수 제한 없이 한 줄 3칸씩 채우고, 많아지면 카드를 줄인다 (오른쪽 손잡이는 비워 둔다)
                    int cols = Mathf.Min(sel.Count, 3), rows = (sel.Count + 2) / 3;
                    float w = 0.33f / cols, h = 0.19f / rows;
                    float x0 = 0.075f + (onBoard % cols) * w, y1 = 0.48f - (onBoard / cols) * h;
                    IngredientToken(ing, kv.Value, r, x0 + 0.005f, y1 - h + 0.005f, x0 + w - 0.005f, y1 - 0.005f, board, true, true);
                    onBoard++;
                }
                else
                {
                    // 식탁 위 3칸 x 2줄
                    float x0 = 0.055f + (onTable % 3) * 0.143f, top = 0.79f - (onTable / 3) * 0.135f;
                    IngredientToken(ing, kv.Value, r, x0, top - 0.125f, x0 + 0.133f, top, board, false, true);
                    onTable++;
                }
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
        UIKit.Label(root, "엄마의 전화", 0.54f, 0.39f, 0.75f, 0.45f, 28, TextAnchor.MiddleLeft, UIKit.Muted);
        if (!guideMeal) UIKit.Btn(root, "엄마의 전언 [M]", 0.76f, 0.385f, 0.95f, 0.44f, ShowMomRecipes, UIKit.Accent, 28);
        UIKit.Label(root, hint, 0.54f, 0.26f, 0.95f, 0.38f, 34, TextAnchor.MiddleLeft);

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

    // 식탁/도마 위의 재료 카드. 끌어서 도마에 놓으면 고르고, 도마에서 밖으로 끌어내면 뺀다. 숫자키로도 된다.
    void IngredientToken(Ingredient ing, int qty, int? key, float x0, float y0, float x1, float y1, RectTransform board, bool onBoard, bool draggable)
    {
        int dl = run.expire.ContainsKey(ing) ? DaysLeft(ing) : 99;
        string tag = dl == 1 ? "오늘까지!" : dl >= 99 ? "" : "D-" + (dl - 1);
        RectTransform rt;
        if (onBoard)
        {
            // 도마 위에는 카드 없이 재료 그림만 올린다. 투명한 칸은 끌기용 손잡이다.
            rt = UIKit.Box(root, "Ing_" + ing.name, x0, y0, x1, y1, new Color(1f, 1f, 1f, 0f), false);
            SetIcon(UIKit.Box(rt, "Icon", 0f, 0f, 1f, 1f).gameObject.AddComponent<Image>(), ing);
        }
        else
        {
            var color = dl == 1 ? UIKit.Bad : UIKit.Accent;
            rt = UIKit.Box(root, "Ing_" + ing.name, x0, y0, x1, y1, color);
            SetIcon(UIKit.Box(rt, "Icon", 0.03f, 0.08f, 0.40f, 0.92f).gameObject.AddComponent<Image>(), ing); // 왼쪽에 재료 그림
            var name = UIKit.Label(rt, (key.HasValue ? "[" + key.Value + "] " : "") + ing.name, 0.40f, 0.48f, 0.97f, 0.92f, 34, TextAnchor.MiddleCenter, Color.white);
            name.gameObject.AddComponent<Outline>().effectColor = new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.8f);
            UIKit.Label(rt, "x" + qty + (tag == "" ? "" : "  " + tag), 0.40f, 0.08f, 0.97f, 0.48f, 26, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f));
        }
        if (!draggable) return;
        var click = rt.gameObject.AddComponent<Button>(); // 클릭해도 식탁 <-> 도마로 옮긴다
        click.targetGraphic = rt.GetComponent<Image>();
        click.transition = Selectable.Transition.None;
        click.onClick.AddListener(() => ToggleIngredient(ing));
        rt.gameObject.AddComponent<Bouncy>();
        var drag = rt.gameObject.AddComponent<DragItem>();
        drag.target = board;
        drag.onDrop = inside =>
        {
            if (inside != onBoard) ToggleIngredient(ing); // 식탁->도마면 고르기, 도마->밖이면 빼기
            else ShowDay();                               // 제자리에 놓으면 그대로
        };
    }

    void ToggleIngredient(Ingredient ing)
    {
        if (sel.Contains(ing)) sel.Remove(ing);
        else
        {
            sel.Add(ing);
        }
        ShowDay();
    }

    // ---------------------------------------------------------------- 조리
    const float RefW = 1920f, RefH = 1080f; // 기준 해상도. 픽셀로 계산한 뒤 화면 비율로 바꾼다

    static void SetSquare(RectTransform rt, Vector2 c, float s)
    {
        rt.anchorMin = new Vector2((c.x - s / 2f) / RefW, (c.y - s / 2f) / RefH);
        rt.anchorMax = new Vector2((c.x + s / 2f) / RefW, (c.y + s / 2f) / RefH);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void StartCook()
    {
        var chosen = sel.ToArray();
        var md = Data.GetMethod(selMethod.Value);
        Clear();
        cook = new Cook(chosen, md, Balance.Ease(run.week));
        stirTimer = 0f;
        momTips = chosen.Select(i => Data.MomHint(i)).Where(s => !string.IsNullOrEmpty(s))
            .Concat(new[] { MethodTip(md.id), "게이지가 주황색이 되면 바로 꺼내렴. 빨개지면 늦은 거다." }).ToArray();

        // 방식별 배경 그림(팬/웍/냄비 + 도마 + 접시)이 있으면 그걸로, 없으면 단색 칸으로 그린다.
        string bgFile = md.id == Method.Grill ? "bg_cook_grill" : md.id == Method.Stirfry ? "bg_cook_wok" : "bg_cook_pot";
        cookArt = Art.Load(bgFile) != null;
        if (cookArt) SetBg(bgFile);

        if (cookArt)
        {
            UIKit.Box(root, "TopNote", 0.185f, 0.83f, 0.975f, 0.98f, new Color(1f, 0.97f, 0.9f, 0.92f)); // 배경 그림 위에서도 글자가 읽히게
            UIKit.Label(root, md.name + " 중", 0.20f, 0.905f, 0.97f, 0.975f, 40, TextAnchor.MiddleLeft, UIKit.Accent);
            UIKit.Label(root, (guideMeal ? "[첫날 가이드] " : "") + md.guide + "  (모두 꺼내기 [Enter])", 0.20f, 0.84f, 0.97f, 0.90f, 20, TextAnchor.MiddleLeft, new Color(0.3f, 0.2f, 0.1f));

            panRect = UIKit.Box(root, "Pan", 0.056f, 0.247f, 0.500f, 0.975f);
            trayRect = UIKit.Box(root, "Tray", 0.576f, 0.602f, 0.971f, 0.977f);
            plateRect = UIKit.Box(root, "Plate", 0.651f, 0.241f, 0.920f, 0.572f);

            var timerBg = UIKit.Box(root, "TimerBg", 0.023f, 0.870f, 0.172f, 0.977f);
            timerBg.gameObject.AddComponent<Image>().sprite = Art.Load("cook_timer");
            timeLabel = UIKit.Label(timerBg, "", 0.30f, 0.08f, 0.96f, 0.92f, 30, TextAnchor.MiddleCenter, UIKit.Ink);

            var barBg = UIKit.Box(root, "BurnerBar", 0.023f, 0.019f, 0.586f, 0.171f);
            barBg.gameObject.AddComponent<Image>().sprite = Art.Load("cook_burner_bar");
            // 불 조절: 가스 밸브 손잡이를 돌린다(왼쪽 약불 · 위 중불 · 오른쪽 강불). 숫자키 1·2·3도 된다
            knobRT = UIKit.ToolImage(root, "Knob", UIKit.Knob(), 0, 0, 0, 0);
            SetSquare(knobRT, new Vector2(0.17f * RefW, 0.095f * RefH), 145f);
            knobRT.gameObject.AddComponent<KnobDrag>().onAngle = deg =>
            {
                if (cook != null) cook.SetFire(deg < -30f ? 0 : deg > 30f ? 2 : 1);
            };

            // 엄마의 조언 말풍선: 그림 비율(694x216)에 맞춘 칸을 오른쪽 아래에 둔다. 글씨는 왼쪽 "!" 아이콘과 오른쪽 꼬리를 피해 몸통 안에.
            var tipBg = UIKit.Box(root, "TipBg", 0.6636f, 0.012f, 0.985f, 0.19f);
            tipBg.gameObject.AddComponent<Image>().sprite = Art.Load("cook_tip");
            stateLabel = UIKit.Label(tipBg, "", 0.19f, 0.10f, 0.88f, 0.76f, 26, TextAnchor.MiddleLeft, UIKit.Ink);
        }
        else
        {
            UIKit.Label(root, md.name + " 중", 0.03f, 0.88f, 0.5f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
            UIKit.Label(root, guideMeal ? "[첫날 가이드] 재료를 팬에 넣고, 게이지가 주황일 때 꺼내! 너무 오래 두면 탄다. " + md.guide : md.guide,
                0.03f, 0.78f, 0.97f, 0.88f, 32, TextAnchor.MiddleLeft, UIKit.Muted);
            timeLabel = UIKit.Label(root, "", 0.6f, 0.88f, 0.97f, 0.98f, 40, TextAnchor.MiddleRight, UIKit.Muted);
            panRect = UIKit.Box(root, "Pan", 0.03f, 0.30f, 0.60f, 0.77f, new Color(0.25f, 0.2f, 0.18f));
            UIKit.Label(root, "팬", 0.03f, 0.70f, 0.60f, 0.77f, 28, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.5f));
            trayRect = UIKit.Box(root, "Tray", 0.03f, 0.05f, 0.60f, 0.24f, new Color(0.45f, 0.33f, 0.22f));
            plateRect = UIKit.Box(root, "Plate", 0.64f, 0.05f, 0.97f, 0.24f, new Color(0.45f, 0.5f, 0.55f));
            UIKit.Label(root, "접시", 0.64f, 0.19f, 0.97f, 0.24f, 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));

            UIKit.Label(root, "불 조절 (숫자키 1·2·3)", 0.64f, 0.70f, 0.97f, 0.77f, 30, TextAnchor.MiddleLeft, UIKit.Muted);
            for (int f = 0; f < 3; f++)
            {
                int level = f;
                float x0 = 0.64f + f * 0.11f;
                var b = UIKit.Btn(root, "[" + (f + 1) + "] " + Balance.FireName[f], x0, 0.57f, x0 + 0.10f, 0.69f, () => cook.SetFire(level), UIKit.Muted, 26);
                fireBtns.Add(b.GetComponent<Image>());
            }
            stateLabel = UIKit.Label(root, "", 0.64f, 0.27f, 0.97f, 0.34f, 30, TextAnchor.MiddleLeft, UIKit.Bad);
            UIKit.Btn(root, "팬의 재료 모두 꺼내기 [Enter]", 0.03f, 0.245f, 0.60f, 0.295f, () => { if (cook != null) cook.TakeOutAll(); }, UIKit.Bad, 26);
        }

        for (int i = 0; i < chosen.Length; i++)
        {
            int idx = i;
            var rt = UIKit.Box(root, "Item" + i, 0, 0, 0.1f, 0.1f, chosen[i].raw, false);
            var img = rt.GetComponent<Image>();
            img.type = Image.Type.Simple; // 재료 그림은 둥근 칸으로 자르지 않는다
            img.preserveAspect = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => ItemClick(idx)); // 클릭으로 바로 넣기/꺼내기
            var drag = rt.gameObject.AddComponent<DragItem>(); // 끌어다 놓기로도 옮긴다
            drag.canDrag = () => cook != null && cook.items[idx].spot != Spot.Plate;
            drag.onDropAt = pos => DropItem(idx, pos);
            itemDrags.Add(drag);
            itemRects.Add(rt);
            itemImgs.Add(img);
            var label = UIKit.Label(rt, "", 0f, 0f, 1f, 0.26f, 30, TextAnchor.MiddleCenter, Color.white);
            label.gameObject.AddComponent<Outline>().effectColor = new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.8f);
            itemLabels.Add(label);
            var gaugeBg = UIKit.Box(rt, "GaugeBg", 0.08f, 1.04f, 0.92f, 1.14f, new Color(0f, 0f, 0f, 0.45f), false); // 음식 그림 바로 위 (겹치지 않게)
            gaugeFills.Add(UIKit.Box(gaugeBg, "GaugeFill", 0f, 0f, 0f, 1f, Color.white, false).GetComponent<Image>());
            swArt.Add(Art.HasSet(chosen[i]));
            PlaceItem(i);
        }

        // 방식별 고유 조작 (버너바 오른쪽 칸)
        if (md.id == Method.Grill)
        {
            if (cookArt)
            {
                var fbRect = UIKit.Box(root, "FlipBtnBg", 0.335f, 0.034f, 0.566f, 0.156f);
                var fbImg = fbRect.gameObject.AddComponent<Image>();
                fbImg.sprite = Art.Load("cook_flip_btn");
                fbImg.type = Image.Type.Sliced;
                flipBtn = fbRect.gameObject.AddComponent<Button>();
                flipBtn.targetGraphic = fbImg;
                flipBtn.onClick.AddListener(() => cook.Flip());
                fbRect.gameObject.AddComponent<Bouncy>();
                UIKit.Label(fbRect, "뒤집기! [Space]", 0.04f, 0.04f, 0.96f, 0.96f, 26, TextAnchor.MiddleCenter, Color.white);
            }
            else
            {
                flipBtn = UIKit.Btn(root, "뒤집기! [Space]", 0.64f, 0.34f, 0.97f, 0.53f, () => cook.Flip(), UIKit.Good, 48);
            }
            flipBtn.gameObject.SetActive(false);
        }
        else if (md.id == Method.Stirfry)
        {
            var area = cookArt ? UIKit.Box(root, "StirArea", 0.335f, 0.034f, 0.566f, 0.156f, UIKit.Card)
                : UIKit.Box(root, "StirArea", 0.64f, 0.34f, 0.97f, 0.53f, UIKit.Card);
            var areaBtn = area.gameObject.AddComponent<Button>();
            areaBtn.targetGraphic = area.GetComponent<Image>();
            areaBtn.onClick.AddListener(() => stirTimer = 0.25f); // 클릭 연타로도 젓는다
            area.gameObject.AddComponent<Bouncy>();
            UIKit.Label(area, "빠르게 연타!\n(클릭 또는 Space)", 0.04f, 0.06f, 0.96f, 0.94f, cookArt ? 18 : 30);
        }
        else
        {
            lidRect = cookArt ? UIKit.Box(root, "Lid", 0.335f, 0.034f, 0.566f, 0.156f, UIKit.Card)
                : UIKit.Box(root, "Lid", 0.64f, 0.34f, 0.97f, 0.53f, UIKit.Card);
            lidImage = lidRect.GetComponent<Image>();
            UIKit.Label(lidRect, "뚜껑 (꾹 누르기)\n또는 Space를 누른 채", 0.05f, 0.05f, 0.95f, 0.95f, cookArt ? 18 : 32);
        }

        scr = Scr.Cook;
    }

    // 클릭과 Z X C V B N 키: 넣기/꺼내기를 바로 토글한다.
    void ItemClick(int i)
    {
        if (cook == null || i >= cook.items.Length) return;
        var it = cook.items[i];
        if (it.spot == Spot.Tray) cook.Put(i);
        else if (it.spot == Spot.Pan) cook.TakeOut(i);
    }

    // 끌어다 놓기: 트레이 -> 팬이면 넣고, 팬 -> 접시/트레이면 꺼낸다. 엉뚱한 곳이면 제자리로 돌아간다.
    void DropItem(int i, Vector2 pos)
    {
        if (cook == null) return;
        var it = cook.items[i];
        if (it.spot == Spot.Tray && Over(panRect, pos)) cook.Put(i);
        else if (it.spot == Spot.Pan && (Over(plateRect, pos) || Over(trayRect, pos))) cook.TakeOut(i);
        PlaceItem(i);
    }

    static bool Over(RectTransform rt, Vector2 screenPos)
    {
        return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);
    }

    // 재료 아이템을 지금 자리(트레이/팬/접시)의 칸으로 옮긴다. 같은 자리의 재료끼리 칸을 나눠 쓴다.
    void PlaceItem(int i)
    {
        if (i < itemDrags.Count && itemDrags[i].dragging) return; // 끄는 중에는 마우스를 따라가게 둔다
        var it = cook.items[i];
        int n = 0, k = 0; // 같은 자리에 있는 재료 수와 그중 이 재료의 순서
        for (int j = 0; j < cook.items.Length; j++)
            if (cook.items[j].spot == it.spot) { if (j < i) k++; n++; }
        float x0, x1, y0, y1;
        if (cookArt)
        {
            // 그림의 팬/트레이/접시 영역 안쪽으로 좁혀서(팬·접시는 둥글어서 모서리가 비어 있다) 격자로 나눠 놓는다.
            var zone = it.spot == Spot.Pan ? panRect : it.spot == Spot.Tray ? trayRect : plateRect;
            float zx0 = zone.anchorMin.x, zx1 = zone.anchorMax.x, zy0 = zone.anchorMin.y, zy1 = zone.anchorMax.y;
            float insetX = it.spot == Spot.Tray ? 0.10f : 0.22f;
            float insetY = it.spot == Spot.Tray ? 0.08f : 0.20f;
            float zw = zx1 - zx0, zh = zy1 - zy0;
            zx0 += zw * insetX; zx1 -= zw * insetX;
            zy0 += zh * insetY; zy1 -= zh * insetY;
            float itemW = it.spot == Spot.Tray ? 0.065f : 0.095f;
            float itemH = it.spot == Spot.Tray ? 0.085f : 0.125f;
            int cols = it.spot == Spot.Tray ? Mathf.Min(n, 3) : Mathf.CeilToInt(Mathf.Sqrt(n));
            int rows = Mathf.CeilToInt(n / (float)cols);
            float cellW = (zx1 - zx0) / cols, cellH = (zy1 - zy0) / rows;
            float cx = zx0 + (k % cols + 0.5f) * cellW, cy = zy1 - (k / cols + 0.5f) * cellH;
            float s = Mathf.Min(1f, Mathf.Min(cellW * 0.9f / itemW, cellH * 0.9f / itemH));
            float w = itemW * s, h = itemH * s;
            x0 = cx - w * 0.5f; x1 = cx + w * 0.5f; y0 = cy - h * 0.5f; y1 = cy + h * 0.5f;
        }
        else if (it.spot == Spot.Pan)
        {
            float w = 0.57f / n;
            x0 = 0.03f + k * w + w * 0.15f; x1 = 0.03f + (k + 1) * w - w * 0.15f; y0 = 0.33f; y1 = 0.69f;
        }
        else if (it.spot == Spot.Tray)
        {
            float w = 0.57f / n;
            x0 = 0.03f + k * w + w * 0.2f; x1 = 0.03f + (k + 1) * w - w * 0.2f; y0 = 0.06f; y1 = 0.23f;
        }
        else
        {
            float w = 0.33f / n;
            x0 = 0.64f + k * w + w * 0.1f; x1 = 0.64f + (k + 1) * w - w * 0.1f; y0 = 0.06f; y1 = 0.19f;
        }
        var rt = itemRects[i];
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        string key = i < 6 ? "ZXCVBN".Substring(i, 1) : "-";
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
        if (modal != null && dexSearch != null && dexSearch.isFocused) // 검색창에 글자를 치는 중에는 단축키를 막는다
        {
            if (kb.escapeKey.wasPressedThisFrame) EventSystem.current.SetSelectedGameObject(null);
            return;
        }
        if (dexDetail != null)
        {
            if (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                kb.spaceKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) CloseDexDetail();
            return;
        }
        if (modal != null)
        {
            int pick = DigitPressed(kb);
            if (modalPick != null && pick >= 1) { modalPick(pick - 1); return; }
            if (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                kb.spaceKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.mKey.wasPressedThisFrame) CloseModal();
            return;
        }
        if (kb.dKey.wasPressedThisFrame && (scr == Scr.Title || scr == Scr.Day || scr == Scr.Result)) { ShowDex(); return; }
        if (kb.mKey.wasPressedThisFrame && scr == Scr.Day && !guideMeal) { ShowMomRecipes(); return; }
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
            stirTimer -= Time.deltaTime; // 젓기는 연타로 채운다(젓기 칸 클릭/Space 탭). 가만히 누르고만 있으면 식는다.

            if (kb != null)
            {
                // 뒤집기·젓기는 Space를 "누를 때마다"(연타), 뚜껑은 Space를 "누르고 있는 동안"
                if (kb.spaceKey.wasPressedThisFrame)
                {
                    if (cook.flipAvailable) cook.Flip();
                    if (cook.m.id == Method.Stirfry) stirTimer = 0.25f;
                }
                if (kb.spaceKey.isPressed && cook.m.id == Method.Boil) lid = true;
                int n = DigitPressed(kb);
                if (n >= 1 && n <= 3) cook.SetFire(n - 1);
                // 재료가 몇 개든 Z X C V B N 순서로 넣고 꺼낸다
                var itemKeys = new[] { kb.zKey, kb.xKey, kb.cKey, kb.vKey, kb.bKey, kb.nKey };
                for (int k = 0; k < itemKeys.Length && k < cook.items.Length; k++)
                    if (itemKeys[k].wasPressedThisFrame) ItemClick(k);
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) cook.TakeOutAll();
            }

            cook.Tick(Time.deltaTime, stirTimer > 0f, lid);
            RefreshCookUI();
        }
        if (cook.finished) FinishCook();
    }

    // 익힘 게이지 색. 하양(날것) → 노랑(덜 익음) → 주황(적정, 지금 빼면 된다) → 빨강(많이 익음·탄 것)
    Color GaugeColor(CookItem it)
    {
        float d = it.d;
        if (d < Balance.RawLimit) return new Color(0.95f, 0.95f, 0.9f);
        if (d < cook.Lo) return new Color(1f, 0.85f, 0.2f);
        if (d <= cook.Hi(it)) return new Color(1f, 0.55f, 0.1f);
        return new Color(0.85f, 0.15f, 0.1f);
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
            var gfill = gaugeFills[i].rectTransform;
            gfill.anchorMin = Vector2.zero;
            gfill.anchorMax = new Vector2(Mathf.Clamp01(it.d / 100f), 1f);
            gfill.offsetMin = Vector2.zero;
            gfill.offsetMax = Vector2.zero;
            gaugeFills[i].color = GaugeColor(it);
            PlaceItem(i);
        }
        for (int f = 0; f < fireBtns.Count; f++) fireBtns[f].color = f == cook.fire ? UIKit.Accent : UIKit.Muted; // 그림 없는 화면의 불 세기 버튼
        if (knobRT != null) knobRT.localRotation = Quaternion.Euler(0, 0, -(cook.fire - 1) * 60f); // 밸브 표시가 지금 불 세기 방향을 가리킨다
        if (flipBtn != null) flipBtn.gameObject.SetActive(cook.flipAvailable);
        timeLabel.text = "남은 시간 " + Mathf.Max(0, Mathf.CeilToInt(Balance.MaxCookTime - cook.t)) + "초";
        if (lidImage != null) lidImage.color = cook.bubble ? new Color(1f, 0.6f, 0.4f) : UIKit.Card;
        string warn = cook.bubble ? "거품이 넘친다! 뚜껑을 눌러!"
            : cook.flipAvailable ? "지금 뒤집어야 한다!"
            : (cook.m.id == Method.Stirfry && cook.AnyInPan && !cook.stirring) ? "안 저으면 눌어붙는다!"
            : (cook.m.id == Method.Grill && cook.flipMissed && !cook.flipped) ? "뒤집을 때를 놓쳤다... 한쪽만 익었다"
            : (cook.m.id == Method.Boil && cook.hadOverflow) ? "국이 끓어넘쳤다... 맛이 줄었다"
            : null;
        // 급한 일이 없으면 엄마의 조언이 몇 초마다 바뀌며 계속 나온다
        stateLabel.text = warn ?? (momTips.Length == 0 ? "" : "엄마: " + momTips[(int)(cook.t / 4f) % momTips.Length]);
        stateLabel.color = warn != null ? UIKit.Bad : UIKit.Ink;
    }

    string[] momTips = new string[0]; // 이번 조리의 엄마 조언 (재료별 한마디 + 방식별 요령)

    static string MethodTip(Method m)
    {
        return m == Method.Grill ? "뒤집기 버튼이 뜨면 놓치지 말고 바로 뒤집어라."
            : m == Method.Stirfry ? "손 쉬지 말고 계속 저어라. 멈추면 바로 눌어붙어."
            : "거품이 올라오면 뚜껑을 꾹 눌러서 가라앉혀라.";
    }

    void FinishCook()
    {
        var used = cook.Used;                 // 접시에 올린 재료(소비됨). 트레이에 남은 재료는 쓰지 않은 것
        var ings = cook.GoodIngredients;      // 성공한 재료만 요리가 된다
        var usedStages = cook.UsedStages;
        var stages = cook.GoodStages;
        var lost = cook.LostIngredients;
        string fail = cook.FailText;
        var md = cook.m;
        int sat = cook.Satisfaction();
        cook = null;

        if (ings.Length == 0)
        {
            string why = used.Length == 0 ? null : fail;
            if (guideMeal)
            {
                if (why == null) ShowMessage("엄마: 재료를 팬에 넣고 꺼내야지! 다시 해 봐.", "다시 하기", ShowDay);
                else ShowRuined(used, usedStages, md, "엄마: " + why + "이 됐네! 첫날은 봐줄게. 다시 해 봐.", "다시 하기", ShowDay);
                return;
            }
            Consume(used);
            if (why == null) Starve("아무것도 만들지 못했다.");
            else ShowRuined(used, usedStages, md, "요리가 " + why + "이 되어 못 먹었다...", "계속", () => Starve("요리가 " + why + "이 되어 못 먹었다."));
            return;
        }

        if (!guideMeal) Consume(used);
        var dish = new Dish
        {
            name = Recipes.NameOf(ings, md.id), // 우선 아는 요리 이름. AI 이름이 오면 바꾼다

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
        ShowMeal(dish, isNew);
        DishNamer.Request(this, ings, stages, md.id, name =>
        {
            dish.name = name;
            if (mealDish == dish && mealName != null) mealName.text = name; // 아직 그 식사 화면이면 바로 바꾼다
            RefreshDexDetail(dish);
        });
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
    Dish mealDish;  // 지금 식사 화면에 떠 있는 요리 (AI 이름이 늦게 와도 그 화면일 때만 바꾼다)
    Text mealName;

    void ShowMeal(Dish dish, bool isNew)
    {
        Clear();
        string reaction = dish.satisfaction >= 80 ? "엄청 맛있다! 자취 만세!"
            : dish.satisfaction >= 60 ? "나쁘지 않은데?"
            : dish.satisfaction >= 40 ? "먹을 만은 하다..."
            : "일단 배는 채웠다.";
        UIKit.Label(root, run.week + "주차 " + run.day + "일차 식사", 0.03f, 0.88f, 0.5f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        // 요리 그림이 있으면 왼쪽에 그림, 오른쪽에 글을 놓는다 (비상 라면은 재료가 없어 그림 없음)
        bool photo = dish.ings.Length > 0 && dish.stages != null;
        if (photo) Photo(root, 0.04f, 0.23f, 0.40f, 0.87f, dish.ings, dish.stages, dish.method);
        float x0 = photo ? 0.42f : 0.05f, x1 = photo ? 0.97f : 0.95f;
        mealDish = dish;
        mealName = UIKit.Label(root, dish.name, x0, 0.62f, x1, 0.80f, 100);
        if (isNew) UIKit.Label(root, "도감에 새로 등록!", photo ? x0 : 0.3f, 0.54f, photo ? x1 : 0.7f, 0.62f, 40, TextAnchor.MiddleCenter, UIKit.Good);
        UIKit.Label(root, "만족도 " + dish.satisfaction, photo ? x0 : 0.2f, 0.40f, photo ? x1 : 0.8f, 0.54f, 80, TextAnchor.MiddleCenter, UIKit.Accent);
        UIKit.Label(root, reaction, photo ? x0 : 0.1f, 0.28f, photo ? x1 : 0.9f, 0.40f, 52);
        if (!string.IsNullOrEmpty(dish.note)) UIKit.Label(root, dish.note, photo ? x0 : 0.1f, 0.21f, photo ? x1 : 0.9f, 0.28f, 32, TextAnchor.MiddleCenter, UIKit.Bad);
        bool last = run.day >= Balance.Days;
        UIKit.Btn(root, (last ? "결산 보기" : "다음 날") + " [Enter]", 0.30f, 0.07f, 0.70f, 0.21f, NextDay, UIKit.Good, 48);
        scr = Scr.Meal;
        confirm = NextDay;
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
        UIKit.Label(root, grade, 0.05f, 0.62f, 0.35f, 0.84f, 180, TextAnchor.MiddleCenter, UIKit.Accent);
        UIKit.Label(root, "총점 " + Mathf.RoundToInt(total) + "점\n"
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
        UIKit.Label(root, run.week + "주차 " + run.day + "일차에 실패  (총 " + ((run.week - 1) * Balance.Days + run.day - 1) + "일 버팀)", 0.05f, 0.66f, 0.95f, 0.76f, 48);
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

    // 망친 요리. 탄/날것 상태 그대로의 그림을 보여 주고 다음으로 넘어간다.
    void ShowRuined(Ingredient[] used, Doneness[] stages, MethodDef md, string msg, string btn, System.Action next)
    {
        Clear();
        string how = stages.Contains(Doneness.Burnt) ? "새까맣게 탄 " : "날것 그대로인 ";
        Photo(root, 0.04f, 0.23f, 0.40f, 0.87f, used, stages, md.id, false); // 망친 요리는 AI 그림을 만들지 않는다
        UIKit.Label(root, how + md.verb + " " + string.Join(" ", used.Select(i => i.name)), 0.42f, 0.62f, 0.97f, 0.80f, 90, TextAnchor.MiddleCenter, UIKit.Bad);
        UIKit.Label(root, msg, 0.42f, 0.34f, 0.97f, 0.60f, 52);
        UIKit.Btn(root, btn + " [Enter]", 0.30f, 0.07f, 0.70f, 0.21f, next, UIKit.Good, 48);
        confirm = next;
    }

    // 요리 그림 칸. 먼저 대체 그림을 보여 주고, AI 그림이 도착하면 바꿔 끼운다.
    // generate가 false면(망친 요리) AI에게 그리게 하지 않고 재료를 그 익힘 색으로 칠한 대체 그림만 보여 준다. AI 그림은 도감에 들어가는 요리만 만든다.
    void Photo(Transform parent, float x0, float y0, float x1, float y1, Ingredient[] ings, Doneness[] stages, Method m, bool generate = true)
    {
        if (!generate)
        {
            var plain = UIKit.Box(parent, "DishPhoto", x0, y0, x1, y1, Color.white);
            plain.localRotation = Quaternion.Euler(0, 0, -2f);
            var plainArea = UIKit.Box(plain, "PicArea", 0.04f, 0.04f, 0.96f, 0.97f);
            var plainPic = UIKit.Box(plainArea, "Pic", 0, 0, 1, 1);
            var plainFit = plainPic.gameObject.AddComponent<AspectRatioFitter>();
            plainFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            plainFit.aspectRatio = 1f;
            var plainImg = plainPic.gameObject.AddComponent<RawImage>();
            plainImg.raycastTarget = false;
            plainImg.texture = DishPhoto.Placeholder(ings, stages);
            return;
        }
        var frame = UIKit.Box(parent, "DishPhoto", x0, y0, x1, y1, Color.white); // 폴라로이드 사진처럼 흰 테두리
        frame.localRotation = Quaternion.Euler(0, 0, -2f);
        var area = UIKit.Box(frame, "PicArea", 0.04f, 0.19f, 0.96f, 0.97f); // 비율 맞춤(FitInParent)은 부모 전체를 쓰므로 칸을 따로 둔다
        var pic = UIKit.Box(area, "Pic", 0, 0, 1, 1);
        var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = 1f;
        var img = pic.gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;
        var placeholder = DishPhoto.Placeholder(ings, stages);
        img.texture = placeholder;
        var status = UIKit.Label(frame, "로딩중...", 0.04f, 0.075f, 0.96f, 0.18f, 48, TextAnchor.MiddleCenter, UIKit.Ink);
        var bar = UIKit.Box(frame, "LoadBar", 0.08f, 0.025f, 0.92f, 0.065f, UIKit.Muted);
        var fill = UIKit.Box(bar, "Fill", 0, 0, 0, 1, UIKit.Accent, false);
        bool finished = false;
        string retry = null; // 실패해서 다시 시도하는 중이면 그 안내
        StartCoroutine(LoadingBar(status, fill, () => finished, () => retry));
        // 생성은 성공할 때까지 뒤에서 계속 다시 시도한다. 화면을 넘겨도 계속 만들어 저장해 두고, 다음에 같은 요리면 바로 나온다
        DishPhoto.Request(ings, stages, m, tex =>
        {
            finished = true;
            if (img == null) { Destroy(placeholder); return; } // 그새 화면이 바뀌었다
            bar.gameObject.SetActive(false);
            img.texture = tex;
            Destroy(placeholder);
            status.text = "";
        }, msg => retry = msg);
    }

    // 실제 진행률은 알 수 없어서, 예상 시간 기준으로 차오르다 95%에서 기다린다. 재시도 중이면 그 안내를 보여 준다.
    System.Collections.IEnumerator LoadingBar(Text status, RectTransform fill, System.Func<bool> finished, System.Func<string> retry)
    {
        float t = 0f, expected = DishPhoto.ExpectedSeconds;
        while (!finished() && fill != null)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Min(0.95f, 1f - Mathf.Exp(-2f * t / expected));
            fill.anchorMax = new Vector2(p, 1f);
            status.text = retry() ?? "로딩중... " + Mathf.RoundToInt(p * 100f) + "% (" + Mathf.FloorToInt(t) + "초)";
            yield return null;
        }
    }

    // ---------------------------------------------------------------- 공용
    void ShowMessage(string msg, string btn, System.Action onClick)
    {
        Clear();
        UIKit.Label(root, msg, 0.1f, 0.45f, 0.9f, 0.75f, 56);
        UIKit.Btn(root, btn + " [Enter]", 0.30f, 0.2f, 0.70f, 0.34f, onClick, UIKit.Good, 48);
        confirm = onClick;
    }

    // ---------------------------------------------------------------- 도감
    // 만들 수 있는 모든 조합(재료 1~2개 x 방식 3개)을 칸으로 보여 주고, 만든 것만 채운다.
    // 칸의 key는 요리를 만들 때 쓰는 Dish.key와 같은 규칙이다. 재료 3개 이상으로 만든 요리는 만든 뒤에 칸이 덧붙는다.
    class DexSlot { public string key; public Ingredient[] ings; public Method method; }
    static List<DexSlot> dexSlots;

    static List<DexSlot> DexSlots()
    {
        if (dexSlots != null) return dexSlots;
        dexSlots = new List<DexSlot>();
        var combos = new List<Ingredient[]>();
        foreach (var a in Data.All) combos.Add(new[] { a });
        for (int i = 0; i < Data.All.Length; i++)
            for (int j = i + 1; j < Data.All.Length; j++) combos.Add(new[] { Data.All[i], Data.All[j] });
        foreach (var md in Data.Methods)
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

    static string Stars(int sat)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(sat / 20f), 0, 5);
        return new string('★', n) + new string('☆', 5 - n);
    }

    // 요리 그림. 먼저 재료 그림을 가로로 나란히 놓고, 만든 요리면 AI 요리 사진(저장해 둔 것)이 오면 바꿔 끼운다.
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

        if (photoState != null) photoState(false);
        DishPhoto.Request(d.ings, d.stages, d.method, tex =>
        {
            if (parent == null) return; // 그새 도감을 닫았다
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
        });
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
        var m = modal.transform;
        UIKit.Label(m, "도감 (" + found + " / " + slots.Count + ")", 0.05f, 0.88f, 0.45f, 0.98f, 60, TextAnchor.MiddleLeft, UIKit.Accent);
        dexInfo = UIKit.Label(m, "", 0.40f, 0.88f, 0.74f, 0.98f, 28, TextAnchor.MiddleRight, UIKit.Muted);
        UIKit.Btn(m, "닫기 [Esc]", 0.76f, 0.89f, 0.96f, 0.98f, CloseModal, UIKit.Muted, 34);

        // 1줄: 정렬 버튼 + 검색창
        DexChip(m, "만족도 높은 순", 0.02f, 0.80f, 0.17f, 0.87f, () => dexSort == 1, () => dexSort = dexSort == 1 ? 0 : 1);
        DexChip(m, "만족도 낮은 순", 0.175f, 0.80f, 0.325f, 0.87f, () => dexSort == 2, () => dexSort = dexSort == 2 ? 0 : 2);
        var fieldRT = UIKit.Box(m, "Search", 0.35f, 0.80f, 0.98f, 0.87f, Color.white);
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

        // 2줄: 재료 칩(여러 개 고르면 모두 들어간 요리) + 방식 칩
        UIKit.Label(m, "재료", 0.02f, 0.72f, 0.065f, 0.79f, 28, TextAnchor.MiddleCenter, UIKit.Muted);
        for (int i = 0; i < Data.All.Length; i++)
        {
            var ing = Data.All[i];
            float x = 0.07f + i * 0.095f;
            DexChip(m, ing.name, x, 0.72f, x + 0.09f, 0.79f, () => dexIngs.Contains(ing),
                () => { if (!dexIngs.Remove(ing)) dexIngs.Add(ing); });
        }
        UIKit.Label(m, "방식", 0.65f, 0.72f, 0.70f, 0.79f, 28, TextAnchor.MiddleCenter, UIKit.Muted);
        for (int i = 0; i < Data.Methods.Length; i++)
        {
            int mi = i;
            float x = 0.71f + i * 0.095f;
            DexChip(m, Data.Methods[i].name, x, 0.72f, x + 0.09f, 0.79f, () => dexMethod == mi, () => dexMethod = dexMethod == mi ? -1 : mi);
        }

        // 스크롤 영역: 5열 그리드
        var view = UIKit.Box(m, "Scroll", 0.02f, 0.02f, 0.98f, 0.70f);
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
        grid.spacing = new Vector2(20, 20);
        grid.padding = new RectOffset(20, 20, 10, 20);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.childAlignment = TextAnchor.UpperCenter;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
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
        var b = card.gameObject.AddComponent<Button>();
        b.targetGraphic = card.GetComponent<Image>();
        var cb = b.colors;
        cb.highlightedColor = new Color(1f, 0.92f, 0.7f);
        cb.pressedColor = new Color(0.9f, 0.8f, 0.55f);
        b.colors = cb;
        b.onClick.AddListener(() => ShowDexDetail(d));
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
        var panel = UIKit.Box(card, "Pic", 0.04f, 0.18f, 0.44f, 0.94f, Color.white);
        dtPic = UIKit.Box(panel, "Img", 0.04f, 0.10f, 0.96f, 0.96f);
        dtImgNote = UIKit.Label(panel, "", 0.04f, 0.01f, 0.96f, 0.10f, 26, TextAnchor.MiddleCenter, UIKit.Muted);
        dtName = UIKit.Label(card, "", 0.48f, 0.76f, 0.96f, 0.94f, 64, TextAnchor.MiddleLeft);
        UIKit.Label(card, string.Join(" ", d.ings.Select(i => Data.MomHint(i))), 0.48f, 0.56f, 0.96f, 0.76f, 30, TextAnchor.UpperLeft, UIKit.Muted); // 엄마 한마디

        string ings = string.Join(", ", d.ings.Select((i, k) =>
            i.name + (d.stages != null && k < d.stages.Length ? "(" + StageName[(int)d.stages[k]] + ")" : "")).ToArray());
        UIKit.Label(card, "재료  " + ings, 0.48f, 0.44f, 0.96f, 0.56f, 36, TextAnchor.MiddleLeft);
        UIKit.Label(card, "방식  " + Data.GetMethod(d.method).name, 0.48f, 0.34f, 0.96f, 0.44f, 36, TextAnchor.MiddleLeft);
        UIKit.Label(card, "최고 만족도  " + d.satisfaction + "   " + Stars(d.satisfaction), 0.48f, 0.22f, 0.96f, 0.34f, 40, TextAnchor.MiddleLeft, UIKit.Accent);
        if (!string.IsNullOrEmpty(d.note)) UIKit.Label(card, d.note, 0.04f, 0.08f, 0.62f, 0.18f, 28, TextAnchor.MiddleLeft, UIKit.Bad);
        UIKit.Btn(card, "닫기 [Esc]", 0.70f, 0.04f, 0.96f, 0.16f, CloseDexDetail, UIKit.Muted, 34);
        BuildDexPic(dtPic, d.ings, d, false, ready => { if (dtImgNote != null) dtImgNote.text = ready ? "" : "AI가 그림을 그리는 중..."; });
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

    // 엄마의 전언: 보유 재료로 만들 추천 요리와 레시피. 고르면 재료와 방식이 바로 선택된다.
    void ShowMomRecipes()
    {
        CloseModal();
        var list = Recipes.Suggest(run.inv.Keys, DaysLeft);
        modal = UIKit.Box(canvasRT, "MomRecipes", 0.05f, 0.06f, 0.95f, 0.94f, UIKit.Card).gameObject;
        var m = modal.transform;
        UIKit.Label(m, "엄마의 전언", 0.04f, 0.87f, 0.6f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Btn(m, "닫기 [Esc]", 0.78f, 0.88f, 0.96f, 0.97f, CloseModal, UIKit.Muted, 32);
        UIKit.Label(m, list.Count == 0 ? "엄마: 냉장고가 텅 비었네... 비상 라면이라도 챙겨 먹어라."
            : "엄마: 냉장고 보니까 이거 해 먹으면 되겠더라. 엄마 말 들어서 손해 볼 거 없다!",
            0.04f, 0.79f, 0.96f, 0.87f, 36, TextAnchor.MiddleLeft);

        float w = 0.92f / 3f;
        for (int k = 0; k < list.Count; k++)
        {
            var r = list[k];
            int idx = k;
            float x0 = 0.04f + k * w, x1 = x0 + w - 0.02f;
            UIKit.Box(m, "Recipe", x0, 0.04f, x1, 0.77f, Color.white);
            UIKit.Label(m, r.name, x0 + 0.01f, 0.67f, x1 - 0.01f, 0.76f, 52, TextAnchor.MiddleCenter, UIKit.Ink);
            UIKit.Label(m, string.Join(" + ", r.ings.Select(i => i.name)) + "  ·  " + Data.GetMethod(r.method).name + "\n예상 만족도 " + r.expected,
                x0 + 0.01f, 0.58f, x1 - 0.01f, 0.67f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
            float y = 0.57f;
            if (r.why != null)
            {
                UIKit.Label(m, r.why, x0 + 0.015f, y - 0.07f, x1 - 0.015f, y, 28, TextAnchor.MiddleLeft, UIKit.Bad);
                y -= 0.08f;
            }
            UIKit.Label(m, string.Join("\n", r.steps.Select((s, n) => (n + 1) + ". " + s)), x0 + 0.015f, 0.17f, x1 - 0.015f, y, 28, TextAnchor.UpperLeft);
            UIKit.Btn(m, "이걸로 할래 [" + (k + 1) + "]", x0 + 0.02f, 0.06f, x1 - 0.02f, 0.15f, () => modalPick(idx), UIKit.Good, 34);
        }
        modalPick = i =>
        {
            if (i >= list.Count) return;
            sel.Clear();
            sel.AddRange(list[i].ings);
            selMethod = list[i].method;
            ShowDay(); // ShowDay의 Clear가 모달도 닫는다
        };
    }

    void CloseModal()
    {
        CloseDexDetail();
        if (modal != null) Destroy(modal);
        modal = null;
        modalPick = null;
        dexSearch = null; dexContent = null; dexInfo = null;
    }
}
