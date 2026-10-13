# B612

『어린왕자』와 『슈퍼마리오 갤럭시』를 축으로 한, 우주 배경의 카툰풍 3D 플랫포머 컨셉 데모입니다.
작은 행성마다 중력이 있어서 행성을 한 바퀴 걸어서 돌 수 있고, 약한 중력 속에서 점프하며 행성 사이를 건넙니다.

![밤 전경](Assets/Captures/B612_Night.png)

## 시작하기

1. **Git LFS 설치** — 모델과 텍스처가 Git LFS로 올라가 있습니다. <https://git-lfs.com> 에서 설치한 뒤 한 번만 실행하세요.
   ```
   git lfs install
   ```
2. **받기**
   ```
   git clone https://github.com/hpc99jtzqd-dot/B612.git
   ```
3. **Unity Hub에서 열기** — 반드시 같은 버전 **Unity 6000.3.23f1** (Unity 6.3, URP)로 엽니다.
   처음 열 때는 Library 폴더를 새로 만드느라 몇 분 걸립니다.
4. `Assets/B612/Scenes/B612_Night.unity` 를 열고 ▶ Play.

| 씬 | 내용 |
| --- | --- |
| `B612_Night` | 밤하늘 버전 (기본) |
| `B612_Diorama` | 낮 버전 |
| `B612_Turnaround` | 왕자·여우 3면도 |

## 조작

| 입력 | 동작 |
| --- | --- |
| WASD / 방향키 | 이동 |
| Space | 점프 (길게 누르면 더 높이) |
| 마우스 드래그 · Q / E | 카메라 회전 |
| 마우스 휠 | 카메라 줌 |
| R | 처음 자리로 |
| H | 도움말 숨기기 / 보이기 |

## 데모에 있는 것

- 메인 행성 B-612: 집(안에 들어가면 벽이 반투명해짐), 화산 3개, 장미, 바오밥 새싹
- 반대편의 바오밥 숲 (등불, 빛나는 버섯, 숨은 별)
- 징검돌을 건너 가는 시계 별, 돌아올 때는 발사 별(Space)
- 별 6개 모으기
- 왕자를 따라다니는 여우 (멈추면 앉아서 왕자를 바라봄)
- 절차적 밤하늘 (은하수, 별, 초승달, 별똥별)

## 폴더 구조

```
Assets/B612/
  Scenes/      씬
  Scripts/     게임 코드 (중력, 이동, 카메라, 애니메이션, 집 내부, 발사 별 …)
  Shaders/     밤하늘, 반딧불 셰이더
  Models/      왕자(Meshy), 여우(Blender) 모델
  Generated/   빌더가 만든 메시·재질·프리팹 (직접 수정하지 말 것)
AgentScripts/  씬과 모델을 생성하는 스크립트
  B612Builder.cs           씬 전체를 다시 만드는 빌더 (Unity CLI로 실행)
  blender/                 캐릭터 모델 생성 스크립트 (Blender 5.2)
  runtime/                 Assets/B612/Scripts 원본
```

> 씬과 `Generated/` 는 `AgentScripts/B612Builder.cs` 가 처음부터 다시 만듭니다.
> 씬을 손으로 수정한 뒤 빌더를 다시 실행하면 수정 내용이 덮어써지니, 이 점을 맞춰서 작업해 주세요.

## 크레딧

- 왕자 3D 모델: Meshy AI로 생성
- 그 외 모델·코드: Unity, Blender 스크립트로 제작 (Claude 활용)
