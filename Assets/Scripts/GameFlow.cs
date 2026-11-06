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
    static readonly HashSet<string> dexNew = new HashSet<string>(); // 새로 등록됐지만 도감에서 아직 열어 보지 않은 요리

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        LoadDex();
        Titles.Load();
        var go = new GameObject("COOKER");
        inst = go.AddComponent<GameFlow>();
    }

    // 키보드 조작: 화면마다 Enter/Space로 누를 기본 동작을 정해 둔다.
    enum Scr { None, Title, Lobby, Shop, Day, Cook, Meal, Result, Other }
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
    Color lidBase = Color.white; // 뚜껑 칸의 평소 색
    GameObject lidOnPot;         // 뚜껑을 누르는 동안 냄비 위에 덮이는 그림
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
        // 어디서든 로비로 돌아가는 버튼. 화면이 바뀌어도 남아 있고, 판 도중 화면에서만 보인다 (Update)
        lobbyBtn = UIKit.IconBtn(canvasRT, "icon_home", "로비", 0.905f, 0.915f, 0.99f, 0.99f, GoLobby, UIKit.Accent, 30).gameObject;
        lobbyBtn.SetActive(false);

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var ego = new GameObject("EventSystem", typeof(EventSystem));
            var module = ego.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }

    // 화면 안쪽 여백: 내용 전체를 조금 줄여서 가장자리에 여백을 만든다 (그림 배경은 그대로 꽉 찬다)
    void Pad(float s)
    {
        root.localScale = new Vector3(s, s, 1f);
        flyLayer.localScale = new Vector3(s, s, 1f);
    }

    void Clear()
    {
        CloseModal();
        SetBg(null);
        root.localScale = Vector3.one;
        flyLayer.localScale = Vector3.one;
        cook = null;
        scr = Scr.Other;
        confirm = null;
        dayList.Clear();
        lidRect = null;
        lidImage = null;
        lidOnPot = null;
        potBubbles.Clear();
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
        }
        else
        {
            UIKit.Label(root, "COOKER", 0.1f, 0.58f, 0.9f, 0.88f, 150, TextAnchor.MiddleCenter, UIKit.Accent);
            UIKit.Label(root, "일주일치 장바구니로 최대한 오래 버텨라", 0.1f, 0.46f, 0.9f, 0.58f, 54);
            UIKit.Btn(root, "시작  [Enter]", 0.35f, 0.26f, 0.65f, 0.40f, StartRun, null, 44, true, true);
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
        ShowLobby(); // 타이틀 다음 로비
    }

    // 로비 (Lobby.cs). [게임 시작]이면 장보기. 주가 끝난 뒤 처음 들어올 때 새 칭호·해금을 알린다.
    readonly List<TitleDef> pendingGain = new List<TitleDef>();
    void ShowLobby()
    {
        Lobby.Show(root, run, Clear, ShowShop, ShowDex, ShowTitles, ShowFridge);
        scr = Scr.Lobby;
        confirm = ShowShop;
        if (pendingGain.Count > 0)
        {
            var g = new List<TitleDef>(pendingGain);
            pendingGain.Clear();
            ShowTitleGain(g);
        }
    }

    // ---------------------------------------------------------------- 장보기
    // 장보기 왼쪽 패널 색 (시안 HTML: 노란 카드 #fef08a, 갈색 #451a03, 주황 #E9684F)
    static Color ShopYellow { get { return UIKit.Card; } }
    static Color ShopBrown { get { return UIKit.Ink; } }
    static Color ShopOrange { get { return UIKit.Accent; } }

    void ShowShop()
    {
        Clear();
        Pad(0.96f);
        SetBg("bg_shop");
        PurgeExpired();
        int spent = cart.Sum(kv => Price(kv.Key) * kv.Value);
        int units = cart.Values.Sum();
        int left = run.budget - spent;

        // 왼쪽: 장보기 표 (노란 메뉴 카드 + 연한 줄 카드. 색은 장보기 시안 HTML을 따른다)
        var shopCard = UIKit.Box(root, "Panel", 0.02f, 0.03f, 0.545f, 0.97f, ShopYellow);
        shopCard.GetComponent<Outline>().effectColor = ShopBrown;
        shopCard.GetComponent<Outline>().effectDistance = new Vector2(4f, 4f);
        UIKit.Label(root, run.week + "주차 장보기", 0.04f, 0.88f, 0.30f, 0.96f, 52, TextAnchor.MiddleLeft, ShopOrange);
        UIKit.Label(root, "남은 예산 <color=#E9684F>" + left.ToString("N0") + "원</color>", 0.30f, 0.88f, 0.525f, 0.96f, 44, TextAnchor.MiddleRight, ShopBrown);
        UIKit.Label(root, run.week == 1 ? "6끼분 담기 · 첫날 달걀은 무료" : "7끼분 채우기 · 이월 재료 있음, 가격 인상",
            0.04f, 0.795f, 0.32f, 0.875f, 28, TextAnchor.MiddleLeft, new Color(UIKit.Muted.r, UIKit.Muted.g, UIKit.Muted.b, 1f));
        // 단축키 안내: 너무 작아 안 보이던 것을 같은 줄 오른쪽에 읽히는 크기로
        UIKit.Label(root, "숫자 담기 · Shift+숫자 빼기", 0.30f, 0.795f, 0.525f, 0.875f, 24, TextAnchor.MiddleRight, new Color(UIKit.Muted.r, UIKit.Muted.g, UIKit.Muted.b, 0.9f));

        Color head = new Color(UIKit.Muted.r, UIKit.Muted.g, UIKit.Muted.b, 0.95f);
        UIKit.Label(root, "재료", 0.10f, 0.725f, 0.235f, 0.785f, 30, TextAnchor.MiddleLeft, head);
        UIKit.Label(root, "가격", 0.235f, 0.725f, 0.325f, 0.785f, 30, TextAnchor.MiddleCenter, head);
        UIKit.Label(root, "유통기한", 0.325f, 0.725f, 0.395f, 0.785f, 30, TextAnchor.MiddleCenter, head);
        UIKit.Label(root, "수량", 0.405f, 0.725f, 0.525f, 0.785f, 30, TextAnchor.MiddleCenter, head);
        UIKit.Box(root, "HeadLine", 0.035f, 0.722f, 0.53f, 0.726f, new Color(0.92f, 0.78f, 0.73f, 0.8f), false); // 표 머리글 아래 줄

        var shopList = Data.Active; // 해금 전에는 새우가 상점에 나오지 않는다
        for (int r = 0; r < shopList.Length; r++)
        {
            var ing = shopList[r];
            float top = RowTop(r);
            float bot = top - (RowStep - 0.022f);
            var rowBox = UIKit.Box(root, "Row", 0.035f, bot, 0.53f, top, new Color(1f, 0.98f, 0.92f), false);
            var rowLine = rowBox.gameObject.AddComponent<Outline>(); // 연한 테두리
            rowLine.effectColor = new Color(0.96f, 0.88f, 0.84f);
            rowLine.effectDistance = new Vector2(2f, 2f);
            int qty;
            cart.TryGetValue(ing, out qty);
            int own;
            run.inv.TryGetValue(ing, out own);
            SetIcon(UIKit.Box(root, "Icon", 0.045f, bot + 0.008f, 0.095f, top - 0.008f).gameObject.AddComponent<Image>(), ing);
            UIKit.Label(root, "<color=#A67C6DCC><size=24>[" + (r + 1) + "]</size></color> " + ing.name + (own > 0 ? " <color=#3F8F63><size=24>보유 " + own + "개</size></color>" : ""),
                0.10f, bot, 0.235f, top, 34, TextAnchor.MiddleLeft, ShopBrown);
            UIKit.Label(root, Price(ing).ToString("N0") + "원", 0.235f, bot, 0.325f, top, 34, TextAnchor.MiddleCenter, ShopBrown);
            UIKit.Label(root, ing.shelf + "일", 0.325f, bot, 0.395f, top, 34, TextAnchor.MiddleCenter, new Color(UIKit.Muted.r, UIKit.Muted.g, UIKit.Muted.b, 1f));
            ShopBtn("−", 0.405f, bot + 0.012f, 0.445f, top - 0.012f, () => ChangeCart(ing, -1), new Color(0.267f, 0.2f, 0.161f), new Color(0.149f, 0.11f, 0.086f),
                new Color(0.996f, 0.953f, 0.78f), 44, qty > 0, 5f);
            UIKit.Label(root, qty.ToString(), 0.445f, bot, 0.485f, top, 38, TextAnchor.MiddleCenter, qty > 0 ? ShopOrange : ShopBrown);
            ShopBtn("+", 0.485f, bot + 0.012f, 0.525f, top - 0.012f, () => ChangeCart(ing, 1), UIKit.Accent, new Color(0.62f, 0.25f, 0.2f),
                Color.white, 44, Price(ing) <= left, 5f);
        }

        UIKit.Box(root, "FootLine", 0.035f, 0.172f, 0.53f, 0.176f, new Color(0.92f, 0.78f, 0.73f, 0.8f), false); // 아래 영역 구분선
        UIKit.Label(root, "담은 재료 <color=#E9684F>" + units + "</color>개", 0.04f, 0.04f, 0.25f, 0.16f, 38, TextAnchor.MiddleLeft, ShopBrown);
        bool ok = units >= Need();
        // 주요 버튼 (로비 "게임 시작"과 같은 그림·글자 모양). 아직 못 누르면 연하게 보인다
        UIKit.PrimaryBtn(root, ok ? "구매 확정  [Enter]" : "최소 " + Need() + "개 필요", 0.27f, 0.05f, 0.525f, 0.15f, ConfirmShop, 40, ok);

        DrawBasket();
        scr = Scr.Shop;
        confirm = ok ? (System.Action)ConfirmShop : null;
    }

    // 장보기 시안의 버튼: 색 바탕 + 아래로 두꺼운 그림자(눌리면 내려앉는다). enabled가 false면 연하게(투명 35%) 보이고 그림자도 없다.
    Button ShopBtn(string text, float x0, float y0, float x1, float y1, System.Action onClick, Color bg, Color lip, Color textColor,
        int size, bool enabled, float lipDepth, Color? border = null)
    {
        var rt = UIKit.Box(root, "Btn_" + text, x0, y0, x1, y1, enabled ? bg : new Color(bg.r, bg.g, bg.b, 0.35f), false);
        if (enabled)
        {
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = lip;
            sh.effectDistance = new Vector2(0f, -lipDepth);
        }
        if (border.HasValue)
        {
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = border.Value;
            o.effectDistance = new Vector2(3f, 3f);
        }
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        var cb = b.colors;
        cb.highlightedColor = new Color(1.0f, 1.0f, 1.0f);
        cb.pressedColor = new Color(0.9f, 0.9f, 0.9f);
        cb.disabledColor = Color.white;
        b.colors = cb;
        b.interactable = enabled;
        b.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<PressDown>().depth = lipDepth * 0.6f;
        var tc = enabled ? textColor : new Color(textColor.r, textColor.g, textColor.b, 0.35f);
        UIKit.Label(rt, text, 0.04f, 0.04f, 0.96f, 0.96f, size, TextAnchor.MiddleCenter, tc);
        return b;
    }

    // ---- 장바구니 연출
    // 재료 줄의 위쪽 위치(0~1). 날아가는 재료의 출발점도 이 값으로 정한다.
    static float RowStep { get { return Data.Active.Length > 6 ? 0.078f : 0.09f; } } // 재료가 7개가 되면 줄 간격을 줄인다
    static float RowTop(int r) { return 0.715f - r * RowStep; }
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
    // 주를 취소하고 돌아온 경우(돈을 이미 썼을 수 있다)에도 막히지 않게, 살 수 있는 만큼으로 줄인다.
    int Need()
    {
        int need = Mathf.Max(0, (run.week == 1 ? Balance.MinUnits : Balance.Days) - run.inv.Values.Sum());
        int cheapest = Data.Active.Min(i => Price(i));
        return Mathf.Min(need, run.budget / Mathf.Max(1, cheapest));
    }

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
            int r = System.Array.IndexOf(Data.Active, ing);
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

    // 장보기를 떠날 때: 아직 날아가던 재료 그림을 지운다 (안 지우면 다음 화면 왼쪽에 둥둥 떠 남는다)
    void ClearFlying()
    {
        flyingIn.Clear();
        foreach (Transform c in flyLayer) Destroy(c.gameObject);
    }

    void ConfirmShop()
    {
        ClearFlying();
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
        Pad(0.96f);
        UIKit.Label(root, run.week + "주차 " + run.day + "일차 / " + Balance.Days + "일", 0.03f, 0.88f, 0.55f, 0.98f, 56, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Label(root, "비상 라면 " + (Balance.Lifelines - run.strikes) + "/" + Balance.Lifelines + "   먹은 끼니 " + run.dishes.Count + "개", 0.45f, 0.88f, 0.89f, 0.98f, 40, TextAnchor.MiddleRight);

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
        UIKit.Box(root, "Right", 0.54f, 0.47f, 0.96f, 0.87f, UIKit.Card);
        UIKit.Label(root, "조리 방식", 0.56f, 0.80f, 0.94f, 0.86f, 34, TextAnchor.MiddleLeft, UIKit.Muted);
        var openMethods = Data.ActiveMethods; // 튀기기는 "자취 생존자"를 얻기 전에는 선택지에 나오지 않는다
        for (int k = 0; k < openMethods.Length; k++)
        {
            var md = openMethods[k];
            float step = openMethods.Length > 3 ? 0.08f : 0.105f;
            float top = 0.795f - k * step;
            float bot = top - (step - 0.02f);
            bool on = selMethod == md.id;
            var mb = UIKit.IconBtn(root, md.id == Method.Grill ? "icon_pan" : md.id == Method.Stirfry ? "icon_wok" : md.id == Method.Boil ? "icon_pot" : "icon_fry", "[" + "QWER"[k] + "] " + md.name, 0.57f, bot, 0.93f, top, () => { selMethod = md.id; ShowDay(); },
                on ? UIKit.Accent : UIKit.Muted, openMethods.Length > 3 ? 38 : 44, !guideMeal || on); // 고른 방식만 진한 주황, 나머지는 차분한 갈색 (전부 빨갛던 것 정리)
            // 방식마다 핵심 조작을 한마디로 (고르기 전에 알 수 있게)
            string tip = md.id == Method.Grill ? "뒤집을 때를 잡아라" : md.id == Method.Stirfry ? "쉬지 않고 저어라"
                : md.id == Method.Boil ? "넘치면 뚜껑" : "불을 약하게 두지 마";
            var ml = mb.GetComponentInChildren<Text>(); // 이름은 아이콘 옆, 설명은 오른쪽 끝
            if (ml != null) { ml.rectTransform.anchorMin = new Vector2(0.27f, 0.08f); ml.rectTransform.anchorMax = new Vector2(0.66f, 0.92f); ml.alignment = TextAnchor.MiddleLeft; }
            UIKit.Label(mb.transform, tip, 0.62f, 0.12f, 0.97f, 0.88f, 24, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.8f));
        }

        // 엄마 힌트
        // 알림처럼: 카드 전체가 눌리는 메시지 알림 (버튼 모양 없이 아이콘 + 제목 + 내용)
        var hintCard = UIKit.Box(root, "Hint", 0.54f, 0.255f, 0.96f, 0.43f, UIKit.Card);
        string hint = sel.Count == 0
            ? "밥은 잘 챙겨 먹고 있니? 아껴 쓰되, 굶지는 마라."
            : string.Join("\n", sel.Select(i => Data.MomHint(i)));
        var hintBg = hintCard.GetComponent<Image>();
        if (!guideMeal && hintBg != null)
        {
            hintBg.raycastTarget = true;
            var hb = hintCard.gameObject.AddComponent<Button>();
            hb.targetGraphic = hintBg;
            hb.transition = Selectable.Transition.None;
            hb.onClick.AddListener(ShowMomRecipes);
            hintCard.gameObject.AddComponent<Bouncy>();
        }
        var mailImg = UIKit.Box(root, "MailIcon", 0.56f, 0.355f, 0.585f, 0.415f).gameObject.AddComponent<Image>();
        mailImg.sprite = Art.Load("icon_mail");
        mailImg.color = UIKit.Accent;
        mailImg.raycastTarget = false;
        UIKit.Label(root, "엄마에게서 메시지", 0.59f, 0.355f, 0.80f, 0.42f, 26, TextAnchor.MiddleLeft, UIKit.Accent);
        if (!guideMeal) UIKit.Label(root, "눌러서 추천 요리 보기 [M]", 0.74f, 0.355f, 0.945f, 0.42f, 24, TextAnchor.MiddleRight, UIKit.Muted);
        UIKit.Label(root, hint, 0.56f, 0.265f, 0.94f, 0.35f, 32, TextAnchor.MiddleLeft);

        // 하단
        // 오늘의 요리 카드: 고른 재료와 방식을 색으로 구분해서 (아직 안 골랐으면 흐리게 안내)
        var sumCard = UIKit.Box(root, "Summary", 0.03f, 0.045f, 0.55f, 0.20f, UIKit.Card);
        UIKit.Label(sumCard, "오늘의 요리", 0.04f, 0.68f, 0.5f, 0.97f, 26, TextAnchor.MiddleLeft, UIKit.Muted);
        string ingText = sel.Count == 0 ? "<color=#A67C6D>재료를 골라 주세요</color>" : "<color=#E9684F>" + string.Join(" + ", sel.Select(i => i.name)) + "</color>";
        string methodText = selMethod.HasValue ? "<color=#5BA77A>" + Data.GetMethod(selMethod.Value).name + "</color>" : "<color=#A67C6D>방식을 골라 주세요</color>";
        UIKit.Label(sumCard, ingText + "   ·   " + methodText, 0.04f, 0.06f, 0.96f, 0.70f, 40, TextAnchor.MiddleLeft, UIKit.Ink);
        UIKit.DexBtn(root, 0.58f, 0.05f, 0.12f, ShowDex);
        bool canCook = sel.Count > 0 && selMethod.HasValue;
        UIKit.PrimaryBtn(root, "조리 시작 [Enter]", 0.73f, 0.045f, 0.96f, 0.20f, StartCook, 40, canCook);
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
            UIKit.Label(rt, "x" + qty + (tag == "" ? "" : " " + tag), 0.38f, 0.08f, 0.95f, 0.48f, 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f)); // 오른쪽 끝이 잘리지 않게
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
        // 튀기기는 전용 배경 그림이 아직 없어 웍 그림을 임시로 쓴다 (bg_cook_fry.png를 넣으면 그것을 쓴다)
        // 새 그림: 조리대(bg_cook: 도마·가스레인지·접시) 하나에 조리도구(cook_pan/wok/pot)를 따로 올린다. 없으면 위의 방식별 배경을 쓴다.
        string toolFile = md.id == Method.Grill ? "cook_pan" : md.id == Method.Boil ? "cook_pot" : "cook_wok";
        bool newArt = Art.Load("bg_cook") != null && Art.Load(toolFile) != null;
        string bgFile = newArt ? "bg_cook" : md.id == Method.Grill ? "bg_cook_grill" : md.id == Method.Stirfry ? "bg_cook_wok"
            : md.id == Method.Fry ? (Art.Load("bg_cook_fry") != null ? "bg_cook_fry" : "bg_cook_wok") : "bg_cook_pot";
        cookArt = Art.Load(bgFile) != null;
        if (cookArt) SetBg(bgFile);

        if (cookArt)
        {
            // 위 설명은 한 줄로 짧게 (긴 규칙은 첫날 가이드/도움말에서). 지금 해야 할 일은 가운데 안내(promptLabel)가 크게 알려 준다.
            UIKit.Box(root, "TopNote", 0.185f, 0.895f, 0.895f, 0.98f, new Color(1f, 0.97f, 0.9f, 0.92f)); // 배경 그림 위에서도 글자가 읽히게 (오른쪽 끝은 로비 버튼 자리)
            UIKit.Label(root, md.name + " 중", 0.20f, 0.90f, 0.30f, 0.975f, 44, TextAnchor.MiddleLeft, UIKit.Accent);
            UIKit.Label(root, (guideMeal ? "[첫날 가이드] " : "") + ShortGuide(md.id), 0.30f, 0.90f, 0.885f, 0.975f, 32, TextAnchor.MiddleLeft, new Color(0.3f, 0.2f, 0.1f));

            // 가운데 안내: 지금 해야 할 단 하나의 행동. 바탕색으로 위급한 정도를 구분한다.
            promptBg = UIKit.Box(root, "Prompt", 0.27f, 0.775f, 0.73f, 0.875f, UIKit.Card).GetComponent<Image>();
            promptLabel = UIKit.Label(root, "", 0.28f, 0.78f, 0.72f, 0.87f, 46, TextAnchor.MiddleCenter, UIKit.Ink);
            promptLabel.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1.5f, -1.5f);

            if (newArt)
            {
                // 조리도구: 가스레인지 불 위(1920x1080 기준 960,500)에 그릇 중심을 맞춘다. 값 = (가로 px, 그릇 중심 x비율, 그릇 중심 y비율, 그림 가로:세로)
                float tw, fx, fy, asp;
                // 도구를 조금 작게 해서 가스레인지 불꽃이 둘레에 보이게 한다
                if (md.id == Method.Grill) { tw = 500f; fx = 0.62f; fy = 0.43f; asp = 648f / 545f; }
                else if (md.id == Method.Boil) { tw = 480f; fx = 0.50f; fy = 0.58f; asp = 651f / 467f; } // 냄비는 바닥 중심을 불 자리에 맞춘다 (테두리는 그 위로 올라간다)
                else { tw = 540f; fx = 0.60f; fy = 0.42f; asp = 781f / 631f; }
                float th = tw / asp, left = 960f - fx * tw, top = 500f - fy * th;
                var toolRT = UIKit.Box(root, "Tool", left / RefW, 1f - (top + th) / RefH, (left + tw) / RefW, 1f - top / RefH);
                var toolImg = toolRT.gameObject.AddComponent<Image>();
                toolImg.sprite = Art.Load(toolFile);
                toolImg.raycastTarget = false;
                float zt = md.id == Method.Boil ? 325f : 340f, zb = md.id == Method.Boil ? 585f : 660f; // 재료가 들어가는 칸 위·아래 (냄비는 테두리가 위로 올라와 있다)
                panRect = UIKit.Box(root, "Pan", 770f / RefW, 1f - zb / RefH, 1150f / RefW, 1f - zt / RefH); // 재료가 들어가는 칸 (그릇 안쪽)
                trayRect = UIKit.Box(root, "Tray", 0.05f, 0.345f, 0.32f, 0.655f); // 도마
                plateRect = UIKit.Box(root, "Plate", 0.70f, 0.34f, 0.945f, 0.655f); // 접시
                // 접시 빛: 알맞게 익은 재료가 있으면 접시 둘레가 은은하게 깜빡여 "여기에 담아요"를 알려 준다
                plateGlow = UIKit.Box(root, "PlateGlow", 0.692f, 0.325f, 0.953f, 0.668f).gameObject.AddComponent<Image>();
                plateGlow.sprite = RingSprite();
                plateGlow.raycastTarget = false;
                plateGlow.color = new Color(1f, 0.85f, 0.35f, 0f);
            }
            else
            {
                panRect = UIKit.Box(root, "Pan", 0.056f, 0.247f, 0.500f, 0.975f);
                trayRect = UIKit.Box(root, "Tray", 0.576f, 0.602f, 0.971f, 0.977f);
                plateRect = UIKit.Box(root, "Plate", 0.651f, 0.241f, 0.920f, 0.572f);
            }

            var timerBg = UIKit.Box(root, "TimerBg", 0.023f, 0.870f, 0.172f, 0.977f);
            timerBg.gameObject.AddComponent<Image>().sprite = Art.Load("cook_timer");
            timeLabel = UIKit.Label(timerBg, "", 0.30f, 0.08f, 0.96f, 0.92f, 30, TextAnchor.MiddleCenter, UIKit.Ink);

            // (아래 갈색 바는 없앴다: 밸브·글자·버튼이 조리대 위에 바로 놓인다)
            // 불 조절: 가스 밸브 손잡이를 돌린다(왼쪽 약불 · 위 중불 · 오른쪽 강불). 숫자키 1·2·3도 된다
            knobRT = UIKit.ToolImage(root, "Knob", UIKit.Knob(), 0, 0, 0, 0);
            SetSquare(knobRT, new Vector2(0.17f * RefW, 0.095f * RefH), 145f);
            // 다이얼 이름: 눈에 띄게 "불 세기"로 표시하고, 지금 단계를 글자로도 보여 준다
            var fireTitle = UIKit.Label(root, "불 세기", 0.040f, 0.06f, 0.105f, 0.135f, 32, TextAnchor.MiddleCenter, UIKit.Ink);
            fireTitle.raycastTarget = false;
            fireLevelLabel = UIKit.Label(root, "", 0.235f, 0.05f, 0.33f, 0.14f, 44, TextAnchor.MiddleLeft, UIKit.Ink);
            fireLevelLabel.raycastTarget = false;
            var flo = fireLevelLabel.gameObject.AddComponent<Outline>(); // 밝은 단계 색이 밝은 바탕에서도 읽히게 진한 테두리
            flo.effectColor = new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.9f);
            flo.effectDistance = new Vector2(2f, -2f);
            knobRT.gameObject.AddComponent<KnobDrag>().onAngle = deg =>
            {
                if (cook != null) cook.SetFire(deg < -30f ? 0 : deg > 30f ? 2 : 1);
            };

            // 엄마의 조언 말풍선: 오른쪽 위 로비 버튼 바로 아래. 그림 비율(694x216)에 맞춘 칸. 글씨는 왼쪽 "!" 아이콘과 오른쪽 꼬리를 피해 몸통 안에.
            var tipBg = UIKit.Box(root, "TipBg", 0.735f, 0.755f, 0.985f, 0.893f);
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
            // 재료 이름·키 안내는 그림 아래에 넓게 (그림 안쪽 아래에 작게 넣으면 흐려서 안 읽혔다)
            var label = newArt ? UIKit.Label(rt, "", -0.7f, -0.36f, 1.7f, -0.02f, 28, TextAnchor.MiddleCenter, Color.white)
                : UIKit.Label(rt, "", 0f, 0f, 1f, 0.26f, 30, TextAnchor.MiddleCenter, Color.white);
            var lo = label.gameObject.AddComponent<Outline>();
            lo.effectColor = new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.95f);
            lo.effectDistance = new Vector2(2f, -2f); // 밝은 도마·접시 위에서도 읽히게
            itemLabels.Add(label);
            var gaugeBg = UIKit.Box(rt, "GaugeBg", 0.08f, 1.04f, 0.92f, 1.14f, new Color(0f, 0f, 0f, 0.45f), false); // 음식 그림 바로 위 (겹치지 않게)
            gaugeFills.Add(UIKit.Box(gaugeBg, "GaugeFill", 0f, 0f, 0f, 1f, Color.white, false).GetComponent<Image>());
            swArt.Add(Art.HasSet(chosen[i]));
            PlaceItem(i);
        }

        // 방식별 고유 조작 (버너바 오른쪽 칸)
        if (md.id == Method.Grill)
        {
            const string flipText = "[SPACE]  뒤집기";
            if (cookArt)
            {
                var fbRect = MakePill(root, "FlipBtnBg", 0.370f, 0.028f, 0.630f, 0.162f); // 그림 파일 없이 코드로 그린 빨간 버튼
                var fbImg = fbRect.GetComponent<Image>();
                flipBtn = fbRect.gameObject.AddComponent<Button>();
                flipBtn.targetGraphic = fbImg;
                flipBtn.onClick.AddListener(() => cook.Flip());
                fbRect.gameObject.AddComponent<Bouncy>();
                bool spatulaArt = newArt && Art.Load("cook_spatula") != null;
                if (spatulaArt)
                {
                    var ic = UIKit.Box(fbRect, "SpatulaIcon", 0.04f, 0.08f, 0.24f, 0.92f).gameObject.AddComponent<Image>();
                    ic.sprite = Art.Load("cook_spatula");
                    ic.preserveAspect = true;
                    ic.raycastTarget = false;
                }
                flipBtn.transition = Selectable.Transition.None; // 색은 RefreshCookUI가 직접 바꾼다 (대기 회색 / 뒤집을 때 빨강)
                flipLabel = UIKit.Label(fbRect, flipText, spatulaArt ? 0.25f : 0.04f, 0.04f, 0.96f, 0.96f, 44, TextAnchor.MiddleCenter, Color.white);
            }
            else
            {
                flipBtn = UIKit.Btn(root, flipText, 0.64f, 0.34f, 0.97f, 0.53f, () => cook.Flip(), UIKit.Good, 48);
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
            if (newArt && Art.Load("cook_spoon") != null)
            {
                // 뚜껑 칸처럼: 스푼 그림이 버튼이고, 글자는 오른쪽 바 위에 흰 글자로
                // 뒤집기 버튼과 같은 큼직한 주요 버튼으로, 아래 바 한가운데에 둔다 (스푼 그림 + 글자)
                StylePill(area);
                area.anchorMin = new Vector2(0.370f, 0.028f);
                area.anchorMax = new Vector2(0.630f, 0.162f);
                var spIc = UIKit.Box(area, "SpoonIcon", 0.03f, 0.06f, 0.25f, 1.12f).gameObject.AddComponent<Image>(); // 바 위로 살짝 튀어나오게
                spIc.sprite = Art.Load("cook_spoon");
                spIc.preserveAspect = true;
                spIc.raycastTarget = false;
                UIKit.Label(area, "젓기! 연타 [SPACE]", 0.26f, 0.04f, 0.97f, 0.96f, 38, TextAnchor.MiddleCenter, Color.white).raycastTarget = false;
            }
            else UIKit.Label(area, "빠르게 연타!\n(클릭 또는 Space)", 0.04f, 0.06f, 0.96f, 0.94f, cookArt ? 18 : 30);
        }
        else if (md.id == Method.Boil)
        {
            lidRect = cookArt ? UIKit.Box(root, "Lid", 0.335f, 0.034f, 0.566f, 0.156f, UIKit.Card)
                : UIKit.Box(root, "Lid", 0.64f, 0.34f, 0.97f, 0.53f, UIKit.Card);
            lidImage = lidRect.GetComponent<Image>();
            lidBase = UIKit.Card;
            bool newLidLabel = false;
            if (newArt && Art.Load("cook_pot_lid") != null)
            {
                // 뚜껑 그림을 칸에 그대로 쓰고, 누르고 있는 동안 냄비 위에도 덮는다
                // 뒤집기 버튼과 같은 큼직한 주요 버튼 모양으로 아래 바 한가운데에 둔다 (뚜껑 그림 + 글자). 거품이 올라오면 색이 변한다
                StylePill(lidRect);
                lidBase = PillRed;
                lidImage.color = lidBase;
                lidRect.anchorMin = new Vector2(0.370f, 0.028f);
                lidRect.anchorMax = new Vector2(0.630f, 0.162f);
                var lidIc = UIKit.Box(lidRect, "LidIcon", 0.03f, 0.08f, 0.25f, 0.92f).gameObject.AddComponent<Image>();
                lidIc.sprite = Art.Load("cook_pot_lid");
                lidIc.preserveAspect = true;
                lidIc.raycastTarget = false;
                UIKit.Label(lidRect, "뚜껑 꾹! [SPACE]", 0.26f, 0.04f, 0.97f, 0.96f, 38, TextAnchor.MiddleCenter, Color.white).raycastTarget = false;
                newLidLabel = true;
                // 냄비 속 보글보글 거품: 평소엔 작게, 넘칠 듯하면 크고 하얗게 (뚜껑 그림보다 먼저 만들어 뚜껑 밑에 깔린다)
                potBubbles.Clear();
                for (int b = 0; b < 12; b++)
                {
                    var bub = UIKit.Box(root, "Bubble", 0.5f, 0.5f, 0.5f, 0.5f, new Color(1f, 1f, 1f, 0f), false).GetComponent<Image>();
                    bub.sprite = RingSprite();
                    bub.raycastTarget = false;
                    potBubbles.Add(bub);
                }
                var onPot = UIKit.Box(root, "LidOnPot", (960f - 199f) / RefW, 1f - 605f / RefH, (960f + 199f) / RefW, 1f - 313f / RefH); // 냄비 테두리 위에 맞게 (448x328): 손잡이는 밖으로 보이고 윗선이 냄비 윗선과 맞는다
                var onPotImg = onPot.gameObject.AddComponent<Image>();
                onPotImg.sprite = Art.Load("cook_pot_lid");
                onPotImg.raycastTarget = false;
                lidOnPot = onPot.gameObject;
                lidOnPot.SetActive(false);
            }
            if (!newLidLabel) UIKit.Label(lidRect, "뚜껑 (꾹 누르기)\n또는 Space를 누른 채", 0.05f, 0.05f, 0.95f, 0.95f, cookArt ? 18 : 32);
        }

        // 뒤집개·스푼 연출: 뒤집을 때 뒤집개가 팬 위로 쓱 들어와 뒤집고, 저을 때 스푼이 웍 안에서 빙글 돈다 (뚜껑을 덮는 것처럼)
        fxRT = null; fxImg = null; fxFlip = -1f; fxStir = 0f; fxWasFlipped = false;
        if (newArt && (md.id == Method.Grill || md.id == Method.Stirfry))
        {
            var fxSprite = Art.Load(md.id == Method.Grill ? "cook_spatula" : "cook_spoon");
            if (fxSprite != null)
            {
                fxRT = UIKit.Box(root, "ToolFx", 0.4f, 0.4f, 0.5f, 0.5f);
                fxImg = fxRT.gameObject.AddComponent<Image>();
                fxImg.sprite = fxSprite;
                fxImg.preserveAspect = true;
                fxImg.raycastTarget = false;
                fxImg.color = new Color(1f, 1f, 1f, 0f);
                fxRT.SetAsLastSibling(); // 재료 그림 위에 보이게
            }
        }

        // 불꽃: 불 세기에 따라 도구 둘레의 불꽃 길이가 달라진다 (FireFlame.cs, yo00ng 작업). 새 조리대에서는 가스레인지 불 자리(960,500)에 맞춘다
        if (newArt) FireFlame.Attach(root, cook, panRect, cookArt, new Vector2(960f, RefH - 500f), md.id == Method.Boil ? 165f : md.id == Method.Grill ? 182f : 205f);
        else FireFlame.Attach(root, cook, panRect, cookArt);

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
            : it.spot == Spot.Pan ? (promptLabel != null && !(it.d >= cook.Lo && it.d <= cook.Hi(it)) ? "" : "[" + key + "] " + (promptLabel != null ? "접시에 담기" : it.ing.name + " 꺼내기")) // 새 화면에서는 꺼낼 때가 됐을 때만 보인다
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
        if (kb.dKey.wasPressedThisFrame && (scr == Scr.Lobby || scr == Scr.Day || scr == Scr.Result)) { ShowDex(); return; }
        if (kb.tKey.wasPressedThisFrame && scr == Scr.Lobby) { ShowTitles(); return; } // 칭호는 로비에서만 연다
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
        if (scr == Scr.Shop && n >= 1 && n <= Data.Active.Length) ChangeCart(Data.Active[n - 1], shift ? -1 : 1);
        else if (scr == Scr.Day && !guideMeal)
        {
            if (n >= 1 && n <= dayList.Count) ToggleIngredient(dayList[n - 1]);
            else if (kb.qKey.wasPressedThisFrame) { selMethod = Method.Grill; ShowDay(); }
            else if (kb.wKey.wasPressedThisFrame) { selMethod = Method.Stirfry; ShowDay(); }
            else if (kb.eKey.wasPressedThisFrame) { selMethod = Method.Boil; ShowDay(); }
            else if (kb.rKey.wasPressedThisFrame && Data.Unlocked(Method.Fry)) { selMethod = Method.Fry; ShowDay(); }
        }
    }

    // 로비 버튼: 판 도중(장보기·하루 선택·조리·식사)에만 보이고, 팝업이 열려 있으면 숨긴다.
    // 누르면 바로 가지 않고 확인 창을 띄운다. 가기로 하면 이번 주차가 취소된다 (CancelWeekToLobby).
    GameObject lobbyBtn;

    void GoLobby()
    {
        if (scr != Scr.Shop && scr != Scr.Day && scr != Scr.Cook && scr != Scr.Meal) return;
        CloseModal();
        bool cooking = cook != null && !guideMeal;
        modal = UIKit.Box(canvasRT, "ConfirmLobby", 0, 0, 1, 1, new Color(0f, 0f, 0f, 0.55f), false).gameObject; // 뒤의 화면을 못 누르게 막는다
        var card = UIKit.Box(modal.transform, "Card", 0.22f, 0.25f, 0.78f, 0.75f, UIKit.Card);
        card.localScale = Vector3.one * 0.97f;
        UIKit.Label(card, "로비로 갈까요?", 0.05f, 0.74f, 0.95f, 0.96f, 64, TextAnchor.MiddleCenter, UIKit.Bad);
        UIKit.Label(card, run.week + "주차 진행이 <b>취소</b>되고 1일 차부터 다시 시작한다.\n지금까지 쓴 돈과 재료는 되돌려 받지 못한다."
            + (cooking ? "\n<color=#D9534F>지금 조리 중인 재료도 복구되지 않는다.</color>" : ""),
            0.06f, 0.32f, 0.94f, 0.74f, 34, TextAnchor.MiddleCenter, UIKit.Ink);
        UIKit.Btn(card, "계속 하기 [Esc]", 0.06f, 0.07f, 0.48f, 0.27f, CloseModal, UIKit.Muted, 36);
        UIKit.Btn(card, "로비로 가기", 0.52f, 0.07f, 0.94f, 0.27f, CancelWeekToLobby, UIKit.Bad, 36);
    }

    // 주차 취소: 이번 주 진행(일차·먹은 요리·비상 라면·장바구니)을 처음으로 돌린다.
    // 쓴 돈과 가진 재료는 그대로이고, 조리 중이던 재료는 사라진다(첫날 가이드는 공짜라 제외).
    void CancelWeekToLobby()
    {
        CloseModal();
        if (cook != null && !guideMeal) Consume(cook.items.Select(it => it.ing).ToArray());
        run.day = 1;
        run.strikes = 0;
        run.dishes.Clear();
        cart.Clear();
        ClearFlying();
        sel.Clear();
        selMethod = null;
        ShowLobby();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (lobbyBtn != null)
            lobbyBtn.SetActive((scr == Scr.Shop || scr == Scr.Day || scr == Scr.Cook || scr == Scr.Meal) && modal == null && dexDetail == null);
        if (cook == null)
        {
            if (kb != null) HandleMenuKeys(kb);
            return;
        }
        if (modal != null) // 확인 창이 떠 있는 동안 조리는 멈춘다
        {
            if (kb != null && kb.escapeKey.wasPressedThisFrame) CloseModal();
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

            if (lidOnPot != null)
            {
                lidOnPot.SetActive(lid);
                foreach (var l in itemLabels) l.enabled = !lid; // 뚜껑 위로 비치는 "꺼내기" 글자는 가린다
            }
            UpdateToolFx(stirTimer > 0f);
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
        if (flipBtn != null)
        {
            if (promptLabel != null)
            {
                // 새 조리 화면: 뒤집기 버튼은 늘 가운데에 있고, 뒤집을 때만 빨갛게 켜진다 (평소엔 회색으로 기다림)
                flipBtn.gameObject.SetActive(true);
                flipBtn.interactable = cook.flipAvailable;
                var fimg = (Image)flipBtn.targetGraphic;
                fimg.color = cook.flipAvailable ? PillRed : new Color(0.72f, 0.66f, 0.63f);
                if (!cook.flipAvailable) flipBtn.transform.localScale = Vector3.one;
                if (flipLabel != null) flipLabel.text = cook.flipAvailable ? "[SPACE]  뒤집기" : cook.flipped ? "뒤집기 완료!" : cook.flipMissed ? "뒤집기 놓침" : "뒤집기 대기";
            }
            else flipBtn.gameObject.SetActive(cook.flipAvailable);
        }
        timeLabel.text = "남은 시간 " + Mathf.Max(0, Mathf.CeilToInt(Balance.MaxCookTime - cook.t)) + "초";
        if (lidImage != null) lidImage.color = cook.bubble ? new Color(1f, 0.70f, 0.20f) : lidBase;
        if (lidRect != null && promptLabel != null) lidRect.localScale = Vector3.one * (cook.bubble ? 1f + 0.05f * Mathf.Sin(Time.time * 9f) : 1f); // 거품이 넘칠 때 두근
        string warn = cook.bubble ? "거품이 넘친다! 뚜껑을 눌러!"
            : cook.flipAvailable ? "지금 뒤집어야 한다!"
            : (cook.m.id == Method.Fry && cook.hadSoggy) ? "기름이 식어서 눅눅해졌다..."
            : (cook.m.id == Method.Fry && cook.AnyInPan && cook.fire == 0) ? "불이 약하다! 기름이 식는다!"
            : (cook.m.id == Method.Stirfry && cook.AnyInPan && !cook.stirring && cook.StirIdle > 0.5f) ? "안 저으면 눌어붙는다!" // 넣자마자 빨간 경고가 뜨지 않게 잠깐 여유
            : (cook.m.id == Method.Grill && cook.flipMissed && !cook.flipped) ? "뒤집을 때를 놓쳤다... 한쪽만 익었다"
            : (cook.m.id == Method.Boil && cook.hadOverflow) ? "국이 끓어넘쳤다... 맛이 줄었다"
            : null;
        string momLine = momTips.Length == 0 ? "" : "엄마: " + momTips[(int)(cook.t / 4f) % momTips.Length];
        if (promptLabel != null)
        {
            // 가운데 안내 하나로 통합: 급한 것 > 꺼낼 때 > 진행 상황. 말풍선은 엄마의 조언만 한다.
            int inPan = 0, ready = 0, onTray = 0;
            for (int i = 0; i < cook.items.Length; i++)
            {
                var it = cook.items[i];
                if (it.spot == Spot.Tray) onTray++;
                else if (it.spot == Spot.Pan) { inPan++; if (it.d >= cook.Lo && it.d <= cook.Hi(it)) ready++; }
            }
            UpdateBubbles();
            if (plateGlow != null) plateGlow.color = new Color(1f, 0.85f, 0.35f, ready > 0 ? 0.55f + 0.35f * Mathf.Sin(Time.time * 6f) : 0f);
            if (cook.bubble) SetPrompt("거품이 넘쳐요! 뚜껑을 눌러요", 2);
            else if (cook.flipAvailable) SetPrompt("지금 뒤집으세요! [Space]", 1);
            else if (warn != null) SetPrompt(warn, 2);
            else if (ready > 0) SetPrompt("알맞게 익었어요! 접시에 담아요", 1);
            else if (inPan > 0 && cook.m.id == Method.Stirfry) SetPrompt(cook.stirring ? "좋아요, 계속 저어요!" : "저어요! 연타 [Space]", cook.stirring ? 0 : 1);
            else if (inPan > 0) SetPrompt("익는 중이에요...", 0);
            else if (onTray > 0) SetPrompt("재료를 눌러서 넣어요", 0);
            else SetPrompt("모두 담았어요! [Enter]", 0);
            stateLabel.text = momLine;
            stateLabel.color = UIKit.Ink;
            if (fireLevelLabel != null) { fireLevelLabel.text = Balance.FireName[cook.fire]; fireLevelLabel.color = FireFlame.LevelColor[Mathf.Clamp(cook.fire, 0, 2)]; }
            float left = Mathf.Max(0, Balance.MaxCookTime - cook.t);
            timeLabel.color = left <= 5f ? UIKit.Bad : left <= 15f ? UIKit.Accent : UIKit.Ink; // 시간이 줄수록 색으로 알린다
            if (flipBtn != null && cook.flipAvailable)
                flipBtn.transform.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(Time.time * 9f)); // 뒤집을 때만 살짝 두근
        }
        else
        {
            // 급한 일이 없으면 엄마의 조언이 몇 초마다 바뀌며 계속 나온다
            stateLabel.text = warn ?? momLine;
            stateLabel.color = warn != null ? UIKit.Bad : UIKit.Ink;
        }
    }

    string[] momTips = new string[0]; // 이번 조리의 엄마 조언 (재료별 한마디 + 방식별 요령)
    // 코드로 그린 큰 빨간 버튼 (그림 파일이 늘어나면 깨져서): 둥근 모서리 + 진한 외곽선 + 윗면 빛
    static readonly Color PillRed = new Color(0.90f, 0.36f, 0.30f);
    static RectTransform MakePill(Transform parent, string name, float x0, float y0, float x1, float y1)
    {
        var rt = UIKit.Box(parent, name, x0, y0, x1, y1, PillRed);
        StylePill(rt);
        return rt;
    }
    static void StylePill(RectTransform rt)
    {
        var img = rt.GetComponent<Image>();
        img.color = PillRed; // 둥근 그림(UIKit.Box가 넣은 것)은 그대로 쓴다
        var o = rt.GetComponent<Outline>();
        if (o != null) { o.effectColor = new Color(0.42f, 0.13f, 0.10f, 1f); o.effectDistance = new Vector2(3.5f, 3.5f); }
        var hi = UIKit.Box(rt, "Hi", 0.06f, 0.62f, 0.94f, 0.90f, new Color(1f, 1f, 1f, 0.05f), false);
        hi.GetComponent<Image>().raycastTarget = false;
        hi.SetAsFirstSibling(); // 아이콘·글자 아래
    }

    // 부드러운 타원 고리 (접시 빛). 한 번 만들어 계속 쓴다
    static Sprite ringSprite;
    static Sprite RingSprite()
    {
        if (ringSprite != null) return ringSprite;
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f)) / (S / 2f); // 0 가운데 ~ 1 가장자리
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.09f); // 가장자리 근처만 빛나는 고리
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        ringSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return ringSprite;
    }

    readonly List<Image> potBubbles = new List<Image>(); // 끓이기 냄비 속 거품

    // 거품: 냄비 테두리 안(기준 픽셀 중심 960,450)에서 생겼다 커지며 사라진다. 넘칠 때는 많고 크게
    void UpdateBubbles()
    {
        if (potBubbles.Count == 0 || cook == null) return;
        bool any = cook.AnyInPan;
        for (int b = 0; b < potBubbles.Count; b++)
        {
            var img = potBubbles[b];
            if (img == null) continue;
            float spd = 0.6f + (b % 4) * 0.18f;
            float life = Mathf.Repeat(Time.time * spd + b * 0.37f, 1f);          // 0 생김 → 1 터짐
            float ang = b * 2.399f;                                              // 골고루 흩어지게
            float r = 0.4f + 0.6f * Mathf.Repeat(b * 0.618f, 1f);
            float cx = 960f + Mathf.Cos(ang) * 180f * r, cy = 450f + Mathf.Sin(ang) * 85f * r - life * 14f;
            float size = (cook.bubble ? 48f : 24f) * (0.4f + life);
            PlacePx(img.rectTransform, cx, cy, size, size);
            bool show = any && (cook.bubble || b % 2 == 0);                     // 평소엔 절반만, 넘칠 땐 전부
            img.color = new Color(1f, 1f, 1f, show ? (cook.bubble ? 0.95f : 0.55f) * Mathf.Sin(life * Mathf.PI) : 0f);
        }
    }

    Text flipLabel;          // 뒤집기 버튼 글자 (대기 / 뒤집기 / 완료)
    Image plateGlow;         // 꺼낼 때가 된 재료가 있으면 접시가 은은하게 빛난다

    // 뒤집개·스푼 연출 상태
    RectTransform fxRT;      // 팬 위에 나타나는 도구 그림
    Image fxImg;
    float fxFlip = -1f;      // 뒤집는 동작 진행 시간 (-1이면 쉬는 중)
    float fxStir;            // 젓는 중 0~1 (부드럽게 나타났다 사라진다)
    float fxPhase;           // 스푼 도는 각도
    bool fxWasFlipped;
    const float FlipFxTime = 0.6f;

    // 그림 한가운데를 기준 픽셀(왼쪽 위가 0,0)로 놓는다
    static void PlacePx(RectTransform rt, float cx, float cy, float w, float h)
    {
        rt.anchorMin = new Vector2((cx - w / 2f) / RefW, 1f - (cy + h / 2f) / RefH);
        rt.anchorMax = new Vector2((cx + w / 2f) / RefW, 1f - (cy - h / 2f) / RefH);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // 매 프레임: 뒤집개/스푼이 팬 위에서 움직인다
    void UpdateToolFx(bool stirring)
    {
        if (fxRT == null || cook == null) return;
        if (cook.m.id == Method.Grill)
        {
            if (cook.flipped && !fxWasFlipped) fxFlip = 0f; // 뒤집은 순간 시작
            fxWasFlipped = cook.flipped;
            if (fxFlip < 0f) { fxImg.color = new Color(1f, 1f, 1f, 0f); return; }
            fxFlip += Time.deltaTime;
            float p = fxFlip / FlipFxTime;
            if (p >= 1f) { fxFlip = -1f; fxImg.color = new Color(1f, 1f, 1f, 0f); return; }
            // 오른쪽 아래에서 쓱 들어와 달걀 밑을 떠서 휙 뒤집고 빠진다: 이동 + 기울기 + 살짝 커짐
            float e = Mathf.Sin(p * Mathf.PI);                       // 0 → 1 → 0
            float cx = Mathf.Lerp(1190f, 1060f, e);
            float cy = Mathf.Lerp(650f, 455f, e) - 40f * Mathf.Sin(Mathf.Clamp01((p - 0.35f) / 0.4f) * Mathf.PI); // 중간에 번쩍 들어올림
            float rot = p < 0.4f ? Mathf.Lerp(0f, 22f, p / 0.4f) : Mathf.Lerp(22f, -28f, (p - 0.4f) / 0.4f) * (p < 0.8f ? 1f : 1f - (p - 0.8f) / 0.2f);
            float sc = 1f + 0.12f * e;
            PlacePx(fxRT, cx, cy, 330f * sc, 264f * sc);
            fxRT.localRotation = Quaternion.Euler(0, 0, rot);
            fxImg.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Min(p / 0.15f, (1f - p) / 0.2f)));
        }
        else
        {
            // 저을 때: 스푼이 웍 안에서 빙글빙글. 멈추면 서서히 사라진다
            fxStir = Mathf.MoveTowards(fxStir, stirring ? 1f : 0f, Time.deltaTime * (stirring ? 8f : 3f));
            fxPhase += Time.deltaTime * 9f * (0.4f + fxStir);
            float r = 55f;
            float cx = 1075f + Mathf.Cos(fxPhase) * r, cy = 520f + Mathf.Sin(fxPhase) * r * 0.6f;
            PlacePx(fxRT, cx, cy, 300f, 293f);
            fxRT.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(fxPhase) * 8f);
            fxImg.color = new Color(1f, 1f, 1f, fxStir);
        }
    }

    Image promptBg;          // 가운데 안내 바탕
    Text promptLabel;        // 가운데 안내 글자 (지금 해야 할 한 가지)
    Text fireLevelLabel;     // 불 세기 현재 단계 글자

    // 조리 화면 위쪽에 쓰는 한 줄 요령
    static string ShortGuide(Method m)
    {
        return m == Method.Grill ? "노릇해지면 뒤집어요"
            : m == Method.Stirfry ? "쉬지 말고 저어요"
            : m == Method.Fry ? "기름이 식지 않게 불을 지켜요"
            : "거품이 올라오면 뚜껑을 눌러요";
    }

    // 가운데 안내와 그 바탕색을 정한다: 위급(빨강) · 지금 할 일(주황) · 평소(연한 바탕)
    void SetPrompt(string text, int level)
    {
        if (promptLabel == null) return;
        promptLabel.text = text;
        promptLabel.color = level == 0 ? UIKit.Ink : Color.white;
        promptBg.color = level == 2 ? UIKit.Bad : level == 1 ? UIKit.Accent : new Color(1f, 0.965f, 0.925f, 0.92f);
    }

    static string MethodTip(Method m)
    {
        return m == Method.Grill ? "뒤집기 버튼이 뜨면 놓치지 말고 바로 뒤집어라."
            : m == Method.Stirfry ? "손 쉬지 말고 계속 저어라. 멈추면 바로 눌어붙어."
            : m == Method.Fry ? "기름 온도를 유지해라. 약불로 오래 두면 눅눅해져."
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
        if (isNew) dexNew.Add(dish.key);
        SaveDex();
        ShowMeal(dish, isNew);
        DishNamer.Request(this, ings, stages, md.id, name =>
        {
            dish.name = name;
            Dish saved;
            if (dex.TryGetValue(dish.key, out saved) && saved == dish) SaveDex(); // AI가 지은 이름이 도착하면 저장 파일도 갱신
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
        Pad(0.97f);
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
        UIKit.Label(root, "만족도 " + dish.satisfaction, photo ? x0 : 0.2f, 0.45f, photo ? x1 : 0.8f, 0.57f, 76, TextAnchor.MiddleCenter, UIKit.Accent);
        UIKit.Label(root, Stars(dish.satisfaction), photo ? x0 : 0.2f, 0.36f, photo ? x1 : 0.8f, 0.45f, 60, TextAnchor.MiddleCenter, new Color(0.95f, 0.7f, 0.05f));
        UIKit.Label(root, reaction, photo ? x0 : 0.1f, 0.27f, photo ? x1 : 0.9f, 0.36f, 46);
        if (!string.IsNullOrEmpty(dish.note)) UIKit.Label(root, dish.note, photo ? x0 : 0.1f, 0.21f, photo ? x1 : 0.9f, 0.28f, 32, TextAnchor.MiddleCenter, UIKit.Bad);
        bool last = run.day >= Balance.Days;
        UIKit.PrimaryBtn(root, (last ? "결산 보기" : "다음 날") + " [Enter]", 0.30f, 0.07f, 0.70f, 0.21f, NextDay, 48);
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
        ShowLobby(); // 정산 뒤에는 로비로 돌아온다
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
        Pad(0.96f);
        float sat = (float)run.dishes.Average(d => (float)d.satisfaction);
        int combos = run.dishes.Where(d => d.key != "비상 라면").Select(d => d.key).Distinct().Count();
        float div = 100f * combos / Balance.Days;
        int leftover = run.inv.Values.Sum();
        float left = Mathf.Min(100f, 100f * leftover / Mathf.Max(1, run.bought));
        float total = sat * 0.5f + div * 0.3f + left * 0.2f;
        string grade = total >= Balance.GradeS ? "S" : total >= Balance.GradeA ? "A" : total >= Balance.GradeB ? "B" : "C";

        UIKit.Label(root, run.week + "주차 생존!", 0.05f, 0.87f, 0.95f, 0.99f, 80, TextAnchor.MiddleCenter, UIKit.Good);

        // 등급 카드: 큰 등급 글자와 총점
        Color gradeColor = grade == "S" ? new Color(0.95f, 0.7f, 0.05f) : grade == "A" ? UIKit.Good : grade == "B" ? UIKit.Accent : new Color(0.5f, 0.45f, 0.4f);
        var gradeCard = UIKit.Box(root, "GradeCard", 0.06f, 0.53f, 0.29f, 0.85f, UIKit.Card);
        UIKit.Label(gradeCard, "이번 주 등급", 0.05f, 0.84f, 0.95f, 0.97f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(gradeCard, grade, 0.05f, 0.28f, 0.95f, 0.86f, 190, TextAnchor.MiddleCenter, gradeColor);
        UIKit.Label(gradeCard, "총점 " + Mathf.RoundToInt(total) + "점", 0.05f, 0.04f, 0.95f, 0.26f, 44, TextAnchor.MiddleCenter, UIKit.Ink);

        // 점수 카드: 항목마다 막대로 한눈에 (만족도 50% · 다양성 30% · 남은 재료 20%)
        var scoreCard = UIKit.Box(root, "ScoreCard", 0.34f, 0.53f, 0.94f, 0.85f, UIKit.Card);
        ScoreBar(scoreCard, "만족도", "50%", sat, UIKit.Accent, 0.68f, 0.96f);
        ScoreBar(scoreCard, "다양성 " + combos + "종류", "30%", div, UIKit.Good, 0.36f, 0.64f);
        ScoreBar(scoreCard, "남은 재료 " + leftover + "개", "20%", left, new Color(0.25f, 0.55f, 0.85f), 0.04f, 0.32f);

        DishRowsCard("이번 주 먹은 요리", 0.06f, 0.215f, 0.52f, 0.49f);

        // 다음 주 예산 카드
        // (칭호는 결산에서 보여 주지 않는다: 칭호 버튼·대표 칭호는 로비에만 있다. 새로 얻은 칭호는 로비에 들어갈 때 알린다)
        var budgetCard = UIKit.Box(root, "BudgetCard", 0.57f, 0.215f, 0.94f, 0.49f, UIKit.Card);
        UIKit.Label(budgetCard, "다음 주 예산", 0.05f, 0.74f, 0.95f, 0.95f, 30, TextAnchor.MiddleLeft, UIKit.Muted);
        UIKit.Label(budgetCard, NextBudget(sat).ToString("N0") + "원", 0.05f, 0.30f, 0.95f, 0.74f, 72, TextAnchor.MiddleLeft, UIKit.Ink);
        UIKit.Label(budgetCard, "재료값 +" + Mathf.RoundToInt((Balance.PriceGrowth - 1f) * 100f) + "%", 0.05f, 0.06f, 0.95f, 0.30f, 30, TextAnchor.MiddleLeft, UIKit.Bad);

        UIKit.DexBtn(root, 0.06f, 0.05f, 0.15f, ShowDex);
        UIKit.PrimaryBtn(root, "다음 주 [Enter]", 0.57f, 0.05f, 0.94f, 0.19f, NextWeek, 40); // 오른쪽 카드들과 좌우 끝을 맞춘다
        scr = Scr.Result;
        confirm = NextWeek;
        var gained = Titles.Check(dex.Values.ToList()); // 칭호는 한 주가 끝나는 결산 때 판정한다
        pendingGain.AddRange(gained); // 알림은 로비에 들어올 때 보여 준다
    }

    void ShowFail(string reason)
    {
        Clear();
        Pad(0.97f);
        UIKit.Label(root, "굶었다...", 0.05f, 0.80f, 0.95f, 0.98f, 110, TextAnchor.MiddleCenter, UIKit.Bad);
        // 왼쪽 카드: 버틴 기간과 실패한 이유
        int survived = (run.week - 1) * Balance.Days + run.day - 1;
        var left = UIKit.Box(root, "SurvivedCard", 0.08f, 0.27f, 0.45f, 0.77f, UIKit.Card);
        UIKit.Label(left, "버틴 기간", 0.05f, 0.82f, 0.95f, 0.97f, 32, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(left, survived + "일", 0.05f, 0.50f, 0.95f, 0.84f, 120, TextAnchor.MiddleCenter, UIKit.Bad);
        UIKit.Label(left, run.week + "주차 " + run.day + "일차에 실패", 0.05f, 0.38f, 0.95f, 0.52f, 34, TextAnchor.MiddleCenter, UIKit.Ink);
        var why = UIKit.Box(left, "Why", 0.06f, 0.05f, 0.94f, 0.34f, new Color(1f, 0.9f, 0.85f), false);
        UIKit.Label(why, reason, 0.04f, 0.05f, 0.96f, 0.95f, 28, TextAnchor.MiddleCenter, UIKit.Bad);
        DishRowsCard("이번 주 먹은 요리", 0.51f, 0.27f, 0.92f, 0.77f); // 주가 바뀌면 목록이 비워지므로 "이번 주"가 정확하다
        UIKit.PrimaryBtn(root, "다시 하기 [Enter]", 0.30f, 0.05f, 0.70f, 0.19f, ShowTitle, 48);
        confirm = ShowTitle;
        var gainedOnFail = Titles.Check(dex.Values.ToList()); // 굶어서 끝나는 결산에서도 판정한다
        pendingGain.AddRange(gainedOnFail);
    }

    string DishList()
    {
        return string.Join("\n", run.dishes.Select((d, i) => (i + 1) + "일차  " + d.name + "  (" + d.satisfaction + ")"));
    }

    // 먹은 요리 카드: 줄마다 일차 · 요리 이름 · 별 · 만족도 (결산과 굶음 화면이 같이 쓴다)
    void DishRowsCard(string header, float x0, float y0, float x1, float y1)
    {
        var card = UIKit.Box(root, "DishCard", x0, y0, x1, y1, UIKit.Card);
        UIKit.Label(card, header, 0.04f, 0.84f, 0.96f, 0.97f, 30, TextAnchor.MiddleLeft, UIKit.Muted); // 제목이 카드 윗선에 붙지 않게 살짝 내림
        if (run.dishes.Count == 0)
        {
            UIKit.Label(card, "먹은 요리가 없다.", 0.05f, 0.2f, 0.95f, 0.8f, 36, TextAnchor.MiddleCenter, UIKit.Muted);
            return;
        }
        const int maxRows = 7; // 한 주는 7일이라 더 많을 일이 없다
        float rowH = 0.78f / maxRows;
        for (int i = 0; i < Mathf.Min(run.dishes.Count, maxRows); i++)
        {
            var d = run.dishes[i];
            float top = 0.82f - i * rowH, bot = top - rowH * 0.86f;
            var row = UIKit.Box(card, "Row", 0.04f, bot, 0.96f, top, new Color(1f, 1f, 1f, i % 2 == 0 ? 0.65f : 0.3f), false);
            UIKit.Label(row, (i + 1) + "일차", 0.02f, 0f, 0.20f, 1f, 26, TextAnchor.MiddleLeft, UIKit.Muted);
            UIKit.Label(row, d.name, 0.21f, 0f, 0.60f, 1f, 30, TextAnchor.MiddleLeft, UIKit.Ink);
            UIKit.Label(row, Stars(d.satisfaction), 0.58f, 0f, 0.84f, 1f, 26, TextAnchor.MiddleRight, new Color(0.95f, 0.7f, 0.05f));
            UIKit.Label(row, d.satisfaction.ToString(), 0.85f, 0f, 0.98f, 1f, 30, TextAnchor.MiddleRight, UIKit.Accent);
        }
    }

    // 점수 막대: 이름, 막대(0~100), 숫자, 반영 비율
    void ScoreBar(Transform p, string label, string weight, float value, Color fill, float y0, float y1)
    {
        UIKit.Label(p, label, 0.04f, y0, 0.33f, y1, 34, TextAnchor.MiddleLeft, UIKit.Ink);
        float pad = (y1 - y0) * 0.22f;
        var bg = UIKit.Box(p, "BarBg", 0.34f, y0 + pad, 0.78f, y1 - pad, new Color(0.45f, 0.3f, 0.15f, 0.18f), false);
        UIKit.Box(bg, "Fill", 0f, 0f, Mathf.Clamp01(value / 100f), 1f, fill, false);
        UIKit.Label(p, Mathf.RoundToInt(value).ToString(), 0.79f, y0, 0.89f, y1, 38, TextAnchor.MiddleRight, UIKit.Ink);
        UIKit.Label(p, weight, 0.89f, y0, 0.98f, y1, 22, TextAnchor.MiddleRight, UIKit.Muted);
    }

    // 망친 요리. 탄/날것 상태 그대로의 그림을 보여 주고 다음으로 넘어간다.
    void ShowRuined(Ingredient[] used, Doneness[] stages, MethodDef md, string msg, string btn, System.Action next)
    {
        Clear();
        Pad(0.97f);
        string how = stages.Contains(Doneness.Burnt) ? "새까맣게 탄 " : "날것 그대로인 ";
        Photo(root, 0.04f, 0.23f, 0.40f, 0.87f, used, stages, md.id, false); // 망친 요리는 AI 그림을 만들지 않는다
        UIKit.Label(root, how + md.verb + " " + string.Join(" ", used.Select(i => i.name)), 0.42f, 0.62f, 0.97f, 0.80f, 90, TextAnchor.MiddleCenter, UIKit.Bad);
        UIKit.Label(root, msg, 0.42f, 0.34f, 0.97f, 0.60f, 52);
        UIKit.PrimaryBtn(root, btn + " [Enter]", 0.30f, 0.07f, 0.70f, 0.21f, next, 48);
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
        var status = UIKit.Label(frame, "요리 사진 찍는 중...", 0.04f, 0.075f, 0.96f, 0.18f, 40, TextAnchor.MiddleCenter, UIKit.Muted);
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
            status.text = retry() ?? "요리 사진 찍는 중... " + Mathf.RoundToInt(p * 100f) + "%"; // "(0초)" 같은 경과 시간은 빼고 무슨 일인지 말한다
            yield return null;
        }
    }

    // ---------------------------------------------------------------- 공용
    void ShowMessage(string msg, string btn, System.Action onClick)
    {
        Clear();
        Pad(0.97f);
        UIKit.Label(root, msg, 0.1f, 0.45f, 0.9f, 0.75f, 56);
        UIKit.PrimaryBtn(root, btn + " [Enter]", 0.30f, 0.2f, 0.70f, 0.34f, onClick, 48);
        confirm = onClick;
    }

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
        modal = UIKit.Box(canvasRT, "Titles", 0.06f, 0.05f, 0.94f, 0.95f, UIKit.Card, true).gameObject;
        modal.transform.localScale = Vector3.one * 0.97f; // 가장자리 여백
        var m = modal.transform;
        // 아직 해금되지 않은 튀김 관련 칭호는 목록에 보이지 않는다
        var shownTitles = Titles.All.Where(t => t.id != "frypro" || Data.Unlocked(Method.Fry)).ToArray();
        UIKit.Label(m, "칭호 (" + Titles.EarnedCount + " / " + shownTitles.Length + ")", 0.04f, 0.88f, 0.5f, 0.98f, 60, TextAnchor.MiddleLeft, UIKit.Accent);
        var rep = Titles.Representative;
        UIKit.Label(m, rep == null ? "대표 칭호가 없다" : "대표 칭호: [" + rep.name + "]", 0.45f, 0.88f, 0.74f, 0.98f, 30, TextAnchor.MiddleRight, UIKit.Muted);
        UIKit.IconBtn(m, "icon_close", "", 0.90f, 0.88f, 0.97f, 0.98f, CloseModal, UIKit.Muted, 34);
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
        UIKit.IconBtn(m, "icon_close", "닫기", 0.38f, 0.03f, 0.62f, 0.14f, CloseModal, UIKit.Muted, 36);
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

    static string DexPath { get { return System.IO.Path.Combine(Application.persistentDataPath, "dex.json"); } }

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
        modal.transform.localScale = Vector3.one * 0.97f; // 가장자리 여백
        var m = modal.transform;
        UIKit.Label(m, "도감 (" + found + " / " + slots.Count + ")", 0.05f, 0.88f, 0.45f, 0.98f, 60, TextAnchor.MiddleLeft, UIKit.Accent);
        dexInfo = UIKit.Label(m, "", 0.40f, 0.88f, 0.74f, 0.98f, 28, TextAnchor.MiddleRight, UIKit.Muted);
        UIKit.IconBtn(m, "icon_close", "", 0.90f, 0.88f, 0.97f, 0.98f, CloseModal, UIKit.Muted, 34);

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
        var cb = b.colors;
        cb.highlightedColor = new Color(1f, 0.92f, 0.7f);
        cb.pressedColor = new Color(0.9f, 0.8f, 0.55f);
        b.colors = cb;
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
        dtName = UIKit.Label(card, "", 0.48f, 0.80f, 0.96f, 0.95f, 60, TextAnchor.MiddleLeft);

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
        UIKit.IconBtn(card, "icon_close", "닫기", 0.74f, 0.04f, 0.96f, 0.16f, CloseDexDetail, UIKit.Muted, 34);
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
        var list = Recipes.Suggest(run.inv.Keys, DaysLeft, 3, Data.Unlocked);
        modal = UIKit.Box(canvasRT, "MomRecipes", 0.05f, 0.06f, 0.95f, 0.94f, UIKit.Card).gameObject;
        modal.transform.localScale = Vector3.one * 0.97f; // 가장자리 여백
        var m = modal.transform;
        UIKit.Label(m, "엄마의 전언", 0.04f, 0.87f, 0.6f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.IconBtn(m, "icon_close", "", 0.90f, 0.88f, 0.97f, 0.97f, CloseModal, UIKit.Muted, 32);
        UIKit.Label(m, list.Count == 0 ? "엄마: 냉장고가 텅 비었네... 비상 라면이라도 챙겨 먹어라."
            : "엄마: 냉장고 보니까 이거 해 먹으면 되겠더라. 엄마 말 들어서 손해 볼 거 없다!",
            0.04f, 0.79f, 0.96f, 0.87f, 36, TextAnchor.MiddleLeft);

        float w = 0.92f / 3f;
        for (int k = 0; k < list.Count; k++)
        {
            var r = list[k];
            int idx = k;
            float x0 = 0.04f + k * w, x1 = x0 + w - 0.04f;
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
            UIKit.PrimaryBtn(m, "이걸로 할래 [" + (k + 1) + "]", x0 + 0.02f, 0.06f, x1 - 0.02f, 0.15f, () => modalPick(idx), 34);
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
