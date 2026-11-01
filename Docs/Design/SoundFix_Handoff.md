# 사운드 보완 작업 인수인계 (chore/SoundFix)

> 작성 2026-11-02 · 다른 PC에서 이어서 작업하기 위한 메모
> 체크리스트(실시간): https://claude.ai/artifact/H1SAEJQiouA5h2t6ivR1za

## 0. 다른 PC에서 시작하기

1. 이 PC에서 커밋·푸시가 되어 있어야 합니다. 특히 `Assets/Resources/Audio/Monster/Tyranopode/` 아래 wav·mp3 파일은 새 파일이라 커밋하지 않으면 다른 PC에 없습니다.
2. 다른 PC에서 `git fetch` 후 `chore/SoundFix` 브랜치를 체크아웃합니다.
3. Unity를 열고 컴파일을 확인합니다. 이 PC에서는 `dotnet build`로 오류 0개를 확인했지만, Unity 안에서는 아직 실행해 보지 않았습니다.
4. `Tools > Audio > Apply Import Rules`를 실행합니다. 티라노포드 클립이 전부 스테레오라 프로젝트 규칙에 따라 모노로 바뀝니다.
5. 플레이 테스트를 합니다. 확인할 항목은 아래 5번에 있습니다.

## 1. 이번 브랜치에서 한 일

| 항목 | 내용 | 파일 |
|---|---|---|
| 플레이어 소리 안 나던 버그 | `PlayerSoundEmitter`가 플레이어 프리팹에 없어서 소리가 나지 않았습니다. 라우터가 시작할 때 자동으로 붙이도록 고쳤습니다(A안). | `Audio/GameSoundRouter.cs`, `GameSoundRouter.Combat.cs` (`AttachPlayerEmitters`) |
| 피격음 경로 | 없는 클립 `HitFlesh_03`을 테이블에서 뺐습니다. | `SoundTable.tsv` `sfx_hit_flesh` |
| 승리 아나운서 | 클립을 `Announce_MissionAccomplished`로 바꿨습니다. | `SoundTable.tsv` `ann_victory` |
| 티라노포드 사운드 | 25개 키를 등록하고 게임 코드에 연결했습니다(A안). | 아래 2~3번 |

사용자가 직접 바꾼 것도 아직 커밋되지 않았습니다. 같이 커밋할지 정해야 합니다.
- `Assets/Audio_Work/AnnouncerPack`에서 Seg_02, Seg_20, Seg_28을 삭제했습니다.
- `Announce_Fight.wav`를 교체했습니다.
- `Announce_MissionAccomplished.wav`, `Announce_WalkieTalkie.wav`를 추가했습니다.

BGM 두 곡(`Audio/Bgm/Crowd`, `Audio/Bgm/Boss`)은 팀장이 넣기로 했습니다. 파일만 넣으면 되고 테이블은 고칠 필요가 없습니다. 파일이 없으면 경고만 뜨고 소리가 나지 않습니다.

## 2. 티라노포드 소리 구조 (A안: 지역 이벤트 + 전용 이미터)

```
TyranopodeController.SoundCue (event Action<TyranopodeSoundCue, Vector3>)
   ↑ RaiseSoundCue(cue, 위치)  ← 각 패턴·알·침·컨트롤러에서 한 줄씩 호출
   ↓
Audio/TyranopodeSoundEmitter  (GameSoundRouter 가 자동 부착 — 프리팹 수정 없음)
   ├ BossAttack.PhaseChanged   물기·돌진 예고, 돌진 루프, 포효 소환 예고·포자 발사
   ├ SoundCue                  그 밖의 순간 소리 (아래 표)
   ├ 이동 속도 ≥ 0.5m/s        기어가기 루프 (그로기·사망 때 정지)
   └ BossController.Died       루프 정지 + 사망음
```

- **자동 부착:** `GameSoundRouter.Boss.cs`가 붙입니다. `BossAppearedEvent`가 오면 붙이고, 씬에 직접 둔 티라노포드에는 `Start`에서 붙입니다.
- **보스 동작에는 영향 없음:** 보스 코드에는 이벤트 선언 1개와 발행 줄만 추가했습니다. 받는 쪽이 없어도 동작은 그대로입니다.
- **키 위치:** 키는 `TyranopodeSoundEmitter`의 직렬화 필드 기본값에 있습니다. `BossSoundEmitter`와 같은 방식이며, 인스펙터에서 바꿀 수 있습니다.
- **시작 시 키 검사:** 시작할 때 SoundTable에 없는 키가 있으면 경고 로그가 나옵니다.
- **소리 추가하는 법:**
  1. `TyranopodeSoundCue`에 값을 추가합니다.
  2. 이미터에 키 필드를 만들고 `KeyOf`에 넣습니다.
  3. 해당 지점에서 `RaiseSoundCue`를 호출합니다.
  4. SoundTable에 행을 추가합니다.

### 수정한 보스 파일 (MinSeong 님 코드, 발행 줄만 추가)
`TyranopodeController.cs`, `ChargeAttack.cs`, `SpitAttack.cs`, `EggThrowAttack.cs`, `TyranopodeEgg.cs`, `BiteAttack.cs`, `TentacleSwipeAttack.cs`, `TremorAttack.cs`, `TentacleStormAttack.cs`, `TyranopodeSpit.cs`, `SummonRoarAttack.cs`
새 파일: `Boss/Tyranopode/TyranopodeSoundCue.cs`, `Audio/TyranopodeSoundEmitter.cs`
→ PR을 올릴 때 MinSeong 님께 공유해 주세요.

## 3. 키 ↔ 재생 시점

| SoundTable 키 | 클립 | 재생 시점 | 경로 |
|---|---|---|---|
| `mon_tyranopode_roar` | Roar | 등장 포효(`IntroRoar`), 페이즈 2 전환(`BeginTransition`), 포효 소환 예고 | Cue + PhaseChanged |
| `mon_tyranopode_crawl_loop` | CrawlLoop | 맴돌기 이동 중 반복 | 속도 감시 |
| `mon_tyranopode_bite` | Bite | 물기 예고 | PhaseChanged |
| `mon_tyranopode_bite_impact` | BiteImpact (mp3) | 물기 타격 순간 | Cue |
| `mon_tyranopode_charge_start` | ChargeStart | 돌진 예고 | PhaseChanged |
| `mon_tyranopode_charge_loop` | ChargeLoop | 돌진하는 동안 반복 | PhaseChanged |
| `mon_tyranopode_wall_hit` | WallHit | 돌진 중 벽 충돌 | Cue |
| `mon_tyranopode_groggy` | Groggy | 그로기 시작 (벽 충돌·포자 파괴·보호막 파괴 공통) | Cue |
| `mon_tyranopode_spit` | Spit | 침 한 발마다 | Cue |
| `mon_tyranopode_spit_impact` | SpitImpact | 침 착탄 | Cue |
| `mon_tyranopode_egg_throw` | EggThrow | 알 던지기 (한 번) | Cue |
| `mon_tyranopode_egg_hatch` | EggHatch | 알 부화 (알 위치) | Cue |
| `mon_tyranopode_egg_break` | EggBreak | 알이 깨짐 | Cue |
| `mon_tyranopode_swipe` | Swipe (mp3) | 앞 촉수 휘두르기 (타격마다) | Cue |
| `mon_tyranopode_tremor` | Tremor | 땅울림 타격 | Cue |
| `mon_tyranopode_storm_sweep` | StormSweep | 촉수 폭풍 쓸기마다 | Cue |
| `mon_tyranopode_spore_launch` | SporeLaunch | 포자 쏘아 올림 (포효 소환 발동 시작) | PhaseChanged |
| `mon_tyranopode_spore_pulse` | SporePulse | 포자 맥박 | Cue |
| `mon_tyranopode_spore_break` | SporeBreak | 포자 파괴 성공 | Cue |
| `mon_tyranopode_spore_fail` | SporeFail | 포자 시간 초과 폭발 | Cue |
| `mon_tyranopode_absorb` | Absorb | 등장 흡수 시작(`IntroDrain`), 보호막 에너지 흡수 시작 | Cue |
| `mon_tyranopode_shield_hit` | ShieldHit | 보호막이 피해를 막음 | Cue |
| `mon_tyranopode_shield_break` | ShieldBreak | 보호막이 완전히 깨짐 | Cue |
| `mon_tyranopode_immune_hit` | ImmuneHit | 포자 무적 중 피격 (튕김 표시 간격) | Cue |
| `mon_tyranopode_death` | Death | 사망 (`BossController.Died`) | Died |

## 4. 남은 일

### 클립이 아직 없는 것 (5종) — 파일을 넣으면 위 "소리 추가하는 법"대로 진행

| 파일명 | 우선순위 | 연결 후보 지점 |
|---|---|---|
| `Tyranopode_JumpSmash` | 필수 | `JumpSmashAttack` 도약 시점 (예고는 `PhaseChanged(Windup)`도 가능) |
| `Tyranopode_SmashImpact` | 필수 | `JumpSmashAttack` 착지 타격 지점 |
| `Tyranopode_PartBreak` | 권장 | 부위 파괴 → `EnterFlinch` 쪽 |
| `Tyranopode_IntroLand` | 선택 | `TyranopodeController.IntroLand` |
| `Tyranopode_OrbitalBeam` | 선택 | `TyranopodeFinale` 궤도 빔 |

### 플레이 테스트 때 확인할 것
- `Roar`(6.3초)가 등장 포효 구간(2초, 두 번째 등장부터는 1.1초)보다 깁니다. 거슬리면 클립을 자르거나, 연출이 끝날 때 멈추는 처리를 추가합니다.
- `Bite`(3.6초)가 물기 예고보다 길 수 있습니다. `Absorb`는 6.9초입니다.
- `ShieldHit`·`ImmuneHit`·`SpitImpact`처럼 자주 나는 소리는 SoundTable의 Cooldown·MaxInstances로 걸러집니다. 너무 시끄러우면 이 두 값을 조정합니다.
- 보호막의 겹이 깨질 때는 별도 소리가 없습니다(ShieldHit만 납니다). 필요하면 `AbsorbByShield`의 `BreakLayer` 지점에 Cue를 추가합니다.
- 사망음은 최후 연출의 마지막 일격(`BossController.Died`) 때 납니다. 궤도 빔 소리가 들어오면 두 소리가 겹치는지 확인합니다.
- 일반 몬스터·플레이어 소리가 실제로 나는지도 다시 확인합니다(1번의 버그 수정).

### 그 밖에
- 커밋·PR은 아직 하지 않았습니다(브랜치 `chore/SoundFix`, 베이스 `main`).
- 관련 문서: `docs/Design/SoundSystemGuide.md`. 티라노포드 이미터 내용을 이 문서에 반영할지 검토합니다.
