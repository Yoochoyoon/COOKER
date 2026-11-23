using System;
using UnityEngine;
using UnityEngine.UI;

// 로비: 한 판의 거점 화면 (docs/17, docs/18). 타이틀 다음, 그리고 한 주 정산이 끝날 때마다 돌아온다.
// 디자인 시안(이미지요소/로비화면/로비_전체화면.png)의 위치 그대로 그림 조각을 올린다.
//   배경 → 냉장고(재료 보관함 버튼) → 캐릭터(칭호 버튼) → 책상 → 칭호 표시 → 주차·금액 → 도감 · 게임 시작
// 그림(Resources/Art/lobby_*.png)이 없으면 글자만 있는 기본 화면으로 보인다.
// GameFlow는 화면 지우기·장보기·도감·칭호·냉장고를 넘겨주기만 하고, 로비 화면 내용은 이 파일에서 그린다.
// 키보드(Enter/Space 시작, D 도감, T 칭호)는 GameFlow의 공통 메뉴 키 처리(Scr.Lobby)가 맡는다.
public static class Lobby
{
    const float RefW = 1920f, RefH = 1080f; // 시안 크기. 아래 위치는 모두 이 크기의 픽셀 (왼쪽 위가 0,0)
    static readonly Color Brown = new Color(0.29f, 0.2f, 0.17f);

    // root: 화면을 그릴 곳, run: 진행 중인 판(주차·예산을 읽는다), clear: 이전 화면 지우기
    public static void Show(Transform root, Run run, Action clear, Action startGame, Action openDex, Action openTitles, Action openFridge)
    {
        clear();
        Sound.Music("bgm_lobby"); // 장보기·하루 선택도 이 곡이 이어진다
        var screen = UIKit.Box(root, "Lobby", 0, 0, 1, 1);

        // 창 비율이 16:9가 아니어도 시안과 똑같이 보이도록, 16:9 영역 안에 놓는다
        var fit = UIKit.Box(screen, "Stage", 0, 0, 1, 1);
        var f = fit.gameObject.AddComponent<AspectRatioFitter>();
        f.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        f.aspectRatio = RefW / RefH;

        if (Art.Load("lobby_bg") == null) { Fallback(screen, run, startGame, openDex, openTitles, openFridge); return; }

        // 시안(로비 배치) 그림 한 장에 냉장고·캐릭터·칭호 말풍선·도감 책·왕관·시작 바가 이미 그려져 있다.
        // 그래서 그림은 배경 하나만 깔고, 그 자리를 누르는 칸(투명 버튼)과 글자만 올린다.
        Piece(fit, "Bg", "lobby_bg", 0, 0, RefW, RefH, null);
        Hit(fit, "FridgeHit", 1455, 240, 415, 640, openFridge);
        Hit(fit, "CharacterHit", 578, 520, 327, 480, openTitles);

        // 대표 칭호: 캐릭터 머리 위 말풍선. 눌러도 칭호 목록이 열린다
        var rep = Titles.Representative;
        Hit(fit, "TitleHit", 600, 437, 295, 85, openTitles);
        // 글자 칸은 말풍선 안쪽(시안 600~895, 437~522)에 들어간다
        var tag = Text(fit, rep == null ? "칭호 없음" : rep.name, 622, 448, 251, 64, 42, Color.white, TextAnchor.MiddleCenter);
        tagText = tag;
        var o = tag.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.78f, 0.36f, 0.27f);
        o.effectDistance = new Vector2(3f, -3f);

        // 현재 주차 / 보유 금액 (새 게임 직전이면 1주차와 시작 예산)
        // 달력 칸(왼쪽 위 시안 자리 22~318, 25~105)에 주차 글자
        // 글자 칸 가운데를 시안 칸 가운데(y 65)에 맞춘다
        Text(fit, run.week + "주차", 122, 35, 180, 60, 56, Brown, TextAnchor.MiddleLeft);
        // 금액 칸(시안 342~638): 아이콘 옆에 금액 글자
        string money = run.budget.ToString("N0");
        Text(fit, money, 442, 33, 180, 60, 56, Brown, TextAnchor.MiddleLeft);

        // 아래 줄: 도감(책) · 칭호(왕관)
        Hit(fit, "DexHit", 1040, 900, 206, 157, openDex);
        Hit(fit, "TitleHit2", 1268, 905, 192, 150, openTitles);
        // 게임 시작: 글자 없는 버튼 그림(lobby_start_v2) 위에 코드로 글자를 올린다. 같은 그림·글자 모양을 다른 화면의 주요 버튼도 쓴다 (UIKit.PrimaryBtn)
        var start = Hit(fit, "StartHit", 1485, 935, 418, 120, startGame); // 시안 바(935~1055)의 가운데에 글자가 온다
        if (start != null) UIKit.PrimaryLabel(start, "게임 시작", 72, true);
    }

    static Text tagText; // 캐릭터 머리 위 대표 칭호 글자

    // 로비 위에서 대표 칭호를 바꿨을 때 뒤에 있는 로비 글자를 새로 고친다
    public static void RefreshTitle()
    {
        if (tagText == null) return;
        var rep = Titles.Representative;
        tagText.text = rep == null ? "칭호 없음" : rep.name;
    }

    // 시안 픽셀(왼쪽 위 기준)을 화면 비율로 바꿔 상자를 만든다
    static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        return UIKit.Box(parent, name, x / RefW, 1f - (y + h) / RefH, (x + w) / RefW, 1f - y / RefH);
    }

    // 그림 위에 눌리는 투명한 칸. 시안 그림에 이미 그려진 것(책·왕관·캐릭터 등)을 누르게 해 준다
    static RectTransform Hit(Transform parent, string name, float x, float y, float w, float h, Action onClick)
    {
        var rt = Rect(parent, name, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        b.onClick.AddListener(() => onClick());
        return rt;
    }

    // 그림 조각 하나. onClick이 있으면 누를 수 있다 (올리면 살짝 어두워진다)
    static RectTransform Piece(Transform parent, string name, string file, float x, float y, float w, float h, Action onClick)
    {
        var s = Art.Load(file);
        if (s == null) return null;
        var rt = Rect(parent, name, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = s;
        img.raycastTarget = onClick != null;
        if (onClick == null) return rt;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None; // 효과는 LobbyHover가 준다 (확대 + 눌림)
        b.onClick.AddListener(() => onClick());
        b.transition = Selectable.Transition.None;
        if (w * h > 300000f)
        {
            rt.gameObject.AddComponent<HoverScale>(); // 큰 그림(캐릭터·냉장고)은 올리면 아주 살짝 커지는 것뿐
            return rt;
        }
        rt.gameObject.AddComponent<LobbyHover>().img = img;
        rt.gameObject.AddComponent<PressDown>().depth = 5f;
        return rt;
    }

    static Text Text(Transform parent, string text, float x, float y, float w, float h, int size, Color color, TextAnchor anchor)
    {
        var rt = Rect(parent, "Text", x, y, w, h);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = UIKit.GetFont();
        t.text = text;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 12;
        t.resizeTextMaxSize = size;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
        return t;
    }

    // 그림이 아직 없을 때: 글자만 있는 화면 (아래 Fallback)
    static void Fallback(Transform screen, Run run, Action startGame, Action openDex, Action openTitles, Action openFridge)
    {
        UIKit.Label(screen, "로비", 0.03f, 0.86f, 0.5f, 0.98f, 72, TextAnchor.MiddleLeft, UIKit.Accent);
        UIKit.Box(screen, "DayCard", 0.05f, 0.62f, 0.47f, 0.82f, UIKit.Card);
        UIKit.Label(screen, run.week + "주차 " + run.day + "일 차", 0.07f, 0.63f, 0.45f, 0.81f, 52, TextAnchor.MiddleLeft);
        UIKit.Box(screen, "MoneyCard", 0.53f, 0.62f, 0.95f, 0.82f, UIKit.Card);
        UIKit.Label(screen, run.budget.ToString("N0") + "원", 0.55f, 0.63f, 0.93f, 0.81f, 52, TextAnchor.MiddleLeft);
        UIKit.Btn(screen, "도감 [D]", 0.05f, 0.05f, 0.24f, 0.19f, openDex, UIKit.Muted, 40, true, true);
        UIKit.Btn(screen, "칭호 [T]", 0.27f, 0.05f, 0.46f, 0.19f, openTitles, UIKit.Muted, 40, true, true);
        UIKit.Btn(screen, "냉장고", 0.27f, 0.24f, 0.46f, 0.34f, openFridge, UIKit.Muted, 40, true, true);
        UIKit.Btn(screen, "게임 시작 [Enter]", 0.55f, 0.05f, 0.95f, 0.19f, startGame, UIKit.Good, 48, true, true);
    }
}
