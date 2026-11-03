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
    RectTransform panZone;   // 팬/냄비 바닥 원 영역 (도구를 갖다 댔는지 검사)
    RectTransform lidRT, spatulaRT;
    RectTransform restRT;    // 도구를 내려놓는 조리대
    ToolHold lidHold;
    string sayText;          // 잠깐 띄우는 안내 ("아직 뒤집을 때가 아니야" 등)
    float sayTimer;
    readonly List<RectTransform> itemRects = new List<RectTransform>(); // 재료 아이템 (트레이/팬/접시를 오감)
    readonly List<Image> itemImgs = new List<Image>();
    readonly List<Text> itemLabels = new List<Text>();
    readonly List<bool> swArt = new List<bool>(); // 이 재료가 그림(5단계)으로 그려지는지
    readonly List<DragItem> itemDrags = new List<DragItem>();
    RectTransform flameRT, knobRT, plateRT;
    Text fireLabel;
    Text timeLabel, stateLabel;

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
        root = UIKit.Box(canvasRT, "Screen", 0, 0, 1, 1);

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
        cook = null;
        scr = Scr.Other;
        confirm = null;
        dayList.Clear();
        panZone = lidRT = spatulaRT = null;
        lidHold = null;
        restRT = null;
        sayTimer = 0f;
        itemRects.Clear();
        itemImgs.Clear();
        itemLabels.Clear();
        swArt.Clear();
        itemDrags.Clear();
        flameRT = knobRT = plateRT = null;
        fireLabel = null;
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
        var art = Resources.Load<Texture2D>("Background Art/귀여운 주방풍 COOKER 타이틀 화면 (1)");
        if (art != null)
        {
            // 타이틀 그림에 START/EXIT가 그려져 있어서 그 위에 투명 버튼을 놓는다. 화면을 꽉 채우도록 비율 유지
            var pic = UIKit.Box(root, "TitleArt", 0, 0, 1, 1);
            var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = (float)art.width / art.height;
            var img = pic.gameObject.AddComponent<RawImage>();
            img.texture = art;
            img.raycastTarget = false;
            UIKit.HitArea(pic, 0.33f, 0.25f, 0.73f, 0.40f, StartRun);
            UIKit.HitArea(pic, 0.36f, 0.08f, 0.72f, 0.245f, Quit);
            UIKit.Btn(root, "도감 [D]", 0.83f, 0.04f, 0.97f, 0.13f, ShowDex, UIKit.Muted, 36);
            UIKit.Label(root, "Enter: 시작", 0.03f, 0.03f, 0.25f, 0.09f, 30, TextAnchor.MiddleLeft, UIKit.Ink);
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
        cart.Clear();
        sel.Clear();
        selMethod = null;
        ShowShop();
    }

    // ---------------------------------------------------------------- 장보기
    void ShowShop()
    {
        Clear();
        PurgeExpired();
        int spent = cart.Sum(kv => Price(kv.Key) * kv.Value);
        int units = cart.Values.Sum();
        int left = run.budget - spent;

        UIKit.Label(root, run.week + "주차 장보기", 0.03f, 0.88f, 0.4f, 0.98f, 60, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Label(root, "남은 예산 " + left.ToString("N0") + "원", 0.45f, 0.88f, 0.97f, 0.98f, 56, TextAnchor.MiddleRight);
        UIKit.Label(root, (run.week == 1 ? "6끼분을 사자. 첫날 한 끼는 엄마가 달걀을 보내준다." : "7끼분을 맞추자(남은 재료는 이월). 엄마의 달걀은 없고 재료값이 올랐다!") + " 가격은 1끼분 기준, 이번 주 장보기는 한 번뿐!   (숫자키 1~6 담기 / Shift+숫자 빼기 / Enter 확정)",
            0.03f, 0.80f, 0.97f, 0.88f, 28, TextAnchor.MiddleLeft, UIKit.Muted);

        UIKit.Label(root, "재료", 0.05f, 0.72f, 0.25f, 0.79f, 34, TextAnchor.MiddleLeft, UIKit.Muted);
        UIKit.Label(root, "가격", 0.27f, 0.72f, 0.42f, 0.79f, 34, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(root, "유통기한", 0.43f, 0.72f, 0.60f, 0.79f, 34, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(root, "수량", 0.64f, 0.72f, 0.86f, 0.79f, 34, TextAnchor.MiddleCenter, UIKit.Muted);

        for (int r = 0; r < Data.All.Length; r++)
        {
            var ing = Data.All[r];
            float top = 0.71f - r * 0.095f;
            float bot = top - 0.085f;
            UIKit.Box(root, "Row", 0.03f, bot, 0.97f, top, UIKit.Card);
            int qty;
            cart.TryGetValue(ing, out qty);
            int own;
            run.inv.TryGetValue(ing, out own);
            UIKit.Label(root, "[" + (r + 1) + "] " + ing.name + (own > 0 ? " (보유 " + own + ")" : ""), 0.05f, bot, 0.25f, top, 40, TextAnchor.MiddleLeft);
            UIKit.Label(root, Price(ing).ToString("N0") + "원", 0.27f, bot, 0.42f, top, 40);
            UIKit.Label(root, ing.shelf + "일", 0.43f, bot, 0.60f, top, 40);
            UIKit.Btn(root, "-", 0.64f, bot + 0.01f, 0.70f, top - 0.01f, () => ChangeCart(ing, -1), UIKit.Muted, 50, qty > 0);
            UIKit.Label(root, qty.ToString(), 0.70f, bot, 0.80f, top, 44);
            UIKit.Btn(root, "+", 0.80f, bot + 0.01f, 0.86f, top - 0.01f, () => ChangeCart(ing, 1), UIKit.Accent, 50, Price(ing) <= left);
        }

        UIKit.Label(root, "담은 재료 " + units + "개", 0.03f, 0.03f, 0.40f, 0.13f, 44, TextAnchor.MiddleLeft);
        bool ok = units >= Need();
        UIKit.Btn(root, ok ? "구매 확정  [Enter]" : "최소 " + Need() + "개 필요", 0.55f, 0.03f, 0.97f, 0.13f, ConfirmShop, UIKit.Good, 48, ok);
        scr = Scr.Shop;
        confirm = ok ? (System.Action)ConfirmShop : null;
    }

    // 이번 주 재료 가격: 매주 오른다
    int Price(Ingredient i) { return Mathf.RoundToInt(i.price * Mathf.Pow(Balance.PriceGrowth, run.week - 1) / 10f) * 10; }
    // 이번 주 장보기에서 최소로 담아야 하는 개수. 이월된 재료만큼 줄어든다.
    int Need() { return run.week == 1 ? Balance.MinUnits : Mathf.Max(0, Balance.Days - run.inv.Values.Sum()); }

    void ChangeCart(Ingredient ing, int delta)
    {
        int qty;
        cart.TryGetValue(ing, out qty);
        qty = Mathf.Max(0, qty + delta);
        if (qty == 0) cart.Remove(ing); else cart[ing] = qty;
        ShowShop();
    }

    void ConfirmShop()
    {
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
        var color = onBoard ? UIKit.Good : (dl == 1 ? UIKit.Bad : UIKit.Accent);
        var rt = UIKit.Box(root, "Ing_" + ing.name, x0, y0, x1, y1, color);
        var name = UIKit.Label(rt, (key.HasValue ? "[" + key.Value + "] " : "") + ing.name, 0.05f, 0.45f, 0.95f, 0.92f, 34, TextAnchor.MiddleCenter, Color.white);
        name.gameObject.AddComponent<Outline>().effectColor = new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.8f);
        UIKit.Label(rt, "x" + qty + (tag == "" ? "" : "  " + tag), 0.05f, 0.08f, 0.95f, 0.45f, 26, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f));
        if (!draggable) return;
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
    void StartCook()
    {
        var chosen = sel.ToArray();
        var md = Data.GetMethod(selMethod.Value);
        Clear();
        cook = new Cook(chosen, md, Balance.Ease(run.week));
        stirTimer = 0f;

        UIKit.Label(root, md.name + " 중", 0.03f, 0.88f, 0.5f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        timeLabel = UIKit.Label(root, "", 0.6f, 0.88f, 0.97f, 0.98f, 40, TextAnchor.MiddleRight, UIKit.Muted);
        UIKit.Label(root, guideMeal ? "[첫날 가이드] 도마의 재료를 팬으로 끌어다 넣고, 색이 알맞게 변하면 접시로 끌어내! 너무 오래 두면 탄다. " + md.guide : md.guide,
            0.03f, 0.78f, 0.97f, 0.88f, 32, TextAnchor.MiddleLeft, UIKit.Muted);

        // 자리: 아래 왼쪽 도마(트레이) / 가운데 팬(끓이기는 냄비) / 아래 오른쪽 접시. 색(그림) 변화가 유일한 단서
        // 팬 밑으로 삐져나오는 불꽃 (불 세기에 따라 커진다). 팬보다 먼저 그려서 팬 뒤에 깔린다
        flameRT = UIKit.ToolImage(root, "Flame", UIKit.FlameRing(), 0, 0, 0, 0);
        flameRT.GetComponent<Image>().raycastTarget = false;
        if (md.id == Method.Boil) UIKit.Pot(root, PanX0, PanY0, PanX1, PanY1);
        else UIKit.Pan(root, PanX0, PanY0, PanX1, PanY1);
        UIKit.CuttingBoard(root, TrayX0, 0.05f, TrayX1, 0.24f);
        plateRT = UIKit.Plate(root, 0.64f, 0.05f, 0.97f, 0.24f);
        panZone = UIKit.Box(root, "PanZone", 0, 0, 0, 0);
        SetSquare(panZone, PanCenter(), PanFloorRadius() * 2f);
        for (int i = 0; i < chosen.Length; i++)
        {
            int idx = i;
            var rt = UIKit.Box(root, "Item" + i, 0, 0, 0.1f, 0.1f, chosen[i].raw);
            var img = rt.GetComponent<Image>();
            // 도마 -> 팬/냄비로 끌어다 넣고, 팬 -> 접시로 끌어낸다 (끓이기는 집게로 건진다)
            var drag = rt.gameObject.AddComponent<DragItem>();
            drag.canDrag = () => cook != null && (cook.items[idx].spot == Spot.Tray || (cook.items[idx].spot == Spot.Pan && cook.m.id != Method.Boil));
            drag.onDropAt = pos =>
            {
                if (cook == null) return;
                var it = cook.items[idx];
                if (it.spot == Spot.Tray && OverPan(pos))
                {
                    if (cook.m.id == Method.Boil && cook.lidOn) { Say("뚜껑부터 열어!"); return; }
                    cook.Put(idx);
                }
                else if (it.spot == Spot.Pan && RectTransformUtility.RectangleContainsScreenPoint(plateRT, pos, null)) cook.TakeOut(idx);
            };
            itemDrags.Add(drag);
            bool hasArt = Art.HasSet(chosen[i]);
            if (hasArt) img.preserveAspect = true;
            itemRects.Add(rt);
            itemImgs.Add(img);
            // 재료 색 위에서도 읽히게 진한 테두리를 두른 흰 글씨를 카드 가운데에 크게
            var label = UIKit.Label(rt, "", 0.04f, 0.06f, 0.96f, 0.94f, 34, TextAnchor.MiddleCenter, Color.white);
            var lo = label.gameObject.AddComponent<Outline>();
            lo.effectColor = UIKit.Ink;
            lo.effectDistance = new Vector2(2.5f, 2.5f);
            itemLabels.Add(label);
            swArt.Add(hasArt);
            PlaceItem(i);
        }

        // 불 조절: 가스레인지 밸브를 돌린다 (왼쪽 약불 · 위 중불 · 오른쪽 강불). 숫자키 1·2·3도 된다
        UIKit.Box(root, "Stove", 0.64f, 0.57f, 0.97f, 0.77f, new Color(0.93f, 0.93f, 0.95f));
        // 글씨는 밸브 표시가 가리키는 방향(왼쪽 위 -60도 / 위 / 오른쪽 위 +60도)에 놓는다
        UIKit.Label(root, "약", 0.673f, 0.66f, 0.717f, 0.71f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(root, "중", 0.718f, 0.705f, 0.762f, 0.765f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(root, "강", 0.763f, 0.66f, 0.807f, 0.71f, 30, TextAnchor.MiddleCenter, UIKit.Muted);
        knobRT = UIKit.ToolImage(root, "Knob", UIKit.Knob(), 0, 0, 0, 0);
        SetSquare(knobRT, new Vector2(0.74f * RefW, 0.64f * RefH), 130f);
        knobRT.gameObject.AddComponent<KnobDrag>().onAngle = deg =>
        {
            // 위쪽을 0도로, 시계 방향(오른쪽)으로 돌릴수록 센 불
            if (cook != null) cook.SetFire(deg < -30f ? 0 : deg > 30f ? 2 : 1);
        };
        fireLabel = UIKit.Label(root, "", 0.83f, 0.60f, 0.96f, 0.74f, 52, TextAnchor.MiddleCenter, UIKit.Accent);

        // 방식별 도구: 조리대(오른쪽 칸)에 놓여 있다. 클릭하면 손에 집히고, 들고 클릭하면 쓴다. 조리대를 클릭하거나 오른쪽 클릭이면 내려놓는다
        restRT = UIKit.Box(root, "Rest", 0.64f, 0.35f, 0.97f, 0.55f, UIKit.Card);
        if (md.id != Method.Boil)
        {
            UIKit.Label(root, md.id == Method.Stirfry ? "뒤집개를 집어서 팬 위에서 휘젓기 (A·D)" : "뒤집개를 집고, 때가 오면 팬을 클릭 (F)",
                0.65f, 0.495f, 0.96f, 0.545f, 26, TextAnchor.MiddleLeft, UIKit.Muted);
            spatulaRT = UIKit.ToolImage(root, "Spatula", UIKit.Spatula(), 0.67f, 0.365f, 0.94f, 0.49f);
            var hold = spatulaRT.gameObject.AddComponent<ToolHold>();
            if (md.id == Method.Stirfry)
            {
                hold.onMove = (pos, delta) =>
                {
                    if (!OverPan(pos) || delta.magnitude < 2f) return;
                    stirTimer = 0.25f;
                    spatulaRT.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 25f) * 12f); // 휘젓는 손맛
                };
                hold.onUse = OverRest;
            }
            else
                hold.onUse = pos =>
                {
                    if (cook != null && OverPan(pos))
                    {
                        if (cook.flipAvailable) { cook.Flip(); Say("뒤집었다!"); }
                        else Say("아직 뒤집을 때가 아니야");
                    }
                    return OverRest(pos);
                };
        }
        else
        {
            UIKit.Label(root, "집게로 재료 건지기 · 뚜껑을 집으면 열린다 (S)", 0.65f, 0.495f, 0.96f, 0.545f, 26, TextAnchor.MiddleLeft, UIKit.Muted);
            var tongs = UIKit.ToolImage(root, "Tongs", UIKit.Tongs(), 0.655f, 0.37f, 0.83f, 0.49f);
            tongs.gameObject.AddComponent<ToolHold>().onUse = pos =>
            {
                if (cook == null) return true;
                for (int i = 0; i < cook.items.Length; i++)
                {
                    if (cook.items[i].spot != Spot.Pan || !RectTransformUtility.RectangleContainsScreenPoint(itemRects[i], pos, null)) continue;
                    if (cook.lidOn) Say("뚜껑부터 열어!");
                    else cook.TakeOut(i);
                    return false; // 집게는 계속 들고 다음 재료를 건진다
                }
                return OverRest(pos);
            };
            // 뚜껑은 재료보다 위에 그린다 (덮여 있으면 유리 너머로 보인다). 손에 들면 열린 것, 냄비 위에서 클릭하면 덮는다
            lidRT = UIKit.ToolImage(root, "Lid", UIKit.GlassLid(), 0, 0, 0, 0);
            lidHold = lidRT.gameObject.AddComponent<ToolHold>();
            lidHold.onPickUp = () => { if (cook != null) cook.SetLid(false); };
            lidHold.onUse = pos => { if (cook != null) { cook.SetLid(OverPan(pos)); PlaceLid(); } return true; };
            lidHold.onPutDown = PlaceLid;
            PlaceLid();
        }
        stateLabel = UIKit.Label(root, "", 0.64f, 0.27f, 0.97f, 0.34f, 30, TextAnchor.MiddleLeft, UIKit.Bad);

        UIKit.Btn(root, (md.id == Method.Boil ? "냄비" : "팬") + "의 재료 모두 꺼내기 [Space]", 0.03f, 0.245f, 0.60f, 0.295f, () => { if (cook != null) cook.TakeOutAll(); }, UIKit.Bad, 26);
        scr = Scr.Cook;
    }

    // 키보드(Z X C V B N)로 재료 넣기/꺼내기. 마우스는 끌어다 놓기(재료)와 집게(끓이기)를 쓴다.
    void ItemKey(int i)
    {
        if (cook == null) return;
        var it = cook.items[i];
        if (it.spot == Spot.Tray) cook.Put(i);
        else if (it.spot == Spot.Pan) cook.TakeOut(i);
    }

    // 조리 도구 자리 (화면 비율). 팬/냄비 그림 안의 둥근 바닥 중심은 UIKit.PanCX/PanCY 기준
    const float PanX0 = 0.03f, PanY0 = 0.30f, PanX1 = 0.60f, PanY1 = 0.77f;
    const float TrayX0 = 0.03f, TrayX1 = 0.45f;

    const float RefW = 1920f, RefH = 1080f; // 기준 해상도. 픽셀로 계산한 뒤 화면 비율로 바꾼다

    Vector2 PanCenter()
    {
        return new Vector2((PanX0 + (PanX1 - PanX0) * UIKit.PanCX) * RefW, (PanY0 + (PanY1 - PanY0) * UIKit.PanCY) * RefH);
    }
    float PanFloorRadius() { return UIKit.PanR * (PanY1 - PanY0) * RefH; }

    static void SetSquare(RectTransform rt, Vector2 c, float s)
    {
        rt.anchorMin = new Vector2((c.x - s / 2f) / RefW, (c.y - s / 2f) / RefH);
        rt.anchorMax = new Vector2((c.x + s / 2f) / RefW, (c.y + s / 2f) / RefH);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    bool OverPan(Vector2 screenPos)
    {
        return panZone != null && RectTransformUtility.RectangleContainsScreenPoint(panZone, screenPos, null);
    }

    // 뚜껑: 덮여 있으면 냄비 위에 딱 맞게, 열려 있으면 조리대 오른쪽에 작게
    void PlaceLid()
    {
        if (lidRT == null || cook == null) return;
        if (cook.lidOn) SetSquare(lidRT, PanCenter(), PanFloorRadius() * 2f * 150f / 140f);
        else SetSquare(lidRT, new Vector2(0.895f * RefW, 0.43f * RefH), 180f);
        lidRT.SetAsLastSibling();
    }

    bool OverRest(Vector2 screenPos)
    {
        return restRT != null && RectTransformUtility.RectangleContainsScreenPoint(restRT, screenPos, null);
    }

    void Say(string msg) { sayText = msg; sayTimer = 1.5f; }

    // 재료 아이템을 지금 자리(도마/팬/접시)의 칸으로 옮긴다. 재료 수에 따라 크기를 줄여 정사각형으로 놓는다.
    void PlaceItem(int i)
    {
        if (i < itemDrags.Count && itemDrags[i].dragging) return; // 끄는 중에는 마우스를 따라가게 둔다
        var it = cook.items[i];
        int n = cook.items.Length;
        const float W = RefW, H = RefH;
        float cx, cy, s;
        if (it.spot == Spot.Pan)
        {
            // 팬 바닥 원 위에 둥글게 둘러 놓는다
            var pc = PanCenter();
            s = n <= 1 ? 190f : n == 2 ? 160f : n <= 4 ? 130f : n <= 6 ? 110f : 90f;
            float ring = n <= 1 ? 0f : PanFloorRadius() * 0.5f;
            float a = Mathf.PI / 2f - Mathf.PI * 2f * i / n;
            cx = pc.x + Mathf.Cos(a) * ring; cy = pc.y + Mathf.Sin(a) * ring;
        }
        else if (it.spot == Spot.Tray)
        {
            // 도마 몸통(손잡이 제외) 위에 한 줄로
            float bx0 = TrayX0 * W, bw = (TrayX1 - TrayX0) * W * 0.85f;
            s = Mathf.Min(150f, bw / n * 0.85f);
            cx = bx0 + bw * (i + 0.5f) / n + 10f; cy = 0.145f * H;
        }
        else
        {
            float px0 = 0.66f * W, pw = 0.29f * W;
            s = Mathf.Min(120f, pw / n * 0.85f);
            cx = px0 + pw * (i + 0.5f) / n; cy = 0.145f * H;
        }
        var rt = itemRects[i];
        rt.anchorMin = new Vector2((cx - s / 2f) / W, (cy - s / 2f) / H);
        rt.anchorMax = new Vector2((cx + s / 2f) / W, (cy + s / 2f) / H);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        string key = i < 6 ? "ZXCVBN".Substring(i, 1) : "-";
        itemLabels[i].text = it.spot == Spot.Tray ? it.ing.name + "\n[" + key + "] 넣기"
            : it.spot == Spot.Pan ? it.ing.name + "\n[" + key + "] " + (cook.m.id == Method.Boil ? "건지기" : "꺼내기")
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
            stirTimer -= Time.deltaTime; // 뒤집개로 휘젓는 동안 ToolHold가 다시 채운다
            sayTimer -= Time.deltaTime;

            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ||
                    kb.aKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) stirTimer = 0.25f;
                if ((kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) && cook.m.id == Method.Boil)
                {
                    bool on = !cook.lidOn;
                    if (lidHold != null && lidHold.IsHeld) lidHold.PutDown();
                    cook.SetLid(on);
                    PlaceLid();
                }
                if (kb.fKey.wasPressedThisFrame && cook.flipAvailable) { cook.Flip(); Say("뒤집었다!"); }
                int n = DigitPressed(kb);
                if (n >= 1 && n <= 3) cook.SetFire(n - 1);
                // 재료가 몇 개든 Z X C V B N 순서로 넣고 꺼낸다
                var itemKeys = new[] { kb.zKey, kb.xKey, kb.cKey, kb.vKey, kb.bKey, kb.nKey };
                for (int k = 0; k < itemKeys.Length && k < cook.items.Length; k++)
                    if (itemKeys[k].wasPressedThisFrame) ItemKey(k);
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) cook.TakeOutAll();
            }

            cook.Tick(Time.deltaTime, stirTimer > 0f);
            RefreshCookUI();
        }
        if (cook.finished) FinishCook();
    }

    void RefreshCookUI()
    {
        for (int i = 0; i < itemImgs.Count; i++)
        {
            var it = cook.items[i];
            if (swArt[i])
            {
                itemImgs[i].sprite = Art.Get(it.ing, cook.Stage(it));
                itemImgs[i].color = Color.white;
            }
            else itemImgs[i].color = cook.Tone(it);
            PlaceItem(i);
        }
        // 불꽃: 불 세기만큼 팬 밖으로 크게 삐져나오고 살랑거린다. 밸브 손잡이는 지금 불 세기 방향을 가리킨다
        float flame = (1.0f + 0.07f * (cook.fire + 1)) * (1f + Mathf.Sin(Time.time * 18f) * 0.01f);
        SetSquare(flameRT, PanCenter(), PanFloorRadius() * 2f * 150f / 140f * flame);
        knobRT.localRotation = Quaternion.Euler(0, 0, -(cook.fire - 1) * 60f);
        fireLabel.text = Balance.FireName[cook.fire];
        timeLabel.text = "남은 시간 " + Mathf.Max(0, Mathf.CeilToInt(Balance.MaxCookTime - cook.t)) + "초";
        // 넘치기 직전이면 뚜껑이 들썩이고 붉어진다
        if (lidRT != null && !lidHold.IsHeld)
        {
            lidRT.localRotation = Quaternion.Euler(0, 0, cook.bubble ? Mathf.Sin(Time.time * 30f) * 4f : 0f);
            lidRT.GetComponent<Image>().color = cook.bubble ? new Color(1f, 0.75f, 0.7f) : Color.white;
        }
        // 뒤집을 때가 오면 뒤집개가 콩콩 뛴다
        if (spatulaRT != null && cook.m.id == Method.Grill)
            spatulaRT.localScale = Vector3.one * (cook.flipAvailable ? 1f + Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 0.12f : 1f);
        stateLabel.text = cook.bubble ? "넘치겠다! 뚜껑을 집어서 열어! [S]"
            : cook.flipAvailable ? "지금 뒤집어! 뒤집개로 팬을 클릭! [F]"
            : sayTimer > 0f ? sayText
            : cook.stirring ? "젓는 중..." : "";
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
        // 요리 그림이 있으면 왼쪽에 그림, 오른쪽에 글을 놓는다 (비상 라면은 재료가 없어 그림 없음)
        bool photo = dish.ings.Length > 0 && dish.stages != null;
        if (photo) Photo(root, 0.04f, 0.23f, 0.40f, 0.87f, dish.ings, dish.stages, dish.method);
        float x0 = photo ? 0.42f : 0.05f, x1 = photo ? 0.97f : 0.95f;
        UIKit.Label(root, dish.name, x0, 0.62f, x1, 0.80f, 100);
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
        Photo(root, 0.04f, 0.23f, 0.40f, 0.87f, used, stages, md.id);
        UIKit.Label(root, how + md.verb + " " + string.Join(" ", used.Select(i => i.name)), 0.42f, 0.62f, 0.97f, 0.80f, 90, TextAnchor.MiddleCenter, UIKit.Bad);
        UIKit.Label(root, msg, 0.42f, 0.34f, 0.97f, 0.60f, 52);
        UIKit.Btn(root, btn + " [Enter]", 0.30f, 0.07f, 0.70f, 0.21f, next, UIKit.Good, 48);
        confirm = next;
    }

    // 요리 그림 칸. 먼저 대체 그림을 보여 주고, AI 그림이 도착하면 바꿔 끼운다.
    void Photo(Transform parent, float x0, float y0, float x1, float y1, Ingredient[] ings, Doneness[] stages, Method m)
    {
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
        if (modal != null) Destroy(modal);
        modal = null;
        modalPick = null;
    }
}
