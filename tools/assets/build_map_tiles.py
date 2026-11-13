"""Builds the tiled map for the "Map" tab: assets/maptiles/<style>/<z>/<x>_<y>.webp + assets/maptiles/map.json.

The map lives in one "frame": the official 2048x2048 map of the season (fortnite-api.com/images/map.png, the same
texture the Fortnite Wiki has as "<Island> - Island - Fortnite"). Leaflet shows the frame with CRS.Simple, zoom 3 =
one frame pixel per screen pixel, 256 px tiles, so zoom z is a 2^z x 2^z grid of tiles.

Every other picture of the island (the in-game screenshot assets/map.png, or a bigger render you have) is placed into
the frame automatically: land/water masks of both pictures are aligned (SIFT for a first guess, then ECC). The world
calibration comes from assets/calibration.json (world -> in-game screenshot pixel, found from squad positions, see
docs/DECISIONS.md #23) composed with that alignment. A sharper source gets more zoom levels by itself.

    pip install opencv-python-headless pillow numpy
    python tools/assets/build_map_tiles.py                      # downloads the official map, uses assets/map.png
    python tools/assets/build_map_tiles.py --official map.png --style game=assets/map.png --style hd=my_render.png

Styles: "official" always; each --style id=path adds one more (the first extra style is aligned to the screenshot
calibration source unless --calib-source says otherwise). Landmarks (unnamed places) and their Russian names come
from fortnite-api.com/v1/map; named places from assets/pois.json (the same names the Efficiency tab uses)."""
import argparse, hashlib, io, json, math, os, shutil, sys, urllib.request

import cv2
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
FRAME = 2048          # frame size, px (zoom 3)
TILE = 256
FRAME_ZOOM = 3        # 2^3 * 256 = 2048
API = 'https://fortnite-api.com'

STYLE_NAMES = {
    'official': ('Official map', 'Официальная'),
    'game': ('In-game', 'Как в игре'),
}


def fetch(url):
    req = urllib.request.Request(url, headers={'User-Agent': 'pirate-chest-counter map tool'})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def read_image(path):
    img = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    if img is None:
        sys.exit(f'cannot read {path}')
    if img.ndim == 2:
        img = cv2.cvtColor(img, cv2.COLOR_GRAY2BGRA)
    elif img.shape[2] == 3:
        img = cv2.cvtColor(img, cv2.COLOR_BGR2BGRA)
    return img


def land_mask(img):
    """1 on land, 0 on water (blue / turquoise), slightly blurred so ECC has a gradient to follow."""
    hsv = cv2.cvtColor(img[..., :3], cv2.COLOR_BGR2HSV)
    water = (hsv[..., 0] >= 80) & (hsv[..., 0] <= 135) & (hsv[..., 1] > 50)
    water = cv2.morphologyEx(water.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    return cv2.GaussianBlur((1 - water).astype(np.float32), (0, 0), 3)


def align(src, frame_img):
    """2x3 affine source px -> frame px."""
    g1 = cv2.cvtColor(src[..., :3], cv2.COLOR_BGR2GRAY)
    g2 = cv2.cvtColor(frame_img[..., :3], cv2.COLOR_BGR2GRAY)
    sift = cv2.SIFT_create(8000)
    k1, d1 = sift.detectAndCompute(g1, None)
    k2, d2 = sift.detectAndCompute(g2, None)
    good = [a for a, b in cv2.BFMatcher().knnMatch(d1, d2, k=2) if a.distance < 0.75 * b.distance]
    if len(good) < 4:
        sys.exit('alignment: not enough matching features')
    p1 = np.float32([k1[g.queryIdx].pt for g in good])
    p2 = np.float32([k2[g.trainIdx].pt for g in good])
    guess, _ = cv2.estimateAffinePartial2D(p1, p2, ransacReprojThreshold=2.0, maxIters=20000, confidence=0.999)
    if guess is None:
        sys.exit('alignment: no first guess')
    crit = (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 500, 1e-7)
    cc, warp = cv2.findTransformECC(land_mask(src), land_mask(frame_img), guess.astype(np.float32), cv2.MOTION_AFFINE, crit, None, 5)
    print(f'  aligned: correlation {cc:.3f}, scale {math.sqrt(abs(np.linalg.det(warp[:, :2]))):.4f}')
    if cc < 0.8:
        sys.exit('alignment looks wrong (correlation < 0.8); check the source picture')
    return warp.astype(np.float64)


def render_style(sid, img, warp, out_dir, island, top_zoom):
    """Tiles for one style; warp: source px -> frame px. Zoom levels up to the picture's own resolution cover all
    of it; sharper levels (made with Lanczos, up to top_zoom) cover only the island. Returns the style's map.json entry."""
    k = math.sqrt(abs(np.linalg.det(warp[:, :2])))           # frame px per source px
    native = max(0, min(top_zoom, FRAME_ZOOM + round(math.log2(1 / k))))

    # Where the picture is (frame px, [[top, left], [bottom, right]]) and the colour along its edge (the page
    # behind the map gets it, so zooming out past the picture does not show a hard border).
    probe = cv2.warpAffine(img, warp, (FRAME, FRAME), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0, 0))
    ys, xs = np.nonzero(probe[..., 3] > 8)
    y0, y1, x0, x1 = int(ys.min()), int(ys.max()) + 1, int(xs.min()), int(xs.max()) + 1
    bounds = [[float(y0), float(x0)], [float(y1), float(x1)]]
    ring = np.concatenate([probe[y0 + 2:y0 + 8, x0:x1], probe[y1 - 8:y1 - 2, x0:x1]]).reshape(-1, 4)
    ring = ring[ring[:, 3] > 250]
    b, g, r = (int(v) for v in np.median(ring[:, :3], axis=0))
    bg = f'#{r:02x}{g:02x}{b:02x}'
    detail = [[max(y0, island[0][0]), max(x0, island[0][1])], [min(y1, island[1][0]), min(x1, island[1][1])]]

    files = 0
    for z in range(0, top_zoom + 1):
        region = bounds if z <= native else detail
        scale = 2 ** (z - FRAME_ZOOM)                         # canvas px per frame px
        # Leaflet asks for every tile that overlaps the layer's bounds (and only for those).
        tx0, ty0 = int(region[0][1] * scale // TILE), int(region[0][0] * scale // TILE)
        tx1, ty1 = math.ceil(region[1][1] * scale / TILE), math.ceil(region[1][0] * scale / TILE)
        m = warp * scale                                      # source px -> canvas px
        m[:, 2] -= (tx0 * TILE, ty0 * TILE)
        f = k * scale                                         # canvas px per source px
        src = img
        if f < 1:  # warpAffine has no area filter: shrink first, then place
            src = cv2.resize(img, None, fx=f, fy=f, interpolation=cv2.INTER_AREA)
            m[:, :2] /= f
        canvas = cv2.warpAffine(src, m, ((tx1 - tx0) * TILE, (ty1 - ty0) * TILE), flags=cv2.INTER_LANCZOS4 if f > 1.01 else cv2.INTER_LINEAR,
                                borderMode=cv2.BORDER_CONSTANT, borderValue=(0, 0, 0, 0))
        canvas = cv2.cvtColor(canvas, cv2.COLOR_BGRA2RGBA)
        d = os.path.join(out_dir, sid, str(z))
        os.makedirs(d, exist_ok=True)
        for ty in range(ty0, ty1):
            for tx in range(tx0, tx1):
                a = canvas[(ty - ty0) * TILE:(ty - ty0 + 1) * TILE, (tx - tx0) * TILE:(tx - tx0 + 1) * TILE]
                tile = Image.fromarray(a[..., :3] if a[..., 3].min() > 250 else a)
                tile.save(os.path.join(d, f'{tx}_{ty}.webp'), 'WEBP', quality=84 if z <= native else 80, method=6)
                files += 1
    en, ru = STYLE_NAMES.get(sid, (sid, sid))
    print(f'  {sid}: own resolution up to zoom {native}, sharpened to {top_zoom}; {files} tiles; bounds {bounds}')
    st = {'id': sid, 'name': en, 'ru': ru, 'maxNativeZoom': native, 'bounds': bounds, 'bg': bg}
    if top_zoom > native:
        st['detail'] = {'minZoom': native + 1, 'maxNativeZoom': top_zoom, 'bounds': detail}
    return st


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--official', help='official 2048x2048 map (default: download from fortnite-api.com)')
    ap.add_argument('--style', action='append', default=[], help='extra style id=path (default: game=assets/map.png)')
    ap.add_argument('--calib-source', default=os.path.join(ROOT, 'assets', 'map.png'), help='picture that assets/calibration.json describes')
    ap.add_argument('--out', default=os.path.join(ROOT, 'assets', 'maptiles'))
    ap.add_argument('--top-zoom', type=int, default=5, help='deepest zoom level written (3 = the official map 1:1)')
    a = ap.parse_args()

    if a.official:
        official = read_image(a.official)
    else:
        print('downloading the official map…')
        official = cv2.imdecode(np.frombuffer(fetch(f'{API}/images/map.png'), np.uint8), cv2.IMREAD_UNCHANGED)
        official = cv2.cvtColor(official, cv2.COLOR_BGR2BGRA) if official.shape[2] == 3 else official
    if official.shape[:2] != (FRAME, FRAME):
        official = cv2.resize(official, (FRAME, FRAME), interpolation=cv2.INTER_AREA)

    styles = [s.split('=', 1) for s in a.style] or [['game', os.path.join(ROOT, 'assets', 'map.png')]]

    # World -> frame: calibration of the screenshot composed with screenshot -> frame.
    cal = json.load(open(os.path.join(ROOT, 'assets', 'calibration.json')))
    s = (cal['p2']['x'] - cal['p1']['x']) / (cal['w2']['x'] - cal['w1']['x'])
    t = np.array([cal['p1']['x'] - s * cal['w1']['x'], cal['p1']['y'] - s * cal['w1']['y']])
    print('aligning the calibration source…')
    calib_img = read_image(a.calib_source)
    calib_warp = align(calib_img, official)
    A = calib_warp[:, :2] * s
    b = calib_warp[:, :2] @ t + calib_warp[:, 2]
    scale = float((A[0, 0] + A[1, 1]) / 2)  # the tiny shear/rotation of the affine fit is noise
    calibration = {'scale': scale, 'x0': float(b[0]), 'y0': float(b[1])}
    print(f'  world -> frame: px = {scale:.7f} * world + ({b[0]:.2f}, {b[1]:.2f})')

    # The island: where the in-game screenshot lies in the frame (the island with a little sea around it). The first
    # view fits it, and the sharpened zoom levels cover only it.
    h, w = calib_img.shape[:2]
    corners = np.array([[0, 0, 1], [w, 0, 1], [0, h, 1], [w, h, 1]], float) @ calib_warp.T
    island = [[float(np.floor(corners[:, 1].min())), float(np.floor(corners[:, 0].min()))],
              [float(np.ceil(corners[:, 1].max())), float(np.ceil(corners[:, 0].max()))]]

    if os.path.isdir(a.out):
        shutil.rmtree(a.out)
    os.makedirs(a.out)
    print('tiles…')
    out_styles = [render_style('official', official, np.array([[1, 0, 0], [0, 1, 0]], float), a.out, island, a.top_zoom)]
    for sid, path in styles:
        img = read_image(path)
        warp = calib_warp if os.path.abspath(path) == os.path.abspath(a.calib_source) else align(img, official)
        out_styles.append(render_style(sid, img, warp, a.out, island, a.top_zoom))

    # Places: named ones from pois.json (same names as the Efficiency tab), landmarks from the game's map data.
    places = []
    for p in json.load(open(os.path.join(ROOT, 'assets', 'pois.json'), encoding='utf-8')):
        kind = 'landmark' if p['id'] == 'ne-islet' else 'named'
        places.append({'id': p['id'], 'kind': kind, 'name': p['name'], 'ru': p.get('ru') or p['name'], 'x': p['x'], 'y': p['y']})
    try:
        en = json.loads(fetch(f'{API}/v1/map'))['data']['pois']
        ru = {p['id']: p['name'] for p in json.loads(fetch(f'{API}/v1/map?language=ru'))['data']['pois']}
        for p in en:
            if '.UnNamedPOI.' not in p['id']:
                continue
            places.append({'id': p['id'].split('UnNamedPOI.')[-1].replace('.', '-').lower(), 'kind': 'landmark', 'name': p['name'], 'ru': ru.get(p['id'], p['name']),
                           'x': round(p['location']['x']), 'y': round(p['location']['y'])})
    except Exception as e:  # offline: named places only
        print('  landmarks skipped:', e)

    digest = hashlib.sha1()
    for dirpath, _, names in sorted(os.walk(a.out)):
        for nme in sorted(names):
            digest.update(open(os.path.join(dirpath, nme), 'rb').read())
    meta = {
        'version': digest.hexdigest()[:10],
        'frame': FRAME, 'tileSize': TILE, 'frameZoom': FRAME_ZOOM,
        'calibration': calibration,
        'fit': island,
        'styles': out_styles,
        'places': places,
        'credit': 'Fortnite © Epic Games',
    }
    json.dump(meta, open(os.path.join(a.out, 'map.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    total = sum(os.path.getsize(os.path.join(dp, f)) for dp, _, fs in os.walk(a.out) for f in fs)
    print(f'done: {len(places)} places, {total / 1048576:.1f} MB in {a.out}')


if __name__ == '__main__':
    main()
