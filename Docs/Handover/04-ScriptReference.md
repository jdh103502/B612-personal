# 04. 스크립트 레퍼런스

[← 인계 문서 목차](README.md)

`Assets/B612/Scripts`의 66개 런타임 스크립트를 기능별로 묶어 설명한다. 모두 네임스페이스 `B612`다. 괄호 안 숫자는 기본값이다.

- **표기**
  - **필드**는 인스펙터에서 바꿀 수 있는 직렬화 값이고, **속성**은 읽기 전용 상태다.
  - `internal`은 같은 어셈블리(게임 코드)와 검증 스크립트의 리플렉션에서만 쓴다.
- **같이 보면 좋은 도구:** 클래스마다 모든 멤버를 다 적지는 않았다. 정확한 시그니처는 소스를 본다.
  - `AgentScripts`의 검증 스크립트는 `internal` 멤버를 리플렉션으로 부른다. 예: `GravityWalker.TickTraversal`, `PlayerTraversalAbilities.QueueSimulationInput`
  - 이름을 바꾸면 검증 스크립트도 고친다.

목차
1. [플레이어와 이동](#1-플레이어와-이동)
2. [카메라](#2-카메라)
3. [행성 월드와 생성 설정](#3-행성-월드와-생성-설정)
4. [지형 생성](#4-지형-생성)
5. [물](#5-물)
6. [배치와 바오밥 후보](#6-배치와-바오밥-후보)
7. [항성계·여정·지도](#7-항성계여정지도)
8. [바오밥 싹과 위험도](#8-바오밥-싹과-위험도)
9. [양 카드](#9-양-카드)
10. [UI·HUD](#10-uihud)
11. [일시정지·모달·디버그](#11-일시정지모달디버그)
12. [캐릭터 애니메이션과 연출](#12-캐릭터-애니메이션과-연출)
13. [이동 실험실과 기타](#13-이동-실험실과-기타)
14. [에디터 스크립트](#14-에디터-스크립트)

---

## 1. 플레이어와 이동

### `GravityWalker`
작은 행성용 Rigidbody 보행 모터다. 서 있을 때는 지면 법선이 "위"라서 행성을 한 바퀴 돌 수 있다. 공중에서는 가장 가까운 `GravitySource`로 약하게 떨어진다. **속도를 쓰는 유일한 곳**이고, 기술 상태는 `PlayerTraversalAbilities`가 가진다.

- **필드:** 이동, 점프, 지면 판정
  - `moveSpeed`(2 m/s), `acceleration`(12), `airAcceleration`(5), `turnSpeed`(10)
  - `jumpSpeed`(3.1), `coyoteTime`(0.12초), `jumpBuffer`(0.15초), `shortHopGravity`(2.2): 점프 버튼을 놓고 상승할 때 더하는 중력 배율
  - `groundProbe`(0.12m), `maxGroundAngle`(60°), `groundMask`, `groundFollowSeparationSpeed`(0.5): 이미 서 있을 때 볼록한 모서리에서 허용하는 작은 이탈 속도
- **런타임 입력:** `moveInput`(월드 방향), `jumpHeld`, `speedMultiplier`
- **속성**
  - 지면: `Grounded`, `HasSupport`(물리 지지, 착지 확정과 별개), `SupportNormalVelocity`
  - 방향: `Up`(시각용으로 부드럽게 바뀌는 위), `MovementUp`(이동 기준 위: 서 있으면 지면 법선, 공중이면 중력 반대)
  - 속도: `TangentSpeed`, `VerticalSpeed`, `RadialSpeed`(중력 기준 바깥 방향 속도)
  - 그 밖: `Source`, `TimeWithoutGravity`, `Suspended`(발사 별 등이 운반 중), `Traversal`
- **이벤트:** `Jumped`, `Landed`
- **함수**
  - `Jump()`, `RequestDash()`: 기술 컴포넌트에 요청을 넣는다. 없으면 예전 점프를 쓴다.
  - `Teleport(position, up)`: 속도·상태·입력을 초기화하고 순간 이동한다.
  - `Suspend(bool)`, `Carry(position, up, rotation)`: 발사 별 같은 스크립트 비행에 넘기거나 돌려받는다.
  - `TickTraversal(dt)`(internal): 기술이 있을 때의 한 물리 틱이다.
    - 순서: 지지 판정 → 착지 회복 → 점프 → 대쉬 시작·적용 → 활공 → 일반 이동 → 중력 → 대쉬 상승 제한 → 대쉬 충돌 sweep → 속도 적용
    - 검증 스크립트가 직접 호출한다.
  - `SweepDash`: 대쉬 이동량만큼 캡슐을 쓸어 벽 앞에서 멈추고 대쉬를 끝낸다.
  - `GetWorldCapsule`: 회전·크기를 반영한 캡슐 끝점과 반경
  - `FixedUpdate`: 기술 컴포넌트가 없을 때의 예전 단순 모터(여우 등)

### `PlayerTraversalAbilities`
플레이어 전용 기술 상태다: 더블점프·대쉬·활공·착지 회복. 속도는 직접 쓰지 않는다. 명세는 [PlayerTraversalImplementationSpec.md](../PlayerTraversalImplementationSpec.md)에 있다.

- **필드: 추가 점프**
  - `doubleJumpSpeed`(3)
  - `fallingGravityMultiplier`(1.6): 하강 중 중력 배율
- **필드: 대쉬**
  - `dashSpeed`(8 m/s), `dashDuration`(0.18초), `dashBrakeSeconds`(0.06), `dashExitSpeed`(3)
  - `dashCooldown`(0.65), `dashInputBuffer`(0.1), `dashCollisionSkin`(0.015)
  - `dashMaxRiseSpeed`(0.8 m/s): 대쉬가 땅에서 공중으로 들고 갈 수 있는 바깥 방향 속도의 상한
- **필드: 활공**
  - `glideHoldThreshold`(0.2초), `glideBudgetSeconds`(2.5), `glideFallSpeed`(0.75), `glideMinimumFallSpeed`(0.15)
  - `glideVerticalAcceleration`(4), `glideMoveSpeed`(2.6), `glideSteeringAcceleration`(3)
- **필드: 착지**
  - `supportConfirmSeconds`(0.04): 연속으로 서 있어야 하는 시간
  - `takeoffRecoveryBlockSeconds`(0.1): 이륙 직후 회복을 막는 시간
- **속성**
  - 상태: `Mode`(Locomotion, Dash, Glide, Suspended), `IsDashing`, `IsGliding`, `FlightActive`, `JumpAscentActive`, `DashRiseGuardActive`
  - 자원: `AirJumpsRemaining`, `DashChargeAvailable`, `GlideSecondsRemaining`, `DashCooldownRemaining`
  - 시간: `JumpHeldSeconds`, `SupportStableSeconds`, `TimeSinceTakeoff`
  - 허용: `Unlocked`(배운 기술), `AllowDash` / `AllowAirJump` / `AllowGlide`(디버그 허용), `InputEnabled`
  - 입력 기록: `JumpRequestId`, `ConsumedJumpRequestId` 등. 입력이 정확히 한 번 소비됐는지 검증할 때 쓴다.
- **이벤트:** `AirJumped`, `LandingRecovered`, `AllowedChanged`
- **함수 (public)**
  - `SetUnlocked(mask)`, `SetAllowed(feature, bool)`, `IsEnabled(feature)`: `IsEnabled`는 배웠고 허용된 상태다.
  - `RequestJump()`, `RequestDash()`: 플레이어 입력 경로다. 일시정지·모달이면 무시한다.
  - `SetInputEnabled`, `CancelInputAndActions`, `ResetForTeleport`(자원 전부 회복), `ClearPendingRequests`
- **함수 (internal, `GravityWalker`가 호출)**
  - `QueueSimulationInput(jump, dash)`: 준비 시점의 격리 물리 검사용 입력. UI 시계와 모달 검사가 없다.
  - `BeginTick`: 착지 확정과 자원 회복
  - `TryJump`: 0 없음, 1 지면·코요테, 2 공중
  - `TryStartDash`, `ApplyDash`: 대쉬 시작과 진행. 시작한 정면 방향을 고정하고 곡면을 따라 방향만 운반한다.
  - `GlideTime`: 이번 틱에 활공하는 시간
  - `LimitDashRise`: 대쉬 시작부터 안정적인 착지까지, 공중의 바깥 방향 속도가 중력으로 줄어들기만 하게 한다.
  - `EndDash`, `ClampResidual`: 대쉬 종료 다음 틱에 잔여 속도를 3m/s로 제한
  - `EndTick`
- **열거형:** `TraversalFeature`는 Dash, AirJump, Glide다.

### `PlayerController`
왕자의 키보드·게임패드 입력, 리스폰, 별 개수를 담당한다.

- **필드:** `cameraRig`(`OrbitCamera`), `spawnPoint`, `respawnDistance`(40m): 이보다 멀어지면 자동 리스폰
- **속성:** `StarsCollected`, `StarsTotal`, `NearbyLaunch`(서 있는 발사 별)
- **함수**
  - `Respawn()`: 스폰 지점으로 텔레포트
  - `BeginModalPause` / `ObserveModalPauseInput` / `EndModalPause`: 모달 동안 입력을 멈추고, 닫을 때 눌려 있던 버튼을 한 번 떼게 한다.
  - `Collect()`: 별 하나 획득
- **입력:** 카메라 기준 이동, 점프 유지는 여러 장치를 OR로 합친다. 포커스를 잃거나 앱이 일시정지되면 입력을 지운다.

### `GravitySource`
구형 중력장이다. 걷는 캐릭터는 표면이 가장 가까운 중력원으로 떨어진다.

- **필드:** `surfaceRadius`, `influenceRadius`, `strength`(3.4), `terrainPlan`(물속 감속 판정용 지형 계획)
- **함수:** `Find(p)`(정적, 활성 중력원 중 선택), `Pull(p)`(중심 방향 × 세기)

### `LaunchStar`
마리오 갤럭시식 발사 별이다. 안에 서서 점프하면 베지어 호를 따라 착지점으로 날아가고, 여우가 조금 뒤에 따라온다.

- **필드:** `landing`, `visual`, `flightTime`(2.6초), `arcHeight`(6m), `foxDelay`(0.3초)
- **함수:** `Launch(prince)`, 코루틴 `Fly`. 비행 동안 `GravityWalker.Suspend(true)`

### `Collectible`
떠 있는 별이다. 회전하며 위아래로 흔들리고, 왕자가 닿으면 `PlayerController.Collect()`를 부른다. 필드는 `spinSpeed`, `bobHeight`다.

---

## 2. 카메라

### `OrbitCamera`
행성 기준 궤도 카메라다. 지면을 따라가는 초점과 충돌하지 않는 배치를 담당한다. 설계는 [CameraStabilityDesign.md](../CameraStabilityDesign.md)에 있다.

- **필드: 구도**
  - `fieldOfView`(42), `distance`(5.2), `focusHeight`(0.45), `lookAhead`, `pitch`(30, −10~70)
  - `travelPullBack`(3): 행성 이동 중 물러남. `airPullBack`은 쓰지 않는다.
- **필드: 입력**
  - `mouseSensitivity`, `stickSensitivity`, `keyTurnSpeed`
  - `cardInputMode`: 켜면 Q와 휠을 양 카드에 넘기고, 카메라는 Z/C와 Ctrl+휠을 쓴다.
- **필드: 부드럽게**
  - `followSharpness`, `verticalSharpness`, `upSmoothTime`, `autoTurn`, `introSeconds`(시작 연출)
- **필드: 안정된 행성 기준틀**
  - `jumpHeightAllowance`(1m), `traversalHeightAllowance`, `fallHeightAllowance`: 점프·기술 중 높이 변화를 허용하는 폭
  - `sourceConfirmSeconds`: 중력원이 바뀌었다고 확정하는 시간
- **필드: 충돌**
  - `maxLift`, `minDistance`(1.4), `collisionRadius`(0.2), `occlusionDelay`, `collisionMask`
  - `obstacleComfortDistance`, `obstacleOrbitSmoothTime`: 가까운 장애물을 돌아 보는 궤도
- **속성:** `State`(FollowState), `ReferenceSource`, `CameraUp`, `GroundPoint`, `FocusPoint`, `CurrentDistance`, `DesiredDistance`
- **함수**
  - `ResetForPlanet(source, ground)`: 행성 이동 뒤 초기화
  - `PlanarDirection(input, up)`: 입력을 카메라 기준 이동 방향으로 바꾼다. `PlayerController`가 쓴다.
  - 내부: `SafeDistance`, `ResolveObstacleOrbit`, `Depenetrate`. 벽·집 안에서 카메라가 끼지 않게 한다.
- **`IgnoreMask`(정적):** 집 안에 있을 때 집 레이어를 무시한다(`HouseInterior`).

---

## 3. 행성 월드와 생성 설정

### `PlanetGenerationSettings` (ScriptableObject)
행성 생성의 모든 입력이다. 에셋 3개가 있다([01-프로젝트 구조](01-ProjectStructure.md#설정-에셋)).

- **필드**
  - 프로필과 지면: `profileId`, `groundTemplate`(지면 프리팹), `baselineScale`, `baselineColor`
  - 크기와 중력: `nominalRadius`(기준 반경, 메인 12m), `gravitySurfaceRadius`, `influenceRadius`, `gravityStrength`(3.4)
  - 재질: `originalMaterials`(예전 소행성), `proceduralGroundMaterial`, `proceduralWaterMaterial`, `riverWaterMaterial`(`B612/Water`), `routeGuideMaterial`
  - 행성 색: `palette`(행성 색 후보)
  - 스폰: `spawnDirection`(기존 스폰 방향), `spawnForward`, `spawnClearance`(0.05m), `gravityOuterMargin`
  - 바오밥 구역: `subdivisions`(0~2), `candidatesPerCell`(32), `maxSpawnSlope`(55°)
  - 기능 스위치: `proceduralTerrain`, `terrainFeatureBudget`, `chapterCampaign`, `chapters`(챕터 정의 3개), `naturalTerrain`(`NaturalTerrainSettings`), `terrainAppearance`(`TerrainAppearanceSettings`)
- **상수:** `GeneratorVersion`(22), `NaturalProfileId`, `StylizedProfileId`, `MinimumScale`(0.7), `MaximumScale`(2)
- **함수:** `Chapter(index)`, `Classify(scale)`: 크기 등급 Small, Medium, Large

### `PlanetWorldHost`
행성 월드의 생성과 교체를 맡는다.

- `Initialize(planet, capsule)`: 씬에 미리 있는 `initialWorld`를 준비한다.
- `Prepare(planet, capsule)`: 새 루트 아래에 지면 템플릿, `PlanetSurfaceGrid`, `GravitySource`, 스폰 지점을 만들고 준비한다. 실패하면 파괴하고 예외를 다시 던진다.
- `Activate(world)`: 이전 월드를 끄고 새 월드의 중력을 켠다. 새 월드 이름은 `ActivePlanetWorld`다.

### `PlanetWorldInstance`
한 행성의 월드다. 준비가 끝나면 바꿀 수 없다.

- **필드:** `surface`, `gravity`, `spawn`, `ground`, `groundRenderer`
- **속성:** `Data`(PlanetData), `Terrain`(ProceduralPlanetTerrain)
- **함수**
  - `Prepare(data, settings, playerCapsule)`: [02-실행 흐름 3절](02-RuntimeFlow.md#3-행성-준비-planetworldinstanceprepare)의 전 과정
  - `DisposeWorld()`

### `ChapterTerrainDefinition` (ScriptableObject)
챕터 정의다.

- **필드:** `chapter`(1~3), `title`, `guaranteedEntry`(진입에 필요한 기술), `unlock`(해금 기술), `featureBudget`
- **정적 함수**
  - `Entry(c)`: 기본 진입 기술
  - `Reward(c)`: 1장 대쉬, 2장 추가 점프, 3장 활공
  - `AbilityName(ability)`: 한국어 이름
  - `DefaultWeights(c)`
- **열거형 `TraversalCapability`(플래그):** None, Dash, AirJump, Glide, All

### `TerrainAppearanceSettings` (Serializable)
예전 프로필의 외관(거시 기복, 언덕, 분지, 워프)과 지면 셰이더 팔레트 세기(`paletteStrength`, `heightColorRange`)다. 자연 프로필은 팔레트와 색 범위만 쓴다. `Snapshot()`이 검증된 복사본을 만든다.

---

## 4. 지형 생성

전체 흐름은 [03-행성 지형 생성](03-TerrainGeneration.md)에 있다. 여기서는 클래스 단위로 정리한다.

### `ProceduralPlanetTerrain` (1331줄)
지형 생성의 중심이다. 행성 월드에 컴포넌트로 붙고, 생성 계획(`Plan`)과 메시를 가진다.

- **중첩 클래스 `Plan`(직렬화):** 한 행성의 생성 계획과 결과 기록
  - 기본: `seed`, `version`, `chapter`, `radius`, `profileId`, `natural`(행성 반경에 맞춘 설정 복사본), `appearance`, `spawn`
  - seed와 노이즈 오프셋: `macroSeed`, `hillSeed`, `riverSeed`, `placementSeed`, `surfaceSeed`, `roughnessSeed`, `peakSeed`, `canyonSeed`, `*Offset`
  - 결과 객체(직렬화하지 않음): `river`(`TerrainWater.River`), `valley`, `spawnPad`, `erosion`, `moisture`
  - 상태 문자열: `riverStatus`, `valleyStatus`, `spawnPadStatus`
  - 도랑과 습도 통계: `gullyCount`, `gullyLength`, `gullyDeepest`, `gullyMeanDepth`, `moistureMean`
  - 시간 기록: `riverPlanMilliseconds`, `meshMilliseconds`, `spawnPadMilliseconds`, `validationMilliseconds`
  - 배치 결과: `placement`(`NaturalTerrainPlacement.Report`)
  - 예전 프로필용: `features`, `landforms`, `regions`, `patches`, `routeTangent`, `requiredKind`, `fixedPlacement`, `safeFallback`
- **중첩 클래스:** `Feature`(예전 능력 코스 요소), `RouteConnector`(예전 보호 통로)
- **속성:** `GenerationPlan`, `OwnedMesh`(지면 메시), `WaterMesh`, `NaturalPlacement`(internal)
- **높이 함수(정적, 스레드 안전)**
  - `Height(plan, d)`: 최종 지면(물길 포함)
  - `BaseHeight`: 물길 전
  - `FreeHeight`: 자연 프로필 지형 + 빈터
  - `UnpaddedFreeHeight`(private): 빈터 전
  - 자연 지형 구성 요소: `Relief`(v18 기복), `Escarpment`, `EscarpmentGeometry`(v18 벼랑), `Mountains`, `PlaceMountains`(v19 산)
  - 예전 기능: `Rugged`, `RuggedRaw`, `CliffTerrace`, `Terrace`(v15~v16 거친 고지대), `CanyonDepth`, `CanyonGeometry`(v17 협곡)
  - 노이즈: `Basis`, `Simplex`, `Noise`, `Roughness`
- **메시 함수**
  - `BuildMesh(plan)`: 등각 큐브 구면. 정점 색은 `NaturalSurfaceWeights`, UV1은 습도이고 병렬로 계산한다(`ForEachRange`).
  - `SnapToEscarpment`, `SnapToCliff`(정점을 가장자리로 이동), `UnsnapFolds`(뒤집힌 삼각형 되돌리기)
  - `AssignMesh`, `CreateWater`
- **검증용 공개 함수:** `InCliffZone(plan, d)`(메시가 정확히 따라갈 수 없는 벼랑·협곡 근처), `RuggedTerms`, `RuggedWeight`
- **준비 함수**
  - `PrepareNatural(seed, radius, chapter, settings, filter, collider)`(internal): 자연 프로필
  - `Prepare(...)`: 예전 프로필. 보행 검사와 보수 반복
- **배치 필터:** `AllowsSpawn(point, normal)`. 자연 프로필이면 `NaturalTerrainPlacement.AllowsBaobab`로 넘긴다.
- **예전 경로 함수:** `CreatePlan`, `MainRoute`, `FeatureRoute`, `MainRouteDistance`, `ConnectorDistance`

### `NaturalTerrainSettings` (Serializable)
자연 지형의 모든 수치다. 필드 설명은 [03-행성 지형 생성 5절](03-TerrainGeneration.md#5-자연-지형-설정-naturalterrainsettings)에 있다.

- `Snapshot(radius)`: 범위를 검사하고 행성 반경에 맞게 크기를 바꾼 복사본을 돌려준다. 범위 밖이면 예외다.
- **열거형:** `NaturalNoiseBasis`(Value, Simplex), `NaturalRiverMode`(Trace, Flow)

### `NaturalTerrainValley` (v19+, internal)
둘레 절반 넘는 강이 흐를 골짜기와 하구 호수를 높이장에 먼저 새긴다.

- **필드:** `route`(경로 방향 목록), `lakeCentre`, `lakeRadius`, `length`, `minRiverLength`, `maxCut`, `meanCut`, `status`
- **함수**
  - `Build(plan)`: 호수 위치 → 우선순위 범람 경로 → 수원 선택 → `Smooth` → `Meander`(파장 약 15m, 옆으로 약 2m) → `Shape`(바닥·벽·호수 그릇·조회 청크)
  - `Apply(metres, h)`: 그 지점의 높이에 골짜기와 호수를 섞는다.
- **상수:** `Stride`(2, 경로 점 하나 건너 조회), `FloorFall`(0.003, 하류 경사)

### `NaturalTerrainSpawnPad` (v21~v22)
스폰 주변 NPC용 평면 빈터다. 설명은 [03-행성 지형 생성 7절](03-TerrainGeneration.md#7-스폰-빈터-naturalterrainspawnpad)에 있다.

- **필드:** `centre`(중심 방향), `level`(평면 높이), `core`(평면 반경), `blend`(띠 폭), `planar`, `maxCut`, `maxFill`, `offsetMeters`(기존 스폰 방향에서 옮긴 거리), `status`
- **함수:** `Build(plan, preferred)`(internal), `Apply(d, h)`, `Weight(d)`, `Target(d)`, `InCore(d, margin)`

### `NaturalTerrainFlow` (v11+)
구면 셀 그래프 위의 물 흐름 라우팅이다.

- **중첩 `Field`:** 셀, 방향, 높이, 면적, 누적 유량, 하류 셀(`receiver`), 상류 셀(`donors`)
- **함수**
  - `Build(plan)`: 약 0.6m 셀 그래프와 가장 가파른 하강 라우팅
  - `Plan(plan, field)`: 강 계획
    - 골짜기 강을 먼저 맞춘다.
    - 안 되면 상위 8개 연못 후보에 범람 라우팅(`Flood`)을 해서 가장 큰 유입을 상류로 따라가 강으로 만든다.
    - `Fit`은 경로를 다듬고 양쪽 강둑 아래에 머무는 하류로만 내려가는 수면을 맞춘다.
  - `FillPond`, `Rim`: 분지를 채워 얕은 연못을 만들고, 수면이 물가 밖으로 보이지 않는 반경을 구한다.
  - `Gorge`: v17 협곡 구간(현재 꺼짐)
- **중첩 `Moisture`:** 렌더링 전용 습도. 상류 유량 40%, 지형 위치 30%, 고도 30%를 섞는다. `Sample(d)`

### `NaturalTerrainErosion` (v12+)
도랑을 판 흐름장에서 상류 셀이 `erosionMinCells` 이상인 셀을 물길로 본다. 물길을 따라 깊이가 유량의 로그로 자라는 얕은 도랑을 판다. 단면은 (1−t²)²다.

- `Carve(d)`: 그 방향에서 파낸 깊이
- 통계: `gullies`, `totalLength`, `deepest`, `meanDepth`

### `NaturalTerrainRiver` (v10)
높은 수원에서 가장 가파른 하강을 따라 내려가는 강 추적이다. 지금은 흐름 강이 실패할 때만 쓴다(`Plan`, `Trace`, `TryFit`).

### `PlanetSurfaceGrid`
행성 표면을 논리 구역(정이십면체 세분)과 바오밥 후보 슬롯으로 나눈다. **지면 메시는 바꾸지 않는다.**

- **필드:** `ground`, `subdivisions`, `candidatesPerCell`, `maxSlope`, `drawCellGizmos`, `CandidateFilter`(후보 거르기 함수)
- **중첩 클래스:** `SurfaceCell`(id, 꼭짓점 a/b/c, 입체각, 이웃 3개), `SpawnSlot`(구역, 지역 좌표의 점과 법선, 점유 여부)
- **속성:** `Cells`, `SlotCount`, `EmptyCellCount`, `SizeScale`, `PreparedFrequency`
- **함수**
  - `CalculateFrequency(level, scale)`: 1배 80구역, 1.5배 180, 2배 320
  - `ConfigureResolution`, `Prepare(seed)`: 구역마다 후보를 레이캐스트로 만든다.
  - `TryProject(localDirection, out point, out normal)`: 지면 투영. 배치에서 널리 쓴다.
  - `CreateTopology(frequency)`(정적): 구역 그래프. 흐름·배치 분석에도 쓴다.
  - `WorldPoint`, `WorldNormal`, `Contains`

### 예전 프로필 전용
- **`TerrainRegions`:** 지역 종류(평지, 언덕, 거친 곳)와 패치를 만들고 섞는다(`Build`, `BlendWeights`).
- **`TerrainLandforms`:** 큰 언덕·분지를 배치하고 높이를 준다. 접근로(`RouteConnector`)도 만든다.
- **`TerrainTraversalVerifier`:** 준비 시점에 완성된 충돌체 위에서 실제 모터로 걷는다.
  - 기본 통로 왕복과 능력 지름길을 검사하고(`Verify`, `Walk`, `Shortcut`) 결과를 `Verdict`, `Cost`로 돌려준다.
  - `Walk`는 자연 지형 검증 스크립트도 리플렉션으로 쓴다.

---

## 5. 물

### `TerrainWater` (정적)
- **중첩 `River`:** 강 데이터([03-행성 지형 생성 6절](03-TerrainGeneration.md#6-강과-물-terrainwater))
- **높이:** `Height(plan, n, original)`(물길·강둑·연못을 반영한 지면), `PondCarve`, `SmoothBankHeight`, `NaturalChannelHeight`
- **조회**
  - `Sample`, `SampleWidth`, `WidthAt(river, i)`: 가장 가까운 물길 점과 폭
  - `IsWet(plan, d, margin, out surface)`: 물 아래인지
  - `MovementFactor`: 물속 보행 배율 0.65
  - `PondShore`: 연못 물가 색조
- **협곡(v17, 현재 꺼짐):** `GorgeRaise`, `SnapToGorge`, `InGorge`
- **색인:** `BuildIndex(river)`(internal): 강 구간을 위치별 칸에 넣어 근처만 찾는다.
- **메시:** `CreateMesh(plan)` → `CreateNaturalMesh`(강 리본 5열 + 연못 원판 `AppendPond`)

---

## 6. 배치와 바오밥 후보

### `NaturalTerrainPlacement`
완성된 충돌체 위의 보수적인 보행 연결 분석과 배치를 맡는다.

- **생성자:** `(plan, surface, capsule, clearance, preferred, campaign)`
  - 셀마다 실제 캡슐 겹침과 경사로 설 수 있는지 본다(`Clear`, `TryStand`).
  - 이웃 사이를 0.3m 간격으로 걸어 보며 경사 이하인지 확인하고(`CanConnect`, `WithinGrade`) 연결 영역을 합친다(`Root`).
  - 가장 큰 영역의 비율이 기준보다 작으면 실패다.
- **배치**
  - `FindLocation`: 선호 방향에서 가까운 마른 셀부터, 간격과 발밑 면적(`DryFootprint`)을 확인한다.
  - `PlaceSites`: 스폰에서 120° 간격, 3.2~9m 거리에 상호작용 지점 3곳
- **`AllowsBaobab(point, normal)`**(internal): 바오밥 후보 필터. 다음을 모두 만족해야 한다.
  - 경사가 기준 이하
  - 물에서 0.24m 이상 떨어짐
  - 캡슐이 겹치지 않음
  - 스폰 1.1m 밖, 지점 0.8m 밖, 빈터 평면 밖
  - 가장 가까운 연결 셀과 이어짐
- **`CheckCapacity(count, spacing)`:** 동시 활성 싹 수만큼 서로 떨어진 후보가 있는지 확인한다.
- **중첩 `Report`:** 노드 수, 설 수 있는 수, 연결 간선, 연결 면적 비율, 경사 한계, 스폰, 상호작용 지점, 후보 수, 빈 구역 수, 시간
- **중첩 `Location`:** 방향, 지역 점, 법선, 노드

### `CampaignWorldSites`
캠페인 행성의 상호작용 지점 3곳이다. 각 지점은 충돌체 없는 표지(기둥 등)다.

- 0: 금색 기둥(능력 배우기), 1: 작물 조사, 2: 만남
- **함수:** `Initialize(flow, world)`, `CanInteract(index)`, `Nearest()`, `Prompt()`
- E 키 입력은 `StarSystemFlowController.RequestSiteInteraction`으로 넘긴다.
- 방문 시간(`ElapsedSeconds`)과 이동 거리(`TravelMetres`)를 기록한다.
- `Glyphs`는 TMP 폰트를 구울 때 쓰는 한국어 글자 모음이다.

---

## 7. 항성계·여정·지도

### `StarSystemData.cs`
데이터 클래스(`StarSystemData`, `PlanetData`, `PlanetSpec`, `PlanetState`, `RunProgressData` 등)와 `StarSystemFactory`가 있다. 데이터 클래스는 [02-실행 흐름 8절](02-RuntimeFlow.md#8-데이터-모델-starsystemdatacs)에 정리했다.

**`StarSystemFactory`**
- `Create(seed, settings, chapter)`: 6노드 항성계 생성
- `DeriveSeed(seed, stream, node)`: 결정론적 seed 파생
- `ValidatePlanet`, `Validate`: 버전·프로필·크기·연결 검사

### `StarSystemFlowController` (실행 순서 300)
여정 전체의 흐름을 관리한다. 시작, 지도, 이동, 챕터 진행, 상호작용 지점, 디버그 스킵을 맡는다.

- **필드:** `worldHost`, `player`, `surge`, `hand`, `pause`, `view`, `randomizeRunSeed`(true), `runSeed`(612), `transitionSeconds`(0.35)
- **속성**
  - 데이터: `SystemData`, `Progress`, `Current`
  - 상태: `WorldReady`, `IsMapOpen`, `IsTravelling`, `SystemComplete`, `CanOpenMap`, `CanAdvanceChapter`, `CampaignEnabled`
  - 그 밖: `Notice`(4초 안내 문구), `Sites`, `CompletedSystems`
- **이벤트:** `Changed`
- **함수**
  - 지도: `OpenMap`, `CloseMap`, `RequestMap`
  - 이동: `CanTravel(id)`, `RequestTravel(id)`, `RequestNextChapter()`, `CancelMapAndTravel()`
  - 상호작용: `RequestSiteInteraction(sites, index)`
  - 디버그: `HasCapabilities(mask)`, `GetDebugSkipState`, `RequestDebugSkipBaobab`, `CancelDebugSkipBaobab`
  - 내부: `BeginTransition`, `Travel`(코루틴), `BindWorld`, `SnapshotCurrent`, `ApplySiteInteraction`, `ApplyDebugSkip`
- **규칙:** 요청은 기록만 하고 `LateUpdate`에서 적용한다. 게임오버가 우선한다.

### `StarSystemMapView`
지도 UI다. 노드, 연결선, 툴팁(크기 대·중·소), 상태 문구, 닫기·다음 챕터 버튼, 전환 화면이 있다.

- **함수:** `ShowMap`, `ShowTransition`, `Refresh`, `BuildNodes`, `Hover`, `HideTooltip`, `NodeRect`
- **같이 쓰는 컴포넌트:** `PlanetMapNodeGraphic`(원형 노드 UI 그래픽), `PlanetMapNodeHover`(포인터 진입·이동·이탈을 지도 뷰에 전달)

### `ChapterJourneyHUD`
챕터, 수집한 능력, 다음 목표, 상호작용 안내를 표시한다. `Abilities(mask)`는 능력 목록 문자열을 만든다.

### `StarSystemDevelopmentSmoke`
개발 빌드에서 켜는 로컬 연기 검사다. 네트워크나 릴리스 코드는 없다. 행성 준비, 강, 지면 텍스처, GPU 시간을 확인하고 프레임을 캡처한다.

---

## 8. 바오밥 싹과 위험도

설계는 [BaobabThreatDesign.md](../BaobabThreatDesign.md)에 있다.

### `BaobabSurge` (실행 순서 −100)
시간 제한이 있고 공간적으로 고르게 퍼지는 싹 스폰 페이즈다.

- **필드**
  - 연결: `surface`, `sproutPrefab`, `player`, `spawnPoint`, `threat`
  - 수량과 간격: `durationSeconds`(120), `targetSprouts`(40), `maxActiveSprouts`(6), `minimumSpacing`(1.4m), `playerClearance`(0.9m), `spawnClearance`(0.7m)
  - seed와 시작: `randomizeSeed`, `seed`, `automaticStart`(지도 흐름이 있는 씬에서는 흐름이 끈다)
- **중첩 `SpawnRecord`:** `slot`, `instance`, `scheduledTime`, `spawnedAt`, `type`, `growth`, `lifeCycle`, `Stage`, `FeedingLamb`, `IsBeingEaten`, `IsActive`
- **속성**
  - 시간: `ElapsedSeconds`(스폰 시계), `SimulationSeconds`(스폰이 끝나도 계속 흐르는 성장 시계), `RemainingSeconds`
  - 상태: `IsRunning`, `IsComplete`, `IsGameOver`, `WasSkippedByDebug`
  - 통계: `Attempts`, `SkippedSpawns`, `CapacityBlockedSpawns`, `SpawnedCount`, `ActiveSproutCount`
- **이벤트:** `Changed`
- **함수**
  - `Begin(surfacePrepared)`, `StopAndClear()`
  - `Advance(delta)`: 게임 시간 진행
  - `RemoveSprout(instance)`: 양이 먹었을 때
  - `RefreshLifeStages()`, `RefreshStage(record)`
  - `CompleteForDebug(hand)`(internal)
  - 내부 `TrySpawn`: 가장 덜 쓴 구역 우선, 가장 가까운 싹과의 거리를 무작위로 흔들어 최대화

### `BaobabThreatMeter` (실행 순서 50)
- **필드:** `surge`, `profile`, `playerInput`, `cameraInput`, `runCapacityOverride`(행성 데이터의 용량)
- **속성:** `State`(Phase), `IsGameOver`, `Capacity`, `RawThreat`, `Fraction`, `GrowthPerSecond`, `RunType`, `RunGrowth`, `RunLifeCycle`
- **이벤트:** `Changed`, `GameOver`
- **함수**
  - `Evaluate()`: 살아 있는 싹 기여의 합. 용량 이상이면 게임오버 일시정지와 입력 차단
  - `Contribution(record)`
  - `ResetForRun()`, `RestartAttempt()`: 재시도는 씬을 다시 불러 새 여정을 시작한다.

### `BaobabTypeDefinition` (ScriptableObject)
싹 종류다.

- **필드:** `typeId`, `displayName`, `initialThreat`(2), `linearGrowth`(0.15), `quadraticGrowth`(0.005), `maximumThreat`(25), `saplingAtSeconds`(20), `treeAtSeconds`(40), `saplingPrefab`, `treePrefab`
- **함수:** `Snapshot()`은 `BaobabGrowth`를, `SnapshotLifeCycle()`은 `BaobabLifeCycle`을 돌려준다(시작 시점 값 고정).
- **같은 파일의 타입**
  - `BaobabStage`: Sprout, Sapling, Tree
  - `BaobabGrowth`: `Evaluate(age)`, `Rate(age)`
  - `BaobabLifeCycle`: `StageAt(age)`, `PrefabFor(stage)`

### `PlanetBaobabProfile` (ScriptableObject)
위험도 용량(`threatCapacity` 100)과 기본 싹 종류(`defaultType`)만 가진다.

---

## 9. 양 카드

설계와 결과는 [LambPrototypeDesign.md](../LambPrototypeDesign.md)에 있다.

### `LambCardHand`
양 카드 손패와 소환·회수를 맡는다.

- **필드**
  - 연결: `surge`, `player`, `lambPrefab`, `ringMaterial`
  - 카드와 시간: `startingCards`(4), `activeSeconds`(20)
  - 먹는 시간: `secondsPerSprout`(7), `secondsPerSapling`(10), `secondsPerTree`(13)
  - 반경: `eatingRadius`(1.7m), `rangeGrace`(0.25), `intentRadius`(2.05), `pickupRadius`(0.9m)
- **속성:** `SelectedIndex`, `CardCount`, `InHandCount`, `Deployed`, `ClaimCount`, `Message`
- **이벤트:** `Changed`
- **함수**
  - 카드 선택과 소환: `SelectCard`, `SelectNext`, `TrySummonSelected`, `TryGetPlacement`(소환 위치와 노릴 싹 미리보기)
  - 싹 배정: `ClaimTarget`, `OwnsTarget`, `ReleaseTarget`(양 한 마리가 싹 하나를 독점한다)
  - 회수: `RecoverCard`, `RecallForTravel`(이동 전 전부 회수), `BindSurfaceAfterTravel`
  - 그 밖: `EatingSeconds(stage)`, `SurfaceDistance(a, b, centre)`(구면 거리), `CreateRing`, `DrawRing`(범위 고리), `Notify`(안내)

### `LambAgent`
소환된 양 한 마리다.

- **상태(`LambState`):** Grazing(먹는 중), Pickup(줍기 대기), Collected
- **속성:** `Age`, `EatingProgress`, `EatingDuration`, `EatingFraction`, `EatingStage`, `SproutsEaten`, `CardIndex`, `Target`, `RemainingSeconds`
- **함수**
  - `Initialize(owner, cardIndex, preferred)`
  - `Advance(seconds)`: 범위 안 싹을 찾아 먹고, `activeSeconds`가 지나면 줍기 상태가 된다.
  - 줍기 상태로 0.8초 지난 뒤 플레이어가 `pickupRadius` 안에 오면 카드가 돌아온다.

---

## 10. UI·HUD

| 스크립트 | 역할 |
|---|---|
| `SurgeTimerHUD` | 바오밥 스폰 남은 시간 막대와 개수. 표시만 하고 상태는 `BaobabSurge`가 가진다 |
| `BaobabThreatHUD`(60) | 위험도 막대, 퍼센트, 상태 문구, 게임오버 패널과 재시도 버튼 |
| `LambCardHUD` | 화면 아래에 부채꼴로 펼친 카드 4장. 소환한 카드는 손에서 빠진다 |
| `LambEatingIndicators`(110) | 먹는 중인 양 위의 원형 진행도(`EatingProgressGraphic`) |
| `EatingProgressGraphic` | 12시에서 시계 방향으로 차는 원 그래픽(uGUI `OnPopulateMesh`) |
| `SproutHorizonIndicators`(100) | 화면 밖이나 행성 뒤의 싹·줍기 양을 지평선 말풍선으로 표시. 거리에 따라 크기를 바꾼다(`ScaleForDistance`). 행성 가림(`IsBehindPlanet`)과 지평선 점(`TryGetHorizon`)을 계산한다 |
| `SproutBubbleGraphic` | 말풍선 그래픽. 꼬리가 움직여도 싹·양 아이콘은 서 있다 |
| `DemoHUD` | 디오라마 데모의 조작 도움말과 별 개수(`OnGUI`). H로 숨긴다 |
| `ChapterJourneyHUD` | [7절](#7-항성계여정지도) |
| `DeveloperDebugHUD` | [11절](#11-일시정지모달디버그) |

한국어 문자열이 있는 UI는 TMP 폰트를 구울 때 쓰는 `Glyphs` 상수를 가진다. 새 문구를 넣으면 이 상수와 폰트 에셋(빌더가 다시 구움)을 함께 갱신해야 글자가 빠지지 않는다.

---

## 11. 일시정지·모달·디버그

### `GamePauseCoordinator`
씬 단위 일시정지 관리자다. 사유(`GamePauseReason`)는 DebugOverlay, GameOver, MapOverlay, PlanetTransition이다.

- `GetOrCreate(scene)`
- `AcquirePause(owner, reason)`, `ReleasePause(owner)`, `HasReason(reason)`
- `IsPaused`, `OwnerCount`
- `ResetForSceneReload()`

### `GameplayModalInput` (정적)
한 번에 한 모달만 입력을 가진다.

- `TryBegin(requester, player)`, `Observe(requester)`, `End(requester)`
- `IsOpen`, `BlocksGameplayInput`, `BlocksMouseInput`
- 커서 잠금 상태를 저장했다가 되돌린다. 닫은 프레임과 마우스를 떼기 전까지는 입력을 막는다.

### `DeveloperDebugController` (실행 순서 −1000)
F12 개발자 디버그다.

- `Available`: 에디터나 개발 빌드일 때만
- `Open`, `Close`, `IsOpen`, `RequestBaobabSkip()`
- 열면 `DebugOverlay`로 일시정지한다. 게임오버 일시정지는 따로 유지한다.

### `DeveloperDebugHUD`
- 대쉬·추가 점프·활공 토글: 배운 기술만 켤 수 있다.
- 바오밥 페이즈 스킵 버튼과 결과 문구(`Skip*` 상수), 닫기 버튼
- 명세: [DeveloperDebugModeSpec.md](../DeveloperDebugModeSpec.md), [DeveloperBaobabSkipImplementationSpec.md](../DeveloperBaobabSkipImplementationSpec.md)

---

## 12. 캐릭터 애니메이션과 연출

| 스크립트 | 역할 |
|---|---|
| `PrinceCharacterAnimator` | 리깅된 왕자의 Animator. 대기·걷기·달리기 블렌드와 공중 자세. 숨쉬기, 여우 바라보기, 점프·착지 늘리기, 회전 기울기를 덧입힌다. 목도리는 Verlet 사슬로 흔든다(`scarf*` 필드) |
| `PrinceAnimator` | 예전 부품식 왕자 모델의 절차적 달리기·점프·대기와 목도리 |
| `FoxRigAnimator` | 리깅된 여우의 절차적 애니메이션: 대각 속보, 척추, 꼬리 4관절, 귀, 도약, 앉아서 왕자 바라보기. `PreviewPose`로 편집 모드 미리보기 |
| `FoxAnimator` | 예전 부품식 여우 |
| `FoxFollower` | 여우가 왕자를 따라간다. 거리에 따라 걷기·달리기·순간이동하고, 왕자가 뛰면 조금 늦게 뛴다 |
| `PartRig` | 부품식 모델 한 부분의 쉬는 자세. 루트 기준 축으로 회전(`Pose`)·이동(`Offset`) |
| `HouseInterior` | 왕자가 집 안에 있으면 바깥 벽·지붕을 반투명하게 하고 카메라가 집과 충돌하지 않게 한다 |
| `NightSky` | 밤하늘 스카이박스. 달을 달빛 반대편에 두고 화면 안에 별똥별(`Launch`)을 띄운다 |
| `Spinner` | 일정 회전과 위아래 흔들림 |

---

## 13. 이동 실험실과 기타

| 스크립트 | 역할 |
|---|---|
| `TraversalTestCourse` | `B612_TraversalTest` 씬 전용. 구간 3개(골짜기와 우회로, 높은 턱과 경사로, 활공 출발대와 낮은 착지대)를 고르고 시간을 잰다(`SelectStation`, `Project`) |
| `TraversalTestCourseGeometry` | 고정 실험 행성(반경 12m)의 수작업 지형. 골짜기 폭 4.2m·깊이 2m, 턱 2m, 활공 간격 7m. `Height`, `CreateMesh`, 기본 경로 |

---

## 14. 에디터 스크립트

`Assets/B612/Editor/`

- **`PlanetSurfaceGridEditor`:** `PlanetSurfaceGrid` 인스펙터. 구역 수와 후보 수 같은 준비 상태를 보여 준다.
- **`ProceduralPlanetTerrainEditor`:** `ProceduralPlanetTerrain` 인스펙터. 생성 계획의 상태 문자열과 통계를 보여 준다.

Play 중 Hierarchy의 `ActivePlanetWorld` → `ProceduralPlanetTerrain`에서 실제로 적용된 값과 배치 결과를 볼 수 있다.
