# 지형 외관 개선 구현 결과

2026-11-14. `ProceduralTerrainTraversalDesign.md` 17절의 순서로 적용했다. 비교 씬은 `B612_ChapterJourney`이고 여정 seed는 612101, 1챕터 시작 행성 seed는 395484021이다. 기준 반경은 12m다.

## 적용한 설정

- 절차적 지면은 하나의 submesh와 행성 소유 재질을 사용한다. `B612/Planet Ground`는 대표색을 중심으로 고도·경사·지역·강가 색을 섞는다. 정점 색의 R/G/B/A는 각각 언덕 가중치, 골짜기 가중치, 강가 가중치, 독립 저주파 명암이다.
- 종이 알베도는 행성 로컬 좌표에서 세 방향으로 투영한다. 기존 mipmap 텍스처를 1.4회/m로 샘플링하며 대비는 0.05, 거리 감쇠는 5~16m, smoothness는 0.1이다. 미세 노멀 강도는 0이다. URP Forward·ShadowCaster·DepthOnly·DepthNormals 패스를 둔다.
- 큰 높이층의 주파수는 1.2다. 중간 높이층은 3.2m를 사용하되 작은 행성에서는 반경/1.2로 상한을 둔다. 진폭 상한은 min(0.55m, 0.08×반경), 배분은 80:20이다. 단조 높이 커브는 (0, −1), (0.30, −0.12), (0.65, 0.12), (1, 1)이다. warp와 ridged는 독립 옵션이며 검증한 기본값은 0이다.
- 코스 핵심 영역은 검수한 단면과 출발·착지 평탄부, 우회·탈출 동선을 포함한다. 외곽은 기본 2.4m 전이에서 주변 높이와 섞는다. 골짜기 끝의 벽은 핵심 영역 바깥에서만 완만해진다. 기본 통로 반폭 1.25m와 연결로 중심 높이는 보존한다. 전이 폭이 늘어난 만큼 요소의 배치 예약 반경을 늘렸다.
- 생성기 5와 `terrain.stylized.v1`을 세 설정 에셋에 적용했다. 계획은 커브 키와 파생 seed를 복사해 보관한다. 데이터 형식은 1이다.

## 전후 화면

같은 행성 방향·카메라 위치·씬 조명으로 1280×720을 캡처했다. 근거리·전체 비교에서는 캐릭터, HUD와 안내 표시를 숨겼다. 플레이 화면은 실제 게임 카메라와 안내 표시를 사용한다.

| 단계 | 근거리 | 행성 전체 |
| --- | --- | --- |
| 변경 전 | [근거리](../Artifacts/TerrainAppearance/before-near.png) | [전체](../Artifacts/TerrainAppearance/before-whole.png) |
| 변경 전 단색, 노멀맵 없음 | [근거리](../Artifacts/TerrainAppearance/before-flat-near.png) | [전체](../Artifacts/TerrainAppearance/before-flat-whole.png) |
| 연속 재질만 적용 | [근거리](../Artifacts/TerrainAppearance/material-near.png) | [전체](../Artifacts/TerrainAppearance/material-whole.png) |
| 큰 높이층과 평탄부 적용 | [근거리](../Artifacts/TerrainAppearance/height-near.png) | [전체](../Artifacts/TerrainAppearance/height-whole.png) |
| 외곽 접합까지 적용 | [근거리](../Artifacts/TerrainAppearance/after-near.png) | [전체](../Artifacts/TerrainAppearance/after-whole.png) |
| 최종 형상 단색 | [근거리](../Artifacts/TerrainAppearance/after-flat-near.png) | [전체](../Artifacts/TerrainAppearance/after-flat-whole.png) |
| 실제 플레이 | [변경 전](../Artifacts/TerrainAppearance/before-play.png) | [변경 후](../Artifacts/TerrainAppearance/after-play.png) |

재질만 바꾼 화면에서 사각형 색 경계와 절벽을 따라 늘어난 종이 무늬가 사라졌다. 단색 전후 화면에서 골짜기 양 끝과 주변 지면의 접합이 둥글어졌다. 중앙 횡단부의 급한 벽은 검수 치수를 따른다.

## 이동 회귀

| 챕터 | 행성 seed | 기본 우회 | 능력 지름길 | 안정 접지 높이 |
| --- | ---: | ---: | ---: | ---: |
| 1 골짜기 | 395484021 | 9.11초 | 대쉬 3.17초 | 0.00m |
| 2 높은 턱 | 1798197590 | 9.88초 | 더블점프 3.75초 | +2.00m |
| 3 활공 | 558768094 | 12.80초 | 활공 5.41초 | +0.40m |

실제 씬에서 해금 전 보행, E 해금 후 지름길, 착지 자원 회복을 확인했다. 활공은 중앙 평탄부 좌표 (6.24m, 0.00m)에 착지했다. F12로 대기를 줄였고, M 지도와 분기를 통해 11회 전환했다. R 리스폰, 해금·조사·안내 위치 도달, 양 소환·먹기·카드 회수, 전환 실패 시 기존 행성 보존이 통과했다. [연속 플레이 로그](../Artifacts/TerrainAppearance/Journey.txt)

36개 계획에서 동일 입력 재현, 유한 높이와 자유 지형 진폭, 보호 통로, 타일 지역 연결을 확인했다. 29개에 강이 생성됐고 7개에서 생략됐다. 여울 바닥 −0.06m, 물속 이동 배율 0.65와 수면 위 배율 1을 확인했다. [시드별 계획](../Artifacts/TerrainAppearance/Plans.json)

캠페인 반경 12m를 기준으로 일반 행성 0.7/1/1.5/2배에 각각 세 시드를 적용한 12건이 통과했다. 검수 지형 양방향 통과, 표시와 충돌 메시의 동일성, 1배 메시의 재생성 해시와 닫힌 cube-sphere, 바오밥 후보 투영과 원본 재질 보존을 확인했다. [0.7배](../Artifacts/TerrainAppearance/Scale0.json) · [1배](../Artifacts/TerrainAppearance/Scale1.json) · [1.5배](../Artifacts/TerrainAppearance/Scale2.json) · [2배](../Artifacts/TerrainAppearance/Scale3.json)

공통 경로의 반경 5.3m 설정에서도 0.7/1/1.5/2배 준비가 통과했다. 생성기 4 입력과 동일 월드의 중복 준비 요청을 거부했다. 이동 능력 출력을 고의로 낮춘 일반 행성의 경우 두 선택 요소를 제거하고 3회 안에 준비를 마쳤다. [공통 행성](../Artifacts/TerrainAppearance/MainSized.json) · [제한된 복구](../Artifacts/TerrainAppearance/Repairs.json)

시작 행성은 챕터별 3개 여정 seed로 9건, 고정 예비 배치는 3종을 각각 검증했다. 활공의 고정 예비 배치는 새 외곽에서 연결로 검사를 실패해 16절의 검수 외곽 형상으로 복원한 뒤 통과했다. 실제 여정에서는 세 챕터 모두 첫 후보를 사용했다. 두 메시 경로의 닫힌 이음선과 노멀을 검사했다. 검수 구간의 치수는 Collider에서 확인했고, 정확한 정점 위에서 Raycast가 누락된 경우 0.2mm 옆에서 같은 기준 높이를 검사했다. [1챕터](../Artifacts/TerrainAppearance/Reviewed1.json) · [2챕터](../Artifacts/TerrainAppearance/Reviewed2.json) · [3챕터](../Artifacts/TerrainAppearance/Reviewed3.json)

## 형상과 비용

동일 시작 행성의 정점 수는 97,905개, 삼각형은 195,806개로 같았다. 지면 재질은 8개에서 1개로 줄었다. 같은 고정 골짜기 기준 영역을 분리하고, 나머지 삼각형의 실제 표면 면적으로 경사 구간을 가중했다.

| 단계 | 0~15도 | 15~30도 | 30도 초과 | 고정 비교 영역/전체 | 메시 준비와 이동 검사 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 변경 전 | 90.50% | 7.88% | 1.63% | 6.70% | 1,099ms |
| 재질만 적용 | 90.50% | 7.88% | 1.63% | 6.70% | 1,019ms |
| 큰 높이층 적용 | 92.53% | 6.44% | 1.03% | 6.82% | 1,431ms |
| 최종 | 94.01% | 3.81% | 2.18% | 6.76% | 1,023ms |

최종에서 30도 초과 비율도 소폭 증가했다. 높이가 집중되는 외곽 접합이 바뀐 결과이며 필수 보행로는 최종 Collider로 별도 검사했다. 근거리 고정 렌더와 GPU 픽셀 읽기 평균은 이 표본에서 18.1ms→22.0ms였다. 이는 에디터의 강제 렌더와 읽기를 합친 값으로 실제 플레이 FPS를 나타내지 않는다. 메시는 두 경로 모두 기존 표본 간격을 유지한다.

일반 행성 12건의 전체 준비 시간은 크기별 약 0.39~0.47초, 0.69~0.75초, 1.87~2.09초, 2.25~2.32초였다. 삼각형이 2,097,152개를 넘는 메시는 기존 물리 엔진 경고에 따라 Fast Midphase를 끄며, 이동 검사용 Collider에도 같은 설정을 사용한다.

월드 전환에서 떠난 지면 메시가 해제됐음을 연속 플레이에서 확인했다. 행성별 지면 재질은 월드가 소유하고 종료 시 해제한다. 설정 에셋의 원본 재질은 복제해 사용한다. `AgentScripts/runtime`과 `Assets/B612/Scripts`의 대응 소스 47쌍을 해시로 대조했다.

## Windows 플레이어

Unity 6000.3.23f1의 Windows 개발 빌드와 D3D12 실행을 확인했다. 초기 빌드에서는 기존 URP 셰이더 컴파일 메모리 오류가 기록됐고, 플레이어에서 물·안내 셰이더의 이름 조회에 의존하던 초기화가 실패했다. 세 생성 설정에 물·안내 재질 에셋을 참조시킨 뒤 다시 빌드했다. 최종 빌드의 오류는 0건이고 플레이어 검사는 종료 코드 0으로 통과했다.

플레이어의 시작 행성에서 새 지면 셰이더와 종이 텍스처가 로드되고, 바닥의 캐릭터 그림자와 민트색 안내가 렌더링됐다. 4개 패스를 확인했고 DepthOnly 패스의 128×128 테스트에서 지면이 12,092픽셀에 깊이를 썼다. 기본 카메라의 depth-copy 정책과 별개로 패스 출력을 확인한 결과다. 개발 빌드에서 시작 행성만 렌더링 검사했고, 전체 3챕터 이동은 에디터에서 검증했다. [플레이어 화면](../Artifacts/TerrainAppearance/player-start.png) · [실행 기록](../Artifacts/TerrainAppearance/PlayerVerification.txt)

## 2차 개선 · 18절 (2026-11-18)

### 적용 설정

생성기와 Plan은 버전 6, 프로필은 `terrain.stylized.v2`, 데이터 형식은 버전 1이다. 반경 12m 기준 언덕은 최대 2개(설정 높이 1.05–1.40m, 영향 반경 7.6–8.0m), 분지는 최대 1개(깊이 0.55–0.80m, 영향 반경 6.3–7.0m)다. 길이와 높이는 작은 행성에서 함께 축소되며, 시작 행성과 반경 8m 미만은 한 덩어리까지 시도한다. 설정 범위의 상단에 가깝게 뽑아 조명이 닿는 전경과 근거리에서 높낮이가 읽히도록 했다. 별도 파생 시드를 쓰며, 확정 형상의 중심과 높이, 접근로, 보완 사유는 비교 자료의 Plan 스냅샷에 있다.

선형 커브의 저주파 잔여 진폭은 0.24/0.07m이고, warp/ridge 강도는 0이다. 큰 형태와 모든 잔여 항의 합은 `min(1.6m, 0.14R)` 예산 안에 들어온다. 보호 길 바깥 어깨는 높이차에 따라 최대 0.9m 더 넓어진다. 최종 MeshCollider에서 능력 없이 언덕·분지 중심에 들어갔다가 복귀하도록 검사한다. 영향권 안의 바오밥 후보는 이 접근선 0.35m 이내에서 기존 경사·보호·물길 조건도 만족해야 한다. 지형 후보는 64회, 최종 메시 준비는 5회의 기존 상한을 사용한다.

지면 재질은 행성 대표색에 흙·식생·암석 색을 강도 0.70으로 섞는다. 식생 전이 범위는 0.35–0.63, 고도 색 범위는 ±1.05m다. 종이 대비 0.05와 미세 normal 0을 사용한다. 영역 간 색차는 조명이 닿는 면에서 잘 보이고, 그늘진 반구에서는 작아진다. [야간 612101의 일반 행성 앞면](../Artifacts/TerrainAppearanceV6/final-612101-c2-normal-front.png) · [같은 구도의 조명 없는 색](../Artifacts/TerrainAppearanceV6/final-612101-c2-normal-front-color.png)

### 같은 시드 비교

여정 시드 612101·1·77에서 각 챕터의 시작 행성과 일반 노드 1을 준비했다. 일반 행성은 크기를 반경 12m로 고정했다. 각 조합마다 `near`(같은 플레이 거리, 고정 카메라), `front`/`side`/`back`(전경과 `-flat` 단색)를 저장했다. `shape`는 6버전 형상에 기준 색 혼합을 적용한 진단 화면이다. HUD가 포함된 실제 Play 캡처는 기본 여정 시드의 첫 시작 행성이다. [5버전 Play](../Artifacts/TerrainAppearanceV6/v5-actual-play.png) · [형상 비교 Play](../Artifacts/TerrainAppearanceV6/shape-actual-play.png) · [최종 Play](../Artifacts/TerrainAppearanceV6/final-actual-play.png)

| 여정 시드 | 5버전 기준 | 큰 형태만 변경 | 최종 색 |
| --- | --- | --- | --- |
| 612101 | [앞면](../Artifacts/TerrainAppearanceV6/v5-612101-c2-normal-front.png) · [통계](../Artifacts/TerrainAppearanceV6/v5-612101.json) | [앞면](../Artifacts/TerrainAppearanceV6/shape-612101-c2-normal-front.png) · [통계](../Artifacts/TerrainAppearanceV6/shape-612101.json) | [앞면](../Artifacts/TerrainAppearanceV6/final-612101-c2-normal-front.png) · [통계](../Artifacts/TerrainAppearanceV6/final-612101.json) |
| 1 | [옆면](../Artifacts/TerrainAppearanceV6/v5-1-c1-normal-side.png) · [통계](../Artifacts/TerrainAppearanceV6/v5-1.json) | [옆면](../Artifacts/TerrainAppearanceV6/shape-1-c1-normal-side.png) · [통계](../Artifacts/TerrainAppearanceV6/shape-1.json) | [옆면](../Artifacts/TerrainAppearanceV6/final-1-c1-normal-side.png) · [통계](../Artifacts/TerrainAppearanceV6/final-1.json) |
| 77 | [단색 앞면](../Artifacts/TerrainAppearanceV6/v5-77-c2-normal-front-flat.png) · [통계](../Artifacts/TerrainAppearanceV6/v5-77.json) | [단색 앞면](../Artifacts/TerrainAppearanceV6/shape-77-c2-normal-front-flat.png) · [통계](../Artifacts/TerrainAppearanceV6/shape-77.json) | [단색 앞면](../Artifacts/TerrainAppearanceV6/final-77-c2-normal-front-flat.png) · [통계](../Artifacts/TerrainAppearanceV6/final-77.json) |

행성당 균일 구면 방향 16,384개로 최종 Collider의 방사 높이 P10/P50/P90·최솟값·최댓값을 구했다. 경사는 메시 삼각형 면적으로 가중해 15° 이하 / 15° 초과–30° 이하 / 30° 초과로 분류했다. 통계의 분류 우선순위는 코스 핵심 → 보호 통로·스폰 → 자유 지형이다. 아래는 자유 지형만의 `P90−P10` 변화와 최종 큰 형태 개수다.

| 시드 | 챕터 | 시작 행성 (m, 형태 수) | 일반 행성 (m, 형태 수) |
| --- | ---: | --- | --- |
| 612101 | 1 | 0.317 → 0.303, 1 | 0.199 → 0.528, 3 |
| 612101 | 2 | 0.194 → 0.284, 1 | 0.260 → 0.598, 3 |
| 612101 | 3 | 0.250 → 0.310, 1 | 0.228 → 0.557, 2 |
| 1 | 1 | 0.150 → 0.189, 1 | 0.257 → 0.646, 3 |
| 1 | 2 | 0.315 → 0.325, 1 | 0.189 → 0.531, 3 |
| 1 | 3 | 0.225 → 0.301, 1 | 0.413 → 0.642, 3 |
| 77 | 1 | 0.208 → 0.220, 1 | 0.312 → 0.598, 3 |
| 77 | 2 | 0.162 → 0.265, 1 | 0.185 → 0.658, 3 |
| 77 | 3 | 0.264 → 0.265, 1 | 0.277 → 0.589, 3 |

일반 행성 9개 중 3개가 조절 목표 하단 0.6m 이상이었다. 시드 612101의 3챕터 일반 행성은 분지를 배치할 자리를 확보하지 못했다. 나머지 일반 행성 8개는 언덕 두 개와 분지 한 개를 포함했다. 18개 모두 첫째 또는 둘째 메시에서 통과했으며 검수 고정 배치나 평탄 안전 복구를 사용한 사례와 비교용 투영 누락은 0건이었다. 612101의 2챕터 일반 행성의 자유 지형은 흙·식생·암석 우세 면적이 약 49%·48%·3%다. 일반 행성 전경에서 넓어진 실루엣을 확인했고, 예약이 많은 시작 코스의 평탄부와 의도된 턱은 남아 있다.

### 보행과 진행 회귀

| 챕터 | 행성 시드 | 기본 우회 | 해금 후 지름길 | 안정 착지 높이 |
| --- | ---: | ---: | ---: | ---: |
| 1 골짜기 | 395484021 | 9.11초 | 대쉬 3.16초 | 0.00m |
| 2 높은 턱 | 1798197590 | 9.84초 | 더블점프 3.74초 | +2.00m |
| 3 활공 | 558768094 | 12.74초 | 활공 5.42초 | +0.40m |

빌드 인덱스 0의 `B612_ChapterJourney`에서 실제 입력으로 기본 우회, E 해금, 대응 능력과 안정 착지·복귀, R 리스폰, F12 개발자 설정을 확인했다. 활공은 중앙 착지대 로컬 (6.24m, 0.00m)에 도착했다. 양 소환·먹기·카드 회수를 확인했고 11회 전환을 거쳐 세 챕터를 끝냈다. 실패한 챕터 전환에서는 기존 월드·진행을 보존했고, 성공한 전환에서는 떠난 월드의 지면/물 메시와 소유 지면 재질이 해제됐다. 바오밥 페이즈 스킵으로 대기 시간을 줄였다. [실제 여정 로그](../Artifacts/TerrainAppearanceV6/Journey.txt)

36개 계획에서 동일 입력 결정성, 유한 높이, 자유 진폭 상한, 보호 통로 높이와 지역 연결을 확인했다. 강 생성 29건, 생략 7건이며 여울 바닥 −0.06m와 감속 배율 0.65를 보존했다. 물 표면 삼각형은 바깥쪽을 향하고 양 끝이 바닥보다 낮다. [36개 계획](../Artifacts/TerrainAppearanceV6/Plans.json)

일반 행성 0.7/1/1.5/2배에 세 시드씩 적용한 12건에서 보행·표시/충돌 일치·바오밥 후보를 확인했다. 1배에서는 재생성 메시 해시와 슬롯 수가 일치했고, 삼각형 모서리 검사에서 열린 이음선이 없었다. 기준 반경 5.3m의 공통 경로에서 네 크기를 준비했고 5버전 입력이 거절됐다. 잘못된 능력 설정을 주입했을 때는 세 번째 메시에서 선택 코스 생략으로 복구했다. 준비 후 설정의 색·높이 커브를 바꿔도 이미 준비된 행성의 Plan·높이·소유 재질은 그대로였다. [0.7배](../Artifacts/TerrainAppearanceV6/Scale0.json) · [1배](../Artifacts/TerrainAppearanceV6/Scale1.json) · [1.5배](../Artifacts/TerrainAppearanceV6/Scale2.json) · [2배](../Artifacts/TerrainAppearanceV6/Scale3.json) · [작은 행성](../Artifacts/TerrainAppearanceV6/MainSized.json) · [복구](../Artifacts/TerrainAppearanceV6/Repairs.json) · [설정 스냅샷](../Artifacts/TerrainAppearanceV6/Immutability.json)

세 여정 시드의 시작 행성 9개와 각 챕터의 고정 예비 배치 3종을 검사했다. 검수 단면, 재현 가능한 배치, 닫힌 메시, 왕복 연결로와 이동 시간 이득을 확인했다. [골짜기](../Artifacts/TerrainAppearanceV6/Reviewed1.json) · [높은 턱](../Artifacts/TerrainAppearanceV6/Reviewed2.json) · [활공](../Artifacts/TerrainAppearanceV6/Reviewed3.json)

### 파란 조각 진단

여정 612101의 1챕터 시작 행성 뒷면에서 지평선 가까운 밝은 파란 조각을 확인했다. 같은 프레임에서 `Shallow connected river`만 끄면 해당 조각이 사라졌다. Near 캡처에 보이던 긴 하이라이트도 물 표면에서 나왔다. 물 표면 양 끝 폭을 줄여 강바닥 아래로 잇고 바깥쪽 면만 표시하도록 했다. 매끈함을 0.85에서 0.36으로 낮춘 뒤 밝은 조각과 긴 하이라이트가 약해졌고 물길 면 자체는 보인다. 강바닥과 보행 감속에 쓰는 계획은 동일하다. 사용자가 처음 지적한 컷의 정확한 구도는 확보하지 못해, 관찰한 조각이 그 컷과 동일한지는 미확인이다. [수정 전](../Artifacts/TerrainAppearanceV6/diagnostic-612101-back.png) · [물만 숨김](../Artifacts/TerrainAppearanceV6/diagnostic-612101-back-no-water.png) · [수정 후](../Artifacts/TerrainAppearanceV6/final-612101-c1-start-back.png)

### 준비 비용과 범위

비교 행성 18개의 지형 준비·검사 평균은 5버전 1.01초, 형상 단계 1.13초, 최종 1.11초였다. 최종 범위는 0.90–1.65초였다. 정점 97,905–107,738개와 삼각형 195,806–215,472개 범위는 세 단계에서 같았고 지면은 재질 하나다. 고정 근거리 카메라 960×540의 강제 렌더와 픽셀 읽기 평균은 6.68 → 6.13 → 5.63ms였다. 이 수치는 개별 실행 비교이며 플레이 FPS나 렌더링 성능 향상을 판정하는 데 쓰지 않았다.

일반 행성 준비 시간은 0.7배 0.50–0.53초, 1배 0.87–0.98초, 1.5배 1.57–1.67초, 2배 2.78–3.09초였다. 바뀐 높이 항과 양방향 접근 검사 비용이 포함된다. warp/ridge는 기본 비활성화 상태에서만 측정했다. 런타임 원본과 Assets의 대응 C# 소스 48쌍은 해시가 일치한다.

## 일반 행성의 높낮이 강화 (2026-11-20)

생성기 버전 7은 일반 행성에 `normalPlanetHillHeights`와 `normalPlanetBasinDepths`를 적용한다. 세 절차적 생성 설정의 시작값은 반경 12m 이상에서 언덕 2.5~3m, 분지 깊이 1~1.5m다. 반경 12m보다 작은 행성은 높이차와 영향 반경을 함께 줄인다. 일반 행성의 자유 지형 높이 예산은 `min(3.35m, 0.28 × 반경)`이다. 시작·도착 행성은 기존 설정을 쓴다. 필수 보행로와 스폰은 기존 방식으로 보호하고, 최종 충돌체에서 평지와 언덕·분지를 기본 이동으로 왕복한다. 왕복 검사에 실패한 언덕·분지는 기존 절차에 따라 낮추거나 생략한다.

### 같은 시드의 전후 비교

여정 시드 612101의 2챕터 일반 행성 시드 1076931390으로 비교했다. 높이는 기준 반경 대비 최종 MeshCollider의 방사 높이다. 언덕 열은 배치된 언덕 중심 중 가장 높은 값이다. 자유 지형 P90−P10은 코스 핵심과 보호 통로를 제외한 높이 분포를 나타낸다.

| 크기 | 가장 높은 언덕 중심 | 분지 중심 | 자유 지형 P90−P10 |
| --- | ---: | ---: | ---: |
| 0.7배 / 8.4m | 1.00 → 2.14m | −0.57 → −1.04m | 0.41 → 0.83m |
| 1배 / 12m | 1.20 → 2.82m | −0.66 → −1.38m | 0.60 → 0.98m |
| 2배 / 24m | 1.31 → 2.99m | −0.63 → −1.34m | 0.258 → 0.264m |

2배 표본의 큰 높이차는 언덕과 분지 주변에 집중됐다. 1배 표본의 다른 언덕 하나는 보행 검사 후 낮아졌다. 세 크기에서 보호 보행로 검사를 통과했다. 1챕터 시작 행성 시드 395484021의 메시 정점 해시와 바오밥 후보 수는 전후 일치했다.

| 구도 | 변경 전 | 변경 후 |
| --- | --- | --- |
| 행성 실루엣 | [전체](../Artifacts/DynamicPlanetRelief/before-planet.png) | [전체](../Artifacts/DynamicPlanetRelief/after-planet.png) |
| 높은 언덕 | [근접](../Artifacts/DynamicPlanetRelief/before-hill.png) | [근접](../Artifacts/DynamicPlanetRelief/after-hill.png) |
| 분지 | [근접](../Artifacts/DynamicPlanetRelief/before-basin.png) | [근접](../Artifacts/DynamicPlanetRelief/after-basin.png) |

### 생성 및 이동 검증

Unity 6000.3.23f1 에디터에서 여정 시드 612101·1·77의 일반 행성을 0.7·1·1.5·2배로 준비했다. 12건 모두 최종 메시의 왕복 검사, 렌더 메시와 충돌 메시 일치, 안전한 바오밥 후보 확인을 통과했다. 반경 5.3m를 기준으로 쓰는 공통 생성 설정에서도 네 크기를 확인했다.

`B612_ChapterJourney`에서 실제 입력으로 세 챕터의 기본 우회·E 해금·능력 지름길·복귀·R 리스폰·11회 행성 전환을 확인했다. 방문한 일반 행성 6개에서 강화 지형과 잔디·흙 텍스처가 함께 적용됐고, 바오밥 생성과 양의 먹기도 통과했다. [에디터 여정 로그](../Artifacts/DynamicPlanetRelief/EditorJourney.txt)
