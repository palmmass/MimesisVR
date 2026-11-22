namespace UnityVRMod.Mimesis
{
    /// <summary>Built-in Korean texts (build 48). Generated from Lang/ko.txt - edit that file, then run Lang/make_langko.py.</summary>
    internal static class LangKo
    {
        public const string Text = @"# MIMESIS VR - Korean (machine translation; corrections are welcome).
#name: 한국어

@ Controls
= 조작

@ Buttons
= 버튼

@ Hands & items
= 손과 아이템

@ Interface
= 인터페이스

@ Left watch
= 왼쪽 시계

@ Right watch
= 오른쪽 시계

@ Graphics
= 그래픽

@ Performance
= 성능

@ Comfort
= 편의

@ Service
= 서비스

@ Test (host)
= 테스트 (호스트)

@ Turning
= 회전

@ How the right stick turns you.
= 오른쪽 스틱으로 회전하는 방식입니다.

@ Snap
= 스냅

@ Smooth
= 부드럽게

@ Looking down does not move the view
= 아래를 봐도 시야가 움직이지 않음

@ The game moves its camera forward and down when you look down (made for the flat screen to show the feet). In VR that shifts the whole view and spins it when you look straight down. On: the view moves only with your head.
= 게임은 아래를 볼 때 카메라를 앞쪽 아래로 옮깁니다(평면 화면에서 발이 보이도록 만든 기능). VR에서는 전체 시야가 밀리고, 바로 아래를 보면 시야가 돌아갑니다. 켜짐: 시야는 머리 움직임으로만 움직입니다.

@ Off
= 끔

@ On
= 켬

@ Jump: view lift
= 점프: 시야 상승

@ In a jump the game lifts the camera (about 65 cm) on top of the body animation; without it the view hardly rises. 100% = as in the game, 0% = the view stays. Pick the value where your arms and the body's shadow jump together.
= 점프할 때 게임은 몸 애니메이션 위에 카메라를 추가로 올립니다(약 65cm). 이것이 없으면 시야가 거의 올라가지 않습니다. 100% = 게임과 동일, 0% = 시야 고정. 팔과 몸의 그림자가 함께 뛰는 값을 고르세요.

@ Walking in the room moves the character
= 방 안에서 걸으면 캐릭터도 이동

@ When you physically step around, the character comes with you (walls stop it as in the game). Off: the view moves away from the body.
= 실제로 걸어 다니면 캐릭터도 함께 이동합니다(게임처럼 벽에 막힘). 끔: 시야가 몸에서 멀어집니다.

@ Walking in the room: legs walk
= 방 안에서 걷기: 다리 움직임

@ The character's legs walk (and footsteps sound) when you walk in the room, instead of sliding.
= 방 안에서 걸을 때 캐릭터의 다리가 미끄러지지 않고 걸으며 발소리도 납니다.

@ Snap angle
= 스냅 각도

@ Angle of one snap turn.
= 한 번의 스냅 회전 각도입니다.

@ Smooth turn speed
= 부드러운 회전 속도

@ Turn speed with the stick fully pushed.
= 스틱을 끝까지 밀었을 때의 회전 속도입니다.

@ Stick dead zone
= 스틱 데드존

@ Small stick movements below this are ignored (walking).
= 이보다 작은 스틱 움직임은 무시합니다(걷기).

@ Walk direction
= 걷는 방향

@ Stick forward walks where you look, or where the controller of the move stick points.
= 스틱을 앞으로 밀면 바라보는 방향으로, 또는 이동 스틱 컨트롤러가 가리키는 방향으로 걷습니다.

@ Head
= 머리

@ Left controller
= 왼쪽 컨트롤러

@ Sprint
= 달리기

@ The Run button (Buttons tab) toggles sprint, or sprint while it is held.
= 달리기 버튼(버튼 탭)을 누르면 달리기가 켜지고 꺼지거나, 누르고 있는 동안만 달립니다.

@ Click to toggle
= 눌러서 전환

@ Hold
= 누르고 있기

@ Item follows controller
= 아이템이 컨트롤러를 따라감

@ The item in your hand follows the right controller (only in the headset picture). Key F3.
= 손에 든 아이템이 오른쪽 컨트롤러를 따라갑니다(헤드셋 화면에서만). 단축키 F3.

@ Item grip
= 아이템 잡는 방향

@ How the item is oriented in the hand. Key PageDown.
= 손에 든 아이템의 방향입니다. 단축키 PageDown.

@ Auto (long axis)
= 자동 (긴 축)

@ Like controller
= 컨트롤러와 같게

@ Along pointer
= 포인터 방향

@ Fingers on an item
= 아이템을 쥔 손가락

@ Game: the fingers take the game's own hold pose for each item (as in the original). Mod: a fist, set by 'Fingers: grip on an item'.
= 게임: 손가락이 아이템마다 게임 자체의 쥐는 자세를 취합니다(원작과 같음). 모드: '손가락: 아이템 쥐기'로 설정한 주먹입니다.

@ Game
= 게임

@ Mod
= 모드

@ Hide item until it is in the hand
= 손에 들기 전까지 아이템 숨기기

@ When switching items the game first puts the new item elsewhere (Hands = Character arms); it is hidden until it is in the hand.
= 아이템을 바꿀 때 게임은 새 아이템을 먼저 다른 곳에 둡니다(손 = 캐릭터 팔). 손에 들어올 때까지 숨깁니다.

@ Melee hit
= 근접 공격

@ Button: the trigger, as in the game. Swing: a fast movement of the right hand starts the hit (the game's wind-up delay and pace stay, so you cannot hit faster than with the button). Both: either.
= 버튼: 게임처럼 트리거로 공격합니다. 휘두르기: 오른손을 빠르게 움직이면 공격이 시작됩니다(게임의 준비 동작과 공격 속도는 그대로라 버튼보다 빨리 칠 수 없습니다). 둘 다: 어느 쪽이든 됩니다.

@ Button
= 버튼

@ Swing
= 휘두르기

@ Both
= 둘 다

@ Swing: hand speed
= 휘두르기: 손 속도

@ How fast the hand must move to count as a swing. Raise it if hits start by accident, lower it if swings are missed.
= 휘두르기로 인정되는 손의 속도입니다. 실수로 공격이 나가면 올리고, 휘둘러도 안 되면 내리세요.

@ Swing: vibration
= 휘두르기: 진동

@ Medium pulse on the swing, strong when the hit lands, medium when it misses, soft when you can hit again.
= 휘두를 때 중간 진동, 명중하면 강한 진동, 빗나가면 중간 진동, 다시 칠 수 있게 되면 약한 진동.

@ Test vibration
= 진동 테스트

@ Four pulses on the right controller, about a second apart: 1 - default, 2 - 160 Hz, 3 - 320 Hz, 4 - shortest. Tell which ones you felt.
= 오른쪽 컨트롤러에 약 1초 간격으로 진동 네 번: 1 - 기본, 2 - 160Hz, 3 - 320Hz, 4 - 가장 짧게. 어떤 것이 느껴졌는지 확인하세요.

@ Vibration frequency
= 진동 주파수

@ Frequency used for all pulses. Default lets the headset choose; if it does not vibrate, try 160 or 320 Hz.
= 모든 진동에 쓰는 주파수입니다. 기본은 헤드셋이 고릅니다. 진동이 없으면 160 또는 320Hz를 시도하세요.

@ Default
= 기본

@ Item actions direction
= 아이템 사용 방향

@ Hits, shots and other item actions go where the right hand points, or where you look (as in the game). Other players briefly see your character turn that way.
= 공격, 사격 등 아이템 사용이 오른손이 가리키는 곳 또는 바라보는 곳(게임과 같음)으로 향합니다. 다른 플레이어에게는 캐릭터가 잠깐 그쪽으로 도는 것이 보입니다.

@ Hand
= 손

@ Gaze
= 시선

@ Interaction
= 상호작용

@ Pick up and use things with the right hand pointer or with your gaze. Key End.
= 오른손 포인터 또는 시선으로 물건을 줍고 사용합니다. 단축키 End.

@ Hand (pointer)
= 손 (포인터)

@ Pointer opacity
= 포인터 불투명도

@ How visible the pointer is when it is not on something usable (on a usable thing it always lights up). 0% = hidden.
= 사용할 수 있는 대상 위가 아닐 때 포인터가 보이는 정도입니다(사용 가능한 대상 위에서는 항상 밝아집니다). 0% = 숨김.

@ Pointer style
= 포인터 모양

@ Short: a short beam from the hand and a dot where it points. Full: the whole beam.
= 짧게: 손에서 나오는 짧은 빔과 가리키는 곳의 점. 전체: 빔 전체.

@ Short + dot
= 짧은 빔 + 점

@ Full beam
= 전체 빔

@ Short pointer length
= 짧은 포인터 길이

@ Length of the short beam from the hand (Pointer style = Short).
= 손에서 나오는 짧은 빔의 길이입니다(포인터 모양 = 짧게).

@ Hands
= 손

@ Character: the character's own arms follow the controllers (the body is then shown). Bars: grey bars. None: nothing.
= 캐릭터: 캐릭터 자신의 팔이 컨트롤러를 따라갑니다(이때 몸이 표시됨). 막대: 회색 막대. 없음: 아무것도 표시하지 않음.

@ Bars
= 막대

@ Character arms
= 캐릭터 팔

@ None
= 없음

@ Bars visible
= 막대 표시

@ Grey bars where your controllers are (when Hands = Bars).
= 컨트롤러 위치에 회색 막대를 표시합니다(손 = 막대일 때).

@ Show body
= 몸 표시

@ Show your character's body in the headset. Key F4.
= 헤드셋에서 캐릭터의 몸을 표시합니다. 단축키 F4.

@ Item in hand: back/forward
= 손에 든 아이템: 뒤/앞

@ Correction for the item in your right hand now (Hands = Character arms). Saved separately for each item.
= 지금 오른손에 든 아이템의 위치 보정입니다(손 = 캐릭터 팔). 아이템마다 따로 저장됩니다.

@ Item in hand: down/up
= 손에 든 아이템: 아래/위

@ Item in hand: left/right
= 손에 든 아이템: 왼쪽/오른쪽

@ Item in hand: tilt (barrel down)
= 손에 든 아이템: 기울기 (총구 아래로)

@ Item in hand: turn left/right
= 손에 든 아이템: 좌우 회전

@ Item in hand: roll
= 손에 든 아이템: 롤

@ Item in hand: reset grip
= 손에 든 아이템: 보정 초기화

@ Removes the correction of the item in your right hand now.
= 지금 오른손에 든 아이템의 보정을 지웁니다.

@ Item in hand: base pose
= 손에 든 아이템: 기본 자세

@ Model: the item sits in the hand as in the character model (does not move with the game animation). Game, fixed: as the game holds it, taken 0.6 s after the item is in hand and then kept. Game: as the game animates it (no fix).
= 모델: 아이템이 캐릭터 모델 그대로 손에 놓입니다(게임 애니메이션을 따라 움직이지 않음). 게임, 고정: 게임이 드는 방식대로, 손에 든 지 0.6초 뒤의 자세를 그대로 유지합니다. 게임: 게임 애니메이션 그대로(고정 없음).

@ Model
= 모델

@ Game, fixed
= 게임, 고정

@ Finger rest pose
= 손가락 기본 자세

@ Model: fingers start from the model's own pose (stable). Animation: from the game animation at load (old behaviour, could bend fingers the wrong way).
= 모델: 손가락이 모델 자체의 자세에서 시작합니다(안정적). 애니메이션: 로드 시 게임 애니메이션에서 시작합니다(이전 방식, 손가락이 반대로 굽을 수 있음).

@ Animation (old)
= 애니메이션 (이전)

@ Interface in headset
= 헤드셋의 인터페이스

@ Show the game interface in the headset. Key Home.
= 헤드셋에 게임 인터페이스를 표시합니다. 단축키 Home.

@ Interface on hands
= 손의 인터페이스

@ Inventory on the left wrist, hints at the pointer. Key Insert.
= 왼쪽 손목에 인벤토리, 포인터 옆에 안내를 표시합니다. 단축키 Insert.

@ Hand inventory place
= 손 인벤토리 위치

@ Above the left controller facing you, or lying on the wrist.
= 왼쪽 컨트롤러 위에 나를 향하게, 또는 손목 위에 눕혀서 표시합니다.

@ Above the controller
= 컨트롤러 위

@ On the wrist
= 손목 위

@ Wrist inventory size
= 손목 인벤토리 크기

@ Width of the inventory panel on the left wrist.
= 왼쪽 손목 인벤토리 패널의 너비입니다.

@ Hand inventory: left/right
= 손 인벤토리: 왼쪽/오른쪽

@ Moves the hand inventory left (-) or right (+).
= 손 인벤토리를 왼쪽(-) 또는 오른쪽(+)으로 옮깁니다.

@ Hand inventory: down/up
= 손 인벤토리: 아래/위

@ Moves the hand inventory down (-) or up (+).
= 손 인벤토리를 아래(-) 또는 위(+)로 옮깁니다.

@ Hand inventory: closer/further
= 손 인벤토리: 가까이/멀리

@ Moves the hand inventory towards you (-) or away (+).
= 손 인벤토리를 나에게 가깝게(-) 또는 멀리(+) 옮깁니다.

@ Hand inventory: tilt
= 손 인벤토리: 기울기

@ Tilts the hand inventory top away (+) or towards you (-).
= 손 인벤토리의 윗부분을 바깥쪽(+) 또는 나를 향해(-) 기울입니다.

@ Frame counter on the wrist
= 손목 프레임 카운터

@ Frames per second next to the wrist gear: green - the headset rate is kept, yellow - a bit below, red - far below.
= 손목 톱니바퀴 옆에 초당 프레임을 표시합니다: 초록 - 헤드셋 주사율 유지, 노랑 - 약간 낮음, 빨강 - 많이 낮음.

@ Own wrist panel
= 자체 손목 패널

@ The mod draws its own panel on the left hand: the slots with item pictures and the same numbers as in the game; health, contamination and stamina are on the right watch. Off: the pieces cut out of the game screen (as before).
= 모드가 왼손에 자체 패널을 그립니다: 아이템 그림과 게임과 같은 숫자가 있는 슬롯. 체력, 오염, 스태미나는 오른쪽 시계에 있습니다. 끔: 게임 화면에서 잘라낸 조각을 표시합니다(이전 방식).

@ Wrist panel: visibility
= 손목 패널: 표시

@ When you look at it: half transparent until you look at your hand. Always: always fully visible.
= 볼 때: 손을 볼 때까지 반투명. 항상: 항상 완전히 표시.

@ When you look at it
= 볼 때

@ Always
= 항상

@ Wrist panel: when not looking
= 손목 패널: 보지 않을 때

@ How visible the panel stays while you do not look at it (0% = hidden).
= 보지 않을 때 패널이 보이는 정도입니다(0% = 숨김).

@ Wrist panel: attached to
= 손목 패널: 부착 위치

@ Controller: above the left hand, as before (positioned by the 'Hand inventory' settings). Forearm: on the left watch on the character's forearm (Hands = Character arms; positioned on the 'Left watch' tab). On the forearm the bag slots go to the left of the main ones, the gear and frame counter further left.
= 컨트롤러: 이전처럼 왼손 위('손 인벤토리' 설정으로 위치 조정). 팔뚝: 캐릭터 팔뚝의 왼쪽 시계 위(손 = 캐릭터 팔, '왼쪽 시계' 탭에서 위치 조정). 팔뚝에서는 가방 슬롯이 기본 슬롯 왼쪽에, 톱니바퀴와 프레임 카운터는 그보다 더 왼쪽에 놓입니다.

@ Controller
= 컨트롤러

@ Forearm
= 팔뚝

@ Key tips (top right)
= 키 안내 (오른쪽 위)

@ Hints about keys and the item in hand: on the right hand (like the inventory on the left), on the screen as in the game, or hidden.
= 키와 손에 든 아이템에 대한 안내: 오른손 위(왼쪽 인벤토리처럼), 게임처럼 화면에, 또는 숨김.

@ On the right hand
= 오른손 위

@ On the screen
= 화면에

@ Hidden
= 숨김

@ Key tips: left/right
= 키 안내: 왼쪽/오른쪽

@ Position on the right hand.
= 오른손 위의 위치입니다.

@ Key tips: down/up
= 키 안내: 아래/위

@ Key tips: closer/further
= 키 안내: 가까이/멀리

@ Key tips: tilt
= 키 안내: 기울기

@ Tilt of the tips panel.
= 안내 패널의 기울기입니다.

@ Key tips: size
= 키 안내: 크기

@ Size relative to the inventory scale.
= 인벤토리 크기에 대한 상대 크기입니다.

@ Tips at the bottom right (tram)
= 오른쪽 아래 안내 (트램)

@ The hints at the bottom right of the screen (tram and similar): hidden or on the screen as in the game.
= 화면 오른쪽 아래의 안내(트램 등): 숨기거나 게임처럼 화면에 표시합니다.

@ Pointer hint size
= 포인터 안내 크기

@ Size of the crosshair and interaction hint at the end of the pointer.
= 포인터 끝의 조준점과 상호작용 안내의 크기입니다.

@ Interface distance
= 인터페이스 거리

@ Distance of the interface in front of your eyes (its apparent size stays the same).
= 눈앞 인터페이스의 거리입니다(보이는 크기는 그대로).

@ Interface size
= 인터페이스 크기

@ Size of the interface in front of your eyes.
= 눈앞 인터페이스의 크기입니다.

@ Menu distance
= 메뉴 거리

@ Distance of the game menu panel. In a menu the right stick also moves it.
= 게임 메뉴 패널의 거리입니다. 메뉴에서는 오른쪽 스틱으로도 옮길 수 있습니다.

@ Menu backdrop
= 메뉴 배경

@ Darkness of the backing behind game menus.
= 게임 메뉴 뒤 배경의 어두운 정도입니다.

@ Dark screens of the game
= 게임의 어두운 화면

@ Full-screen dark overlays of the game (hints, fades, black screens) are made no darker than this. 0% = removed.
= 게임의 전체 화면 어두운 오버레이(안내, 페이드, 검은 화면)를 이 값보다 어둡지 않게 합니다. 0% = 제거.

@ Move the watches and screens by hand
= 시계와 화면을 손으로 옮기기

@ While on, the watch bodies glow: grip a body or its screen with the other hand and move and turn it as you like; let go - it stays, the values below follow. The screen keeps its place on the body. Not saved (off at the next start).
= 켜져 있는 동안 시계 본체가 빛납니다: 다른 손으로 본체나 화면을 잡고 원하는 대로 옮기고 돌리세요. 놓으면 그 자리에 남고 아래 값이 따라 바뀝니다. 화면은 본체 위 자기 자리를 유지합니다. 저장되지 않습니다(다음 실행 시 꺼짐).

@ Watch body
= 시계 본체

@ The body under the screen: the lid of the game's compass with the game's own texture. Off: only the screen.
= 화면 아래의 본체: 게임 자체 텍스처를 쓴 게임 나침반 뚜껑. 끔: 화면만 표시.

@ Fit
= 맞춤

@ Sizes as set; or the body around the screen (its length / width are then ignored); or the screen as large as fits on the body ('Screen: size' is then ignored).
= 설정한 크기 그대로, 또는 화면에 맞춘 본체(본체 길이/너비는 무시됨), 또는 본체에 들어가는 최대 크기의 화면('화면: 크기'는 무시됨).

@ As set
= 설정대로

@ Body to the screen
= 본체를 화면에

@ Screen to the body
= 화면을 본체에

@ Bag slots in a second row
= 가방 슬롯을 두 번째 줄에

@ On the watch the bag slots go to a row under the main ones - only while you have a bag, otherwise the panel is one row.
= 시계에서 가방 슬롯을 기본 슬롯 아래 줄에 놓습니다. 가방이 있을 때만이며, 없으면 패널은 한 줄입니다.

@ Screen backing
= 화면 배경

@ How dark the backing behind the screen is (0% = none).
= 화면 뒤 배경의 어두운 정도입니다(0% = 없음).

@ Screen: turns to the eyes
= 화면: 눈 쪽으로 회전

@ The model's elbow does not rise when you bring the hand to the eyes, so the watch stays steep: the screen turns towards your eyes by this share, the body stays on the arm. 0% = flat on the body.
= 손을 눈으로 가져가도 모델의 팔꿈치가 올라가지 않아 시계가 가파르게 남습니다. 화면이 이 비율만큼 눈 쪽으로 돌고 본체는 팔에 남습니다. 0% = 본체에 평평하게.

@ Watch face
= 시계 문자판

@ A dark eight-sided face over the whole top of the body, so the screen does not sit on bare wood (0% = none). Also seen on the monitor.
= 본체 윗면 전체를 덮는 어두운 팔각형 문자판으로, 화면이 나무 위에 그대로 놓이지 않게 합니다(0% = 없음). 모니터에서도 보입니다.

@ Watch shadow
= 시계 그림자

@ The body casts a shadow (a little extra work for the video card).
= 본체가 그림자를 드리웁니다(그래픽 카드 부담이 조금 늘어남).

@ Watch: turns with the skin
= 시계: 피부와 함께 회전

@ How much the watch turns when you turn the hand: 100% = exactly like the forearm skin under it (no edges through the watch), 0% = not at all.
= 손을 돌릴 때 시계가 도는 정도입니다: 100% = 아래 팔뚝 피부와 똑같이(시계를 뚫고 나오는 모서리 없음), 0% = 돌지 않음.

@ Watch: shift along the arm
= 시계: 팔을 따라 이동

@ Moves the watch along the forearm (+ = towards the hand).
= 시계를 팔뚝을 따라 옮깁니다(+ = 손 쪽으로).

@ Watch: shift across the arm
= 시계: 팔을 가로질러 이동

@ Moves the watch to the side of the forearm.
= 시계를 팔뚝 옆쪽으로 옮깁니다.

@ Watch: height above the arm
= 시계: 팔 위 높이

@ Lifts the watch off the arm (+) or lowers it into it (-).
= 시계를 팔에서 띄우거나(+) 팔 안으로 내립니다(-).

@ Watch: turn on the arm
= 시계: 팔 위에서 회전

@ Turns the watch flat on the arm (around the axis out of the arm).
= 시계를 팔 위에서 평평하게 돌립니다(팔에서 바깥으로 나가는 축 기준).

@ Watch: tilt along the arm
= 시계: 팔 방향 기울기

@ Tilts the watch: the end towards the hand up (+) or down (-).
= 시계를 기울입니다: 손 쪽 끝을 위로(+) 또는 아래로(-).

@ Watch: tilt across the arm
= 시계: 팔 가로 기울기

@ Tilts the watch sideways (around the arm). 180 turns it over.
= 시계를 옆으로 기울입니다(팔을 축으로). 180이면 뒤집힙니다.

@ Watch: length
= 시계: 길이

@ Size of the body along the arm.
= 팔 방향 본체 크기입니다.

@ Watch: width
= 시계: 너비

@ Size of the body across the arm.
= 팔을 가로지르는 본체 크기입니다.

@ Watch: thickness
= 시계: 두께

@ Thickness of the body.
= 본체의 두께입니다.

@ Screen: shift along
= 화면: 세로 이동

@ Moves the screen on the watch along the arm.
= 시계 위의 화면을 팔 방향으로 옮깁니다.

@ Screen: shift across
= 화면: 가로 이동

@ Moves the screen on the watch across the arm.
= 시계 위의 화면을 팔을 가로질러 옮깁니다.

@ Screen: height above the watch
= 화면: 시계 위 높이

@ Distance from the top of the watch body.
= 시계 본체 윗면에서의 거리입니다.

@ Screen: turn
= 화면: 회전

@ Turns the screen in its plane (+ = counterclockwise). 90 / -90 = along the arm.
= 화면을 평면 안에서 돌립니다(+ = 반시계 방향). 90 / -90 = 팔 방향.

@ Screen: tilt
= 화면: 기울기

@ Tilts the screen towards your eyes. 180 flips it if it faces the wrong way.
= 화면을 눈 쪽으로 기울입니다. 반대쪽을 향하면 180으로 뒤집으세요.

@ Screen: tilt sideways
= 화면: 옆 기울기

@ Tilts the screen around its other axis.
= 화면을 다른 축을 기준으로 기울입니다.

@ Screen: size
= 화면: 크기

@ Size of the wrist panel on the watch (x1 = the 'Wrist inventory size' on the Interface tab).
= 시계 위 손목 패널의 크기입니다(x1 = 인터페이스 탭의 '손목 인벤토리 크기').

@ Health, contamination, stamina
= 체력, 오염, 스태미나

@ On the right watch: the mod's own live picture (pulse, health in quarters, contamination tube with bubbles, stamina). On the screen: the game's own elements, as in the game.
= 오른쪽 시계에: 모드 자체의 실시간 그림(맥박, 4단계 체력, 거품이 있는 오염 튜브, 스태미나). 화면에: 게임 자체 요소를 게임처럼 표시.

@ On the right watch
= 오른쪽 시계에

@ Size of the screen (x1 = 6 cm wide).
= 화면 크기입니다(x1 = 너비 6cm).

@ Settings gear
= 설정 톱니바퀴

@ Where the gear that opens these settings is. On the left: next to the panel, at the elbow end of the left watch (shows when you look at it) or in its own cell at the start of the panel - press with the right hand. On the right: at the elbow end of the right watch (shows when you look at it) or in the corner of the right screen - press with the left hand.
= 이 설정을 여는 톱니바퀴의 위치입니다. 왼쪽: 패널 옆, 왼쪽 시계의 팔꿈치 쪽 끝(볼 때 나타남), 또는 패널 시작 부분의 별도 칸 - 오른손으로 누릅니다. 오른쪽: 오른쪽 시계의 팔꿈치 쪽 끝(볼 때 나타남) 또는 오른쪽 화면 모서리 - 왼손으로 누릅니다.

@ Left, next to the panel
= 왼쪽, 패널 옆

@ Right watch, end
= 오른쪽 시계, 끝

@ Right screen, corner
= 오른쪽 화면, 모서리

@ Left watch, end
= 왼쪽 시계, 끝

@ Left panel, own cell
= 왼쪽 패널, 별도 칸

@ Left panel, corner
= 왼쪽 패널, 모서리

@ Headset view on monitor
= 모니터에 헤드셋 화면

@ Show the headset picture (one eye) with the interface on the monitor. Key F1.
= 인터페이스를 포함한 헤드셋 화면(한쪽 눈)을 모니터에 표시합니다. 단축키 F1.

@ Monitor: eye
= 모니터: 눈

@ Which eye's picture the monitor (headset view, F1) shows.
= 모니터(헤드셋 화면, F1)에 표시할 눈입니다.

@ Left
= 왼쪽

@ Right
= 오른쪽

@ Monitor: interface for viewers
= 모니터: 시청자용 인터페이스

@ What the monitor (headset view, F1) shows of the watch screens and the wrist panel. Corners: health bottom left, inventory bottom middle, always readable. In the world: where the player sees them (with the gear and the laser).
= 모니터(헤드셋 화면, F1)에 시계 화면과 손목 패널을 어떻게 표시할지 정합니다. 모서리: 체력은 왼쪽 아래, 인벤토리는 아래 가운데에 항상 읽기 쉽게. 월드 안: 플레이어가 보는 위치 그대로(톱니바퀴와 레이저 포함).

@ Corners
= 모서리

@ In the world
= 월드 안

@ Monitor: corner picture size
= 모니터: 모서리 그림 크기

@ Height of the health picture as a share of the monitor height.
= 모니터 높이에 대한 체력 그림의 높이 비율입니다.

@ Screen effects (rain, smudge)
= 화면 효과 (비, 얼룩)

@ Effects the game draws on the screen: on the eyes like on a lens, off, or in the interface panel as before.
= 게임이 화면에 그리는 효과: 렌즈처럼 눈 위에, 끔, 또는 이전처럼 인터페이스 패널에.

@ On the eyes
= 눈 위에

@ In interface (old)
= 인터페이스에 (이전)

@ Rain on the lens
= 렌즈의 빗방울

@ The game's rain drops on the screen, drawn on the eyes. Off by default: in the headset they looked like a black sheet with holes; the rain itself is in the world anyway.
= 화면의 게임 빗방울을 눈 위에 그립니다. 기본값은 끔: 헤드셋에서는 구멍 난 검은 천처럼 보였고, 비 자체는 어차피 월드에 있습니다.

@ Noise on game panels
= 게임 패널의 노이즈

@ The game's noise overlay on its interface panels (tips, health). Off by default: in the headset it gave dark backings.
= 게임 인터페이스 패널(안내, 체력)의 노이즈 오버레이입니다. 기본값은 끔: 헤드셋에서 어두운 배경이 생겼습니다.

@ Sharpening
= 샤프닝

@ Game sharpening filter (Beautify). Strong sharpening adds grain in dark places in VR.
= 게임의 샤프닝 필터(Beautify)입니다. 강한 샤프닝은 VR에서 어두운 곳에 노이즈를 더합니다.

@ As in game
= 게임과 동일

@ Reduced
= 약하게

@ Lens dirt
= 렌즈 먼지

@ Dirty-lens glow around bright lights (Beautify).
= 밝은 빛 주변의 더러운 렌즈 빛 번짐 효과(Beautify)입니다.

@ Posterize filter
= 포스터화 필터

@ The game's colour posterize / dither filter.
= 게임의 색상 포스터화 / 디더 필터입니다.

@ Preset
= 프리셋

@ Applies a ready set of the options below (from Very low to High = as in game); any option can then be changed separately.
= 아래 옵션의 준비된 조합을 적용합니다(매우 낮음부터 높음 = 게임과 동일까지). 이후 각 옵션을 따로 바꿀 수 있습니다.

@ Custom
= 사용자 지정

@ Very low
= 매우 낮음

@ Low
= 낮음

@ Medium
= 보통

@ Good
= 좋음

@ High (as game)
= 높음 (게임과 동일)

@ Picture on the monitor
= 모니터 화면

@ Off: the game does not draw its picture for the monitor while you are in VR (a big saving). The monitor shows a note, or the headset view with F1.
= 끔: VR 중에는 게임이 모니터용 화면을 그리지 않습니다(큰 절약). 모니터에는 안내 문구가 보이며, F1로 헤드셋 화면을 볼 수 있습니다.

@ Headset resolution
= 헤드셋 해상도

@ Share of the headset's recommended resolution the game renders at; the picture is stretched to full size.
= 헤드셋 권장 해상도 중 게임이 렌더링하는 비율입니다. 화면은 전체 크기로 늘어납니다.

@ Anti-aliasing (MSAA)
= 안티앨리어싱 (MSAA)

@ Smoother edges in the headset; higher costs GPU time. Key F5.
= 헤드셋에서 가장자리가 부드러워집니다. 높을수록 GPU 부담이 큽니다. 단축키 F5.

@ Frame limit
= 프레임 제한

@ As in game: the game's own VSync / limit. Headset: no game limit, the headset sets the pace. Or a fixed limit.
= 게임과 동일: 게임 자체의 수직 동기화 / 제한. 헤드셋: 게임 제한 없이 헤드셋이 속도를 정합니다. 또는 고정 제한.

@ Headset
= 헤드셋

@ Lamp shadows
= 조명 그림자

@ Shadows cast by lamps and other local lights. Each eye draws them separately - off can help a lot on weaker PCs.
= 램프 등 국소 조명이 만드는 그림자입니다. 눈마다 따로 그리므로, 끄면 사양이 낮은 PC에서 큰 도움이 될 수 있습니다.

@ Draw distance
= 시야 거리

@ How far the headset draws the world. Less = fewer objects for the processor on open maps; far objects disappear. Indoors it changes little.
= 헤드셋이 월드를 그리는 거리입니다. 짧을수록 열린 맵에서 프로세서가 처리할 물체가 줄고, 먼 물체는 사라집니다. 실내에서는 차이가 작습니다.

@ Post-processing in headset
= 헤드셋 후처리

@ Glow, colour grading and other screen effects of the game in the eyes. Off is faster but the picture looks plainer.
= 게임의 빛 번짐, 색 보정 등 화면 효과를 눈에 적용합니다. 끄면 빠르지만 화면이 밋밋해집니다.

@ Interface refresh
= 인터페이스 갱신

@ Redraw the game interface every frame or less often (it still moves smoothly). Saves processor time.
= 게임 인터페이스를 매 프레임 또는 더 드물게 다시 그립니다(움직임은 여전히 부드러움). 프로세서 시간을 아낍니다.

@ Every frame
= 매 프레임

@ Every 2nd frame
= 2프레임마다

@ Every 3rd frame
= 3프레임마다

@ Shadow distance
= 그림자 거리

@ How far shadows are drawn (share of the game's distance). 0% = no shadows.
= 그림자를 그리는 거리입니다(게임 거리에 대한 비율). 0% = 그림자 없음.

@ Shadow quality
= 그림자 품질

@ Resolution of the shadow maps.
= 그림자 맵의 해상도입니다.

@ Low (512)
= 낮음 (512)

@ Medium (1024)
= 보통 (1024)

@ High (2048)
= 높음 (2048)

@ Texture quality
= 텍스처 품질

@ Lower texture resolution saves video memory and some speed.
= 텍스처 해상도를 낮추면 비디오 메모리와 약간의 속도를 아낍니다.

@ Full
= 전체

@ Half
= 1/2

@ Quarter
= 1/4

@ Detail distance
= 디테일 거리

@ How far detailed versions of models are used (LOD).
= 모델의 상세 버전을 사용하는 거리입니다(LOD).

@ Volumetric fog
= 볼류메트릭 안개

@ Buto volumetric fog. Off is faster and removes its noise.
= Buto 볼류메트릭 안개입니다. 끄면 빠르고 안개의 노이즈도 사라집니다.

@ Ambient occlusion (SSAO)
= 앰비언트 오클루전 (SSAO)

@ Contact shadows. Off is faster.
= 접촉 그림자입니다. 끄면 빠릅니다.

@ Decals
= 데칼

@ Stains, marks and other decals on surfaces.
= 표면의 얼룩, 자국 등 데칼입니다.

@ Recenter now
= 지금 중앙 재설정

@ Re-centre the view on where you stand and look now. Key F10.
= 지금 서 있는 위치와 바라보는 방향으로 시야를 다시 맞춥니다. 단축키 F10.

@ Hand calibration: tilt
= 손 보정: 기울기

@ Tilts the character's hands relative to the controllers (fingers up/down).
= 컨트롤러에 대한 캐릭터 손의 기울기입니다(손가락 위/아래).

@ Hand calibration: roll
= 손 보정: 롤

@ Rolls the hands around the forearm.
= 팔뚝을 축으로 손을 돌립니다.

@ Hand calibration: turn
= 손 보정: 좌우 회전

@ Turns the hands left/right.
= 손을 왼쪽/오른쪽으로 돌립니다.

@ Hand calibration: wrist offset
= 손 보정: 손목 위치

@ How far the wrist sits behind the controller grip.
= 손목이 컨트롤러 손잡이 뒤로 얼마나 떨어져 있는지입니다.

@ Hand and item: barrel down
= 손과 아이템: 총구 아래로

@ Tilts the hand and the held item down (+) or up (-), like pointing a pistol barrel lower. Works with and without an item.
= 손과 든 아이템을 아래(+) 또는 위(-)로 기울입니다. 권총 총구를 낮추는 것과 같습니다. 아이템이 있을 때와 없을 때 모두 적용됩니다.

@ Hand and item: left/right
= 손과 아이템: 왼쪽/오른쪽

@ Shifts the hands and the held item sideways (mirrored for the left hand: + is outward).
= 손과 든 아이템을 옆으로 옮깁니다(왼손은 반대: + 가 바깥쪽).

@ Hand and item: down/up
= 손과 아이템: 아래/위

@ Shifts the hands and the held item down (-) or up (+) relative to the controller.
= 손과 든 아이템을 컨트롤러 기준으로 아래(-) 또는 위(+)로 옮깁니다.

@ Hand and item: back/forward
= 손과 아이템: 뒤/앞

@ Shifts the hands and the held item back (-) or forward (+) along the controller.
= 손과 든 아이템을 컨트롤러를 따라 뒤(-) 또는 앞(+)으로 옮깁니다.

@ Fingers at rest
= 쉬는 손가락

@ How much the fingers are bent when no button is pressed. Raise it if they look bent backwards.
= 버튼을 누르지 않았을 때 손가락이 굽은 정도입니다. 손가락이 뒤로 젖혀져 보이면 올리세요.

@ Finger bend direction
= 손가락 굽는 방향

@ Switch if the fingers bend backwards.
= 손가락이 뒤로 굽으면 바꾸세요.

@ Normal
= 보통

@ Reversed
= 반대

@ Fingers: grip on an item
= 손가락: 아이템 쥐기

@ How tightly the fingers close around a held item (100% = full fist). Lower it if the fingers sink into the item.
= 든 아이템을 손가락이 얼마나 꽉 쥐는지입니다(100% = 완전한 주먹). 손가락이 아이템 안으로 들어가면 낮추세요.

@ Fingers: full bend
= 손가락: 최대 굽힘

@ How far the fingers bend at full trigger / grip (100% = as before).
= 트리거 / 그립을 끝까지 눌렀을 때 손가락이 굽는 정도입니다(100% = 이전과 같음).

@ Fingers: spread
= 손가락: 벌림

@ Spreads the fingers apart (+) or together (-), so they do not sink into each other. If + brings them together, use -.
= 손가락을 벌리거나(+) 모읍니다(-). 손가락끼리 겹치지 않게 합니다. + 로 모아지면 - 를 쓰세요.

@ Fingers react to touch
= 손가락이 터치에 반응

@ The thumb bends a little when it rests on a button, the stick or the thumbrest; the index finger - when it rests on the trigger.
= 엄지는 버튼, 스틱, 엄지 받침에 닿으면 조금 굽고, 검지는 트리거에 닿으면 굽습니다.

@ Hands stop at walls
= 손이 벽에서 멈춤

@ The character's hands stop at walls and objects instead of going through them (only how they look; pointing and pressing work as before).
= 캐릭터의 손이 벽과 물체를 뚫지 않고 멈춥니다(보이는 모습만 바뀌며, 가리키기와 누르기는 그대로입니다).

@ Damage: red flash on the eyes
= 피해: 눈에 붉은 번쩍임

@ When health drops, a short red flash at the edges of the view (stronger for a bigger hit).
= 체력이 줄면 시야 가장자리가 잠깐 붉게 번쩍입니다(피해가 클수록 강함).

@ Damage: vibration
= 피해: 진동

@ When health drops, both controllers vibrate briefly.
= 체력이 줄면 두 컨트롤러가 잠깐 진동합니다.

@ Fingers: spread of the index finger
= 손가락: 검지 벌림

@ Turns this finger sideways (degrees), on top of the common spread. Use it to separate fingers that touch each other.
= 공통 벌림에 더해 이 손가락을 옆으로 돌립니다(도). 서로 닿는 손가락을 떼어 놓을 때 쓰세요.

@ Fingers: spread of the middle finger
= 손가락: 중지 벌림

@ Fingers: spread of the ring finger
= 손가락: 약지 벌림

@ Fingers: spread of the little finger
= 손가락: 새끼손가락 벌림

@ Arms: forearm twist
= 팔: 팔뚝 비틀림

@ How much of the hand's roll the forearm takes (so the wrist does not wring like a cloth). 0% = only the hand turns (as before).
= 손의 롤 중 팔뚝이 나눠 받는 비율입니다(손목이 천처럼 비틀리지 않게). 0% = 손만 돕니다(이전과 같음).

@ Arms: shoulders follow
= 팔: 어깨 추종

@ Body: shoulders are where the character model has them (game animations of the body - throws, jumps, item switch - move the arms). Head: fixed under your head (animations do not move the arms; the shoulder settings shift from that point; in a jump the arms go as high as your view). Body, smoothed: on the body, but sharp jerks of the animation are damped.
= 몸: 어깨가 캐릭터 모델의 위치에 있습니다(던지기, 점프, 아이템 교체 같은 몸 애니메이션이 팔을 움직임). 머리: 머리 아래에 고정됩니다(애니메이션이 팔을 움직이지 않음, 어깨 설정은 이 지점 기준, 점프하면 팔이 시야만큼 올라감). 몸, 완화: 몸에 붙어 있지만 애니메이션의 급격한 움직임을 줄입니다.

@ Body
= 몸

@ Body, smoothed
= 몸, 완화

@ Arms: shoulder smoothing
= 팔: 어깨 완화

@ For 'Body, smoothed': how fast the shoulders follow the body animation (lower = smoother, but the arms lag behind the body).
= '몸, 완화'일 때 어깨가 몸 애니메이션을 따라가는 속도입니다(낮을수록 부드럽지만 팔이 몸보다 늦습니다).

@ Body: back/forward
= 몸: 뒤/앞

@ Moves the character model forward so the body (feet) is under you and not behind (only in your view; Hands = Character arms). Too far and the chest covers the view.
= 캐릭터 모델을 앞으로 옮겨 몸(발)이 뒤가 아닌 내 아래에 오게 합니다(내 시야에서만, 손 = 캐릭터 팔). 너무 멀면 가슴이 시야를 가립니다.

@ Body: down/up
= 몸: 아래/위

@ Moves the character model down or up (only in your view).
= 캐릭터 모델을 아래나 위로 옮깁니다(내 시야에서만).

@ Legs: back/forward
= 다리: 뒤/앞

@ Moves only the pelvis and legs forward (the chest and arms stay) - the model keeps its feet far behind. Use with 'Body: back/forward'.
= 골반과 다리만 앞으로 옮깁니다(가슴과 팔은 그대로). 모델은 발을 뒤쪽 멀리 두기 때문입니다. '몸: 뒤/앞'과 함께 쓰세요.

@ Body: tilt (feet forward)
= 몸: 기울기 (발 앞으로)

@ Tilts the whole model back around your head: the feet come forward much more than the chest, nothing stretches. Only in your view; the shadow leans too.
= 모델 전체를 머리를 축으로 뒤로 기울입니다. 발이 가슴보다 훨씬 앞으로 오고, 늘어나는 곳은 없습니다. 내 시야에서만 적용되며 그림자도 기울어집니다.

@ Body tilt: feet stay on the ground
= 몸 기울기: 발은 땅에

@ With 'Body: tilt' the model is lifted or lowered by itself so the lowest foot stays where the animation has it (no feet in the ground). 'Body: down/up' still adds on top.
= '몸: 기울기'를 쓸 때 모델을 자동으로 올리거나 내려 가장 낮은 발이 애니메이션 위치에 남게 합니다(발이 땅에 묻히지 않음). '몸: 아래/위'는 여기에 더해집니다.

@ Torso straight
= 곧은 상체

@ After the game's animation the spine and neck are straightened towards the model's vertical: walking no longer leans the body forward and looking down no longer bends the chest and neck into the view. 0% = as animated. Only in your view - the others see the animation as before.
= 게임 애니메이션 뒤 척추와 목을 모델의 수직 방향으로 펴 줍니다: 걸을 때 몸이 앞으로 숙여지지 않고, 아래를 볼 때 가슴과 목이 시야로 굽지 않습니다. 0% = 애니메이션 그대로. 내 시야에서만 적용되며 다른 플레이어는 이전처럼 애니메이션을 봅니다.

@ Arms: thickness
= 팔: 두께

@ Makes the character's arms thinner or thicker (the hand size is set separately).
= 캐릭터의 팔을 가늘게 또는 두껍게 합니다(손 크기는 따로 설정).

@ Arms: upper arm length
= 팔: 위팔 길이

@ Length from the shoulder to the elbow.
= 어깨에서 팔꿈치까지의 길이입니다.

@ Arms: forearm length
= 팔: 팔뚝 길이

@ Length from the elbow to the wrist.
= 팔꿈치에서 손목까지의 길이입니다.

@ Arms: hand size
= 팔: 손 크기

@ Size of the hands and fingers (the item in the hand keeps its real size).
= 손과 손가락의 크기입니다(손에 든 아이템은 실제 크기 유지).

@ Arms: shoulders down/up
= 팔: 어깨 아래/위

@ Moves where the arms start (the shoulders) down (-) or up (+).
= 팔이 시작되는 곳(어깨)을 아래(-) 또는 위(+)로 옮깁니다.

@ Arms: shoulders back/forward
= 팔: 어깨 뒤/앞

@ Moves the shoulders back (-) or forward (+).
= 어깨를 뒤(-) 또는 앞(+)으로 옮깁니다.

@ Arms: shoulders narrower/wider
= 팔: 어깨 좁게/넓게

@ Moves the shoulders inward (-) or outward (+).
= 어깨를 안쪽(-) 또는 바깥쪽(+)으로 옮깁니다.

@ Height correction
= 키 보정

@ Raise or lower your eyes relative to the character.
= 캐릭터에 대한 눈높이를 올리거나 내립니다.

@ Mark: smudge in front of eyes
= 표시: 눈앞의 얼룩

@ Press while you see the 'dirty screen' about a metre away. Writes what is on screen to the log.
= 약 1미터 앞에 '더러운 화면'이 보일 때 누르세요. 화면에 있는 것을 로그에 기록합니다.

@ Mark: grain / noise
= 표시: 입자 / 노이즈

@ Press while the film-grain noise in the dark bothers you. Writes active effects to the log.
= 어두운 곳의 필름 입자 노이즈가 거슬릴 때 누르세요. 활성 효과를 로그에 기록합니다.

@ Write item in hand model to files
= 손에 든 아이템 모델을 파일로 기록

@ Writes the meshes, materials and textures of the item in your right hand to BepInEx/MimesisVR_dump (for the wrist watch work).
= 오른손에 든 아이템의 메시, 머티리얼, 텍스처를 BepInEx/MimesisVR_dump에 기록합니다(손목 시계 작업용).

@ Write interface tree to log
= 인터페이스 트리를 로그에 기록

@ Writes the list of visible interface panels to the log.
= 보이는 인터페이스 패널 목록을 로그에 기록합니다.

@ Language
= 언어

@ Language of the mod's texts. 'As the game' follows the game's language (English when the mod has no such language). More languages: files in UserData\\MimesisVR\\Lang (see README).
= 모드 텍스트의 언어입니다. '게임과 동일'은 게임 언어를 따릅니다(모드에 그 언어가 없으면 영어). 다른 언어: UserData\\MimesisVR\\Lang 폴더의 파일(README 참고).

@ As the game
= 게임과 동일

@ Debug tools
= 디버그 도구

@ Shows the diagnostic tools on this tab, the 'Test (host)' tab with cheats and the developer hotkeys. Turning it off also switches every cheat off.
= 이 탭의 진단 도구, 치트가 있는 '테스트 (호스트)' 탭, 개발자 단축키를 표시합니다. 끄면 모든 치트도 꺼집니다.

@ Frame flip mode
= 프레임 뒤집기 방식

@ Diagnostics. Key F8.
= 진단용. 단축키 F8.

@ Projection (old)
= 투영 (이전)

@ Menu panel mode
= 메뉴 패널 방식

@ Diagnostics. Key F2.
= 진단용. 단축키 F2.

@ Headset layer
= 헤드셋 레이어

@ In-world object
= 월드 안 오브젝트

@ Cheats for
= 치트 적용 대상

@ Who the health, sprint and revive cheats apply to. Works only when you host the lobby.
= 체력, 달리기, 부활 치트가 적용되는 대상입니다. 로비 호스트일 때만 작동합니다.

@ Everyone
= 모든 플레이어

@ Only me
= 나만

@ Undying
= 불사

@ Health cannot drop to zero. Works only when you host the lobby.
= 체력이 0까지 떨어지지 않습니다. 로비 호스트일 때만 작동합니다.

@ Endless sprint
= 무한 달리기

@ Stamina is refilled all the time. Works only when you host the lobby.
= 스태미나가 계속 채워집니다. 로비 호스트일 때만 작동합니다.

@ Slow monsters
= 느린 몬스터

@ Monsters move at 30% speed. Works only when you host the lobby.
= 몬스터가 30% 속도로 움직입니다. 로비 호스트일 때만 작동합니다.

@ Monsters frozen (AI off)
= 몬스터 정지 (AI 끔)

@ Monster AI is switched off: they stand still. Works only when you host the lobby.
= 몬스터 AI가 꺼져 제자리에 서 있습니다. 로비 호스트일 때만 작동합니다.

@ Restore health
= 체력 회복

@ Full health now. Works only when you host the lobby.
= 지금 체력을 가득 채웁니다. 로비 호스트일 때만 작동합니다.

@ Remove contamination
= 오염 제거

@ Clears contamination (radiation) of the whole team. Works only when you host the lobby.
= 팀 전체의 오염(방사능)을 없앱니다. 로비 호스트일 때만 작동합니다.

@ Revive
= 부활

@ Brings dead players back. Works only when you host the lobby.
= 죽은 플레이어를 되살립니다. 로비 호스트일 때만 작동합니다.

@ Money +1000
= 돈 +1000

@ Adds 1000 to the team money (in the tram / maintenance only). Works only when you host the lobby.
= 팀 자금에 1000을 더합니다(트램 / 정비 구역에서만). 로비 호스트일 때만 작동합니다.

@ Item: spawn next to me
= 아이템: 내 옆에 생성

@ Stick left/right chooses the item from the game's full list, trigger spawns it next to you. Works only when you host the lobby.
= 스틱 좌우로 게임 전체 목록에서 아이템을 고르고, 트리거로 내 옆에 생성합니다. 로비 호스트일 때만 작동합니다.

@ Item: as loot all over the map
= 아이템: 맵 전체에 전리품으로

@ Puts the item chosen above on every loot point of the level. Works only when you host the lobby.
= 위에서 고른 아이템을 레벨의 모든 전리품 지점에 놓습니다. 로비 호스트일 때만 작동합니다.

@ Game time +1 hour
= 게임 시간 +1시간

@ Moves the level clock one hour forward (on a level only). Works only when you host the lobby.
= 레벨 시계를 1시간 앞으로 돌립니다(레벨에서만). 로비 호스트일 때만 작동합니다.

@ Game time -1 hour
= 게임 시간 -1시간

@ Moves the level clock one hour back (on a level only). Works only when you host the lobby.
= 레벨 시계를 1시간 뒤로 돌립니다(레벨에서만). 로비 호스트일 때만 작동합니다.

@ Remove all monsters
= 모든 몬스터 제거

@ Kills every monster on the level. Works only when you host the lobby.
= 레벨의 모든 몬스터를 죽입니다. 로비 호스트일 때만 작동합니다.

@ Show the layout
= 배치 보기

@ Both controllers with what each button does now.
= 두 컨트롤러와 각 버튼에 지금 할당된 동작을 보여 줍니다.

@ Standard layout
= 기본 배치

@ Attack on the right trigger, interact on the right grip, voice on the left grip, emotes on Y.
= 오른쪽 트리거에 공격, 오른쪽 그립에 상호작용, 왼쪽 그립에 음성, Y에 이모트.

@ Attack / use item
= 공격 / 아이템 사용

@ The game's left mouse button. Swinging the hand also attacks (Hands tab). One button can hold several actions; ""-"" = not assigned.
= 게임의 마우스 왼쪽 버튼입니다. 손을 휘둘러도 공격합니다(손 탭). 한 버튼에 여러 동작을 넣을 수 있습니다. ""-"" = 할당 안 됨.

@ Left stick click
= 왼쪽 스틱 누르기

@ Right stick click
= 오른쪽 스틱 누르기

@ Left trigger
= 왼쪽 트리거

@ Right trigger
= 오른쪽 트리거

@ Left grip
= 왼쪽 그립

@ Right grip
= 오른쪽 그립

@ Second action
= 보조 동작

@ The game's right mouse button (second use of the item in hand). One button can hold several actions; ""-"" = not assigned.
= 게임의 마우스 오른쪽 버튼입니다(손에 든 아이템의 두 번째 사용). 한 버튼에 여러 동작을 넣을 수 있습니다. ""-"" = 할당 안 됨.

@ Interact (take, open)
= 상호작용 (줍기, 열기)

@ Take items, open doors, press buttons. One button can hold several actions; ""-"" = not assigned.
= 아이템 줍기, 문 열기, 버튼 누르기. 한 버튼에 여러 동작을 넣을 수 있습니다. ""-"" = 할당 안 됨.

@ Voice (push to talk)
= 음성 (눌러서 말하기)

@ Works in menus and with the mod settings open too. One button can hold several actions; ""-"" = not assigned.
= 메뉴에서도, 모드 설정이 열려 있을 때도 작동합니다. 한 버튼에 여러 동작을 넣을 수 있습니다. ""-"" = 할당 안 됨.

@ Jump
= 점프

@ One button can hold several actions; ""-"" = not assigned.
= 한 버튼에 여러 동작을 넣을 수 있습니다. ""-"" = 할당 안 됨.

@ Drop item in hand
= 손에 든 아이템 버리기

@ Status
= 상태

@ The game's status screen (Tab). One button can hold several actions; ""-"" = not assigned.
= 게임의 상태 화면(Tab)입니다. 한 버튼에 여러 동작을 넣을 수 있습니다. ""-"" = 할당 안 됨.

@ Emote wheel
= 이모트 휠

@ Hold: the wheel opens. Move the hand towards an emote (or tilt the other stick) and release. One button can hold several actions; ""-"" = not assigned.
= 누르고 있기: 휠이 열립니다. 손을 이모트 쪽으로 움직이고(또는 다른 스틱을 기울이고) 놓으세요. 한 버튼에 여러 동작을 넣을 수 있습니다. ""-"" = 할당 안 됨.

@ Reload
= 재장전

@ Run
= 달리기

@ Toggles running (stops when you stop moving), or hold - see Sprint on the Controls tab. One button can hold several actions; ""-"" = not assigned.
= 달리기를 켜고 끄거나(멈추면 꺼짐) 누르고 있기 - 조작 탭의 '달리기' 참고. 한 버튼에 여러 동작을 넣을 수 있습니다. ""-"" = 할당 안 됨.

@ Tram settings (host)
= 트램 설정 (호스트)

@ The game's tram settings key. Off by default. One button can hold several actions; ""-"" = not assigned.
= 게임의 트램 설정 키입니다. 기본값은 끔. 한 버튼에 여러 동작을 넣을 수 있습니다. ""-"" = 할당 안 됨.

@ Move with the right stick
= 오른쪽 스틱으로 이동

@ Move with the right stick; turn and switch slots with the left one.
= 오른쪽 스틱으로 이동하고, 왼쪽 스틱으로 회전과 슬롯 전환을 합니다.

@ Button names in game prompts
= 게임 안내의 버튼 이름

@ The game's prompts (""press E"") show the controller button instead of the keyboard key.
= 게임 안내(""E 누르기"")에 키보드 키 대신 컨트롤러 버튼을 표시합니다.

@ Emote wheel: hand travel
= 이모트 휠: 손 이동 거리

@ How far to move the hand to pick an emote.
= 이모트를 고르기 위해 손을 움직여야 하는 거리입니다.

@ Emote wheel: disc opacity
= 이모트 휠: 원판 불투명도

@ The game draws the wheel on a solid dark disc. Applies the next time the wheel opens.
= 게임은 휠을 어두운 원판 위에 그립니다. 다음에 휠을 열 때 적용됩니다.

@ Emotes: view
= 이모트: 시야

@ Some emotes move the body forward, in front of your eyes. Follow: the view moves with the character's head sideways. Stay: the view stays. Hide body: your own body is hidden while the emote plays (others see it).
= 일부 이모트는 몸을 앞으로, 눈앞으로 움직입니다. 따라가기: 시야가 캐릭터 머리를 따라 옆으로 움직입니다. 고정: 시야가 그대로입니다. 몸 숨기기: 이모트 중 내 몸을 숨깁니다(다른 플레이어에게는 보임).

@ Follow the head
= 머리 따라가기

@ Stay
= 고정

@ Hide body
= 몸 숨기기

@ Write a language file template
= 언어 파일 템플릿 기록

@ For translators: writes UserData\\MimesisVR\\Lang\\<language>.template.txt with every text of the mod and its current translation.
= 번역자용: 모드의 모든 텍스트와 현재 번역을 UserData\\MimesisVR\\Lang\\<언어>.template.txt에 기록합니다.

@ Reset all settings
= 모든 설정 초기화

@ Return every setting to its default (language stays).
= 모든 설정을 기본값으로 되돌립니다(언어는 유지).

@ The game's left mouse button. Swinging the hand also attacks (Hands tab).
= 게임의 마우스 왼쪽 버튼입니다. 손을 휘둘러도 공격합니다(손 탭).

@ The game's right mouse button (second use of the item in hand).
= 게임의 마우스 오른쪽 버튼입니다(손에 든 아이템의 두 번째 사용).

@ Take items, open doors, press buttons.
= 아이템 줍기, 문 열기, 버튼 누르기.

@ Works in menus and with the mod settings open too.
= 메뉴에서도, 모드 설정이 열려 있을 때도 작동합니다.

@ The game's status screen (Tab).
= 게임의 상태 화면(Tab)입니다.

@ Hold: the wheel opens. Move the hand towards an emote (or tilt the other stick) and release.
= 누르고 있기: 휠이 열립니다. 손을 이모트 쪽으로 움직이고(또는 다른 스틱을 기울이고) 놓으세요.

@ Toggles running (stops when you stop moving), or hold - see Sprint on the Controls tab.
= 달리기를 켜고 끄거나(멈추면 꺼짐) 누르고 있기 - 조작 탭의 '달리기' 참고.

@ The game's tram settings key. Off by default.
= 게임의 트램 설정 키입니다. 기본값은 끔.

@ Debug tools are off
= 디버그 도구가 꺼져 있습니다

@ Only when you host
= 호스트일 때만

@ Player not found
= 플레이어를 찾을 수 없음

@ Done ({0} of {1})
= 완료 ({1}명 중 {0}명)

@ Done
= 완료

@ Not possible here
= 여기서는 불가

@ No item list
= 아이템 목록 없음

@ Everywhere: 
= 맵 전체:

@  WARNING: the headset runs at {0} Hz, a {1} limit does not match it - choose Headset or set the headset to {1} Hz.
=  경고: 헤드셋이 {0}Hz로 작동 중이며 제한 {1}과 맞지 않습니다 - '헤드셋'을 고르거나 헤드셋을 {1}Hz로 설정하세요.

@  Nothing in the right hand now.
=  지금 오른손에 아무것도 없습니다.

@  In hand now: {0}.
=  지금 손에 든 것: {0}.

@ MIMESIS VR - picture is in the headset
= MIMESIS VR - 화면은 헤드셋에 있습니다

@ Monitor view is off for speed. Turn on: wrist mod settings -> Performance, or F1 (headset view).
= 속도를 위해 모니터 출력이 꺼져 있습니다. 켜기: 손목의 모드 설정 -> 성능, 또는 F1(헤드셋 화면).

@ Settings
= 설정

@ < Back to the list
= < 목록으로

@ Shared: 
= 공유됨:

@ move
= 이동

@ turn; up/down - slots
= 회전, 위/아래 - 슬롯

@ Right controller
= 오른쪽 컨트롤러

@ stick
= 스틱

@ Trigger
= 트리거

@ Grip
= 그립

@ Stick
= 스틱

@ Stick click
= 스틱 누르기

@ Menu
= 메뉴

@ game menu
= 게임 메뉴

@ Click - select or change     Wheel - scroll     Right click - close
= 클릭 - 선택 또는 변경     휠 - 스크롤     오른쪽 클릭 - 닫기

@ Trigger - select     Stick left/right - change, up/down - scroll     B - close
= 트리거 - 선택     스틱 좌우 - 변경, 위/아래 - 스크롤     B - 닫기

@ Auto
= 자동
";
    }
}
