# 3D 하이브리드 슈터 (Team Even)

vr9기 Unity 3D 코스 팀 프로젝트 · Unity 6000.3 (URP)

**1인칭으로 몰려오는 군중을 쓸어내고, 3인칭으로 거대한 보스와 맞서는 하이브리드 슈터.**
군중전(약 3분) → 보스 등장·시점 전환 → 보스전 → 강화 선택 → 다음 라운드.

## 브랜치 전략 (Git flow, release 미사용)

| 브랜치 | 역할 |
|---|---|
| `main` | 안정 버전. `develop` 에서만 merge |
| `develop` | 개발 통합. 완료된 feature 를 merge |
| `feat/<PascalCase>` | 기능 개발 (예: `feat/RifleFire`). develop merge 후 삭제 |
| `hotfix/<PascalCase>` | main 긴급 수정, 수정 직후 main 으로 |

커밋: `<Add|Feat|Fix|Refactor|Chore|Remove>: 영문 요약 (BL-xxx)` + 한국어 설명

## 프로젝트 구조

```
Assets/
├─ Scripts/<기능>/        Core · Player · Weapon · Monster · Boss · Crowd · Battlefield · Supply · Reward · UI · Audio
├─ Prefabs/<기능>/  Animations/<기능>/  Scenes/ (Main, Test_*)
└─ StreamingAssets/Tables/   기획 데이터 (TSV)
```

네임스페이스: `HybridShooter.<Scripts 아래 폴더 경로>`

## 문서
- [팀 규약](Docs/Conventions/TeamRules.md) · [작업 흐름](Docs/Guides/Workflow.md) · [문서 목차](Docs/README.md)
- 작업 관리: Asana "Team Even" 보드
