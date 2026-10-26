# 플레이어 애니메이션 설계 (재구축 2단계)

> 목표: 헬다이버즈식(몸이 카메라 방향에 고정된 3인칭 슈터) 애니메이션.
> 하체는 8방향 전투 이동, 상체는 '들고 다니기 ↔ 조준'을 따로 처리한다.
> 참고: Unreal Lyra(레이어 분리, 재생 속도 보정 15~20% 이내), Unity 슈터 에셋(상체 마스크 + additive 반동·피격).

## 0. 1단계 조사에서 확정된 전제

| 항목 | 내용 | 설계에 주는 영향 |
|---|---|---|
| 세트 구조 | 라이플·권총·산탄총·바주카·미니건 클립 이름 체계가 같음 | 구조 하나 + 무기별 Animator Override Controller |
| 이동 클립 | 조준 걷기·달리기 8방향, 조준 안 함은 앞쪽만 | 하체는 8방향 조준 이동 하나로 통일 |
| 클립 속도 | 제자리 클립이라 averageSpeed = 0 → `_RM`(루트모션) 판으로 측정 | 블렌드 좌표는 `_RM` 실측값으로 직접 지정 |
| 라이플 실측 | 걷기 0.77, 달리기 2.50, 전력질주 4.81 m/s | 이동 속도 기준 (아래 1장) |
| 루프 설정 오류 | 재장전·구르기·가벼운 피격·수류탄 던지기 등 1회성 클립이 루프 켜짐 | 빌더가 임포트 설정의 루프를 끈다 |
| 위·아래 조준 자세 | 없음 | 조준 방향은 Final IK 의 AimIK 로 처리 |
| IK 에셋 | 프로젝트에 Final IK(RootMotion) 전체 소스 있음 (`Assets/Plugins/RootMotion`) | 내장 휴머노이드 IK 대신 Final IK 사용 (4장) |
| 휴머노이드 마스크 | 'Body'에 골반까지 포함 → 상체 레이어가 하체를 끌고 감 (이전 문제의 주원인) | 상체 마스크에서 Body 제외 |

## 1. 이동 속도와 클립 맞추기

| 상태 | 게임 속도 | 사용 클립 | 재생 속도 |
|---|---|---|---|
| 조준 이동 | 3.0 × 0.7 = **2.1** | 조준 걷기(0.77)~달리기(2.50) 사이 블렌드 | 1.0 |
| 평소 이동 | **3.0** (현재 3.5) | 조준 달리기 2.50 | **1.2** (상한 1.25) |
| 전력질주 | 3.0 × 1.6 = **4.8** | 전력질주 4.81 | 1.0 |

- 3.5를 유지하면 평소 이동이 1.4배속이 되어 업계 관례(±20%)를 넘는다 → **3.0 권장**.
- MoveX / MoveY = 몸 기준 실제 속도(m/s). 달리기 클립보다 빠르면 좌표는 달리기에 두고 MoveSpeedScale로 재생 속도만 올린다.

## 2. 애니메이터 구조 (Player_AC)

### 파라미터

| 이름 | 종류 | 용도 |
|---|---|---|
| MoveX, MoveY | float | 몸 기준 이동 속도 (블렌드 좌표, m/s) |
| MoveSpeedScale | float | 이동 클립 재생 속도 (1~1.25) |
| Aim | float 0~1 | 들고 다니기 ↔ 조준 (0.15초에 걸쳐 변함) |
| Grounded, Falling, Sprint, Dead | bool | 지상 / 진짜 낙하 / 전력질주 / 사망 |
| Jump, Dodge, Draw, Reload, Throw, Heal, Fire, Hit, Die | trigger | 1회 동작 |
| DodgeX, DodgeY, HitX, HitY | float | 방향 (몸 기준) |
| DeathDir, LandType | int | 사망 방향 0~3 / 착지 세기 0~2 |
| ActionSpeed, DodgeSpeed | float | 재장전·구르기 길이를 실제 시간에 맞춤 |

### 레이어

| # | 레이어 | 마스크 | 방식 | 내용 |
|---|---|---|---|---|
| 0 | Base | 전신 | Override | Locomotion(8방향 2D) · Sprint · 점프(시작→낙하→착지) · 구르기(4방향) · 사망(4방향 → 쓰러짐 루프) |
| 1 | Upper Pose | 팔·손가락·머리 (**Body 제외**) | Override | 1D[Aim]: IdleHold ↔ IdleAim |
| 2 | Upper Action | 위와 같음 | Override | 꺼내기 · 재장전 · 수류탄 · 회복(임시: 수류탄 조준 자세) |
| 3 | Additive | 상체 + Body | Additive | 가벼운 피격(GetHit Light 4방향) |

- **피격은 additive**: 맞은 방향으로 움찔하는 변화량만 더한다 → 이동이 멈추지 않는다.
- **사격 반동은 애니메이션 클립 대신 Final IK `Recoil`** (4장) → 걷기·점프 중에도 자세가 끊기지 않고, 무기마다 세기 조절.
- 전이 시간: 이동 계열 0.15초, 1회 동작 진입 0.1~0.15초.

## 3. 무기별 클립 교체

- Player_AC는 라이플 클립으로 만든다.
- 무기 세트마다 Animator Override Controller를 만든다 (클립 이름의 `AssaultRifle` → `2HandedGun`/`Shotgun`/`Bazooka`/`Minigun` 치환).
- 총 → 세트: 드라이버 인스펙터의 목록 (총 클래스 이름 → Override Controller, 없으면 라이플).
- 처음 구현은 라이플만. 다른 무기는 7단계에서 추가.

## 4. IK — Final IK 사용

내장 휴머노이드 IK(LookAt)보다 Final IK 가 낫다고 판단했다.

| 비교 | 내장 LookAt IK | Final IK |
|---|---|---|
| 조준 | 머리·가슴을 목표 쪽으로 돌릴 뿐 → **총구**가 목표를 향한다는 보장 없음 (조준 자세의 비틀린 가슴을 펴 버릴 위험) | **AimIK**: 총구 방향(Aim Transform)이 목표를 향하도록 척추 사슬을 돌린다 → 슈터용으로 만들어진 기능 |
| 왼손 | 손 IK 따로 계산 | **FullBodyBipedIK**: 조준 후 왼손을 총 손잡이에 붙임 (데모 `SecondHandOnGun` 방식) |
| 반동·피격 | 클립에 의존 | **Recoil**, **HitReaction** (절차적) |
| 경사면 발 | 직접 구현 | **GrounderFBBIK** |
| 실행 순서 | 애니메이션 안 | 이미 정해져 있음: AimIK 9997 → FullBodyBipedIK 9999 (일반 스크립트 LateUpdate 이후). PlayerController 의 몸 방향 맞추기(LateUpdate, 순서 0)가 먼저 끝난 자세에 IK 가 얹힌다 |

몸(Body) 오브젝트 구성:

| 컴포넌트 | 단계 | 설정 |
|---|---|---|
| AimIK | 3 | 뼈: spine_01 → spine_02 → spine_03 → upperarm_r(가중치 작게). Aim Transform = 총구 방향을 나타내는 빈 오브젝트 (오른손 소켓 아래). 목표 = 카메라 정면 조준점 |
| FullBodyBipedIK | 4·6 | Recoil, 왼손 손잡이 고정, Grounder 의 바탕 |
| Recoil | 4 | 무기별 반동 세기 |
| GrounderFBBIK | 6 | 경사면 발 맞춤 |

`PlayerAimIK` (우리 스크립트, Body 에 붙음): AimIK 목표 위치를 카메라 조준점으로 옮기고, 가중치를 Aim 값에 맞춘다 (들고 다닐 때 0, 조준할 때 1). 구르기·사망·벽넘기 중에는 0.
머리카락 물리 뼈(순서 0)는 IK 전에 계산되므로 머리가 IK 로 움직이면 한 프레임 늦을 수 있다 → 보이면 물리 뼈 순서를 IK 뒤로 옮긴다.

## 5. 코드 구조

| 파일 | 위치 | 역할 |
|---|---|---|
| `PlayerAnimationDriver` | Player (루트) | PlayerController·GunSlot·PlayerItems 의 공개 상태를 **읽기만** 해서 파라미터·트리거 설정, 무기 세트 교체 |
| `PlayerAimIK` | Body (Animator 오브젝트) | Final IK AimIK 목표·가중치 조정, 6단계에서 왼손(FullBodyBipedIK) |
| `PlayerAnimationDebugHUD` | Player | F3: 레이어별 현재 상태 이름·주요 파라미터 표시 |
| `PlayerAnimatorBuilder` (에디터) | Scripts/Player/Editor | 메뉴 한 번으로 Player_AC + Override Controller 생성, 블렌드 좌표 실측, 루프 설정 수정 |

PlayerController 쪽 추가: `Died` 이벤트 (사망 순간 알림). 나머지는 기존 공개 API(Velocity, IsGrounded, IsSliding, GroundDistance, LookPitch, IsSprinting, Jumped/Landed/RollStarted)로 충분.

## 6. 구현 순서 (3단계)

1. 대기 · 8방향 걷기/달리기 · 전력질주
2. 점프 · 낙하 · 착지
3. 들고 다니기 ↔ 조준 + AimIK (총구가 조준점을 향함)
4. 사격 반동(Final IK Recoil) · 재장전 · 꺼내기
5. 구르기 · 피격(additive) · 사망
6. 왼손 IK(FullBodyBipedIK) · 총 위치 보정 · 발 IK(GrounderFBBIK)
7. 다른 무기 세트

각 단계마다 플레이 영상 + 디버그 HUD로 확인 후 다음 단계.
