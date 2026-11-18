using UnityEngine;

// 재료 한 가지의 수치와 색을 담는 에셋. 인스펙터에서 바로 고치고, 게임은 실행할 때 Data.cs가 이 에셋을 읽어 Ingredient로 만든다.
// 파일 위치: Assets/Resources/Data/Ingredients/ (한 재료에 에셋 하나)
[CreateAssetMenu(menuName = "COOKER/Ingredient", fileName = "Ingredient")]
public class IngredientData : ScriptableObject
{
    public int order;            // 장보기와 도감에서 보이는 순서
    public string ingredientName; // 재료 이름. 도감 저장과 사진 키에 쓰이므로 바꾸면 기록이 깨진다 (CLAUDE.md)
    public int price;            // 1끼분 기준 실제 소매가 근사 (docs/14)
    public int shelf;            // 구매일 포함 냉장 보관 일수 (실제 소비기한 근사)
    public Method[] fits;        // 적합 방식
    public int baseScore;
    public float speedMod;
    public Color raw, cooked, burnt;

    public Ingredient ToIngredient()
    {
        return new Ingredient
        {
            name = ingredientName, price = price, shelf = shelf, fits = fits, baseScore = baseScore, speedMod = speedMod,
            raw = raw, cooked = cooked, burnt = burnt
        };
    }
}
