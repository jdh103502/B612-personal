# 행성과 항성계의 기본 데이터 구조

2026-11-08. 항성계 지도에서 다음 행성을 선택하고 해당 데이터로 실제 행성을 생성하기 위한 기본 설계다. 사용자가 제외한 이미지의 밤·노을·한낮 분류는 데이터에 포함하지 않는다. 후속 구현으로 `StarSystemData.cs`에 기본 DTO·고정 그래프와 검증을 연결하고 크기/색 적용·M 지도·월드 전환을 구현했다. NPC·작물·보상·중간 저장의 필드는 확장 계약이며 해당 플레이 시스템은 구현하지 않았다. 현재 적용 범위와 검증은 `StarSystemMapTravelImplementationSpec.md`의 구현 결과를 따른다.

타일 밀도 구현은 생성기 버전 2, 지형 2단계는 3, 지역·챕터 구현은 4였다. 17절 외관 개선의 `generatorVersion`은 **5**, 데이터 형식 `modelVersion`은 **1**, 지형 프로필은 `terrain.stylized.v1`이다. `terrain.sizeScale`과 템플릿 기준 `subdivisions`의 격자 밀도 공식을 사용한다. 이전 버전 입력은 거부한다. 현재 외관 설정과 검증은 `TerrainAppearanceImplementationResults.md`를 따른다.

2026-11-11 챕터 시제품에서 `PlanetSpec.chapter`, `requiredCapabilities`, `unlock`을 추가했다. `RunProgressData`는 `chapter`, `journeySeed`, 누적 `unlocked`, `journeyComplete`를 소유하고, 지난 항성계 객체는 `StarSystemFlowController.CompletedSystems`가 유지한다. `PlanetState.visitedSites`와 길이 3의 `siteVisitSeconds/siteTravelMetres`는 해금·작물 조사·안내 방문과 도착 이후 비용을 기록한다. 디버그 Allow 값은 해금 기록을 바꾸지 않는다. 새 Play/패배 재시도에서 초기화하며 파일 저장·복원 계약은 아니다.

2026-11-09 디버그 스킵 구현에서 `state.surge.skippedByDebug`를 추가했다. 기본 false이며, 스킵한 행성의 Resolved·출발 기록에 true를 유지한다. 신규 행성은 false다. 자연 클리어 보상이나 저장 기능을 추가하는 필드는 아니며 상세는 `DeveloperBaobabSkipImplementationSpec.md`를 따른다.

## 1. 먼저 정할 원칙

행성마다 고유한 `PlanetData`를 만든다. 이동할 때 하나의 공용 행성 데이터를 다음 행성의 값으로 계속 덮어쓰지 않는다.

`PlanetData` 안에서도 생성 설정, 지도 배치, 진행 상태를 나눈다.

```text
PlanetData
  modelVersion : 데이터 형식 버전
  spec   : 이번 여정에서 확정한 이 행성의 설정
  map    : 지도에 표시할 위치와 진행 깊이
  state  : 방문·폭주·수확·퀘스트 진행 상태

StarSystemData
  planets : 이 항성계의 PlanetData 목록
  links   : 어느 행성에서 어디로 갈 수 있는지

RunProgressData
  currentSystemId / currentPlanetId
  선택해 지나온 경로 / 이동 대기 목적지
  보유 양·양털 등 여정 전체 상태와 연결
```

예를 들어 A에서 B로 이동하면 A를 B의 설정으로 바꾸지 않는다. A의 상태를 출발 완료로 기록하고, 현재 위치를 B로 옮긴 뒤 B의 설정으로 월드를 생성한다.

## 2. PlanetData의 세 부분

### PlanetSpec: 생성 전에 결정하고 유지할 설정

| 필드 | 형식 | 의미 |
| --- | --- | --- |
| `planetId` | string | 항성계 안팎에서 행성을 식별하는 고유 ID |
| `seed` | int | 지형·배치 등 무작위 생성의 기준값 |
| `generatorVersion` | int | 같은 seed를 해석할 생성기 버전 |
| `role` | enum | `Start`, `Normal`, `Special`, `Destination` |
| `sizeClass` | enum | 지도에 보여줄 `Small`, `Medium`, `Large` 분류 |
| `terrain` | TerrainPlan | 원본 대비 크기 배율·명목 반경, 지형 프리셋, 중력 세기, 지면 대표색 |
| `baobab` | BaobabPlan | 생성 규모와 위험도 한계, 등장 종류 |
| `crops` | CropPlan[] | 작물 종류별 생성할 수확 단위 수 |
| `npc` | NpcPlan 또는 null | NPC, 퀘스트·대화, 요구 양털과 보상 선택지 |

`Destination`은 기존 기획의 도착 행성이다. 이미지의 '보스' 표시는 이 역할로 대응하며, 별도의 전투 보스 시스템을 이번 데이터에 추가하지 않는다.

`sizeClass`는 분류다. 2026-11-08 후속 요청의 공통 빈 행성 템플릿 방식에서는 `terrain.sizeScale`로 원본 대비 선형 크기를 정하고, 템플릿의 고정 명목 반경으로부터 `terrain.radius`를 계산해 함께 확정한다. 배율·반경·분류를 독립적으로 무작위 추출하지 않는다. 세부 구현 계약은 `StarSystemMapTravelImplementationSpec.md`를 따른다.

```text
TerrainPlan
  profileId        : 지형 프리셋 ID
  sizeScale        : 해당 템플릿 원본 대비 선형 크기 배율
  radius           : 템플릿 명목 반경 × sizeScale
  gravityStrength  : 중력 세기
  groundColor      : 지면 대표색, sRGB 6자리 HEX 문자열
```

반경은 미터 단위의 생성 설정이다. 요철이 있는 행성의 모든 표면점까지의 거리를 뜻하지 않으며, 지도 아이콘의 크기나 월드 배치 좌표와 구분한다. 현재 `GravitySource.surfaceRadius`는 중력원 선택용 대표 반경이므로, 지형의 명목 반경을 그대로 대입한다고 가정하지 않는다.

#### 공통 빈 행성의 크기 (2026-11-08 후속 요청)

- 새 행성의 `sizeScale` 범위는 기존 `B612_EmptyPlanet` 원본 대비 **0.7~2.0**이다. 동일 메시를 균일 확대/축소하며, 플레이어·양·바오밥의 모델과 이동 수치는 확대하지 않는다.
- 첫 지도 시제품의 분류 제안은 `Small: 0.7 ≤ s < 1.0`, `Medium: 1.0 ≤ s < 1.5`, `Large: 1.5 ≤ s ≤ 2.0`이다. 기존 크기 1.0은 중형이다. 이 경계는 최종 밸런스 확정이 아니다.
- 현 빌더의 명목 반경 5.3을 원본으로 쓰면 `radius = 5.3 × sizeScale`이다. 원본 Transform의 scale에 매번 배율을 적용하고 직전 행성의 scale에 곱하지 않는다.
- 템플릿 ID/버전은 원본 메시·반경·scale을 식별해야 한다. 같은 프로필의 원본을 임의로 바꿔 이미 확정된 행성의 크기를 바꾸지 않는다.
- 항성계 생성 시 배율·분류·색을 미리 확정한다. 지도 열기·hover·방문·월드 재생성에서 다시 추출하지 않는다.
- 기존 설계 예시에서 `sizeScale`이 없으면 해당 템플릿과 `radius`로 한 번 환산하고 분류를 검증한다. 원본을 조회할 수 없으면 조용히 1배로 생성하지 말고 검증 오류로 처리한다. 신규 데이터는 배율을 명시한다.

#### 지면 색상 (2026-11-08 추가)

사용자의 후속 요청에 따라 `spec.terrain.groundColor`로 행성마다 땅의 색을 정할 수 있게 설계한다.

```json
{
  "terrain": {
    "profileId": "terrain.rocky",
    "sizeScale": 1.0,
    "radius": 5.3,
    "gravityStrength": 3.4,
    "groundColor": "#B8A6D8"
  }
}
```

- 예: 모래색 `#EADFC6`, 연두색 `#A6B88C`, 연보라색 `#B8A6D8`. 예시 색은 시간대·난이도·바오밥 속도와 자동 연결하지 않는다.
- 값은 조명을 받기 전의 지면 대표색이다. 실제 화면의 색에는 조명·그림자·후처리도 영향을 준다. 알파는 첫 구현에서 1로 고정하고 땅을 투명하게 만드는 용도로 쓰지 않는다.
- 현재 `B612Builder.BuildAsteroid`는 8가지 색상의 지면 재질을 사용한다. 새 대표색으로부터 기존 팔레트의 밝기 차이를 유지하는 여러 톤을 만들어 적용한다. 모든 재질을 동일한 색으로 덮어 지층·면별 명암을 없애지 않는다.
- 종이 질감 텍스처와 노멀·표면 설정은 유지한다. 색 변경은 지형 형태·Collider·중력·작물·NPC의 색을 바꾸지 않는다.
- 첫 구현은 행성 소유의 재질 인스턴스를 한 번 준비하고 대표색에서 파생한 팔레트를 적용하는 방식이 적합하다. 공유 원본 재질을 직접 수정하지 않고, 생성한 재질 인스턴스는 월드 정리 때 해제한다.
- 적용할 때마다 현재 색에 새 색을 계속 곱하지 않는다. 원본 팔레트와 `groundColor`에서 다시 계산해 같은 데이터는 같은 팔레트를 만든다.
- 색은 행성 생성 설정에 확정해 보관한다. 지도를 다시 열거나 월드를 다시 만들어도 유지한다. 지도 아이콘의 대표색도 이 값을 참조할 수 있다.
- 이전 데이터에 값이 없으면 기존 지형 프리셋의 원래 색을 사용한다. 새 데이터 생성 시 실제 선택한 색을 명시해 저장한다.
- 이후 여러 지면 유형이 필요하면 색 팔레트 프리셋을 추가할 수 있지만, 첫 구조는 대표색 한 필드로 시작한다.

후속 시제품은 `PlanetWorldInstance.Prepare`에서 이 대표색과 원본 명암을 사용해 지면 재질을 적용한다. 지도 아이콘도 같은 확정 색을 읽는다.

### PlanetMapPlacement: 지도 표시

```text
PlanetMapPlacement
  depth       : 단방향 진행 단계
  position01  : 지도 안의 정규화된 2차원 좌표(x, y)
```

- 원형·궤도형 지도든 층별 지도든 `position01`을 바꾸어 표현한다.
- 지도에서 물리적으로 가까워 보이는 것과 이동 가능한 연결은 별개다.
- `depth`는 방향 검증용이다. 실제 방문 횟수나 양의 휴식 횟수로 사용하지 않는다.
- '지금 여기', '다음 선택 가능', '지나온 길'은 현재 위치·연결·방문 경로에서 계산한다. 행성마다 `isCurrent`, `isAvailable`, `isVisited`를 중복 저장해 값이 어긋나게 만들지 않는다.

### PlanetState: 플레이하면서 바뀌는 상태

| 필드 | 형식 | 의미 |
| --- | --- | --- |
| `phase` | enum | 현재 행성 플레이 단계 |
| `worldInitialized` | bool | 최초 논리 배치가 만들어졌는지 |
| `simulationSeconds` | float | 이 행성에서 실제 진행된 게임 시간 |
| `surge` | SurgeProgress | 폭주 진행·결과와 생성 진행 기록 |
| `harvestedCrops` | CropAmount[] | 종류별 수확 완료량 |
| `quest` | QuestProgress | 퀘스트 상태와 선택한 보상 |

첫 단계 enum 제안:

```text
Unvisited → Surge → Clearing → Harvest → Quest → ReadyToLeave → Departed
                         진행 중 실패 → GameOver
```

이는 목표 게임의 일반 행성 흐름을 위한 기본안이다. 특수·도착 행성의 구체적인 진행은 해당 콘텐츠가 결정하며 모든 행성이 모든 단계를 수행하도록 강제하지 않는다.

후속 지도·이동 시제품에서는 생성 시간 종료(`BaobabSurge.IsComplete`)로 지도를 열고 남은 싹이 있어도 출발하는 정책을 제안한다. 이때 `Clearing`을 완료로 꾸미거나 지도 해금만으로 `ReadyToLeave`로 바꾸지 않고, 실제 출발 때 `Departed`로 기록한다. 수확·퀘스트 단계와 목표 게임의 출발 조건은 후속 범위다. 자세한 상태·패배 우선순위는 `StarSystemMapTravelImplementationSpec.md`를 따른다.

- `surge`에는 `elapsedSpawnSeconds`, `processedSpawnEvents`, `result`를 둔다. 결과는 `NotStarted`, `InProgress`, `Resolved`, `LeftWithRemaining`, `Failed` 등으로 표현한다. `Resolved`는 생성과 잔여 제거가 모두 끝난 경우, `LeftWithRemaining`은 지도 시제품에서 생성 종료 후 잔여 싹을 두고 실제 출발한 경우다.
- `simulationSeconds`는 신규 생성이 끝난 뒤에도 남은 바오밥이 성장하는 동안 증가한다. 생성 제한 시간과 같은 값으로 묶지 않는다.
- 퀘스트 상태는 `NotStarted`, `InProgress`, `Completed`, `RewardClaimed`로 구분한다. `chosenRewardOptionId`로 실제 선택을 기록한다.
- `worldInitialized`는 '최초 배치를 만들었음'을 뜻한다. 월드 오브젝트를 제거했다는 이유로 false로 돌려 수확물·보상을 다시 생성하지 않는다.

먼저 정상적인 행성 간 이동 시점의 기록을 구현한다. 행성 플레이 도중 저장·로드는 별도 확장 범위이며, 이 요약 상태만으로 중간 상태가 완전히 복구된다고 가정하지 않는다.

## 3. 바오밥 데이터

기존 위험도 설계와 연결하되 종류와 행성 설정을 분리한다.

```text
BaobabPlan
  profileId              : 참조한 행성용 바오밥 설정 ID
  durationSeconds        : 신규 생성 지속 시간
  spawnEventCount        : 생성 시점의 수
  spawnBatchSize         : 한 시점에 생성 요청할 수
  maxActive              : 동시에 살아 있을 수 있는 수
  minimumSpacing         : 배치 최소 간격
  threatCapacity         : 이 행성의 고정 위험도 한계량
  typePool[]             : 종류 ID, 등장 가중치, 종류별 활성 상한
```

예: 120초, 40회, 회당 1개, 동시 6개는 현재 시제품과 같은 생성 규모다. 균등 일정의 생성 간격은 `durationSeconds / spawnEventCount`로 계산하며 0초부터 마지막 생성 시점까지 사용한다. 첫 모델에서는 생성 간격과 시점 수를 각각 독립 값으로 중복 저장하지 않는다. 이후 비균등 웨이브가 필요하면 일정 구조를 별도로 확장한다.

종류별 시작 위험량·성장식·상한·모델·먹기 작업량은 `BaobabTypeDefinition`에 두고 종류 ID로 조회한다. 모든 수치를 행성마다 손으로 중복 작성하지 않는다. 여정을 생성할 때 실제 적용값을 스냅샷으로 고정할 수 있도록 한다.

중요한 구분:

- `spec.baobab`는 생성 규칙이다. 제거 후 빈자리와 현재 개체 수는 런타임 상태다.
- 현재 위험도는 살아 있는 개체의 종류·나이와 고정 `threatCapacity`에서 계산한다. 저장한 퍼센트를 독립적인 정답으로 유지하지 않는다.
- `maxActive`, `spawnBatchSize`, `spawnEventCount`는 서로 다른 수량이다.
- `profileId`는 출처와 조회 키다. `PlanetSpec`에 확정한 숫자가 있다면 해당 여정에서는 그 숫자를 적용한다. 공유 에셋 편집으로 진행 중인 행성이 갑자기 바뀌지 않게 한다.

## 4. NPC·작물·보상

```text
CropPlan
  cropTypeId
  count                : 수확 가능한 논리 단위 수

NpcPlan
  npcDefinitionId
  encounterKind        : QuestNpc / SpecialEvent / DestinationNpc
  contentId            : 퀘스트 또는 대화·이벤트 정의 ID
  requiredWool[]       : 양털 종류 ID와 수량
  rewardOptions[]      : 선택지 ID, 보상 종류, 보상 정의 ID
```

작물의 `count`는 장식용 풀잎 메시 수가 아니다. 수확 가능한 작물/군집 단위를 작물 시스템에서 일관되게 정의한다.

일반 NPC 보상 종류는 우선 `AddSheep`, `UpgradeSheep`를 지원한다. 강화할 실제 양의 ID는 플레이어가 보상을 받을 때 선택하고 `PlanetState.quest`에 선택 결과를 기록한다. 새 양을 추가하거나 강화하는 실제 변경은 여정의 보유 양 데이터에 적용한다.

`contentId`는 콘텐츠 정의의 조회 키이고 `requiredWool`·`rewardOptions`는 그 정의에서 이번 행성에 확정한 값이다. 진행 중 원본 정의와 스냅샷을 서로 다른 정답으로 사용하지 않는다.

NPC가 결정된 뒤 해당 퀘스트에 맞는 작물 종류와 공급 규칙을 결정한다. NPC, 퀘스트, 작물을 서로 독립적으로 뽑아 필요한 작물이 전혀 없는 행성이 되는 일을 피한다. 보유 양·휴식·기존 양털까지 고려해 모든 퀘스트를 반드시 완수 가능하게 할지는 별도 밸런스 결정이다.

도착 행성은 원작 인물과 대화 콘텐츠를 연결한다. 다음 항성계 이동 조건은 `StarSystemData`의 출구 규칙으로 관리하고 플레이어의 실제 양털을 조회한다. 이미지의 '18/30'을 행성 자체의 보유 양털 값으로 저장하지 않는다.

특수 행성의 양 요구·새 특성 부여 등은 `SpecialEvent`와 이벤트 정의를 연결해 확장한다. 이번 기본 모델에서 모든 특수 이벤트 필드를 미리 고정하지 않는다.

## 5. 항성계 데이터와 연결

```text
StarSystemData
  systemId
  systemIndex
  seed
  startPlanetId
  destinationPlanetId
  planets[]             : PlanetData 목록
  links[]               : fromPlanetId → toPlanetId
  exitRuleId            : 다음 항성계 이동 조건 정의
```

연결은 `links` 한 곳에 저장한다. 개별 행성에 별도의 `nextPlanetIds`도 함께 저장하지 않는다. 실행 시 빠른 조회가 필요하면 `links`에서 인접 목록을 만들어 캐시한다.

첫 지도는 순환 없는 단방향 그래프를 제안한다. 모든 연결은 `from.depth < to.depth`이고, 시작 행성에서 각 노드로 갈 수 있으며, 각 노드에서 도착 행성까지 경로가 있어야 한다.

```text
        B
      ↗   ↘
Start       Destination
      ↘   ↗
        C
```

B를 골랐다고 C 데이터를 삭제하거나 덮어쓰지 않는다. 현재 위치에서 갈 수 있는 연결만 선택 가능하게 한다. 다른 지도에서는 나중에 합류할 수 있는 노드도 있으므로 선택하지 않은 모든 행성을 즉시 영구 폐쇄하는 규칙도 넣지 않는다.

## 6. 여정 전체 데이터와 분리할 것

```text
RunProgressData
  currentSystemId
  currentPlanetId
  chosenPath[]              : 실제 방문한 systemId / planetId 순서
  mode                     : PlayingPlanet / SelectingRoute / Travelling / GameOver
  pendingDestinationId     : 이동 준비 중에만 존재

RunInventory / PartyState
  양털 종류별 보유량
  보유 양과 특성·강화·휴식 상태
```

- 현재 위치와 방문 경로는 여정 데이터가 소유한다. 행성별 `phase`는 해당 행성의 진행만 표현한다.
- 보유 양·양털·카드 설정을 각 행성에 복사하지 않는다.
- 양의 '다음 행성에서 한 번 휴식'은 실제 이동 완료 횟수로 처리한다. 지도 depth 차이나 항성계 번호 차이를 휴식 횟수로 사용하지 않는다.
- 재화 소비와 보상 수령은 한 번만 적용하고 결과를 기록한다. 지도 재열기·월드 재생성·데이터 재바인딩이 보상을 다시 지급하면 안 된다.

## 7. 언제 데이터를 만들고 바꾸는가

### 항성계에 처음 진입할 때

1. 항성계의 행성 ID와 연결을 만든다.
2. 각 행성의 role·크기·NPC 등 지도에 공개할 정보를 결정한다.
3. NPC에 맞는 퀘스트/작물 계획, 바오밥 규모와 행성 seed를 확정한다.
4. `state`를 미방문으로 초기화한다.

행성 메시와 모든 오브젝트를 이때 전부 만들 필요는 없다. 지도에 공개한 설정은 유지하고 실제 월드는 방문할 행성만 생성한다.

### 행성 안에서 플레이할 때

- `PlanetSpec`은 읽고, `PlanetState`와 활성 월드의 개체 상태를 갱신한다.
- 수확은 작물 상태와 실제 플레이어 양털에 반영한다.
- 퀘스트 완료·보상 선택은 해당 행성의 퀘스트 상태와 여정의 보유 양에 반영한다.

### 다음 행성을 선택할 때

1. 현재 행성이 출발 가능 단계인지, 목적지가 실제 outgoing link인지 검사한다.
2. `pendingDestinationId`를 설정하고 중복 이동 요청을 막는다.
3. 현재 행성 결과를 보존하고 목적지 `PlanetSpec`으로 새 월드를 준비한다. 준비만으로 보상이나 양 휴식을 다시 적용하지 않는다.
4. 목적지 준비 성공과 도착 확정 시 현재 위치·방문 경로·이전 행성 `Departed`·목적지 시작 상태를 한 번만 갱신한다. 실제 이동 1회에 해당하는 양 휴식도 이때 처리한다.
5. 새 월드를 활성화하고 이전 월드 오브젝트를 정리한다. 이전 행성의 데이터 기록은 남긴다.

생성 실패 시 현재 위치·경로·재화·휴식이 이미 변경된 채 남지 않게 한다. 미리 보존한 출발 상태로 복구하고 동일한 목적지 설정으로 재시도한다. 실패할 때마다 새 NPC·크기·보상을 뽑지 않는다.

## 8. 정의 에셋·인스턴스·저장의 경계

- `PlanetDefinition` 같은 작성용 에셋은 프리팹과 생성 범위·규칙을 제공하는 재사용 프리셋이다.
- `PlanetSpec`은 프리셋을 이번 여정의 한 행성에 적용해 확정한 생성 입력이다. ID·seed·실제 반경·확정 콘텐츠가 여기에 속한다.
- `PlanetState`는 그 행성을 플레이한 결과다. 공유 ScriptableObject에 방문 여부·수확량·퀘스트 진행을 쓰지 않는다.
- 지도 표시용 2D 좌표와 실제 월드의 Transform은 별개다. 첫 구조는 한 번에 한 행성을 활성화해도 된다.
- 저장용 데이터는 안정적인 ID와 값으로 표현한다. GameObject·Collider·Transform·Material 인스턴스를 행성의 저장 데이터로 들고 다니지 않는다.
- seed는 행성을 만들 때 한 번 정한다. 매번 방문하거나 지도를 열 때 새로 생성하지 않는다. 지형·작물·NPC·바오밥 등 목적별 난수 흐름도 분리한다.
- seed와 생성기 버전만으로 이미 제거한 싹·먹은 작물·받은 보상을 복원할 수는 없다.

중간 저장을 추가할 때는 활성 바오밥의 개체 ID·종류·실제 생성 시각·로컬 위치, 작물별 잔량, 진행 중 먹기와 배치, 생성 시점 진행도·필요한 난수 상태 등을 별도 월드 스냅샷으로 저장한다. 현재 `SpawnSlot`의 `cellId` 하나는 개체 고유 ID가 아니므로 단독 복구 키로 쓰지 않는다.

## 9. 현재 컴포넌트와 연결할 위치

| 데이터 | 현재 코드 연결 |
| --- | --- |
| `spec.seed` | `BaobabSurge.seed`, `randomizeSeed = false`, `PlanetSurfaceGrid.Prepare`의 목적별 seed |
| `terrain.sizeScale / radius / profileId` | `PlanetGenerationSettings`와 `PlanetWorldInstance.Prepare`. 고정 템플릿 원본에서 배율·명목 반경 적용 |
| `terrain.gravityStrength` | `GravitySource.strength` |
| `terrain.groundColor` | `PlanetWorldInstance.Prepare`의 월드 소유 8색 재질 인스턴스와 지도 아이콘 |
| 바오밥 시간·생성 시점 수·활성 상한·배치 간격 | 현재 `BaobabSurge`의 대응 설정 |
| `threatCapacity / 기본 종류` | `PlanetBaobabProfile`과 `BaobabThreatMeter`의 실행용 스냅샷 |
| `typePool / spawnBatchSize` | 후속 확장. 현재 `PlanetBaobabProfile`은 한계량과 기본 종류만 포함 |
| `state.simulationSeconds` | 현재 `BaobabSurge.SimulationSeconds`와 연결할 논리 시계 |
| NPC·작물·보상 계획 | 아직 구현되지 않은 시스템의 입력 계약 |

현재 `BaobabSurge.Begin()`은 초기화 후 첫 싹을 생성하는 경로다. 복구할 상태에 무조건 `Begin()`을 호출한 뒤 기존 개체를 추가하면 중복이 생긴다. 신규 진입과 상태 복원은 별도의 초기화 경로로 나눈다.

초기화 순서는 설정 주입 → 지면·배치 준비 → 플레이어 배치 → 초기 상태 확정 → 타이머/생성 시작으로 명시한다. MonoBehaviour의 임의 Start 순서에 맡겨 설정 주입 전에 플레이가 시작되지 않게 한다.

## 10. 첫 데이터 검증과 구현 범위

- 행성 ID가 고유하고 모든 연결의 시작·끝 ID가 존재해야 한다.
- 시작·도착 역할과 ID가 맞고 그래프가 역방향·순환·막다른 경로를 만들지 않아야 한다.
- 반경은 양수, 이번 공통 템플릿의 배율은 0.7~2.0이고 반경·분류와 일치해야 한다. 생성 수·재화 수량은 유효 범위, 참조한 프리셋·종류 ID는 조회 가능해야 한다.
- 지면 색은 유효한 6자리 HEX 값이고, 한 행성의 색 변경이 공유 재질이나 다른 행성·NPC·작물에 번지지 않아야 한다.
- NPC의 퀘스트와 작물 계획이 호환되어야 한다.
- 바오밥 한계량과 종류·활성 상한은 패배에 도달할 수 있는 구성인지 확인한다. 행성 크기만 키우면서 한계량을 무제한 올리지 않는다.
- 지도 재열기·경로 선택·월드 생성으로 공개된 `spec`이 바뀌지 않아야 한다.
- 데이터 인스턴스는 행성별로 독립적이어야 한다. 한 행성의 수확/퀘스트 변경이 다른 행성에 반영되면 안 된다.

후속 요청의 첫 구현으로 이 모델의 ID 조회, 작은 고정 항성계 데이터, 현재 행성 ID 변경, 크기·색 설정 주입, M 지도와 hover·클릭 이동을 연결했다. `StarSystemMapTravelImplementationSpec.md`의 구현 결과가 현재 범위다. 절차적 경로 생성·행성 간 점프 연출·완전한 중간 저장은 별도 단계다.

전체 행성 한 개의 형식 예시는 `Examples/PlanetDataExample.json`에 있다. 기본 바오밥 수치 외의 NPC·퀘스트·작물·지형 ID와 보상은 형식을 설명하기 위한 예시이며 실제 프로젝트 에셋이 존재한다고 뜻하지 않는다. 예시의 `modelVersion = 1`은 이 행성 데이터 형식의 버전이다.
