"""Named POIs from the in-game map screenshot (assets/map.png, 943x969).
Map calibration (found by registering 480k squad positions on the land mask, anchored on the small
round islet): pixel = 0.0055 * world + (361.9, 409.2), world X → right, world Y → down."""
import json, math, os, sys
S, TX, TY = 0.0055, 361.9, 409.2
LABELS = {  # label centers on the map, pixels
    'rubber-resort': ('Rubber Resort', 230, 153),
    'the-chest': ('The Chest', 184, 284),
    'loot-lagoon': ('Loot Lagoon', 440, 279),
    'fortunes-fort': ("Fortune's Fort", 625, 251),
    'lantern-landing': ('Lantern Landing', 296, 430),
    'arlong-park': ('Arlong Park', 78, 491),
    'cursed-cavern': ('Cursed Cavern', 534, 510),
    'ss-rocky': ('The S.S. Rocky', 727, 568),
    'loguetown': ('Loguetown', 372, 705),
    'windmill-village': ('Windmill Village', 178, 751),
    'busy-bay': ('Busy Bay', 705, 787),
    'plunder-port': ('Plunder Port', 506, 891),
    'ne-islet': ('Островок на северо-востоке', 855, 88),  # unnamed on the map; buried-chest ring
}
out = sys.argv[1] if len(sys.argv) > 1 else 'assets'
pois = [{'id': k, 'name': n, 'x': round((px - TX) / S), 'y': round((py - TY) / S), 'px': px, 'py': py} for k, (n, px, py) in LABELS.items()]
for p in pois:
    nearest = min(math.hypot(p['x'] - q['x'], p['y'] - q['y']) for q in pois if q is not p)
    p['r'] = round(max(12000, min(25000, nearest * 0.5)))
json.dump(pois, open(os.path.join(out, 'pois.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
cal = {'w1': {'x': 0, 'y': 0}, 'p1': {'x': TX, 'y': TY}, 'w2': {'x': 100000, 'y': 0}, 'p2': {'x': TX + S * 100000, 'y': TY}}
json.dump(cal, open(os.path.join(out, 'calibration.json'), 'w'), indent=1)
print([(p['id'], p['x'] // 100, p['y'] // 100, p['r'] // 100) for p in pois])
