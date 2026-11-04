재료 그림을 여기에 넣는다. 재료당 5장, 파일 이름 규칙: <id>_<단계>.png
단계: raw(날것), under(적게 익힘), good(알맞음), over(많이 익힘), burnt(탄 것)
id: egg, tofu, onion, potato, pork, kimchi  (5장이 모두 있어야 표시됨)
장보기·조리 대기열 아이콘: <id>_shop.png (조리 중 5단계 그림과는 다른 그림)
자세한 규격은 docs/13아트가이드.md 참고.

장보기: bg_shop.png(배경), shop_basket_back.png / shop_basket_front.png(장바구니 뒤·앞, 재료가 그 사이에 끼인다). 글자·표는 그림에 넣지 않는다.
조리: bg_cook_grill.png / bg_cook_wok.png / bg_cook_pot.png(방식별 배경: 팬·웍·냄비 + 도마 + 접시),
      cook_burner_bar.png(불 조절·조작 바), cook_fire_on.png / cook_fire_off.png(불 세기 아이콘),
      cook_flip_btn.png(뒤집기 버튼), cook_timer.png(타이머), cook_tip.png(안내 말풍선)

조리 도구 그림 (선택): Assets/Resources/Art/Kitchen/<이름>.png
없으면 게임이 코드로 그린 그림을 쓴다. 넣을 때는 Sprite로 임포트하고 아래 크기 비율을 맞춘다.
- tablecloth.png (식탁보, 반복되는 무늬 한 칸)
- cutting_board.png (도마, 640x200 비율, 오른쪽 손잡이)
- knob.png (불 조절 밸브, 정사각형, 위쪽이 가리키는 방향)
- pan.png / pot.png / plate.png / spatula.png / tongs.png / lid.png / flame.png (지금 조리 화면은 위 bg_cook 그림을 써서 쓰이지 않는다)
