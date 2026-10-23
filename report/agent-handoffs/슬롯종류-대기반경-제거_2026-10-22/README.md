# 슬롯 종류·대기 반경 제거

## 목표와 시작 상태

- `feature/enemy-surround-and-chase`(HEAD `73020a05`)에서 별도 worktree `3rd_3D_1-3D-Project-team_even_ai`를 생성했다.
- 작업 브랜치: `codex/slot-kind-wait-radius-removal`. 시작 작업 트리는 깨끗하다.
- 목표: `SlotKind`와 `WaitRadius` 중복을 제거하고 기존 첫 링의 Engage/Wait 허가 의미를 유지한다.

## 계획

1. 실제 선언·호출부·테스트를 확인한다.
2. `SlotKind`는 첫 링 ID 범위에서 파생하도록 바꾼다.
3. `WaitRadius`의 토큰 모드 처리 방향을 확정한 뒤 제거한다.
4. 좁은 정적 확인과 관련 테스트로 검증한다.

## 결과와 결정

- `UseSlots=false`도 `CrowdCoordinator`를 경유한다. `CrowdCoordinator.Update`가 `Allocate → EngageAllocator.Allocate`로 토큰·대기 지점을 계산한 후 등록된 개체의 `Think`를 호출한다. 코디네이터를 거치지 않는 경로는 `MonsterMovement.ConfigureDirect`로 직접 목표를 설정한 개체다. 따라서 슬롯을 끈다는 설정만으로 토큰 경로가 불필요해지는 것은 아니다.
- `SlotKind`를 제거했다. `SlotSnapshot`에 `RingIndex`를 두고 현재 고정 두 링에서 첫 링은 0, 두 번째 링은 1로 표시한다. `SlotRegistry`의 예약·승격 판단은 기존 첫 링 ID 범위로 계산하고, `CrowdCoordinator`는 `RingIndex == 0`에서 `DirectiveKind.Engage`를 만든다. `DirectiveKind.Engage/Wait`과 근접 정원 제한은 유지한다.
- `CrowdSettings.WaitRadius`, `AllocationParams.WaitRadius`, `MovementContext.WaitRadius`, `MonsterMovement.Think`의 반경 인자를 제거했다. 슬롯 모드는 원래부터 실제 슬롯 위치를 사용한다. 토큰 모드(`UseSlots=false`)는 기존 `SlotSettings.WaitingRadius`로 대기점을 만들고, `WaitOrbit`은 전달받은 `WaitPoint`에서 선회 반경을 계산한다. 토큰 모드를 유지하는 최소 변경으로 판단했다.
- 토큰 모드의 기본 대기 반경은 이전 `WaitRadius=9m`에서 공유 설정 `WaitingRadius=8m`로 바뀐다. 이는 의도된 동작 변경이며, `NearRadius=6m < WaitingRadius=8m <= ReleaseRadius=10.5m`를 토큰 모드에서 검증한다. 저장된 실험실 씬의 `WaitRadius` 직렬화 값과 해당 Gizmo 원을 제거했다.
- 이 worktree는 `feature/enemy-surround-and-chase`의 커밋에서 분기했다. 앞선 대화에서 보인 다중 링 미커밋 작업은 이 브랜치 커밋에 포함되지 않아 이 worktree에는 없다. 이 작업은 요청한 두 값의 제거에 한정하며, 고정 두 링을 다중 링으로 확장하지 않았다.

## 수정 파일과 핵심 흐름

- `Assets/Scripts/Monster/Movement/Contracts/SlotTypes.cs`, `CrowdControl/SlotRegistry.cs`, `CrowdControl/CrowdCoordinator.cs`: `SlotKind` 대신 첫 링 인덱스에서 접근 허가를 판단한다.
- `CrowdControl/AllocationTypes.cs`, `CrowdControl/EngageAllocator.cs`, `Contracts/MovementTypes.cs`, `Core/MonsterMovement.cs`, `Policies/WaitOrbit.cs`: 독립 대기 반경 전달을 제거하고 토큰 모드의 대기 위치와 선회 반경을 공유한다.
- `Assets/MonsterMovementLab/Runtime/MovementLabGizmos.cs`, `Scenes/MonsterMovementLaboratory.unity`: 제거된 표시·직렬화 항목을 정리한다.
- `Assets/MonsterMovementLab/Tests/EditMode/SlotRegistryTests.cs`, `PolicyContractTests.cs`, `Tests/PlayMode/MovementPlayTests.cs`: 첫 링 판별을 갱신하고 토큰 모드 대기점·선회가 공유 반경을 따르는 테스트를 추가한다.

## 검증과 다음 단계

- `rg`로 `Assets`의 C#·씬·프리팹·에셋에서 `SlotKind`, `WaitRadius`, `waitRadius` 참조가 0건임을 확인했다.
- `git diff --check`에서 공백 오류가 없었다. 변경 전후 diff와 `SlotSnapshot`, `MovementContext`, `AllocationParams` 생성 호출부를 정적으로 확인했다.
- Unity 실행·컴파일·EditMode/PlayMode 테스트는 실행하지 않았다. Unity에서 `SlotRegistryTests`, `PolicyContractTests`를 우선 실행하고, `UseSlots=false`에서 8m 대기·선회와 `UseSlots=true`에서 첫 링 허가·외곽 대기를 확인해야 한다.
- 원래 `develop` 체크아웃은 수정하지 않았다. 이 보고서는 저장소 `.gitignore`의 `/report/` 규칙으로 Git 상태에는 나타나지 않지만 worktree 안에 있다.

## 후속 작업: 실험실 씬과 테스트 스크립트

- 요청: 관련 테스트 씬·스크립트를 갱신해 슬롯/토큰 모드와 새 반경 계약을 확인할 수 있게 한다.
- 수정 범위: 저장된 `MonsterMovementLaboratory.unity`의 기존 `MovementLabExample` 실행 메뉴, `MovementLabGizmos` 표시, 저장 씬 통합 테스트. 새 NavMesh/씬 자산은 만들지 않는다.
- 확인 중 발견: `MovementLabExample.DumpSlots`에 삭제된 `SlotSnapshot.Kind` 접근이 남아 있어 컴파일 오류가 예상된다. `RingIndex`로 고친다.
- 검증 방법: 변경 파일과 호출부 정적 확인, 남은 삭제 심볼 검색, `git diff --check`. Unity 실행은 요청되지 않은 무거운 검증이라 필요성 판단 후 진행한다.

### 완료 결과

- 기존 저장 씬 `Assets/MonsterMovementLab/Scenes/MonsterMovementLaboratory.unity`의 `MovementLabExample`에 `Play/Spawn Token Crowd` 메뉴를 추가했다. 기존 `Play/Spawn Slot Crowd`와 같은 인원·배치·정책으로 생성하되 `UseSlots=false`로 설정한다. 두 메뉴는 같은 씬과 NavMesh를 재사용한다. Play 모드에서 `Monster Movement Lab` 오브젝트의 `MovementLabExample` 컴포넌트 컨텍스트 메뉴로 실행한다.
- `MovementLabExample.DumpSlots`의 삭제된 `s.Kind` 접근을 `RingIndex`로 바꾸고, 토큰 모드에서 슬롯 덤프를 시도하면 해당 모드에는 예약 슬롯이 없다고 표시한다.
- `MovementLabGizmos`는 토큰 모드에서 슬롯 후보를 그리지 않고 실제 대기점 반경인 `Slots.WaitingRadius`를 원으로 그린다. 슬롯 모드에서는 첫 링과 대기 링 후보를 계속 표시한다.
- `LaboratoryIntegrationTests.SavedSceneSupportsContextActionsAndToolRemoval`에 토큰 군중 생성 → 등록 24개 확인 → 외곽 대기 개체의 슬롯 미사용과 `WaitPoint` 8m 확인 → 슬롯 군중으로 재전환 순서를 추가했다. 기존 저장 씬 자체의 직렬화는 앞 단계에서 삭제한 `WaitRadius` 항목 외에 새 설정이 필요하지 않다.
- 정적 확인: `Assets`의 C#·씬·프리팹·에셋에서 `SlotKind`, `WaitRadius`, `waitRadius`, `s.Kind`, `slot.Kind` 참조 없음. `git diff --check` 이상 없음. Unity Editor/컴파일/테스트 실행은 하지 않았다. 남은 확인은 Unity Test Runner에서 저장 씬 통합 테스트와 관련 EditMode 테스트 실행 및 Play 모드 메뉴로 토큰·슬롯 시각화 확인이다.
