# COOKER 배경 음악 3곡을 코드로 합성한다 (외부 음원 없음, Python 표준 라이브러리만 사용).
# 사용: py docs/bgm_gen.py Assets/Resources/Sfx
# 끝과 처음이 이어지는 루프로 만든다: 마지막 음의 꼬리를 곡 처음에 겹쳐 넣어 이음새에서 끊기지 않게 한다.
import math, os, random, struct, sys, wave

SR = 22050  # 배경 음악은 효과음보다 낮은 샘플레이트로 충분하다 (파일 크기 절약)
out = sys.argv[1]
os.makedirs(out, exist_ok=True)

NOTE = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}


def hz(name):
    # 'C4', 'F#3', 'Bb4'
    n = NOTE[name[0]]
    i = 1
    if name[i] == '#': n += 1; i += 1
    elif name[i] == 'b': n -= 1; i += 1
    octv = int(name[i:])
    return 440.0 * 2 ** ((n + 12 * (octv + 1) - 69) / 12)


class Track:
    def __init__(self, bpm, bars, beats=4):
        self.beat = 60.0 / bpm
        self.n = int(round(SR * self.beat * beats * bars))
        self.buf = [0.0] * self.n

    def add(self, start_beat, samples, gain):
        st = int(round(start_beat * self.beat * SR))
        for i, v in enumerate(samples):
            self.buf[(st + i) % self.n] += v * gain  # 넘친 꼬리는 곡 처음으로 (루프 이음새)

    def save(self, name, peak=0.6):
        m = max(1e-9, max(abs(v) for v in self.buf))
        k = peak / m
        data = bytearray()
        for v in self.buf:
            data += struct.pack('<h', int(max(-1, min(1, v * k)) * 32767))
        with wave.open(os.path.join(out, name + '.wav'), 'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
            w.writeframes(bytes(data))


def pluck(f, dur, bright=4, decay=0.25):
    n = int(SR * dur); s = []
    for i in range(n):
        t = i / SR
        v = sum(math.sin(2 * math.pi * f * h * t) / h for h in range(1, bright * 2, 2))
        s.append(v * min(1, t / 0.004) * math.exp(-t / decay))
    return s


def bell(f, dur, decay=0.4):
    n = int(SR * dur); s = []
    for i in range(n):
        t = i / SR
        v = math.sin(2 * math.pi * f * t) + 0.35 * math.sin(2 * math.pi * f * 2.0 * t) * math.exp(-t / 0.1)
        s.append(v * min(1, t / 0.003) * math.exp(-t / decay))
    return s


def bass(f, dur, decay=0.35):
    n = int(SR * dur); s = []
    for i in range(n):
        t = i / SR
        ph = (f * t) % 1
        tri = 4 * abs(ph - 0.5) - 1
        s.append(tri * min(1, t / 0.006) * math.exp(-t / decay))
    return s


def pad(freqs, dur):
    n = int(SR * dur); s = []
    for i in range(n):
        t = i / SR
        a = min(1, t / 0.25) * min(1, (dur - t) / 0.3)
        v = sum(math.sin(2 * math.pi * f * t) + 0.2 * math.sin(2 * math.pi * f * 1.003 * t) for f in freqs)
        s.append(v * max(0, a))
    return s


def kick():
    n = int(SR * 0.18); s = []; ph = 0
    for i in range(n):
        t = i / SR
        ph += 2 * math.pi * (50 + 110 * math.exp(-t / 0.03)) / SR
        s.append(math.sin(ph) * math.exp(-t / 0.06))
    return s


def hat(rng):
    n = int(SR * 0.04); s = []; prev = 0
    for i in range(n):
        x = rng.uniform(-1, 1)
        s.append((x - prev) * math.exp(-i / SR / 0.012))
        prev = x
    return s


def snare(rng):
    n = int(SR * 0.14); s = []
    for i in range(n):
        t = i / SR
        s.append((rng.uniform(-1, 1) * 0.7 + 0.5 * math.sin(2 * math.pi * 190 * t)) * math.exp(-t / 0.05))
    return s


def chord(root, kind):
    r = hz(root)
    ints = {'M': [0, 4, 7], 'm': [0, 3, 7], 'M7': [0, 4, 7, 11], 'm7': [0, 3, 7, 10], '7': [0, 4, 7, 10]}[kind]
    return [r * 2 ** (x / 12) for x in ints]


def melody(tr, notes, inst, gain, **kw):
    # notes: [(박, 음 또는 None, 길이)]
    for b, nm, ln in notes:
        if nm:
            tr.add(b, inst(hz(nm), ln * tr.beat + 0.3, **kw), gain)


def seq(pattern, step=0.5):
    # "C5 - E5 G5 ..." 한 칸 = step 박, '-'는 쉼
    out_, b = [], 0.0
    for tok in pattern.split():
        if tok != '-':
            out_.append((b, tok, step))
        b += step
    return out_


# ---------------------------------------------------------------- 타이틀: 통통 튀는 C장조, 110bpm, 8마디
def title():
    rng = random.Random(1)
    tr = Track(110, 8)
    prog = [('C3', 'M'), ('A2', 'm'), ('F2', 'M'), ('G2', '7')] * 2
    for bar, (r, k) in enumerate(prog):
        b0 = bar * 4
        f = chord(r, k)
        tr.add(b0, pad([x * 2 for x in f], 4 * tr.beat), 0.05)
        for q in range(4):
            tr.add(b0 + q, bass(f[0] if q % 2 == 0 else f[2], tr.beat * 0.9), 0.30)
            tr.add(b0 + q, kick() if q % 2 == 0 else snare(rng), 0.30 if q % 2 == 0 else 0.12)
            tr.add(b0 + q + 0.5, hat(rng), 0.10)
    mel = ("E5 - G5 E5 C5 - D5 E5  C5 - E5 - A4 - - -  "
           "F5 - E5 D5 C5 - A4 - B4 - D5 - G4 - - -  "
           "E5 - G5 E5 C5 - D5 E5  C6 - B5 A5 E5 - - -  "
           "F5 E5 D5 - A4 - C5 -  B4 - G4 - C5 - - -")
    melody(tr, seq(mel), pluck, 0.22, bright=3, decay=0.18)
    tr.save('bgm_title')


# ---------------------------------------------------------------- 로비·장보기: 느긋한 F장조, 88bpm, 8마디
def lobby():
    rng = random.Random(2)
    tr = Track(88, 8)
    prog = [('F2', 'M7'), ('D2', 'm7'), ('Bb2', 'M7'), ('C3', '7')] * 2
    for bar, (r, k) in enumerate(prog):
        b0 = bar * 4
        f = chord(r, k)
        tr.add(b0, pad([x * 2 for x in f], 4 * tr.beat), 0.06)
        tr.add(b0, bass(f[0], tr.beat * 1.8, decay=0.6), 0.30)
        tr.add(b0 + 2.5, bass(f[2], tr.beat * 1.2, decay=0.5), 0.22)
        for e in range(8):  # 잔잔한 분산화음
            tr.add(b0 + e * 0.5, bell(f[e % len(f)] * 4, 0.6, decay=0.25), 0.05)
        tr.add(b0 + 1, hat(rng), 0.05)
        tr.add(b0 + 3, hat(rng), 0.05)
    mel = ("A4 - - C5 - - F5 -  E5 - - D5 - - - -  "
           "D5 - - F5 - - A5 -  G5 - F5 - E5 - - -  "
           "A4 - - C5 - - F5 -  E5 - - D5 - - C5 -  "
           "D5 - - E5 - - C5 -  - - - - - - - -")
    melody(tr, seq(mel), bell, 0.16, decay=0.5)
    tr.save('bgm_lobby')


# ---------------------------------------------------------------- 조리: 바쁜 G단조 느낌, 136bpm, 8마디
def cook():
    rng = random.Random(3)
    tr = Track(136, 8)
    prog = [('G2', 'm'), ('Eb2', 'M'), ('F2', 'M'), ('D2', '7')] * 2
    for bar, (r, k) in enumerate(prog):
        b0 = bar * 4
        f = chord(r, k)
        for e in range(8):  # 쿵짝 8분 베이스
            tr.add(b0 + e * 0.5, bass(f[0] * (2 if e % 2 else 1), tr.beat * 0.45, decay=0.12), 0.26)
            tr.add(b0 + e * 0.5, hat(rng), 0.12 if e % 2 else 0.06)
        for q in range(4):
            tr.add(b0 + q, kick() if q % 2 == 0 else snare(rng), 0.32 if q % 2 == 0 else 0.15)
        for s16 in range(16):  # 보글보글 16분 분산화음
            tr.add(b0 + s16 * 0.25, pluck(f[s16 % 3] * 4, 0.12, bright=2, decay=0.05), 0.05)
    mel = ("G4 - Bb4 - D5 - Bb4 G4  Eb5 - D5 - Bb4 - G4 -  "
           "F4 - A4 - C5 - A4 F4  F#4 - A4 - D5 - - -  "
           "G4 - Bb4 - D5 - G5 -  F5 Eb5 D5 - Bb4 - G4 -  "
           "A4 - C5 - F5 - Eb5 D5  C5 - A4 - F#4 - D4 -")
    melody(tr, seq(mel), pluck, 0.18, bright=3, decay=0.10)
    tr.save('bgm_cook')


for fn in (title, lobby, cook):
    fn()
print('ok')
