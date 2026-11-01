# 재료별 투입 타이밍 조리 구현 계획

> 실행: 한 세션에서 인라인으로 진행한다(코드가 `Cook.cs`와 `GameFlow.cs` 조리 구간에 모여 있어 작업을 나누면 충돌한다). 설계는 `docs/superpowers/specs/2026-11-02-ingredient-timing-cook-design.md` 참고.

**목표:** 재료를 하나씩 팬에 넣고 하나씩 꺼내는 조리 화면.
**구조:** `Cook`이 재료별 상태(`CookItem`: 트레이/팬/접시, 익힘 값, 판정)를 갖고 화력·방식 동작은 팬 전체에 공유한다. `GameFlow`는 재료 아이템 하나를 상태에 따라 트레이·팬·접시 자리로 옮겨 그린다.
**기술:** Unity 6, uGUI(코드로 생성), 새 Input System.

## 전역 제약
- 재료 최대 2개, 제한시간 `Balance.MaxCookTime`(45초), 5단계와 방식별 적정 구간은 기존 `Cook.Stage` 기준 그대로.
- 수치는 `Data.cs`에서만 고친다. 재료 익힘 배율은 기존 `Ingredient.speedMod`를 쓴다.
- 키: 1/2/3 화력, Z/X 재료, Space·Enter 팬의 재료 모두 꺼내기.

## Task 1: 재료별 상태와 판정 (`Cook.cs`, `Data.cs`)
- `CookItem { Ingredient ing; Spot spot; float d, hiBonus; bool ok; string fail; float coef; }`, `enum Spot { Tray, Pan, Plate }`.
- `Cook`: `Put(i)`, `TakeOut(i)`(접시에 올리며 판정 확정), `TakeOutAll()`, `Tick`(팬의 재료만 익힘, 100이 되면 그 재료만 자동으로 접시에 올라 탄 것으로 확정, 시간 종료 시 팬의 재료 모두 꺼냄), `Stage(item)`, `Tone(item)`, `Used`, `GoodIngredients`, `Satisfaction()`, `FailText`.
- `Data.cs`: `speedMod`를 달걀 1.4, 두부 1.0, 양파 0.9, 감자 0.6, 돼지고기 0.7, 김치 0.8로 조정.
- 검증: 컴파일 오류 0, eval 스크립트로 경계(넣기 전 익지 않음, 재료별 속도 차, 접시 판정, 시간 종료) 확인.

## Task 2: 조리 화면과 입력 (`GameFlow.cs`)
- 재료 아이템을 상태에 따라 트레이(아래 왼쪽) / 팬(가운데) / 접시(아래 오른쪽) 자리에 배치, 클릭과 Z/X로 넣기·꺼내기, 모두 꺼내기 버튼.
- `FinishCook`을 재료별 결과로 교체(일부 실패 시 식사 화면에 안내, 전부 실패하거나 아무것도 안 만들면 굶음).
- 검증: 컴파일, Play에서 키 입력 주입으로 2재료 조리(감자 먼저, 달걀 나중), 부분 실패, 시간 종료, 첫날 가이드를 확인.

## Task 3: 문서와 마무리
- README 조작 키 표, `docs/13아트가이드.md`(조리 화면 구성), `docs/12` 현황을 고친다. 커밋하고 push한다.
