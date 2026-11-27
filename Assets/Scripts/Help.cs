using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// F1 도움말 화면 (docs/19 7번). 재료, 방식별 조작, 단축키 표, 게임 규칙을 네 탭으로 보여 준다.
// 다른 파일을 고치지 않고 혼자 돌도록 만들었다:
//  - 스스로 캔버스를 만들고 F1(또는 오른쪽 위 [?] 버튼)로 연다. 열려 있는 동안 Time.timeScale을 0으로 해서 조리가 멈춘다.
//  - 열려 있는 동안 키보드 입력이 게임으로 새지 않도록 키보드 장치를 잠시 끈다(Enter·Space가 뒤쪽 화면을 넘기지 않게).
//    그동안 닫기 키(F1·Esc)와 탭 이동 키(←·→)는 Windows API로 직접 읽는다. Windows가 아니면 장치를 끄지 않는다.
// 내용은 Data·Balance에서 읽어 오므로 수치가 바뀌어도 도움말이 따라간다. 잠긴 재료·방식(새우, 튀기기)은 해금 전에 보여 주지 않는다.
public class Help : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    static Help inst;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
#endif
    const int VkF1 = 0x70, VkEsc = 0x1B, VkLeft = 0x25, VkRight = 0x27;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("COOKER_Help");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<Help>();
    }

    static readonly string[] TabNames = { "조작 키", "조리 방식", "재료", "게임 규칙" };

    GameObject canvasGo, panel, openBtn;
    RectTransform openRt;
    int curCand;
    float nextPlaceCheck;
    Transform body;
    readonly List<Image> tabImgs = new List<Image>();
    int tab;
    float prevScale = 1f;
    InputDevice disabledKb;
    bool pendingEnable;
    float pendingSince;
    bool wasF1, wasEsc, wasLeft, wasRight;

    void Awake() { BuildCanvas(); }

    void BuildCanvas()
    {
        canvasGo = new GameObject("HelpCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000; // 게임의 모든 화면과 팝업 위
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // 마우스로도 열 수 있는 작은 [?] 버튼. 자리는 화면마다 달라서 글자·버튼과 겹치지 않는 곳으로 알아서 옮긴다 (PlaceOpenButton).
        var b = UIKit.ImageBtn(canvasGo.transform, "icon_help", Cand[0][0], Cand[0][1], Cand[0][2], Cand[0][3], Open);
        openBtn = b.gameObject;
        openRt = (RectTransform)openBtn.transform;
        IconRect = openRt; // 아이콘 자리는 CornerDock이 정한다 (설정 톱니와 한 쌍으로 어느 화면에서든 아무것도 가리지 않게)
    }

    public static RectTransform IconRect { get; private set; }

    bool Down(Key key, int vk)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return Application.isFocused && (GetAsyncKeyState(vk) & 0x8000) != 0;
#else
        var kb = Keyboard.current;
        return kb != null && kb[key].isPressed;
#endif
    }

    void Update()
    {
        if (!IsOpen)
        {
            if (pendingEnable) TryEnableKeyboard();
            if (!CornerDock.Active && Time.unscaledTime >= nextPlaceCheck) { nextPlaceCheck = Time.unscaledTime + 0.5f; PlaceOpenButton(); } // CornerDock이 있으면 그쪽이 자리를 정한다
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) Open();
            return;
        }

        bool f1 = Down(UnityEngine.InputSystem.Key.F1, VkF1), esc = Down(UnityEngine.InputSystem.Key.Escape, VkEsc);
        bool left = Down(UnityEngine.InputSystem.Key.LeftArrow, VkLeft), right = Down(UnityEngine.InputSystem.Key.RightArrow, VkRight);
        if ((f1 && !wasF1) || (esc && !wasEsc)) Close();
        else if (left && !wasLeft) ShowTab((tab + TabNames.Length - 1) % TabNames.Length);
        else if (right && !wasRight) ShowTab((tab + 1) % TabNames.Length);
        wasF1 = f1; wasEsc = esc; wasLeft = left; wasRight = right;
    }

    void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        prevScale = Time.timeScale;
        Time.timeScale = 0f; // 조리 중이어도 시간이 흐르지 않는다
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem)).AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

        // 열자마자 F1이 아직 눌려 있어도 바로 닫히지 않도록, 눌린 상태에서 시작한다
        wasF1 = true; wasEsc = true; wasLeft = true; wasRight = true;
        DisableKeyboard();
        openBtn.SetActive(false);
        BuildPanel();
        ShowTab(tab);
    }

    void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Time.timeScale = prevScale <= 0f ? 1f : prevScale;
        if (panel != null) Destroy(panel);
        panel = null;
        openBtn.SetActive(true);
        pendingEnable = true; // 닫기 키를 뗀 뒤에 키보드를 다시 켠다 (눌린 채로 켜면 게임이 Esc를 따로 받는다)
        pendingSince = Time.unscaledTime;
    }

    void DisableKeyboard()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        var kb = Keyboard.current;
        if (kb != null && kb.enabled)
        {
            InputSystem.DisableDevice(kb);
            disabledKb = kb;
        }
#endif
    }

    void TryEnableKeyboard()
    {
        bool held = Down(UnityEngine.InputSystem.Key.F1, VkF1) || Down(UnityEngine.InputSystem.Key.Escape, VkEsc)
            || Down(UnityEngine.InputSystem.Key.LeftArrow, VkLeft) || Down(UnityEngine.InputSystem.Key.RightArrow, VkRight);
        if (held && Time.unscaledTime - pendingSince < 1.5f) return;
        EnableKeyboard();
    }

    void EnableKeyboard()
    {
        pendingEnable = false;
        if (disabledKb != null)
        {
            InputSystem.EnableDevice(disabledKb);
            disabledKb = null;
        }
    }

    // 도움말이 열린 채 꺼지거나 장면이 바뀌어도 키보드와 시간이 멈춘 채 남지 않게 한다.
    void OnDisable()
    {
        if (IsOpen) { IsOpen = false; Time.timeScale = prevScale <= 0f ? 1f : prevScale; }
        EnableKeyboard();
    }

    // ---------------------------------------------------------------- [?] 버튼 자리
    // 화면마다 오른쪽 위에 로비 버튼·금액이, 왼쪽 위에 주차 카드가 있어서 한 자리로 정할 수 없다. 후보 자리를 순서대로 보고
    // 그 자리에 있는 글자·버튼과 겹치지 않는 첫 곳에 둔다 (0.5초마다 확인). 모두 겹치면 가장 덜 겹치는 곳.
    static readonly float[][] Cand =
    {
        new[] { 0.3542f, 0.9074f, 0.3906f, 0.9722f }, // 로비: 위쪽 줄 (HUD와 같은 줄, x 680~750, y 30~100 픽셀). 다른 화면에서 겹치면 아래 후보로 옮겨 간다
        new[] { 0.002f, 0.860f, 0.027f, 0.920f }, // 왼쪽 가장자리, 조금 아래
        new[] { 0.002f, 0.800f, 0.027f, 0.855f },
        new[] { 0.47f, 0.0f, 0.53f, 0.035f },     // 아래 가운데 (화면 맨 아래 띠)
        new[] { 0.935f, 0.925f, 0.99f, 0.975f },  // 오른쪽 위 모서리
        new[] { 0.93f, 0.872f, 0.99f, 0.91f },    // 오른쪽 위, 로비 버튼 아래
        new[] { 0.47f, 0.935f, 0.53f, 0.985f },   // 위 가운데
        new[] { 0.945f, 0.50f, 0.995f, 0.54f },   // 오른쪽 가운데
    };

    static Rect ScreenRect(RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // 글자는 칸 전체가 아니라 실제로 글자가 차지하는 만큼만 장애물로 친다 (정렬 방향 기준)
    static Rect TextRect(Text t)
    {
        var r = ScreenRect(t.rectTransform);
        var canvas = t.canvas != null ? t.canvas.rootCanvas : null;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float w = Mathf.Min(r.width, t.preferredWidth * scale), h = Mathf.Min(r.height, t.preferredHeight * scale);
        float x = r.xMin, y = r.yMin;
        switch (t.alignment)
        {
            case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: x = r.center.x - w / 2f; break;
            case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: x = r.xMax - w; break;
        }
        switch (t.alignment)
        {
            case TextAnchor.UpperLeft: case TextAnchor.UpperCenter: case TextAnchor.UpperRight: y = r.yMax - h; break;
            case TextAnchor.MiddleLeft: case TextAnchor.MiddleCenter: case TextAnchor.MiddleRight: y = r.center.y - h / 2f; break;
        }
        return new Rect(x, y, w, h);
    }

    List<Rect> Obstacles()
    {
        var list = new List<Rect>();
        foreach (var t in FindObjectsByType<Text>(FindObjectsSortMode.None))
        {
            if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text) || t.color.a < 0.1f || t.transform.IsChildOf(canvasGo.transform)) continue;
            list.Add(TextRect(t));
        }
        foreach (var s in FindObjectsByType<Selectable>(FindObjectsSortMode.None))
        {
            var rt = s.transform as RectTransform;
            if (rt == null || !s.isActiveAndEnabled || s.transform.IsChildOf(canvasGo.transform)) continue;
            list.Add(ScreenRect(rt));
        }
        return list;
    }

    Rect CandRect(int i)
    {
        var c = Cand[i];
        return Rect.MinMaxRect(c[0] * Screen.width - 6f, c[1] * Screen.height - 6f, c[2] * Screen.width + 6f, c[3] * Screen.height + 6f);
    }

    static float OverlapArea(Rect a, Rect b)
    {
        float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return w > 0f && h > 0f ? w * h : 0f;
    }

    void PlaceOpenButton()
    {
        if (openRt == null || !openBtn.activeSelf) return;
        var obs = Obstacles();
        float Cost(int i) { var r = CandRect(i); float s = 0f; foreach (var o in obs) s += OverlapArea(r, o); return s; }
        if (Cost(curCand) <= 0f) return; // 지금 자리가 비어 있으면 그대로 둔다 (자꾸 옮겨 다니지 않게)
        int best = curCand;
        float bestCost = float.MaxValue;
        for (int i = 0; i < Cand.Length; i++)
        {
            float c = Cost(i);
            if (c < bestCost) { bestCost = c; best = i; }
            if (c <= 0f) break; // 앞쪽 후보일수록 선호
        }
        if (best == curCand) return;
        curCand = best;
        var cand = Cand[best];
        openRt.anchorMin = new Vector2(cand[0], cand[1]);
        openRt.anchorMax = new Vector2(cand[2], cand[3]);
        openRt.offsetMin = Vector2.zero;
        openRt.offsetMax = Vector2.zero;
    }

    // ---------------------------------------------------------------- 화면
    void BuildPanel()
    {
        panel = UIKit.Box(canvasGo.transform, "HelpPanel", 0, 0, 1, 1, new Color(0f, 0f, 0f, 0.6f), false).gameObject; // 뒤쪽 화면을 가리고 클릭을 막는다
        var card = UIKit.Box(panel.transform, "Card", 0.08f, 0.10f, 0.92f, 0.90f, UIKit.Card, true);
        UIKit.Label(card, "도움말", 0.04f, 0.89f, 0.4f, 0.98f, 60, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Btn(card, "닫기 [F1·Esc]", 0.74f, 0.89f, 0.96f, 0.98f, Close, UIKit.Muted, 32, true, true);

        tabImgs.Clear();
        for (int i = 0; i < TabNames.Length; i++)
        {
            int idx = i;
            float x0 = 0.04f + i * 0.2275f;
            var b = UIKit.Btn(card, TabNames[i], x0, 0.80f, x0 + 0.215f, 0.875f, () => ShowTab(idx), UIKit.Muted, 34, true, true);
            tabImgs.Add(b.GetComponent<Image>());
        }
        body = UIKit.Box(card, "Body", 0.04f, 0.07f, 0.96f, 0.78f);
        UIKit.Label(card, "← → 로 탭을 넘길 수 있어요", 0.04f, 0.0f, 0.96f, 0.065f, 24, TextAnchor.MiddleCenter, UIKit.Muted);
    }

    void ShowTab(int i)
    {
        tab = i;
        if (panel == null || body == null) return;
        for (int k = body.childCount - 1; k >= 0; k--)
        {
            var c = body.GetChild(k);
            c.SetParent(null);
            Destroy(c.gameObject);
        }
        for (int k = 0; k < tabImgs.Count; k++) tabImgs[k].color = k == i ? UIKit.Accent : UIKit.Muted;
        switch (i)
        {
            case 0: KeysTab(); break;
            case 1: MethodsTab(); break;
            case 2: IngredientsTab(); break;
            default: RulesTab(); break;
        }
    }

    // 왼쪽 제목 + 오른쪽 설명 한 줄(여러 줄 가능)
    void Row(float top, float h, string head, string text, int size = 28)
    {
        UIKit.Label(body, head, 0.0f, top - h, 0.16f, top, 34, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Label(body, text, 0.17f, top - h, 1.0f, top, size, TextAnchor.MiddleLeft);
    }

    void KeysTab()
    {
        var active = Data.Active;
        var methods = Data.ActiveMethods;
        string pick = string.Join(" / ", methods.Select(m => m.name).ToArray());
        string qwer = string.Join(" / ", "QWER".Take(methods.Length).Select(c => c.ToString()).ToArray());
        const float h = 0.155f;
        Row(1.0f, h, "공통", "Enter / Space = 화면의 기본 버튼     F1 = 이 도움말     Esc = 팝업 닫기");
        Row(1.0f - h, h, "로비", "Enter = 게임 시작     D = 도감     T = 칭호");
        Row(1.0f - 2 * h, h, "장보기", "숫자 1~" + active.Length + " = 재료 담기     Shift + 숫자 = 빼기     Enter = 구매 확정");
        Row(1.0f - 3 * h, h, "하루 선택", "재료를 끌어서 철판에 올리거나 숫자키로 고르기 (" + IngredientLimitText() + ")\n" + qwer + " = " + pick + "     M = 엄마의 전언     D = 도감     Enter = 조리 시작");
        Row(1.0f - 4 * h, h * 1.3f, "조리", "1 / 2 / 3 = 약불 / 중불 / 강불 (밸브 손잡이를 돌려도 돼요). 왼쪽 바늘을 초록 구간에 맞추세요\n"
            + "Z X C V B N = 재료를 팬에 넣기 (오래 익는 재료부터)     Enter = 조리 완료     Q W E R = 간 고르기\n"
            + "Space = 방식별 동작 (굽기: 뒤집기 · 볶기: 연타해서 젓기 · 끓이기: 누르고 있으면 뚜껑)");
        Row(1.0f - 4 * h - h * 1.3f, h, "도감", "위쪽 버튼과 검색창으로 요리를 찾고, 카드를 누르면 자세히 볼 수 있어요     Esc = 닫기");
    }

    // 재료 한도 안내 ("최대 2개, 2주차부터 3개"). 값은 Balance.asset의 maxIngFromWeek/maxIngCount를 따른다
    static string IngredientLimitText()
    {
        const int MaxWeekToScan = 20;
        int first = Balance.MaxIngredients(1);
        string s = "최대 " + first + "개";
        int prev = first;
        for (int w = 2; w <= MaxWeekToScan; w++)
        {
            int n = Balance.MaxIngredients(w);
            if (n != prev) { s += ", " + w + "주차부터 " + n + "개"; prev = n; }
        }
        return s;
    }

    void MethodsTab()
    {
        var methods = Data.ActiveMethods;
        float h = Mathf.Min(0.23f, 0.88f / Mathf.Max(1, methods.Length));
        for (int i = 0; i < methods.Length; i++)
        {
            var m = methods[i];
            float top = 1.0f - i * h;
            UIKit.Label(body, m.name, 0.0f, top - h, 0.15f, top, 44, TextAnchor.MiddleLeft, UIKit.Accent);
            UIKit.Label(body, m.guide, 0.16f, top - h, 0.80f, top, 28, TextAnchor.MiddleLeft);
            UIKit.Label(body, "속도 " + m.speed.ToString("0.#") + "\n적정 구간 " + Mathf.RoundToInt(m.lo) + "~" + Mathf.RoundToInt(m.hi),
                0.81f, top - h, 1.0f, top, 24, TextAnchor.MiddleLeft, UIKit.Muted);
        }
        UIKit.Label(body, "전체 익힘 막대: 노랑(덜 익음) → 주황(알맞음) → 빨강(너무 익음). 주황일 때 조리 완료!",
            0.0f, 0.0f, 1.0f, 0.1f, 28, TextAnchor.MiddleLeft, UIKit.Good);
    }

    void IngredientsTab()
    {
        var list = Data.Active;
        const float h = 0.095f;
        string[] heads = { "재료", "가격 (1끼분)", "유통기한", "잘 어울리는 방식", "기본 점수" };
        float[] x0 = { 0.0f, 0.17f, 0.34f, 0.48f, 0.80f };
        float[] x1 = { 0.16f, 0.33f, 0.47f, 0.79f, 1.0f };
        for (int c = 0; c < heads.Length; c++)
            UIKit.Label(body, heads[c], x0[c], 1.0f - h, x1[c], 1.0f, 26, TextAnchor.MiddleLeft, UIKit.Muted);
        for (int i = 0; i < list.Length; i++)
        {
            var ing = list[i];
            float top = 1.0f - (i + 1) * h;
            string[] cells =
            {
                ing.name,
                ing.price.ToString("N0") + "원",
                ing.shelf + "일",
                string.Join(", ", ing.fits.Select(f => Data.GetMethod(f).name).ToArray()),
                ing.baseScore.ToString(),
            };
            for (int c = 0; c < cells.Length; c++)
                UIKit.Label(body, cells[c], x0[c], top - h, x1[c], top, c == 0 ? 34 : 30, TextAnchor.MiddleLeft, c == 0 ? UIKit.Accent : UIKit.Ink);
        }
        UIKit.Label(body, "가격은 매주 " + Mathf.RoundToInt((Balance.PriceGrowth - 1f) * 100f) + "% 오르고, 어울리지 않는 방식으로 만들면 점수가 "
            + Mathf.RoundToInt(Balance.NonFit * 100f) + "%로 줄어요. 달걀은 급하게 익고, 감자는 오래 걸려요.",
            0.0f, 0.0f, 1.0f, 0.12f, 26, TextAnchor.MiddleLeft, UIKit.Muted);
    }

    void RulesTab()
    {
        string text =
            "▶ 첫날 따라 하기\n"
            + "① 장보기: 숫자키로 재료를 담고 Enter로 사요. 첫날 달걀은 공짜예요.\n"
            + "② 하루 선택: 엄마가 보낸 달걀이 올라와 있어요. [조리 시작 Enter]를 눌러요.\n"
            + "③ 달걀을 눌러 팬에 넣고, [뒤집기]가 켜지면 Space. 불 바늘이 초록을 벗어나면 1·2·3키로 맞춰요.\n"
            + "④ 전체 익힘 막대가 주황이면 Enter로 조리 완료. 첫날은 실패해도 굶지 않고 다시 해요.\n\n"
            + "• 한 주는 " + Balance.Days + "일이에요. 주가 시작할 때 한 번 장을 보고, 그 재료로 " + Balance.Days + "일을 버텨요.\n"
            + "• 예산은 첫 주 " + Balance.Budget.ToString("N0") + "원이고, 다음 주부터는 지난주 만족도에 따라 "
            + Balance.BudgetMin.ToString("N0") + "~" + Balance.BudgetMax.ToString("N0") + "원이에요.\n"
            + "• 재료는 유통기한이 지나면 버려져요. 남은 재료는 다음 주로 이어져요.\n"
            + "• 재료와 방식을 골라 조리하고, 재료는 하나씩 팬에 넣어요. 오래 익는 재료를 먼저 넣어야 해요(틀리면 맛이 줄어요). 다 익으면 조리 완료로 한꺼번에 담고, 마지막에 간을 골라요.\n"
            + "• 한 요리에 넣을 수 있는 재료는 " + IngredientLimitText() + "예요.\n"
            + "• 간이 맞으면 점수가 오르고 틀리면 내려가요. 제한 시간을 넘기거나, 불 바늘이 초록 구간 밖에 " + Mathf.RoundToInt(Balance.GaugeDangerAfter) + "초 넘게 있어도 점수가 깎여요.\n"
            + "• 재료 2개를 모두 성공시키면 만족도가 " + Mathf.RoundToInt(Balance.MixBonus * 100f) + "% 올라가요.\n"
            + "• 요리를 못 만들어 굶으면 주마다 " + Balance.Lifelines + "번은 엄마가 비상 라면(만족도 " + Balance.RamenSat + ")으로 넘겨 줘요. 또 굶으면 끝이에요.\n"
            + "• 한 주를 마치면 결산 점수가 나와요: 만족도 50% + 요리 다양성 30% + 남은 재료 20%.\n"
            + "• 만든 요리는 도감에 기록돼요. 도감을 채우면 칭호를 얻고, 새로운 재료와 조리 방식이 열릴지도 몰라요.\n"
            + "• 판 도중에는 로비 버튼으로 돌아갈 수 있어요. 그러면 이번 주차는 취소돼요.";
        var l = UIKit.Label(body, text, 0.0f, 0.0f, 1.0f, 0.95f, 30, TextAnchor.UpperLeft);
        l.lineSpacing = 1.15f;
    }
}
