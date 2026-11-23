# 05. 도구와 검증

[← 인계 문서 목차](README.md)

`AgentScripts/`의 C# 파일은 게임 어셈블리에 들어가지 않는다. Unity CLI의 `run_script`로 열린 에디터 안에서 그때그때 컴파일해 실행한다.

## 1. Unity CLI로 스크립트 실행하기

에디터에서 프로젝트를 연 채로 실행한다.

```bash
# 파일 하나의 정적 진입점 실행 (Play 모드 검증은 Play 중에)
unity command --timeout 400 run_script --project-path "C:\Dev\B612" \
  --file AgentScripts/NaturalTerrainMeshValidation.cs \
  --entry NaturalTerrainMeshValidation.Compare \
  --args "[612101,2,\"system_02_B\",1]" --timeout_ms 400000 --result-only

# 짧은 C# 코드 실행 (예: Play 모드 상태 확인)
unity command script eval --project-path "C:\Dev\B612" --result-only \
  --code 'return "playing " + UnityEditor.EditorApplication.isPlaying;'
```

- **결과 형식:** JSON(`success`, `result`, `errorDetails`, `diagnostics`)이다. 컴파일 오류는 `diagnostics`에 나온다.
- **`internal` 멤버:** `run_script`의 어셈블리는 게임 어셈블리와 달라서 직접 못 쓴다. 리플렉션을 쓰거나 해당 멤버를 `public`으로 만든다.
- **`script eval`:** `run_script`로 컴파일한 클래스를 볼 수 없다. 그런 클래스의 상태를 바꾸려면 진입점을 따로 만든다(예: `DashTrace`).
- **오래 걸리는 실행:** `unity command --timeout 400`과 `--timeout_ms 400000`으로 시간 제한을 늘린다. 행성 하나씩 나눠 호출해 Pipeline 메인 스레드 예산을 넘지 않게 한다.
- **런타임 코드를 고친 뒤:** `UnityEditor.AssetDatabase.Refresh()`로 다시 컴파일시키고 `EditorApplication.isCompiling`이 false가 될 때까지 기다린 뒤 Play에 들어간다.

## 2. 빌더 (씬·에셋 생성)

편집 모드에서 실행하며, 씬에 저장한다. 빌더가 만든 씬 부분을 손으로 고친 뒤 빌더를 다시 돌리면 덮어써진다.

| 빌더 | 대상 |
|---|---|
| `B612Builder.Build` | `Generated/`(메시·재질·프리팹·볼륨) 전체와 `B612_Diorama`, `B612_Night`, `B612_Turnaround` 씬 |
| `EmptyPlanetSceneBuilder.Build` | `B612_Night`를 바탕으로 `B612_EmptyPlanet` 씬을 만든다 |
| `BaobabPrototypeBuilder.Build`, `BaobabGrowthBuilder`, `BaobabThreatBuilder` | 빈 행성 씬의 바오밥 스폰·성장·위험도 구성 |
| `LambPrototypeBuilder.Build` | 양 카드 구성 |
| `PlayerTraversalBuilder.Build` | 이동 기술 컴포넌트 연결 |
| `CameraStabilityBuilder.Apply` | 카메라 안정화 설정 |
| `DeveloperDebugBuilder.Build` | F12 디버그 UI와 폰트 |
| `StarSystemMapTravelBuilder.Build` | 지도 UI |
| `ChapterJourneyBuilder.Build` | `B612_ChapterJourney` 씬과 빌드 설정(빌드 인덱스 0 등록), 한국어 폰트 |
| `TraversalTestCourseBuilder.Build` | `B612_TraversalTest` 씬 |
| `ProceduralTerrainStage2Builder.Build` | 예전 절차 지형 비교 씬 |
| `TerrainAppearanceBuilder.Apply` | 지면 외관 설정 |

캐릭터 모델은 Blender 5.2에서 `AgentScripts/blender/build_characters.py`, `build_fox_rig.py`로 만든다.

## 3. 검증 스크립트

모두 Play 모드에서 실행한다(따로 적힌 것 제외). 검증은 별도 물리 씬이나 임시 오브젝트를 쓰고 씬 에셋을 저장하지 않는다.

### 지형 (가장 자주 쓴다)

| 진입점 | 내용 |
|---|---|
| `TerrainPrepFingerprint.Run(journey, chapter, planetId, size)` | 행성 하나를 준비하고 해시를 낸다. 대상은 지면 정점·색·인덱스, 물 메시, 스폰·지점, 바오밥 슬롯. **리팩터링 전후 출력이 같은지** 확인할 때 쓴다 |
| `TerrainPrepFingerprint.RunV21`, `RunV20`, `RunV18`, … `RunV9Noise` | 설정을 이전 버전으로 바꿔 같은 해시를 낸다. 이전 버전 재현 확인용 |
| `NaturalTerrainMeshValidation.Compare(...)` | 등각 메시와 v8 정규화 메시를 비교하고 지면 오차(메시 대 해석 높이), 연결 면적, 후보 수, 강 깊이를 본다. v21+는 빈터 평탄도도 본다(`pad` 필드: 평면에서 벗어난 mm, 띠 경사 대 원래 지면, 스폰 위치) |
| `NaturalTerrainMeshValidation.Walk(...)` | 실제 `GravityWalker`로 10개 경로를 걷는다: 스폰→최고점·최저점 왕복, 강 횡단 2곳 왕복, 호수 드나들기 |
| `NaturalTerrainMeshValidation.Relief(...)` | 높이 범위와 경사 분포(30° 초과 비율, 최대 경사), 연결 면적 |
| `NaturalTerrainMeshValidation.Dash(...)` / `DashTrace(...)` | 강둑 오르기, 강 쪽으로, 비탈 오르기, 벼랑 내려가기, 평지에서 대쉬해 이륙 지점 위로 뜬 높이(`lift`)와 지면 위 높이(`clear`)를 잰다. `DashTrace`는 0.4m 넘게 뜬 경우의 틱별 기록 |
| `NaturalTerrainMeshValidation.Shortcuts(...)` | 대쉬·이단점프·활공이 걷기보다 시간을 줄이는 지름길 수 |
| `NaturalTerrainMeshValidation.NoiseSpread(count)` | 노이즈 기저의 분포(보정용) |
| `TerrainPrepProfile.Run(...)` | 준비 시간을 단계별로 쪼개서 잰다 |
| `TerrainAppearanceCapture.Capture`, `TerrainV6Comparison` | 외관 비교 캡처 |
| `PlanetTileDensityValidation` | 크기별 바오밥 구역 수와 후보 밀도 |

**인자:** `(journey, chapter, planetId, size)`
- `journey`: 여정 seed. 기준으로 612101과 77을 쓴다.
- `chapter`: 1~3
- `planetId`: `system_0<챕터>_<Start|B|C|D|E|Destination>`
- `size`: 0이면 원래 크기, 0.7~2이면 그 배율로 강제

### 이동·카메라

| 스크립트 | 내용 |
|---|---|
| `PlayerTraversalValidation.Validate` | 격리 물리 씬에서 17개 기술 검증 묶음: 점프·코요테·추가 점프·대쉬·활공·착지 회복·벽 sweep·구면 대쉬·30/60/120fps 입력 |
| `PlayerTraversalValidation.ValidateSlopes` | 15/30/45° 경사와 실제 행성에서의 보행·기술 |
| `PlayerTraversalValidation.ValidateGeometryAndLegacy` | 같은 메시 벽에서 대쉬 정지, 캡슐 크기, 예전 모터 |
| `PlayerTraversalSceneValidation.Start`, `PlayerTraversalSlopeSceneValidation.Start` | 실제 씬과 가상 키보드로 기술 연계 |
| `TraversalTestCourseValidation.Validate` | `B612_TraversalTest` 씬에서 고정 코스 시도. **2026-11-23 기준 수정 전부터 `passed=false`다**(구간 0 "Dash launch −0.1" 등). 대쉬 상승 제한 전후 결과는 같다 |
| `CameraStabilityValidation`, `CameraScenarioValidation`, `CameraInputValidation`, `CameraWallOrbitValidation` | 카메라 구도·입력·벽 근처 궤도 |
| `JumpProbe`, `Probe`, `RuntimeProbe`, `NightProbe` | 개별 측정과 편집 모드 미리보기 |

### 게임플레이·진행

| 스크립트 | 내용 |
|---|---|
| `BaobabPrototypeValidation`, `BaobabGrowthValidation`, `BaobabThreatValidation` | 싹 스폰 분포, 성장 단계, 위험도와 게임오버 |
| `LambPrototypeValidation`, `LambFeedbackValidation` | 양 소환·먹기·회수, 먹는 중 표시 |
| `HorizonPrototypeValidation` | 화면 밖 말풍선 |
| `StarSystemMapTravelValidation`, `StarSystemMapTravelSceneValidation` | 지도와 행성 이동 |
| `ChapterJourneyValidation`(`Plans`, `Prepared`, `ReviewedStarts`), `ChapterJourneySceneValidation`, `ChapterJourneyEdgeValidation` | 3챕터 여정, 패배 우선순위 |
| `DeveloperDebugValidation`, `DeveloperBaobabSkipValidation` | F12 디버그와 바오밥 스킵 |
| `ProceduralTerrainStage2Validation`, `ProceduralTerrainStage2SceneValidation` | 예전 절차 지형 |

## 4. 지형을 바꿀 때의 검증 절차

생성기 v20~v22 작업에서 쓴 절차다.

1. **기준 해시 기록.** 바꾸기 전에 행성 8곳의 `TerrainPrepFingerprint.Run` 결과를 저장한다.
   - 기준 행성: `(612101,2,system_02_B,0.7)`, `(612101,2,system_02_B,1)`, `(612101,2,system_02_B,2)`, `(612101,1,system_01_Start,0)`, `(612101,1,system_01_D,0)`, `(612101,3,system_03_E,0)`, `(77,1,system_01_C,0)`, `(77,3,system_03_D,0)`
2. **구현.** 새 기능은 끌 수 있는 설정으로 넣는다. 생성기 버전을 올리고, 새 기본값을 설정 에셋 3개에 쓴다. `Assets/B612/Scripts`와 `AgentScripts/runtime`을 같게 둔다.
3. **이전 버전 재현.** `RunV<이전>` 진입점을 추가해 8곳 해시가 1단계 기록과 같은지 확인한다(시간 값은 비교에서 뺀다).
4. **행성 36곳 비교.** 여정 612101과 77 × 3챕터 × 6행성으로 `Compare`를 돌린다. 생성 성공, 연결 면적 60% 이상, 메시 오차, 새 기능의 지표를 본다.
5. **보행 8곳.** 1단계의 8곳에서 `Walk`를 돌려 80개 경로가 모두 통과하는지 본다.
6. **화면 비교.** 같은 행성 같은 지점에서 이전 버전과 새 버전을 캡처해 `Artifacts/NaturalPlanetTerrain/<기능>/`에 둔다.
7. **기록.** `Docs/NaturalPlanetTerrainImplementationResults.md`에 절을 추가하고, 결정이면 `Docs/GameDesign.md`, 상태가 바뀌면 `AGENTS.md`·`README.md`도 고친다. 검증 로그를 `Artifacts/.../<기능>Validation.txt`로 남긴다.

## 5. 에디터 메모리 관리

긴 검증(행성 수십 곳 준비와 캡처)을 한 Play 세션에서 계속하면 에디터가 시스템 메모리 부족으로 꺼진다. 오류는 d3d12 `CreateCommittedResource 8007000e`, 또는 Windows 커밋 한도다.

- **Play 모드 재시작 주기:** `Compare`·`Walk`·지문 검사는 행성 2곳마다, `Relief`는 3곳마다, 캡처 전에 한 번 Play 모드를 다시 시작한다.
- **재시작 방법:**
  1. `isPlaying = false`
  2. 정지 확인
  3. `EditorUtility.UnloadUnusedAssetsImmediate(true)`와 `GC.Collect()`
  4. `isPlaying = true`
  5. `StarSystemFlowController`가 생길 때까지 기다린다.

  ```csharp
  // script eval로 단계마다 실행
  UnityEditor.EditorApplication.isPlaying = false;
  UnityEditor.EditorUtility.UnloadUnusedAssetsImmediate(true); System.GC.Collect(); System.GC.WaitForPendingFinalizers();
  UnityEditor.EditorApplication.isPlaying = true;
  ```
- **메모리 기록:** 재시작할 때마다 관리 메모리(`Profiler.GetTotalAllocatedMemoryLong`)와 에디터 프로세스 전용 메모리를 기록한다.
  - 2026-11-23 기준 관리 메모리는 재시작 뒤 약 450MB이고, 에디터 전용 메모리는 4.4~5.8GB였다.
  - 재시작해도 계속 오르면(예: 8GB 이상) 멈추고 에디터를 다시 시작할지 사용자에게 묻는다.
- **겹친 실행 주의:** 백그라운드 검증을 멈춘 뒤에는 남은 bash/tail 프로세스가 없는지 확인한다. 두 검증이 같은 에디터에 동시에 들어가 꺼진 적이 있다.
- **임시 파일 위치:** 프로젝트 `Temp/`는 Unity가 재시작할 때 지운다. 도우미 스크립트와 중간 결과는 그 밖에 둔다.

## 기존 설계·구현 문서 목록

`Docs/`의 문서다. 날짜순 결정 기록이 섞여 있어서, 같은 주제는 나중 문서와 결과 문서가 우선한다.

| 문서 | 주제 |
|---|---|
| `GameDesign.md` | **기본 게임 기획(기준 문서).** 행성 사이클, NPC·양털·작물 구상, 지형 결정 기록 |
| `PlanetDataModel.md` | 행성·항성계 데이터 구조 설계 |
| `PlanetGenerationDesign.md` | 행성 구역 분할과 무작위 생성 초기 제안 |
| `PlanetTileDensityImplementationSpec.md` | 행성 크기별 바오밥 구역 밀도 |
| `StarSystemMapTravelImplementationSpec.md` | 공통 행성 생성과 항성계 지도·이동 |
| `ProceduralTerrainTraversalDesign.md` | 챕터별 이동 성장과 절차 지형 설계(최신 머리말에 방향 전환 기록) |
| `TraversalTestCourseStage1Results.md` | 이동 지형 1단계 고정 실험 행성 |
| `ProceduralTerrainStage2Results.md`, `ProceduralTerrainStages34Results.md` | 예전 절차 지형 2~4단계 결과 |
| `TerrainAppearanceImplementationResults.md` | 예전 지형 외관 개선 결과 |
| `NaturalPlanetTerrainImplementationSpec.md` | 자연 지형·강 우선 생성 명세(생성기 8) |
| `NaturalPlanetTerrainImplementationResults.md` | **자연 지형 생성기 8~22의 구현·검증 기록** |
| `NaturalTerrainImprovementProposals.md` | 자연 지형 개선 제안 A~E(A~C 구현, D 비동기 준비, E 조각 메시는 제안) |
| `GrassDirtTextureAssetImplementationSpec.md`, `...Results.md` | 잔디·흙 텍스처 적용 |
| `PlayerTraversalImplementationSpec.md` | 더블점프·대쉬·활공 명세와 검증(대쉬 상승 제한 포함) |
| `PlayerTraversalSlopeDiagnosis.md` | 경사 이동 실패와 걷기 모션 끊김 원인 분석 |
| `CameraStabilityDesign.md` | 구형 행성 카메라 안정화 |
| `BaobabThreatDesign.md` | 바오밥 위험도와 게임오버 |
| `LambPrototypeDesign.md` | 양 카드와 싹 먹기 |
| `DeveloperDebugModeSpec.md`, `DeveloperBaobabSkipImplementationSpec.md` | F12 디버그와 바오밥 스킵 |

## 알려진 미해결 사항

- **플레이어 빌드:** 생성기 v9 이후 다시 확인하지 않았다.
- **준비 시간:** 행성 준비는 동기 실행이다. 반경 24m 행성은 약 1.2초 멈춘다(제안 D: 비동기 준비).
- **고정 코스 검사:** `TraversalTestCourseValidation`이 수정 전부터 `passed=false`다.
- **스폰 빈터:** v22 평면 빈터의 띠에서 35° 넘는 곳이 원래 지면보다 조금 늘었다(행성 36곳 중 14곳). 실제 화면의 인상은 확인하지 않았다.
- **NPC·작물·퀘스트·양털·중간 저장:** 데이터 자리만 있고 구현은 없다.
- **스크립트 미러:** 7개 파일(바오밥 종류·양 카드 UI·말풍선 관련)이 `AgentScripts/runtime`에 없다.
