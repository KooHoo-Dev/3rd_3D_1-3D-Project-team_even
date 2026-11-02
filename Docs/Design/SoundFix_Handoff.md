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
| `mon_tyranopode_roar` | Roar | 페이즈 2 전환(`BeginTransition`), 포효 소환 예고 | Cue + PhaseChanged |
| `mon_tyranopode_crawl_loop` | CrawlLoop | 맴돌기 이동 중 반복 | 속도 감시 |
| `mon_tyranopode_bite` | Bite | 물기 예고 | PhaseChanged |
| `mon_tyranopode_bite_impact` | BiteImpact (mp3) | 물기 타격 순간 | Cue |
| `mon_tyranopode_charge_start` | ChargeStart | 돌진 예고 | PhaseChanged |
| `mon_tyranopode_charge_loop` | ChargeTentacleLoop (합성, 촉수로 달리는 소리) | 돌진하는 동안 반복 | PhaseChanged |
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
| `mon_tyranopode_intro_land` | Tremor (임시) | 등장 착지 (`IntroLand`) | Cue |
| `mon_tyranopode_cinematic_roar` | Roar | 등장 포효(`IntroRoar`), 죽기 전 최후 연출 울부짖음(`BeginFinale`) | Cue `CinematicRoar` |

클리페오사우루스 등장(`EntranceAttack`) 착지 순간에는 프리팹의 `BossSoundEmitter`가 `mon_clypeosaurus_entrance_land`(착지, StompGround 클립)와 `mon_clypeosaurus_entrance_roar`(포효, `ImpactVoiceKey`)를 재생합니다. 짓밟기 공격의 착지음(`mon_clypeosaurus_stomp_impact`)은 그대로 3D입니다.

**연출음은 크게:** 등장 착지·등장 포효·최후 울부짖음 키(`mon_clypeosaurus_entrance_land`, `mon_clypeosaurus_entrance_roar`, `mon_tyranopode_intro_land`, `mon_tyranopode_cinematic_roar`)는 **Sfx·2D**입니다. 플레이어와의 거리에 상관없이 같은 크기로 들리고, 보스 경고 때의 Monster 덕킹(3초간 30%)도 받지 않습니다. 포효하는 동안에는 Monster·BGM을 2.5초간 35%로 낮춥니다. 이 값은 각 이미터의 인스펙터(`voiceDuckLevel`·`voiceDuckTime`, `cinematicRoarDuckLevel`·`cinematicRoarDuckTime`)에서 바꿀 수 있습니다. Sfx 카테고리라서 옵션의 몬스터 볼륨이 아니라 효과음 볼륨을 따릅니다.

## 4. 남은 일

### 합성으로 만든 임시 클립 (5종)
아래 5개는 외부 소리 없이 `tools/audio/TyranopodeSfx`(C# 합성기)로 만들어 연결했습니다. 녹음 소리보다 단순하므로 더 좋은 소리를 구하면 같은 파일명으로 덮어쓰면 됩니다(테이블·코드는 그대로). 다시 만들 때는 `dotnet run --project tools/audio/TyranopodeSfx -- Assets/Resources/Audio/Monster/Tyranopode [이름]` (이름을 주면 그 소리만). 등장 착지용 합성음(IntroLand)은 어색해서 빼고 Tremor 클립으로 되돌렸습니다.

| 파일명 | SoundTable 키 | 재생 시점 | 설정 |
|---|---|---|---|
| `Tyranopode_JumpSmash` | `mon_tyranopode_jump_smash` | `JumpSmashAttack` 도약 (`OnActiveStarted`). Roar 0.05~1.4초를 0.8배 음높이로 낮춘 힘주는 소리 (약 1.8초) | Monster·3D |
| `Tyranopode_SmashImpact` | `mon_tyranopode_smash_impact` | `JumpSmashAttack` 착지 타격 | Monster·3D |
| `Tyranopode_PartBreak` | `mon_tyranopode_part_break` | 부위 파괴 (`BreakPart`). Roar 1.25~1.85초를 1.35배 음높이로 올린 비명 | Monster·3D |
| `Tyranopode_ChargeTentacleLoop` | `mon_tyranopode_charge_loop` | 돌진하는 동안 반복 (여러 촉수가 땅을 짚으며 달리는 소리, 2초 끊김 없는 루프). 원래 ChargeLoop 클립은 남겨 둠 | Monster·3D 루프 |
| `Tyranopode_OrbitalBeam` | `mon_tyranopode_orbital_beam` | 최후 연출 궤도 빔 타격 (`TyranopodeFinale.Strike`) | Sfx·2D |

### 보스 바로 부르기 (`Round/BossCallTester`)
게임 씬(`Monster Player Integration`)의 아무 오브젝트에 붙입니다. 런이 진행 중일 때 **F10 = 1스테이지 보스(클리페오사우루스), F11 = 2스테이지 보스(티라노포드)**입니다. 지금 라운드를 정리하고 그 라운드를 시작한 뒤, 0.5초 뒤 군중전을 건너뛰고 보스 경고·등장으로 넘어갑니다. 키·대기 시간은 인스펙터에서 바꿉니다. 지금 라운드에서 바로 보스만 부르려면 기존 F12를 씁니다.

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
