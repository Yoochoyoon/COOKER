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
    public int[] questLast = new int[Quests.All.Length]; // 지난 주 퀘스트별 진행 (GameFlow.Quest.cs)
    public int questPending;                             // 받을 수 있는 퀘스트 (비트마다 퀘스트 하나)
}

public partial class GameFlow : MonoBehaviour
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
        // 씬에 COOKER 오브젝트(GameFlow)를 직접 올려 두었으면 그것을 쓴다. 없으면 코드로 만든다 (중복 방지)
        if (FindFirstObjectByType<GameFlow>() != null) return;
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
        Settings.Load(); // 음량과 전체화면을 먼저 맞춘다 (Sound.Boot의 타이틀 음악이 이 음량을 쓴다)
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
        lobbyBtn = UIKit.ImageBtn(canvasRT, "icon_exit", 0.9125f, 0.8472f, 0.9438f, 0.9000f, GoLobby).gameObject; // 나가기 그림 버튼 (타이틀의 나가기와 같은 그림)
        lobbyBtn.SetActive(false);
        // 타이틀 버튼: 로비에서만 보인다 (로비 버튼이 있던 자리). 로비는 주가 시작하는 자리라 나가도 잃는 것이 없다.
        // 위쪽 줄 오른쪽 끝 (x 1793~1863, y 30~100 픽셀): 시안 화면 안쪽 여백(22)에 맞춘다. 도움말·설정과 같은 줄
        titleBtn = UIKit.ImageBtn(canvasRT, "icon_exit", 0.9125f, 0.8472f, 0.9438f, 0.9000f, GoTitle).gameObject;
        titleBtn.SetActive(false);

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var ego = new GameObject("EventSystem", typeof(EventSystem));
            var module = ego.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }

    // 화면 안쪽 여백: 내용 전체를 조금 줄여서 가장자리에 여백을 만든다 (그림 배경은 그대로 꽉 찬다)
    // 화면 가장자리 100픽셀(위·아래·왼쪽·오른쪽)은 비워 둔다. 기준 화면 1080에서 880만 쓰므로 안쪽 크기는 880/1080.
    // 1을 주면 줄이지 않는다 (하루 선택·로비처럼 좌표를 픽셀로 직접 정한 화면).
    const float MarginScale = 880f / 1080f;
    void Pad(float s)
    {
        float k = s >= 1f ? 1f : MarginScale;
        root.localScale = new Vector3(k, k, 1f);
        flyLayer.localScale = new Vector3(k, k, 1f);
    }

    void Clear()
    {
        CloseModal();
        CornerDock.Recheck(); // 화면이 바뀌면 도움말·설정 아이콘 자리를 바로 다시 잡는다
        SetBg(null);
        CornerDock.ForceTopLeft = false;
        Pad(0f); // 기본은 가장자리 100픽셀 여백. 좌표를 직접 정한 화면은 Pad(1f)로 다시 부른다
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
        Pad(1f);
        CornerDock.ForceTopLeft = true; // 타이틀 배경 위에서는 창틀에 걸치지 않게 왼쪽 위 고정
        SetBg("bg_title");
        // 새 타이틀 시안(bg_title)에는 로고와 캐릭터가 그려져 있고, 버튼 자리에는 같은 모양의 진짜 버튼을 얹어 가린다 (1920x1080 픽셀 좌표).
        bool hasSave = RunSave.Has();
        TitleBtn("title_btn_cream", "새 게임", 1321f, 577f, 542f, 141f, 0.832f, 0.85f, 56, HudStyle.Text, NewGame, true);
        TitleBtn("title_btn_cream", "이어하기", 1321f, 721f, 542f, 131f, 0.832f, 0.85f, 52, HudStyle.Text, Continue, hasSave);
        TitleBtn("title_btn_cream", "나가기", 1321f, 859f, 542f, 131f, 0.832f, 0.85f, 52, HudStyle.Text, Quit, true);
        scr = Scr.Title;
        confirm = NewGame;
    }

    // 타이틀 버튼: 그림 속 알약 모양(가로 fw, 세로 fh 비율)이 중심 (cx,cy)에 pillW x pillH로 보이게 놓는다. 못 누르면 어두운 회색.
    void TitleBtn(string file, string text, float cx, float cy, float pillW, float pillH, float fw, float fh, int size, Color textColor, System.Action onClick, bool enabled)
    {
        float w = pillW / fw, h = pillH / fh;
        var rt = UIKit.Box(root, "TitleBtn_" + text, DX(cx - w / 2f), DY(cy + h / 2f), DX(cx + w / 2f), DY(cy - h / 2f));
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Art.Load(file);
        img.color = enabled ? Color.white : new Color(0.45f, 0.44f, 0.43f);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.highlightedColor = new Color(1f, 0.93f, 0.50f); // 마우스를 올리면 베이지가 노랗게
        cb.pressedColor = new Color(0.90f, 0.78f, 0.38f);
        cb.disabledColor = Color.white;
        b.colors = cb;
        b.interactable = enabled;
        b.onClick.AddListener(() => onClick());
        if (enabled) { rt.gameObject.AddComponent<Bouncy>(); rt.gameObject.AddComponent<PressDown>().depth = 4f; }
        var lbl = UIKit.Label(rt, text, 0.08f, 0.14f, 0.92f, 0.86f, size, TextAnchor.MiddleCenter, enabled ? textColor : HudStyle.DisabledText);
        lbl.raycastTarget = false;
    }

    void Continue()
    {
        var saved = RunSave.Load();
        if (saved == null) { RunSave.Delete(); StartRun(); return; }
        run = saved;
        flyingIn.Clear();
        cart.Clear();
        sel.Clear();
        selMethod = null;
        if (saved.day > 1) ShowDay(); // 하루 선택 중에 저장된 판은 그날의 하루 선택으로, 주 시작 저장은 로비로
        else ShowLobby();
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

    // 타이틀의 [새 게임]: 도감·칭호·이어하기 저장·순서 힌트 횟수를 모두 지우고 처음부터 시작한다.
    // 지울 기록이 있으면 먼저 확인 창을 띄운다 (실수로 눌러도 기록이 날아가지 않게).
    void NewGame()
    {
        if (modal != null) return;
        if (dex.Count == 0 && Titles.EarnedCount == 0 && !RunSave.Has()) { ResetProgress(); StartRun(); return; }
        modal = UIKit.Box(canvasRT, "ConfirmNewGame", 0, 0, 1, 1, new Color(0f, 0f, 0f, 0.55f), false).gameObject;
        var card = UIKit.Box(modal.transform, "Card", 0.22f, 0.25f, 0.78f, 0.75f, UIKit.Card);
        UIKit.Label(card, "새로 시작할까요?", 0.05f, 0.74f, 0.95f, 0.96f, 64, TextAnchor.MiddleCenter, UIKit.Bad);
        UIKit.Label(card, "도감 " + dex.Count + "개, 칭호 " + Titles.EarnedCount + "개, 이어하기 저장이 <b>모두 지워진다</b>.\n되돌릴 수 없다.",
            0.06f, 0.32f, 0.94f, 0.74f, 34, TextAnchor.MiddleCenter, UIKit.Ink);
        UIKit.Btn(card, "취소 [Esc]", 0.06f, 0.07f, 0.48f, 0.27f, CloseModal, UIKit.Muted, 36);
        UIKit.Btn(card, "새로 시작", 0.52f, 0.07f, 0.94f, 0.27f, () => { CloseModal(); ResetProgress(); StartRun(); }, UIKit.Bad, 36);
    }

    // 저장된 플레이 기록을 전부 지운다. 설정(음량·전체화면)은 지우지 않는다.
    void ResetProgress()
    {
        dex.Clear();
        dexNew.Clear();
        SaveDex(); // 빈 도감을 파일에 덮어쓴다
        Titles.ResetAll();
        RunSave.Delete();
        PlayerPrefs.DeleteKey("cooker.orderHintRuns"); // 순서 힌트도 처음부터
        PlayerPrefs.Save();
    }

    void StartRun()
    {
        run = new Run();
        flyingIn.Clear();
        cart.Clear();
        sel.Clear();
        selMethod = null;
        RunSave.Save(run); // 새 판을 시작하자마자 저장해 둔다 (첫 하루가 끝나기 전에 꺼도 이어하기가 켜져 있게)
        ShowLobby(); // 타이틀 다음 로비
    }

    // 로비 (Lobby.cs). [게임 시작]이면 장보기. 주가 끝난 뒤 처음 들어올 때 새 칭호·해금을 알린다.
    readonly List<TitleDef> pendingGain = new List<TitleDef>();
    void ShowLobby()
    {
        Lobby.Show(root, run, Clear, ShowShop, ShowDex, ShowTitles, ShowFridge);
        Pad(1f);
        BuildQuestBtn(); // 퀘스트 버튼 (GameFlow.Quest.cs)
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
    static Color ShopYellow { get { return HudStyle.Sand; } }
    static Color ShopBrown { get { return HudStyle.Text; } }
    static Color ShopOrange { get { return new Color(0.80f, 0.42f, 0.20f); } }

    void ShowShop()
    {
        Clear();
        Pad(0.864f); // 장보기 창을 지금(0.96)의 90%로 줄임 (0.96 × 0.9). 배경 그림은 그대로 꽉 찬다
        SetBg("bg_shop");
        PurgeExpired();
        int spent = cart.Sum(kv => Price(kv.Key) * kv.Value);
        int units = cart.Values.Sum();
        int left = run.budget - spent;

        // 왼쪽: 장보기 표 (노란 메뉴 카드 + 연한 줄 카드. 색은 장보기 시안 HTML을 따른다)
        // 모양은 로비 위쪽 바·시작 버튼에서 뽑은 특징(모래색 판, 진한 갈색 선과 아래 두께, 안쪽 크림 선)을 코드로 그린다 (HudStyle)
        HudStyle.Panel(root, "Panel", 0.02f, 0.03f, 0.545f, 0.97f, ShopYellow);
        UIKit.Label(root, run.week + "주차 장보기", 0.04f, 0.905f, 0.30f, 0.965f, 50, TextAnchor.MiddleLeft, ShopOrange);
        UIKit.Label(root, "남은 예산 <color=#E9684F>" + left.ToString("N0") + "원</color>", 0.30f, 0.905f, 0.525f, 0.965f, 42, TextAnchor.MiddleRight, ShopBrown);
        UIKit.Label(root, run.week == 1 ? "6끼분 담기 · 첫날 달걀은 무료" : "7끼분 채우기 · 이월 재료 있음, 가격 인상",
            0.04f, 0.855f, 0.32f, 0.905f, 28, TextAnchor.MiddleLeft, new Color(UIKit.Muted.r, UIKit.Muted.g, UIKit.Muted.b, 1f));
        // 단축키 안내: 너무 작아 안 보이던 것을 같은 줄 오른쪽에 읽히는 크기로
        UIKit.Label(root, "숫자 담기 · Shift+숫자 빼기", 0.30f, 0.855f, 0.525f, 0.905f, 24, TextAnchor.MiddleRight, new Color(UIKit.Muted.r, UIKit.Muted.g, UIKit.Muted.b, 0.9f));

        Color head = new Color(UIKit.Muted.r, UIKit.Muted.g, UIKit.Muted.b, 0.95f);
        UIKit.Label(root, "재료", 0.10f, 0.805f, 0.225f, 0.850f, 28, TextAnchor.MiddleLeft, head);
        UIKit.Label(root, "가격", 0.225f, 0.805f, 0.315f, 0.850f, 28, TextAnchor.MiddleCenter, head);
        UIKit.Label(root, "유통기한", 0.315f, 0.805f, 0.385f, 0.850f, 28, TextAnchor.MiddleCenter, head);
        UIKit.Label(root, "수량", 0.395f, 0.805f, 0.525f, 0.850f, 28, TextAnchor.MiddleCenter, head);
        UIKit.Box(root, "HeadLine", 0.035f, 0.800f, 0.53f, 0.804f, new Color(0.6f, 0.45f, 0.3f, 0.45f), false); // 표 머리글 아래 줄

        var shopList = Data.Active; // 해금 전에는 새우가 상점에 나오지 않는다
        for (int r = 0; r < shopList.Length; r++)
        {
            var ing = shopList[r];
            float top = RowTop(r);
            float bot = top - (RowStep - 0.012f);
            HudStyle.Panel(root, "Row", 0.035f, bot, 0.53f, top, HudStyle.SandSoft, true);
            int qty;
            cart.TryGetValue(ing, out qty);
            int own;
            run.inv.TryGetValue(ing, out own);
            SetIcon(UIKit.Box(root, "Icon", 0.054f, bot + 0.014f, 0.100f, top - 0.014f).gameObject.AddComponent<Image>(), ing); // 줄 왼쪽 테두리와 위아래에서 띄운다
            UIKit.Label(root, "<color=#A67C6DCC><size=24>[" + (r + 1) + "]</size></color> " + ing.name + (own > 0 ? " <color=#3F8F63><size=24>보유 " + own + "개</size></color>" : ""),
                0.106f, bot, 0.225f, top, 36, TextAnchor.MiddleLeft, ShopBrown);
            UIKit.Label(root, Price(ing).ToString("N0") + "원", 0.225f, bot, 0.315f, top, 36, TextAnchor.MiddleCenter, ShopBrown);
            UIKit.Label(root, ing.shelf + "일", 0.315f, bot, 0.385f, top, 36, TextAnchor.MiddleCenter, new Color(UIKit.Muted.r, UIKit.Muted.g, UIKit.Muted.b, 1f));
            ShopBtn("−", 0.380f, bot + 0.014f, 0.424f, top - 0.014f, () => ChangeCart(ing, -1), HudStyle.Tan, false, HudStyle.Text, 52, qty > 0);
            UIKit.Label(root, qty.ToString(), 0.424f, bot, 0.458f, top, 40, TextAnchor.MiddleCenter, qty > 0 ? ShopOrange : ShopBrown);
            ShopBtn("+", 0.458f, bot + 0.014f, 0.502f, top - 0.014f, () => ChangeCart(ing, 1), HudStyle.Gold, true, HudStyle.Text, 52, Price(ing) <= left);
        }

        UIKit.Box(root, "FootLine", 0.035f, 0.148f, 0.53f, 0.152f, new Color(0.6f, 0.45f, 0.3f, 0.45f), false); // 아래 영역 구분선
        UIKit.Label(root, "담은 재료 <color=#E9684F>" + units + "</color>개", 0.04f, 0.055f, 0.25f, 0.14f, 38, TextAnchor.MiddleLeft, ShopBrown);
        bool ok = units >= Need();
        // 주요 버튼 (로비 "게임 시작"과 같은 그림·글자 모양). 아직 못 누르면 연하게 보인다
        UIKit.PrimaryBtn(root, ok ? "구매 확정  [Enter]" : "최소 " + Need() + "개 필요", 0.27f, 0.058f, 0.512f, 0.14f, ConfirmShop, 40, ok);

        DrawBasket();
        scr = Scr.Shop;
        confirm = ok ? (System.Action)ConfirmShop : null;
    }

    // 하루 선택의 그림 틀: Resources/Art/<file>.png를 늘려 깐다. 그림이 없으면 HudStyle 모양으로 대신한다.
    RectTransform DayFrame(string file, string name, float x0, float y0, float x1, float y1, Color fallback)
    {
        var sp = Art.Load(file);
        if (sp == null) return HudStyle.Panel(root, name, x0, y0, x1, y1, fallback);
        var rt = UIKit.Box(root, name, x0, y0, x1, y1);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sp;
        img.type = Image.Type.Simple;
        img.preserveAspect = false;
        img.raycastTarget = false;
        return rt;
    }

    // 하루 선택의 방식 버튼: HudStyle 모양에 아이콘, 이름, 한 줄 설명. 고른 것만 노란색(윗면 빛).
    Button DayMethodBtn(string icon, string name, string tip, float x0, float y0, float x1, float y1, System.Action onClick, bool on, int size, bool enabled)
    {
        // 갈색 바 그림(ui_bar_tan)을 쓴다. 고른 방식은 노랗게 빛나고 살짝 커진다. 그림이 없으면 HudStyle 모양으로 대신한다.
        var sp = Art.Load("ui_btn_cream");
        RectTransform rt;
        Image img;
        if (sp != null)
        {
            rt = UIKit.Box(root, "Method_" + name, x0, y0, x1, y1);
            img = rt.gameObject.AddComponent<Image>();
            img.sprite = sp;
            img.type = Image.Type.Sliced; // 9칸 늘이기: 크기가 달라도 모서리와 테두리가 일그러지지 않는다
            rt.gameObject.AddComponent<SlicedFit>();
            img.color = on ? new Color(0.72f, 1f, 0.66f) : Color.white; // 고른 방식은 초록 바
        }
        else
        {
            rt = HudStyle.Panel(root, "Method_" + name, x0, y0, x1, y1, on ? HudStyle.Gold : HudStyle.SandSoft, false, on);
            img = rt.GetComponent<Image>();
        }
        if (!enabled) img.color = new Color(0.55f, 0.52f, 0.50f);
                var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.9f, 0.9f, 0.9f);
        cb.disabledColor = Color.white;
        b.colors = cb;
        b.interactable = enabled;
        b.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<PressDown>().depth = 4f;
        var sprite = Art.Load(icon);
        if (sprite != null)
        {
            var ir = UIKit.Box(rt, "Icon", 0.07f, 0.14f, 0.40f, 0.86f).gameObject.AddComponent<Image>();
            ir.sprite = sprite; ir.preserveAspect = true; ir.raycastTarget = false;
            ir.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.6f);
        }
        Color nameColor = enabled ? new Color(0.25f, 0.13f, 0.08f) : new Color(0.78f, 0.77f, 0.75f);
        var nameTxt = UIKit.Label(rt, name, 0.43f, 0.52f, 0.94f, 0.92f, size, TextAnchor.MiddleLeft, nameColor);
        nameTxt.resizeTextForBestFit = true; nameTxt.resizeTextMinSize = 18; nameTxt.resizeTextMaxSize = size;
        var tipTxt = UIKit.Label(rt, tip, 0.43f, 0.14f, 0.96f, 0.52f, 22, TextAnchor.MiddleLeft, new Color(nameColor.r, nameColor.g, nameColor.b, 0.85f));
        tipTxt.resizeTextForBestFit = true; tipTxt.resizeTextMinSize = 16; tipTxt.resizeTextMaxSize = 22;
        return b;
    }

    // 장보기 줄 안의 작은 버튼: HudStyle 모양(진한 갈색 선, 아래 두께, 안쪽 크림 선). gold면 노란 버튼처럼 윗면에 빛이 한 줄 든다.
    // enabled가 false면 연하게 보이고 눌리지 않는다.
    Button ShopBtn(string text, float x0, float y0, float x1, float y1, System.Action onClick, Color fill, bool gold, Color textColor,
        int size, bool enabled)
    {
        // 못 누를 때는 투명하게 하지 않고 회색 버튼으로 보인다 (선·두께 모양은 그대로)
        var rt = enabled ? HudStyle.Panel(root, "Btn_" + text, x0, y0, x1, y1, fill, true, gold)
            : HudStyle.Panel(root, "Btn_" + text, x0, y0, x1, y1, HudStyle.DisabledGray, true, false);
        var img = rt.GetComponent<Image>();
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.highlightedColor = new Color(1.0f, 1.0f, 1.0f);
        cb.pressedColor = new Color(0.9f, 0.9f, 0.9f);
        cb.disabledColor = Color.white;
        b.colors = cb;
        b.interactable = enabled;
        b.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<PressDown>().depth = 3f;
        var tc = enabled ? textColor : HudStyle.DisabledText;
        UIKit.Label(rt, text, 0.04f, 0.10f, 0.96f, 0.96f, size, TextAnchor.MiddleCenter, tc);
        return b;
    }

    // ---- 장바구니 연출
    // 재료 줄의 위쪽 위치(0~1). 날아가는 재료의 출발점도 이 값으로 정한다.
    static float RowStep { get { return Data.Active.Length > 6 ? 0.090f : 0.105f; } } // 재료가 7개가 되면 줄 간격을 줄인다
    static float RowTop(int r) { return 0.792f - r * RowStep; }
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
    Image bgFill;
    void SetBg(string file)
    {
        if (bgImg == null) return;
        var s = file == null ? null : Art.Load(file);
        bgImg.sprite = s;
        bgImg.preserveAspect = true; // 창 비율이 달라도 그림이 늘어나지 않는다
        bgImg.enabled = s != null;
        bgImg.rectTransform.localScale = Vector3.one; // 기본은 화면 전체
        if (bgFill != null) bgFill.gameObject.SetActive(false);
    }

    // 조리 화면: 접시·도마·가스레인지처럼 배경 그림 위치에 맞춰 놓는 것들이 내용(root)과 같은 비율로 줄어드므로, 배경 그림도 같은 비율로 줄인다.
    // 줄어든 만큼 생기는 바깥은 같은 그림을 어둡게 깔아 채운다 (가장자리 100픽셀 여백 규칙 + 그림과 요소의 위치 일치)
    void SetBgScaled(string file)
    {
        SetBg(file);
        if (bgImg == null || !bgImg.enabled) return;
        bgImg.rectTransform.localScale = new Vector3(MarginScale, MarginScale, 1f);
        if (bgFill == null)
        {
            var fr = UIKit.Box(canvasRT, "BackgroundFill", 0, 0, 1, 1);
            bgFill = fr.gameObject.AddComponent<Image>();
            bgFill.raycastTarget = false;
            bgFill.preserveAspect = true;
        }
        bgFill.gameObject.SetActive(true);
        bgFill.sprite = null; // 단색: 조리대 그림 가장자리 네 변의 평균색(식탁 나무 색). 같은 그림을 크게 깔면 도마·접시 가장자리가 비쳐 보인다
        bgFill.color = new Color(0.989f, 0.775f, 0.652f);
        // 채움 칸은 항상 그림 바로 아래. (한 번만 옮기면 두 번째 조리부터 채움 칸이 그림 위로 올라가 배경이 사라졌다)
        bgFill.transform.SetSiblingIndex(bgImg.transform.GetSiblingIndex());
        if (bgFill.transform.GetSiblingIndex() > bgImg.transform.GetSiblingIndex())
            bgImg.transform.SetSiblingIndex(bgFill.transform.GetSiblingIndex());
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
        Pad(1f);
        SetBg("bg_kitchen_blur"); // 흐린 주방 배경: 앞의 도마·철판·바가 또렷하게 보인다
        // 배치도(1920x1080 기준 픽셀)대로 놓는다. 모든 요소는 화면 가장자리에서 100픽셀 안쪽에 둔다 (로비와 같게).
        // 위 바: 주차·일차, 남은 돈
        DayFrame("lobby_bar", "TopBar", DX(100), DY(167), DX(700), DY(103), HudStyle.Sand);
        DayIcon("icon_week", DX(121), DY(148), DX(152), DY(116));
        DayIcon("icon_money", DX(386), DY(148), DX(414), DY(116));
        var weekTxt = UIKit.Label(root, run.week + "주차 " + run.day + "일차", DX(168), DY(158), DX(368), DY(112), 36, TextAnchor.MiddleLeft, HudStyle.Text);
        weekTxt.resizeTextForBestFit = true; weekTxt.resizeTextMinSize = 20; weekTxt.resizeTextMaxSize = 36;
        var moneyTxt = UIKit.Label(root, run.budget.ToString("N0") + "원", DX(428), DY(158), DX(678), DY(112), 36, TextAnchor.MiddleLeft, HudStyle.Text);
        moneyTxt.resizeTextForBestFit = true; moneyTxt.resizeTextMinSize = 20; moneyTxt.resizeTextMaxSize = 36;
        var stTxt = UIKit.Label(root, "비상 라면 " + (Balance.Lifelines - run.strikes) + "/" + Balance.Lifelines + "   먹은 끼니 " + run.dishes.Count + "개   (" + run.day + "/" + Balance.Days + "일)",
            DX(830), DY(262), DX(1450), DY(200), 32, TextAnchor.MiddleLeft, HudStyle.Cream);
        var stOutline = stTxt.gameObject.AddComponent<Outline>(); stOutline.effectColor = HudStyle.Text; stOutline.effectDistance = new Vector2(2f, -2f); // 흐린 배경 위에서도 읽히게

        // 가운데: 철판 뒤의 갈색 탭에 오늘의 요리 한 줄. 고른 재료와 방식을 색으로 구분한다
        DayFrame("ui_guide_board", "SummaryTab", DX(990), DY(352), DX(1340), DY(273), HudStyle.Tan); // 안내판
        string ingText = "<color=#D9533C>" + string.Join(" + ", sel.Select(i => i.name)) + "</color>";
        string methodText = selMethod.HasValue ? "<color=#3F8F63>" + Data.GetMethod(selMethod.Value).name + "</color>" : "<color=#8C7060>방식 선택</color>";
        string tabText = sel.Count == 0 ? "재료를 골라주세요 (최대 " + Balance.MaxIngredients(run.week) + "개)" : ingText + " · " + methodText; // 아무것도 안 골랐을 때는 안내 문구
        var sumTxt = UIKit.Label(root, tabText, DX(1002), DY(338), DX(1328), DY(284), 30, TextAnchor.MiddleCenter, HudStyle.Text);
        sumTxt.resizeTextForBestFit = true; sumTxt.resizeTextMinSize = 16; sumTxt.resizeTextMaxSize = 30;
        // 철판(요리할 재료): 여기로 끌어다 놓아서 고르고, 철판에서 밖으로 끌어내면 뺀다
        var board = DayFrame("ui_tray", "Pan", DX(876), DY(657), DX(1457), DY(340), HudStyle.SandSoft);
        if (sel.Count == 0) UIKit.Label(board, "재료를 여기로 끌어다 놓기", 0.10f, 0.30f, 0.90f, 0.70f, 34, TextAnchor.MiddleCenter, new Color(0.96f, 0.93f, 0.85f, 0.85f));
        // 왼쪽 도마(보유 재료)
        DayFrame("ui_cutting_board", "Board", DX(237), DY(965), DX(797), DY(180), new Color(0.85f, 0.65f, 0.40f));
        if (guideMeal)
        {
            IngredientToken(Data.Egg, 1, null, DX(1090), DY(540), DX(1240), DY(440), board, true, false);
            HudStyle.Panel(root, "GuideNote", DX(262), DY(740), DX(772), DY(380), HudStyle.SandSoft); // 도마 위에 쪽지를 얹는다
            UIKit.Label(root, "엄마가 보낸 달걀!\n첫날은 달걀을 굽는다. 불 세기와 꺼내기를 익혀 보자.\n실패해도 굶지 않으니 다시 하면 돼!",
                DX(285), DY(725), DX(750), DY(395), 30, TextAnchor.MiddleLeft, HudStyle.Text);
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
                    // 철판 위: 카드 없이 재료 그림만. 많아지면 칸을 줄인다 (철판 안쪽 x 905~1430, y 368~625)
                    int n = sel.Count, cols = n <= 4 ? n : n <= 8 ? (n + 1) / 2 : 5, rows = (n + cols - 1) / cols;
                    float cw = 525f / cols, ch = 257f / rows, side = Mathf.Min(cw, ch, 150f) * 0.92f; // 정사각형 칸
                    float cx = 905f + (onBoard % cols + 0.5f) * cw, cy = 368f + (onBoard / cols + 0.5f) * ch;
                    IngredientToken(ing, kv.Value, r, DX(cx - side / 2f), DY(cy + side / 2f), DX(cx + side / 2f), DY(cy - side / 2f), board, true, true);
                    onBoard++;
                }
                else
                {
                    // 도마 위 2칸 x 4줄 (도마 안쪽 x 250~785, y 332~945)
                    float x0 = 276f + (onTable % 2) * 250f, top = 346f + (onTable / 2) * 148f;
                    IngredientToken(ing, kv.Value, r, DX(x0), DY(top + 128f), DX(x0 + 236f), DY(top), board, false, true);
                    onTable++;
                }
            }
        }

        // 오른쪽: 방식 (바 4개, 고른 것만 초록)
        var openMethods = Data.ActiveMethods; // 튀기기는 "자취 생존자"를 얻기 전에는 선택지에 나오지 않는다
        float[] barTop = { 312f, 437f, 555f, 673f };
        for (int k = 0; k < openMethods.Length && k < 4; k++)
        {
            var md = openMethods[k];
            bool on = selMethod == md.id;
            // 방식마다 핵심 조작을 한마디로 (고르기 전에 알 수 있게)
            // 바가 좁아 긴 문장은 11px까지 줄어 안 읽혔다. 한 줄에 들어가는 짧은 말로 쓴다
            string tip = md.id == Method.Grill ? "제때 뒤집기" : md.id == Method.Stirfry ? "계속 젓기"
                : md.id == Method.Boil ? "넘치면 뚜껑" : "센불 유지";
            DayMethodBtn(md.id == Method.Grill ? "icon_pan" : md.id == Method.Stirfry ? "icon_wok" : md.id == Method.Boil ? "icon_pot" : "icon_fry",
                md.name, "[" + "QWER"[k] + "] " + tip, DX(1541), DY(barTop[k] + 96f), DX(1812), DY(barTop[k]), () => { selMethod = md.id; ShowDay(); },
                on, 36, !guideMeal || on);
        }

        // 엄마 메시지: 파란 바 전체가 눌리는 알림 (아이콘 + 제목 + 내용). 글이 길면 바가 아래로 늘어나고, 눌러 볼 수 있다는 것이 보이게 느리게 두근거린다.
        string hint = sel.Count == 0
            ? "밥을 잘 먹고 다니니?"
            : string.Join(" ", sel.Select(i => Data.MomHint(i)));
        int hintLines = Mathf.Max(2, Mathf.CeilToInt(hint.Length / 17f));
        float barBot = Mathf.Clamp(688f + 62f + hintLines * 31f, 785f, 852f);
        var hintCard = DayFrame("ui_bar_blue", "Hint", DX(891), DY(barBot), DX(1440), DY(688), HudStyle.Sand);
        var hintBg = hintCard.GetComponent<Image>();
        if (hintBg != null && hintBg.sprite != null && hintBg.sprite.border.sqrMagnitude > 1f) { hintBg.type = Image.Type.Sliced; hintCard.gameObject.AddComponent<SlicedFit>(); }
        if (!guideMeal && hintBg != null)
        {
            hintBg.raycastTarget = true;
            var hb = hintCard.gameObject.AddComponent<Button>();
            hb.targetGraphic = hintBg;
            hb.transition = Selectable.Transition.None;
            hb.onClick.AddListener(ShowMomRecipes);
            var pulse = hintCard.gameObject.AddComponent<Pulse>(); pulse.scaleAmp = 0.014f; pulse.speed = 2.6f;
        }
        var mail = DayIcon("icon_mail", DX(928), DY(762), DX(992), DY(710));
        if (!guideMeal) { var mp = mail.gameObject.AddComponent<Pulse>(); mp.scaleAmp = 0.06f; mp.rotAmp = 7f; mp.speed = 4.2f; }
        UIKit.Label(root, "[엄마]", DX(1008), DY(728), DX(1280), DY(692), 24, TextAnchor.MiddleLeft, HudStyle.Text);
        if (!guideMeal) UIKit.Label(root, "[M] 추천 요리", DX(1290), DY(728), DX(1425), DY(692), 20, TextAnchor.MiddleRight, HudStyle.TextSoft);
        var hintTxt = UIKit.Label(root, hint, DX(1008), DY(barBot - 10f), DX(1425), DY(728), 26, TextAnchor.UpperLeft, HudStyle.Text);
        hintTxt.resizeTextForBestFit = true; hintTxt.resizeTextMinSize = 18; hintTxt.resizeTextMaxSize = 26;

        // 아래: 도감, 조리 시작
        UIKit.DexBtn(root, DX(1275), DY(978), 130f / 1920f, ShowDex);
        bool canCook = sel.Count > 0 && selMethod.HasValue;
        UIKit.PrimaryBtn(root, "조리 시작 [Enter]", DX(1477), DY(970), DX(1812), DY(882), StartCook, 30, canCook);
        scr = Scr.Day;
        confirm = canCook ? (System.Action)StartCook : null;
    }

    static float DX(float px) { return px / 1920f; }
    static float DY(float px) { return 1f - px / 1080f; } // 위에서 잰 픽셀을 아래 기준 비율로

    RectTransform DayIcon(string file, float x0, float y0, float x1, float y1)
    {
        var rt = UIKit.Box(root, "Icon_" + file, x0, y0, x1, y1);
        var im = rt.gameObject.AddComponent<Image>();
        im.sprite = Art.Load(file); im.preserveAspect = true; im.raycastTarget = false;
        if (im.sprite == null) im.enabled = false;
        return rt;
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
            var color = dl == 1 ? new Color(0.98f, 0.78f, 0.70f) : HudStyle.SandSoft; // 오늘까지인 재료만 살짝 붉게
            rt = HudStyle.Panel(root, "Ing_" + ing.name, x0, y0, x1, y1, color, true);
            SetIcon(UIKit.Box(rt, "Icon", 0.03f, 0.08f, 0.40f, 0.92f).gameObject.AddComponent<Image>(), ing); // 왼쪽에 재료 그림
            var nameTxt = UIKit.Label(rt, ing.name, 0.38f, 0.48f, 0.98f, 0.94f, 32, TextAnchor.MiddleCenter, HudStyle.Text);
            nameTxt.resizeTextForBestFit = true; nameTxt.resizeTextMinSize = 16; nameTxt.resizeTextMaxSize = 32; // 이름이 길어도 한 줄에
            var subTxt = UIKit.Label(rt, (key.HasValue ? "[" + key.Value + "] " : "") + "x" + qty + (tag == "" ? "" : " " + tag), 0.34f, 0.06f, 0.98f, 0.50f, 20, TextAnchor.MiddleCenter, dl == 1 ? UIKit.Bad : HudStyle.TextSoft);
            subTxt.resizeTextForBestFit = true; subTxt.resizeTextMinSize = 12; subTxt.resizeTextMaxSize = 20; // 오른쪽 끝이 잘리지 않게
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
        bool capped = false;
        if (sel.Contains(ing)) sel.Remove(ing);
        else if (sel.Count >= Balance.MaxIngredients(run.week)) capped = true; // 이번 주에 넣을 수 있는 재료 수를 넘기지 않는다
        else sel.Add(ing);
        ShowDay();
        if (capped)
        {
            // 철판 아래에 잠깐 알려 준다. 다음 행동(ShowDay)으로 화면이 다시 그려지면 같이 사라진다.
            var note = UIKit.Label(root, "이번 주는 재료를 최대 " + Balance.MaxIngredients(run.week) + "개까지 넣을 수 있어요", DX(830), DY(196), DX(1450), DY(146), 30, TextAnchor.MiddleLeft, UIKit.Bad);
            var o = note.gameObject.AddComponent<Outline>(); o.effectColor = HudStyle.Cream; o.effectDistance = new Vector2(2f, -2f);
            Destroy(note.gameObject, 2.2f);
            Sound.Play("order_wrong");
        }
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
        if (kb.qKey.wasPressedThisFrame && scr == Scr.Lobby) { ShowQuests(); return; } // 퀘스트도 로비에서만
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
    GameObject titleBtn;

    // 로비에서 타이틀로. 저장된 주가 있으면 타이틀의 [이어하기]로 돌아올 수 있다.
    void GoTitle()
    {
        if (scr != Scr.Lobby || modal != null) return;
        Sound.Music("bgm_title");
        ShowTitle();
    }

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
        RunSave.Save(run); // 주를 취소했으니 저장도 1일 차로 맞춘다
        cart.Clear();
        ClearFlying();
        sel.Clear();
        selMethod = null;
        ShowLobby();
    }

    void Update()
    {
        if (Settings.IsOpen) return; // 설정 화면이 열려 있으면 뒤의 게임은 입력을 받지 않는다
        var kb = Keyboard.current;
        CornerDock.Suppress = modal != null || dexDetail != null; // 팝업이 가장 위: 도움말·설정 아이콘도 팝업 뒤로 숨긴다
        if (lobbyBtn != null)
            lobbyBtn.SetActive((scr == Scr.Shop || scr == Scr.Day || scr == Scr.Cook || scr == Scr.Meal) && modal == null && dexDetail == null);
        if (titleBtn != null)
            titleBtn.SetActive(scr == Scr.Lobby && modal == null && dexDetail == null);
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
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) cook.Complete(); // 조리 완료(docs/27)
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
        Quests.CloseWeek(run); // 이번 주 퀘스트를 판정해 두고, 로비에서 받게 한다
        run.week++;
        run.day = 1;
        run.strikes = 0;
        run.dishes.Clear();
        run.budget = NextBudget(sat);
        RunSave.Save(run); // 이어하기: 새 주의 시작 상태를 저장한다
        cart.Clear();
        sel.Clear();
        selMethod = null;
        ShowLobby(); // 정산 뒤에는 로비로 돌아온다
    }

    void NextDay()
    {
        run.day++;
        if (run.day <= Balance.Days) RunSave.Save(run); // 이어하기: 하루가 끝날 때마다 자동 저장
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

        // 도감 버튼 높이는 그림 비율로 정해진다. 새 그림(153x124)은 예전보다 세로가 길어서 폭 0.15면 먹은 요리 카드(0.215부터)의 7일차 줄을 가렸다.
        // 다음 주 버튼과 같은 띠(0.05~0.19) 안에 들어가는 폭으로 둔다.
        UIKit.DexBtn(root, 0.06f, 0.05f, 0.10f, ShowDex);
        UIKit.PrimaryBtn(root, "다음 주 [Enter]", 0.57f, 0.05f, 0.94f, 0.19f, NextWeek, 40); // 오른쪽 카드들과 좌우 끝을 맞춘다
        BuildQuestNote(); // 이번 주 퀘스트 달성 안내 (GameFlow.Quest.cs)
        scr = Scr.Result;
        confirm = NextWeek;
        var gained = Titles.Check(dex.Values.ToList()); // 칭호는 한 주가 끝나는 결산 때 판정한다
        pendingGain.AddRange(gained); // 알림은 로비에 들어올 때 보여 준다
    }

    void ShowFail(string reason)
    {
        RunSave.Delete(); // 굶어서 끝난 판은 이어하기에서 지운다
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
        UIKit.PrimaryBtn(root, "다시 하기 [Enter]", 0.30f, 0.05f, 0.70f, 0.19f, () => { RunSave.Delete(); ShowTitle(); }, 48); // 굶어서 끝난 판은 이어하기에서 지운다
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

    // 요리 그림 칸. 미리 만들어 둔 요리 사진(Resources/AiDishes)이 있으면 그것을, 없으면 도감처럼 조리된 재료 그림을 옆으로 나란히 보여 준다.
    // generate가 false면(망친 요리) 사진은 쓰지 않고 재료를 그 익힘 단계 그림(날것·탄 것)으로만 보여 준다. 사진 파일은 익힘과 상관없이 "잘된 요리" 한 장이라서다.
    void Photo(Transform parent, float x0, float y0, float x1, float y1, Ingredient[] ings, Doneness[] stages, Method m, bool generate = true)
    {
        var made = generate ? DishArt.Load(ings, stages, m) : null;
        var frame = UIKit.Box(parent, "DishPhoto", x0, y0, x1, y1, Color.white); // 흰 사진틀 (기울이지 않고 반듯하게)
        // 사진이 있으면 아래 테두리를 두껍게(0.19), 재료 그림이면 얇게
        var area = UIKit.Box(frame, "PicArea", 0.04f, made != null ? 0.19f : 0.10f, 0.96f, 0.97f);
        if (made == null) { IngredientRow(area, ings, stages); return; }
        var pic = UIKit.Box(area, "Pic", 0, 0, 1, 1);
        var fit = pic.gameObject.AddComponent<AspectRatioFitter>(); // 비율 맞춤(FitInParent)은 부모 전체를 쓰므로 칸을 따로 둔다
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = 1f;
        var img = pic.gameObject.AddComponent<RawImage>();
        img.raycastTarget = false;
        img.texture = made;
    }

    // 조리된 재료 그림을 가로로 나란히 (도감과 같은 모양). 재료 그림이 없으면 그 재료의 색 네모로 대신한다.
    void IngredientRow(Transform parent, Ingredient[] ings, Doneness[] stages)
    {
        int n = ings.Length;
        for (int k = 0; k < n; k++)
        {
            var stage = stages != null && k < stages.Length ? stages[k] : Doneness.Good;
            var sp = Art.Get(ings[k], stage) ?? Art.ShopIcon(ings[k]);
            var im = UIKit.Box(parent, "Pic", k / (float)n + 0.01f, 0, (k + 1) / (float)n - 0.01f, 1).gameObject.AddComponent<Image>();
            im.sprite = sp;
            im.preserveAspect = true;
            im.raycastTarget = false;
            im.color = sp != null ? Color.white : ings[k].cooked;
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


    // 엄마의 전언: 보유 재료로 만들 추천 요리와 레시피. 고르면 재료와 방식이 바로 선택된다.
    void ShowMomRecipes()
    {
        CloseModal();
        var list = Recipes.Suggest(run.inv.Keys, DaysLeft, 3, Data.Unlocked);
        modal = ModalDim("MomRecipesDim");
        var m = UIKit.Box(modal.transform, "MomRecipes", 0.05f, 0.10f, 0.95f, 0.90f, UIKit.Card);
        m.localScale = Vector3.one * 0.97f; // 가장자리 여백
        UIKit.Label(m, "엄마의 전언", 0.04f, 0.87f, 0.6f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        UIX.CloseX(m, 0.915f, 0.885f, 0.975f, 0.975f, CloseModal);
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

    // 팝업 뒤를 어둡게 덮는 칸. 팝업 창은 이 칸의 자식으로 만들고, 이 칸을 modal로 두면 닫을 때 같이 사라진다.
    // 화면 전체를 덮어 뒤의 버튼이 눌리지 않게 막는다.
    GameObject ModalDim(string name)
    {
        return UIKit.Box(canvasRT, name, 0, 0, 1, 1, new Color(0f, 0f, 0f, 0.45f), false).gameObject;
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
