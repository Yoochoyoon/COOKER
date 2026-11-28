using UnityEngine;

// 창 비율이 16:9가 아닐 때(Game 창 Free Aspect, 4:3 모니터 등) 화면 내용을 배경 그림과 같은 16:9 칸에 맞춘다.
// 배경 그림은 비율을 지키며 가운데에 놓이는데(preserveAspect), 화면 내용은 창 전체 비율로 늘어나서
// 넓은 창에서는 접시 위 음식이 오른쪽으로 쏠리고, 로비의 퀘스트 버튼이 도감 버튼과 겹쳤다.
//
// 쓰는 법: 화면을 그리기 전에 StageFit.Attach(root). root의 자식으로 붙어서, 화면이 바뀌어
// GameFlow.Clear가 자식을 끄는 순간(OnDisable) root를 원래대로(창 전체) 되돌린다. 다른 화면에는 영향이 없다.
[DisallowMultipleComponent]
public class StageFit : MonoBehaviour
{
    const float Aspect = 1920f / 1080f; // 배경 그림과 좌표 기준(1920x1080)의 비율

    RectTransform target; // 16:9로 맞출 화면 (GameFlow의 root)
    RectTransform area;   // target이 들어 있는 곳 (캔버스)

    public static void Attach(RectTransform target)
    {
        if (target == null) return;
        var go = new GameObject("StageFit");
        go.transform.SetParent(target, false);
        var f = go.AddComponent<StageFit>();
        f.target = target;
        f.area = target.parent as RectTransform;
        f.Apply();
    }

    void LateUpdate() { Apply(); } // 플레이 중에 창 크기를 바꿔도 따라간다

    void Apply()
    {
        if (target == null || area == null) return;
        Vector2 size = area.rect.size;
        if (size.x <= 0f || size.y <= 0f) return;
        float w = size.x, h = size.y;
        if (w / h > Aspect) w = h * Aspect; else h = w / Aspect;
        target.anchorMin = new Vector2(0.5f, 0.5f);
        target.anchorMax = new Vector2(0.5f, 0.5f);
        target.sizeDelta = new Vector2(w, h);
        target.anchoredPosition = Vector2.zero;
    }

    void OnDisable()
    {
        if (target == null) return;
        target.anchorMin = Vector2.zero;
        target.anchorMax = Vector2.one;
        target.sizeDelta = Vector2.zero;
        target.anchoredPosition = Vector2.zero;
    }
}
