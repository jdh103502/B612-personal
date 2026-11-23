# B612 인계 문서

작업을 이어받는 사람이나 다른 LLM이 프로젝트를 빠르게 파악하도록 정리한 문서다. 2026-11-23, 생성기 버전 22, 커밋 `498e1e8` 기준이다.

## 이 프로젝트는

『어린왕자』와 『슈퍼마리오 갤럭시』를 축으로 한 우주 배경 카툰풍 3D 플랫포머의 컨셉 데모다.

- 작은 구형 행성마다 중력이 있어 행성을 한 바퀴 걸어서 돌 수 있다.
- 한 챕터는 6개 행성의 항성계 하나다. 행성마다 바오밥 싹이 퍼지는 시간 제한 페이즈가 있다.
- 플레이어는 양 카드로 싹을 먹여 위험도를 막는다. 페이즈가 끝나면 지도에서 다음 행성으로 이동한다.
- 이동 기술(대쉬·더블점프·활공)은 챕터마다 하나씩 해금된다.

엔진은 Unity 6000.3.23f1(URP 17.3, Input System 1.20)이다. 모델·텍스처는 Git LFS로 관리한다.

## 읽는 순서

| 순서 | 문서 | 내용 |
|---|---|---|
| 1 | [01-프로젝트 구조](01-ProjectStructure.md) | 폴더, 씬, 설정 에셋, 셰이더, 패키지 |
| 2 | [02-실행 흐름](02-RuntimeFlow.md) | 게임 시작부터 행성 이동까지의 흐름, 실행 순서, 데이터 모델, 입력 |
| 3 | [03-행성 지형 생성](03-TerrainGeneration.md) | 자연 지형 파이프라인, 높이 함수 계층, 생성기 버전 역사, 설정값 |
| 4 | [04-스크립트 레퍼런스](04-ScriptReference.md) | `Assets/B612/Scripts`의 모든 스크립트: 클래스, 주요 변수와 함수 |
| 5 | [05-도구와 검증](05-ToolsAndValidation.md) | `AgentScripts` 빌더·검증 도구, Unity CLI 실행법, 회귀 검사 방법 |

## 함께 봐야 하는 기존 문서

- **[GameDesign.md](../GameDesign.md):** 목표 게임 기획의 기준이다. 검토 중인 아이디어와 확정 규칙을 구분해서 읽는다.
- **[README.md](../../README.md):** 실행 방법과 현재 데모 상태다.
- **[AGENTS.md](../../AGENTS.md):** 에이전트 작업 규칙과 지금까지의 결정 요약이다.
- **[NaturalPlanetTerrainImplementationResults.md](../NaturalPlanetTerrainImplementationResults.md):** 생성기 버전별 구현·검증 수치 기록이다.
- **[PlayerTraversalImplementationSpec.md](../PlayerTraversalImplementationSpec.md):** 이동 기술 명세와 검증이다.
- 그 밖의 `Docs/*.md` 목록과 용도는 [05-도구와 검증](05-ToolsAndValidation.md#기존-설계구현-문서-목록)에 있다.

## 꼭 지켜야 하는 작업 규칙

1. **기획 문서의 위상.** `Docs/GameDesign.md`의 검토 중 아이디어나 미정 사항을 확정 규칙으로 취급하지 않는다. 사용자가 정한 변경은 기획 문서에도 반영한다.
2. **지형을 바꾸면 생성기 버전을 올린다.** 같은 seed로 같은 행성이 나오는 것이 계약이다.
   - 지형 모양을 바꾸는 변경은 `PlanetGenerationSettings.GeneratorVersion`을 올린다.
   - 이전 버전은 설정값 하나로 비트 단위까지 재현되게 남긴다(예: `spawnPadPlanar=false`이면 v21).
   - `AgentScripts/TerrainPrepFingerprint.cs`에 `RunV??` 재현 진입점을 추가해 해시가 같은지 확인한다.
   - 이전 버전 경로의 식은 글자 그대로 둔다. 식을 함수로 옮기는 것만으로 JIT 부동소수점 반올림이 바뀐 적이 있다.
3. **스크립트 미러.** `AgentScripts/runtime/*.cs`는 `Assets/B612/Scripts`의 같은 이름 파일과 내용이 같아야 한다. 고치면 둘 다 고친다.
   - 현재 다음 7개 파일은 `Assets` 쪽에만 있다: `BaobabTypeDefinition`, `EatingProgressGraphic`, `LambCardHUD`, `LambEatingIndicators`, `PlanetBaobabProfile`, `SproutBubbleGraphic`, `SproutHorizonIndicators`.
4. **설정 에셋.** 코드 기본값만 바꾸면 기존 `.asset`의 직렬화 값이 이긴다. 새 기본값은 `Assets/B612/Settings`의 생성 설정 에셋 3개에도 직접 써 넣는다.
   - 대상: `Chapters/ChapterJourneyGeneration.asset`, `PlanetTravel/EmptyPlanetGeneration.asset`, `PlanetTravel/ProceduralTerrainTestGeneration.asset`
5. **에디터 메모리.** 긴 검증 중에는 행성 2곳마다 Play 모드를 껐다 켜며 메모리를 정리한다. 에디터가 시스템 메모리 부족으로 여러 번 꺼진 적이 있다. 자세한 방법은 [05-도구와 검증](05-ToolsAndValidation.md#에디터-메모리-관리)에 있다.
6. **Git.** 기능마다 브랜치에서 작업하고 검증 뒤 main에 fast-forward 병합한다. 커밋 메시지는 한국어로 쓴다. force push와 히스토리 재작성은 하지 않는다.

## 현재 상태 요약 (2026-11-23)

- **지형:** 생성기 버전 22, 프로필 `terrain.natural.v1`이다.
  - 행성 전체에 이어지는 연속 기복과 큰 벼랑 몇 개가 있다.
  - 행성 둘레의 0.6~0.8배를 흐르는 골짜기 강 하나가 하구 호수로 들어간다.
  - 스폰 주변에는 NPC를 둘 평면 빈터(지름 약 6m)가 있다.
- **이동:** 걷기·점프·코요테 타임·더블점프·대쉬·활공이 있다. 대쉬가 비탈 꼭대기에서 튀어 오르지 않게 상승 속도를 제한한다.
- **게임플레이:** 바오밥 싹 스폰(120초)과 위험도 게이지(100% 게임오버)가 있다. 양 카드 4장으로 싹을 먹인다.
- **진행:** 6노드 항성계 지도와 행성 이동, 3챕터 여정(챕터마다 기술 하나 해금)이 있다. F12 개발자 디버그가 있다.
- **아직 없는 것:** NPC·퀘스트·작물 수확·양털·중간 저장이다. 기획만 있다.
- **확인하지 않은 것:** 생성기 v9 이후 플레이어 빌드는 다시 확인하지 않았다.
