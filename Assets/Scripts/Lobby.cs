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

        // 시안(로비 배치)을 그림 층으로 나눠 놓았다. 배경(lobby_bg) 위에 각 층을 픽셀 위치에 올린다.
        // 각 층은 시안 그림과 픽셀 단위로 맞춘 위치다 (냉장고·캐릭터·말풍선·달력·금액·도감·왕관·시작 바)
        Piece(fit, "Bg", "lobby_bg", 0, 0, RefW, RefH, null);
        // 냉장고는 버튼임을 드러낸다: 올리면 살짝 커지고(1.04), 눌리면 어두워지며 내려앉는다
        var fridge = Piece(fit, "Fridge", "lobby_fridge", 1451, 238, 420, 644, openFridge);
        if (fridge != null) fridge.gameObject.AddComponent<HoverScale>().scale = 1.04f;
        Piece(fit, "Character", "lobby_character", 558, 473, 359, 541, openTitles);

        // 대표 칭호: 캐릭터 머리 위 말풍선. 눌러도 칭호 목록이 열린다
        var rep = Titles.Representative;
        var tagPiece = Piece(fit, "TitleTag", "lobby_titletag", 589, 415, 310, 110, openTitles);
        // 글자는 말풍선의 자식이라 말풍선과 함께 움직인다. 글자 칸은 말풍선 안쪽(시안 600~895, 437~522)
        if (tagPiece != null)
        {
            var tag = LabelIn(tagPiece, 310, 110, rep == null ? "칭호 없음" : rep.name, 31, 33, 251, 64, 34, Color.white);
            tagText = tag;
            var o = tag.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.78f, 0.36f, 0.27f);
            o.effectDistance = new Vector2(3f, -3f);
        }

        // 현재 주차 / 보유 금액 (새 게임 직전이면 1주차와 시작 예산)
        Piece(fit, "WeekCard", "lobby_day", 22, 21, 297, 88, null);
        Piece(fit, "WeekIcon", "icon_week", 46, 37, 49, 50, null);
        Text(fit, run.week + "주차", 122, 40, 180, 50, 44, Brown, TextAnchor.MiddleLeft);
        Piece(fit, "MoneyCard", "lobby_money", 342, 21, 297, 88, null);
        Piece(fit, "MoneyIcon", "icon_money", 370, 36, 47, 53, null);
        string money = run.budget.ToString("N0");
        Text(fit, money, 442, 40, 180, 50, 44, Brown, TextAnchor.MiddleLeft);

        // 아래 줄: 도감(책) · 칭호(왕관) · 게임 시작
        // 단축키 이름표는 각 버튼 위에 붙어 같이 움직인다
        var dex = Piece(fit, "Dex", "lobby_dex", 1043, 903, 206, 157, openDex);
        if (dex != null) LabelIn(dex, 206, 157, "도감 [D]", 0, -40, 206, 34, 26, Color.white);
        var crown = Piece(fit, "TitleIcon", "icon_title", 1266, 906, 196, 153, openTitles);
        if (crown != null) LabelIn(crown, 196, 153, "칭호 [T]", 0, -40, 196, 34, 26, Color.white);
        var start = Piece(fit, "Start", "lobby_start", 1481, 931, 418, 128, startGame);
        if (start != null) UIKit.PrimaryLabel(start, "게임 시작", 60, true);
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

    // 그림 조각의 자식으로 붙는 글자. x, y는 그림 왼쪽 위 기준 픽셀(음수 가능, 그림 밖에 둘 수 있다), pw·ph는 그림의 기준 크기.
    // 그림의 자식이라 그림이 눌리거나 올라올 때 글자도 같이 움직인다.
    static Text LabelIn(RectTransform piece, float pw, float ph, string text, float x, float y, float w, float h, int size, Color color)
    {
        var rt = UIKit.Box(piece, "Label", x / pw, 1f - (y + h) / ph, (x + w) / pw, 1f - y / ph);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = UIKit.GetFont();
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 12;
        t.resizeTextMaxSize = size;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = Brown;
        o.effectDistance = new Vector2(2f, -2f);
        return t;
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
