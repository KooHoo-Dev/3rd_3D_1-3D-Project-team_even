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
| 3000~3899 | MonsterTable 일반 (30xx) / MonsterSkillTable (35xx~38xx) |
| 3900~3999 | MonsterTable 보스 (390x) / 보스 스킬 (359x 처럼 MonsterSkillTable 대역 사용) |
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

## WeaponTable — BL-003·004
| 컬럼 | 타입 | 설명 |
|---|---|---|
| FireMode | enum | `Single` / `Auto` |
| Damage | int | 1발 피해 (일반 몬스터가 1~2발에 죽는 수준) |
| FireRate | float | RPM. 코드에서는 `FireInterval` |
| MagazineSize | int | 강화 전 탄창 |
| ReloadTime | float | 강화 전 재장전(초) |
| Range / SpreadAngle | float | 사거리(m) / 퍼짐 반각(도) |
| PrefabKey, FireSfxKey, ReloadSfxKey | string | |

## GrenadeTable — BL-005
| 컬럼 | 타입 | 설명 |
|---|---|---|
| Damage / Radius | int / float | 폭발 피해 / 반경(m) |
| FuseTime | float | 투척 후 폭발까지(초) |
| ThrowSpeed | float | 투척 초기 속도 |

## MonsterTable — E3, E5
| 컬럼 | 타입 | 설명 |
|---|---|---|
| Role | enum | `Melee`(BL-010) / `PoisonFog`(011) / `Infiltrator`(012) / `Aerial`(013) / `Charger`(014) / `Boss`(E5) |
| MaxHp | int | 일반은 낮게(쉽게 죽는 다수), 보스는 "체력 돼지" |
| MoveSpeed, AttackDamage, AttackRange, AttackInterval, DetectRange | | 기본 공격 |
| Reward | int | 처치 재화 (BL-029, 종류별 차등) |
| PrefabKey | string | `Resources/` 기준 경로 |

## MonsterSkillTable — BL-011, BL-023~027
| 컬럼 | 타입 | 설명 |
|---|---|---|
| MonsterId | int | MonsterTable.Id |
| SkillKey | string | 구현 클래스 키 (아래 표) |
| Slot | enum | `Basic`(일반 몬스터 고유) / `Entrance`(보스 등장 공격, 1개) / `Pattern`(보스 반복) |
| Damage / Radius | int / float | 피해 / 범위(m) |
| WindupTime | float | 준비 동작·예고 표시(초) = 플레이어 회피 시간 |
| RecoverTime | float | 공격 후 경직(초) = 플레이어 반격 시간 (체력 회복 아님) |
| Cooldown | float | 재사용 대기 |
| Weight | int | Pattern 선택 가중치 |
| Param1 / Param2 | float | SkillKey 별 의미 ↓ |

| SkillKey | Param1 | Param2 | 백로그 | 구현 클래스 |
|---|---|---|---|---|
| PoisonFog | 같은 위치 체류 판정 시간(초) | 독안개 지속(초) | BL-011 | (몬스터 담당 작성) |
| EntranceRoar | — | — | BL-023 | |
| GroundSlam | — | — | BL-024 | |
| Charge | 돌진 속도 | 벽 충돌 시 추가 경직(초) | BL-025 | |
| Artillery | 포탄 수 | 낙하 구역 반경(m) | BL-026 | |

## RoundTable — BL-015·018·021·032
| 컬럼 | 타입 | 설명 |
|---|---|---|
| Round | int | 1부터. 마지막 행 이후 라운드는 마지막 행 재사용 |
| BossId | int | MonsterTable.Id (Role=Boss) |
| BossSpawnTimeMin / Max | float | 보스 등장 시각 범위(초). 이 사이 무작위, 남은 시간 표시 안 함 |
| SupplyStartTime / SupplyInterval | float | 첫 보급(초) / 이후 간격 |
| BossReward | int | 보스 처치 추가 재화 |

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
| SupplyType | enum | `Grenade` / `Heal` |
| Amount | int | 수류탄 보충 수 / 회복량 |
| Weight | int | 투하 종류 가중치 |
| PrefabKey / MarkerKey | string | 물자 프리팹 / 낙하 위치 표시 |

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
| SoundTable | Key, Category(`Bgm`/`Sfx`/`Ui`), Path(Resources 경로), Volume(0~1), Loop |
| ConfigTable | Key, Value, Description — 코드는 `GameConfig` 상수 키로 접근 |

| Config Key | 의미 | 백로그 |
|---|---|---|
| SpawnSafeRadius | 시작 직후 플레이어 주변 생성 금지 반경 | BL-015 |
| SpawnRingRadius | 기본 스폰 링 반경 (전장 구역 없을 때) | BL-015 |
| CrowdNearRadius / CrowdMaxNear | 과밀 판정 반경 / 인원 | BL-016 |
| PlayerInvincibleTime | 피격 후 무적 | BL-006 |
| LowHpRatio | 플레이어 위험 상태 비율 | BL-007 |
| MonsterLowHpRatio | 일반 몬스터 저체력 표현 비율 | BL-017 |
| BossDamageStageRatios | 보스 외형 변화 비율 목록 (내림차순) | BL-028 |
| SupplyLifetime | 보급 유지 시간 | BL-018 |
| CameraTransitionTime | 1인칭→3인칭 전환 시간 | BL-022 |
