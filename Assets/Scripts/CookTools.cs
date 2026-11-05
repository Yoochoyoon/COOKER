using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 조리 도구: 굽기·볶기는 뒤집개, 끓이기는 냄비 뚜껑.
// - 뒤집개: 클릭하면 손에 집힌다. 볶기는 들고 팬 위에서 휘저으면 젓기, 굽기는 뒤집을 때 팬을 클릭하면 뒤집기.
//   조리대(원래 버튼 자리)를 클릭하거나 오른쪽 클릭·Esc면 내려놓는다.
// - 뚜껑: 냄비 위에 덮여 있다. 끓어넘칠 때(거품) 손잡이를 클릭하면 뚜껑이 떼어지며 거품이 가라앉고, 잠시 뒤 다시 덮인다.
// 원래의 뒤집기 버튼·젓기 칸·뚜껑 칸은 이 도구로 대신하므로 화면 밖으로 치운다. 키보드(Space)는 그대로 된다.
// GameFlow.StartCook 끝에서 Attach 한 줄로 붙인다.
public class CookTools : MonoBehaviour
{
    // root: 조리 화면, panRect: 팬/냄비 칸, stir: 한 번 저었다고 알리기 (GameFlow의 젓기 타이머를 채운다)
    public static void Attach(Transform root, Cook cook, RectTransform panRect, bool cookArt, Action stir)
    {
        if (cook == null || panRect == null) return;
        foreach (var old in new[] { "FlipBtnBg", "Btn_뒤집기! [Space]", "StirArea", "Lid" }) HideOld(root, old);
        var go = new GameObject("CookTools", typeof(RectTransform));
        go.transform.SetParent(root, false);
        var t = go.AddComponent<CookTools>();
        t.cook = cook;
        t.panRect = panRect;
        t.stir = stir;
        t.Build(root, cookArt);
    }

    // 원래 조작 칸은 지우지 않고 화면 밖으로 옮긴다 (GameFlow가 계속 참조해도 문제없게)
    static void HideOld(Transform root, string name)
    {
        var tr = root.Find(name) as RectTransform;
        if (tr == null) return;
        tr.anchorMin = new Vector2(-2f, -2f);
        tr.anchorMax = new Vector2(-1.9f, -1.9f);
        var g = tr.GetComponent<CanvasGroup>();
        if (g == null) g = tr.gameObject.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        g.blocksRaycasts = false;
        g.interactable = false;
    }

    Cook cook;
    RectTransform panRect, restRT, spatulaRT, blocker, lidRT, lidImgRT;
    Image lidImg;
    ToolHold spatula;
    Action stir;
    Text hint;
    string sayText;
    float sayTimer, lidOffTimer;

    const float LidOffTime = 1.2f; // 뚜껑을 뗐다가 다시 덮을 때까지
    const float HeldScale = 1.6f;  // 손에 든 뒤집개 크기 배율
    const float RefW = 1920f, RefH = 1080f; // 기준 해상도 (픽셀로 계산한 뒤 화면 비율로 바꾼다)

    void Build(Transform root, bool cookArt)
    {
        // 조리대(도구 놓는 자리): 원래 방식별 버튼이 있던 칸
        Vector4 r = cookArt ? new Vector4(0.335f, 0.034f, 0.566f, 0.156f) : new Vector4(0.64f, 0.34f, 0.97f, 0.53f);

        if (cook.m.id == Method.Boil)
        {
            // 뚜껑: 냄비 입구 위에 덮는다. 냄비 그림(bg_cook_pot)의 입구는 냄비 칸 가운데보다 오른쪽에 있어서 그림 기준 위치를 따로 쓴다.
            // 재료를 가리지 않게 유리는 마우스를 통과시키고 손잡이만 눌린다
            Vector2 c; float size;
            if (cookArt) { c = new Vector2(0.332f * RefW, 0.62f * RefH); size = 0.30f * RefW; }
            else
            {
                c = new Vector2((panRect.anchorMin.x + panRect.anchorMax.x) * 0.5f * RefW, (panRect.anchorMin.y + panRect.anchorMax.y) * 0.5f * RefH);
                size = Mathf.Min((panRect.anchorMax.x - panRect.anchorMin.x) * RefW, (panRect.anchorMax.y - panRect.anchorMin.y) * RefH) * 0.9f;
            }
            PotBubbles.Attach(root, cook, c, size * 0.92f); // 끓어넘칠 때 거품 (뚜껑보다 아래에 그린다)
            lidRT = UIKit.Box(root, "LidTool", (c.x - size / 2f) / RefW, (c.y - size / 2f) / RefH, (c.x + size / 2f) / RefW, (c.y + size / 2f) / RefH);
            var fit = UIKit.Box(lidRT, "Fit", 0, 0, 1, 1);
            var f = fit.gameObject.AddComponent<AspectRatioFitter>();
            f.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            f.aspectRatio = 1f;
            lidImgRT = UIKit.Box(fit, "Glass", 0f, 0f, 1f, 1f);
            lidImg = lidImgRT.gameObject.AddComponent<Image>();
            lidImg.sprite = UIKit.GlassLid();
            lidImg.raycastTarget = false;
            var knob = UIKit.Box(lidImgRT, "Knob", 0.38f, 0.38f, 0.62f, 0.62f, new Color(1f, 1f, 1f, 0f), false);
            var b = knob.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(LiftLid);
            hint = UIKit.Label(root, "", r.x + 0.005f, r.y, r.z - 0.005f, r.w, cookArt ? 32 : 36, TextAnchor.MiddleCenter, UIKit.Ink);
            UIKit.Box(root, "LidNote", r.x, r.y, r.z, r.w, new Color(1f, 0.97f, 0.9f, 0.85f)).SetSiblingIndex(hint.transform.GetSiblingIndex());
            return;
        }

        // 뒤집개를 들고 있을 때 팬 위 재료가 클릭으로 꺼내지지 않게 막는 투명 막
        blocker = UIKit.Box(root, "SpatulaBlocker", panRect.anchorMin.x, panRect.anchorMin.y, panRect.anchorMax.x, panRect.anchorMax.y, new Color(1f, 1f, 1f, 0f), false);
        blocker.gameObject.SetActive(false);

        restRT = UIKit.Box(root, "ToolRest", r.x, r.y, r.z, r.w, new Color(1f, 0.97f, 0.9f, 0.92f));
        hint = UIKit.Label(restRT, cook.m.id == Method.Stirfry ? "뒤집개를 집어 팬 위에서 휘젓기" : "뒤집개를 집고, 때가 되면 팬 클릭",
            0.02f, 0.58f, 0.98f, 0.99f, cookArt ? 30 : 34, TextAnchor.MiddleCenter, UIKit.Ink);
        // 뒤집개는 조리대 칸보다 크게(칸 밖으로 조금 삐져나와도 된다) 그리고, 손에 들면 더 커진다 (Update)
        spatulaRT = UIKit.ToolImage(restRT, "Spatula", UIKit.Spatula(), -0.04f, -0.30f, 1.04f, 0.66f);
        spatula = spatulaRT.gameObject.AddComponent<ToolHold>();
        spatula.onPickUp = () => blocker.gameObject.SetActive(true);
        spatula.onPutDown = () => blocker.gameObject.SetActive(false);
        if (cook.m.id == Method.Stirfry)
        {
            spatula.onMove = (pos, delta) =>
            {
                if (!OverPan(pos) || delta.magnitude < 2f) return;
                stir();
                spatulaRT.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 25f) * 14f); // 휘젓는 손맛
            };
            spatula.onUse = OverRest;
        }
        else
        {
            spatula.onUse = pos =>
            {
                if (OverPan(pos))
                {
                    if (cook.flipAvailable) { cook.Flip(); Say("뒤집었다!"); }
                    else Say(cook.flipped ? "이미 뒤집었다" : "아직 뒤집을 때가 아니야");
                }
                return OverRest(pos);
            };
        }
    }

    bool OverPan(Vector2 pos) { return RectTransformUtility.RectangleContainsScreenPoint(panRect, pos, null); }
    bool OverRest(Vector2 pos) { return restRT != null && RectTransformUtility.RectangleContainsScreenPoint(restRT, pos, null); }
    void Say(string msg) { sayText = msg; sayTimer = 1.5f; }

    // 뚜껑 떼기: 거품이 넘칠 때 떼면 가라앉는다. 안 넘칠 때 떼면 그냥 한 번 들여다본다
    void LiftLid()
    {
        if (lidOffTimer > 0f || cook.finished) return;
        if (cook.bubble) { cook.OpenLid(); Say("뚜껑을 떼서 거품을 가라앉혔다!"); }
        else Say("아직 끓어넘치지 않는다");
        lidOffTimer = LidOffTime;
    }

    void Update()
    {
        if (cook == null) return;
        sayTimer -= Time.deltaTime;

        if (lidImgRT != null)
        {
            // 뗀 동안은 냄비 오른쪽 위로 비켜 있다가 다시 내려앉는다. 넘칠 것 같으면 들썩이며 붉어진다
            lidOffTimer = Mathf.Max(0f, lidOffTimer - Time.deltaTime);
            float off = lidOffTimer > 0f ? 1f : 0f;
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            var target = new Vector2(off * lidImgRT.rect.width * 0.55f, off * lidImgRT.rect.height * 0.45f);
            lidImgRT.anchoredPosition = Vector2.Lerp(lidImgRT.anchoredPosition, target, k);
            lidImgRT.localRotation = Quaternion.Euler(0, 0, off > 0f ? -18f : cook.bubble ? Mathf.Sin(Time.time * 30f) * 4f : 0f);
            lidImg.color = cook.bubble && off == 0f ? new Color(1f, 0.75f, 0.7f) : Color.white;
            hint.text = sayTimer > 0f ? sayText : cook.bubble ? "끓어넘친다! 뚜껑 손잡이를 클릭해서 떼!" : "뚜껑: 끓어넘치면 손잡이 클릭";
            hint.color = cook.bubble && sayTimer <= 0f ? UIKit.Bad : UIKit.Ink;
            return;
        }

        if (spatulaRT != null)
        {
            // 굽기: 뒤집을 때가 오면 뒤집개가 콩콩 뛴다
            bool call = cook.m.id == Method.Grill && cook.flipAvailable && !spatula.IsHeld;
            float held = spatula.IsHeld ? HeldScale : 1f; // 손에 들었을 때는 팬 위에서 잘 보이게 크게
            spatulaRT.localScale = Vector3.one * held * (call ? 1f + Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 0.15f : 1f);
            if (spatula.IsHeld && cook.m.id == Method.Stirfry && Mouse.current != null && Mouse.current.delta.ReadValue().magnitude < 2f)
                spatulaRT.localRotation = Quaternion.identity;
            if (sayTimer > 0f) hint.text = sayText;
            else hint.text = cook.m.id == Method.Stirfry ? (spatula.IsHeld ? "팬 위에서 휘저어! (오른쪽 클릭: 내려놓기)" : "뒤집개를 집어 팬 위에서 휘젓기")
                : cook.flipAvailable ? (spatula.IsHeld ? "지금 팬을 클릭해서 뒤집어!" : "지금! 뒤집개를 집어!")
                : spatula.IsHeld ? "때가 되면 팬 클릭 (오른쪽 클릭: 내려놓기)" : "뒤집개를 집고, 때가 되면 팬 클릭";
        }
    }
}
