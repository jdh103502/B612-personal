# 02. 실행 흐름

[← 인계 문서 목차](README.md)

메인 씬 `B612_ChapterJourney`(또는 검증 씬 `B612_EmptyPlanet`)에서 Play를 누른 뒤 무슨 일이 일어나는지 순서대로 정리한다.

## 1. 시작: 항성계와 첫 행성

`StarSystemFlowController.Start()`가 전체를 시작한다.

1. **바인딩 검사.** 씬 참조가 모두 연결됐는지 확인한다(`ValidateBindings`). 참조는 `worldHost`, `player`, `surge`, `hand`, `pause`, `view`다.
2. **여정 seed.** `randomizeRunSeed`가 켜져 있으면 무작위 seed를, 꺼져 있으면 `runSeed`를 쓴다. 같은 행성을 비교하려면 끄고 `runSeed`를 정한다.
3. **항성계 생성.** `StarSystemFactory.Create(seed, settings, chapter)`가 6노드 항성계를 만든다.
   - **노드:** `Start`, `B`, `C`, `D`, `E`, `Destination`이다. 깊이는 0, 1, 1, 2, 2, 3이다.
   - **연결:** Start→B, Start→C, B→D, B→E, C→E, D→Destination, E→Destination이다.
   - **크기:** Start 행성은 1배, 나머지는 0.7~2배 무작위다. 반경은 `nominalRadius × 배율`이다.
   - **seed:** 행성 seed는 `DeriveSeed(seed, 2, i)`, 외형 seed는 `DeriveSeed(seed, 1, i)`다.
   - **캠페인일 때의 기술:** 각 행성에 필요한 기술(`requiredCapabilities`)과 해금 기술(`unlock`)을 정한다. Start 행성만 해금 기술을 가진다.
4. **진행 상태.** `RunProgressData`를 만든다. 캠페인이면 해금 기술은 없음, 아니면 모든 기술로 시작한다.
5. **첫 행성 준비.** `PlanetWorldHost.Initialize(Current, playerCapsule)` → `PlanetWorldInstance.Prepare(...)`가 행성을 만든다([3절](#3-행성-준비-planetworldinstanceprepare)). 이어서 `Activate(world)`와 `BindWorld(world)`를 부른다.
6. **바오밥 페이즈 시작.** `surge.Begin(true)`

`BindWorld`가 하는 일:
- 바오밥 스폰 설정(지면, 스폰 지점, seed, 시간·개수·간격·위험도 용량)을 행성 데이터의 `spec.baobab`으로 바꾼다.
- 플레이어를 스폰 지점으로 리스폰한다.
- 카메라를 새 행성 기준으로 초기화한다.
- 캠페인이면 상호작용 지점(`CampaignWorldSites`)을 붙인다.

## 2. 한 행성에서의 플레이

```
행성 도착 → 바오밥 싹이 120초 동안 퍼짐 → 양 카드로 싹을 먹임
         → 위험도 100% 도달 시 게임오버 / 스폰 시간 종료 시 지도 활성화
         → M으로 지도 → 연결된 다음 행성 선택 → 이동
```

- **바오밥 스폰** (`BaobabSurge`)
  - `durationSeconds`(120초) 동안 `targetSprouts`(40)개의 스폰 기회를 고르게 나눈다.
  - 동시에 있는 싹은 `maxActiveSprouts`(6)개까지, 싹 사이 간격은 `minimumSpacing`(1.4m) 이상이다.
  - 위치는 `PlanetSurfaceGrid`의 후보 슬롯에서 고른다. 가장 덜 쓴 구역을 먼저 쓰고, 가장 가까운 싹과의 거리를 최대화하는 쪽을 고른다.
  - 플레이어 주변 0.9m와 스폰 지점 주변 0.7m에는 놓지 않는다.
- **싹 성장**
  - 싹은 나이에 따라 싹 → 묘목(20초) → 나무(40초)로 바뀐다(`BaobabTypeDefinition`).
  - 각 싹의 위험도 기여는 `initial + linear·age + quadratic·age²`이고 `maximum`에서 멈춘다.
- **위험도** (`BaobabThreatMeter`)
  - 살아 있는 싹의 기여를 매 프레임 합한다.
  - `Capacity`(기본 100) 이상이면 게임오버다. 게임을 멈추고 재시도 버튼을 띄운다.
  - 스폰 시간이 끝나도 남은 싹은 계속 자란다.
- **양 카드** (`LambCardHand`, `LambAgent`)
  - 카드 4장을 쓴다. 숫자 키나 휠로 고르고, Q로 플레이어 앞에 양을 소환한다.
  - 양은 반경 1.7m 안의 싹을 하나씩 먹는다. 먹는 시간은 싹 7초, 묘목 10초, 나무 13초다.
  - 양은 20초 뒤 줍기 상태가 된다. 플레이어가 가까이 가면 카드가 손으로 돌아온다.
- **상호작용 지점** (`CampaignWorldSites`, 캠페인일 때만)
  - 금색 기둥(능력 배우기), 작물 조사, 만남의 3곳이 스폰에서 3.2~9m 떨어져 있다.
  - E로 상호작용하고, 방문 시간과 이동 거리를 기록한다.
  - 금색 기둥에서 그 행성의 해금 기술을 배운다.
- **지도 열기:** 바오밥 스폰이 끝나고 게임오버가 아닐 때만 M으로 지도를 연다(`CanOpenMap`). 지도가 열려 있는 동안은 일시정지다.

## 3. 행성 준비: `PlanetWorldInstance.Prepare`

새 행성은 항상 새 `PlanetWorldInstance`에서 만든다. 준비된 월드는 바꿀 수 없다.

1. **데이터 검사.** 행성 데이터의 프로필, 생성기 버전, 모델 버전이 현재 코드와 맞는지 확인한다(`StarSystemFactory.ValidatePlanet`). 다르면 예외를 던진다.
2. **재질.** 지면 템플릿 재질을 복제해 행성 색(`groundColor`)과 반경을 넣는다.
3. **지형 생성.**
   - **자연 프로필:** `ProceduralPlanetTerrain.PrepareNatural(...)`이 높이장, 강, 메시, 충돌체, 물 메시를 만든다. 이어서 `NaturalTerrainPlacement`가 완성된 충돌체에서 걸어서 이어진 영역을 분석하고 스폰과 상호작용 지점을 정한다. 단계별 상세는 [03-행성 지형 생성](03-TerrainGeneration.md)에 있다.
   - **예전 프로필:** `ProceduralPlanetTerrain.Prepare(...)`가 지역·지형 요소·검수 코스를 만들고, 실제 모터로 보행을 검사하며 최대 5번 보수한다.
4. **팔레트와 습도.** 지면 셰이더에 생성 계획의 흙·초목·암석 팔레트와 습도 값을 넣는다.
5. **중력과 후보 필터.** 중력원(`GravitySource`)에 지형 계획을 연결하고, 바오밥 후보 필터를 `Terrain.AllowsSpawn`으로 정한다.
6. **바오밥 후보 슬롯.** `PlanetSurfaceGrid.ConfigureResolution`과 `Prepare(seed)`가 구역마다 바오밥 후보 슬롯을 만든다. 자연 프로필이면 동시 활성 싹 수만큼 서로 떨어진 위치가 있는지 확인한다(`CheckCapacity`).
7. **스폰 지점.** 지면에 투영하고, 첫 상호작용 지점(금색 기둥)을 바라보게 한다. 플레이어 캡슐이 지면과 겹치면 실패다.
8. **중력 범위.** 중력 반경, 세기, 영향 반경을 정한다.

준비가 실패하면 예외가 난다.
- 처음 행성에서는 안내 문구를 띄운다.
- 이동 중에는 현재 행성과 데이터를 하나도 바꾸지 않고 지도로 돌아간다. 현재 월드는 준비가 끝난 뒤에야 바뀐다.

## 4. 행성 이동

`StarSystemFlowController`의 순서는 다음과 같다.

1. 지도에서 노드를 클릭하면 `RequestTravel(id)`로 의도만 기록한다.
   - 이동 조건(`CanTravel`): 연결된 미방문 행성이고, 현재 행성이 도착 행성이 아니고, 캠페인이면 필요한 기술을 갖고 있어야 한다.
2. `LateUpdate`에서 `BeginTransition(id)`: 일시정지 사유를 `PlanetTransition`으로 바꾸고 전환 화면을 띄운다.
3. 코루틴 `Travel`이 다음을 한다.
   1. 0.35초 기다린 뒤 `worldHost.Prepare(planet)`로 새 월드를 만든다(`Prepared <planetId>` 오브젝트).
   2. 성공하면 현재 행성 상태를 기록하고, 양을 회수하고, 싹을 지운다.
   3. 새 월드를 활성화하고 `BindWorld`를 부른다. 이전 월드는 파괴한다.
   4. 0.35초 뒤 일시정지를 푼다.
4. **다음 챕터**(캠페인): 도착 행성에서 바오밥 페이즈를 마치면 지도에 "다음 챕터" 버튼이 생긴다. 누르면 `DeriveSeed(journeySeed, 50, chapter)`로 새 항성계를 만들어 그 시작 행성으로 이동한다.

검증 도구도 같은 seed 규칙을 쓴다. 예를 들어 `(612101, 2, "system_02_B", 1)`은 여정 seed 612101의 2챕터 항성계 B 행성을 1배 크기로 만든다는 뜻이다.

## 5. 프레임 실행 순서

`[DefaultExecutionOrder]`로 다음 순서가 정해져 있다(작은 값이 먼저).

| 순서 | 스크립트 | 이유 |
|---|---|---|
| −1000 | `DeveloperDebugController` | F12 디버그 입력과 일시정지를 가장 먼저 처리 |
| −100 | `BaobabSurge` | 싹 스폰·성장 시간 진행 |
| −50 | `TraversalTestCourse` | 실험실 구간 선택 |
| 0 | 대부분(플레이어 입력, 양, 카메라 등) | |
| 50 | `BaobabThreatMeter` | `LateUpdate`에서 이번 프레임 양의 제거가 끝난 뒤 패배를 한 번만 판정 |
| 60 | `BaobabThreatHUD` | 판정 결과 표시 |
| 100 / 110 | `SproutHorizonIndicators` / `LambEatingIndicators` | 화면 밖 싹 말풍선, 먹는 중 표시 |
| 300 | `StarSystemFlowController` | `LateUpdate`에서 패배 판정 뒤 지도·이동·사이트 요청을 적용 |

**규칙:** M 키나 지도 클릭 같은 입력은 `Update`나 UI 콜백에서 의도만 기록한다. 실제 상태 변경은 `StarSystemFlowController.LateUpdate`에서 한다. 그래야 같은 프레임에 패배와 이동이 겹쳐도 패배가 우선한다.

## 6. 일시정지와 모달

- **`GamePauseCoordinator`**
  - 씬마다 하나 있다. 여러 소유자가 사유(`DebugOverlay`, `GameOver`, `MapOverlay`, `PlanetTransition`)를 붙여 일시정지를 요청한다(`AcquirePause` / `ReleasePause`).
  - 소유자가 하나라도 있으면 `Time.timeScale = 0`이다. 원래 속도는 하나만 저장하고 새 시도로 넘기지 않는다.
- **`GameplayModalInput`**
  - 한 번에 하나의 모달(지도, 디버그 등)만 입력을 가진다.
  - 모달이 열려 있으면 게임플레이 입력과 마우스 카메라 입력을 막는다(`BlocksGameplayInput`, `BlocksMouseInput`).
  - 닫힌 프레임의 입력이 게임으로 새지 않게 막는다.
- **F12 개발자 디버그** (`DeveloperDebugController`, `DeveloperDebugHUD`)
  - 에디터와 개발 빌드에서만 연다.
  - 대쉬·추가 점프·활공을 따로 허용하거나 끌 수 있다. 이미 배운 기술만 켤 수 있다.
  - "바오밥 페이즈 스킵"으로 현재 행성의 싹을 정리하고 지도를 연다.

## 7. 입력

| 입력 | 동작 | 처리 스크립트 |
|---|---|---|
| WASD / 방향키, 왼쪽 스틱 | 이동(카메라 기준) | `PlayerController` → `GravityWalker.moveInput` |
| Space / 패드 South | 점프. 누른 채 유지하면 더 높이. 공중에서 다시 누르면 추가 점프. 하강 중 유지하면 활공 | `PlayerController` → `PlayerTraversalAbilities` |
| 좌·우 Shift / 패드 Right Shoulder | 대쉬 | 〃 |
| 마우스 드래그, 오른쪽 스틱, Z / C | 카메라 회전. 카드 모드가 아니면 Q / E | `OrbitCamera` |
| Ctrl + 휠 | 카메라 줌. 카드 모드가 아니면 휠만으로 | `OrbitCamera` |
| 1~9, 휠 | 양 카드 선택 | `LambCardHand` |
| Q | 선택한 양 소환 | `LambCardHand` |
| E | 상호작용 지점 사용 | `CampaignWorldSites` |
| M / Esc | 지도 열기 / 닫기 | `StarSystemFlowController` |
| R | 현재 행성 스폰 지점으로 리스폰 | `PlayerController.Respawn` |
| F12 | 개발자 디버그 패널 | `DeveloperDebugController` |
| H | 도움말 숨기기(디오라마 데모) | `DemoHUD` |

같은 틱에 Space와 Shift를 함께 누르면 점프를 먼저 적용하고 대쉬를 시작한다. 근처에 발사 별(`LaunchStar`)이 있으면 Space+Shift는 발사 별 탑승이 우선한다. 입력 우선순위와 자원 회복 규칙은 [PlayerTraversalImplementationSpec.md](../PlayerTraversalImplementationSpec.md)에 있다.

## 8. 데이터 모델 (`StarSystemData.cs`)

모두 `[Serializable]` 클래스이고 JSON으로 저장할 수 있다. 예시는 `Docs/Examples/PlanetDataExample.json`, 설계는 [PlanetDataModel.md](../PlanetDataModel.md)에 있다.

| 클래스 | 역할 | 주요 필드 |
|---|---|---|
| `StarSystemData` | 한 챕터의 항성계 | `systemId`(`system_01`…), `systemIndex`(챕터), `seed`, `startPlanetId`, `destinationPlanetId`, `planets`, `links` |
| `PlanetData` | 행성 하나 | `modelVersion`(1), `spec`(고정 생성 입력), `map`(지도 배치), `state`(진행 상태) |
| `PlanetSpec` | 행성의 고정 생성 입력 | `planetId`(`system_02_B` 형식), `seed`, `generatorVersion`, `chapter`, `role`, `sizeClass`, `requiredCapabilities`, `unlock`, `terrain`, `baobab`, `crops`, `npc` |
| `TerrainPlan` | 지형 입력 | `profileId`, `groundColor`, `sizeScale`(0.7~2), `radius`, `gravityStrength` |
| `BaobabPlan` | 바오밥 페이즈 입력 | `durationSeconds`(120), `spawnEventCount`(40), `maxActive`(6), `minimumSpacing`(1.4), `threatCapacity`(100), `typePool` |
| `PlanetMapPlacement` | 지도 위치 | `depth`, `position01` |
| `PlanetState` | 진행 상태 | `phase`, `worldInitialized`, `simulationSeconds`, `surge`, `visitedSites`(비트), `siteVisitSeconds`, `siteTravelMetres`, `harvestedCrops`, `quest` |
| `SurgeProgress` | 바오밥 페이즈 결과 | `elapsedSpawnSeconds`, `processedSpawnEvents`, `result`, `skippedByDebug` |
| `RunProgressData` | 여정 전체 진행 | `currentSystemId`, `currentPlanetId`, `pendingDestinationId`, `chapter`, `journeySeed`, `unlocked`, `journeyComplete`, `chosenPath`, `mode` |
| `CropPlan`, `NpcPlan`, `QuestProgress`, `RewardOption`, `WoolAmount`, `CropAmount` | 작물·NPC·퀘스트·보상 | 자리만 있고 아직 게임 로직이 없다 |

| 열거형 | 값 |
|---|---|
| `PlanetRole` | Start, Normal, Special, Destination |
| `PlanetPhase` | Unvisited, Surge, Clearing, Harvest, Quest, ReadyToLeave, Departed, GameOver |
| `SurgeResult` | NotStarted, InProgress, Resolved, LeftWithRemaining, Failed |
| `RunMode` | PlayingPlanet, SelectingRoute, Travelling, GameOver |
| `PlanetSizeClass` | Small, Medium, Large(`PlanetGenerationSettings.Classify(scale)`) |
| `TraversalCapability`(플래그) | None, Dash, AirJump, Glide, All |

- **`StarSystemFactory.DeriveSeed(seed, stream, node)`:** 결정론적 seed 파생 함수다. 같은 입력이면 늘 같은 값을 준다. 행성·외형·챕터·배치 seed가 모두 이 함수에서 나온다.
- **`PlanetMapPreview`:** 지도 툴팁 전용 표시 데이터다. 생성 규칙을 가지지 않는다.
