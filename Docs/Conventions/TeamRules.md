# 팀 규약 요약

> 원본: Notion "3D 하이브리드 슈터" 페이지 (리포의 `팀프로젝트 지침 정보/` export), Asana "Team Even" 보드의 `규칙 및 공유` 섹션.
> 원본이 바뀌면 이 문서를 함께 갱신한다.

## 1. Git

### 브랜치 전략 — Git flow (release 미사용)
| 브랜치 | 역할 | merge 방향 |
|---|---|---|
| `main` | 버그 없이 안정적인 버전 | `develop` 에서만 받음 |
| `develop` | 개발 통합 | feature 를 받고, 확인 후 `main` 으로 |
| `feat/<작업명>` | 기능 개발용 일회성 | `develop` 으로 merge 후 **삭제** |
| `hotfix/<작업명>` | main 의 긴급 버그 수정 | 수정 직후 `main` 으로 |

### 브랜치 명명
- `feat/작업명`, 작업명은 **영어 PascalCase**, 구체적으로
  - ○ `feat/PlayerMovement`, `feat/InventoryUI`, `feat/Minigame-UI-Panel`
  - ✗ `feat/Game`, `feat/System`
- 공백 불가. 추가 구분이 필요하면 하이픈 `-`

### 커밋 메시지
```
<타입>: <간단한 내용 요약>        ← 예시처럼 짧은 영문

<작업 내용 설명>                  ← 한국어
```
| 타입 | 용도 |
|---|---|
| Add | 새로운 오브젝트, 리소스 추가 |
| Feat | 새로운 기능 추가 |
| Fix | 버그 수정 |
| Refactor | 리팩터링 |
| Chore | 코드 수정 없이 설정·빌드 정보 변경 |
| Remove | 리소스나 오브젝트 삭제 |

예) `feat: EndingUI` / `fix: sprite render problem` / `chore: Install Rider` / `Add: add sprite`

**+ Asana 규칙**: 커밋 메시지에 카드 제목의 핵심 단어를 넣는다 → 이 리포에서는 백로그 ID 를 괄호로: `feat: RifleFire (BL-003)`

**이 프로젝트에서의 적용**
| 작업 | 타입 |
|---|---|
| TSV 수치만 조정 | Chore |
| 테이블 컬럼 추가 + 코드 | Feat |
| 문서 신규 작성 | Add |
| 문서 수정 | Chore |

## 2. 폴더명
- 모든 폴더명 **영어 PascalCase** (`player`, `mini_game`, `Mini Game`, `플레이어` ✗)
- 최상위는 **에셋 종류** (복수형): `Animations, Art, Audio, Data, Input, Materials, Prefabs, Scenes, ScriptableObjects, Scripts, Settings, Shaders` + `Plugins`, `ThirdParty`
- 내부는 **기능 단위**, 에셋 종류가 달라도 같은 기능은 같은 이름: `Scripts/Monster`, `Prefabs/Monster`, `Animations/Monster`
- 기능이 커지면 그 안에서 세분화 (`Monster/Common`, `Monster/PoisonFog`). 처음부터 세분화하지 않음
- `Managers`, `Systems`, `Misc` 같은 넓은 이름 폴더 지양. `Manager` 클래스 이름은 가능

## 3. 네임스페이스
- `프로젝트명.폴더경로` — `Assets/Scripts/` 아래 경로를 그대로, 저장용 폴더명(`Assets`, `Scripts`) 제외
- 영어 PascalCase, 점으로 계층 구분
- 이 프로젝트: **`HybridShooter`** → `Assets/Scripts/Monster/PoisonFog/` = `HybridShooter.Monster.PoisonFog`

## 4. 작업자 간 씬 분리
1. **개인 작업**: 메인 씬을 수정하지 않고 새 임시 씬 생성 (`Test_Dialogue`, `Feat_GoldFish_Action`). 기능은 특정 씬에 종속되지 않게
2. **합동 작업**: 통합·동시 작업이 필요할 때만 공통 씬. 각자 프리팹으로 만들어 배치하는 방식 지향
3. **정리**: 끝난 임시 씬은 확인 후 삭제

## 5. Asana 보드
- 보드 운영 기록 자체가 **제출물이자 평가 대상**. 코드는 Git, 작업은 Asana. 기록 안 된 작업 = 안 한 작업
- 섹션(상태): 규칙 및 공유(고정) → 백로그 → 스프린트 백로그 → 진행 중(팀 4개) → 리뷰(팀 3개) → 완료. 한 방향으로만 이동
- 카드: 담당자 1명, 하위 작업으로 세부 단계. 하위 작업 0개 → 하위 작업일 가능성 / 10개 초과·3일 초과 → 쪼개기
- 필드: Epic(E1~E9), Type(Feature/Bug/Chore/Spike/Design/Doc), 담당자, 마감일(스프린트 백로그부터, Spike 는 필수)
- Type 고르는 순서: Bug → Spike → Design → Doc → Feature → Chore (먼저 걸리는 것)
- 카드 형식: **목적**(안 하면 무엇이 막히나) / **완료 조건 DoD**(다른 팀원이 O/X 판정 가능, 3~6개) / **관련 항목**(참고·선행·후속·PR)
- Type 별 완료 의미: Feature 동작한다 · Bug 재발하지 않는다 · Chore 목표 상태 도달 · Spike 결론이 기록으로 남음 · Design 결정 확정·팀 합의 · Doc 대상 독자가 이해함
- 이동: 백로그→스프린트(Sprint·담당·DoD 필수) / 스프린트→진행 중(손대는 순간, 1인 1개) / 진행 중→리뷰(DoD 전부 스스로 확인) / 리뷰→완료(작성자 아닌 팀장·확인자)
- 하루 리듬: 시작 시 카드를 진행 중으로 → 커밋에 카드 핵심어 → 종료 시 카드 상태를 실제와 맞춤
- 흔한 실수: 카드 없이 작업 / 담당자 비움 / 진행 중 건너뛰기 / DoD 를 "구현 완료"로 / Blocked 미표시 / Spike 를 공부 시간으로

## 6. 링크
- Git: https://github.com/KooHoo-Dev/3rd_3D_1-3D-Project-team_even
- Asana: Team Even 보드
- 참고 서적: Pro Git 2 (`팀프로젝트 지침 정보/.../progit.pdf`)
