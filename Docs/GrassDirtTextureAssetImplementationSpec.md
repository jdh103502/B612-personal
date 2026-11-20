# 잔디·흙 텍스처 에셋 적용 구현 명세

작성일: 2026-11-20. 상태: **구현 워커용 명세이며, 이 문서 작성 단계에서는 다운로드·임포트·코드 적용을 하지 않았다.**

작업 대상은 `C:\Dev\B612`의 현재 개발 프로젝트다. 작성 시 확인한 기준은 `main`, `2a82a13`이며, 실제 작업 시에는 최신 코드와 작업 중인 변경을 먼저 확인한다. 별도 초기화 사본인 `C:\Dev\B612-clean`에는 적용하지 않는다.

## 1. 목표와 적용 범위

사용자가 선택한 JulioVII의 **Stylized Grass & Dirt** 에셋을 내려받아, 현재 절차적으로 생성되는 구형 행성의 지면에 적용한다. 실제 플레이 카메라에서 잔디와 흙의 무늬·작은 표면 요철이 읽히면서, 행성별 대표색과 기존의 동화적인 색감을 유지하는 것이 목표다.

- 이번 필수 구현: 무료 텍스처 다운로드, 출처 기록, 흙·잔디 각 1종의 Base Color와 Normal 임포트, 기존 지면 셰이더 확장, 생성용 재질 연결, 실제 게임에서 확인.
- 최초 선택은 `Stylized_HandpaintedDirt_01`, `Stylized_HandpaintedGrass_01`이다. Grass 02는 비교 후보이며 최초 구현에서 무작위로 섞지 않는다.
- 암석 영역은 기존 색 혼합을 유지한다. 이번에 다른 암석 에셋이나 새로운 바이옴까지 추가하지 않는다.
- Roughness·Height는 이번 필수 적용에서 제외한다. Smoothness는 기존 0.1을 출발점으로 사용한다. Height로 정점·Collider를 변형하거나 테셀레이션·패럴랙스를 추가하지 않는다.
- 지형 형상, 타일 개수, 이동 능력, 바오밥 생성, 지도·챕터 진행은 이번 변경의 대상이 아니다. 신규 잔디 메시·파티클·효과음도 추가하지 않는다.

이 문서는 텍스처 적용을 위한 추가 명세다. [기본 기획](GameDesign.md)과 [기존 지형 외관 구현 결과](TerrainAppearanceImplementationResults.md)를 함께 참고하되, 과거 명세의 미구현 표시만 보고 이미 존재하는 지형을 다시 구현하지 않는다.

## 2. 사용할 에셋과 다운로드

공식 출처: [Stylized Grass & Dirt — JulioVII](https://juliovii.itch.io/ftpgrass-dirt).

2026-11-20에 확인한 판매 페이지는 9종의 4K 재질, Base Color·Roughness·Normal·Height 맵을 안내한다. 무료 다운로드 파일은 `Stylized_GrassAndDirt.zip`이며, Substance 소스가 포함된 별도 유료 파일은 이번 작업에 필요하지 않다. 이 내용은 **페이지 안내 기준**이다. ZIP 내부의 실제 폴더·파일명, 이미지 형식, 노멀 방향 규약과 타일 반복 품질은 워커가 다운로드 후 확인한다.

다운로드 순서:

1. 공식 페이지에서 Download Now를 선택한다.
2. [다운로드 안내 화면](https://juliovii.itch.io/ftpgrass-dirt/purchase)의 무료 다운로드 항목인 `No thanks, just take me to the downloads`로 이동한다. 이번 작업에 결제는 필요하지 않다.
3. `Stylized_GrassAndDirt.zip`을 프로젝트의 `Assets` 밖에 내려받고 압축을 푼다.
4. 흙 01과 잔디 01의 Base Color·Normal을 식별하고, 원본을 열어 무늬와 반복 경계를 확인한다. 파일명 추정만으로 맵을 배정하지 않는다.
5. 실제 사용 이미지 4개와 제공된 라이선스·출처 문서를 프로젝트로 가져온다. Blender 파일, Substance 파일, 미사용 재질 전체와 ZIP을 `Assets`에 통째로 넣지 않는다.

### 출처와 라이선스 기록

공식 페이지에는 **CC-BY**로 표기되어 있으며, 상업적 사용 허용과 재판매 제외 조건을 별도로 안내한다. 장래 CC0 전환 의향도 적혀 있으나, 현재 에셋을 CC0로 간주하지 않는다. 페이지에 CC-BY 버전이 명시되지 않았으므로 임의로 CC-BY 4.0이라고 적지 않는다. [제작자의 사용 조건](https://juliovii.itch.io/ftpgrass-dirt)

워커는 동봉된 라이선스가 있으면 그대로 보존하고, 사용 파일·제작자·원본 링크·다운로드일·적용한 변경을 기록한다. 페이지와 동봉 조건이 다르면 차이를 보고한다. 배포물에도 출처 고지가 포함되도록 기존 크레딧 또는 동봉 고지 방식에 연결한다. 별도의 크레딧 화면 신설은 이번 필수 범위에 포함하지 않는다.

출처 기록 예시: `Stylized Grass & Dirt / JulioVII / https://juliovii.itch.io/ftpgrass-dirt / CC-BY (제공된 원문의 버전 표기 확인) / B612용 임포트 및 셰이더 혼합 설정 적용`.

## 3. 현재 구현에서 연결할 지점

| 파일·설정 | 현재 역할과 작업 방향 |
| --- | --- |
| `Assets/B612/Shaders/PlanetGround.shader` | `B612/Planet Ground`. 기존 셰이더를 확장해 흙·잔디 텍스처를 샘플링한다. |
| `Assets/B612/Scripts/PlanetWorldInstance.cs` | `Prepare`에서 생성 설정의 지면 재질을 복제하고 행성 대표색·반경·지역 팔레트를 적용한다. 이 생명주기를 유지한다. |
| `Assets/B612/Scripts/PlanetGenerationSettings.cs` | `proceduralGroundMaterial`이 재질 템플릿 진입점이다. 기존 필드를 사용한다. |
| `Assets/B612/Settings/PlanetTravel/StylizedPlanetGround.mat` | 세 생성 설정이 현재 참조하는 공통 지면 재질. 새 텍스처와 조절값을 여기에 연결한다. |
| `Assets/B612/Scripts/ProceduralPlanetTerrain.cs` | 지형 메시와 정점 색 가중치 생성. 이번 텍스처 적용을 위해 형상이나 가중치 생성 규칙을 바꾸지 않는다. |

현재 `_Paper`는 로컬 좌표에서 세 방향으로 투영한 텍스처의 R 채널을 약한 명암으로만 사용한다. Base Color 전체와 Normal을 처리하는 슬롯은 없다. 따라서 임포트한 맵을 기존 `_Paper`에 넣거나 일반 URP/Lit 재질을 씬 Renderer에 끼우는 것만으로는 이번 구현이 완료되지 않는다.

런타임에서 `Prepare`가 재질을 다시 연결하므로 **생성 설정 → 공통 재질 템플릿 → 행성별 복제 재질** 경로로 적용한다. 행성이 소유한 복제 재질은 기존처럼 폐기 시 해제하고, 텍스처 에셋은 공유 참조한다.

## 4. 에셋 배치와 임포트 설정

권장 폴더:

```text
Assets/B612/Art/ThirdParty/JulioVII/GrassDirt/
  Textures/       실제 사용하는 흙·잔디 이미지 4개
  License/        원문 라이선스와 출처·수정 기록
```

원본 이미지의 구분 가능한 파일명을 보존한다. 별도로 만든 출처 기록에는 실제 파일명과 각 맵의 용도를 대응시킨다. 아래 값은 최초 임포트 설정이며 최종 선택은 플레이 화면과 메모리 확인 후 기록한다.

| 항목 | Base Color | Normal |
| --- | --- | --- |
| Texture Type | Default | Normal map |
| 색 처리 | sRGB 활성 | 노멀 데이터로 임포트, 색 텍스처로 취급하지 않음 |
| Wrap Mode | Repeat | Repeat |
| Mip Maps | 활성 | 활성 |
| Filter Mode | Trilinear | Trilinear |
| Aniso Level | 4에서 시작 | 4에서 시작 |
| Max Size | 2048에서 시작 | 2048에서 시작 |
| Read/Write | 비활성 | 비활성 |

원본 해상도는 유지하고 Unity 임포트 상한으로 런타임 크기를 조절한다. 우선 Windows 대상의 적절한 압축을 사용하며, 실제 지원 형식과 임포트 후 메모리를 기록한다. 기존 `.gitattributes`의 이미지 LFS 규칙을 따르고 Unity가 만든 `.meta`도 포함한다.

Normal이 OpenGL/DirectX 중 어느 규약인지는 아카이브를 확인한다. Unity에서 홈이 튀어나오는 것처럼 보일 때는 방향 규약과 축 변환부터 점검한다. **Flip Green Channel을 이유 없이 켜지 않는다.** [Unity Normal Map 임포트 문서](https://docs.unity3d.com/6000.0/Documentation/Manual/texture-type-normal-map.html)

## 5. 셰이더 확장

### 5.1 재질에서 조절할 입력

아래 이름은 새 입력의 제안이다. 기존 프로퍼티 이름과의 호환성을 유지하는 범위에서 정리할 수 있다.

| 입력 | 용도 | 시작값 |
| --- | --- | --- |
| `_GroundTexturesEnabled` | 새 Base Color·Normal 경로 사용. 0이면 기존 지면 표현으로 복귀 | 셰이더 기본 0, 적용 재질 1 |
| `_DirtAlbedo`, `_GrassAlbedo` | 선택한 두 Base Color | 다운로드한 이미지 |
| `_DirtNormal`, `_GrassNormal` | 각 Base Color와 짝이 맞는 Normal | 다운로드한 이미지 |
| `_DirtTileMeters`, `_GrassTileMeters` | 한 번 반복되는 투영 평면상의 길이 | 각각 2m |
| `_GroundTextureStrength` | 기존 팔레트에 섞는 텍스처 색·명암 강도 | 0.5 |
| `_DirtNormalStrength`, `_GrassNormalStrength` | 미세 요철 강도 | 각각 0.2 |

2m와 강도 값은 검증 전 출발값이다. 흙 알갱이와 풀 무늬가 캐릭터에 비해 너무 크거나 자글거리면 반복 길이와 강도를 조절한다. 최초 선택값은 공통 재질에 저장하고, 이번에는 행성 데이터 필드나 F12용 UI를 추가하지 않는다.

### 5.2 구형 메시의 텍스처 좌표

기존 방식처럼 **행성 중심 기준 로컬 위치와 기하 노멀을 사용하는 triplanar**를 유지한다. 매끄러운 UV가 없어도 연속 메시와 절벽에서 텍스처를 적용할 수 있다. 세 방향 샘플을 표면 방향으로 혼합하는 방식은 [Unity Triplanar 문서](https://docs.unity.cn/Packages/com.unity.shadergraph@17.0/manual/Triplanar-Node.html)를 참고한다.

- 투영 위치는 `positionOS / TileMeters`로 계산한다. 현재 절차적 지면은 로컬 스케일 1로 생성된다. 반경으로 위치를 나누거나 정규화하지 않는다.
- 같은 2m 설정이면 0.7배·1배·2배 행성에서도 무늬의 대략적인 실제 크기가 같아야 한다. 큰 행성에서는 반복 횟수가 늘어난다.
- 기존 기하 노멀 기반의 세 축 가중치를 재사용한다. 타일 ID별 독립 UV나 매 프레임 무작위 오프셋은 추가하지 않는다.
- Base Color와 짝 Normal은 같은 좌표·반복 길이·축 방향을 사용한다. 행성이 이동·회전해도 무늬가 표면에 붙어 있어야 한다.

### 5.3 기존 팔레트·지역과 혼합

1. 기존의 높이·경사·정점 색으로 흙색 `earth`, 식생색 `grass`, 암석색 `stone`, 식생 가중치 `meadow`, 암석 가중치 `rock`을 구한다.
2. 흙·잔디 Base Color를 RGB로 triplanar 샘플링한다. 기존 `_Paper`의 단일 채널 경로를 그대로 대체하는 것으로 끝내지 않는다.
3. 각 샘플을 기존 흙·식생 팔레트에 텍스처 디테일로 혼합한다. 강도 0에서는 기존 색으로 정확히 돌아가게 한다. 모든 행성이 원본 이미지의 갈색·초록색으로 고정되거나 지나치게 어두워지지 않도록 한다.
4. 권장 방식은 선택한 텍스처의 선형 공간 평균 RGB를 중립값으로 정규화한 뒤 팔레트에 곱하고 강도로 보간하는 것이다. 평균값은 에디터에서 한 번 계산해 재질에 저장한다. 런타임 CPU에서 이미지 픽셀을 읽지 않는다. 극단적인 밝기 비율은 제한하고 실제 사용한 보정값을 기록한다.
5. 텍스처가 적용된 흙·잔디를 기존 `meadow`로 혼합한 뒤, 기존 `rock`으로 암석색과 혼합한다. 암석 영역에는 잔디 무늬와 잔디 Normal이 남지 않도록 한다. 기존 강가의 식생 억제와 고도 명암도 유지한다.

`_BaseColor`, `_EarthColor`, `_VegetationColor`, `_RockColor` 등은 현재 `PlanetWorldInstance.Prepare`가 행성별로 설정한다. 재질 템플릿에서만 이 값을 바꾸고 최종 적용됐다고 판단하지 않는다. 새 텍스처·반복 길이·강도는 템플릿에서 복제되도록 둔다.

기존 종이 질감은 새 디테일과 겹쳐 지저분해지지 않도록 약하게 유지하거나 적용 재질에서 대비를 낮춘다. 새 색·Normal에 기존 `_Paper`의 5~16m 거리 감쇠를 일괄 적용하지 않는다. 최초에는 mipmap으로 축소를 처리하고, 추가 거리 감쇠가 필요하면 실제 플레이 거리에서 무늬가 사라지지 않는 범위를 검증한다.

### 5.4 Normal 처리와 렌더 패스

- Unity의 Normal 디코딩 함수를 사용한다. 세 방향의 노멀 맵 RGB를 색처럼 평균 내거나, 디코딩한 tangent-space 노멀을 곧바로 월드 노멀로 사용하지 않는다.
- 각 투영축의 방향과 양·음의 면 방향을 고려해 같은 공간으로 재지향한 뒤 혼합·정규화한다. Unity Triplanar의 Normal 예제를 참고하되 현재 셰이더의 로컬 좌표 투영에 맞춘다.
- 평탄 Normal 입력 및 강도 0에서 **원래 메시 노멀과 같은 결과**가 나와야 한다. 행성의 아랫면이나 축 경계에서 조명 방향이 뒤집히면 실패다.
- 흙·잔디 노멀은 동일 공간에서 기존 표면 가중치로 혼합하고, 암석 영역에서는 기하 노멀로 돌아간다. 최종 월드 노멀을 URP 조명 입력으로 전달한다.
- 기존 Forward·ShadowCaster·DepthOnly·DepthNormals 패스를 유지한다. Forward와 DepthNormals의 노멀 계산을 공유해 깊이 노멀 기반 효과와 어긋나지 않도록 한다. ShadowCaster와 DepthOnly는 기존 형상을 사용한다.
- 텍스처가 없거나 기능이 꺼진 기존 재질도 정상 렌더링되어야 한다. 기존 진단 옵션의 동작은 보존하고, 화면 비교 때 어떤 옵션을 사용했는지 기록한다.

## 6. 실제 생성 경로에 연결

`StylizedPlanetGround.mat`에 선택한 맵과 새 조절값을 저장하고 다음 세 설정의 `proceduralGroundMaterial` 참조를 확인한다. 현재 모두 같은 재질을 가리키므로 불필요하게 세 재질을 복제하지 않는다.

- `Assets/B612/Settings/Chapters/ChapterJourneyGeneration.asset`
- `Assets/B612/Settings/PlanetTravel/EmptyPlanetGeneration.asset`
- `Assets/B612/Settings/PlanetTravel/ProceduralTerrainTestGeneration.asset`

주 확인 경로는 실제 게임 시작 씬인 `Assets/B612/Scenes/B612_ChapterJourney.unity`다. `B612_EmptyPlanet` 또는 테스트 씬 한 곳에만 수동 적용한 상태는 완료로 보지 않는다. 새 행성에 도착해 `Prepare`가 다시 실행돼도 같은 텍스처 설정을 사용해야 한다.

셰이더·재질 변경만으로 해결되면 C# 생성기나 저장 데이터 형식을 변경하지 않는다. C#을 수정할 필요가 생기면 `Assets/B612/Scripts`와 대응하는 `AgentScripts/runtime` 사본을 함께 갱신한다. 텍스처는 행성마다 복제하거나 런타임 생성하지 않는다.

## 7. 구현 순서

1. 현재 씬·생성 설정과 기존 작업을 확인하고, 같은 seed·카메라의 적용 전 화면을 저장한다.
2. 무료 ZIP을 다운로드해 실제 구성과 사용 조건을 기록하고 필요한 이미지 4개를 임포트한다.
3. 기존 표현으로 돌아갈 수 있는 스위치와 Base Color triplanar를 먼저 연결한다. 대표색·흙/잔디 경계·무늬 크기를 확인한다.
4. Normal triplanar를 연결하고 축 경계·아랫면·강도 0을 확인한다. 같은 계산을 DepthNormals에도 적용한다.
5. 공통 재질에 설정을 저장하고, 실제 게임 시작·행성 이동·다른 크기에서 유지되는지 확인한다.
6. 동일 조건의 전후 화면, 최종 파라미터, 사용 파일·라이선스, 성능과 회귀 확인 결과를 별도 결과 문서에 기록한다.

검증만을 위한 새 맵·이동 기능이나 전체 자동 테스트 체계를 추가할 필요는 없다. 기존 검증 수단으로 변경 범위에 맞게 확인한다.

## 8. 완료 조건

| 확인 항목 | 통과 기준 |
| --- | --- |
| 에셋 | 지정한 무료 팩의 실제 흙·잔디 맵을 사용하고 파일별 출처·라이선스와 `.meta`가 남아 있다. |
| 실제 플레이 | `B612_ChapterJourney`의 기본 카메라에서 기존 단색 지면과 비교해 흙·잔디 무늬가 읽힌다. 근접 확대에서만 보이는 결과로 끝내지 않는다. |
| 행성색 | 서로 다른 대표색의 행성 최소 2개가 원래 색 차이를 유지한다. 과포화·과도한 암부 뭉침이 없다. |
| 크기 | 0.7·1·2배 지면을 같은 관찰 거리에서 비교해 무늬의 실제 크기가 비슷하다. 큰 행성에서 무늬까지 커지지 않는다. |
| 구면·경사 | ±X/±Y/±Z와 축 혼합 영역, 평지·언덕·절벽에서 찢어짐·심한 늘어짐·노멀 반전이 없다. 2×2 반복에서도 이미지 경계가 거슬리지 않는다. |
| 렌더링 | 셰이더 오류·분홍 재질이 없고 캐릭터 그림자와 DepthNormals가 정상이다. 텍스처 비활성 및 Normal 강도 0에서 기존 동작으로 복귀한다. |
| 재생성 | 새 행성 이동과 R 리스폰 뒤에도 텍스처·반복 크기가 유지된다. 공유 원본 재질이 런타임에 오염되지 않는다. |
| 게임 회귀 | 기존 지형 seed·메시·Collider를 변경하지 않았으며, 보행·점프·능력 사용, 바오밥/양 상호작용과 지도 전환이 정상이다. |
| 비용 | 같은 해상도·구도에서 변경 전후 GPU 프레임 시간과 임포트 후 텍스처 메모리를 기록한다. 큰 변화가 있으면 해상도·샘플 경로·Normal 설정을 재검토한다. |
| 빌드 | Windows 개발 빌드에서도 텍스처와 셰이더가 포함되고 게임 시작·다음 행성 이동을 확인한다. 에디터 검증과 빌드 검증 범위를 구분해 기록한다. |

색 2종과 Normal 2종을 각각 세 방향으로 샘플링하면 기본적으로 12회 텍스처 샘플이 추가된다. 여기에 기존 종이 샘플 비용이 있다. 첫 구현에서는 두 레이어로 제한하고, 미사용 팩 전체의 맵이나 Roughness까지 추가 샘플링하지 않는다. 품질 문제가 없는 상태에서 필요할 때만 최적화한다.

결과 기록의 권장 위치는 `Docs/GrassDirtTextureAssetImplementationResults.md`다. 같은 seed와 고정 카메라의 실제 플레이 전후, 흙·잔디 근접, 행성 뒷면/아랫면, 서로 다른 크기·대표색 비교를 남긴다. 수행하지 못한 확인은 통과로 적지 않는다.

## 9. 구현 워커에게 전달할 프롬프트

> `C:\Dev\B612\Docs\GrassDirtTextureAssetImplementationSpec.md`를 기준으로 구현해줘. JulioVII의 지정된 무료 Grass & Dirt 팩을 직접 다운로드하고, 흙 01·잔디 01의 Base Color와 Normal을 기존 `B612/Planet Ground` 셰이더 및 공통 생성 재질에 연결해줘. 행성별 색과 미터 기준 반복 크기를 유지하고, 실제 `B612_ChapterJourney` 시작·행성 이동 및 Windows 빌드에서 확인해줘. 지형 형상과 게임 규칙은 유지하고, 사용 조건·전후 화면·최종 설정·검증 결과를 결과 문서에 기록해줘.
