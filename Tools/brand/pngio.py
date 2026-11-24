# 纯标准库的 PNG 读写（本机 Blender 自带的 Python 没有 PIL）。只支持 8 位 RGBA / RGB / 灰度 / 调色板，非隔行。
import struct, zlib
def read(path):
    d = open(path, 'rb').read(); assert d[:8] == b'\x89PNG\r\n\x1a\n'
    p = 8; idat = b''; plte = None; trns = None
    while p < len(d):
        n, t = struct.unpack('>I4s', d[p:p+8]); c = d[p+8:p+8+n]; p += 12 + n
        if t == b'IHDR': w, h, bd, ct, _, _, il = struct.unpack('>IIBBBBB', c); assert bd == 8 and il == 0, (bd, il)
        elif t == b'IDAT': idat += c
        elif t == b'PLTE': plte = c
        elif t == b'tRNS': trns = c
    ch = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ct]; raw = zlib.decompress(idat); st = w * ch; prev = bytearray(st); out = []; q = 0
    for y in range(h):
        f = raw[q]; q += 1; cur = bytearray(raw[q:q+st]); q += st
        for i in range(st):
            a = cur[i-ch] if i >= ch else 0; b = prev[i]; c2 = prev[i-ch] if i >= ch else 0
            if f == 1: cur[i] = (cur[i] + a) & 255
            elif f == 2: cur[i] = (cur[i] + b) & 255
            elif f == 3: cur[i] = (cur[i] + ((a + b) >> 1)) & 255
            elif f == 4:
                pa = abs(b - c2); pb = abs(a - c2); pc = abs(a + b - 2 * c2)
                cur[i] = (cur[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c2)) & 255
        out.append(cur); prev = cur
    px = []
    for row in out:
        r = []
        for x in range(w):
            v = row[x*ch:(x+1)*ch]
            if ct == 6: r.append(tuple(v))
            elif ct == 2: r.append((v[0], v[1], v[2], 255))
            elif ct == 0: r.append((v[0], v[0], v[0], 255))
            elif ct == 4: r.append((v[0], v[0], v[0], v[1]))
            else: i = v[0]; r.append((plte[i*3], plte[i*3+1], plte[i*3+2], trns[i] if trns and i < len(trns) else 255))
        px.append(r)
    return w, h, px
def write(path, w, h, px):
    raw = bytearray()
    for row in px:
        raw.append(0)
        for p in row: raw += bytes(p)
    def chunk(t, c): return struct.pack('>I', len(c)) + t + c + struct.pack('>I', zlib.crc32(t + c) & 0xffffffff)
    open(path, 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b''))
def canvas(w, h, c): return [[c] * w for _ in range(h)]
def over(dst, src, ox, oy, k=1):
    """把 src 按整数倍 k 放大后叠到 dst 上（预览用）"""
    H = len(dst); W = len(dst[0])
    for y, row in enumerate(src):
        for x, (r, g, b, a) in enumerate(row):
            if a == 0: continue
            for dy in range(k):
                Y = oy + y * k + dy
                if Y < 0 or Y >= H: continue
                for dx in range(k):
                    X = ox + x * k + dx
                    if X < 0 or X >= W: continue
                    R, G, B, A = dst[Y][X]; t = a / 255
                    dst[Y][X] = (round(r * t + R * (1 - t)), round(g * t + G * (1 - t)), round(b * t + B * (1 - t)), 255)
def bbox(px):
    xs = [x for row in px for x, p in enumerate(row) if p[3] > 8]; ys = [y for y, row in enumerate(px) if any(p[3] > 8 for p in row)]
    return (min(xs), min(ys), max(xs) + 1, max(ys) + 1) if xs else None
