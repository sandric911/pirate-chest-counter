"""Draws the app icon (a pirate chest) procedurally and writes app.ico + icon.png. No dependencies."""
import struct, zlib, sys, os

def inside_round_rect(x, y, x0, y0, x1, y1, r_top, r_bot=0.0):
    if x < x0 or x > x1 or y < y0 or y > y1:
        return False
    for (cx, cy, r, cond) in ((x0 + r_top, y0 + r_top, r_top, y < y0 + r_top),
                              (x1 - r_top, y0 + r_top, r_top, y < y0 + r_top),
                              (x0 + r_bot, y1 - r_bot, r_bot, y > y1 - r_bot),
                              (x1 - r_bot, y1 - r_bot, r_bot, y > y1 - r_bot)):
        if r > 0 and cond and ((x < x0 + r and cx < (x0 + x1) / 2) or (x > x1 - r and cx > (x0 + x1) / 2)):
            if (x - cx) ** 2 + (y - cy) ** 2 > r * r:
                return False
    return True

OUT = (40, 22, 10, 255)
LID = (168, 92, 44, 255)
BODY = (139, 78, 36, 255)
PLANK = (110, 60, 26, 255)
GOLD = (240, 186, 60, 255)
GOLD_D = (190, 130, 30, 255)
HOLE = (35, 20, 8, 255)
GLOW = (255, 230, 140, 255)

def color_at(x, y):
    # layers from top to bottom priority
    # keyhole
    if (x - 0.5) ** 2 + (y - 0.50) ** 2 < 0.035 ** 2 or (0.485 < x < 0.515 and 0.50 < y < 0.585):
        return HOLE
    # lock plate
    if inside_round_rect(x, y, 0.40, 0.40, 0.60, 0.62, 0.04, 0.04):
        return GOLD if inside_round_rect(x, y, 0.415, 0.415, 0.585, 0.605, 0.03, 0.03) else GOLD_D
    # rim band between lid and body
    if 0.12 <= x <= 0.88 and 0.43 <= y <= 0.49:
        return GOLD
    # vertical straps
    if (0.22 <= x <= 0.30 or 0.70 <= x <= 0.78) and 0.17 <= y <= 0.86:
        if inside_round_rect(x, y, 0.10, 0.16, 0.90, 0.87, 0.20, 0.04):
            return GOLD if (0.23 <= x <= 0.29 or 0.71 <= x <= 0.77) else GOLD_D
    # outline + fill
    if inside_round_rect(x, y, 0.08, 0.14, 0.92, 0.89, 0.22, 0.05):
        if not inside_round_rect(x, y, 0.11, 0.17, 0.89, 0.86, 0.19, 0.03):
            return OUT
        if y < 0.45:
            # lid with a highlight
            return (188, 108, 54, 255) if y < 0.26 else LID
        # body with plank lines
        if abs(y - 0.67) < 0.012:
            return PLANK
        return BODY
    # sparkle
    if (abs(x - 0.86) < 0.012 and abs(y - 0.10) < 0.06) or (abs(y - 0.10) < 0.012 and abs(x - 0.86) < 0.06):
        return GLOW
    return (0, 0, 0, 0)

def render(size, ss=4):
    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            acc = [0, 0, 0, 0]
            for sy in range(ss):
                for sx in range(ss):
                    c = color_at((px + (sx + 0.5) / ss) / size, (py + (sy + 0.5) / ss) / size)
                    a = c[3]
                    acc[0] += c[0] * a; acc[1] += c[1] * a; acc[2] += c[2] * a; acc[3] += a
            n = ss * ss
            a = acc[3] / n
            if acc[3] > 0:
                row += bytes((int(acc[0] / acc[3]), int(acc[1] / acc[3]), int(acc[2] / acc[3]), int(a)))
            else:
                row += bytes((0, 0, 0, 0))
        rows.append(bytes(row))
    return rows

def png(size, rows):
    raw = b"".join(b"\x00" + r for r in rows)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")

def main(out_dir):
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    images = [(s, png(s, render(s))) for s in sizes]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    data = b""
    for s, p in images:
        entries += struct.pack("<BBBBHHII", s if s < 256 else 0, s if s < 256 else 0, 0, 0, 1, 32, len(p), offset + len(data))
        data += p
    open(os.path.join(out_dir, "app.ico"), "wb").write(header + entries + data)
    open(os.path.join(out_dir, "icon.png"), "wb").write(dict(images)[256])

if __name__ == "__main__":
    main(sys.argv[1])
