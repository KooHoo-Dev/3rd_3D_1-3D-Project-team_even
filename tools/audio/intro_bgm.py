"""
인트로 크롤 BGM 합성기 (외부 샘플·라이브러리 없이 numpy/scipy 만으로 렌더)

    python tools/audio/intro_bgm.py            → Assets/Resources/Audio/Bgm/Intro.wav
    python tools/audio/intro_bgm.py -o out.wav

곡 구성 (75 BPM, 4/4 → 1박 0.8초, 1마디 3.2초). 마디 경계 시각은 IntroTimeline(C#) 의 연출 타이밍과 같다
    마디  0~1   0.0 ~  6.4초  인트로     첼로·베이스 열린 5도 → 바이올린 하모닉스 → 합창, 한 겹씩 쌓아 로고 직전까지 부풀린다
    마디  2~5   6.4 ~ 19.2초  팡파르     로고 타격. 트럼펫 주제를 먼저 홀로 → 반복(마디 4~5)에서 바이올린 대선율·첼로 진행이 더해진다
                                       I – ♭VI·♭VII (크로매틱 미디언트) – Lydian II(E/D) – I – IV – Vsus4·V
    마디  6~13 19.2 ~ 44.8초  크롤       A(6~9) 하프 오스티나토 + 비올라 패드 + 호른 선율 / B(10~13) 선율이 바이올린으로,
                                       호른은 긴 대선율, 하프 대신 첼로 스타카토, 합창·타이코가 새 겹으로 (iv 단조 차용 Gm 으로 쓸쓸한 색)
    마디 14~15 44.8 ~ 51.2초  빌드업     ♭VI–♭VII. 2박마다 한 겹씩 (첼로 8분 → 하프 16분 → 바이올린 상행·팀파니 롤)
    마디 16~17 51.2 ~ 57.6초  행성       i – VI·VII (Dm 에픽 진행). 브람 + 합창 + 첼로 오스티나토 + 호른 상행
    마디 18~19 57.6 ~ 64.0초  격추       57.6초 피격 타격(불협 브람) + 바이올린·호른 하강 선율 → V(A) 로 긴장
    마디 20    64.0초 ~       종지       화면 암전과 함께 D 장조(피카르디)로 해결 — 생존의 다짐 → 잔향

편성·레이어 (악기 층이 따로 들리게 — 영화 음악 편곡 관례)
  - 좌석 배치 패닝: 바이올린 왼쪽, 비올라 가운데, 첼로 오른쪽, 호른 왼쪽 뒤, 트럼펫 오른쪽 뒤, 하프 오른쪽, 합창 넓게
  - 음역 분리: 오스티나토는 선율과 떨어진 음역, 저음은 5도·8도만 (낮은 음정 한계 — 탁해지지 않게)
  - 리듬·아티큘레이션 대비: 긴 선율 ↔ 짧은 오스티나토 ↔ 긴 대선율. 주제는 처음엔 홀로, 반복할 때 대선율을 더한다
  - 오스티나토·스타카토는 리버브를 적게(앞), 패드·합창·호른은 많이(뒤) → 앞뒤 깊이감
  - 성부 진행: 화음이 바뀔 때 모든 음을 새로 뜯지 않는다. 공통음은 붙잡고 나머지는 반음·온음으로 옮긴다 (voice_lead)
  - 레가토: 현·호른 선율은 끊기지 않는 한 소리로 그리고 음이 바뀌는 곳만 짧게 미끄러진다 (legato) — 트럼펫·빠른 음계·스타카토만 음마다 어택
  - 페이드: 처음 2초 페이드 인, 종지 화음 뒤 잔향까지 페이드 아웃

기법 (하이브리드 오케스트라 트레일러 음악의 관례를 신스로 흉내)
  - 음원: 대역 제한 웨이브테이블 (배음 진폭을 irfft 로 한 주기 테이블로 만든다 → 에일리어싱 없음, 가산 합성보다 훨씬 빠름)
      현: 톱니 스펙트럼 + 4보이스 디튠 유니즌(슈퍼소우 계열) + 지연 비브라토
      브라스: 밝기별 테이블 4장을 시간에 따라 섞어 '부푸는' 음색 (어택에서 필터가 열리는 브라스 특유의 스웰) + 음정 스쿱
      합창: 'ah' 모음 포먼트(750/1150/2800/3500Hz) 로 배음을 깎은 테이블 + 6보이스 코러스
      하프: 밝은 테이블 → 어두운 테이블로 빠르게 넘어가며 감쇠 (뜯는 소리)
  - 타악: 피치가 떨어지는 사인 몸통 + 필터 노이즈 (타이코·팀파니·붐), 노이즈 심벌·역심벌·라이저, 브람(저음 브라스 클러스터 + 새추레이션)
  - 공간: 대역별 감쇠 시간이 다른 합성 홀 임펄스 응답으로 컨볼루션 리버브 (버스별 센드)
  - 믹스: 큰 타격 때 패드류를 잠깐 낮추는 덕킹, 마스터 글루 컴프 → BS.1770 라우드니스 -16 LUFS 로 맞춤 → True Peak -1.5 dBTP 리미터
"""

import argparse
import os
import time

import numpy as np
from scipy import signal
from scipy.io import wavfile
from scipy.ndimage import minimum_filter1d

SR = 48000
BPM = 75
BEAT = 60.0 / BPM
BAR = 4 * BEAT
END_BEAT = 80          # 마디 20 시작 = 종지 화음
TAIL = 9.0             # 종지 후 잔향 (초)
LENGTH = END_BEAT * BEAT + TAIL
N = int(LENGTH * SR)

FADE_IN = 2.0
FADE_OUT_START = END_BEAT * BEAT + 2.4   # 종지 뒤 2.4초 울린 다음 잔향 끝까지 서서히

TARGET_LUFS = -16.0
CEILING_DBTP = -1.5

TABLE = 4096
NYQUIST_LIMIT = 16000.0

rng = np.random.default_rng(20261031)
# 모든 웨이브테이블이 같은 배음 위상 표를 쓴다 → 음정이 다른 테이블을 겹쳐 섞어도(레가토 이음매) 위상이 어긋나 소리가 비지 않는다
HARMONIC_PHASES = np.random.default_rng(7).uniform(0, 2 * np.pi, 512)


# ───────────────────────── 음정 ─────────────────────────

def midi(name):
    """'D4', 'F#3', 'Bb2' → MIDI 번호"""
    steps = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
    n = steps[name[0]]
    i = 1
    while name[i] in '#b':
        n += 1 if name[i] == '#' else -1
        i += 1
    return n + 12 * (int(name[i:]) + 1)


def hz(m):
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


def notes(text):
    return [midi(x) for x in text.split()]


# ───────────────────────── 웨이브테이블 ─────────────────────────

def make_table(amps):
    """amps[k-1] = k번째 배음 진폭 → RMS 0.35 로 맞춘 한 주기 테이블 (보간용으로 끝에 첫 샘플을 붙인다)"""
    amps = np.asarray(amps, dtype=float)
    k = np.arange(1, len(amps) + 1)
    spec = np.zeros(TABLE // 2 + 1, dtype=complex)
    phases = HARMONIC_PHASES[:len(amps)]  # 고정된 무작위 위상 → 파형 꼭짓점이 뾰족하지 않아 피크가 낮다
    spec[k] = amps * (TABLE / 2) * np.exp(1j * phases)
    table = np.fft.irfft(spec, TABLE)
    rms = np.sqrt(np.mean(table ** 2))
    if rms > 0:
        table *= 0.35 / rms
    return np.append(table, table[0])


def harmonic_count(f0, limit=200):
    return max(1, min(limit, int(NYQUIST_LIMIT / f0)))


def lowpass_curve(freqs, cutoff, order=2):
    return 1.0 / np.sqrt(1.0 + (freqs / cutoff) ** (2 * order))


def saw_amps(f0, cutoff, tilt=1.0):
    k = np.arange(1, harmonic_count(f0) + 1)
    return k ** -tilt * lowpass_curve(k * f0, cutoff)


def brass_amps(f0, brightness):
    """brightness 0~1 → 필터 컷오프 350~3550Hz, 1.2kHz 부근 포먼트로 금관 특유의 '빵' 하는 색"""
    k = np.arange(1, harmonic_count(f0) + 1)
    f = k * f0
    cutoff = 350.0 + 3200.0 * brightness ** 1.5
    formant = 1.0 + 1.2 * brightness * np.exp(-((f - 1200.0) / 500.0) ** 2)
    return k ** -0.7 * lowpass_curve(f, cutoff, 3) * formant


def choir_amps(f0):
    k = np.arange(1, harmonic_count(f0) + 1)
    f = k * f0
    formants = ((750, 1.0, 130), (1150, 0.6, 170), (2800, 0.28, 260), (3500, 0.14, 320))
    shape = 0.03 + sum(g * np.exp(-((f - c) / bw) ** 2) for c, g, bw in formants)
    return k ** -0.6 * shape * lowpass_curve(f, 4500)


_cache = {}


def cached(key, build):
    if key not in _cache:
        _cache[key] = build()
    return _cache[key]


def osc(table, phase):
    pos = (phase % 1.0) * TABLE
    i = pos.astype(np.int64)
    frac = pos - i
    return table[i] * (1.0 - frac) + table[i + 1] * frac


# ───────────────────────── 엔벨로프·공통 ─────────────────────────

def adsr(dur, a, d, s, r):
    """dur 동안 A-D-S, 이후 r 동안 지수 릴리스. 길이 = dur + r"""
    t = np.arange(int((dur + r) * SR)) / SR
    held = np.minimum(t, dur)
    attack = np.clip(held / max(a, 1e-4), 0.0, 1.0) ** 0.8
    decay = 1.0 - (1.0 - s) * np.clip((held - a) / max(d, 1e-4), 0.0, 1.0)
    env = np.where(held < a, attack, decay)
    release = np.exp(-5.0 * np.clip(t - dur, 0.0, None) / max(r, 1e-4))
    tail = np.clip((t[-1] - t) / 0.01, 0.0, 1.0)  # 마지막 10ms 는 0 까지 → 끝에서 툭 끊기는 소리 방지
    return env * release * tail


def pan_gains(p):
    angle = (p + 1.0) * np.pi / 4.0
    return np.cos(angle), np.sin(angle)


def stereo(mono, p=0.0):
    left, right = pan_gains(p)
    return np.stack([mono * left, mono * right], axis=1)


def vibrato(t, cents, rate, delay):
    depth = cents * np.clip((t - delay) / 0.6, 0.0, 1.0)
    return depth * np.sin(2 * np.pi * rate * t + rng.uniform(0, 2 * np.pi))


def phase_of(f0, cents, t):
    freq = f0 * 2.0 ** (cents / 1200.0)
    return np.cumsum(freq) / SR + rng.uniform()


def lowpass(x, cutoff, order=2):
    sos = signal.butter(order, cutoff, 'low', fs=SR, output='sos')
    return signal.sosfilt(sos, x, axis=0)


def highpass(x, cutoff, order=2):
    sos = signal.butter(order, cutoff, 'high', fs=SR, output='sos')
    return signal.sosfilt(sos, x, axis=0)


def eq(x, kind, freq, gain_db, q=0.8):
    """RBJ 쿡북 바이쿼드 (peak / highshelf / lowshelf)"""
    a_gain = 10 ** (gain_db / 40)
    w = 2 * np.pi * freq / SR
    alpha = np.sin(w) / (2 * q)
    cos = np.cos(w)
    if kind == 'peak':
        b = [1 + alpha * a_gain, -2 * cos, 1 - alpha * a_gain]
        a = [1 + alpha / a_gain, -2 * cos, 1 - alpha / a_gain]
    else:
        sq = 2 * np.sqrt(a_gain) * alpha
        sign = 1 if kind == 'highshelf' else -1
        b = [a_gain * ((a_gain + 1) + sign * (a_gain - 1) * cos + sq),
             -2 * sign * a_gain * ((a_gain - 1) + sign * (a_gain + 1) * cos),
             a_gain * ((a_gain + 1) + sign * (a_gain - 1) * cos - sq)]
        a = [(a_gain + 1) - sign * (a_gain - 1) * cos + sq,
             2 * sign * ((a_gain - 1) - sign * (a_gain + 1) * cos),
             (a_gain + 1) - sign * (a_gain - 1) * cos - sq]
    return signal.lfilter(b, a, x, axis=0)


def bandpass(x, low, high, order=2):
    sos = signal.butter(order, [low, high], 'band', fs=SR, output='sos')
    return signal.sosfilt(sos, x, axis=0)


# ───────────────────────── 버스 ─────────────────────────

BUSES = ('violins', 'violas', 'celli', 'brass', 'horn', 'choir', 'harp', 'bass', 'perc', 'fx')
bus = {name: np.zeros((N, 2)) for name in BUSES}


def add(name, x, start):
    i0 = int(round(start * SR))
    if i0 >= N:
        return
    if i0 < 0:
        x = x[-i0:]
        i0 = 0
    n = min(len(x), N - i0)
    if x.ndim == 1:
        x = stereo(x)
    bus[name][i0:i0 + n] += x[:n]


def at(beat):
    """박 → 초 (사람이 연주한 듯 ±6ms 흔들림)"""
    return beat * BEAT + rng.normal(0.0, 0.006)


# ───────────────────────── 악기 ─────────────────────────

# 현 섹션: (버스, 가운데 위치, 퍼짐, 밝기 가산) — 좌석 배치대로 놓아 섹션이 따로 들리게
SECTIONS = {
    'violins': ('violins', -0.45, 0.3, 0.25),
    'violas': ('violas', 0.1, 0.45, 0.0),
    'celli': ('celli', 0.45, 0.25, -0.15),
}


def strings(m, beat, beats, vel=0.6, attack=0.35, release=1.1, section='violas', bright=None):
    bus_name, center, spread, extra = SECTIONS[section]
    f0 = hz(m)
    dur = beats * BEAT
    cutoff = 1300.0 + 2600.0 * float(np.clip((vel if bright is None else bright) + extra, 0.05, 1.0))
    table = cached(('str', m, int(cutoff / 100)), lambda: make_table(saw_amps(f0, cutoff)))
    env = adsr(dur, attack, 0.6, 0.85, release)
    t = np.arange(len(env)) / SR
    out = np.zeros((len(env), 2))
    for det, p in zip((-11, -4, 4, 11), (-1.0, -0.35, 0.35, 1.0)):
        cents = det + rng.normal(0, 1.5) + vibrato(t, 9, 5.0 + rng.uniform(-0.4, 0.4), 0.35)
        out += stereo(osc(table, phase_of(f0, cents, t)), float(np.clip(center + p * spread, -1, 1)))
    add(bus_name, out * env[:, None] * vel ** 1.3 * 0.32, at(beat))


def brass(m, beat, beats, vel=0.8, bus_name='brass', max_bright=1.0, attack=0.06, release=0.35, voices=3, pan=0.25):
    """밝기 테이블 4장을 섞어 어택에서 필터가 열리는 스웰. 음정은 아주 살짝(-6센트) 아래에서 붙는다"""
    f0 = hz(m)
    dur = beats * BEAT
    levels = (0.0, 0.33, 0.66, 1.0)
    tables = [cached(('brass', m, b), lambda b=b: make_table(brass_amps(f0, b))) for b in levels]
    env = adsr(dur, attack, 0.5, 0.8, release)
    t = np.arange(len(env)) / SR
    swell = np.clip(t / (attack * 3.0 + 0.12), 0.0, 1.0) ** 0.6 * (0.85 + 0.15 * np.exp(-t / 0.4))
    b = np.clip(vel * max_bright * swell * env ** 0.5, 0.0, 1.0) * (len(levels) - 1)
    lo = np.clip(b.astype(int), 0, len(levels) - 2)
    w = b - lo
    scoop = -6.0 * np.exp(-t / 0.03)
    out = np.zeros((len(env), 2))
    dets = np.linspace(-6, 6, voices) if voices > 1 else [0.0]
    for i, det in enumerate(dets):
        ph = phase_of(f0, det + scoop + vibrato(t, 7, 5.3, 0.5), t)
        waves = [osc(tb, ph) for tb in tables]
        mono = np.zeros(len(env))
        for j in range(len(levels) - 1):
            sel = lo == j
            mono[sel] = waves[j][sel] * (1 - w[sel]) + waves[j + 1][sel] * w[sel]
        p = pan + (i / max(voices - 1, 1) - 0.5) * 0.3
        out += stereo(mono, p)
    loud = 0.55 + 0.45 * b / (len(levels) - 1)
    add(bus_name, out * (env * loud)[:, None] * vel ** 1.2 * 0.3, at(beat))


def trumpet(m, beat, beats, vel=0.8, **kw):
    """트럼펫: 밝은 브라스, 오른쪽 뒤"""
    brass(m, beat, beats, vel, **kw)


def horn(m, beat, beats, vel=0.55):
    """호른: 어두운 브라스, 왼쪽 뒤"""
    brass(m, beat, beats, vel, bus_name='horn', max_bright=0.45, attack=0.18, release=0.6, voices=2, pan=-0.35)


def choir(m, beat, beats, vel=0.5):
    f0 = hz(m)
    dur = beats * BEAT
    table = cached(('choir', m), lambda: make_table(choir_amps(f0)))
    env = adsr(dur, 1.2, 1.0, 0.9, 1.6)
    t = np.arange(len(env)) / SR
    out = np.zeros((len(env), 2))
    for i in range(6):
        det = rng.uniform(-14, 14)
        cents = det + vibrato(t, 14, rng.uniform(4.6, 6.0), 0.2)
        out += stereo(osc(table, phase_of(f0, cents, t)), rng.uniform(-0.8, 0.8))
    add('choir', out * env[:, None] * vel * 0.17, at(beat))


def harp(m, beat, vel=0.45, p=0.5):
    """뜯는 소리: 밝은 테이블 → 어두운 테이블로 빠르게 넘어가며 감쇠"""
    f0 = hz(m)
    bright = cached(('harp_b', m), lambda: make_table(saw_amps(f0, 7500, 1.2)))
    dark = cached(('harp_d', m), lambda: make_table(saw_amps(f0, 700, 1.6)))
    length = int(2.2 * SR)
    t = np.arange(length) / SR
    ph = phase_of(f0, rng.normal(0, 2), t)
    fade = np.exp(-t / 0.12)
    mono = osc(bright, ph) * fade + osc(dark, ph) * (1 - fade)
    env = np.exp(-t / 0.9) * np.clip(t / 0.003, 0, 1)
    add('harp', stereo(mono * env * vel * 0.5, p), at(beat))


def celli_staccato(m, beat, beats=0.5, vel=0.6):
    """첼로 스타카토 (오스티나토용, 오른쪽)"""
    f0 = hz(m)
    table = cached(('low', m), lambda: make_table(saw_amps(f0, 1600)))
    length = int((beats * BEAT + 0.3) * SR)
    t = np.arange(length) / SR
    env = np.clip(t / 0.012, 0, 1) * np.exp(-t / (beats * BEAT * 0.45))
    out = np.zeros((length, 2))
    for det, p in ((-7, 0.3), (0, 0.45), (7, 0.6)):
        out += stereo(osc(table, phase_of(f0, det, t)), p)
    add('celli', out * env[:, None] * vel * 0.3, at(beat))


def bass(m, beat, beats, vel=0.6):
    f0 = hz(m)
    table = cached(('bass', m), lambda: make_table([1.0, 0.35, 0.12, 0.05]))
    env = adsr(beats * BEAT, 0.08, 0.5, 0.9, 0.8)
    t = np.arange(len(env)) / SR
    add('bass', stereo(osc(table, phase_of(f0, 0, t)) * env * vel * 0.55), at(beat))


# ───────────────────────── 레가토·성부 진행 ─────────────────────────
#
# 실제 편곡처럼 화음이 바뀔 때 모든 음을 새로 뜯지 않는다.
#  - 성부 진행(voice leading): 화음마다 4성부 배치를 고를 때 앞 화음에서 가장 적게 움직이는 배치를 고른다
#    → 공통음은 같은 성부가 그대로 붙잡고(이음줄), 나머지는 반음·온음으로 옮겨 간다. 병행 5도·8도와 큰 도약은 벌점
#  - 레가토 렌더: 한 성부(또는 선율)를 끊지 않는 하나의 소리로 그린다. 음이 바뀌는 곳만 짧게(50~70ms) 미끄러지고
#    (현악 목업의 슬러·포르타멘토 전이), 활 바꿈처럼 음량이 아주 살짝 숨을 쉰다. 비브라토는 음마다 늦게 피어난다
#  - 음정에 따라 배음 테이블이 바뀌는 구간은 같은 위상 표를 쓰는 테이블끼리 겹쳐 섞어 이음매가 들리지 않게 한다

PITCH_CLASS = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}


def pitch_class(name):
    n = PITCH_CLASS[name[0]]
    for c in name[1:]:
        n += 1 if c == '#' else -1
    return n % 12


def smooth01(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def dynamics(points):
    """[(박, 크기)] 를 잇는 셈여림 곡선 → 초를 받는 함수 (크레셴도·디미누엔도를 음마다가 아니라 흐름으로)"""
    beats, gains = zip(*points)
    return lambda seconds: np.interp(seconds / BEAT, beats, gains)


def legato(bus_name, events, table_for, center=0.0, spread=0.3, dets=(-8, -3, 3, 8), attack=0.3, release=1.0,
           glide=0.06, vib=(9.0, 5.2), dyn=None, gain=0.3, dip=0.1):
    """
    events: [(midi, 시작 박, 길이 박)]. 같은 음이 이어지면 묶고(이음줄), 0.05박보다 벌어지면 새 프레이즈.
    한 프레이즈는 위상이 끊기지 않는 하나의 소리 — 음이 바뀌는 곳만 glide 초 동안 미끄러진다
    """
    phrases, cur = [], []
    for m, beat, beats in sorted(events, key=lambda e: e[1]):
        if cur and beat - (cur[-1][1] + cur[-1][2]) > 0.05:
            phrases.append(cur)
            cur = []
        if cur and cur[-1][0] == m:
            cur[-1] = (m, cur[-1][1], beat + beats - cur[-1][1])
            continue
        cur.append((m, beat, beats))
    if cur:
        phrases.append(cur)

    for phrase in phrases:
        start = phrase[0][1] * BEAT
        dur = (phrase[-1][1] + phrase[-1][2]) * BEAT - start
        env = adsr(dur, attack, 0.5, 0.9, release)
        n = len(env)
        t = np.arange(n) / SR
        bounds = [e[1] * BEAT - start for e in phrase[1:]]

        pitch = np.full(n, float(phrase[0][0]))
        since = t.copy()                      # 지금 음이 시작된 뒤 지난 시간 (비브라토가 음마다 늦게 피어나게)
        for k, b in enumerate(bounds):
            pitch += (phrase[k + 1][0] - phrase[k][0]) * smooth01((t - b) / glide)
            since = np.where(t >= b, t - b, since)
            env *= 1.0 - dip * np.exp(-((t - b - 0.02) / 0.05) ** 2)
        weights = []
        for k in range(len(phrase)):
            on = smooth01((t - bounds[k - 1]) / glide) if k > 0 else 1.0
            off = 1.0 - smooth01((t - bounds[k]) / glide) if k < len(bounds) else 1.0
            weights.append(on * off)
        if dyn is not None:
            env = env * dyn(start + t)
        depth = vib[0] * np.clip((since - 0.25) / 0.5, 0.0, 1.0)

        out = np.zeros((n, 2))
        for i, det in enumerate(dets):
            cents = det + rng.normal(0, 1.0) + depth * np.sin(2 * np.pi * (vib[1] + rng.uniform(-0.3, 0.3)) * t + rng.uniform(0, 6.28))
            phase = np.cumsum(hz(pitch) * 2.0 ** (cents / 1200.0)) / SR + rng.uniform()
            mono = np.zeros(n)
            for k, (m, _, _) in enumerate(phrase):
                w = np.broadcast_to(weights[k], (n,))
                idx = np.nonzero(w > 1e-4)[0]
                mono[idx] += w[idx] * osc(table_for(m), phase[idx])
            p = center + (i / max(len(dets) - 1, 1) - 0.5) * 2.0 * spread
            out += stereo(mono, float(np.clip(p, -1, 1)))
        add(bus_name, out * (env * gain)[:, None], start)


def string_table(section, vel):
    _, _, _, extra = SECTIONS[section]
    cutoff = 1300.0 + 2600.0 * float(np.clip(vel + extra, 0.05, 1.0))
    return lambda m: cached(('str', m, int(cutoff / 100)), lambda: make_table(saw_amps(hz(m), cutoff)))


def string_legato(section, events, vel, attack=0.3, release=1.0, dyn=None, glide=0.06):
    bus_name, center, spread, _ = SECTIONS[section]
    legato(bus_name, events, string_table(section, vel), center, spread, (-11, -4, 4, 11), attack, release, glide,
           (9.0, 5.0), dyn, vel ** 1.3 * 0.32)


def horn_legato(events, vel, attack=0.18, release=0.7, dyn=None):
    table_for = lambda m: cached(('horn_l', m), lambda: make_table(brass_amps(hz(m), 0.38)))
    legato('horn', events, table_for, -0.35, 0.12, (-5, 5), attack, release, 0.07, (6.0, 5.0), dyn, vel ** 1.2 * 0.3)


def bass_legato(events, vel, dyn=None):
    table_for = lambda m: cached(('bass', m), lambda: make_table([1.0, 0.35, 0.12, 0.05]))
    # 베이스는 근음이 크게 뛰므로 미끄러지지 않고(15ms) 활만 바꾸듯 살짝 다시 긋는다
    legato('bass', events, table_for, 0.0, 0.0, (0,), 0.08, 0.8, 0.015, (0.0, 5.0), dyn, vel * 0.55, dip=0.25)


def line(text, start_beat, shift=0):
    """'음 길이(박)' 표기 → events. 음 이름 '-' 는 쉼표"""
    tokens = text.split()
    events, beat = [], start_beat
    for i in range(0, len(tokens), 2):
        name, beats = tokens[i], float(tokens[i + 1])
        if name != '-':
            events.append((midi(name) + shift, beat, beats))
        beat += beats
    return events


def voice_lead(progression, start_beat, voices=4, low=55, high=74):
    """
    progression: [(박 수, '화음 음 이름(옥타브 없이)')] → 성부별 events.
    화음마다 음역 안의 모든 배치 중 앞 배치에서 가장 덜 움직이는 것을 고른다 (공통음 유지·순차 진행·병행 5/8도 벌점)
    """
    from itertools import combinations
    parts = [[] for _ in range(voices)]
    prev = None
    beat = start_beat
    for beats, names in progression:
        pcs = {pitch_class(x) for x in names.split()}
        pool = [m for m in range(low, high + 1) if m % 12 in pcs]
        best, best_cost = None, None
        for combo in combinations(pool, voices):
            if len(pcs) <= voices and {m % 12 for m in combo} != pcs:
                continue
            if any(b - a > 12 for a, b in zip(combo, combo[1:])):
                continue
            if prev is None:
                cost = abs(np.mean(combo) - (low + high) / 2) + (combo[-1] - combo[0]) * 0.1
            else:
                moves = [c - p for c, p in zip(combo, prev)]
                cost = sum(abs(d) for d in moves) + 2.0 * sum(abs(d) > 4 for d in moves)
                for i in range(voices):
                    for j in range(i + 1, voices):
                        before, after = (prev[j] - prev[i]) % 12, (combo[j] - combo[i]) % 12
                        if before == after and after in (0, 7) and moves[i] != 0 and moves[i] * moves[j] > 0:
                            cost += 4.0
            if best_cost is None or cost < best_cost:
                best, best_cost = combo, cost
        for v, m in enumerate(best):
            parts[v].append((m, beat, beats))
        prev = best
        beat += beats
    return parts


# ───────────────────────── 타악·효과 ─────────────────────────

def noise(length, channels=1):
    x = rng.standard_normal((length, channels)) if channels > 1 else rng.standard_normal(length)
    return x


def taiko(beat, vel=0.8, p=0.0):
    length = int(1.4 * SR)
    t = np.arange(length) / SR
    freq = 52.0 + 75.0 * np.exp(-t / 0.045)
    body = np.sin(2 * np.pi * np.cumsum(freq) / SR) * np.exp(-t / 0.38)
    skin = lowpass(noise(length), 900) * np.exp(-t / 0.05) * 0.5
    click = highpass(noise(length), 2500) * np.exp(-t / 0.004) * 0.08
    mono = np.tanh(1.6 * (body + skin + click)) * vel * 0.8
    add('perc', stereo(mono, p), beat * BEAT)


def timpani(m, beat, vel=0.7):
    f0 = hz(m)
    length = int(3.0 * SR)
    t = np.arange(length) / SR
    mono = np.zeros(length)
    for ratio, amp, decay in ((1.0, 1.0, 1.4), (1.504, 0.5, 0.9), (1.742, 0.3, 0.7), (2.0, 0.25, 0.6), (2.245, 0.12, 0.45)):
        mono += amp * np.sin(2 * np.pi * f0 * ratio * t + rng.uniform(0, 6.28)) * np.exp(-t / decay)
    mono += lowpass(noise(length), 1500) * np.exp(-t / 0.025) * 0.6
    mono *= np.clip(t / 0.002, 0, 1)
    add('perc', stereo(mono * vel * 0.35, -0.15), beat * BEAT)


def timpani_roll(m, beat, beats, v0, v1):
    hits = int(beats * BEAT * 13)
    for i in range(hits):
        k = i / max(hits - 1, 1)
        timpani(m, beat + k * beats + rng.normal(0, 0.01), (v0 + (v1 - v0) * k ** 1.5) * 0.6)


def boom(beat, vel=1.0):
    """시네마틱 붐: 60→28Hz 로 떨어지는 서브 + 짧은 몸통"""
    length = int(3.5 * SR)
    t = np.arange(length) / SR
    freq = 28.0 + 40.0 * np.exp(-t / 0.35)
    sub = np.sin(2 * np.pi * np.cumsum(freq) / SR) * np.exp(-t / 1.4)
    thump = lowpass(noise(length), 300) * np.exp(-t / 0.08) * 0.8
    mono = np.tanh(1.3 * (sub + thump)) * np.clip(t / 0.003, 0, 1)
    add('perc', stereo(mono * vel * 0.6), beat * BEAT)


def crash(beat, vel=0.6, length_s=4.5):
    length = int(length_s * SR)
    t = np.arange(length) / SR
    x = highpass(noise(length, 2), 3200, 3)
    x += bandpass(noise(length, 2), 6000, 11000) * 0.6
    env = np.exp(-t / 1.5) * np.clip(t / 0.002, 0, 1)
    add('fx', x * env[:, None] * vel * 0.12, beat * BEAT)


def reverse_cymbal(end_beat, beats=4, vel=0.5):
    length = int(beats * BEAT * SR)
    t = np.arange(length) / SR
    x = highpass(noise(length, 2), 3000, 3) + bandpass(noise(length, 2), 5000, 10000) * 0.5
    env = (t / t[-1]) ** 3
    add('fx', x * env[:, None] * vel * 0.13, end_beat * BEAT - length / SR)


def riser(beat, beats, vel=0.6, f_lo=250.0, f_hi=7000.0):
    """대역 통과 노이즈를 블록 단위로 올리며 스윕 + 아래에서 올라오는 사인 글리산도"""
    length = int(beats * BEAT * SR)
    x = noise(length, 2)
    out = np.zeros_like(x)
    block = 1024
    zi = None
    for s in range(0, length, block):
        k = s / length
        center = f_lo * (f_hi / f_lo) ** k
        sos = signal.butter(2, [center / 1.4, min(center * 1.4, SR / 2 - 100)], 'band', fs=SR, output='sos')
        if zi is None:
            zi = np.zeros((sos.shape[0], 2, 2))
        out[s:s + block], zi = signal.sosfilt(sos, x[s:s + block], axis=0, zi=zi)
    t = np.arange(length) / SR
    k = t / t[-1]
    gliss = np.sin(2 * np.pi * np.cumsum(hz(50) * 2 ** (3.0 * k)) / SR) * 0.15
    env = k ** 2.2
    add('fx', (out * 0.35 + gliss[:, None]) * env[:, None] * vel, beat * BEAT)


def braam(chord, beat, vel=1.0, beats=6):
    """저음 브라스 클러스터를 최대 밝기로 + 새추레이션 (트레일러의 '브람')"""
    start = len(bus['brass'])
    tmp = bus['brass']
    bus['brass'] = np.zeros((N, 2))
    for m in chord:
        brass(m, beat, beats, vel, max_bright=1.0, attack=0.04, release=1.5, voices=5, pan=0.0)
    hit = bus['brass']
    bus['brass'] = tmp
    i0 = int(beat * BEAT * SR)
    seg = hit[i0:]
    seg = np.tanh(seg * 2.2) / 1.6
    seg = lowpass(seg, 2800)
    bus['brass'][i0:] += seg[:start - i0]
    boom(beat, vel * 0.8)


def impact(beat, vel=1.0):
    boom(beat, vel)
    taiko(beat, vel)
    crash(beat, vel * 0.8)
    timpani(midi('D2'), beat, vel)


# ───────────────────────── 악보 ─────────────────────────

def ostinato(chord_tones, beat, beats, step=0.5, vel=0.4, pattern=(0, 1, 2, 3, 2, 1, 2, 1)):
    """하프 아르페지오 (오른쪽, 음마다 살짝 좌우로)"""
    ms = notes(chord_tones)
    count = int(round(beats / step))
    for i in range(count):
        accent = 1.0 if i % 4 == 0 else 0.8
        harp(ms[pattern[i % len(pattern)] % len(ms)], beat + i * step, vel * accent, p=0.62 if i % 2 else 0.38)


# 곡 전체 화성 (8박 ~ 86박). 비올라 4성부·베이스·팡파르 호른이 모두 이 진행을 성부 진행으로 따라간다
HARMONY = (
    # 팡파르 (마디 2~5): I – ♭VI·♭VII – II/I(Lydian) – I – IV – Vsus4·V
    (4, 'D F# A', 'D2'), (2, 'Bb D F', 'Bb1'), (2, 'C E G', 'C2'), (2, 'E G# B', 'D2'),
    (2, 'D F# A', 'D2'), (2, 'G B D', 'G1'), (1, 'A D E', 'A1'), (1, 'A C# E', 'A1'),
    # 크롤 A (마디 6~9): I – IV/I – vi(Bm) – Vsus4·V
    (4, 'D F# A', 'D2'), (4, 'G B D', 'D2'), (4, 'B D F#', 'B1'), (2, 'A D E', 'A1'), (2, 'A C# E', 'A1'),
    # 크롤 B (마디 10~13): I – iii – IV·iv(단조 차용) – Vsus4·V7
    (4, 'D F# A', 'D2'), (4, 'F# A C#', 'F#1'), (2, 'G B D', 'G1'), (2, 'G Bb D', 'G1'), (2, 'A D E', 'A1'), (2, 'A C# E G', 'A1'),
    # 빌드업 (마디 14~15): ♭VI – ♭VII
    (4, 'Bb D F', 'Bb1'), (4, 'C E G', 'C2'),
    # 행성 (마디 16~17): i – VI·VII
    (4, 'D F A', 'D2'), (2, 'Bb D F', 'Bb1'), (2, 'C E G', 'C2'),
    # 격추 (마디 18~19): iv/i – VI – V7
    (4, 'G Bb D', 'D2'), (2, 'Bb D F', 'Bb1'), (2, 'A C# E G', 'A1'),
    # 종지 (마디 20): I (피카르디)
    (6, 'D F# A', 'D2'),
)
HARMONY_START = 8


def score():
    progression = [(beats, names) for beats, names, _ in HARMONY]

    # ── 바탕: 비올라 4성부 (곡 내내 성부 진행으로 이어진다) + 베이스 ──
    viola_dyn = dynamics([(8, 0.55), (22, 0.55), (24, 0.46), (38, 0.46), (40, 0.52), (54, 0.52), (56, 0.5), (63.5, 0.66),
                          (64, 0.52), (71.5, 0.56), (72, 0.6), (80, 0.6), (86, 0.55)])
    for part in voice_lead(progression, HARMONY_START, voices=4, low=55, high=74):
        string_legato('violas', part, 1.0, attack=0.12, release=3.0, dyn=viola_dyn)
    bass_events = [(midi('D2'), 0, 8)]
    beat = HARMONY_START
    for beats, _, root in HARMONY:
        bass_events.append((midi(root), beat, beats))
        beat += beats
    bass_legato(bass_events, 1.0, dynamics([(0, 0.4), (8, 0.62), (24, 0.5), (40, 0.55), (56, 0.6), (64, 0.62), (86, 0.65)]))

    # ── 마디 0~1: 인트로 (0~8박) — 한 겹씩 ──
    string_legato('celli', [(midi('D3'), 0, 8.5)], 0.4, attack=2.5, release=1.5)          # 열린 5도 (저음은 5도·8도만)
    string_legato('celli', [(midi('A3'), 0.5, 8)], 0.35, attack=2.5, release=1.5)
    strings(midi('A5'), 2, 6, 0.22, attack=2.0, section='violins', bright=0.15)
    strings(midi('E6'), 2.5, 5.5, 0.18, attack=2.0, section='violins', bright=0.15)
    choir(midi('A4'), 4, 4.5, 0.3)
    reverse_cymbal(8, beats=6, vel=0.7)
    riser(2, 6, 0.45)

    # ── 마디 2~5: 팡파르 (8~24박). 8박 = 6.4초 로고 타격 ──
    impact(8, 1.0)
    # 호른 3성부: 화음을 성부 진행으로 받친다 (왼쪽 뒤)
    for part in voice_lead(progression[:8], 8, voices=3, low=50, high=67):
        horn_legato(part, 0.42, attack=0.1, release=0.8)
    # 트럼펫 주제 (혀로 끊어 부는 금관이라 음마다 어택) — 마디 2~3 은 홀로
    theme = 'A4 1.5 D5 0.5 E5 1 F#5 1   F5 1.5 D5 0.5 E5 1.5 G5 0.5   G#5 2 F#5 1.5 E5 0.5   D5 1 F#5 1 E5 2'
    for m, b, beats in line(theme, 8):
        trumpet(m, b, beats * 1.05, 0.9)
    # 반복 구간(마디 4~5): 바이올린 대선율(길게, 위) + 첼로 진행(4분, 아래) — 둘 다 레가토
    string_legato('violins', line('B5 2 A5 2   B5 2 A5 1 C#6 1', 16), 0.45, attack=0.15)
    string_legato('celli', line('D3 1 E3 1 F#3 1 G3 1   G3 1 F#3 1 E3 1 C#3 1', 16), 0.5, attack=0.08, release=0.6)
    for b in (12, 16, 20):
        taiko(b, 0.6)
    for b in (10, 14, 18, 22):
        taiko(b, 0.4, 0.2)
    timpani_roll(midi('A2'), 22.5, 1.5, 0.3, 0.7)
    crash(16, 0.4)

    # ── 마디 6~9: 크롤 A (24~40박) — 하프 오스티나토(높게) + 비올라(가운데) + 호른 선율(아래) ──
    beat = 24
    for beats, arp in ((4, 'D5 F#5 A5 D6'), (4, 'D5 G5 B5 D6'), (4, 'D5 F#5 B5 D6'), (2, 'E5 A5 D6 E6'), (2, 'E5 A5 C#6 E6')):
        ostinato(arp, beat, beats, vel=0.38)
        beat += beats
    horn_legato(line('F#4 2 E4 1 D4 1   G4 3 F#4 1   F#4 2 D4 1 B3 1   E4 2 C#4 1 E4 1', 24), 0.62)

    # ── 마디 10~13: 크롤 B (40~56박) — 선율은 바이올린, 호른은 긴 대선율, 하프 대신 첼로 스타카토 + 합창·타이코 ──
    beat = 40
    for beats, low, roll in ((4, 'D3', ('D5', 'A5', 'D6')), (4, 'F#2', ('F#5', 'C#6', 'F#6')), (2, 'G2', ('G5', 'D6', 'G6')),
                             (2, 'G2', ()), (2, 'A2', ('A5', 'E6', 'A6')), (2, 'A2', ())):
        for k in range(int(beats * 2)):
            celli_staccato(midi(low) + (12 if k % 2 else 0), beat + k * 0.5, 0.5, 0.42 if k % 2 == 0 else 0.32)
        for k, name in enumerate(roll):     # 하프는 화음 첫 박 아르페지오만 남긴다
            harp(midi(name), beat + k * 0.25, 0.4)
        beat += beats
    string_legato('violins', line('A5 2 F#5 1 A5 1   C#6 3 B5 1   B5 2 Bb5 2   A5 2 G5 1 E5 1', 40), 0.5, attack=0.2)
    horn_legato(line('D4 4   C#4 4   D4 4   E4 2 C#4 2', 40), 0.5)
    choir(midi('F#4'), 48, 4, 0.22)
    choir(midi('A4'), 48, 8, 0.24)
    reverse_cymbal(40, 4, 0.3)
    for b in range(40, 56, 4):
        taiko(b, 0.32)
        taiko(b + 2.5, 0.2, 0.15)

    # ── 마디 14~15: 빌드업 (56~64박) — 2박마다 한 겹씩 ──
    for k in range(16):                                                        # 56: 첼로 8분
        celli_staccato(midi('Bb2') if k < 8 else midi('C3'), 56 + k * 0.5, 0.5, 0.45 + 0.02 * k)
    ostinato('D5 F5 Bb5 D6', 58, 2, step=0.25, vel=0.4)                        # 58: 하프 16분
    ostinato('E5 G5 C6 E6', 60, 4, step=0.25, vel=0.48)
    run = 'C4 D4 E4 F4 G4 A4 B4 C5 D5 E5 F5 G5 A5 B5 C6 D6'.split()          # 60: 바이올린 상행 (빠른 음계는 활을 따로)
    for k, name in enumerate(run):
        strings(midi(name), 60 + k * 0.25, 0.35, 0.35 + 0.02 * k, attack=0.02, release=0.3, section='violins')
    timpani_roll(midi('D2'), 60, 4, 0.2, 1.0)                                 # 60: 팀파니 롤
    for b, voicing in ((56, 'D4 F4 Bb4'), (60, 'E4 G4 C5')):                   # 화음 첫 박 트럼펫 찌르기
        for m in notes(voicing):
            trumpet(m, b, 0.6, 0.6, max_bright=0.8, attack=0.03, release=0.25)
    riser(56, 8, 0.6)
    reverse_cymbal(64, 4, 0.6)

    # ── 마디 16~17: 행성 (64~72박) — i – VI·VII ──
    braam(notes('D2 A2 D3 A3'), 64, 0.9)
    crash(64, 0.5)
    choir(midi('D4'), 64, 8, 0.45)
    choir(midi('F4'), 64, 4, 0.4)
    choir(midi('A4'), 64, 4, 0.35)
    choir(midi('F4'), 68, 2, 0.4)
    choir(midi('G4'), 70, 2, 0.4)
    pulse = 'D3 D3 D3 D3 F3 D3 E3 D3'.split()
    for k in range(16):
        celli_staccato(midi(pulse[k % 8]), 64 + k * 0.5, 0.5, 0.6 if k % 4 == 0 else 0.45)
    for b in (64, 66, 67.5, 68, 70, 71, 71.5):
        taiko(b, 0.7 if b % 2 == 0 else 0.5, rng.uniform(-0.3, 0.3))
    horn_legato(line('A3 2 Bb3 2 C4 2', 66), 0.55)
    string_legato('violins', [(midi('A5'), 64, 8)], 0.3, attack=1.5)              # 높은 지속음 — 긴장
    riser(69, 3, 0.5, 400, 9000)

    # ── 마디 18~19: 격추 (72~80박). 72박 = 57.6초 피격 ──
    braam(notes('D2 A2 Eb3'), 72, 1.0)
    impact(72, 1.0)
    choir(midi('Eb5'), 72, 4, 0.35)
    choir(midi('D5'), 72, 4, 0.35)
    fall = 'D6 1 C6 1 Bb5 1 A5 1 G5 1 F5 1 E5 1 C#5 1'
    string_legato('violins', line(fall, 72), 0.45, attack=0.08)
    horn_legato(line(fall, 72, shift=-24), 0.5)
    for b in (74, 76, 77, 78, 79, 79.5):
        taiko(b, 0.55, rng.uniform(-0.3, 0.3))
    timpani_roll(midi('A2'), 78, 2, 0.3, 0.9)
    reverse_cymbal(80, 3, 0.55)

    # ── 마디 20: 종지 (80박 = 64.0초) — D 장조(피카르디)로 해결, 섹션 전체가 함께 ──
    impact(80, 0.9)
    for m in notes('A4 D5 F#5'):
        string_legato('violins', [(m, 80, 6)], 0.55, attack=0.1, release=3.5)
    for m in notes('D3 A3'):
        string_legato('celli', [(m, 80, 6)], 0.6, attack=0.1, release=3.5)
    for m in notes('D4 F#4 A4'):
        trumpet(m, 80, 4, 0.75, max_bright=0.8, release=2.5)
    horn_legato([(midi('D3'), 80, 6), ], 0.6, release=2.5)
    horn_legato([(midi('A3'), 80, 6), ], 0.6, release=2.5)
    choir(midi('A4'), 80, 6, 0.45)
    choir(midi('D5'), 80, 6, 0.4)
    harp(midi('D5'), 80.0, 0.5)
    harp(midi('A5'), 80.25, 0.45)
    harp(midi('D6'), 80.5, 0.4)


# ───────────────────────── 믹스·마스터 ─────────────────────────

def hall_ir(seconds=3.4, predelay=0.025):
    """대역마다 감쇠 시간이 다른 합성 홀 IR (저역 길게, 고역 짧게) + 초기 반사"""
    length = int(seconds * SR)
    t = np.arange(length) / SR
    ir = np.zeros((length, 2))
    for (lo, hi), rt60 in (((20, 400), 3.2), ((400, 3000), 2.6), ((3000, 16000), 1.3)):
        band = bandpass(noise(length, 2), lo, hi)
        ir += band * np.exp(-6.9 * t / rt60)[:, None]
    for _ in range(14):
        d = int(rng.uniform(0.008, 0.07) * SR)
        ir[d] += rng.uniform(-0.6, 0.6, 2)
    ir *= np.clip(t / 0.03, 0, 1)[:, None]
    ir = np.concatenate([np.zeros((int(predelay * SR), 2)), ir])
    return ir / np.sqrt(np.sum(ir ** 2) / 2)


def reverb(x, ir):
    wet = np.stack([signal.fftconvolve(x[:, c], ir[:, c])[:len(x)] for c in range(2)], axis=1)
    return highpass(wet, 180)


def duck_curve(hits):
    """큰 타격 직후 패드류를 잠깐 낮춘다 (사이드체인 대용)"""
    gain = np.ones(N)
    t = np.arange(N) / SR
    for beat, db, recover in hits:
        t0 = beat * BEAT
        mask = t >= t0
        depth = 1.0 - 10 ** (-db / 20)
        dt = t[mask] - t0
        gain[mask] *= 1.0 - depth * np.clip(dt / 0.005, 0, 1) * np.exp(-dt / recover)
    return gain


def k_weighted(x):
    """ITU-R BS.1770 K 가중 (48kHz 계수)"""
    x = signal.lfilter([1.53512485958697, -2.69169618940638, 1.19839281085285],
                       [1.0, -1.69065929318241, 0.73248077421585], x, axis=0)
    return signal.lfilter([1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621], x, axis=0)


def integrated_lufs(x):
    y = k_weighted(x)
    block, hop = int(0.4 * SR), int(0.1 * SR)
    powers = np.array([np.sum(np.mean(y[s:s + block] ** 2, axis=0)) for s in range(0, len(y) - block, hop)])
    loud = -0.691 + 10 * np.log10(powers + 1e-12)
    gated = powers[loud > -70]
    rel = -0.691 + 10 * np.log10(np.mean(gated)) - 10
    gated = powers[(loud > -70) & (loud > rel)]
    return -0.691 + 10 * np.log10(np.mean(gated))


def true_peak_db(x):
    up = signal.resample_poly(x, 4, 1, axis=0)
    return 20 * np.log10(np.max(np.abs(up)) + 1e-12)


def glue(x, threshold_db=-14.0, ratio=2.0):
    """RMS 기반 2:1 버스 컴프 (어택 15ms, 릴리스 250ms)"""
    level = np.sqrt(lowpass(np.mean(x ** 2, axis=1), 8, 1).clip(1e-10))
    db = 20 * np.log10(level)
    reduction = np.minimum(0.0, (threshold_db - db) * (1 - 1 / ratio))
    gain = 10 ** (reduction / 20)
    a_att, a_rel = np.exp(-1 / (0.015 * SR)), np.exp(-1 / (0.25 * SR))
    smooth = np.empty_like(gain)
    g = 1.0
    for i in range(0, len(gain), 64):     # 64샘플 블록 단위로 충분
        target = gain[i]
        coef = a_att ** 64 if target < g else a_rel ** 64
        g = target + (g - target) * coef
        smooth[i:i + 64] = g
    return x * smooth[:, None]


def limit(x, ceiling_db):
    """룩어헤드 피크 리미터 (4배 오버샘플 피크 기준)"""
    ceiling = 10 ** (ceiling_db / 20)
    up = np.abs(signal.resample_poly(x, 4, 1, axis=0)).max(axis=1)
    peak = up[:len(up) // 4 * 4].reshape(-1, 4).max(axis=1)
    peak = np.pad(peak, (0, len(x) - len(peak)), constant_values=0)
    gain = np.minimum(1.0, ceiling / np.maximum(peak, 1e-9))
    lookahead = int(0.003 * SR)
    gain = minimum_filter1d(gain, size=lookahead * 2 + 1)
    gain = np.convolve(gain, np.ones(lookahead) / lookahead, mode='same')
    return x * gain[:, None]


def master():
    ir = hall_ir()
    # 오스티나토·스타카토(하프·첼로)는 리버브를 적게 → 앞에, 패드·합창·호른은 많이 → 뒤에
    sends = {'violins': 0.35, 'violas': 0.45, 'celli': 0.2, 'brass': 0.28, 'horn': 0.45, 'choir': 0.6, 'harp': 0.28,
             'bass': 0.0, 'perc': 0.22, 'fx': 0.35}
    levels = {'violins': 1.0, 'violas': 0.85, 'celli': 0.95, 'brass': 0.9, 'horn': 0.85, 'choir': 0.85, 'harp': 0.9,
              'bass': 0.9, 'perc': 0.9, 'fx': 0.7}
    duck = duck_curve([(8, 5, 0.5), (64, 4, 0.5), (72, 5, 0.6), (80, 4, 0.5)])

    # 저음은 bass·타악이 맡고, 화음 악기는 저역을 비워 탁해지지 않게
    # 섹션마다 아래 대역을 비워 자기 음역에 머물게 (바이올린 위 · 비올라 가운데 · 첼로 아래)
    for name, cut in (('violins', 220), ('violas', 140), ('celli', 60), ('brass', 70), ('horn', 90), ('choir', 150), ('harp', 180)):
        bus[name] = highpass(bus[name], cut)
    for name in ('violins', 'violas', 'celli', 'choir', 'harp', 'horn'):
        bus[name] *= duck[:, None]

    dry = sum(bus[n] * levels[n] for n in BUSES)
    send = sum(bus[n] * levels[n] * sends[n] for n in BUSES)
    mix = dry + reverb(send, ir) * 0.9
    mix = highpass(mix, 25)
    # 톤 밸런스: 쌓인 저중역(250Hz)을 덜고 현·브라스의 존재감(3.5kHz 이상)을 살린다
    mix = eq(mix, 'peak', 260, -3.5, 0.7)
    mix = eq(mix, 'highshelf', 3500, 4.5, 0.7)
    mix = eq(mix, 'peak', 1600, 1.5, 0.9)
    mix = glue(mix)

    gain_db = TARGET_LUFS - integrated_lufs(mix)
    mix *= 10 ** (gain_db / 20)
    mix = limit(mix, CEILING_DBTP - 0.3)

    # 페이드 인 (처음 FADE_IN 초, sin² 곡선) · 페이드 아웃 (종지 화음이 FADE_OUT_START 초까지 울린 뒤 끝까지, cos² 곡선)
    t = np.arange(N) / SR
    fade_in = np.sin(0.5 * np.pi * np.clip(t / FADE_IN, 0, 1)) ** 2
    fade_out = np.cos(0.5 * np.pi * np.clip((t - FADE_OUT_START) / (LENGTH - FADE_OUT_START), 0, 1)) ** 2
    return mix * (fade_in * fade_out)[:, None]


def write_wav(path, x):
    dither = (rng.uniform(-1, 1, x.shape) + rng.uniform(-1, 1, x.shape)) / 32768.0
    pcm = np.clip((x + dither) * 32767.0, -32768, 32767).astype(np.int16)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    wavfile.write(path, SR, pcm)


def main():
    root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
    parser = argparse.ArgumentParser()
    parser.add_argument('-o', '--output', default=os.path.join(root, 'Assets', 'Resources', 'Audio', 'Bgm', 'Intro.wav'))
    args = parser.parse_args()

    started = time.time()
    score()
    mix = master()
    write_wav(args.output, mix)
    print(f'{args.output}')
    print(f'  길이 {LENGTH:.1f}s · {integrated_lufs(mix):.1f} LUFS · True Peak {true_peak_db(mix):.2f} dBTP · 렌더 {time.time() - started:.0f}s')


if __name__ == '__main__':
    main()
