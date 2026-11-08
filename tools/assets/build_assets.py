"""Builds assets/atlas.json (static chest positions) and assets/pois.json (dense chest clusters)
from a PirateChestCounter database. Only map coordinates are exported — nothing personal."""
import sqlite3, json, sys, math, os

db, out = sys.argv[1], sys.argv[2]
names = json.load(open(sys.argv[3], encoding='utf-8')) if len(sys.argv) > 3 and os.path.exists(sys.argv[3]) else {}
con = sqlite3.connect(db)
rows = con.execute("select chest_key, sx, sy, sz, n from atlas where n > 0").fetchall()
atlas = {k: [round(sx / n), round(sy / n), round(sz / n), min(n, 5)] for k, sx, sy, sz, n in rows}
json.dump(atlas, open(os.path.join(out, 'atlas.json'), 'w', encoding='utf-8'), separators=(',', ':'), sort_keys=True)

pts = [(v[0], v[1]) for v in atlas.values()]
EPS, MIN = 4000.0, 7   # 40 m neighbourhood, >= 7 chests
cell = {}
for i, (x, y) in enumerate(pts):
    cell.setdefault((int(x // EPS), int(y // EPS)), []).append(i)
def neigh(i):
    x, y = pts[i]; cx, cy = int(x // EPS), int(y // EPS)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for j in cell.get((cx + dx, cy + dy), []):
                if (pts[j][0] - x) ** 2 + (pts[j][1] - y) ** 2 <= EPS * EPS:
                    yield j
label = [None] * len(pts); cid = 0
for i in range(len(pts)):
    if label[i] is not None: continue
    nb = list(neigh(i))
    if len(nb) < MIN: label[i] = -1; continue
    label[i] = cid; queue = nb
    while queue:
        j = queue.pop()
        if label[j] == -1: label[j] = cid
        if label[j] is not None: continue
        label[j] = cid
        nb2 = list(neigh(j))
        if len(nb2) >= MIN: queue.extend(nb2)
    cid += 1
clusters = {}
for i, l in enumerate(label):
    if l is not None and l >= 0: clusters.setdefault(l, []).append(pts[i])
pois = []
for l, ps in clusters.items():
    cx = sum(p[0] for p in ps) / len(ps); cy = sum(p[1] for p in ps) / len(ps)
    d = sorted(math.hypot(p[0] - cx, p[1] - cy) for p in ps)
    r = max(20000, d[int(len(d) * 0.9)] + 8000)
    pois.append({'x': round(cx), 'y': round(cy), 'r': round(r), 'chests': len(ps)})
pois.sort(key=lambda p: -p['chests'])
for i, p in enumerate(pois, 1):
    p['id'] = f'poi{i}'
    p['name'] = names.get(p['id'], f'Точка {i}')
json.dump(pois, open(os.path.join(out, 'pois.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(atlas), 'chests;', len(pois), 'pois:', [(p['id'], p['chests'], p['x'] // 100, p['y'] // 100) for p in pois])
