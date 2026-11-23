# 01. 프로젝트 구조

[← 인계 문서 목차](README.md)

## 최상위 폴더

| 경로 | 내용 |
|---|---|
| `Assets/B612/` | 게임의 모든 자체 에셋과 코드 |
| `Assets/Captures/` | README용 캡처 이미지 |
| `Assets/StreamingAssets/ThirdPartyNotices/` | 플레이어 빌드에 포함되는 서드파티 에셋 고지 |
| `Assets/_Recovery/` | Unity가 충돌 뒤 남긴 복구 씬. 추적하지 않는 임시 파일 |
| `AgentScripts/` | 씬·에셋을 만드는 빌더, 검증·측정 스크립트, 런타임 스크립트 미러. 게임 어셈블리에 들어가지 않는다 |
| `AgentScripts/runtime/` | `Assets/B612/Scripts`와 같은 내용이어야 하는 미러([규칙](README.md#꼭-지켜야-하는-작업-규칙)) |
| `AgentScripts/blender/` | Blender 5.2용 캐릭터 모델·리그 생성 스크립트(`build_characters.py`, `build_fox_rig.py`, FBX 점검 도구) |
| `AgentScripts/Fixtures/` | 검증 기준 데이터(`PlanetGridV1Baseline.json`: 행성 구역 격자 기준값) |
| `Docs/` | 기획·설계·구현 명세·결과 문서(한국어). 이 인계 문서는 `Docs/Handover/` |
| `Docs/Examples/` | `PlanetDataExample.json`: 행성 데이터 직렬화 예시 |
| `Artifacts/` | 검증 로그와 비교 캡처(PNG는 LFS). 예: `Artifacts/NaturalPlanetTerrain/PlanarClearingV22/` |
| `Builds/` | 로컬 플레이어 빌드 결과(검증용) |
| `Packages/`, `ProjectSettings/` | Unity 패키지·프로젝트 설정 |
| `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `*.csproj` | Unity가 만드는 파일. 직접 수정하지 않는다 |

## `Assets/B612/` 안

| 경로 | 내용 |
|---|---|
| `Scripts/` | 게임 런타임 코드(네임스페이스 `B612`, 66개 파일). [04-스크립트 레퍼런스](04-ScriptReference.md) |
| `Editor/` | 에디터 확장. `PlanetSurfaceGridEditor`(구역 격자 인스펙터), `ProceduralPlanetTerrainEditor`(생성 계획 인스펙터) |
| `Scenes/` | 씬 7개([아래](#씬)) |
| `Settings/` | 생성 설정, 챕터 정의, 바오밥 종류·프로필, 지면·물 재질([아래](#설정-에셋)) |
| `Shaders/` | URP 셰이더 5개([아래](#셰이더)) |
| `Prefabs/` | 바오밥 싹·묘목·나무 외형, 구름 양, 빈 행성 지면 템플릿(`PlanetTravel/EmptyPlanetGround.prefab`) |
| `Models/` | 왕자(`Prince.fbx`, Meshy AI), 여우(`Fox.fbx`, `FoxRig.fbx`, Blender), 양·싹·묘목·나무 모델 |
| `Generated/` | `B612Builder`가 만든 메시·재질·프리팹·볼륨·애니메이터(`Prince.controller`)·종이 텍스처. **직접 수정하지 않는다** |
| `Art/ThirdParty/` | `JulioVII/GrassDirt`(지면 잔디·흙), `3DTexturesMe/StylizedCliffRock006`(절벽 암석, 사용 중)·`003`(비교용, CC0). JulioVII 에셋의 이용 조건은 `Assets/StreamingAssets/ThirdPartyNotices/`의 고지 파일을 따른다 |
| `UI/` | 양 카드 이미지(`Cards/`), 디버그·지도 한국어 TMP 폰트 에셋 |

## 씬

빌드 순서는 `ProjectSettings/EditorBuildSettings.asset`에 있다.

| 씬 | 빌드 | 용도 |
|---|---|---|
| `B612_ChapterJourney` | 0 | **메인 플레이 씬.** 3챕터 여정(`chapterCampaign=true`, `ChapterJourneyGeneration.asset`). 플레이어 빌드도 여기서 시작한다 |
| `B612_Night` | 1 | 원래 밤하늘 디오라마 데모 |
| `B612_Diorama` | 2 | 낮 버전 디오라마 |
| `B612_EmptyPlanet` | 3 | 게임 방식 검증 씬. 싹·양·위험도·이동 기술·F12 디버그·지도 이동을 한 행성에서 시험한다. 검증 스크립트 대부분이 이 씬의 Play 모드를 가정한다 |
| `B612_TraversalTest` | 빌드 밖 | 이동 지형 실험실. 고정 골짜기·높은 턱·활공대(`TraversalTestCourse`) |
| `B612_ProceduralTerrainTest` | 빌드 밖 | 예전 절차적 지형 2단계 비교 씬 |
| `B612_Turnaround` | 빌드 밖 | 왕자·여우 3면도 |

`B612_EmptyPlanet`의 `GroundSurface`는 원래 소행성 메시(`Generated/B612_Meshes.asset`의 `Asteroid B-612`)다. Play 모드에서는 `PlanetWorldHost`가 생성 설정의 지면 템플릿으로 행성을 새로 만든다.

## 설정 에셋

| 에셋 | 타입 | 내용 |
|---|---|---|
| `Settings/Chapters/ChapterJourneyGeneration.asset` | `PlanetGenerationSettings` | 메인 여정의 행성 생성 설정. 프로필 `terrain.natural.v1`, 기준 반경 12m, `chapterCampaign=true`, 자연 지형 설정(`naturalTerrain`) 전체 |
| `Settings/PlanetTravel/EmptyPlanetGeneration.asset` | `PlanetGenerationSettings` | `B612_EmptyPlanet` 씬용. 같은 자연 지형 프로필이고 캠페인은 꺼져 있다(모든 기술 사용 가능) |
| `Settings/PlanetTravel/ProceduralTerrainTestGeneration.asset` | `PlanetGenerationSettings` | 절차적 지형 비교 씬용 |
| `Settings/Chapters/Chapter1~3.asset` | `ChapterTerrainDefinition` | 챕터 번호·제목·진입에 필요한 기술·해금 기술(1장 대쉬, 2장 추가 점프, 3장 활공) |
| `Settings/BasicBaobab.asset` | `BaobabTypeDefinition` | 기본 싹 종류: 위험도 성장 곡선, 묘목·나무로 자라는 시간과 프리팹 |
| `Settings/PrototypeBaobabProfile.asset` | `PlanetBaobabProfile` | 위험도 용량(100)과 기본 싹 종류 |
| `Settings/PlanetTravel/StylizedPlanetGround.mat` | `B612/Planet Ground` | 절차 행성 지면 재질 |
| `Settings/PlanetTravel/StylizedWater.mat` | `B612/Water` | 강·호수 물 재질 |
| `Settings/PlanetTravel/ShallowRiver.mat` | | 생성 설정의 `proceduralWaterMaterial`. 예전 프로필의 얕은 물이다. `CampaignWorldSites`가 상호작용 지점 표지 재질의 원본으로도 쓰므로 바꾸면 안 된다 |
| `Settings/PlanetTravel/RouteGuide.mat` | | 예전 프로필의 경로 안내 재질 |
| `Settings/Chapters/Chapter Journey Korean SDF.asset` | TMP 폰트 | HUD 한국어 글리프 |

생성 설정 에셋 3개는 `naturalTerrain` 블록을 각각 가지고 있다. 지형 기본값을 바꾸면 세 파일 모두 고친다.

## 셰이더

| 셰이더 | 파일 | 역할 |
|---|---|---|
| `B612/Planet Ground` | `PlanetGround.shader` | 절차 행성 지면 |
| `B612/Water` | `Water.shader` | 강·호수 물 |
| `B612/Night Sky` | `NightSky.shader` | 절차적 밤하늘 스카이박스 |
| (글로우 파티클) | `GlowParticle.shader` | 반딧불·별가루의 가산 혼합 부드러운 점. 텍스처 없이 UV에서 감쇠를 계산한다 |
| `B612/Traversal Course` | `TraversalCourse.shader` | 이동 실험실 고정 코스 지면 |

**`B612/Planet Ground`**
- 행성 색 `_BaseColor`와 종이 질감 `_Paper`를 쓴다.
- 높이에 따라 흙·초목·암석 팔레트를 섞는다(`_EarthColor`, `_VegetationColor`, `_RockColor`, `_HeightColorRange`).
- 잔디·흙 텍스처를 타일링한다(`_GrassTileMeters`, `_DirtTileMeters`).
- 가파른 면에는 행성 반지름 방향으로 투영한 절벽 암석 텍스처를 입힌다(`_RockTileMeters`, `_RockTextureStrength`).
- 메시 UV1의 습도로 물길 주변을 짙게 칠한다(`_MoistureStrength`, `_MoistureMean`).

**`B612/Water`**
- 깊이에 따라 얕은 색과 깊은 색을 섞는다(`_ShallowColor`, `_DeepColor`).
- 물가에 거품(`_Foam*`), 하류로 흐르는 잔물결과 줄무늬(`_Ripple*`, `_StreakStrength`, `_FlowSpeed`), 호수의 느린 표류(`_PondDrift`)를 그린다.
- 게임 시간을 쓰므로 일시정지하면 물결도 멈춘다. 캡처용으로 `_DebugTime`을 고정할 수 있다.
- 메시 UV0은 흐름 좌표(m), UV1은 (깊이, 유속, 중심선 거리 0..1, 종류: 0 강 / 1 호수)다.

**`B612/Night Sky`**
- 하늘 그라데이션, 반짝이는 별, 은하수와 먼지띠, 성운, 위상이 있는 달을 그린다.
- `NightSky.cs`가 달을 달빛의 반대 방향에 두고 별똥별을 띄운다.

## 패키지

`Packages/manifest.json`의 주요 패키지는 다음과 같다.

| 패키지 | 버전 |
|---|---|
| URP | 17.3.0 |
| Input System | 1.20.0 |
| uGUI | 2.0.0 |
| AI Navigation | 2.0.14 |
| Timeline | 1.8.13 |
| Test Framework | 1.6.0 |

이 밖에 TextMeshPro와 Unity Pipeline 패키지를 쓴다. Pipeline은 검증 스크립트를 실행하는 Unity CLI의 `run_script`·`script eval` 명령을 제공한다.

입력은 새 Input System으로 직접 읽는다(`Keyboard.current`, `Gamepad.current`, `Mouse.current`). `Assets/InputSystem_Actions.inputactions`는 템플릿 기본값이며 게임 코드에서 쓰지 않는다.

## Git

- **원격:** GitHub. 대용량 파일(FBX, PNG, 텍스처 등)은 Git LFS로 관리하므로 처음 받기 전에 `git lfs install`이 필요하다.
- **기본 브랜치:** `main`. 기능 작업은 브랜치에서 하고 fast-forward로 병합한다.
- **커밋 메시지:** 한국어. 예: `생성기 버전 22: 스폰 빈터를 진짜 평면으로`
