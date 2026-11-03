#!/usr/bin/env bash
# 대사본 TSV(Tutorial_VoiceScript.tsv) → 공유용 md · TTS 입력 TXT · 파일명 대조 TXT 를 다시 만든다.
# 사용: bash Docs/Design/build_voice_script.sh   (TSV 열: 대사 Id · 단계 · 나오는 때 · 자막 · TTS 입력(비우면 자막과 같음))
# 게임 안 자막은 Unity 메뉴 Tools > Tutorial > Reimport Lines From Voice Script 로 TutorialScript 에셋에 반영한다.
D="$(cd "$(dirname "$0")" && pwd)"
S="$D/Tutorial_VoiceScript.tsv"
TMP="$(mktemp)"
tr -d '\r' < "$S" > "$TMP"
n=$(grep -c . "$TMP")

awk -F'\t' 'NF{t=($5==""?$4:$5); print t}' "$TMP" > "$D/Tutorial_VoiceScript_TTS.txt"
awk -F'\t' 'NF{t=($5==""?$4:$5); printf "%02d. %s  %s\n", ++i, $1, t}' "$TMP" > "$D/Tutorial_VoiceScript_FileNames.txt"
{
cat <<EOF
# 튜토리얼 대사본 v3 — TTS 입력용

- 기준 문서: \`Tutorial_Proposal_v3.md\` (불시착 후 기본 전투 테스트, 6단계)
- 화자: 궤도 모함 **"오퍼레이터"** (무전)
- 대사 수: **${n}줄**
- 원본은 \`Tutorial_VoiceScript.tsv\` 하나다. 고친 뒤 \`bash Docs/Design/build_voice_script.sh\` 로 이 문서와 TXT 두 개를 다시 만든다
  - \`Tutorial_VoiceScript_TTS.txt\` : TTS 에 넣을 대사만. 한 줄 = 대사 하나
  - \`Tutorial_VoiceScript_FileNames.txt\` : \`번호. 대사ID  대사\` — N번째 음성을 N번째 ID 이름으로 저장

## 0. 사용법

1. **TTS 설정 (권장)**: 차분한 성인 목소리, 군 관제·무전 톤 · 속도 1.0~1.1배 · 감정 담담하게
2. **넣기**: \`Tutorial_VoiceScript_TTS.txt\` 를 그대로 넣는다.
3. **파일 이름**: 대사 ID 그대로 (예: \`vo_tut_01_move.wav\`), wav 44.1kHz 모노 기준 (mp3 도 가능). \`Assets/Audio/Voice/Tutorial/\` 에 넣는다.
4. **자막 / TTS 입력이 다른 줄**: 키 이름·숫자는 TTS 가 잘못 읽지 않도록 소리 나는 대로 적었다 (Shift → 시프트, Ctrl → 컨트롤, G → 지, 4번 → 사 번 등). 화면 자막은 원래 표기. **자막을 고치면 TTS 입력 칸도 같이 고친다.**
5. **대사 ID 는 바꾸지 않는다.** 구현(\`TutorialScript\` 에셋)이 ID 로 음성을 찾는다.

## 1. 대사별 표

나오는 때: **시작** = 단계 시작 · **이어서** = 앞 대사가 끝난 뒤 · **힌트** = 20초 동안 진행이 없을 때 · **완료** = 과목 통과

EOF
awk -F'\t' 'NF{ if($2!=prev){ printf "\n### %s\n| # | 대사 ID | 나오는 때 | 자막 | TTS 입력 |\n|---|---|---|---|---|\n", $2; prev=$2 } t=($5==""?"(자막과 같음)":$5); printf "| %d | `%s` | %s | %s | %s |\n", ++i, $1, $3, $4, t }' "$TMP"
cat <<'EOF'

## 2. 음성이 없는 안내 (화면 문구만)

- 목표 패널 문구 (예: "표적 처치 2 / 3")
- "+N 생체 자원" 표시
- 보급 상자 안내 (기존 보급 상자 프롬프트·정보 카드)
- 무기 칸이 비어 있을 때 경고 (무기 선택 화면 · 게임 중 토스트)
- 보급 무기별 특징 (기존 보급 정보 카드 — 무기가 바뀌어도 다시 녹음할 필요 없음)
EOF
} > "$D/Tutorial_VoiceScript.md"
rm -f "$TMP"
echo "대사 ${n}줄 → Tutorial_VoiceScript.md · _TTS.txt · _FileNames.txt"
