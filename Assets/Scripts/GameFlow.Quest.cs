using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// 퀘스트: 한 주 동안 먹은 요리(run.dishes)만 보고 판정하는 다섯 가지 목표.
// 주가 끝나 다음 주로 넘어갈 때(NextWeek) 달성한 퀘스트를 "받을 수 있음"으로 바꿔 두고, 로비의 [퀘스트]에서 [받기]를 누르면 예산이 늘어난다.
// 로비는 주 시작 때만 열리므로(판 도중 로비 복귀는 주차 취소) 보상은 바로 그 주의 장보기 예산에 더해진다.
// 판정에 새로 기록하는 것이 없어서 조리·식사 화면은 건드리지 않는다. 저장은 Run.questLast(지난 주 진행)와 Run.questPending(받을 것)뿐이다.
public class QuestDef
{
    public string name, desc;
    public string item; // 받을 때 함께 주는 재료 (이름은 저장·식별에 쓰는 재료 이름 그대로)
    public int target;
    public System.Func<IList<Dish>, int> progress;
}

public static class Quests
{
    public const int Reward = 300; // 퀘스트 하나를 받을 때 늘어나는 예산 (원). 재료는 퀘스트마다 1개 (QuestDef.item). 바꾸면 docs/09에 한 줄 남긴다
    const string Ramen = "비상 라면";

    static IEnumerable<Dish> Cooked(IList<Dish> dishes) { return dishes.Where(d => d.key != Ramen); }

    public static readonly QuestDef[] All =
    {
        new QuestDef { name = "직접 해 먹기", desc = "비상 라면 없이 끼니 7번 먹기", target = 7, item = "달걀",
            progress = d => Cooked(d).Count() },
        new QuestDef { name = "맛있는 한 끼", desc = "만족도 70 이상 요리 3번 만들기", target = 3, item = "감자",
            progress = d => Cooked(d).Count(x => x.satisfaction >= 70) },
        new QuestDef { name = "골고루 먹기", desc = "서로 다른 요리 5가지 만들기", target = 5, item = "김치",
            progress = d => Cooked(d).Select(x => x.key).Distinct().Count() },
        new QuestDef { name = "푸짐한 한 상", desc = "재료를 2가지 이상 섞은 요리 3번 만들기", target = 3, item = "양파",
            progress = d => Cooked(d).Count(x => x.ings != null && x.ings.Length >= 2) },
        new QuestDef { name = "방식 두루두루", desc = "굽기·볶기·끓이기를 모두 써 보기", target = 3, item = "달걀",
            progress = d => Cooked(d).Select(x => x.method).Distinct().Count(m => m == Method.Grill || m == Method.Stirfry || m == Method.Boil) },
    };

    public static int[] Progress(IList<Dish> dishes)
    {
        return All.Select(q => Mathf.Min(q.target, q.progress(dishes))).ToArray();
    }

    // 주가 끝날 때: 이번 주 진행을 지난 주 기록으로 남기고, 달성한 퀘스트를 받을 수 있게 한다 (아직 안 받은 것은 그대로 둔다)
    public static void CloseWeek(Run run)
    {
        run.questLast = Progress(run.dishes);
        for (int i = 0; i < All.Length; i++)
            if (run.questLast[i] >= All[i].target) run.questPending |= 1 << i;
    }

    public static Ingredient Item(QuestDef q) { return Data.All.FirstOrDefault(x => x.name == q.item); }

    public static bool IsPending(Run run, int i) { return (run.questPending & (1 << i)) != 0; }

    public static int PendingCount(Run run)
    {
        int n = 0;
        for (int i = 0; i < All.Length; i++) if (IsPending(run, i)) n++;
        return n;
    }
}

public partial class GameFlow
{
    // 로비 아래 줄(도감 · 칭호 · 게임 시작) 왼쪽에 퀘스트 버튼을 놓는다. 받을 보상이 있으면 빨간 숫자 표시가 붙는다.
    // 로비 그림은 영은의 Lobby.cs가 그리므로 그 파일은 건드리지 않고, 로비를 그린 뒤 GameFlow가 버튼만 더 올린다.
    void BuildQuestBtn()
    {
        int pend = Quests.PendingCount(run);
        var btn = HudStyle.Panel(root, "QuestBtn", DX(1013), DY(978), DX(1143), DY(873), HudStyle.Tan, false, true);
        var img = btn.GetComponent<Image>();
        var b = btn.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.highlightedColor = new Color(1f, 0.93f, 0.62f);
        cb.pressedColor = new Color(0.85f, 0.75f, 0.55f);
        b.colors = cb;
        b.onClick.AddListener(ShowQuests);
        btn.gameObject.AddComponent<PressDown>().depth = 5f;
        var icon = UIKit.Box(btn, "Icon", 0.2f, 0.16f, 0.8f, 0.84f).gameObject.AddComponent<Image>();
        icon.sprite = Art.Load("icon_star");
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        if (icon.sprite == null) icon.enabled = false;
        var tag = UIKit.Label(root, "퀘스트 [Q]", DX(1003), DY(870), DX(1153), DY(838), 24, TextAnchor.MiddleCenter, Color.white);
        var o = tag.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.78f, 0.36f, 0.27f);
        o.effectDistance = new Vector2(2f, -2f);
        if (pend > 0)
        {
            var badge = HudStyle.Panel(btn, "Badge", 0.68f, 0.66f, 1.12f, 1.14f, UIKit.Bad, true);
            UIKit.Label(badge, pend.ToString(), 0f, 0f, 1f, 1f, 30, TextAnchor.MiddleCenter, Color.white).raycastTarget = false;
        }
    }

    // 퀘스트 창 (로비 전용). 줄마다 이름 · 목표 · 지난 주 진행 막대 · [받기]
    void ShowQuests()
    {
        CloseModal();
        modal = ModalDim("QuestDim");
        var m = HudStyle.Panel(modal.transform, "Quest", 0.20f, 0.06f, 0.80f, 0.94f, HudStyle.Sand);
        m.localScale = Vector3.one * 0.97f; // 가장자리 여백
        UIKit.Label(m, "퀘스트", 0.05f, 0.88f, 0.85f, 0.985f, 60, TextAnchor.MiddleCenter, HudStyle.Text);
        UIX.CloseX(m, 0.88f, 0.89f, 0.97f, 0.985f, CloseModal);
        string note = run.week == 1 ? "이번 주에 달성하면 다음 주 로비에서 받을 수 있어요" : "지난주에 달성한 퀘스트예요. 받으면 예산과 재료를 받아요";
        UIKit.Label(m, note, 0.05f, 0.80f, 0.95f, 0.875f, 28, TextAnchor.MiddleCenter, HudStyle.TextSoft);

        float rowH = 0.104f, step = 0.124f; // 줄 사이 간격을 벌린다
        for (int i = 0; i < Quests.All.Length; i++)
        {
            int qi = i;
            var q = Quests.All[i];
            float top = 0.79f - i * step, bot = top - rowH;
            var row = HudStyle.Panel(m, "Row", 0.055f, bot, 0.945f, top, HudStyle.SandSoft, true);
            var nameTxt = UIKit.Label(row, q.name, 0.035f, 0.50f, 0.43f, 0.94f, 34, TextAnchor.MiddleLeft, HudStyle.Text);
            nameTxt.resizeTextForBestFit = true; nameTxt.resizeTextMinSize = 20; nameTxt.resizeTextMaxSize = 34;
            var descTxt = UIKit.Label(row, q.desc, 0.035f, 0.06f, 0.43f, 0.52f, 24, TextAnchor.MiddleLeft, HudStyle.TextSoft);
            descTxt.resizeTextForBestFit = true; descTxt.resizeTextMinSize = 14; descTxt.resizeTextMaxSize = 24;
            // 보상 그림 두 칸: 돈 주머니(+금액)와 재료 그림(+개수). 오른쪽 아래에 숫자가 붙는다. 받을 수 있는 줄이 아니면 흐리게
            bool canClaim = Quests.IsPending(run, i);
            RewardSlot(row, 0.455f, "icon_money", null, Quests.Reward.ToString(), canClaim);
            RewardSlot(row, 0.545f, null, Quests.Item(q), "1", canClaim);
            int cur = run.questLast != null && i < run.questLast.Length ? Mathf.Min(q.target, run.questLast[i]) : 0;
            UIKit.Label(row, cur + " / " + q.target, 0.65f, 0.50f, 0.76f, 0.94f, 26, TextAnchor.MiddleRight, HudStyle.Text);
            BarBox(row, "BarBg", 0.65f, 0.18f, 0.775f, 0.40f, new Color(0.35f, 0.27f, 0.22f));
            if (cur > 0) BarBox(row, "BarFill", 0.65f, 0.18f, 0.65f + 0.125f * cur / q.target, 0.40f, cur >= q.target ? UIKit.Good : HudStyle.Gold);
            bool pending = Quests.IsPending(run, i);
            // 도전 중·받음 버튼과 같은 크림 버튼 그림을 쓰고, 받을 수 있을 때만 초록으로 물들인다 (못 누르면 그림이 회색으로 어두워진다)
            var claimBtn = UIKit.Btn(row, pending ? "받기" : cur >= q.target ? "받음" : "도전 중", 0.80f, 0.17f, 0.965f, 0.83f, () => ClaimQuest(qi), UIKit.Good, 30, pending);
            if (pending) TintGreen(claimBtn);
        }

        int count = Quests.PendingCount(run);
        UIKit.Label(m, "예산 " + run.budget.ToString("N0") + "원", 0.07f, 0.04f, 0.34f, 0.13f, 30, TextAnchor.MiddleLeft, HudStyle.Text);
        // 닫기는 오른쪽 위 X(와 Esc·Enter)가 맡으므로 아래에는 [모두 받기] 하나만 둔다. 노란 주요 버튼(lobby_start)과 같은 그림을 초록으로 바꿔 쓴다
        GreenPrimaryBtn(m, count > 0 ? "모두 받기 (" + count + "개)" : "모두 받기", 0.62f, 0.04f, 0.94f, 0.135f, ClaimAllQuests, 40, count > 0);
    }

    // 주요 버튼(lobby_start, 노란색)의 색상만 초록으로 돌린 그림. 처음 한 번 만들어 두고 계속 쓴다. 그림이 없으면 null
    static Sprite greenPrimary;
    static Sprite GreenPrimarySprite()
    {
        if (greenPrimary != null) return greenPrimary;
        var src = Art.Load("lobby_start");
        if (src == null) return null;
        var rt = RenderTexture.GetTemporary(src.texture.width, src.texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src.texture, rt); // 읽기 불가 텍스처도 복사해서 읽는다
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var r = src.rect;
        var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = tex.GetPixels();
        for (int k = 0; k < px.Length; k++)
        {
            float h, s, v;
            Color.RGBToHSV(px[k], out h, out s, out v);
            if (s < 0.12f) continue; // 흰 하이라이트·회색은 그대로
            // 노랑(0.13) -> 게임의 초록(UIKit.Good, 색상 0.41)으로. 주황 테두리도 같이 초록. 그대로 돌리면 너무 쨍해서 채도와 밝기를 낮춘다
            var c = Color.HSVToRGB(Mathf.Repeat(h + 0.28f, 1f), s * 0.62f, v * 0.84f);
            c.a = px[k].a;
            px[k] = c;
        }
        tex.SetPixels(px);
        tex.Apply();
        greenPrimary = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect, src.border);
        return greenPrimary;
    }

    // [Enter] 큰 버튼(PrimaryBtn)과 같은 모양의 초록 버튼. 못 누르면 PrimaryBtn과 같은 회색 버튼이다
    Button GreenPrimaryBtn(Transform parent, string text, float x0, float y0, float x1, float y1, System.Action onClick, int size, bool enabled)
    {
        var s = GreenPrimarySprite();
        if (s == null || !enabled) return UIKit.PrimaryBtn(parent, text, x0, y0, x1, y1, onClick, size, enabled);
        var rt = UIKit.Box(parent, "Btn_" + text, x0, y0, x1, y1);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = s;
        img.type = Image.Type.Sliced;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.None;
        if (onClick != null) b.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<LobbyHover>().img = img;
        rt.gameObject.AddComponent<PressDown>().depth = 5f;
        var label = UIKit.PrimaryLabel(rt, text, size, true);
        var o = label.GetComponent<Outline>();
        if (o != null) o.effectColor = new Color(0.16f, 0.40f, 0.26f); // 글자 테두리도 주황 대신 진한 초록
        return b;
    }

    // 크림 버튼(UIKit.Btn 기본 그림)을 게임의 초록으로 물들인다. 글자는 흰색, 마우스를 올리면 밝아지고 누르면 어두워진다
    static void TintGreen(Button b)
    {
        var img = b.GetComponent<Image>();
        if (img == null) return;
        var cream = new Color(1f, 0.965f, 0.86f); // 크림 버튼 그림의 바탕색 쯤
        img.color = new Color(UIKit.Good.r / cream.r, UIKit.Good.g / cream.g, UIKit.Good.b / cream.b);
        var cb = b.colors;
        cb.normalColor = new Color(0.92f, 0.92f, 0.92f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.78f, 0.78f, 0.78f);
        b.colors = cb;
        var label = b.GetComponentInChildren<Text>();
        if (label != null) label.color = Color.white;
    }

    // 진행 막대: 거의 사각형이고 모서리만 2px 정도 둥글다 (9칸 늘이기 그림 한 장을 만들어 둘 다 쓴다)
    static Sprite barSprite;
    static Sprite BarSprite()
    {
        if (barSprite != null) return barSprite;
        const int n = 8; const float r = 2f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float dx = Mathf.Max(r - px, 0f, px - (n - r)), dy = Mathf.Max(r - py, 0f, py - (n - r));
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f); // 모서리 바깥쪽은 투명, 가장자리는 부드럽게
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();
        barSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(3, 3, 3, 3));
        return barSprite;
    }

    static void BarBox(Transform parent, string name, float x0, float y0, float x1, float y1, Color color)
    {
        var rt = UIKit.Box(parent, name, x0, y0, x1, y1, color, false);
        var img = rt.GetComponent<Image>();
        img.sprite = BarSprite();
        img.type = Image.Type.Sliced;
        img.raycastTarget = false;
    }

    // 줄 안의 보상 칸 하나: 어두운 모래색 정사각 칸에 그림 + 오른쪽 아래 숫자
    void RewardSlot(Transform row, float x0, string iconFile, Ingredient ing, string count, bool lit)
    {
        float a = lit ? 1f : 0.45f;
        var slot = HudStyle.Panel(row, "Reward", x0, 0.14f, x0 + 0.08f, 0.86f, new Color(0.83f, 0.74f, 0.62f), true);
        var img = UIKit.Box(slot, "Icon", 0.10f, 0.14f, 0.90f, 0.90f).gameObject.AddComponent<Image>();
        if (ing != null) SetIcon(img, ing);
        else img.sprite = Art.Load(iconFile);
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, a);
        if (img.sprite == null) img.enabled = false;
        var n = UIKit.Label(slot, count, 0.20f, 0f, 1.0f, 0.50f, 26, TextAnchor.LowerRight, Color.white);
        n.resizeTextForBestFit = true; n.resizeTextMinSize = 18; n.resizeTextMaxSize = 26;
        n.raycastTarget = false;
        var o = n.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0.29f, 0.20f, 0.14f);
        o.effectDistance = new Vector2(1.5f, -1.5f);
    }

    void ClaimQuest(int i)
    {
        if (!Quests.IsPending(run, i)) return;
        run.questPending &= ~(1 << i);
        AfterQuestClaim(Quests.Reward, new[] { Quests.Item(Quests.All[i]) });
    }

    void ClaimAllQuests()
    {
        int n = Quests.PendingCount(run);
        if (n == 0) return;
        var items = new List<Ingredient>();
        for (int i = 0; i < Quests.All.Length; i++) if (Quests.IsPending(run, i)) items.Add(Quests.Item(Quests.All[i]));
        run.questPending = 0;
        AfterQuestClaim(n * Quests.Reward, items);
    }

    // 퀘스트 재료를 냉장고에 넣는다. 유통기한은 장보기와 같게 오늘부터 센다 (이미 가진 재료와 섞이면 먼저 상하는 쪽을 따른다)
    void AddQuestItem(Ingredient ing)
    {
        if (ing == null) return;
        int have;
        run.inv.TryGetValue(ing, out have);
        run.inv[ing] = have + 1;
        int last = run.Abs + ing.shelf - 1, old;
        run.expire[ing] = run.expire.TryGetValue(ing, out old) && have > 0 ? Mathf.Min(old, last) : last;
    }

    void AfterQuestClaim(int gained, IEnumerable<Ingredient> items)
    {
        string before = run.budget.ToString("N0");
        run.budget += gained;
        foreach (var it in items) AddQuestItem(it);
        Sound.Play("click");
        RunSave.Save(run); // 받은 돈과 받은 표시를 바로 저장한다 (끄고 다시 켜도 두 번 받지 못하게)
        ShowQuests(); // 창을 새로 그려 버튼 상태와 예산을 갱신한다
        // 뒤에 있는 로비의 금액 글자도 바꾼다 (로비를 다시 그리면 창이 닫히고 칭호 알림이 겹칠 수 있어서 글자만 바꾼다)
        foreach (var t in root.GetComponentsInChildren<Text>(true))
            if (t.text == before) t.text = run.budget.ToString("N0");
        RefreshQuestBadge();
    }

    void RefreshQuestBadge()
    {
        var badge = root.Find("QuestBtn/Badge");
        if (badge != null && Quests.PendingCount(run) == 0) Destroy(badge.gameObject);
        else if (badge != null)
        {
            var t = badge.GetComponentInChildren<Text>();
            if (t != null) t.text = Quests.PendingCount(run).ToString();
        }
    }
}
