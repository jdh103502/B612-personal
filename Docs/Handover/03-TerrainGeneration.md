# 03. 행성 지형 생성

[← 인계 문서 목차](README.md)

현재 지형은 **자연 지형 프로필 `terrain.natural.v1`, 생성기 버전 22**다. 코드는 대부분 `ProceduralPlanetTerrain.cs`와 `NaturalTerrain*.cs`, `TerrainWater.cs`에 있다. 버전별 결정 이유와 검증 수치는 [NaturalPlanetTerrainImplementationResults.md](../NaturalPlanetTerrainImplementationResults.md)에 있다.

## 1. 두 가지 프로필

| 프로필 | 상수 | 경로 | 상태 |
|---|---|---|---|
| 자연 지형 | `PlanetGenerationSettings.NaturalProfileId` = `terrain.natural.v1` | `ProceduralPlanetTerrain.PrepareNatural` | **현재 사용.** 세 생성 설정 에셋 모두 이 프로필 |
| 예전 절차 지형 | `StylizedProfileId` = `terrain.stylized.v2` 등 | `ProceduralPlanetTerrain.Prepare` + `TerrainRegions`, `TerrainLandforms`, 검수 코스(`Feature`) | 비교용으로 코드만 남아 있다 |

예전 경로는 챕터별 능력 코스를 미리 깔고 실제 모터로 보행을 검사해 최대 5번 고쳤다(`MaxBuildAttempts`). 사용자가 2026-11-21에 "미리 계획한 보행로·능력 코스" 개념을 없애기로 해서 자연 지형으로 바뀌었다.

## 2. 생성 파이프라인 (`PrepareNatural`)

입력은 행성 seed, 반경, 챕터, 생성 설정이다. 같은 입력이면 비트 단위로 같은 결과가 나온다.

| 단계 | 하는 일 | 코드 |
|---|---|---|
| 1. 설정 고정 | 설정을 복사하고 반경에 맞게 크기를 바꾼다(`Snapshot(radius)`). 노이즈 크기는 반경 비례, v18 기복 높이는 반경에 맞춰 축소, 빈터 크기는 고정 m | `NaturalTerrainSettings.Snapshot` |
| 2. seed와 노이즈 오프셋 | 거시·언덕·강·배치·표면·거칠기·봉우리·협곡 seed를 행성 seed에서 파생한다(`DeriveSeed(seed, 81~88, 0)`) | `PrepareNatural` |
| 3. 거친 고지대 임계값 | v15 거친 고지대가 켜져 있을 때만 | `RuggedThreshold` (현재 꺼짐) |
| 4. 산 배치 | v19 산이 켜져 있을 때만 | `PlaceMountains` (현재 꺼짐) |
| 5. 골짜기 강 경로 | 가장 넓은 저지대에 호수를 두고, 우선순위 범람으로 가장 낮은 고개를 따라 둘레의 0.52~0.7배 경로를 고른다. 사행을 넣고 하류로만 내려가는 골짜기와 그릇 모양 호수를 높이장에 새긴다 | `NaturalTerrainValley.Build` |
| 6. 스폰 빈터 | 스폰 방향 근처에서 강·호수·벼랑·오목한 곳을 피해 가장 덜 깎는 곳에 평면 빈터를 만든다 | `NaturalTerrainSpawnPad.Build` |
| 7. 도랑 | 지금 높이장의 흐름 그래프에서 물이 모이는 선을 따라 얕은 마른 도랑을 판다 | `NaturalTerrainFlow.Build` → `NaturalTerrainErosion.Build` |
| 8. 흐름장과 습도 | 도랑이 반영된 높이장으로 흐름 그래프를 다시 만든다. 그 결과로 렌더링 전용 습도를 계산한다 | `NaturalTerrainFlow.Build`, `NaturalTerrainFlow.Moisture` |
| 9. 강 계획 | 골짜기 경로를 먼저 맞춘다. 안 되면 범람 라우팅으로 연못에 이르는 가장 큰 유입을 강으로 고른다. 그래도 안 되면 v10 하강 추적으로 대신한다 | `NaturalTerrainFlow.Plan`, `NaturalTerrainRiver.Plan` |
| 10. 빈터 침수 검사 | 강이나 연못이 빈터에 닿으면 상태 문자열에 기록한다(옮기지는 않는다) | `PrepareNatural` |
| 11. 지면 메시 | 등각 큐브 구면 메시를 만든다. 정점 간격은 약 0.18m(강폭 × 0.3이 더 작으면 그 값)이고 정점 수는 6·f²+2다. 벼랑·협곡 가장자리로 정점을 옮기고, 뒤집힌 삼각형을 되돌린다. 정점 색은 지면 종류 가중치, UV1은 습도다 | `BuildMesh`, `SnapToEscarpment`, `SnapToCliff`, `UnsnapFolds` |
| 12. 충돌체 | 같은 메시를 MeshCollider에 넣는다. 삼각형이 많으면 Fast Midphase를 끈다 | `AssignMesh` |
| 13. 물 메시 | 강 리본(가로 5열)과 호수 원판. UV에 깊이·유속을 넣는다 | `TerrainWater.CreateMesh` → `CreateNaturalMesh` |

그다음 `PlanetWorldInstance`가 `NaturalTerrainPlacement`를 만든다.
- **보행 연결 분석:** 완성된 충돌체 위 약 0.6m 간격 셀마다 실제 캡슐로 설 수 있는지와 경사 35° 이하인지를 본다. 이웃 셀 사이를 걸을 수 있으면 연결하고, 가장 큰 연결 영역을 고른다.
- **실패 조건:** 그 영역이 행성 표면의 60%(`minConnectedAreaRatio`)보다 작으면 실패다.
- **배치:** 그 영역 안의 마른 땅에 스폰(빈터 중심)과 상호작용 지점 3곳을 놓는다.
- **바오밥 후보:** 같은 연결 기준으로 거른다. 스폰 1.1m 안, 상호작용 지점 0.8m 안, 빈터 평면 안은 제외한다.

## 3. 높이 함수의 계층

어느 방향(행성 중심에서 본 단위 벡터)의 지면 높이(기준 반경에서의 m)는 다음처럼 겹친다. 모든 함수는 스레드 안전한 순수 함수다.

```
ProceduralPlanetTerrain.Height(plan, d)
 = TerrainWater.Height(plan, d, BaseHeight(plan, d))   // 강 물길·강둑·호수 그릇을 판다
BaseHeight(plan, d)                                      // 자연 프로필이면 FreeHeight
FreeHeight(plan, d)                                      // v21+: 스폰 빈터를 마지막에 덮는다
 = spawnPad == null ? UnpaddedFreeHeight(d) : spawnPad.Apply(d, UnpaddedFreeHeight(d))
UnpaddedFreeHeight(plan, d)                              // 자연 지형 본체
 = valley.Apply( Relief(...) − erosion.Carve(d) )        // v19+: 골짜기·호수 그릇
Relief(plan, metres, broad, medium)                      // v18: 연속 기복
 = 두 노이즈 층(broad·medium × 거칠기 마스크) + 큰 기복(도메인 워프, 둥근 능선) + 벼랑(Escarpment) [+ 산(Mountains)]
```

- **노이즈:** `Basis`는 v10부터 3D simplex 노이즈(`Simplex`)다. 값 범위는 v9 값 노이즈와 같도록 보정했다(`SimplexGain` 2.26, `SimplexFrequency` 0.40).
- **거칠기 마스크:** `Roughness`는 중간 층을 0.2~1.6배로 바꿔 잔잔한 초원과 울퉁불퉁한 구역을 나눈다.
- **흐름 그래프:** 강·도랑·골짜기 계획은 `FreeHeight`를 읽는다. 물길을 판 뒤의 높이는 `Height`다.
- **분기 순서:** `UnpaddedFreeHeight`는 설정에 따라 v9 비교 경로, v15~v17 거친 고지대·협곡 경로, v18 이후 기복 경로로 갈린다. 예전 경로의 식은 재현성을 위해 글자 그대로 남아 있다.

## 4. 생성기 버전 역사

`PlanetGenerationSettings.GeneratorVersion`. 행성 데이터의 `spec.generatorVersion`이 다르면 준비 단계에서 거부한다.

| 버전 | 내용 | 끄는 설정(이전 재현) |
|---|---|---|
| 8 | 자연 지형 프로필 도입: 전역 높이장, 지형 기반 강, 생성 후 배치 | |
| 9 | 등각 큐브 구면 메시(정점 절약, 큐브 모서리 과밀 제거) | `equalAngleMesh=false` |
| 10 | simplex 노이즈와 거칠기 마스크 | `noiseBasis=Value`, `roughnessRange=(1,1)` |
| 11 | 흐름 기반 강과 얕은 연못(걸어 들어갈 수 있다) | `riverMode=Trace` |
| 12 | 물길을 따른 얕은 마른 도랑 | `erosionDepthMeters=0` |
| 13 | 하류로 넓어지는 강(0.9m → 하구 3m) | `riverMouthWidthMeters=0` |
| 14 | 길고 깊은 강(15~40m, 발·정강이 깊이), 범람 라우팅 | `floodedRouting=false` 등 |
| 15 | 능력용 거친 고지대 1단계(계단 절벽·산괴) | `ruggedStrength=0` |
| 16 | 절벽 모양 다듬기(절개형 경사로, 가장자리 정렬) | |
| 17 | 협곡과 강이 흐르는 협곡(2단계) | `canyonDepthMeters=0` |
| 18 | **방향 전환:** 작은 지형을 끄고 행성 전체에 이어지는 연속 기복과 큰 벼랑 몇 개 | `reliefHeightMeters=0` |
| 19 | 둘레 절반 넘는 골짜기 강, 1.5배 강폭, 높은 산. **사용자가 부자연스럽다고 판단해 롤백** | |
| 20 | v19에서 산만 빼고 골짜기 강과 강폭(1.35m → 하구 4.5m)을 되살림 | `riverCircumferenceShare=(0,0)` |
| 21 | 스폰 주변 NPC용 평탄한 빈터(곡면을 따름) | `spawnPadRadiusMeters=0` |
| 22 | 빈터를 진짜 평면으로(기존 소행성 첫 시작 지점처럼) | `spawnPadPlanar=false` |

`AgentScripts/TerrainPrepFingerprint.cs`의 `RunV9Noise`, `RunV10River`, `RunV13River`, `RunV14`~`RunV18`, `RunV20`, `RunV21`이 각 이전 버전을 재현해 해시를 낸다.

## 5. 자연 지형 설정 (`NaturalTerrainSettings`)

생성 설정 에셋의 `naturalTerrain` 블록이다. 인스펙터 Tooltip에 버전과 의미가 적혀 있다. 지금 쓰이는 값 위주로 정리한다.

### 기본 높이 층

| 필드 | 기본값 | 의미 |
|---|---|---|
| `broadScaleMeters`, `broadAmplitudeMeters` | 12m, 1.4m | 큰 노이즈 층의 크기와 높이 |
| `mediumScaleMeters`, `mediumAmplitudeMeters` | 5m, 0.3m | 중간 노이즈 층 |
| `noiseBasis` | Simplex | Value는 v9 비교용 |
| `roughnessScaleMeters`, `roughnessRange` | 18m, (0.2, 1.6) | 거칠기 마스크 크기와 중간 층 배율 범위 |
| `reliefHeightMeters` | 5m | v18 연속 기복의 높이 범위. 0이면 v15~v17 경로 |
| `reliefScaleMeters` | 10 | 기복 크기. 반경 12m 기준이고 반경에 비례한다. 내부에서 0.4배 주파수 계수가 곱해져 실제 크기는 약 25m |
| `reliefWarp` | 0.35 | 능선과 골짜기를 구부리는 도메인 워프 |
| `escarpmentHeightMeters`, `escarpmentScaleMeters`, `escarpmentWidthMeters` | 2m, 30, 2m | 긴 벼랑의 높이, 간격(반경 비례), 절벽면 폭 |

### 강·호수

| 필드 | 기본값 | 의미 |
|---|---|---|
| `generateRiver`, `riverMode` | true, Flow | 강 생성 여부와 방식. Trace는 v10 대체 경로 |
| `riverCircumferenceShare` | (0.52, 0.7) | v19+ 골짜기 강의 길이(둘레 비율 범위). x=0이면 v18식 강 |
| `riverLakeRadiusMeters` | 7m | 골짜기 강이 끝나는 호수 반경. 작은 행성은 축소 |
| `riverWidthMeters`, `riverMouthWidthMeters` | 1.35m, 4.5m | 수원 폭과 하구 폭. 하구는 연못 반경의 80% 이하 |
| `riverLengthMeters` | (15, 40) | v14 흐름 강 길이. 골짜기 강이 실패할 때 쓴다 |
| `riverDepthMeters`, `riverDepthPerWidth` | (0.28, 0.38), 0.2 | 물 깊이. 폭의 20%를 넘지 않는다 |
| `riverMaxCutMeters`, `floodedRouting` | 0.45m, true | 고개를 깎을 수 있는 최대 깊이, 얕은 분지를 채운 것처럼 흐르게 할지 |
| `pondMaxDepthMeters`, `pondMaxRadiusMeters` | 0.25m, 8m | 연못(호수) 최대 깊이와 반경 |
| `flowSpacingMeters` | 0.6m | 흐름 그래프 셀 간격 |
| `bankTransitionMeters` | 1.2m | 강둑 폭 |

### 도랑·습도

| 필드 | 기본값 | 의미 |
|---|---|---|
| `erosionDepthMeters`, `erosionMinCells`, `erosionWidthMeters` | 0.2m, 40, 1m | 도랑 최대 깊이, 도랑이 시작하는 상류 셀 수, 반폭 |
| `moistureStrength`, `moistureBlurMeters` | 1, 1.5m | 렌더링 전용. 물길·저지대를 짙게 칠하는 정도와 흐림 반경 |

### 스폰 빈터 (v21, v22)

| 필드 | 기본값 | 의미 |
|---|---|---|
| `spawnPadRadiusMeters` | 3m | 빈터 평면 반경(모든 행성에서 같은 m, 행성 반경의 30% 이하). 0이면 v20 |
| `spawnPadBlendMeters`, `spawnPadMaxBlendMeters` | 2.5m, 7m | 주변 지형과 섞는 띠의 최소·최대 폭 |
| `spawnPadMaxBlendSlope` | 22° | 띠가 원래 지면보다 가파르지 않게 할 때의 기준 경사 |
| `spawnPadSearchMeters` | 14m | 기존 스폰 방향에서 찾는 거리. 없으면 행성 전체로 넓힌다 |
| `spawnPadPlanar` | true | 평면(v22) 또는 곡면을 따르는 한 높이(v21) |

### 배치·분석

| 필드 | 기본값 | 의미 |
|---|---|---|
| `analysisSpacingMeters`, `maxAnalysisNodes` | 0.6m, 30000 | 보행 연결 분석 셀 간격과 상한 |
| `minConnectedAreaRatio` | 0.6 | 걸어서 이어진 최대 영역의 최소 비율. 못 넘으면 생성 실패 |
| `siteSpacingMeters`, `siteCandidateLimit` | 2m, 64 | 상호작용 지점 사이 최소 간격, 후보 시도 수 |

### 꺼져 있는 예전 기능

설정값만 바꾸면 다시 켤 수 있지만 사용자 결정으로 쓰지 않는다.

- **산(v19):** `mountainHeightMeters`, `mountainRadiusMeters`, `mountainCount`. 기본값 0
- **거친 고지대(v15):** `ruggedStrength`, `ruggedCoverage`, `peakHeightMeters`, `terraceStepMeters`, `rampCorridorMeters` 등
- **절벽 다듬기(v16):** `rampNotchLength`, `cliffJitter`, `cliffSnap`
- **협곡(v17):** `canyonDepthMeters`, `canyonWidthMeters` 등

## 6. 강과 물 (`TerrainWater`)

- **`TerrainWater.River`:** 강 하나의 데이터다.
  - 강 경로: 중심선 방향(`directions`), 수면 높이(`levels`), 바닥 높이(`beds`), 지점별 폭(`widths`), 강둑 폭(`bankWidth`)
  - 연못: 방향(`pondDirection`), 수면(`pondLevel`), 반경(`pondRadius`), 그릇 깊이(`pondBowl`)
  - 협곡 구간(`gorge*`)과 위치 색인(`index`)
- **`Height(plan, n, original)`**
  - 연못 그릇을 파고(`PondCarve`) 가장 가까운 물길까지의 거리를 찾는다(`Sample`).
  - 단면은 v14 이차 스플라인(`SmoothBankHeight`)이다. 물가에서 꺾임이 생기지 않아 걷는 캐릭터가 튀어 오르지 않는다.
- **`IsWet(plan, direction, margin, out surface)`:** 그 방향이 물 아래인지 알려 준다. 배치와 바오밥 후보, 빈터 검사에 쓴다.
- **`MovementFactor`:** 물속 보행 속도 배율(65%)이다.
- **`CreateNaturalMesh`:** 물 메시를 만든다. 강은 가로 5열 리본, 연못은 수면 높이의 원판이다. 지면이 원판의 나머지를 가린다.

## 7. 스폰 빈터 (`NaturalTerrainSpawnPad`)

- **위치 고르기 (`Build`)**
  - 기존 스폰 방향에서 가까운 곳부터 2m 간격 피보나치 구면 격자 후보를 본다.
  - 버리는 후보: 골짜기 경로나 호수에 가까운 곳(빈터 반경 + 띠 절반 + 강 최대 반폭 + 1m), v18 벼랑 위, 바깥 고리가 모두 높은 오목한 곳
  - 점수(낮을수록 좋음): 평면에서 벗어난 최대 높이 + 0.6 × 바깥 평균 높이 차 + 0.03 × 거리
  - 탐색 거리 안에 맞는 곳이 없으면 행성 전체에서 찾는다.
- **평면 높이:** 가운데 표본마다 "그 점을 지나는 평면의 높이"를 구해 평균한다(`LevelThrough`). 그래서 깎는 양과 채우는 양이 비슷하다.
- **모양 (`Weight`, `Target`)**
  - 가운데 반경은 노이즈로 ±15% 들쭉날쭉하다(`Edge`).
  - 그 안은 중심 방향에 수직인 평면이다.
  - 테두리 밖에서는 평면의 기울기를 띠 폭의 절반에 걸쳐 0으로 줄인다. 보행 모터는 약 4.3° 넘는 볼록한 꺾임에서 지면을 놓치므로 꺾임을 만들지 않는다.
  - 띠의 가중치는 smootherstep이다.
- **띠 폭:** 0.5m씩 넓혀 보면서 섞은 지면이 어디서도 "22°와 원래 경사 + 3° 중 큰 값"보다 가파르지 않은 가장 좁은 폭을 쓴다. 최대 7m다.
- **바깥 쓰임새**
  - `InCore(d)`: 바오밥 후보에서 빈터를 뺀다.
  - `centre`: 스폰을 빈터 중심에 놓는다.

## 8. 주의할 점

- **보행 모터와 지형의 관계:** 실측값이다.
  - 점프를 유지하면 약 2.0m, 이단점프로 약 3.4m를 오른다.
  - 70° 안팎의 벽은 공중에서 미끄러져 넘는다.
  - 2m/s로 걷는 캐릭터는 약 4.3° 넘는 볼록한 메시 꺾임에서 지면을 놓친다.
  - 그래서 강둑·빈터 가장자리 같은 단면은 C1 연속(기울기 연속)으로 만든다.
- **준비 시간:** 반경 12m 행성 약 0.4초, 24m 약 1.2초다. 아직 동기 실행이다. 비동기 준비(제안 D)와 조각 메시(제안 E)는 [NaturalTerrainImprovementProposals.md](../NaturalTerrainImprovementProposals.md)에 제안으로만 있다.
- **설정 변경과 에셋:** 검증 스크립트가 설정을 메모리에서만 바꾸면 에셋을 다시 불러올 때 되돌아간다. 새 기본값은 `.asset` 파일에 직접 쓴 뒤 검증한다.
