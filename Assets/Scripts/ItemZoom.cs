using System.Collections.Generic;
using UnityEngine;

// 팬·냄비 안 재료 크게 보기. GameFlow가 재료 자리(앵커)를 잡은 뒤, 팬 안에 있는 재료만 개수에 맞게 키운다.
// 자리 계산은 건드리지 않고 크기(localScale)만 바꾸므로, 끌기·클릭·게이지는 그대로 같이 커진다.
// GameFlow.StartCook 끝에서 Attach 한 줄로 붙인다.
public class ItemZoom : MonoBehaviour
{
    public static void Attach(Transform root, Cook cook, List<RectTransform> items, List<DragItem> drags)
    {
        if (cook == null) return;
        var go = new GameObject("ItemZoom", typeof(RectTransform));
        go.transform.SetParent(root, false);
        var z = go.AddComponent<ItemZoom>();
        z.cook = cook;
        z.items = items;
        z.drags = drags;
    }

    Cook cook;
    List<RectTransform> items;
    List<DragItem> drags;

    // 팬 안 재료 수에 따른 배율. 칸을 나눠 쓰므로 많을수록 덜 키워서 서로 겹치지 않게 한다
    static float Zoom(int inPan)
    {
        return inPan <= 1 ? 1.9f : inPan == 2 ? 1.35f : inPan <= 4 ? 1.3f : 1.1f;
    }

    // GameFlow.Update에서 자리를 잡은 다음 프레임 끝에 크기를 맞춘다
    void LateUpdate()
    {
        if (cook == null || items == null) return;
        int inPan = 0;
        foreach (var it in cook.items) if (it.spot == Spot.Pan) inPan++;
        float z = Zoom(inPan);
        for (int i = 0; i < items.Count && i < cook.items.Length; i++)
        {
            if (items[i] == null) continue;
            if (i < drags.Count && drags[i] != null && drags[i].dragging) continue; // 끄는 중에는 끌기 연출 크기를 둔다
            float s = cook.items[i].spot == Spot.Pan ? z : 1f;
            items[i].localScale = new Vector3(s, s, 1f);
        }
    }
}
