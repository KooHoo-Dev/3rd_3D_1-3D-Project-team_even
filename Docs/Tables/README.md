# 데이터 테이블 명세

위치: `Assets/StreamingAssets/Tables/` · 형식: TSV (UTF-8, 탭) · 코드: `Assets/Scripts/Core/Data/`

## 공통 규칙
- 1행 = 헤더. **헤더 이름 = DTO 필드 이름** (대소문자까지 동일)
- `#` 로 시작하는 행 = 주석, 빈 행 무시
- enum 값은 대소문자 무시, 목록 값은 `;` 구분, 참조 `0` = 없음
- 원본은 Google Sheets 한 파일(탭 = 테이블) → `파일 > 다운로드 > .tsv` → `XxxTable.tsv` 로 덮어쓰기
- 수정 후 `Tools > Data > Validate Tables` → 오류 0 확인 → 커밋 (`chore: TuneCrowdSpawn (BL-015)`)

## Id 대역

| 대역 | 테이블 |
|---|---|
| 1000~1999 | PlayerTable |
| 2000~2099 | WeaponTable (라이플) |
| 2100~2199 | GrenadeTable |
| 1001~1999 (몬스터) | MonsterTable 일반체 (1001~) |
| 2001~2999 (몬스터) | MonsterTable 중간체 (2001~) |
| 3001~ (몬스터) | MonsterTable 보스 |
| MonsterId×100+순번 | MonsterAttackTable (예: 보스 300104) |
| 1~ (순번) | MonsterPartTable, BossPhaseTable, StatusEffectTable, MonsterGradeTable (GradeId) |

> 주의: 몬스터 Id(1001~)와 PlayerTable(1000~1999) Id 대역이 숫자상 겹치지만 서로 다른 테이블이라 충돌하지 않는다.
| 4000~4999 | SupplyTable |
| 5100~5999 | CrowdSpawnTable |
| 6000~6999 | UpgradeTable (60x1 = 종류별 10단위) |
| 라운드 번호 | RoundTable (Key = Round) |
| 문자열 Key | SoundTable, ConfigTable |

## PlayerTable — BL-001·002·005·006
| 컬럼 | 타입 | 설명 |
|---|---|---|
| MaxHp | int | |
| MoveSpeed | float | m/s |
| LookSensitivity | float | 마우스 감도 |
| PitchMin / PitchMax | float | 상하 시야 제한(도) |
| RifleId / GrenadeId | int | WeaponTable / GrenadeTable |
| StartGrenades / MaxGrenades | int | 시작 수 / 강화 전 최대 보유 수 |
| StartLives / MaxLives | int | 시작 목숨(지금 목숨 포함) / 기본 최대 목숨. 난이도(`IPlayerLives.SetMaxLives`)·스테이지 클리어(`AddMaxLives`)가 바꿀 수 있다 |

## WeaponTable — BL-003·004
| 컬럼 | 타입 | 설명 |
|---|---|---|
| FireMode | enum | `Single` / `Auto` |
| Damage | int | 1발 피해 (일반 몬스터가 1~2발에 죽는 수준) |
| FireRate | float | RPM. 코드에서는 `FireInterval` |
| MagazineSize | int | 강화 전 탄창 (탄창 하나에 든 탄약 수) |
| StartMagazines / MaxMagazines | int | 시작 / 최대 예비 탄창 개수 (탄창 단위, MagazineSize 와 별개). 장전이 끝날 때 1개를 써서 탄창을 가득 채운다 (취소 시 소모 없음). 다 쓰면 재장전 불가. 보충은 `IPlayerAmmo.AddMagazines` |
| SupplyMagazineRatio | float | 보급 1회 예비 탄창 충전 비율. MaxMagazines × 비율을 내림, 최소 1 (0 = 충전 안 함). 일반 총 0.5, 스플래시(로켓·유탄) 0.25 |
| ReloadTime | float | 강화 전 재장전(초) |
| Range / SpreadAngle | float | 사거리(m) / 퍼짐 반각(도) |
| FireSfxKey, ReloadSfxKey, ReloadInsertSfxKey, ReloadEndSfxKey | string | 장전음: 시작 · 탄창·탄 넣기(기본 총 = 장전 55% 시점, 산탄총 = 한 발 시간마다 소리만) · 완료(취소 시 없음) |
| Category | enum | `Sidearm`(1번 칸 고정 권총, 정확히 1행) / `Loadout`(출발 전 2·3번 칸 선택) / `Supply`(4번 보급 칸 전용, 1회용 — 보급 상자로 탄 충전 안 됨) |
| IconKey | string | `Resources/UI/WeaponIcons/{IconKey}` 스프라이트. 비우면 무기 슬롯 HUD 가 이름 텍스트로 대체 |
| UnlockOrder | int | 해금 순서. `0` = 처음부터 해금(권총·돌격소총) · `1~` = 스테이지(보스) 클리어마다 작은 수부터 하나씩 영구 해금 (PlayerPrefs `Unlock.Weapons`). Supply 는 `0` |

## GrenadeTable — BL-005
| 컬럼 | 타입 | 설명 |
|---|---|---|
| Damage / Radius | int / float | 폭발 피해 / 반경(m) |
| FuseTime | float | 투척 후 폭발까지(초) |
| ThrowSpeed | float | 투척 초기 속도 |

## 몬스터 테이블 묶음 — E3, E5 (Build1)
`MonsterTable` 한 행에 공격(`MonsterAttackTable`), 부위(`MonsterPartTable`), 보스 페이즈(`BossPhaseTable`)가 `MonsterId` 로 연결된다. 방어도는 없다: 피해 = 무기 Damage × 부위 DamageMultiplier.

### MonsterGradeTable (검증 기준)
| 컬럼 | 설명 |
|---|---|
| GradeId / Grade | PK / `Weak`(일반체) · `Mid`(중간체) · `Boss` |
| MinHitsToKill / MaxHitsToKill | 처치 타수 범위 = ceil(MaxHp / 라이플 Damage). Max -1 = 제한 없음 |
| AttackPatternCount | 기본 공격(PatternTag None) 개수. -1 = 보스 별도 |

규칙 위반은 로드 시 **경고**로 나온다.

### MonsterTable
| 컬럼 | 타입 | 설명 |
|---|---|---|
| Key / Name | string | 식별 키 / 한글명 |
| GradeId | int | MonsterGradeTable |
| Role | enum | `Melee` / `Air` / `Rusher` / `Infiltrator` / `ZonePressure` / `Boss` / `SelfDestruct` (Boss 는 Grade Boss 와 함께). SelfDestruct는 접근 후 예고·폭발하고 보상 없이 사망하며, 먼저 처치하면 정상 보상을 지급한다. 공격 Range는 접근 거리, Radius는 자기 중심 폭발 반경 |
| MaxHp | int | |
| MoveSpeed | float | 이동 속도 (m/s) |
| DetectRange | float | 탐지 거리(m), -1 = 전장 전체 |
| BodyScale / Height | float | 프리팹 스케일 / 신장(m) |
| FlyHeight | float | 비행 고도(m). Air 역할만 0 초과 (`IsFlying` = Role Air) |
| MeshVariant | enum | `Default` / `WingsOff` / `WingsOn` (무스코모프 육상·공중이 같은 프리팹 사용) |
| Reward | int | 처치 재화 (BL-029) |
| PrefabKey | string | `Resources/` 기준 경로 |
| IdleSfxKey / LoopSfxKey / DeathSfxKey | string | SoundTable 키 (빈칸 = 없음). 울음 / 이동·날갯짓 루프 / 사망 |
| StunSfxKey | string | 기절 시작 (돌진 벽 충돌). 보스 소리는 BossSoundEmitter (테이블 아님) |
| HiddenSfxKey | string | 기습형 신호. 있으면 플레이어 근처에서 울지 않고, 근처·시야 밖일 때 공격 전 1번 재생 (파르다트록스 = `mon_pardathrox_vanish`) |

### MonsterAttackTable
Id = MonsterId×100+순번. 일반 몬스터 1행, 보스 6행(기본 2 + 특수 4).
| 컬럼 | 설명 |
|---|---|
| PatternTag | `None`(기본 공격) / `Entrance`(등장 공격, 1회성) / `GroundSlam` / `Bombard` / `Unique` |
| Damage / HitCount | 1회 피해 / 타격 횟수 |
| Cooldown / TelegraphTime / RecoveryTime | 재사용 대기 / 예고(회피 시간) / 공격 후 경직(반격 시간) |
| Shape | `Melee`(Range) / `Circle`(Radius) / `Cone`(Range, Angle) / `Line`(Range, Width) / `Projectile`(ProjectileSpeed, ProjectileCount) |
| ProjectileSpeed / ProjectileCount | 투사체 속도 / 투사체·구역 개수 |
| KnockbackDistance / WallHitStunTime | 넉백(m) / 벽 충돌 시 자기 기절(초) |
| EffectId | StatusEffectTable (0 = 없음) |
| Trigger / TriggerValue | `None` / `PlayerStationary`(TriggerValue 초 동안 플레이어 정지 시 발동) |
| ClearsMinions | 적중 시 일반 몬스터 정리 (등장 공격) |
| AnimTrigger | Animator 트리거명 (임시 이름) |
| SfxKey | 공격 소리 (SoundTable 키). 예고 시작에 재생. 보스 Entrance = 등장 포효 |

### MonsterPartTable
| 컬럼 | 설명 |
|---|---|
| MonsterId / BossId | 부위 주인. 둘 중 하나만 0 보다 크다. MonsterId = MonsterTable Id (`MonsterWeakPoint`), BossId = BossTable Id (Clypeosaurus `BossWeakPoint`, Tyranopode `TyranopodePart`) |
| PartKey / Name | 몬스터(보스) 안에서 유일한 키 / 부위명 |
| DamageMultiplier | 명중 피해 배율 |
| Exposure | `Always` / `WhileAttacking`(공격 동작 중에만 배율, 아니면 몸통 피해) / `WhileRecovering`(아직 미구현) |
| PartHp | 0 = 파괴 불가 (`IsDestructible` = PartHp > 0) |
| LinkedAttackId | 파괴 시 약화되는 공격 (0 = 없음) |
| TransformToMonsterId | 파괴 시 전환될 몬스터 (0 = 없음, 남은 체력 유지) |

### BossPhaseTable
| 컬럼 | 설명 |
|---|---|
| Phase / HpThresholdPercent | 1부터 연속 / 체력 % 이하에서 진입 (1페이즈 = 100, 점점 작아짐) |
| PatternSequence | 반복 특수 공격 Id 목록 (`;` 구분). **순서대로 순환** (`BossPhaseDef.GetPattern(i)`). 가중치 랜덤 아님 |
| CooldownMultiplier | 쿨다운 배율 |
| ElectricSweep / VisualState | 안테나 전격 스윕 여부 / 외형 상태 키 |

코드: `MonsterDef.Attacks / BasicAttacks / EntranceAttack / Parts / Phases / GetPhase(hpRatio)`

### StatusEffectTable
플레이어 대상 상태 효과. `Type` = `DamageOverTime`, `Value` = 틱 피해, `IsZone` = 바닥 구역. 몬스터 자신의 기절은 `WallHitStunTime` 으로 처리한다.

## RoundTable — BL-015·018·021·032
| 컬럼 | 타입 | 설명 |
|---|---|---|
| Round | int | 1부터. 마지막 행 이후 라운드는 마지막 행 재사용 |
| BossId | int | BossTable.Id. 진행 라운드 번호의 행으로 보스를 고른다 (`RoundBossEncounter`). 없으면 인스펙터 보스로 대신 |
| BossSpawnTimeMin / Max | float | 보스 등장 시각 범위(초). 이 사이 무작위, 남은 시간 표시 안 함 |
| SupplyStartTime / SupplyInterval | float | 첫 보급(초) / 이후 간격 |
| BossReward | int | 보스 처치 추가 재화 |

## BossTable / TyranopodeTable — 보스
### BossTable
| 컬럼 | 타입 | 설명 |
|---|---|---|
| Id | int | 1 = Clypeosaurus, 2 = Tyranopode |
| MaxHp / MoveSpeed / PatternCooldown | int / float / float | 체력 / 이동 속도 / 패턴 사이 최소 간격 (Tyranopode 는 0 — 간격은 TyranopodeTable 이 정함) |
| Prefab | string | 보스 프리팹 Resources 경로 (확장자 없음). `RoundBossEncounter` 가 불러온다 |

### TyranopodeTable (단일 행, 두 번째 보스 전용)
열이 많아 묶음만 적는다. 각 열 설명은 tsv 주석과 `TyranopodeDef` 참고. 값은 모두 제안값
| 묶음 | 열 접두사 | 내용 |
|---|---|---|
| 페이즈·거리 | Phase2*, MeleeRange, FarRange, Circle*, Combo* | 페이즈 2 조건(체력 비율 또는 부위 파괴 수, 먼저 오는 쪽), 거리 구간, 공격 사이 맴돌기, 연속 공격 확률 |
| 부위 | *Hp, *DamageMultiplier, *Broken* | 부위 체력·피해 배율, 파괴 시 약해지는 정도 |
| 그로기 | Groggy*, WallGroggyTime | 포자 파괴·벽 충돌 그로기 시간, 그로기 중 받는 피해 배율 |
| 패턴 | Tentacle*, Bite*, Smash*, Charge*, Spit*, Egg*, Tremor*, Summon*, Beacon*, Storm* | 피해·범위·예고(Windup)·경직(Recovery)·쿨타임 |
| 등장 연출 | Intro* | 남아 있던 웨이브 몬스터를 흡수한 수만큼 보호막을 두르고 시작 (상한 MaxHp×IntroShieldMaxRatio), 두 번째 등장부터 짧은 연출 |
| 난이도 보정 | Assist* | 플레이어가 힘들수록 공격 간격↑, 연속·강공격↓ (예고·피해량은 그대로) |

`SummonMinionIds` 는 MonsterTable.Id 를 `|` 로 구분한다 (예: `1001|1002`).

거리·범위 값은 보스 크기 1.6배(프리팹 빌더 `ModelScale`) 기준 실제 값이다. 크기를 바꾸면 몸이 닿는 범위(Melee·Tentacle·Bite·Smash·ChargeHit·TremorNear·JumpBite*)는 같은 배율로, 맴돌기·도약 거리는 조금 덜 바꾼다.

## CrowdSpawnTable — BL-015
| 컬럼 | 타입 | 설명 |
|---|---|---|
| Round | int | 0 = 모든 라운드 |
| MonsterId | int | 보스 불가 |
| StartTime / EndTime | float | 활성 구간(초). EndTime 0 = 보스 등장까지 |
| Interval / CountPerSpawn | float / int | 몇 초마다 몇 마리 |
| MaxAlive | int | 이 규칙으로 생성된 몬스터 동시 생존 상한 |
| SpawnPattern | enum | `Surround`(여러 방향) / `Flank`(측면·시야 밖) / `Air`(공중) / `Herd`(한 방향 무리) |

> 기획 "약 1분 후 침투형·공중형 합류, 약 2분 후 돌진형 합류, 후반 밀도 상승" = StartTime 이 다른 행들로 표현. 후반 밀도는 같은 몬스터의 짧은 Interval 행을 추가.

## SupplyTable — BL-018·019
| 컬럼 | 타입 | 설명 |
|---|---|---|
| SupplyType | enum | `Grenade` / `Heal` / `Weapon` (1회용 보급 무기) |
| DisplayName | string | HUD·분석 표시 이름 |
| AmountMin / AmountMax | int | 수류탄 / 회복 팩 지급 개수 범위 (획득 때 무작위, 양끝 포함). Weapon 은 0 |
| WeaponId | int | Weapon 행만: 지급할 WeaponTable.Id (`Category = Supply` 여야 함, 로딩 때 검사). 그 외 0 |
| Weight | int | 투하 종류 **기본** 가중치. 투하 때 상황 배율을 곱한다 (수류탄·회복 = 보유량, 무기 = 몬스터 급증·보급 무기 소진/보유 — ConfigTable `SupplyNeedWeight*`·`SupplyWeapon*`·`SupplySurge*`) |
| BeaconColor | string | 빛기둥 색 `#RRGGBB` (안개 무시 빛기둥은 이 색 × HDR 밝기) |
| PrefabKey / MarkerKey | string | 물자 프리팹 / 낙하 위치 표시 |
| DropSfxKey / AnnounceSfxKey / CollectSfxKey | string | 투하음(착지 지점) / 투하 안내 방송(2D) / 획득음(랜덤 장전음) |
| Description | string | 한 줄 활용 설명. 보급 무기 획득 카드(`SupplyInfoCardHud`)에 표시 — 5초 안에 읽을 수 있게 짧게 (아이템 행은 비워 둠) |
| CrateName / CrateDescription | string | 상자를 조준하거나 2m 안에 갔을 때 안내 제목 / 설명 한 줄 (예: `수류탄 보급 상자` / `수류탄과 탄약이 들어 있는 보급 상자`). 제목을 비우면 `{DisplayName} 보급 상자` |

모든 보급(Grenade·Heal·Weapon)은 획득 시 **1~3번 칸 총에 예비 탄창을 충전**한다 (보급 무기에는 충전 안 함) (`WeaponTable.SupplyMagazineRatio`). 아이템·탄약 중 하나라도 실제로 늘면 획득, 둘 다 가득이면 상자가 남는다.
Weapon 보급은 보급 칸(4번)에 무기를 장착하고 탄창도 충전한다 (같은 보급 무기를 들고 있으면 탄창만, 빛기둥 연보라 `#C8A2FF`, 획득 시 우측 카드 `SupplyInfoCardHud`). 1회용이라 탄창·예비 탄창을 모두 쓰면 칸에서 사라진다. 다른 보급 무기는 버려진다.
착지한 상자는 `ConfigTable.SupplyLifetime` 초 뒤 사라진다 (마지막 10초 빛기둥 깜빡임).
조준하지 않아도 상자 반경 2m(`SupplyCrate.pickupRadius`, Inspector) 안에서 F 로 획득한다 (근접 획득, `IProximityInteractable`).

## UpgradeTable — BL-030·031
| 컬럼 | 타입 | 설명 |
|---|---|---|
| UpgradeType | enum | `ReloadSpeed` / `MagazineSize` / `GrenadeCapacity` |
| Level | int | 종류별 1부터 연속 |
| Cost | int | 재화 |
| Value | float | 레벨마다 **누적**. ReloadSpeed = 감소율(0.1 = 10%, 합계 1 미만), MagazineSize = +발, GrenadeCapacity = +개 |
| Name / Description | string | 강화 화면 표시 |

## SoundTable / ConfigTable
| 테이블 | 컬럼 |
|---|---|
| SoundTable | Key, Category(`Bgm`/`Sfx`/`Monster`/`Ui`), Path(Resources 경로, `;` 로 여러 개 → 랜덤), Volume(0~1), Loop, Spatial(3D), MaxInstances(같은 키 동시 재생 수, 0 = 무제한), Cooldown(초), PitchJitter(±비율), Priority(0~100, 클수록 중요), MaxDistance(m, 0 = 무제한) — 클립이 없으면 오류가 아니라 경고 |
| ConfigTable | Key, Value, Desc — 코드는 `GameConfig` 상수 키로 접근 |

| Config Key | 의미 | 백로그 |
|---|---|---|
| SpawnSafeRadius | 시작 직후 플레이어 주변 생성 금지 반경 | BL-015 |
| SpawnRingRadius | 기본 스폰 링 반경 (전장 구역 없을 때) | BL-015 |
| CrowdRadius / CrowdMaxMelee | 근접 과밀 판정 반경 / 근접 몬스터 수 | BL-016 |
| StationaryRadius | 정지 판정 반경 (독안개 트리거) | |
| BossCameraFovAdd | 보스전 3인칭 FOV 추가(도) | |
| PlayerInvincibleTime | 피격 후 무적 | BL-006 |
| LowHpRatio | 플레이어 위험 상태 비율 | BL-007 |
| MonsterLowHpRatio | 일반 몬스터 저체력 표현 비율 | BL-017 |
| SupplyDropMinDistance / SupplyDropMaxDistance | 보급 낙하 위치: 플레이어 기준 XZ 거리 범위 (20 ~ 60) | BL-018 |
| SupplyDropHeight / SupplyLandingHeight | 낙하 시작 높이(30 이상) / 감속 착지 시작 높이 (지면 기준) | BL-018 |
| SupplyMaxTerrainHeight | 보급 낙하 허용 높이 (플레이어 발 높이 + 이 값 이하의 지면만, 5. 맵 원점·보스 아레나 높이와 무관). 착지면은 Ground 레이어여야 하고, 경사 25° 이하·플레이어에서 걸어서 갈 수 있는 곳(NavMesh 길 검사)만 (코드 상수, SupplyDropper) | BL-018 |
| SupplyMaxActive | 필드 동시 보급 최대 개수 (도달하면 그 주기 건너뜀, 5) | BL-018 |
| SupplyLifetime | 착지한 보급이 사라지기까지의 시간(초, 90, 0 = 사라지지 않음). 갈 수 없는 곳에 떨어진 상자가 최대 개수를 계속 차지하지 않도록 | BL-018 |
| SupplyNeedWeightMin / SupplyNeedWeightMax | 보급 확률: 수류탄·회복 팩을 가득 보유 / 0개일 때 그 상자 가중치 배율 (0.25 / 2, 사이는 보유 비율로 선형) | BL-018 || SupplyWeaponSurgeMultiplier / SupplyWeaponDepletedMultiplier / SupplyWeaponHeldMultiplier / SupplyWeaponMultiplierCap | 보급 확률: 무기 상자 배율 — 몬스터 급증 중 / 보급 무기를 다 쓴 뒤(다음 무기 상자까지) / 보급 무기 보유 중 / 상한 (2.5 / 2.5 / 0 = 보유 중 무기 상자 없음 / 4). 필드에 무기 상자가 있으면 급증·소진 배율 미적용 | BL-018 || SupplySurgeSpawnCount / SupplySurgeWindow / SupplySurgeDuration | 몬스터 급증 판정: 최근 Window 초 소환 수 ≥ Count 면 급증, Duration 초 유지 (12 / 10 / 30) | BL-018 |
| SupplyWeaponSurgeMultiplier / SupplyWeaponDepletedMultiplier / SupplyWeaponHeldMultiplier / SupplyWeaponMultiplierCap | 보급 확률: 무기 상자 배율 — 몬스터 급증 중 / 보급 무기를 다 쓴 뒤(다음 무기 상자까지) / 보급 무기 보유 중 / 상한 (2.5 / 2.5 / 0 = 보유 중 무기 상자 없음 / 4). 필드에 무기 상자가 있으면 급증·소진 배율 미적용 | BL-018 |
| SupplySurgeSpawnCount / SupplySurgeWindow / SupplySurgeDuration | 몬스터 급증 판정: 최근 Window 초 소환 수 ≥ Count 면 급증, Duration 초 유지 (12 / 10 / 30) | BL-018 |
| RoundStartGrenades / RoundStartHealPacks / RoundStartMagazines | 보스 클리어 후 다음 라운드 시작 지급: 수류탄 / 회복 팩 / 1~3번 칸 모든 총 예비 탄창 (-1 / 2 / 2, 최대치까지. **-1 = 최대치까지 가득**) | |
| CameraTransitionTime | 1인칭→3인칭 전환 시간 | BL-022 |
