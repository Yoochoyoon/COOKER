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

public partial class GameFlow : MonoBehaviour
{
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
        ResetCookSfx();
        Sound.Music("bgm_cook");
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

        // 화면 번쩍임 칸: 모든 조리 그림 위에 덮이도록 마지막에 만든다. 클릭은 통과시킨다
        flashImg = UIKit.Box(root, "Flash", 0f, 0f, 1f, 1f, new Color(1f, 1f, 1f, 0f), false).GetComponent<Image>();
        flashImg.raycastTarget = false;
        flashT = -1f;
        finishFx = -1f;

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
        float scale = 1f;
        if (i < flyT.Length && flyT[i] < 1f)
        {
            // 성공한 재료: 팬에서 접시까지 포물선으로 날아가며 공중에서 살짝 커진다
            float p = flyT[i], e = 1f - (1f - p) * (1f - p) * (1f - p), lift = FlyArc * Mathf.Sin(p * Mathf.PI);
            var f = flyFrom[i];
            x0 = Mathf.Lerp(f.x, x0, e); x1 = Mathf.Lerp(f.z, x1, e);
            y0 = Mathf.Lerp(f.y, y0, e) + lift; y1 = Mathf.Lerp(f.w, y1, e) + lift;
            scale = 1f + 0.25f * Mathf.Sin(p * Mathf.PI);
        }
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one * scale;
        if (i < shakeT.Length && shakeT[i] > 0f)
            rt.anchoredPosition = new Vector2(Mathf.Sin(shakeT[i] * 90f) * ShakePx * (shakeT[i] / ShakeTime), 0f); // 망친 재료는 접시에서 부르르 떤다
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

    // ---- 조리 효과음
    // 넣기·꺼내기·뒤집기는 클릭, 끌어다 놓기, 키(GameFlow.Core의 Update) 여러 곳에서 일어난다.
    // 호출하는 곳마다 소리를 붙이면 빠뜨리기 쉬워서, 매 프레임 조리 상태가 바뀐 순간을 보고 한 곳에서 튼다.
    Spot[] sfxSpots = new Spot[0];
    bool sfxFlipped, sfxLid, sfxBubble;
    float sfxStirTimer;
    const float StirSfxGap = 0.12f; // 연타가 아주 빠를 때 젓는 소리가 겹쳐 웅웅거리지 않게

    void ResetCookSfx()
    {
        int n = cook.items.Length;
        sfxSpots = cook.items.Select(it => it.spot).ToArray();
        sfxFlipped = false; sfxLid = false; sfxBubble = false; sfxStirTimer = 0f; sfxFlipMissed = false;
        flyFrom = new Vector4[n];
        flyT = Enumerable.Repeat(1f, n).ToArray();
        shakeT = new float[n];
    }

    void PlayCookSfx()
    {
        bool put = false, take = false; // 시간이 다 돼 한꺼번에 꺼낼 때 같은 소리가 여러 번 겹치지 않게 한 프레임에 한 번만
        bool ruined = false;
        for (int i = 0; i < cook.items.Length && i < sfxSpots.Length; i++)
        {
            var it = cook.items[i];
            var now = it.spot;
            if (now == sfxSpots[i]) continue;
            if (now == Spot.Pan) put = true; else take = true;
            if (sfxSpots[i] == Spot.Pan && now == Spot.Plate && i < itemRects.Count)
            {
                // 아직 PlaceItem 전이라 그림은 팬 자리에 있다. 이 자리에서 접시로 날아가게 한다
                var r = itemRects[i];
                if (it.ok) { flyFrom[i] = new Vector4(r.anchorMin.x, r.anchorMin.y, r.anchorMax.x, r.anchorMax.y); flyT[i] = 0f; }
                else { shakeT[i] = ShakeTime; ruined = true; }
            }
            sfxSpots[i] = now;
        }
        if (put) Sound.Play("put");
        if (take) Sound.Play("take");
        if (ruined) Flash(UIKit.Bad, 0.75f, true);
        if (cook.flipped && !sfxFlipped) { Sound.Play("flip"); Flash(Color.white, 0.5f, false); }
        sfxFlipped = cook.flipped;
        if (cook.flipMissed && !sfxFlipMissed) Flash(UIKit.Accent, 0.65f, true); // 뒤집기를 놓친 순간도 색으로 알린다
        sfxFlipMissed = cook.flipMissed;
        if (stirTimer > sfxStirTimer) Sound.Play("stir", StirSfxGap); // 연타할 때마다 stirTimer가 다시 채워진다
        sfxStirTimer = stirTimer;
        bool lid = (lidOnPot != null && lidOnPot.activeSelf) || cook.lidProgress > 0f; // 뚜껑 그림이 없는 화면은 누르는 동안 lidProgress가 오른다
        if (lid && !sfxLid) Sound.Play("lid");
        sfxLid = lid;
        if (cook.bubble && !sfxBubble) Sound.Play("boil_over");
        sfxBubble = cook.bubble;
    }

    // ---- 조리 피드백 연출 (docs/19 4번): 성공은 접시로 날아감, 실패는 빨간 번쩍임과 떨림
    Vector4[] flyFrom = new Vector4[0]; // 날아가기 시작한 자리 (anchorMin.x, anchorMin.y, anchorMax.x, anchorMax.y)
    float[] flyT = new float[0];        // 0~1 날아가는 진행도. 1이면 제자리
    float[] shakeT = new float[0];      // 떨림 남은 시간
    bool sfxFlipMissed;
    Image flashImg;
    float flashT = -1f;                 // 번쩍임이 시작된 뒤 흐른 시간. -1이면 쉬는 중
    float flashPeak;
    Color flashColor;
    float finishFx = -1f;               // 조리가 끝난 뒤 연출을 보여 주는 시간. -1이면 아직 안 끝남
    const float FlyTime = 0.4f, FlyArc = 0.12f, ShakeTime = 0.35f, ShakePx = 14f;
    const float FlashOn = 0.05f, FlashGap = 0.05f, FlashFade = 0.35f; // 번쩍-깜빡-번쩍 후 서서히 사라진다
    const float BoomGap = 0.1f;         // 같은 프레임에 여러 번 번쩍여도 펑은 한 번만
    const float FinishFxTime = 0.5f;    // 마지막 재료가 접시에 닿는 걸 보고 다음 화면으로 넘어가게

    // boom: 펑 소리를 같이 낸다 (실패·놓침). 뒤집기 성공은 자기 소리(flip)가 있어 번쩍임만 한다
    void Flash(Color c, float peak, bool boom)
    {
        if (flashT >= 0f && peak < FlashAlpha()) return; // 더 센 번쩍임이 진행 중이면 덮지 않는다
        flashColor = c;
        flashPeak = peak;
        flashT = 0f;
        if (boom) Sound.Play("pop", BoomGap);
    }

    float FlashAlpha()
    {
        if (flashT < 0f) return 0f;
        if (flashT < FlashOn) return flashPeak;
        if (flashT < FlashOn + FlashGap) return flashPeak * 0.2f;
        float f = flashT - FlashOn - FlashGap;
        return flashPeak * Mathf.Clamp01(1f - f / (FlashOn + FlashFade)); // 두 번째 번쩍은 꼭대기에서 바로 사라지기 시작
    }

    void UpdateCookFx()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < flyT.Length; i++)
        {
            if (flyT[i] < 1f) flyT[i] = Mathf.Min(1f, flyT[i] + dt / FlyTime);
            if (shakeT[i] > 0f) shakeT[i] = Mathf.Max(0f, shakeT[i] - dt);
        }
        if (flashImg == null || flashT < 0f) return;
        flashT += dt;
        float a = FlashAlpha();
        // 번쩍이는 첫 순간은 하얗게 달아오른 색으로, 그 뒤 원래 색으로 (빛이 터지는 느낌)
        var c = Color.Lerp(Color.white, flashColor, Mathf.Clamp01(flashT / (FlashOn + FlashGap)));
        flashImg.color = new Color(c.r, c.g, c.b, a);
        if (a <= 0f) flashT = -1f;
    }

    void RefreshCookUI()
    {
        PlayCookSfx();
        UpdateCookFx();
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
        // 끝나자마자 화면을 바꾸면 마지막 재료가 날아가는 모습과 번쩍임이 보이지 않는다.
        // GameFlow.Core의 Update가 cook.finished인 동안 매 프레임 여기를 부르므로, 잠깐 연출만 돌리고 돌아간다.
        if (finishFx < 0f)
        {
            finishFx = 0f;
            bool allRuined = cook.GoodIngredients.Length == 0;
            if (allRuined && cook.Used.Length > 0) Flash(UIKit.Bad, 0.85f, true);
            Sound.Play(allRuined ? "fail" : "success");
        }
        finishFx += Time.deltaTime;
        UpdateCookFx();
        for (int i = 0; i < itemRects.Count && i < cook.items.Length; i++) PlaceItem(i);
        if (finishFx < FinishFxTime) return;
        finishFx = -1f;

        var used = cook.Used;                 // 접시에 올린 재료(소비됨). 트레이에 남은 재료는 쓰지 않은 것
        var ings = cook.GoodIngredients;      // 성공한 재료만 요리가 된다
        var usedStages = cook.UsedStages;
        var stages = cook.GoodStages;
        var lost = cook.LostIngredients;
        string fail = cook.FailText;
        var md = cook.m;
        int sat = cook.Satisfaction();
        cook = null;
        Sound.Music("bgm_lobby"); // 조리가 끝나면 식사·하루 선택 화면으로 가므로 평소 곡으로 돌아간다

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
        ShowMeal(dish, isNew); // 요리 이름은 Recipes.NameOf 규칙 이름을 그대로 쓴다 (실행 중 AI 호출 없음)
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

}
