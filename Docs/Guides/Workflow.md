# 작업 흐름 가이드 (Asana + Git)

> 팀 규칙 원문 요약: [Conventions/TeamRules](../Conventions/TeamRules.md)

## 1. 카드 하나의 흐름

| 단계 | Asana | Git |
|---|---|---|
| 1. 계획 | 카드 작성 (목적·완료 조건·관련 항목) → 스프린트 백로그 | — |
| 2. 시작 | **진행 중으로 옮긴 뒤** 코드를 연다 | `git switch develop` → `git pull` → `git switch -c feat/RifleFire` |
| 3. 작업 | 하위 작업 체크 | 개인 `Test_RifleFire` 씬에서 작업, 기능은 프리팹으로 |
| 4. 커밋 | — | `feat: RifleFire (BL-003)` + 빈 줄 + 한국어 설명 |
| 5. 리뷰 요청 | 완료 조건을 전부 스스로 확인 → **리뷰로** | develop 으로 PR (템플릿 채움) |
| 6. 완료 | 팀장/확인자가 완료 조건 확인 후 완료로 | merge 후 `feat/RifleFire` 삭제 |

- 커밋하기 전에 테이블을 고쳤다면 `Tools > Data > Validate Tables` → 오류 0
- 하루 1번 이상 `develop` 을 내 브랜치에 merge 해서 충돌을 작게 유지
- 막히면 Asana 카드에 Blocked 표시

## 2. 언제 무엇을 남기나

| 타이밍 | 남는 것 | Asana Type |
|---|---|---|
| Spike 끝 (조사 → 결정) | `Docs/Decisions/000N-*.md` | Spike — "결론이 기록으로 남았다" |
| 규칙·수치 확정 | `Docs/Decisions/` + TSV 커밋 | Design — "결정 확정·팀 합의" |
| 30분 이상 막힌 문제 해결 | `Docs/Troubleshooting/*.md` | Bug / 해당 카드 |
| 하루 끝 (5분) | `Docs/Devlog/YYYY-MM-DD-이름.md` | — |
| 구조·규칙 설명 | `Docs/Architecture/`, `Docs/Tables/` | Doc — "대상 독자가 이해했다" |

대안을 비교해서 고른 결정과 오래 막혔던 문제의 해결 과정은 발표·포트폴리오에서 가장 쓸모 있는 기록이다. 틀렸던 가설도 같이 적는다.

## 3. 발표·포트폴리오 정리 구조
1. 한 줄 소개 + 플레이 GIF (1인칭 군중전 → 3인칭 보스전 전환 장면)
2. 역할 분담과 내 담당
3. 아키텍처 다이어그램 (`Docs/Architecture`)
4. 기술적 의사결정 2~3개 (`Docs/Decisions` 요약)
5. 트러블슈팅 2~3개 (문제 → 원인 → 해결 → 결과)
6. 협업 방식: Asana 카드 흐름 + Git flow
7. 회고
