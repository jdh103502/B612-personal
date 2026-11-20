# 잔디·흙 텍스처 적용 결과

2026-11-20. Unity 6000.3.23f1의 열린 B612 프로젝트에서 적용했다. 비교 씬은 빌드 인덱스 0의 `B612_ChapterJourney`다. 여정 시드는 612101, 시작 행성 시드는 395484021이다.

## 원본과 임포트

JulioVII의 무료 `Stylized_GrassAndDirt.zip`을 itch.io에서 내려받았다. ZIP SHA-256은 `706bddc944f28c751a99fccbbd90f8b00cf5f89ea57f534b74aacc469733fd0d`다. 아카이브에는 JPEG와 Blender 파일이 있고 별도의 라이선스 문서는 없었다. 제작자 페이지에 CC-BY 버전은 표시되어 있지 않으며, 상업 사용을 허용하고 재판매를 제외한다고 안내한다. 원본 파일명과 B612 적용 범위는 README 크레딧과 플레이어에 동봉되는 고지에 연결했다. [제작자 원본 페이지](https://juliovii.itch.io/ftpgrass-dirt) · [배포용 고지](../Assets/StreamingAssets/ThirdPartyNotices/JulioVII-GrassDirt.txt)

| 슬롯 | 원본 파일 | Standalone 형식 |
| --- | --- | --- |
| 흙 Base Color | `Stylized_HandpaintedDirt_01_basecolor.jpg` | BC7 sRGB |
| 흙 Normal | `Stylized_HandpaintedDirt_01_normalogl.jpg` | BC5 Linear |
| 잔디 Base Color | `Stylized_HandpaintedGrass_01_basecolor.jpg` | BC7 sRGB |
| 잔디 Normal | `Stylized_HandpaintedGrass_01_normalogl.jpg` | BC5 Linear |

네 원본 JPEG는 모두 4096×4096이다. Unity 임포트 상한은 2048×2048이며 Repeat, Trilinear, Aniso 4, mipmap 12단계, Read/Write 비활성을 적용했다. 2×2 반복에서 네 맵의 접합부를 육안으로 확인했다. Normal은 OpenGL 파일을 사용하고 Flip Green Channel을 끈 상태로 임포트했다. 두 노멀 버전의 반전된 녹색 채널 평균 오차는 흙 1.09/255, 잔디 0.77/255였다. OpenGL 버전의 X/Y 성분은 모두 원본 Height에서 계산한 대응 경사 방향과 양의 상관을 보였다.

## 재질과 셰이더

| 설정 | 최종값 |
| --- | --- |
| 사용 재질 | `StylizedPlanetGround.mat`, 절차적 생성 설정 3개가 공유 |
| 텍스처 스위치 | 셰이더 기본 0, 적용 재질 1 |
| 흙 / 잔디 반복 길이 | 각각 2m |
| Base Color / 흙 Normal / 잔디 Normal 강도 | 0.50 / 0.20 / 0.20 |
| 흙 선형 평균 RGB | (0.532312, 0.328654, 0.109131) |
| 잔디 선형 평균 RGB | (0.431676, 0.584219, 0.037424) |
| 색 보정 및 종이 대비 | RGB 비율 0.35~2.2, 텍스처 사용 중 유효 종이 대비 약 0.036 |
| Smoothness | 0.10 |

Base Color는 원본 맵의 선형 평균으로 나눈 RGB 디테일을 기존 팔레트에 섞는다. 흙과 잔디의 색·노멀은 기존 식생과 경사 가중치를 사용한다. 강가에서는 식생 가중치가 낮아지고, 암석 비중이 1이면 암석색과 기하 노멀을 사용한다. Forward와 DepthNormals는 같은 오브젝트 공간 노멀 계산을 사용한다. ShadowCaster와 DepthOnly는 기존 지면 메시로 렌더링한다.

## 에디터 화면과 렌더링 검증

| 확인 | 캡처 |
| --- | --- |
| 변경 전 에디터 Game 뷰 | [변경 전 실제 플레이](../Artifacts/GrassDirt/before-play.png) |
| 동일 프레임, 1280×720, 기본 카메라와 HUD | [텍스처 끔](../Artifacts/GrassDirt/before-same-frame.png) · [켬](../Artifacts/GrassDirt/after-play.png) |
| 근거리 표면, 아랫면과 절벽 | [잔디 근접](../Artifacts/GrassDirt/after-near.png) · [Normal 강도 0](../Artifacts/GrassDirt/normal-zero-near.png) · [아랫면 전경](../Artifacts/GrassDirt/after-underside.png) |
| 같은 거리의 흙 표면, 반경 8.4m / 12m / 24m | [0.7배](../Artifacts/GrassDirt/scale-0.7.png) · [1배](../Artifacts/GrassDirt/scale-1.0.png) · [2배](../Artifacts/GrassDirt/scale-2.0.png) |
| 축과 혼합 영역 | [−Y](../Artifacts/GrassDirt/axis-3.png) · [−X·−Y·−Z 혼합 근접](../Artifacts/GrassDirt/axis-7.png) |

같은 프레임 비교에서 카메라·조명·형상을 고정한 채 지면 텍스처 스위치만 바꿨다. 두 캡처 모두 별도 진단 팔레트를 사용하지 않았다. 기본 카메라에서 지표 무늬와 캐릭터 그림자가 보였다. 세 크기의 생성 지면 모두 로컬 스케일 1과 2m 반복 길이를 사용했다. 같은 거리의 흙 무늬 크기는 세 행성에서 비슷하게 보였다.

반경 12m 일반 행성을 ±X·±Y·±Z와 양·음 대각선에서 128×128 DepthNormals로 직접 렌더링했다. 여덟 구도에서 텍스처를 끈 출력과 두 Normal 강도를 0으로 둔 출력이 일치했다. 평탄 Normal을 연결했을 때 최대 벡터 차이는 0.000000146이었다. 적용 Normal과 기하 Normal의 최소 내적은 0.99935로 반전이 없었다. 시작 행성의 지면 메시와 MeshCollider 참조가 일치했다. `B612/Planet Ground`의 에디터 셰이더 진단은 0건이었다.

기존 챕터 입력 검사를 세 챕터에서 실행했다. 기본 우회로 보행, E 해금, 대쉬·더블점프·활공의 지름길과 안정 착지, 바오밥 생성과 양의 먹기, R 리스폰, 지도에서 11회 전환이 통과했다. R과 각 도착 시점에 공통 재질의 네 텍스처 참조와 2m 반복을 확인했다. 출발 행성의 소유 지면 재질과 지면·물 메시도 모두 해제됐다. [에디터 회귀 기록](../Artifacts/GrassDirt/EditorJourney.txt)

## Windows 개발 플레이어

1280×720 D3D12 플레이어에서 시작 행성과 지도 이동을 검증했다. 대쉬 해금 지점 상호작용과 개발자 페이즈 스킵을 거쳐 `system_01_C`에 도착했다. 시작 대표색 `#EADFC6`과 다음 행성 대표색 `#B8A6D8`을 사용했다. 다음 행성 크기는 1.109배, 행성 시드는 1909821952다. 도착과 리스폰 뒤 네 공유 텍스처 및 2m 반복 길이가 유지됐다. 빌드에 네 에셋과 저작자 고지가 포함됐고, DepthOnly에 16,384픽셀 중 12,024픽셀이 기록됐다. 실행 검사는 종료 코드 0으로 통과했다. [시작 행성](../Artifacts/GrassDirt/player-start.png) · [다음 행성](../Artifacts/GrassDirt/player-next.png) · [Windows 실행 기록](../Artifacts/GrassDirt/WindowsSmoke.txt)

Windows 빌드 결과는 `Succeeded`였다. 빌드 리포트에는 URP Lit·Terrain Billboard의 D3D11 셰이더 컴파일 오류 23건이 있었다. 같은 플레이어를 D3D11로 실행한 시작·지도 이동 검사도 종료 코드 0으로 통과했다. 오류가 발생한 개별 셰이더 변형의 동작은 검사하지 않았다.

## 성능

Windows 플레이어에서 Unity Profiler로 측정한 네 텍스처의 합계는 D3D12에서 22,546,176바이트(21.50 MiB), D3D11에서 22,371,520바이트(21.34 MiB)였다. 각 행성 재질이 네 에셋을 공유한다. Forward는 두 레이어에 각각 3방향의 Base Color와 Normal을 샘플링하며, DepthNormals는 Normal 샘플만 사용한다.

D3D12와 D3D11 개발 플레이어에서 `FrameTimingManager`를 켰지만 유효한 GPU 프레임 타임 표본이 반환되지 않았다. 에디터 D3D12의 RTX 3060 Ti에서 대체 측정으로 1280×720 화면을 같은 카메라로 렌더하고 1픽셀 읽기가 완료될 때까지의 시간을 측정했다. 끔/켬/켬/끔 순서로 상태별 60개를 측정했다. 중앙값은 **3.73ms → 4.76ms**, 90백분위수는 **4.02ms → 5.55ms**였다. 이 측정에는 CPU 렌더 제출과 GPU 읽기 대기가 포함된다.
