using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
}

public class Run
{
    public int day = 1;
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
    readonly List<Image> swatches = new List<Image>();
    readonly List<Image> fireBtns = new List<Image>();
    Button flipBtn;
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

        UIKit.Box(canvasRT, "Background", 0, 0, 1, 1, UIKit.Bg);
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
        lidRect = null;
        swatches.Clear();
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
        UIKit.Label(root, "COOKER", 0.1f, 0.58f, 0.9f, 0.88f, 150, TextAnchor.MiddleCenter, UIKit.Accent);
        UIKit.Label(root, "일주일치 장바구니로 7일을 버텨라", 0.1f, 0.46f, 0.9f, 0.58f, 54);
        UIKit.Btn(root, "시작", 0.35f, 0.26f, 0.65f, 0.40f, StartRun);
        UIKit.Btn(root, "도감", 0.35f, 0.10f, 0.65f, 0.22f, ShowDex, UIKit.Muted, 40);
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
        int spent = cart.Sum(kv => kv.Key.price * kv.Value);
        int units = cart.Values.Sum();
        int left = run.budget - spent;

        UIKit.Label(root, "장보기", 0.03f, 0.88f, 0.4f, 0.98f, 70, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Label(root, "남은 예산 " + left.ToString("N0") + "원", 0.45f, 0.88f, 0.97f, 0.98f, 56, TextAnchor.MiddleRight);
        UIKit.Label(root, "6끼분을 사자. 첫날 한 끼는 엄마가 달걀을 보내준다. 장보기는 이번 한 번뿐!",
            0.03f, 0.80f, 0.97f, 0.88f, 34, TextAnchor.MiddleLeft, UIKit.Muted);

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
            UIKit.Label(root, ing.name, 0.05f, bot, 0.25f, top, 44, TextAnchor.MiddleLeft);
            UIKit.Label(root, ing.price.ToString("N0") + "원", 0.27f, bot, 0.42f, top, 40);
            UIKit.Label(root, ing.shelf + "일", 0.43f, bot, 0.60f, top, 40);
            UIKit.Btn(root, "-", 0.64f, bot + 0.01f, 0.70f, top - 0.01f, () => ChangeCart(ing, -1), UIKit.Muted, 50, qty > 0);
            UIKit.Label(root, qty.ToString(), 0.70f, bot, 0.80f, top, 44);
            UIKit.Btn(root, "+", 0.80f, bot + 0.01f, 0.86f, top - 0.01f, () => ChangeCart(ing, 1), UIKit.Accent, 50, ing.price <= left);
        }

        UIKit.Label(root, "담은 재료 " + units + "개", 0.03f, 0.05f, 0.40f, 0.17f, 44, TextAnchor.MiddleLeft);
        bool ok = units >= Balance.MinUnits;
        UIKit.Btn(root, ok ? "구매 확정" : "최소 " + Balance.MinUnits + "개 필요", 0.55f, 0.05f, 0.97f, 0.19f, ConfirmShop, UIKit.Good, 48, ok);
    }

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
        run.budget -= cart.Sum(kv => kv.Key.price * kv.Value);
        run.inv = new Dictionary<Ingredient, int>(cart);
        run.bought = cart.Values.Sum();
        ShowDay();
    }

    // ---------------------------------------------------------------- 하루 메인
    int DaysLeft(Ingredient i) { return i.shelf - run.day + 1; } // 오늘 포함 며칠 더 쓸 수 있는지

    void ShowDay()
    {
        // 유통기한 지난 재료는 폐기
        foreach (var i in run.inv.Keys.ToList())
            if (DaysLeft(i) <= 0) run.inv.Remove(i);
        sel.RemoveAll(i => !run.inv.ContainsKey(i));

        guideMeal = run.day == 1;
        if (!guideMeal && run.inv.Count == 0)
        {
            ShowFail("먹을 재료가 하나도 없다. 오늘은 굶었다...");
            return;
        }
        if (guideMeal)
        {
            sel.Clear();
            sel.Add(Data.Egg);
            selMethod = Method.Grill;
        }

        Clear();
        UIKit.Label(root, run.day + "일차 / " + Balance.Days + "일", 0.03f, 0.88f, 0.5f, 0.98f, 70, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Label(root, "먹은 끼니 " + run.dishes.Count + "개", 0.55f, 0.88f, 0.97f, 0.98f, 46, TextAnchor.MiddleRight);

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
            foreach (var kv in run.inv.OrderBy(k => k.Key.shelf))
            {
                var ing = kv.Key;
                float top = 0.79f - r * 0.092f;
                float bot = top - 0.082f;
                int dl = DaysLeft(ing);
                string tag = dl == 1 ? "오늘까지!" : "D-" + (dl - 1);
                bool on = sel.Contains(ing);
                UIKit.Btn(root, ing.name + " x" + kv.Value + "   (" + tag + ")", 0.06f, bot, 0.47f, top,
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
            UIKit.Btn(root, md.name, 0.55f, bot, 0.94f, top, () => { selMethod = md.id; ShowDay(); },
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
        UIKit.Btn(root, "도감", 0.57f, 0.07f, 0.70f, 0.20f, ShowDex, UIKit.Muted, 36);
        bool canCook = sel.Count > 0 && selMethod.HasValue;
        UIKit.Btn(root, "조리 시작", 0.72f, 0.05f, 0.97f, 0.22f, StartCook, UIKit.Good, 48, canCook);
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
        cook = new Cook(chosen, md);
        stirTimer = 0f;

        UIKit.Label(root, md.name + " 중", 0.03f, 0.88f, 0.5f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        timeLabel = UIKit.Label(root, "", 0.6f, 0.88f, 0.97f, 0.98f, 40, TextAnchor.MiddleRight, UIKit.Muted);
        UIKit.Label(root, guideMeal ? "[첫날 가이드] 색이 알맞게 변했을 때 [꺼내기]! 너무 오래 두면 탄다. " + md.guide : md.guide,
            0.03f, 0.78f, 0.97f, 0.88f, 32, TextAnchor.MiddleLeft, UIKit.Muted);

        // 재료 (색 변화가 유일한 단서)
        UIKit.Box(root, "Pan", 0.03f, 0.30f, 0.60f, 0.77f, new Color(0.25f, 0.2f, 0.18f));
        int n = chosen.Length;
        for (int i = 0; i < n; i++)
        {
            float w = 0.57f / n;
            float x0 = 0.03f + i * w + w * 0.15f;
            float x1 = 0.03f + (i + 1) * w - w * 0.15f;
            var sw = UIKit.Box(root, "Swatch", x0, 0.38f, x1, 0.70f, chosen[i].raw);
            swatches.Add(sw.GetComponent<Image>());
            UIKit.Label(root, chosen[i].name, x0, 0.31f, x1, 0.38f, 36, TextAnchor.MiddleCenter, Color.white);
        }

        // 불 조절
        UIKit.Label(root, "불 조절", 0.64f, 0.70f, 0.97f, 0.77f, 34, TextAnchor.MiddleLeft, UIKit.Muted);
        for (int f = 0; f < 3; f++)
        {
            int level = f;
            float x0 = 0.64f + f * 0.11f;
            var b = UIKit.Btn(root, Balance.FireName[f], x0, 0.57f, x0 + 0.10f, 0.69f, () => cook.SetFire(level), UIKit.Muted, 32);
            fireBtns.Add(b.GetComponent<Image>());
        }

        // 방식별 고유 조작
        if (md.id == Method.Grill)
        {
            flipBtn = UIKit.Btn(root, "뒤집기!", 0.64f, 0.34f, 0.97f, 0.53f, () => cook.Flip(), UIKit.Good, 52);
            flipBtn.gameObject.SetActive(false);
        }
        else if (md.id == Method.Stirfry)
        {
            UIKit.Box(root, "StirArea", 0.64f, 0.34f, 0.97f, 0.53f, UIKit.Card);
            UIKit.Label(root, "마우스를 누른 채\n좌우로 흔들기", 0.65f, 0.35f, 0.96f, 0.52f, 36);
        }
        else
        {
            lidRect = UIKit.Box(root, "Lid", 0.64f, 0.34f, 0.97f, 0.53f, UIKit.Card);
            lidImage = lidRect.GetComponent<Image>();
            UIKit.Label(lidRect, "뚜껑 (꾹 누르기)", 0.05f, 0.05f, 0.95f, 0.95f, 38);
        }
        stateLabel = UIKit.Label(root, "", 0.64f, 0.27f, 0.97f, 0.34f, 30, TextAnchor.MiddleLeft, UIKit.Bad);

        UIKit.Btn(root, "꺼내기", 0.30f, 0.05f, 0.70f, 0.22f, () => cook.TakeOut(), UIKit.Bad, 60);
    }

    void Update()
    {
        if (cook == null) return;
        if (!cook.finished) // [꺼내기] 버튼은 finished만 켜므로, 아래 FinishCook은 항상 한 번 실행된다
        {
            var mouse = Mouse.current;
            bool held = mouse != null && mouse.leftButton.isPressed;
            if (held && Mathf.Abs(mouse.delta.ReadValue().x) > 2f) stirTimer = 0.25f;
            else stirTimer -= Time.deltaTime;
            bool lid = held && lidRect != null &&
                       RectTransformUtility.RectangleContainsScreenPoint(lidRect, mouse.position.ReadValue(), null);

            cook.Tick(Time.deltaTime, stirTimer > 0f, lid);
            RefreshCookUI();
        }
        if (cook.finished) FinishCook();
    }

    void RefreshCookUI()
    {
        for (int i = 0; i < swatches.Count; i++) swatches[i].color = cook.Tone(cook.ings[i]);
        for (int f = 0; f < fireBtns.Count; f++) fireBtns[f].color = f == cook.fire ? UIKit.Accent : UIKit.Muted;
        if (flipBtn != null) flipBtn.gameObject.SetActive(cook.flipAvailable);
        timeLabel.text = "남은 시간 " + Mathf.Max(0, Mathf.CeilToInt(Balance.MaxCookTime - cook.t)) + "초";
        if (lidImage != null) lidImage.color = cook.bubble ? new Color(1f, 0.6f, 0.4f) : UIKit.Card;
        stateLabel.text = cook.bubble ? "거품이 넘친다! 뚜껑을 눌러!" : (cook.stirring ? "젓는 중..." : "");
    }

    void FinishCook()
    {
        float coef; string fail;
        bool ok = cook.Evaluate(out coef, out fail);
        var ings = cook.ings;
        var md = cook.m;
        int sat = ok ? cook.Satisfaction(coef) : 0;
        cook = null;

        if (!ok)
        {
            if (guideMeal)
            {
                ShowMessage("엄마: " + fail + "이 됐네! 첫날은 봐줄게. 다시 해 봐.", "다시 하기", ShowDay);
                return;
            }
            Consume(ings);
            ShowFail("요리가 " + fail + "이 되어 못 먹었다. 굶었다...");
            return;
        }

        if (!guideMeal) Consume(ings);
        var names = string.Join(" ", ings.Select(i => i.name));
        var dish = new Dish
        {
            name = md.verb + " " + names,
            key = string.Join("+", ings.Select(i => i.name).OrderBy(s => s)) + "/" + md.id,
            ings = ings,
            method = md.id,
            satisfaction = sat,
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
            if (q <= 1) run.inv.Remove(i); else run.inv[i] = q - 1;
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
        UIKit.Label(root, run.day + "일차 식사", 0.03f, 0.88f, 0.5f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Label(root, dish.name, 0.05f, 0.62f, 0.95f, 0.80f, 100);
        if (isNew) UIKit.Label(root, "도감에 새로 등록!", 0.3f, 0.54f, 0.7f, 0.62f, 40, TextAnchor.MiddleCenter, UIKit.Good);
        UIKit.Label(root, "만족도 " + dish.satisfaction, 0.2f, 0.40f, 0.8f, 0.54f, 80, TextAnchor.MiddleCenter, UIKit.Accent);
        UIKit.Label(root, reaction, 0.1f, 0.28f, 0.9f, 0.40f, 52);
        bool last = run.day >= Balance.Days;
        UIKit.Btn(root, last ? "결산 보기" : "다음 날", 0.35f, 0.07f, 0.65f, 0.21f, NextDay, UIKit.Good, 52);
    }

    void NextDay()
    {
        run.day++;
        if (run.day > Balance.Days) ShowResult();
        else ShowDay();
    }

    // ---------------------------------------------------------------- 결산 / 실패
    void ShowResult()
    {
        Clear();
        float sat = (float)run.dishes.Average(d => (float)d.satisfaction);
        int combos = run.dishes.Select(d => d.key).Distinct().Count();
        float div = 100f * combos / Balance.Days;
        int leftover = run.inv.Values.Sum();
        float left = Mathf.Min(100f, 100f * leftover / Mathf.Max(1, run.bought));
        float total = sat * 0.5f + div * 0.3f + left * 0.2f;
        string grade = total >= Balance.GradeS ? "S" : total >= Balance.GradeA ? "A" : total >= Balance.GradeB ? "B" : "C";

        UIKit.Label(root, "7일 생존 성공!", 0.05f, 0.84f, 0.95f, 0.98f, 90, TextAnchor.MiddleCenter, UIKit.Good);
        UIKit.Label(root, grade, 0.05f, 0.62f, 0.35f, 0.84f, 180, TextAnchor.MiddleCenter, UIKit.Accent);
        UIKit.Label(root, "총점 " + Mathf.RoundToInt(total) + "점\n"
            + "만족도 " + Mathf.RoundToInt(sat) + " (50%)\n"
            + "다양성 " + Mathf.RoundToInt(div) + " (30%)  " + combos + "종류\n"
            + "남은 재료 " + Mathf.RoundToInt(left) + " (20%)  " + leftover + "개",
            0.40f, 0.52f, 0.97f, 0.84f, 44, TextAnchor.MiddleLeft);
        UIKit.Label(root, DishList(), 0.05f, 0.24f, 0.95f, 0.50f, 34, TextAnchor.UpperLeft);
        UIKit.Btn(root, "도감", 0.2f, 0.05f, 0.45f, 0.19f, ShowDex, UIKit.Muted, 44);
        UIKit.Btn(root, "다시 하기", 0.55f, 0.05f, 0.8f, 0.19f, ShowTitle, UIKit.Good, 48);
    }

    void ShowFail(string reason)
    {
        Clear();
        UIKit.Label(root, "굶었다...", 0.05f, 0.76f, 0.95f, 0.96f, 120, TextAnchor.MiddleCenter, UIKit.Bad);
        UIKit.Label(root, run.day + "일차에 실패", 0.05f, 0.66f, 0.95f, 0.76f, 54);
        UIKit.Label(root, reason, 0.05f, 0.54f, 0.95f, 0.66f, 40, TextAnchor.MiddleCenter, UIKit.Muted);
        UIKit.Label(root, run.dishes.Count == 0 ? "먹은 요리가 없다." : "그때까지 먹은 요리\n" + DishList(),
            0.05f, 0.24f, 0.95f, 0.52f, 34, TextAnchor.UpperLeft);
        UIKit.Btn(root, "다시 하기", 0.35f, 0.05f, 0.65f, 0.19f, ShowTitle, UIKit.Good, 52);
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
        UIKit.Btn(root, btn, 0.35f, 0.2f, 0.65f, 0.34f, onClick, UIKit.Good, 52);
    }

    void ShowDex()
    {
        CloseModal();
        modal = UIKit.Box(canvasRT, "Dex", 0.08f, 0.08f, 0.92f, 0.92f, UIKit.Card).gameObject;
        var m = modal.transform;
        UIKit.Label(m, "도감 (" + dex.Count + "종류)", 0.05f, 0.86f, 0.7f, 0.98f, 64, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Btn(m, "닫기", 0.78f, 0.88f, 0.95f, 0.98f, CloseModal, UIKit.Muted, 40);
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
