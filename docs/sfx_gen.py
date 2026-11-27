import math, random, struct, sys, wave, os

SR = 44100
random.seed(7)
out = sys.argv[1]
os.makedirs(out, exist_ok=True)


def env(t, a, d):
    # attack a, exponential decay d
    if t < a:
        return t / a
    return math.exp(-(t - a) / d)


def write(name, samples, gain=0.8):
    peak = max(1e-9, max(abs(s) for s in samples))
    k = gain / peak
    # 끝 5ms 페이드아웃 (딸깍 소리 방지)
    n = len(samples)
    f = int(SR * 0.005)
    data = bytearray()
    for i, s in enumerate(samples):
        if i > n - f:
            s *= (n - i) / f
        data += struct.pack('<h', int(max(-1, min(1, s * k)) * 32767))
    with wave.open(os.path.join(out, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes(bytes(data))


def lowpass(x, a):
    y, p = [], 0.0
    for v in x:
        p += a * (v - p)
        y.append(p)
    return y


def noise(n):
    return [random.uniform(-1, 1) for _ in range(n)]


def N(sec):
    return int(SR * sec)


# put: 팬에 툭 + 짧은 지글
def put():
    n = N(0.28); nz = lowpass(noise(n), 0.5)
    s = []
    for i in range(n):
        t = i / SR
        thump = math.sin(2 * math.pi * (140 - 200 * t) * t) * env(t, 0.003, 0.04)
        sizzle = nz[i] * env(t, 0.01, 0.12) * 0.5
        s.append(thump + sizzle)
    write('put', s)


# take: 위로 올라가는 뽁
def take():
    n = N(0.14); s = []
    for i in range(n):
        t = i / SR
        f = 500 + 1400 * (t / 0.14)
        s.append(math.sin(2 * math.pi * f * t) * env(t, 0.004, 0.05))
    write('take', s, 0.7)


# flip: 휙 (노이즈 스윕) + 착
def flip():
    n = N(0.36); nz = noise(n); s = []; p = 0.0
    for i in range(n):
        t = i / SR
        a = 0.05 + 0.5 * math.sin(math.pi * min(1, t / 0.22))  # 필터가 열렸다 닫힘
        p += a * (nz[i] - p)
        whoosh = p * math.sin(math.pi * min(1, t / 0.24)) if t < 0.24 else 0
        tt = t - 0.24
        slap = 0 if tt < 0 else (math.sin(2 * math.pi * 180 * tt) * 0.8 + random.uniform(-1, 1) * 0.4) * env(tt, 0.002, 0.03)
        s.append(whoosh * 0.9 + slap)
    write('flip', s)


# stir: 짧게 슥 (연타로 반복)
def stir():
    n = N(0.09); nz = lowpass(noise(n), 0.25); s = []
    for i in range(n):
        t = i / SR
        s.append(nz[i] * math.sin(math.pi * t / 0.09))
    write('stir', s, 0.55)


# lid: 냄비 뚜껑 쨍 (비조화 배음 금속음)
def lid():
    n = N(0.5); s = []
    parts = [(820, 1.0, 0.18), (1340, 0.6, 0.12), (2210, 0.4, 0.08), (3170, 0.25, 0.05)]
    for i in range(n):
        t = i / SR
        v = sum(a * math.sin(2 * math.pi * f * t) * env(t, 0.001, d) for f, a, d in parts)
        v += random.uniform(-1, 1) * env(t, 0.0005, 0.006) * 0.6
        s.append(v)
    write('lid', s, 0.6)


# boil_over: 보글보글
def boil_over():
    n = N(0.9); s = [0.0] * n
    t0 = 0.0
    while t0 < 0.82:
        f0 = random.uniform(300, 700)
        ln = random.uniform(0.03, 0.06)
        st = int(t0 * SR)
        for i in range(int(ln * SR)):
            if st + i >= n:
                break
            t = i / SR
            f = f0 * (1 + 1.5 * t / ln)
            s[st + i] += math.sin(2 * math.pi * f * t) * math.sin(math.pi * t / ln) * random.uniform(0.6, 1)
        t0 += random.uniform(0.03, 0.07)
    write('boil_over', s, 0.7)


def tone(f, t, d):
    return (math.sin(2 * math.pi * f * t) + 0.3 * math.sin(4 * math.pi * f * t)) * env(t, 0.005, d)


# success: 도-미-솔-도 올라가는 띠링
def success():
    notes = [523.25, 659.25, 783.99, 1046.5]
    n = N(0.75); s = [0.0] * n
    for k, f in enumerate(notes):
        st = N(0.08 * k)
        d = 0.35 if k == 3 else 0.12
        for i in range(n - st):
            s[st + i] += tone(f, i / SR, d)
    write('success', s, 0.7)


# fail: 뿌우웅 내려가는 소리 (코믹)
def fail():
    n = N(0.8); s = []; ph = 0.0
    for i in range(n):
        t = i / SR
        seg = int(t / 0.2)
        base = [392, 370, 349, 294][min(seg, 3)]
        f = base * (1 + 0.02 * math.sin(2 * math.pi * 6 * t))
        if seg >= 3:
            f *= 1 - 0.15 * ((t - 0.6) / 0.2)
        ph += 2 * math.pi * f / SR
        v = (2 * ((ph / (2 * math.pi)) % 1) - 1)  # 톱니파
        tl = t - 0.2 * min(seg, 3)
        e = min(1, tl / 0.01) * (0.9 if seg < 3 else math.exp(-tl / 0.12))
        s.append(v * e)
    write('fail', lowpass(s, 0.18), 0.6)


# click: 짧은 톡
def click():
    n = N(0.05); s = []
    for i in range(n):
        t = i / SR
        s.append(math.sin(2 * math.pi * 1800 * t) * env(t, 0.0005, 0.008) + random.uniform(-1, 1) * env(t, 0.0002, 0.002) * 0.3)
    write('click', s, 0.5)


# pop: 번쩍일 때 펑 (낮은 쿵 + 터지는 노이즈). 앞 소리들의 난수 순서가 바뀌지 않게 맨 뒤에 만든다
def pop():
    n = N(0.55); nz = noise(n); s = []; ph = 0.0; lp = 0.0
    for i in range(n):
        t = i / SR
        ph += 2 * math.pi * (45 + 160 * math.exp(-t / 0.025)) / SR
        boom = math.sin(ph) * env(t, 0.002, 0.12)
        a = 0.04 + 0.6 * math.exp(-t / 0.05)  # 처음엔 밝게 터지고 금방 둔해진다
        lp += a * (nz[i] - lp)
        burst = lp * env(t, 0.001, 0.09)
        s.append(boom * 1.0 + burst * 0.9)
    write('pop', s, 0.85)


# warn: 불 바늘 위험 경고 (삐-삐 두 번). 앞 소리들의 난수 순서가 바뀌지 않게 맨 뒤에 만든다
def warn():
    n = N(0.32); s = []
    for i in range(n):
        t = i / SR
        beep = 0.0
        for st in (0.0, 0.16):
            tt = t - st
            if 0 <= tt < 0.11:
                # 사각파 느낌 (홀수 배음)으로 귀에 잘 띄게. 1200Hz는 지글·보글 소리와 겹치지 않는 높이
                f = 1200
                beep += sum(math.sin(2 * math.pi * f * h * tt) / h for h in (1, 3, 5)) * min(1, tt / 0.004) * min(1, (0.11 - tt) / 0.01)
        s.append(beep)
    write('warn', s, 0.6)


# complete: 조리 완료 (불을 끄는 칙 + 맑은 딩). 결과 소리(success/fail)와 헷갈리지 않게 음 하나만 쓴다
def complete():
    n = N(0.7); nz = lowpass(noise(n), 0.4); s = []
    for i in range(n):
        t = i / SR
        hiss = nz[i] * env(t, 0.005, 0.06) * 0.5
        tt = t - 0.06
        ding = 0 if tt < 0 else (math.sin(2 * math.pi * 1568 * tt) + 0.4 * math.sin(2 * math.pi * 3136 * tt) * math.exp(-tt / 0.08)) * env(tt, 0.002, 0.25)
        s.append(hiss + ding)
    write('complete', s, 0.65)


# season: 간 고르기 선택 (양념 통을 탁탁탁 세 번 흔드는 소리)
def season():
    n = N(0.36); s = [0.0] * n
    for k in range(3):
        st = N(0.11 * k)
        burst = lowpass(noise(N(0.07)), 0.7)
        for i, v in enumerate(burst):
            t = i / SR
            # 높은 소리만 남겨 알갱이가 부딪는 느낌으로 (저역 빼기)
            hp = v - (burst[i - 1] if i > 0 else 0)
            s[st + i] += hp * env(t, 0.002, 0.02) * (1 - 0.15 * k)
    write('season', s, 0.6)


# order_wrong: 순서 틀림 (띠요옹 내려가는 소리). 실패(fail)보다 짧고 가볍게
def order_wrong():
    n = N(0.4); s = []; ph = 0.0
    for i in range(n):
        t = i / SR
        f = 700 * (1 - 0.55 * (t / 0.4)) * (1 + 0.04 * math.sin(2 * math.pi * 18 * t))
        ph += 2 * math.pi * f / SR
        s.append((math.sin(ph) + 0.3 * math.sin(2 * ph)) * env(t, 0.005, 0.18))
    write('order_wrong', s, 0.6)


for fn in (put, take, flip, stir, lid, boil_over, success, fail, click, pop, warn, complete, season, order_wrong):
    fn()
print('ok')
