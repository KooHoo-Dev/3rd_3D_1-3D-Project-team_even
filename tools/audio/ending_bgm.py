"""
엔딩 BGM 합성기 — 인트로 BGM(intro_bgm.py)의 악기·믹스 엔진을 그대로 불러 쓰고 악보만 새로 쓴다 (외부 샘플·라이브러리 없음)

    python tools/audio/ending_bgm.py            → Assets/Resources/Audio/Bgm/Ending.wav
    python tools/audio/ending_bgm.py -o out.wav

곡 구성 (80 BPM, 4/4 → 1박 0.75초, 1마디 3초). 마디 경계 시각은 EndingTimeline(C#) 의 연출 타이밍과 같다
    마디  0~3    0 ~ 12초  탑승       D 장조. 하프 8분 오스티나토 + 비올라 패드 위에 호른 독주가 '귀환 주제'를 처음 들려준다
                                     (I – V – vi – IV, 새벽 · 고요한 희망). 마디 2부터 바이올린 하모닉스·합창이 아주 옅게
    마디  4~5   12 ~ 18초  점화       iii – Vsus4·V7. 첼로 8분이 점점 세지고, 하프 16분 → 바이올린 상행 → 팀파니 롤 · 라이저
    마디  6~9   18 ~ 30초  이륙·궤도  인트로 팡파르 주제(라이트모티프)를 이번엔 승리의 색으로 — I – ♭VI·♭VII – II(Lydian) – I – IV.
                                     트럼펫 주제 + 호른 화음 + 바이올린 높은 대선율. 마디 8(우주)부터 합창이 더해진다.
                                     마지막 박에서 B7 로 틀어 반음 위 E 장조로 전조할 준비
    마디 10~11  30 ~ 36초  궤도 돌파  E 장조로 한 음 올라선 클라이맥스 (I – vi – IV·V). 바이올린·호른 옥타브 선율, 트럼펫 화음 찌르기, 하프 16분 반짝임
    마디 12     36 ~ 39초  도약       E 장조 화음을 붙잡은 채 하프 글리산도로 위로 — 우주선이 별 사이로 사라진다
    마디 13~24  39 ~ 75초  크레딧     엔드 타이틀. 귀환 주제를 세 번 — A 호른 독주(하프) / B 바이올린(첼로 스타카토, 호른 대선율) / C 총주(트럼펫·합창)
                                     화면: 마디 17(51초) 행성에 금빛 균열 → 마디 21(63초) 행성 폭발. B 아래로 아주 낮은 땅울림이 점점 차오르고,
                                     마디 20 라이저·팀파니 롤 뒤 반 박을 비웠다가 C 첫 박에 폭발 타격(붐 + 열린 5도 브람) — C 총주는 장조 그대로 '해방'
    마디 25~    75초 ~     종지       IV – iv(단조 차용 '아멘' 종지, 아련함) – I. 78초에 으뜸화음, 잔향으로 끝

레퍼런스 (선율은 옮기지 않고 기법만) — '전투 뒤의 귀환·해방감'을 그린 영화·게임 엔딩 곡들
  - John Williams, E.T. 'Adventures on Earth' : 비행 장면의 Lydian II(장조 위 II 화음) 들어 올림, 하프 글리산도, 바이올린 옥타브 선율
  - James Horner, Apollo 13 'Re-Entry & Splashdown' : 고요한 현 위의 금관 독주로 시작해 총주로 터지는 구성, 반음·온음 위로 올라서는 전조
  - Hans Zimmer, Interstellar 'Day One' : 오래 붙잡는 패드와 쉬지 않는 아르페지오 오스티나토가 만드는 넓은 공간감
  - Star Wars 'Throne Room & End Title' : 엔드 크레딧에서 주제를 악기를 바꿔 가며 여러 번 다시 부르는 구성
  - Halo 3 'Finish the Fight' · Mass Effect 3 'An End Once and For All' : 단조 차용 iv 로 끝맺는 아련한 종지, 합창 'ah' 패드
라이트모티프: 마디 6~9 의 트럼펫 주제는 인트로 팡파르(마디 2~5)의 주제를 그대로 가져와 같은 화성으로 부른다 →
'추락하던 그 주제가 이번엔 떠오른다'. 탑승·크레딧의 귀환 주제는 같은 윤곽을 조성만 바꿔 쓴다 (D → E)

렌더 엔진(웨이브테이블 악기, 레가토·성부 진행, 홀 리버브, -16 LUFS · -1.5 dBTP 마스터링)은 intro_bgm.py 설명 참고
"""

import argparse
import os
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import intro_bgm as eng  # noqa: E402  (엔진 함수는 eng 의 전역(BEAT·N·bus)을 읽는다 → 아래에서 엔딩 값으로 바꾼다)
from intro_bgm import (midi, notes, line, voice_lead, dynamics, strings, string_legato, horn_legato, bass_legato,  # noqa: E402
                       trumpet, choir, harp, celli_staccato, taiko, timpani, timpani_roll, boom, crash,
                       reverse_cymbal, riser, ostinato)

BPM = 80
BEAT = 60.0 / BPM
FINAL_BEAT = 104       # 마디 26 = 78초 — 마지막 으뜸화음
END_BEAT = 112         # 마디 28 = 84초 — 화면 암전 끝 (EndingTimeline.End)
TAIL = 6.0
LENGTH = END_BEAT * BEAT + TAIL
FADE_IN = 1.5
FADE_OUT_START = FINAL_BEAT * BEAT + 3.0   # 으뜸화음이 3초 울린 뒤 잔향 끝까지 서서히


def configure_engine():
    """엔진 전역을 엔딩 곡 값으로 (박 길이·전체 길이·버스·난수)"""
    eng.BPM = BPM
    eng.BEAT = BEAT
    eng.BAR = 4 * BEAT
    eng.LENGTH = LENGTH
    eng.N = int(LENGTH * eng.SR)
    eng.FADE_IN = FADE_IN
    eng.FADE_OUT_START = FADE_OUT_START
    eng.bus = {name: np.zeros((eng.N, 2)) for name in eng.BUSES}
    eng.rng = np.random.default_rng(20261102)


# ───────────────────────── 화성 ─────────────────────────

# (박 수, 화음 음 이름, 베이스). 비올라 4성부·베이스·호른·합창이 이 진행을 성부 진행으로 따라간다
HARMONY = (
    # 탑승 (마디 0~3, D): I – V – vi – IV
    (4, 'D F# A', 'D2'), (4, 'A C# E', 'A1'), (4, 'B D F#', 'B1'), (4, 'G B D', 'G1'),
    # 점화 (마디 4~5): ii(Em) – Vsus4·V7
    (4, 'E G B', 'E2'), (2, 'A D E', 'A1'), (2, 'A C# E G', 'A1'),
    # 이륙·궤도 (마디 6~9): 인트로 팡파르 화성 I – ♭VI·♭VII – II – I – IV, 끝을 Bsus4·B7 로 틀어 E 로
    (4, 'D F# A', 'D2'), (2, 'Bb D F', 'Bb1'), (2, 'C E G', 'C2'), (2, 'E G# B', 'E2'), (2, 'D F# A', 'D2'),
    (2, 'G B D', 'G1'), (1, 'B E F#', 'B1'), (1, 'B D# F# A', 'B1'),
    # 궤도 돌파 (마디 10~11, E): I – vi – IV·V
    (4, 'E G# B', 'E2'), (2, 'C# E G#', 'C#2'), (1, 'A C# E', 'A1'), (1, 'B D# F#', 'B1'),
    # 도약 (마디 12): I
    (4, 'E G# B', 'E2'),
    # 크레딧 A (마디 13~16): I – V – vi – IV
    (4, 'E G# B', 'E2'), (4, 'B D# F#', 'B1'), (4, 'C# E G#', 'C#2'), (4, 'A C# E', 'A1'),
    # 크레딧 B (마디 17~20): I – iii – IV – Vsus4·V7
    (4, 'E G# B', 'E2'), (4, 'G# B D#', 'G#1'), (4, 'A C# E', 'A1'), (2, 'B E F#', 'B1'), (2, 'B D# F# A', 'B1'),
    # 크레딧 C (마디 21~24): IV – V – iii·vi – ii·V
    (4, 'A C# E', 'A1'), (4, 'B D# F#', 'B1'), (2, 'G# B D#', 'G#1'), (2, 'C# E G#', 'C#2'), (2, 'F# A C#', 'F#1'),
    (2, 'B D# F#', 'B1'),
    # 종지 (마디 25~27): IV – iv – I
    (2, 'A C# E', 'A1'), (2, 'A C E', 'A1'), (8, 'E G# B', 'E2'),
)

# 귀환 주제 (탑승 D 장조 / 크레딧 E 장조 — 같은 윤곽)
RETURN_THEME_D = 'F#4 2 A4 1 D5 1   C#5 3 A4 1   B4 2 D5 1 F#5 1   E5 3 D5 1'
RETURN_THEME_E = 'G#4 2 B4 1 E5 1   D#5 3 B4 1   C#5 2 E5 1 G#5 1   F#5 3 E5 1'
# 인트로 팡파르 주제 (intro_bgm.py 마디 2~5) — 마지막 박만 D#5 로 바꿔 B7 → E 전조로 이어진다
FANFARE_THEME = 'A4 1.5 D5 0.5 E5 1 F#5 1   F5 1.5 D5 0.5 E5 1.5 G5 0.5   G#5 2 F#5 1.5 E5 0.5   D5 1 F#5 1 E5 1 D#5 1'


def progression_from(start_beat, end_beat):
    """HARMONY 에서 [start_beat, end_beat) 구간 → voice_lead 용 [(박 수, 음 이름)]"""
    result, beat = [], 0
    for beats, names, _ in HARMONY:
        if start_beat <= beat < end_beat:
            result.append((beats, names))
        beat += beats
    return result


def hit(beat, vel, root):
    """밝은 타격: 붐은 약하게(어두운 트레일러 색을 빼고), 타이코·심벌·팀파니는 으뜸음으로"""
    boom(beat, vel * 0.55)
    taiko(beat, vel * 0.8)
    crash(beat, vel * 0.8)
    timpani(midi(root), beat, vel)


def rumble(beat, beats, v0, v1):
    """아주 낮은 땅울림 (25~70Hz 노이즈 + 35Hz 사인, 크기 v0 → v1 로 차오르며 천천히 일렁인다)"""
    length = int(beats * BEAT * eng.SR)
    t = np.arange(length) / eng.SR
    k = t / t[-1]
    body = eng.bandpass(eng.noise(length, 2), 25, 70) * 2.5
    body += np.sin(2 * np.pi * 35.0 * t)[:, None] * 0.25
    swell = (v0 + (v1 - v0) * k ** 1.5) * (0.8 + 0.2 * np.sin(2 * np.pi * 0.7 * t))
    eng.add('fx', body * swell[:, None] * 0.6, beat * BEAT)


def planet_blast(beat):
    """행성 폭발: 두 겹 붐 + 낮은 열린 5도 브람(장조라 어둡지 않게) + 큰 심벌·타이코·팀파니"""
    eng.braam(notes('A1 E2 A2 E3'), beat, 0.85, beats=4)
    boom(beat + 0.08, 0.9)
    taiko(beat, 1.0)
    crash(beat, 0.95, 6.0)
    timpani(midi('A2'), beat, 1.0)


def arpeggios(chords, start_beat, step=0.5, vel=0.32, pattern=(0, 1, 2, 3, 2, 1, 2, 1)):
    """[(박 수, '하프 음')] 을 이어서 하프 아르페지오"""
    beat = start_beat
    for beats, tones in chords:
        ostinato(tones, beat, beats, step=step, vel=vel, pattern=pattern)
        beat += beats


def celli_pulse(roots, start_beat, vel_from, vel_to):
    """[(박 수, '근음')] → 첼로 8분 스타카토 (근음·옥타브 번갈아, 셈여림은 처음 → 끝으로)"""
    total = sum(b for b, _ in roots)
    beat = start_beat
    for beats, root in roots:
        for k in range(int(beats * 2)):
            pos = (beat + k * 0.5 - start_beat) / total
            vel = vel_from + (vel_to - vel_from) * pos
            celli_staccato(midi(root) + (12 if k % 2 else 0), beat + k * 0.5, 0.5, vel if k % 2 == 0 else vel * 0.75)
        beat += beats


def choir_chords(start_beat, end_beat, vel, low=60, high=76):
    """합창 3성부 — 화성 진행을 성부 진행으로 (긴 'ah' 패드)"""
    for part in voice_lead(progression_from(start_beat, end_beat), start_beat, voices=3, low=low, high=high):
        for m, b, beats in part:
            choir(m, b, beats + 0.25, vel)


def harp_glissando(scale, beat, beats, vel=0.4):
    """음계를 빠르게 훑어 올라간다 (하프 글리산도)"""
    ms = notes(scale)
    step = beats / len(ms)
    for i, m in enumerate(ms):
        harp(m, beat + i * step, vel * (0.7 + 0.3 * i / len(ms)), p=0.3 + 0.4 * i / len(ms))


# ───────────────────────── 악보 ─────────────────────────

def score():
    progression = [(beats, names) for beats, names, _ in HARMONY]

    # ── 바탕: 비올라 4성부 (곡 내내) + 베이스 ──
    viola_dyn = dynamics([(0, 0.32), (8, 0.42), (16, 0.48), (23.5, 0.62), (24, 0.55), (40, 0.62), (48, 0.6), (52, 0.38),
                          (68, 0.45), (84, 0.58), (100, 0.5), (104, 0.55), (112, 0.5)])
    for part in voice_lead(progression, 0, voices=4, low=55, high=74):
        string_legato('violas', part, 1.0, attack=0.6, release=3.0, dyn=viola_dyn)
    bass_events, beat = [], 0
    for beats, _, root in HARMONY:
        bass_events.append((midi(root), beat, beats))
        beat += beats
    bass_legato(bass_events, 1.0, dynamics([(0, 0.0), (3.5, 0.0), (4, 0.35), (16, 0.45), (24, 0.62), (48, 0.6), (52, 0.38),
                                            (84, 0.58), (104, 0.6), (112, 0.55)]))

    # ── 마디 0~3: 탑승 (0~16박, D) — 하프 + 패드 위 호른 독주. 새벽처럼 한 겹씩 ──
    arpeggios(((4, 'D5 F#5 A5 D6'), (4, 'C#5 E5 A5 C#6'), (4, 'B4 D5 F#5 B5'), (4, 'B4 D5 G5 B5')), 0, vel=0.3)
    horn_legato(line(RETURN_THEME_D, 0), 0.55, attack=0.3, release=1.2)
    strings(midi('A5'), 8, 8, 0.18, attack=2.5, section='violins', bright=0.12)     # 하모닉스처럼 높고 옅게
    strings(midi('E6'), 9, 7, 0.14, attack=2.5, section='violins', bright=0.12)
    choir(midi('D4'), 8, 8, 0.18)
    choir(midi('A4'), 8, 8, 0.16)

    # ── 마디 4~5: 점화 (16~24박) — 한 겹씩 쌓아 이륙으로 ──
    celli_pulse(((4, 'E2'), (4, 'A2')), 16, 0.32, 0.62)
    ostinato('E5 A5 C#6 E6', 20, 4, step=0.25, vel=0.38)
    for part in voice_lead(progression_from(16, 24), 16, voices=3, low=50, high=67):
        horn_legato(part, 0.42, attack=0.4, release=0.8)
    run = 'A4 B4 C#5 D5 E5 F#5 G5 A5'.split()                                        # 22: 바이올린 상행 (음마다 활)
    for k, name in enumerate(run):
        strings(midi(name), 22 + k * 0.25, 0.35, 0.32 + 0.03 * k, attack=0.02, release=0.3, section='violins')
    timpani_roll(midi('A2'), 20, 4, 0.15, 0.9)
    riser(16, 8, 0.5)
    reverse_cymbal(24, 4, 0.55)

    # ── 마디 6~9: 이륙·궤도 (24~40박) — 인트로 팡파르 주제를 승리의 색으로 ──
    hit(24, 1.0, 'D2')
    for m, b, beats in line(FANFARE_THEME, 24):
        trumpet(m, b, beats * 1.05, 0.88)
    for part in voice_lead(progression_from(24, 40), 24, voices=3, low=50, high=67):
        horn_legato(part, 0.45, attack=0.1, release=0.8)
    string_legato('violins', line('D6 4   D6 2 E6 2   E6 2 F#6 2   D6 2 F#6 2', 24), 0.42, attack=0.25)
    celli_pulse(((4, 'D2'), (2, 'Bb1'), (2, 'C2'), (2, 'E2'), (2, 'D2'), (2, 'G1'), (2, 'B1')), 24, 0.5, 0.6)
    for b in range(24, 40, 2):
        taiko(b, 0.55 if b % 4 == 0 else 0.4, 0.15 if b % 4 else -0.1)
    for b in (27, 31, 35, 39):
        taiko(b + 0.5, 0.25, 0.25)
    crash(32, 0.45)                                                                   # 32: 대기권을 벗어난다 — 합창이 더해진다
    choir_chords(32, 40, 0.32)
    timpani_roll(midi('B1') + 12, 38, 2, 0.3, 0.85)
    reverse_cymbal(40, 2, 0.45)

    # ── 마디 10~11: 궤도 돌파 (40~48박, E 장조) — 한 음 올라선 클라이맥스 ──
    hit(40, 1.0, 'E2')
    climb = 'B5 2 G#5 1 B5 1   C#6 1.5 B5 0.5 E6 1 D#6 1'
    string_legato('violins', line(climb, 40), 0.6, attack=0.12)
    horn_legato(line(climb, 40, shift=-12), 0.6, attack=0.12)
    for b, voicing in ((40, 'E4 G#4 B4'), (44, 'E4 G#4 C#5'), (46, 'E4 A4 C#5'), (47, 'D#4 F#4 B4')):
        for m in notes(voicing):
            trumpet(m, b, 0.7, 0.65, max_bright=0.85, attack=0.03, release=0.3)
    arpeggios(((4, 'E5 G#5 B5 E6'), (2, 'E5 G#5 C#6 E6'), (1, 'E5 A5 C#6 E6'), (1, 'D#5 F#5 B5 D#6')), 40, step=0.25, vel=0.34)
    choir_chords(40, 48, 0.4)
    celli_pulse(((4, 'E2'), (2, 'C#2'), (1, 'A1'), (1, 'B1')), 40, 0.6, 0.7)
    for b in (40, 42, 44, 45, 46, 46.5, 47, 47.5):
        taiko(b, 0.5 + 0.05 * (b - 40) / 2, eng.rng.uniform(-0.3, 0.3))
    timpani_roll(midi('B2'), 46, 2, 0.35, 0.9)
    reverse_cymbal(48, 2, 0.5)

    # ── 마디 12: 도약 (48~52박) — E 장조를 붙잡고 하프 글리산도로 위로 ──
    hit(48, 0.85, 'E2')
    for m in notes('G#5 B5 E6'):
        string_legato('violins', [(m, 48, 4.5)], 0.45, attack=0.08, release=2.5)
    for m in notes('E3 B3'):
        string_legato('celli', [(m, 48, 4)], 0.5, attack=0.08, release=2.0)
    horn_legato([(midi('E4'), 48, 4)], 0.45, release=1.5)
    horn_legato([(midi('B3'), 48, 4)], 0.42, release=1.5)
    choir(midi('G#4'), 48, 4, 0.36)
    choir(midi('B4'), 48, 4, 0.34)
    choir(midi('E5'), 48, 4, 0.3)
    harp_glissando('E4 F#4 G#4 A4 B4 C#5 D#5 E5 F#5 G#5 A5 B5 C#6 D#6 E6 F#6 G#6 B6', 49, 2.25, 0.42)
    crash(51.5, 0.25, 3.0)

    # ── 마디 13~16: 크레딧 A (52~68박) — 호른 독주 + 하프 ──
    arpeggios(((4, 'E5 G#5 B5 E6'), (4, 'D#5 F#5 B5 D#6'), (4, 'C#5 E5 G#5 C#6'), (4, 'C#5 E5 A5 C#6')), 52, vel=0.28)
    horn_legato(line(RETURN_THEME_E, 52), 0.55, attack=0.25, release=1.0)
    strings(midi('B5'), 60, 8, 0.15, attack=2.5, section='violins', bright=0.1)

    # ── 마디 17~20: 크레딧 B (68~84박) — 바이올린 선율, 첼로 스타카토, 호른 대선율 ──
    string_legato('violins', line('B5 2 G#5 1 B5 1   D#6 2 B5 2   C#6 2 A5 1 C#6 1   B5 2 A5 1 F#5 1', 68), 0.5, attack=0.2)
    horn_legato(line('E4 4   D#4 4   C#4 4   B3 4', 68), 0.42, attack=0.4)
    celli_pulse(((4, 'E2'), (4, 'G#1'), (4, 'A1'), (4, 'B1')), 68, 0.3, 0.45)
    for b, tones in ((68, ('E5', 'B5', 'E6')), (72, ('D#5', 'G#5', 'D#6')), (76, ('E5', 'A5', 'C#6')), (80, ('D#5', 'F#5', 'B5'))):
        for k, name in enumerate(tones):
            harp(midi(name), b + k * 0.25, 0.32)
    for b in (68, 72, 76, 80):
        timpani(midi('E2') if b in (68, 72) else midi('B1') + 12, b, 0.3)
    rumble(68, 15.5, 0.0, 0.5)                                                        # 51초~: 행성 균열 — 땅울림이 차오른다
    riser(80, 3.5, 0.55)
    timpani_roll(midi('E2'), 80, 3.5, 0.15, 0.8)
    reverse_cymbal(83.5, 3.5, 0.6)                                                    # 83.5~84: 반 박 숨

    # ── 마디 21~24: 크레딧 C (84~100박) — 총주로 귀환 주제 ──
    planet_blast(84)                                                                  # 63초: 행성 폭발
    finale_line = 'C#6 2 E6 1 C#6 1   D#6 2 F#6 2   D#6 2 C#6 2   A5 2 F#5 1 B5 1'
    string_legato('violins', line(finale_line, 84), 0.6, attack=0.15)
    for m, b, beats in line(finale_line, 84, shift=-12):
        trumpet(m, b, beats * 1.05, 0.62, max_bright=0.8)
    for part in voice_lead(progression_from(84, 100), 84, voices=3, low=50, high=67):
        horn_legato(part, 0.45, attack=0.15, release=0.8)
    choir_chords(84, 100, 0.34)
    celli_pulse(((4, 'A1'), (4, 'B1'), (2, 'G#1'), (2, 'C#2'), (2, 'F#1'), (2, 'B1')), 84, 0.5, 0.58)
    arpeggios(((4, 'C#5 E5 A5 C#6'), (4, 'D#5 F#5 B5 D#6'), (2, 'D#5 G#5 B5 D#6'), (2, 'C#5 E5 G#5 C#6'),
               (2, 'C#5 F#5 A5 C#6'), (2, 'D#5 F#5 B5 D#6')), 84, step=0.25, vel=0.24)
    for b in range(84, 100, 2):
        taiko(b, 0.4 if b % 4 == 0 else 0.28, 0.1)
    timpani_roll(midi('B2'), 98, 2, 0.2, 0.6)

    # ── 마디 25~27: 종지 (100~112박) — IV – iv – I ──
    string_legato('violins', line('C#6 2 C6 2 B5 8', 100), 0.5, attack=0.2, release=3.5)
    string_legato('violins', line('A5 2 A5 2 G#5 8', 100), 0.42, attack=0.2, release=3.5)
    string_legato('celli', line('A2 2 A2 2 E2 8', 100), 0.5, attack=0.2, release=3.5)
    horn_legato(line('E4 2 E4 2 E4 8', 100), 0.45, release=2.5)
    horn_legato(line('C#4 2 C4 2 B3 8', 100), 0.42, release=2.5)
    choir(midi('A4'), 100, 4, 0.32)
    choir(midi('C#5'), 100, 2, 0.3)
    choir(midi('C5'), 102, 2, 0.3)
    choir(midi('G#4'), 104, 8, 0.34)
    choir(midi('B4'), 104, 8, 0.32)
    choir(midi('E5'), 104, 8, 0.3)
    timpani_roll(midi('B1') + 12, 102.5, 1.5, 0.15, 0.5)
    timpani(midi('E2'), 104, 0.55)
    crash(104, 0.3, 5.0)
    for k, name in enumerate('E4 B4 E5 G#5 B5 E6'.split()):
        harp(midi(name), 104 + k * 0.25, 0.38 - 0.03 * k)
    strings(midi('B6'), 105, 7, 0.12, attack=2.0, section='violins', bright=0.1)    # 맨 위 반짝임


# ───────────────────────── 믹스·마스터 ─────────────────────────

def master():
    """intro_bgm.master 와 같은 체인. 덕킹 시점·페이드만 엔딩 곡에 맞춘다"""
    ir = eng.hall_ir(seconds=3.8)
    sends = {'violins': 0.38, 'violas': 0.48, 'celli': 0.22, 'brass': 0.3, 'horn': 0.5, 'choir': 0.62, 'harp': 0.32,
             'bass': 0.0, 'perc': 0.22, 'fx': 0.35}
    levels = {'violins': 1.0, 'violas': 0.85, 'celli': 0.95, 'brass': 0.88, 'horn': 0.9, 'choir': 0.85, 'harp': 0.95,
              'bass': 0.85, 'perc': 0.85, 'fx': 0.7}
    duck = eng.duck_curve([(24, 4, 0.5), (40, 4, 0.5), (48, 3, 0.5), (84, 6, 0.9)])

    bus = eng.bus
    for name, cut in (('violins', 220), ('violas', 140), ('celli', 60), ('brass', 70), ('horn', 90), ('choir', 150), ('harp', 180)):
        bus[name] = eng.highpass(bus[name], cut)
    for name in ('violins', 'violas', 'celli', 'choir', 'harp', 'horn'):
        bus[name] *= duck[:, None]

    dry = sum(bus[n] * levels[n] for n in eng.BUSES)
    send = sum(bus[n] * levels[n] * sends[n] for n in eng.BUSES)
    mix = dry + eng.reverb(send, ir) * 0.9
    mix = eng.highpass(mix, 25)
    # 인트로보다 밝고 따뜻하게: 저중역을 덜고, 공기감(8kHz 셸프)을 조금 더
    mix = eng.eq(mix, 'peak', 260, -3.0, 0.7)
    mix = eng.eq(mix, 'highshelf', 3500, 4.0, 0.7)
    mix = eng.eq(mix, 'highshelf', 9000, 1.5, 0.7)
    mix = eng.glue(mix)

    gain_db = eng.TARGET_LUFS - eng.integrated_lufs(mix)
    mix *= 10 ** (gain_db / 20)
    mix = eng.limit(mix, eng.CEILING_DBTP - 0.3)

    t = np.arange(eng.N) / eng.SR
    fade_in = np.sin(0.5 * np.pi * np.clip(t / FADE_IN, 0, 1)) ** 2
    fade_out = np.cos(0.5 * np.pi * np.clip((t - FADE_OUT_START) / (LENGTH - FADE_OUT_START), 0, 1)) ** 2
    return mix * (fade_in * fade_out)[:, None]


def main():
    root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
    parser = argparse.ArgumentParser()
    parser.add_argument('-o', '--output', default=os.path.join(root, 'Assets', 'Resources', 'Audio', 'Bgm', 'Ending.wav'))
    args = parser.parse_args()

    started = time.time()
    configure_engine()
    score()
    mix = master()
    eng.write_wav(args.output, mix)
    print(f'{args.output}')
    print(f'  길이 {LENGTH:.1f}s · {eng.integrated_lufs(mix):.1f} LUFS · True Peak {eng.true_peak_db(mix):.2f} dBTP · 렌더 {time.time() - started:.0f}s')


if __name__ == '__main__':
    main()
