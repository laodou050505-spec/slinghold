#!/usr/bin/env python3
"""《木垒》全部音效 + 背景音乐离线合成（原创，无任何第三方采样 / 音乐素材，可商用）。

用法（在工程根目录）：
  python3 Tools/audio/make_audio.py            # 全部重新生成
  python3 Tools/audio/make_audio.py sfx        # 只生成音效
  python3 Tools/audio/make_audio.py music tod2 # 只生成某几首音乐

输出：
  Assets/_PaperSiege/Resources/Audio/Sfx/<名字>_<变体>.wav   （单声道 44.1k / 16-bit，运行时按名字随机挑变体）
  Assets/_PaperSiege/Resources/Audio/Music/<名字>.ogg        （立体声 Vorbis，无缝循环）
  Docs/音频试听/频谱/*.png                                     （频谱图，用来检查刺耳的高频 / 低频轰鸣）

11-26 用户：弹弓的声音很难听，所有音效和 BGM 都要合适 → 整套重做：
  旧版是运行时的简易合成（拉弓 = 330 Hz 持续音 + 窄带噪声循环，越拉音越高 → 一直“吱——”）。
  这版按真实发声原理做：木块 / 石块用模态合成（几组按材料比例排开、各自衰减的共振），
  皮筋用“粘滑摩擦”（一串不规则的小冲击经共振滤波 → 零星的“咯吱”，不是持续尖音），
  松手 = 皮兜抽打的“啪” + 皮筋低频回弹 + 弹丸带起的风声；全部过一个小房间混响（游戏在真实房间里的桌面上）。
"""
import os
import sys

import numpy as np
import soundfile as sf
from scipy import signal

SR = 44100
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SFX_DIR = os.path.join(ROOT, 'Assets/_PaperSiege/Resources/Audio/Sfx')
MUS_DIR = os.path.join(ROOT, 'Assets/_PaperSiege/Resources/Audio/Music')
SPEC_DIR = os.path.join(ROOT, 'Docs/音频试听/频谱')


# ─────────────────────────────────────────── 基础 DSP

def T(dur):
    return np.arange(int(dur * SR)) / SR


def _sos(kind, f, order):
    if kind == 'band':
        lo, hi = f
        hi = min(hi, SR / 2 * 0.95)
        return signal.butter(order, [lo, hi], btype='band', fs=SR, output='sos')
    return signal.butter(order, min(f, SR / 2 * 0.95), btype=kind, fs=SR, output='sos')


def bp(x, lo, hi, order=2):
    return signal.sosfilt(_sos('band', (lo, hi), order), x)


def lp(x, f, order=2):
    return signal.sosfilt(_sos('low', f, order), x)


def hp(x, f, order=2):
    return signal.sosfilt(_sos('high', f, order), x)


def env(n, a, d, hold=0.0):
    """起音 a 秒线性上升，保持 hold 秒，再按时间常数 d 指数衰减"""
    t = np.arange(n) / SR
    e = np.exp(-np.maximum(t - a - hold, 0) / max(d, 1e-5))
    if a > 0:
        e = np.where(t < a, t / a, e)
    return e


def svf(x, fc, q=1.2, mode='bp'):
    """状态变量滤波器（截止频率可以逐采样变化，用来做风声扫频）"""
    fc = np.broadcast_to(np.asarray(fc, dtype=float), x.shape)
    y = np.zeros_like(x)
    low = band = 0.0
    damp = 1.0 / q
    for i in range(len(x)):
        f = 2 * np.sin(np.pi * min(fc[i], SR * 0.22) / SR)
        high = x[i] - low - damp * band
        band += f * high
        low += f * band
        y[i] = band if mode == 'bp' else low
    return y


def modal(dur, modes, rng, attack=0.0008):
    """模态合成：modes = [(频率, 振幅, 衰减时间常数)]"""
    t = T(dur)
    out = np.zeros_like(t)
    for f, a, d in modes:
        if f >= SR * 0.45:
            continue
        out += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 2 * np.pi)) * np.exp(-t / d)
    return out * np.clip(t / attack, 0, 1)


def put(buf, x, t0, gain=1.0):
    i = int(round(t0 * SR))
    if i >= len(buf):
        return
    k = int(0.01 * SR)
    if len(x) > 3 * k:      # 每个音结尾 10 ms 淡出：音还在响就被截断会“咔”一声（11-26 接住音效频谱上看到的竖线）
        x = x.copy()
        x[-k:] *= np.linspace(1, 0, k)
    j = min(len(buf), i + len(x))
    buf[i:j] += x[: j - i] * gain


def sweep_sine(dur, f0, f1, tau):
    """频率从 f0 指数滑到 f1 的正弦（相位积分，不会断）"""
    t = T(dur)
    f = f1 + (f0 - f1) * np.exp(-t / tau)
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


_IR = {}


def room_ir(rt60, size, seed, stereo=False, bright=5500):
    key = (rt60, size, seed, stereo, bright)
    if key in _IR:
        return _IR[key]
    rng = np.random.default_rng(seed)
    n = int(rt60 * 1.2 * SR)
    t = np.arange(n) / SR
    chans = []
    for c in range(2 if stereo else 1):
        x = rng.standard_normal(n) * np.exp(-6.91 * t / rt60)
        x = lp(x, bright, 1)
        x *= np.clip(t / (0.004 + size * 0.01), 0, 1)       # 混响尾巴慢慢长起来
        for k in range(8):                                     # 早期反射
            d = 0.002 + size * rng.uniform(0.004, 0.03)
            x[int(d * SR)] += rng.uniform(0.25, 0.6) * (1 if k % 2 else -1)
        chans.append(x / np.sqrt(np.sum(x ** 2)))
    _IR[key] = chans
    return chans


def add_reverb(x, wet, rt60=0.35, size=0.6, seed=7):
    ir = room_ir(rt60, size, seed)[0]
    y = signal.fftconvolve(x, ir)
    out = np.zeros(len(y))
    out[: len(x)] += x
    out += y * wet
    return out


def finish(x, rms_db=-20.0, peak_db=-1.0, fade_ms=3, tail_db=-62):
    """响度对齐（按有声部分的 RMS）+ 软限幅 + 去掉尾部静音 + 首尾淡入淡出"""
    x = np.asarray(x, dtype=float)
    a = np.abs(x)
    thr = a.max() * 10 ** (tail_db / 20)
    nz = np.nonzero(a > thr)[0]
    if len(nz):
        x = x[: nz[-1] + int(0.02 * SR)]
    act = x[np.abs(x) > np.abs(x).max() * 0.05]
    rms = np.sqrt(np.mean(act ** 2)) if len(act) else 1e-6
    x = x * (10 ** (rms_db / 20) / max(rms, 1e-9))
    pk = 10 ** (peak_db / 20)
    over = np.abs(x) > pk * 0.7
    if over.any():   # 软限幅：0.7 倍以上用 tanh 压到峰值以内
        s = np.sign(x)
        m = np.abs(x)
        k = pk * 0.7
        m = np.where(m > k, k + (pk - k) * np.tanh((m - k) / (pk - k)), m)
        x = s * m
    f = int(fade_ms / 1000 * SR)
    if len(x) > 2 * f:
        x[:f] *= np.linspace(0, 1, f)
        x[-f:] *= np.linspace(1, 0, f)
    return x


# ─────────────────────────────────────────── 音效

def gen_sling_stretch(i):
    """拉弓：皮筋被拉长时零星的“咯吱”（粘滑摩擦）+ 皮兜皮革轻轻的摩擦。很轻，不是持续音。"""
    rng = np.random.default_rng(100 + i)
    dur = 0.26
    n = int(dur * SR)
    imp = np.zeros(n)
    t = 0.008
    rate = rng.uniform(55, 85)
    while t < dur - 0.04:
        a = np.sin(np.pi * t / (dur - 0.03)) ** 1.4
        imp[int(t * SR)] += a * rng.uniform(0.5, 1.0) * (1 if rng.random() < 0.8 else -1)
        t += rng.uniform(0.6, 1.5) / rate
        rate *= 1.006
    rub = bp(imp, 600, 1150, 2) * 1.0 + bp(imp, 1500, 2100, 2) * 0.15 + lp(imp, 260, 2) * 0.6
    rub = rub + 0.3 * modal(dur, [(rng.uniform(180, 230), 0.3, 0.05)], rng) * lp(imp, 80, 1).cumsum().clip(-1, 1)
    leather = bp(rng.standard_normal(n), 600, 2000, 2) * np.sin(np.pi * np.linspace(0, 1, n)) ** 2 * 0.025
    x = lp(rub + leather, 2400, 2)   # 拉弓是背景里的小声音，高频全部收掉（第一版刺耳区占 30～40%）
    return finish(add_reverb(x, 0.08), rms_db=-24)


def gen_sling_grab(i):
    """捏住皮兜：皮革被捏的一声轻“嚓” + 弹丸碰皮兜的小“嗒”"""
    rng = np.random.default_rng(200 + i)
    dur = 0.2
    n = int(dur * SR)
    x = bp(rng.standard_normal(n), 500, 2600, 2) * env(n, 0.004, 0.035) * 0.7
    tap = modal(dur, [(rng.uniform(380, 460), 0.5, 0.02), (rng.uniform(1100, 1300), 0.2, 0.01)], rng)
    x += np.roll(tap, int(0.012 * SR)) * 0.6
    return finish(add_reverb(x, 0.08), rms_db=-24)


def gen_sling_release(i):
    """松手：皮兜抽打的“啪”（宽带短冲击）+ 皮筋回弹的低频“嘣”（很快收住，不嗡）+ 弹丸带起的风声（扫频带通噪声）"""
    rng = np.random.default_rng(300 + i)
    dur = 0.7
    n = int(dur * SR)
    t = T(dur)
    nz = rng.standard_normal(n)
    slap = bp(nz, 900, 5200, 2) * env(n, 0.0004, 0.006) * 0.9
    slap += lp(nz, 1600, 2) * env(n, 0.0006, 0.018) * 0.6
    thwack = sweep_sine(dur, rng.uniform(190, 220), 105, 0.03) * env(n, 0.001, 0.05) * 0.9
    band = (np.sin(2 * np.pi * rng.uniform(88, 100) * t) + 0.35 * np.sin(2 * np.pi * rng.uniform(176, 200) * t)) * env(n, 0.002, 0.07) * 0.45
    fc = 1500 * np.exp(-t / 0.18) + 380
    whoosh = lp(svf(rng.standard_normal(n), fc, q=0.9), 3500, 2) * env(n, 0.025, 0.14) * 0.6
    whoosh = np.roll(whoosh, int(0.01 * SR))
    x = slap + thwack + band + whoosh
    return finish(add_reverb(x, 0.1), rms_db=-17)


def wood_modes(rng, f1, dmul=1.0):
    return [(f1, 1.0, 0.055 * dmul), (f1 * rng.uniform(2.3, 2.5), 0.5, 0.03 * dmul),
            (f1 * rng.uniform(3.8, 4.1), 0.28, 0.016 * dmul), (f1 * rng.uniform(5.4, 5.9), 0.14, 0.009 * dmul)]


def gen_wood_hit(i):
    """木块被弹丸打中：木头的“笃”（模态）+ 撞击瞬间的“咔” + 一下厚实的低频（块被撞得一晃）"""
    rng = np.random.default_rng(400 + i)
    dur = 0.45
    n = int(dur * SR)
    x = modal(dur, wood_modes(rng, rng.uniform(480, 760)), rng)
    x += lp(rng.standard_normal(n), 4500, 2) * env(n, 0.0003, 0.004) * 0.7
    x += sweep_sine(dur, 170, 120, 0.04) * env(n, 0.001, 0.06) * 0.7
    x += bp(rng.standard_normal(n), 250, 900, 2) * env(n, 0.001, 0.03) * 0.35
    return finish(add_reverb(x, 0.1), rms_db=-17)


def gen_stone_hit(i):
    """石块被打：更硬更短的“咔”（密集的非谐共振）+ 颗粒感的碎屑声"""
    rng = np.random.default_rng(500 + i)
    dur = 0.45
    n = int(dur * SR)
    f1 = rng.uniform(950, 1300)
    x = modal(dur, [(f1, 1, 0.022), (f1 * 1.73, 0.7, 0.016), (f1 * 2.9, 0.45, 0.01), (f1 * 4.4, 0.25, 0.007), (f1 * 0.52, 0.5, 0.03)], rng)
    grit = np.zeros(n)
    for k in range(rng.integers(14, 22)):
        tt = abs(rng.normal(0.0, 0.03))
        grit[int(tt * SR)] += rng.uniform(0.2, 1.0) * np.exp(-tt / 0.04)
    x += bp(grit, 1500, 6000, 2) * 2.0
    x += lp(rng.standard_normal(n), 3000, 2) * env(n, 0.0003, 0.005) * 0.8
    x += sweep_sine(dur, 150, 100, 0.03) * env(n, 0.001, 0.045) * 0.6
    return finish(add_reverb(x, 0.1), rms_db=-18)


def small_tap(rng, f=None, dmul=1.0):
    f = f or rng.uniform(850, 1600)
    dur = 0.12
    n = int(dur * SR)
    x = modal(dur, wood_modes(rng, f, 0.55 * dmul), rng)
    x += lp(rng.standard_normal(n), 5000, 1) * env(n, 0.0002, 0.002) * 0.6
    return x


def table_thump(rng):
    dur = 0.15
    n = int(dur * SR)
    return sweep_sine(dur, rng.uniform(140, 180), 90, 0.02) * env(n, 0.001, 0.035) + lp(rng.standard_normal(n), 600, 2) * env(n, 0.0005, 0.012) * 0.5


def gen_clatter(i):
    """积木倒下：一串越来越稀、越来越轻的小磕碰（像积木在桌上弹两下）+ 偶尔一下桌面闷响"""
    rng = np.random.default_rng(600 + i)
    dur = 1.0
    buf = np.zeros(int(dur * SR))
    t = 0.0
    amp = 1.0
    for k in range(rng.integers(9, 14)):
        put(buf, small_tap(rng), t, amp * rng.uniform(0.6, 1.0))
        if rng.random() < 0.3:
            put(buf, table_thump(rng), t + 0.004, amp * 0.5)
        t += rng.uniform(0.025, 0.06) + k * 0.008
        amp *= rng.uniform(0.78, 0.92)
    return finish(add_reverb(buf, 0.12), rms_db=-20)


def gen_collapse(i):
    """整座城塌下来（结局）：一声木头断裂 + 低沉的隆隆 + 两秒多密密麻麻的积木滚落，越来越稀"""
    rng = np.random.default_rng(700 + i)
    dur = 3.4
    n = int(dur * SR)
    buf = np.zeros(n)
    put(buf, gen_wood_hit(9) * 1.2, 0.0)
    rumble = lp(rng.standard_normal(n), 140, 2) * env(n, 0.25, 0.9) * 0.9
    buf += rumble
    t = 0.05
    while t < 2.9:
        dens = np.exp(-t / 0.9)
        f = rng.uniform(380, 1300)
        put(buf, small_tap(rng, f, 1.4 if f < 700 else 1.0), t, rng.uniform(0.4, 1.0) * (0.35 + 0.65 * dens))
        if rng.random() < 0.18:
            put(buf, table_thump(rng), t, 0.6 * (0.3 + dens))
        t += rng.uniform(0.006, 0.04) / max(dens, 0.12)
    return finish(add_reverb(buf, 0.16, rt60=0.6, size=1.0), rms_db=-15)


def gen_bomb(i):
    """炸丸：卡通的“嘭”（低频下滑 + 闷的噪声团），后面跟一阵碎木屑落下；不刺耳、不吓人"""
    rng = np.random.default_rng(800 + i)
    dur = 1.6
    n = int(dur * SR)
    t = T(dur)
    boom = sweep_sine(dur, 135, 62, 0.15) * env(n, 0.002, 0.3)
    burst = lp(rng.standard_normal(n), 700, 2) * env(n, 0.002, 0.11) * 1.2
    crack = bp(rng.standard_normal(n), 500, 2500, 2) * env(n, 0.0005, 0.025) * 0.5
    buf = boom + burst + crack
    tt = 0.12
    while tt < 1.1:
        put(buf, small_tap(rng), tt, rng.uniform(0.15, 0.4) * np.exp(-tt / 0.6))
        tt += rng.uniform(0.03, 0.12)
    buf = hp(np.tanh(buf * 1.3), 50, 2)
    return finish(add_reverb(buf, 0.18, rt60=0.7, size=1.2), rms_db=-15)


def gen_ice(i):
    """冰丸：一下清脆的碎裂（密集小冲击，高频不过分）+ 玻璃样的短共振 + 几点细碎的结冰声"""
    rng = np.random.default_rng(900 + i)
    dur = 1.1
    n = int(dur * SR)
    imp = np.zeros(n)
    for k in range(28):
        tt = abs(rng.normal(0, 0.018))
        imp[int(tt * SR)] += rng.uniform(0.3, 1.0)
    x = bp(imp, 2200, 6000, 2) * 0.8 + bp(imp, 900, 2200, 2) * 0.9
    glass = modal(dur, [(rng.uniform(1700, 1950), 0.5, 0.16), (rng.uniform(2800, 3050), 0.3, 0.1), (rng.uniform(4000, 4300), 0.12, 0.06)], rng)
    x += np.roll(glass, int(0.004 * SR)) * 0.5
    for k in range(6):
        ping = modal(0.15, [(rng.uniform(3000, 5200), 1, 0.03)], rng)
        put(x, ping, rng.uniform(0.08, 0.55), 0.12)
    x += sweep_sine(dur, 160, 110, 0.03) * env(n, 0.001, 0.04) * 0.4
    x = lp(x, 6000, 2)
    return finish(add_reverb(x, 0.14), rms_db=-19)


def gen_fire(i):
    """着火：“呼”地一下烧起来（低通噪声，截止频率先升后落）+ 一串噼啪"""
    rng = np.random.default_rng(1000 + i)
    dur = 1.5
    n = int(dur * SR)
    t = T(dur)
    fc = 250 + 1300 * np.exp(-((t - 0.12) / 0.12) ** 2) + 350 * np.exp(-t / 0.6)
    whoomph = svf(rng.standard_normal(n), fc, q=0.7, mode='lp') * env(n, 0.07, 0.4) * 1.0
    crack = np.zeros(n)
    tt = 0.05
    while tt < 1.4:
        crack[int(tt * SR)] += rng.uniform(0.3, 1.0) * np.exp(-tt / 0.8)
        tt += rng.exponential(0.05)
    crack = bp(crack, 1200, 5000, 2) * 2.2
    x = whoomph + crack
    return finish(add_reverb(x, 0.1), rms_db=-21)


def gen_extinguish(i):
    """灭火：手拍上去的一下 + 蒸汽“嗤——”（高频但柔，往下收）"""
    rng = np.random.default_rng(1100 + i)
    dur = 1.0
    n = int(dur * SR)
    t = T(dur)
    pat = bp(rng.standard_normal(n), 250, 1600, 2) * env(n, 0.0008, 0.02) + sweep_sine(dur, 180, 120, 0.02) * env(n, 0.001, 0.03) * 0.6
    fc = 1100 + 1000 * np.exp(-t / 0.25)
    hiss = svf(rng.standard_normal(n), fc, q=0.8) * env(n, 0.03, 0.3) * 0.55
    sizzle = np.zeros(n)
    for k in range(25):
        tt = abs(rng.normal(0.05, 0.12))
        if tt < dur:
            sizzle[int(tt * SR)] += rng.uniform(0.2, 0.7)
    x = pat + np.roll(hiss, int(0.02 * SR)) + bp(sizzle, 2000, 4500, 2) * 0.5
    x = lp(x, 3400, 2)   # 第一版重心 9 kHz，太尖；蒸汽声放在 1～2 kHz，像“噗嗤”而不是“嘶”
    return finish(add_reverb(x, 0.1), rms_db=-21)


def gen_windup(i):
    """投石机上弦：木棘轮一格一格“咔、咔”（越来越紧）+ 中间绳子绷紧的低“吱”"""
    rng = np.random.default_rng(1200 + i)
    dur = 1.35
    buf = np.zeros(int(dur * SR))
    for k in range(6):
        tt = k * 0.19
        f = 1500 + k * 60
        click = modal(0.08, [(f, 1, 0.009), (f * 2.1, 0.5, 0.005), (f * 0.45, 0.6, 0.015)], rng)
        click += lp(rng.standard_normal(len(click)), 5000, 1) * env(len(click), 0.0002, 0.002) * 0.5
        put(buf, click, tt, 0.6 + 0.08 * k)
        m = int(0.12 * SR)
        imp = np.zeros(m)
        s = 0.01
        while s < 0.11:
            imp[int(s * SR)] += rng.uniform(0.4, 1)
            s += rng.uniform(0.014, 0.03)
        creak = bp(imp, 300, 900, 2) * 0.5
        put(buf, creak, tt + 0.04, 0.4 + 0.05 * k)
    return finish(add_reverb(buf, 0.1), rms_db=-21)


def gen_throw(i):
    """投石机抛出：抛臂“嗵”地撞在横梁上（低频木头）+ 抛出去的风声"""
    rng = np.random.default_rng(1300 + i)
    dur = 0.9
    n = int(dur * SR)
    t = T(dur)
    knock = modal(dur, [(rng.uniform(125, 145), 1, 0.09), (rng.uniform(310, 340), 0.5, 0.05), (rng.uniform(600, 650), 0.3, 0.025)], rng)
    knock += lp(rng.standard_normal(n), 1500, 2) * env(n, 0.0005, 0.02) * 0.8
    fc = 900 * np.exp(-t / 0.25) + 280
    whoosh = svf(rng.standard_normal(n), fc, q=0.8) * env(n, 0.02, 0.22) * 0.6
    x = knock + np.roll(whoosh, int(0.015 * SR))
    return finish(add_reverb(x, 0.12, rt60=0.5), rms_db=-18)


def mallet(f, vel, rng, dur=1.2, dscale=1.0):
    """马林巴 / 木琴的一个音：基频 + 3.93 倍泛音（木琴条的调律比例）+ 槌子软起音"""
    d1 = np.clip(0.55 * (440 / f) ** 0.5, 0.18, 1.3) * dscale
    x = modal(dur, [(f, 1.0, d1), (f * 3.93, 0.28 * vel, d1 * 0.22), (f * 9.2, 0.06 * vel, d1 * 0.07)], rng, attack=0.0015)
    n = len(x)
    x += lp(rng.standard_normal(n), 3000, 1) * env(n, 0.0003, 0.003) * 0.04 * vel
    return x * vel


def kalimba(f, vel, rng, dur=1.6):
    d1 = np.clip(0.9 * (523 / f) ** 0.4, 0.35, 1.6)
    x = modal(dur, [(f, 1.0, d1), (f * 5.93, 0.14 * vel, 0.07), (f * 2.0, 0.05, d1 * 0.5)], rng, attack=0.001)
    n = len(x)
    x += bp(rng.standard_normal(n), 2000, 5000, 1) * env(n, 0.0002, 0.002) * 0.03
    return x * vel


def gen_catch(i):
    """接住：手掌“噗”地兜住 + 一个温和的上行双音（木琴，五度）——奖励感，但不尖"""
    rng = np.random.default_rng(1400 + i)
    dur = 1.0
    n = int(dur * SR)
    buf = lp(rng.standard_normal(n), 1100, 2) * env(n, 0.0008, 0.022) * 0.8 + sweep_sine(dur, 200, 140, 0.02) * env(n, 0.001, 0.035) * 0.6
    base = [659.26, 783.99][i % 2]
    put(buf, mallet(base, 0.9, rng, 0.9), 0.035, 0.5)
    put(buf, mallet(base * 1.5, 0.9, rng, 0.9), 0.1, 0.45)
    return finish(add_reverb(buf, 0.14), rms_db=-19)


def gen_block(i):
    """挡开：手背把弹丸拨开的“啪”（比接住干、短）+ 弹丸飞走的一小下风声"""
    rng = np.random.default_rng(1500 + i)
    dur = 0.45
    n = int(dur * SR)
    t = T(dur)
    x = bp(rng.standard_normal(n), 600, 3200, 2) * env(n, 0.0004, 0.012) * 0.9
    x += sweep_sine(dur, 230, 150, 0.02) * env(n, 0.001, 0.03) * 0.6
    fc = 700 + 1200 * (1 - np.exp(-t / 0.1))
    x += np.roll(svf(rng.standard_normal(n), fc, q=0.9) * env(n, 0.01, 0.08) * 0.3, int(0.01 * SR))
    return finish(add_reverb(x, 0.1), rms_db=-19)


def gen_place(i):
    """放积木：木块落到位的“嗒”（清楚、短）"""
    rng = np.random.default_rng(1600 + i)
    dur = 0.3
    n = int(dur * SR)
    x = modal(dur, wood_modes(rng, rng.uniform(820, 1050), 0.9), rng)
    th = table_thump(rng)
    x[: len(th)] += th * 0.45
    x += lp(rng.standard_normal(n), 5000, 1) * env(n, 0.0002, 0.002) * 0.5
    return finish(add_reverb(x, 0.1), rms_db=-21)


def gen_button(i):
    """按木签：低一点、圆一点的“笃”+ 木签晃一下的轻“哒”"""
    rng = np.random.default_rng(1700 + i)
    dur = 0.35
    buf = np.zeros(int(dur * SR))
    put(buf, modal(0.25, wood_modes(rng, rng.uniform(480, 560), 1.1), rng), 0.0, 1.0)
    put(buf, small_tap(rng, rng.uniform(1300, 1500)), 0.07, 0.25)
    return finish(add_reverb(buf, 0.1), rms_db=-22)


def gen_star(i):
    """结算钉星：一声小铜钉的“叮”（短、暖）"""
    rng = np.random.default_rng(1800 + i)
    x = kalimba([1046.5, 1174.66, 1318.5][i % 3], 1.0, rng, 1.0)
    n = len(x)
    x += lp(rng.standard_normal(n), 4000, 1) * env(n, 0.0002, 0.002) * 0.1
    return finish(add_reverb(x, 0.12), rms_db=-23)


def gen_firework(i):
    """烟花（隔着天花板开口、在远处）：闷闷的“嘭”+ 一阵细碎的噼啪，混响更大更远"""
    rng = np.random.default_rng(1900 + i)
    dur = 2.4
    n = int(dur * SR)
    pop = lp(rng.standard_normal(n), 700, 2) * env(n, 0.002, 0.06) * 1.0 + sweep_sine(dur, 140, 85, 0.05) * env(n, 0.002, 0.1) * 0.5
    crack = np.zeros(n)
    tt = 0.12
    while tt < 2.0:
        dens = np.exp(-((tt - 0.35) / 0.5) ** 2) + 0.1
        crack[int(tt * SR)] += rng.uniform(0.2, 1.0) * dens
        tt += rng.exponential(0.018 / dens)
    crack = bp(crack, 1600, 5500, 2) * 4.0
    x = hp(lp(pop + crack, 7000, 1), 70, 2)
    return finish(add_reverb(x, 0.35, rt60=1.4, size=2.0, seed=11), rms_db=-22)


SFX = {
    'sling_stretch': (gen_sling_stretch, 4),
    'sling_grab': (gen_sling_grab, 2),
    'sling_release': (gen_sling_release, 3),
    'wood_hit': (gen_wood_hit, 4),
    'stone_hit': (gen_stone_hit, 3),
    'clatter': (gen_clatter, 3),
    'collapse': (gen_collapse, 1),
    'bomb': (gen_bomb, 2),
    'ice': (gen_ice, 2),
    'fire': (gen_fire, 2),
    'extinguish': (gen_extinguish, 2),
    'windup': (gen_windup, 1),
    'throw': (gen_throw, 2),
    'catch': (gen_catch, 2),
    'block': (gen_block, 2),
    'place': (gen_place, 3),
    'button': (gen_button, 2),
    'star': (gen_star, 3),
    'firework': (gen_firework, 3),
}


# ─────────────────────────────────────────── 乐器

def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def pluck(f, vel, rng, dur=2.0, bright=0.5, decay=0.996):
    """拨弦（Karplus–Strong）：一段噪声在延迟线里来回、每圈被平均一下 → 越来越圆润的拨弦声；按块向量化算"""
    n = int(dur * SR)
    L = max(2, int(round(SR / f - 0.5)))
    y = np.zeros(n + L + 1)
    exc = rng.uniform(-1, 1, L)
    exc = lp(exc, 800 + 7000 * bright, 1)
    exc -= exc.mean()
    y[1: L + 1] = exc
    a = 0.5 * decay
    s = L + 1
    while s < len(y):
        e = min(len(y), s + L)
        y[s:e] = a * (y[s - L:e - L] + y[s - L - 1:e - L - 1])
        s = e
    x = y[1: n + 1]
    x = x * np.clip(np.arange(n) / (0.002 * SR), 0, 1)
    body = bp(x, 90, 400, 1) * 0.4      # 琴身共鸣
    return (x + body) * vel


def bass(f, vel, rng, dur=1.4):
    n = int(dur * SR)
    t = T(dur)
    x = (np.sin(2 * np.pi * f * t) + 0.35 * np.sin(4 * np.pi * f * t) + 0.12 * np.sin(6 * np.pi * f * t)) * env(n, 0.006, 0.45)
    x += pluck(f, 0.25, rng, dur, bright=0.15, decay=0.993)
    return lp(x, 900, 2) * vel


def flute(f, vel, rng, dur):
    """木笛：正弦 + 少量二三次谐波，晚起的颤音，起音带一点气声"""
    rel = 0.09
    n = int((dur + rel) * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.0045 * np.sin(2 * np.pi * 5.2 * t + rng.uniform(0, 6)) * np.clip((t - 0.18) / 0.25, 0, 1)
    ph = 2 * np.pi * np.cumsum(f * vib) / SR
    tone = np.sin(ph) + 0.22 * np.sin(2 * ph) + 0.07 * np.sin(3 * ph)
    a = np.clip(t / 0.05, 0, 1)
    r = np.clip((dur + rel - t) / rel, 0, 1)
    swell = 1 - 0.12 * np.exp(-t / 0.15)
    e = a * r * swell
    breath = bp(rng.standard_normal(n), f * 0.9, min(f * 4, 15000), 1) * (0.06 * np.exp(-t / 0.08) + 0.012)
    return (tone + breath) * e * vel


def pad(fs, vel, rng, dur):
    """柔和的弦乐垫底：每个音三根略微失谐的锯齿（加法合成、只到 12 次谐波）→ 低通"""
    att, rel = 0.6, 0.8
    n = int((dur + rel) * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for f in fs:
        for det in (-0.004, 0.0, 0.0045):
            ff = f * (1 + det)
            ph = 2 * np.pi * ff * t + rng.uniform(0, 6)
            for h in range(1, 13):
                if ff * h > 6000:
                    break
                out += np.sin(h * ph) / h * (0.92 ** h)
    e = np.clip(t / att, 0, 1) * np.clip((dur + rel - t) / rel, 0, 1)
    return lp(out * e, 1400, 2) * vel / max(1, len(fs))


def drum_low(vel, rng):
    dur = 0.45
    n = int(dur * SR)
    x = sweep_sine(dur, 120, 58, 0.04) * env(n, 0.002, 0.18)
    x += lp(rng.standard_normal(n), 900, 2) * env(n, 0.001, 0.015) * 0.3
    return x * vel


def drum_slap(vel, rng):
    dur = 0.25
    n = int(dur * SR)
    x = bp(rng.standard_normal(n), 700, 3500, 2) * env(n, 0.0005, 0.03) + modal(dur, [(rng.uniform(290, 320), 0.6, 0.04)], rng)
    return x * vel * 0.7


def shaker(vel, rng):
    dur = 0.12
    n = int(dur * SR)
    return bp(rng.standard_normal(n), 4500, 11000, 2) * env(n, 0.012, 0.03) * vel * 0.5


def woodblock(vel, rng, f=1250):
    return modal(0.12, [(f, 1, 0.035), (f * 2.4, 0.4, 0.015)], rng) * vel * 0.5


def tamb(vel, rng):
    dur = 0.3
    n = int(dur * SR)
    x = hp(rng.standard_normal(n), 6000, 2) * env(n, 0.002, 0.07) * 0.5
    x += modal(dur, [(5300, 0.2, 0.08), (7150, 0.15, 0.06), (8900, 0.1, 0.05)], rng)
    return x * vel * 0.45


# ─────────────────────────────────────────── 作曲

CH = {'': (0, 4, 7), 'm': (0, 3, 7), 'sus2': (0, 2, 7), '7': (0, 4, 7, 10), 'm7': (0, 3, 7, 10), 'maj7': (0, 4, 7, 11), 'add9': (0, 4, 7, 14)}
NAMES = {'C': 0, 'C#': 1, 'Db': 1, 'D': 2, 'Eb': 3, 'E': 4, 'F': 5, 'F#': 6, 'Gb': 6, 'G': 7, 'Ab': 8, 'A': 9, 'Bb': 10, 'B': 11}


def chord(sym):
    root = sym[:2] if len(sym) > 1 and sym[1] in '#b' else sym[:1]
    return NAMES[root], CH[sym[len(root):]]


class Song:
    def __init__(self, name, bpm, bars, seed):
        self.name, self.bpm, self.bars = name, bpm, bars
        self.beat = 60.0 / bpm
        self.length = bars * 4 * self.beat
        self.rng = np.random.default_rng(seed)
        self.tracks = {}

    def tr(self, name, pan=0.0, gain=1.0, rev=0.2):
        if name not in self.tracks:
            self.tracks[name] = [np.zeros(int((self.length + 6) * SR)), pan, gain, rev]
        return self.tracks[name][0]

    def at(self, bar, beat):
        return (bar * 4 + beat) * self.beat

    def add(self, name, x, bar, beat, human=0.008):
        t = self.at(bar, beat) + self.rng.normal(0, human)
        put(self.tr(name), x, max(0.0, t))

    def render(self, rt60=1.9, master_rms=-19.0):
        n = int(self.length * SR)
        total = len(next(iter(self.tracks.values()))[0])
        dry = np.zeros((2, total))
        send = np.zeros(total)
        for name, (buf, pan, gain, rev) in self.tracks.items():
            l, r = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
            dry[0] += buf * gain * l
            dry[1] += buf * gain * r
            send += buf * gain * rev
        irl, irr = room_ir(rt60, 3.0, 21, stereo=True, bright=4500)
        wl = signal.fftconvolve(send, irl)[:total]
        wr = signal.fftconvolve(send, irr)[:total]
        mix = dry + np.stack([wl, wr]) * 0.55
        # 无缝循环：循环点之后的余音（最后的混响尾巴、长音）卷回开头叠加
        out = mix[:, :n].copy()
        tail = mix[:, n:]
        out[:, : tail.shape[1]] += tail[:, : n]
        # 轻压缩：按 50 ms 的包络把突出的峰压一压
        mono = np.abs(out).max(axis=0)
        envl = signal.filtfilt(*signal.butter(1, 3, fs=SR), mono)
        rms = np.sqrt(np.mean(out ** 2))
        thr = rms * 2.2
        g = np.where(envl > thr, (thr / np.maximum(envl, 1e-9)) ** 0.5, 1.0)
        out *= g
        rms = np.sqrt(np.mean(out ** 2))
        out *= 10 ** (master_rms / 20) / rms
        pk = np.abs(out).max()
        if pk > 0.89:
            out *= 0.89 / pk
        return out


def melody(song, inst, chords, bars, rng, lo, hi, motifs, plan, scale_pcs, vel=0.8, legato=0.95, start_bar=0):
    """旋律：按乐句计划（每 2 小节一个动机）生成。强拍落和弦音、弱拍走音阶级进；乐句最后一个音落主音或三音收住。
    同一个动机编号重复出现时用同样的随机种子 → 听起来是同一个乐思在不同和弦上再出现一次。"""
    pool = [m for m in range(lo, hi + 1) if m % 12 in scale_pcs]
    prev = pool[len(pool) // 2]
    for p, mot in enumerate(plan):
        if mot is None:
            continue
        rhythm = motifs[mot]
        r2 = np.random.default_rng(hash((song.name, mot)) % (2 ** 31))
        last_phrase = p == len(plan) - 1
        for k, (b, L) in enumerate(rhythm):
            bar = start_bar + p * 2 + int(b // 4)
            if bar >= start_bar + bars:
                break
            root, q = chords[bar % len(chords)]
            ct = {(root + iv) % 12 for iv in q}
            strong = abs(b % 2) < 1e-6
            cands = [m for m in pool if (m % 12 in ct if strong else True)]
            cands.sort(key=lambda m: abs(m - prev))
            if strong:
                pick = cands[min(len(cands) - 1, int(r2.integers(0, 3)))]
            else:
                step = int(r2.choice([-2, -1, -1, 1, 1, 2]))
                idx = pool.index(min(pool, key=lambda m: abs(m - prev)))
                pick = pool[int(np.clip(idx + step, 0, len(pool) - 1))]
            if k == len(rhythm) - 1:
                tonic = [m for m in pool if m % 12 in ({root % 12, (root + q[1]) % 12} if last_phrase or p % 2 else ct)]
                pick = min(tonic, key=lambda m: abs(m - prev))
            prev = pick
            dur = L * song.beat * legato
            v = vel * (1.0 if strong else 0.82) * rng.uniform(0.9, 1.05)
            song.add(inst[0], inst[1](midi(pick), v, rng, dur), bar, b % 4)


def make_title():
    """封面：清晨的薄雾，安静。拇指琴分解和弦 + 木笛 + 很淡的弦乐，不加鼓。"""
    s = Song('title', 76, 32, 1)
    r = s.rng
    prog = [chord(c) for c in ['D', 'Bm', 'G', 'A', 'D', 'F#m', 'G', 'A',
                               'D', 'Bm', 'G', 'A', 'G', 'A', 'D', 'D',
                               'Bm', 'G', 'D', 'A', 'Bm', 'G', 'Em', 'A',
                               'D', 'Bm', 'G', 'A', 'G', 'A', 'D', 'D']]
    for bar, (root, q) in enumerate(prog):
        notes = [62 + ((root - 2) % 12) + iv for iv in q] + [74 + ((root - 2) % 12)]
        for k, b in enumerate([0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5]):
            m = notes[[0, 1, 2, 3, 2, 1, 2, 3][k] % len(notes)]
            s.add('kal', kalimba(midi(m), 0.55 if k % 2 else 0.7, r, 1.4), bar, b)
        s.add('pad', pad([midi(50 + (root - 2) % 12 + iv) for iv in q], 0.5, r, s.beat * 4), bar, 0)
        if bar % 2 == 0:
            s.add('bass', bass(midi(38 + (root - 2) % 12), 0.6, r, 2.2), bar, 0)
    motifs = {0: [(0, 1.5), (1.5, 0.5), (2, 2), (4, 3)], 1: [(0, 1), (1, 1), (2, 1), (3, 1), (4, 4)], 2: [(0.5, 0.5), (1, 1.5), (2.5, 1.5), (4, 1), (5, 3)]}
    plan = [None, None, 0, 1, 0, 2, 1, None, None, 2, 0, 1, 0, 1, 2, None]
    melody(s, ('flute', flute), prog, 32, r, 69, 86, motifs, plan, {2, 4, 6, 7, 9, 11, 1}, vel=0.5)
    s.tracks['kal'][1:] = [-0.35, 0.8, 0.35]
    s.tracks['pad'][1:] = [0.0, 0.5, 0.5]
    s.tracks['bass'][1:] = [0.0, 0.7, 0.1]
    s.tracks['flute'][1:] = [0.25, 0.75, 0.4]
    return s.render(rt60=2.3, master_rms=-21)


def battle_song(name, bpm, seed, prog_syms, key_root, scale_pcs, lead, lead_rng, accomp, drums, energy, motifs, plan, rt60=1.7):
    s = Song(name, bpm, len(prog_syms), seed)
    r = s.rng
    prog = [chord(c) for c in prog_syms]
    for bar, (root, q) in enumerate(prog):
        sec = bar // 8                       # 0 A, 1 A', 2 B, 3 A''
        rel = (root - key_root) % 12
        accomp(s, bar, root, q, rel, sec, r)
        broot = 36 + (root % 12)
        if broot > 45:
            broot -= 12
        bpat = [(0, 0.95), (2, 0.8)] if energy < 2 else [(0, 0.95), (1.5, 0.55), (2, 0.85), (3.5, 0.5)]
        for b, v in bpat:
            m = broot + (7 if (b == 2 and energy >= 2 and bar % 2) else 0)
            s.add('bass', bass(midi(m), v * 0.8, r, s.beat * 1.6), bar, b)
        drums(s, bar, sec, r)
    melody(s, lead, prog, len(prog), r, lead_rng[0], lead_rng[1], motifs, plan, scale_pcs, vel=0.62)
    return s


def acc_pluck_arp(pattern=(0, 1, 2, 1, 3, 2, 1, 2), steps=8, vel=0.5, octave=55):
    def f(s, bar, root, q, rel, sec, r):
        notes = sorted(octave + ((root - octave) % 12) + iv for iv in q)
        notes = notes + [notes[0] + 12]
        for k in range(steps):
            b = k * 4 / steps
            m = notes[pattern[k % len(pattern)] % len(notes)]
            s.add('pluck', pluck(midi(m), vel * (1.0 if k % 2 == 0 else 0.75), r, 1.6, bright=0.45), bar, b)
        if sec == 2:
            s.add('pad', pad([midi(60 + ((root - 60) % 12) + iv) for iv in q], 0.35, r, s.beat * 4), bar, 0)
    return f


def acc_marimba(vel=0.5):
    def f(s, bar, root, q, rel, sec, r):
        notes = sorted(60 + ((root - 60) % 12) + iv for iv in q)
        pat = [(0, 0), (0.5, 2), (1, 1), (1.5, 2), (2, 0), (2.5, 2), (3, 1), (3.5, 2)]
        for b, k in pat:
            s.add('mar', mallet(midi(notes[k % len(notes)]), vel * (1 if b % 1 == 0 else 0.7), r, 0.9), bar, b)
        if sec >= 2:
            for k, nn in enumerate(notes):
                s.add('pluck', pluck(midi(nn - 12), 0.35, r, 1.2, bright=0.35), bar, 0 + k * 0.02)
                s.add('pluck', pluck(midi(nn - 12), 0.28, r, 1.0, bright=0.35), bar, 2 + k * 0.02)
    return f


def acc_strum(vel=0.45, rhythm=((0, 1), (1.5, 0.6), (2, 0.9), (3, 0.6), (3.5, 0.5))):
    def f(s, bar, root, q, rel, sec, r):
        notes = sorted(52 + ((root - 52) % 12) + iv for iv in q)
        notes = notes + [notes[0] + 12, notes[1] + 12]
        for b, v in rhythm:
            down = (b % 1) == 0
            order = notes if down else notes[::-1]
            for k, nn in enumerate(order):
                s.add("pluck", pluck(midi(nn), vel * v * (1 if down else 0.7), r, 1.3, bright=0.3), bar, b + k * 0.012 / s.beat, human=0.003)
    return f


def drums_folk(level):
    def f(s, bar, sec, r):
        lv = level + (0.25 if sec == 2 else 0)
        s.add('drum', drum_low(0.9, r), bar, 0)
        if lv >= 1:
            s.add('drum', drum_low(0.6, r), bar, 2.5 if bar % 2 else 2)
            s.add('drum', drum_slap(0.6, r), bar, 1)
            s.add('drum', drum_slap(0.6, r), bar, 3)
        for k in range(8 if lv >= 1 else 4):
            b = k * (0.5 if lv >= 1 else 1)
            s.add('shk', shaker(0.55 if k % 2 else 0.8, r), bar, b, human=0.005)
        if lv >= 1.5 and sec != 0:
            s.add('tamb', tamb(0.5, r), bar, 1)
            s.add('tamb', tamb(0.5, r), bar, 3)
        if lv >= 2 and bar % 4 == 3:
            for k in range(4):
                s.add('drum', drum_slap(0.45 + 0.1 * k, r), bar, 3 + k * 0.25)
    return f


def drums_soft(s, bar, sec, r):
    if bar % 2 == 0:
        s.add('drum', drum_low(0.55, r), bar, 0)
    s.add('wb', woodblock(0.35, r, 900), bar, 3)
    for k in range(4):
        s.add('shk', shaker(0.35, r), bar, k + 0.5)


def mixdown(s, settings, rt60, rms):
    for k, v in settings.items():
        if k in s.tracks:
            s.tracks[k][1:] = list(v)
    return s.render(rt60=rt60, master_rms=rms)


MOT_A = {0: [(0, 1), (1, 0.5), (1.5, 0.5), (2, 1), (3, 1), (4, 1.5), (5.5, 0.5), (6, 2)],
         1: [(0, 0.5), (0.5, 0.5), (1, 1), (2, 0.5), (2.5, 0.5), (3, 1), (4, 3)],
         2: [(0, 1.5), (1.5, 0.5), (2, 0.5), (2.5, 0.5), (3, 1), (4, 1), (5, 1), (6, 2)],
         3: [(0, 1), (1, 1), (2, 2), (4, 0.5), (4.5, 0.5), (5, 1), (6, 2)]}
PLAN_32 = [0, 1, 0, 2, 0, 1, 3, 2, 3, 2, 3, 1, 0, 1, 2, 3]

MIX_STD = {'pluck': (-0.3, 0.75, 0.22), 'mar': (0.3, 0.8, 0.22), 'bass': (0.0, 0.85, 0.06), 'drum': (0.0, 0.7, 0.12), 'shk': (0.45, 0.35, 0.15),
           'tamb': (-0.45, 0.35, 0.2), 'wb': (0.3, 0.5, 0.2), 'pad': (0.0, 0.45, 0.5), 'flute': (0.15, 0.7, 0.35), 'kal': (-0.25, 0.75, 0.3), 'lead_mar': (0.2, 0.8, 0.25)}

MAJOR = lambda r: {(r + i) % 12 for i in (0, 2, 4, 5, 7, 9, 11)}
MINOR = lambda r: {(r + i) % 12 for i in (0, 2, 3, 5, 7, 8, 10)}
MIXO = lambda r: {(r + i) % 12 for i in (0, 2, 4, 5, 7, 9, 10)}
DORIAN = lambda r: {(r + i) % 12 for i in (0, 2, 3, 5, 7, 9, 10)}


def make_tod0():
    """清晨（第 1～2 关）：G 大调 96 拍，吉他分解和弦 + 木笛旋律 + 轻沙锤，晨间小调，从容"""
    prog = ['G', 'D', 'Em', 'C', 'G', 'D', 'C', 'D'] * 2 + ['Em', 'C', 'G', 'D', 'Em', 'C', 'Am', 'D'] + ['G', 'D', 'Em', 'C', 'C', 'D', 'G', 'G']
    s = battle_song('tod0', 96, 10, prog, 7, MAJOR(7), ('flute', flute), (67, 84), acc_pluck_arp(vel=0.5), drums_folk(0.5), 1, MOT_A, PLAN_32)
    return mixdown(s, MIX_STD, 1.8, -19.5)


def make_tod1():
    """上午（第 3～4 关）：D 大调 112 拍，马林巴主旋律 + 吉他扫弦 + 手鼓，轻快"""
    prog = ['D', 'A', 'Bm', 'G', 'D', 'A', 'G', 'A'] * 2 + ['Bm', 'G', 'D', 'A', 'Bm', 'G', 'Em', 'A'] + ['D', 'A', 'Bm', 'G', 'G', 'A', 'D', 'D']
    s = battle_song('tod1', 112, 20, prog, 2, MAJOR(2), ('lead_mar', lambda f, v, r, d: mallet(f, v, r, max(0.6, d + 0.4))), (66, 86), acc_strum(vel=0.38), drums_folk(1), 1, MOT_A, PLAN_32)
    return mixdown(s, MIX_STD, 1.6, -19)


def make_tod2():
    """午后（第 5～6 关）：A 混合利底亚 120 拍，集市味道：木笛 + 马林巴琶音 + 铃鼓，最热闹"""
    prog = ['A', 'G', 'D', 'A', 'A', 'G', 'E', 'A'] * 2 + ['D', 'A', 'G', 'A', 'D', 'A', 'Bm', 'E'] + ['A', 'G', 'D', 'A', 'G', 'G', 'A', 'A']
    s = battle_song('tod2', 120, 30, prog, 9, MIXO(9), ('flute', flute), (69, 88), acc_marimba(vel=0.42), drums_folk(1.6), 2, MOT_A, PLAN_32)
    return mixdown(s, MIX_STD, 1.5, -18.5)


def make_tod3():
    """黄昏（第 7～8 关）：E 多利亚 100 拍，暖、带一点不舍：吉他分解 + 低音区马林巴旋律 + 弦乐垫底"""
    prog = ['Em', 'D', 'C', 'D', 'Em', 'D', 'A', 'Em'] * 2 + ['C', 'G', 'D', 'A', 'C', 'G', 'D', 'D'] + ['Em', 'D', 'C', 'D', 'A', 'C', 'Em', 'Em']
    def acc(s, bar, root, q, rel, sec, r):
        acc_pluck_arp(pattern=(0, 2, 1, 3, 2, 1, 3, 2), vel=0.45)(s, bar, root, q, rel, sec, r)
        s.add('pad', pad([midi(52 + ((root - 52) % 12) + iv) for iv in q], 0.32, r, s.beat * 4), bar, 0)
    s = battle_song('tod3', 100, 40, prog, 4, DORIAN(4), ('lead_mar', lambda f, v, r, d: mallet(f, v, r, max(0.8, d + 0.6), 1.4)), (59, 79), acc, drums_folk(1), 1, MOT_A, PLAN_32)
    return mixdown(s, MIX_STD, 2.0, -19.5)


def make_tod4():
    """入夜（第 9 关）：A 小调 88 拍，神秘但不吓人：拇指琴固定音型 + 低音 + 木鱼，木笛在低音区"""
    prog = ['Am', 'F', 'G', 'Am', 'Am', 'F', 'Dm', 'E'] * 2 + ['F', 'G', 'Am', 'Em', 'F', 'G', 'E', 'E'] + ['Am', 'F', 'G', 'Am', 'Dm', 'E', 'Am', 'Am']
    def acc(s, bar, root, q, rel, sec, r):
        notes = sorted(57 + ((root - 57) % 12) + iv for iv in q) + [69 + ((root - 57) % 12)]
        for k, b in enumerate([0, 0.75, 1.5, 2, 2.75, 3.5]):
            s.add('kal', kalimba(midi(notes[[0, 2, 1, 3, 2, 1][k] % len(notes)]), 0.5, r, 1.2), bar, b)
        s.add('pad', pad([midi(45 + ((root - 45) % 12) + iv) for iv in q], 0.3, r, s.beat * 4), bar, 0)
    s = battle_song('tod4', 88, 50, prog, 9, MINOR(9), ('flute', flute), (64, 81), acc, drums_soft, 1, MOT_A, PLAN_32)
    return mixdown(s, MIX_STD, 2.2, -20.5)


def make_tod5():
    """烟火夜 · 王城（第 10 关）：D 小调 116 拍，最后一战：手鼓更密、马林巴快速琶音、木笛在高处，副歌转到关系大调"""
    prog = ['Dm', 'Bb', 'C', 'Dm', 'Dm', 'Bb', 'Gm', 'A'] * 2 + ['F', 'C', 'Bb', 'F', 'Gm', 'Dm', 'A', 'A'] + ['Dm', 'Bb', 'C', 'Dm', 'Bb', 'C', 'Dm', 'Dm']
    def acc(s, bar, root, q, rel, sec, r):
        notes = sorted(62 + ((root - 62) % 12) + iv for iv in q)
        for k in range(8):
            s.add('mar', mallet(midi(notes[[0, 1, 2, 1, 0, 2, 1, 2][k] % len(notes)]), 0.42 if k % 2 else 0.55, r, 0.6), bar, k * 0.5)
        s.add('pad', pad([midi(50 + ((root - 50) % 12) + iv) for iv in q], 0.38 if sec == 2 else 0.28, r, s.beat * 4), bar, 0)
    s = battle_song('tod5', 116, 60, prog, 2, MINOR(2), ('flute', flute), (69, 88), acc, drums_folk(2), 2, MOT_A, PLAN_32)
    return mixdown(s, MIX_STD, 1.7, -18.5)


def make_ending():
    """结局（天亮了）：G 大调 80 拍，弦乐慢慢铺开 + 拇指琴分解 + 木笛唱一段完整的旋律，温暖、松一口气"""
    s = Song('ending', 80, 24, 70)
    r = s.rng
    syms = ['G', 'D', 'Em', 'C', 'G', 'D', 'C', 'D', 'Em', 'C', 'G', 'D', 'C', 'D', 'G', 'G', 'C', 'D', 'Bm', 'Em', 'C', 'D', 'G', 'G']
    prog = [chord(c) for c in syms]
    for bar, (root, q) in enumerate(prog):
        notes = sorted(67 + ((root - 67) % 12) + iv for iv in q)
        for k, b in enumerate([0, 1, 2, 3]):
            s.add('kal', kalimba(midi(notes[k % len(notes)] + (12 if k == 3 else 0)), 0.55, r, 1.6), bar, b + 0.5 * (k % 2))
        s.add('pad', pad([midi(55 + ((root - 55) % 12) + iv) for iv in q], 0.4 + 0.2 * (bar / len(prog)), r, s.beat * 4), bar, 0)
        s.add('bass', bass(midi(43 + ((root - 43) % 12)), 0.7, r, s.beat * 3.5), bar, 0)
        if bar >= 8:
            s.add('drum', drum_low(0.45, r), bar, 0)
            s.add('shk', shaker(0.3, r), bar, 2)
    motifs = {0: [(0, 1), (1, 1), (2, 2), (4, 1.5), (5.5, 0.5), (6, 2)], 1: [(0, 0.5), (0.5, 0.5), (1, 1), (2, 2), (4, 4)], 2: [(0, 2), (2, 1), (3, 1), (4, 4)]}
    melody(s, ('flute', flute), prog, 24, r, 67, 86, motifs, [None, 0, 1, 0, 2, 0, 1, 0, 2, 1, 2, None], MAJOR(7), vel=0.55)
    return mixdown(s, MIX_STD, 2.4, -20)


MUSIC = {'title': make_title, 'tod0': make_tod0, 'tod1': make_tod1, 'tod2': make_tod2, 'tod3': make_tod3, 'tod4': make_tod4, 'tod5': make_tod5, 'ending': make_ending}


# ─────────────────────────────────────────── 检查 / 输出

def report(name, x):
    """频谱指标：刺耳区（2.5～6 kHz）占比、低频轰鸣（< 60 Hz）占比、峰值、有声部分 RMS"""
    m = x if x.ndim == 1 else x.mean(axis=0)
    f, P = signal.welch(m, SR, nperseg=4096)
    tot = P.sum() + 1e-20
    harsh = P[(f > 2500) & (f < 6000)].sum() / tot
    boom = P[f < 60].sum() / tot
    cen = (f * P).sum() / tot
    pk = 20 * np.log10(np.abs(x).max() + 1e-9)
    act = m[np.abs(m) > np.abs(m).max() * 0.05]
    rms = 20 * np.log10(np.sqrt(np.mean(act ** 2)) + 1e-9)
    print(f'  {name:<18} {len(m)/SR:5.2f}s  峰 {pk:5.1f} dB  RMS {rms:5.1f} dB  重心 {cen:6.0f} Hz  刺耳区 {harsh*100:4.1f}%  <60Hz {boom*100:4.1f}%')
    return harsh


def spectrogram(name, x):
    """频谱图（scipy 算、PIL 画；横轴时间、纵轴 0～12 kHz、亮 = 能量大），存到 Docs/音频试听/频谱/"""
    from PIL import Image, ImageDraw
    os.makedirs(SPEC_DIR, exist_ok=True)
    m = x if x.ndim == 1 else x.mean(axis=0)
    f, t, S = signal.spectrogram(m, SR, nperseg=1024, noverlap=768)
    S = S[f <= 12000]
    db = 10 * np.log10(S + 1e-14)
    db = np.clip((db - (db.max() - 60)) / 60, 0, 1)[::-1]
    img = Image.fromarray((np.stack([db ** 0.8, db ** 1.6, db ** 3 * 0.6 + db * 0.2], -1) * 255).astype(np.uint8)).resize((640, 240))
    d = ImageDraw.Draw(img)
    for k in (2, 4, 6, 8, 10):
        y = int(240 * (1 - k / 12))
        d.line([(0, y), (12, y)], fill=(120, 120, 120))
        d.text((14, y - 6), f'{k}k', fill=(160, 160, 160))
    d.text((4, 4), name, fill=(255, 255, 255))
    img.save(os.path.join(SPEC_DIR, name + '.png'))


def build_sfx(only):
    os.makedirs(SFX_DIR, exist_ok=True)
    print('音效：')
    for name, (fn, count) in SFX.items():
        if only and name not in only:
            continue
        for i in range(count):
            x = fn(i)
            path = os.path.join(SFX_DIR, f'{name}_{i + 1}.wav')
            sf.write(path, x.astype(np.float32), SR, subtype='PCM_16')
            report(f'{name}_{i + 1}', x)
            if i == 0:
                spectrogram(f'sfx_{name}', x)


def build_music(only):
    os.makedirs(MUS_DIR, exist_ok=True)
    print('音乐：')
    for name, fn in MUSIC.items():
        if only and name not in only:
            continue
        x = fn()
        path = os.path.join(MUS_DIR, f'{name}.ogg')
        data = np.ascontiguousarray(x.T.astype(np.float32))
        with sf.SoundFile(path, 'w', SR, 2, format='OGG', subtype='VORBIS') as fo:   # 一次写整首会让 libsndfile 崩溃 → 分块写
            for k in range(0, len(data), 8192):
                fo.write(data[k:k + 8192])
        report(name, x)
        spectrogram(f'music_{name}', x[:, : SR * 30])


if __name__ == '__main__':
    args = sys.argv[1:]
    what = args[0] if args else 'all'
    only = set(args[1:])
    if what in ('all', 'sfx'):
        build_sfx(only)
    if what in ('all', 'music'):
        build_music(only)
