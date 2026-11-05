using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 로비: 한 판의 거점 화면 (docs/17). 타이틀 다음에 뜨고, [게임 시작]을 누르면 장보기로 간다.
// 지금은 구성만 있다: 현재 일차, 보유 금액, 도감 버튼, 게임 시작 버튼. 디자인은 나중에 입힌다.
// 주인공 캐릭터(칭호 표시)와 냉장고 버튼은 후순위라 자리만 비워 둔다.
// GameFlow는 화면 지우기·장보기·도감을 넘겨주기만 하고, 로비 화면 내용은 전부 이 파일에서 그린다.
public static class Lobby
{
    // root: 화면을 그릴 곳, run: 진행 중인 판(일차·예산을 읽는다), clear: 이전 화면 지우기
    public static void Show(Transform root, Run run, Action clear, Action startGame, Action openDex)
    {
        clear();
        var screen = UIKit.Box(root, "Lobby", 0, 0, 1, 1);

        UIKit.Label(screen, "로비", 0.03f, 0.86f, 0.5f, 0.98f, 72, TextAnchor.MiddleLeft, UIKit.Accent);

        // 현재 일차 / 보유 금액 (새 게임 직전이면 1일 차와 시작 예산)
        UIKit.Box(screen, "DayCard", 0.05f, 0.62f, 0.47f, 0.82f, UIKit.Card);
        UIKit.Label(screen, "현재 일차", 0.07f, 0.75f, 0.45f, 0.81f, 32, TextAnchor.MiddleLeft, UIKit.Muted);
        UIKit.Label(screen, run.week + "주차 " + run.day + "일 차  (총 " + run.Abs + "일 차)", 0.07f, 0.63f, 0.45f, 0.75f, 52, TextAnchor.MiddleLeft);

        UIKit.Box(screen, "MoneyCard", 0.53f, 0.62f, 0.95f, 0.82f, UIKit.Card);
        UIKit.Label(screen, "보유 금액", 0.55f, 0.75f, 0.93f, 0.81f, 32, TextAnchor.MiddleLeft, UIKit.Muted);
        UIKit.Label(screen, run.budget.ToString("N0") + "원", 0.55f, 0.63f, 0.93f, 0.75f, 52, TextAnchor.MiddleLeft);

        // 후순위 자리: 주인공 캐릭터(머리 위 칭호, 누르면 칭호 목록) · 냉장고 버튼(가진 재료 목록)
        Placeholder(screen, "주인공 캐릭터 자리\n(칭호 표시 · 준비 중)", 0.05f, 0.24f, 0.47f, 0.57f);
        Placeholder(screen, "냉장고 버튼 자리\n(가진 재료 · 준비 중)", 0.53f, 0.24f, 0.95f, 0.57f);

        UIKit.Btn(screen, "도감 [D]", 0.05f, 0.05f, 0.30f, 0.19f, openDex, UIKit.Muted, 40);
        UIKit.Btn(screen, "게임 시작 [Enter]", 0.55f, 0.05f, 0.95f, 0.19f, startGame, UIKit.Good, 48);

        var keys = screen.gameObject.AddComponent<LobbyKeys>();
        keys.startGame = startGame;
        keys.openDex = openDex;
    }

    static void Placeholder(Transform parent, string text, float x0, float y0, float x1, float y1)
    {
        UIKit.Box(parent, "Slot", x0, y0, x1, y1, new Color(1f, 1f, 1f, 0.35f), false);
        UIKit.Label(parent, text, x0, y0, x1, y1, 34, TextAnchor.MiddleCenter, UIKit.Muted);
    }

    // 로비의 키보드: Enter/Space = 게임 시작, D = 도감. 도감이 열려 있는 동안(과 닫힌 그 프레임)은 GameFlow가 키를 처리한다.
    class LobbyKeys : MonoBehaviour
    {
        public Action startGame, openDex;
        int lastModalFrame = -10;

        void Update()
        {
            if (GameObject.Find("COOKER/Canvas/Dex") != null || GameObject.Find("COOKER/Canvas/DexDetail") != null)
            {
                lastModalFrame = Time.frameCount;
                return;
            }
            if (Time.frameCount - lastModalFrame <= 1) return; // 도감을 닫은 키가 게임 시작으로 이어지지 않게
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) startGame();
            else if (kb.dKey.wasPressedThisFrame) openDex();
        }
    }
}
