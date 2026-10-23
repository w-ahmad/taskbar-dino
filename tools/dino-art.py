#!/usr/bin/env python3
"""Generates the dino pose grids (src/TaskbarDino.Core/Content/DinoArt.cs) from vector-ish shapes.

Every pose is a 48x48 mask built from rectangles and polygons (facing right); the script derives the
one-pixel outline ('o'), the body fill ('#') and the lighter belly band ('='), then writes the grids
as C# data so the app itself needs no image decoding. Run:  python tools/dino-art.py
"""
import os

W = H = 48

def blank():
    return [[0] * W for _ in range(H)]

def rect(m, x0, y0, x1, y1, v=1):
    for y in range(max(0, y0), min(H, y1 + 1)):
        for x in range(max(0, x0), min(W, x1 + 1)):
            m[y][x] = v

def poly(m, pts, v=1):
    # even-odd scanline fill, inclusive edges
    n = len(pts)
    for y in range(H):
        xs = []
        cy = y + 0.5
        for i in range(n):
            (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
            if (y0 <= cy < y1) or (y1 <= cy < y0):
                xs.append(x0 + (cy - y0) * (x1 - x0) / (y1 - y0))
        xs.sort()
        for i in range(0, len(xs) - 1, 2):
            for x in range(int(round(xs[i])), int(round(xs[i + 1]))):
                if 0 <= x < W:
                    m[y][x] = v

def round_rect(m, x0, y0, x1, y1, r):
    rect(m, x0 + r, y0, x1 - r, y1)
    rect(m, x0, y0 + r, x1, y1 - r)
    for cx, cy in ((x0 + r, y0 + r), (x1 - r, y0 + r), (x0 + r, y1 - r), (x1 - r, y1 - r)):
        for y in range(cy - r, cy + r + 1):
            for x in range(cx - r, cx + r + 1):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r + r and 0 <= x < W and 0 <= y < H:
                    m[y][x] = 1

def render(mask, belly=None, lines=(), holes=()):
    """mask -> rows of '.', 'o', '#', '='. `belly` is a second mask; `lines` are explicit outline pixels."""
    for (x, y) in holes:
        mask[y][x] = 0
    out = []
    for y in range(H):
        row = []
        for x in range(W):
            if not mask[y][x]:
                row.append('.')
                continue
            edge = (x == 0 or not mask[y][x - 1]) or (x == W - 1 or not mask[y][x + 1]) or \
                   (y == 0 or not mask[y - 1][x]) or (y == H - 1 or not mask[y + 1][x])
            if edge:
                row.append('o')
            elif belly is not None and belly[y][x]:
                row.append('=')
            else:
                row.append('#')
        out.append(row)
    for (x, y) in lines:
        if 0 <= x < W and 0 <= y < H and out[y][x] != '.':
            out[y][x] = 'o'
    return [''.join(r) for r in out]

# ---------------------------------------------------------------- building blocks (standing pose)

def head(m, dx=0, dy=0, mouth_open=0):
    # Boxy T-rex head with a rounded crown; the upper jaw overhangs the lower one.
    round_rect(m, 23 + dx, 1 + dy, 46 + dx, 12 + dy, 3)
    rect(m, 22 + dx, 7 + dy, 39 + dx, 12 + dy)
    # lower jaw (drops when the mouth opens)
    jaw_top = 13 + dy + mouth_open
    rect(m, 22 + dx, jaw_top, 40 + dx, 16 + dy + mouth_open)
    rect(m, 40 + dx, jaw_top, 43 + dx, 14 + dy + mouth_open)

def head_lines(dx=0, dy=0, mouth_open=0):
    # the closed-mouth line along the front of the head
    if mouth_open:
        return []
    return [(x, 12 + dy) for x in range(36 + dx, 46 + dx)]

def neck(m, dx=0, dy=0):
    poly(m, [(23 + dx, 10 + dy), (33 + dx, 10 + dy), (33 + dx, 24 + dy), (15 + dx, 24 + dy)])

def torso(m, dx=0, dy=0):
    poly(m, [(15 + dx, 18 + dy), (31 + dx, 18 + dy), (36 + dx, 23 + dy), (36 + dx, 33 + dy),
             (30 + dx, 39 + dy), (11 + dx, 39 + dy), (6 + dx, 33 + dy), (7 + dx, 24 + dy)])

def tail(m, up=False, dx=0, dy=0):
    if up:
        poly(m, [(10 + dx, 23 + dy), (3 + dx, 17 + dy), (0 + dx, 19 + dy), (3 + dx, 25 + dy), (10 + dx, 33 + dy)])
    else:
        poly(m, [(9 + dx, 25 + dy), (1 + dx, 27 + dy), (0 + dx, 31 + dy), (9 + dx, 35 + dy)])

def arm(m, dx=0, dy=0):
    rect(m, 34 + dx, 24 + dy, 41 + dx, 26 + dy)   # upper arm, forward
    rect(m, 39 + dx, 26 + dy, 42 + dx, 30 + dy)   # forearm, down
    rect(m, 42 + dx, 29 + dy, 43 + dx, 30 + dy)   # claw

def belly_mask(dx=0, dy=0):
    b = blank()
    poly(b, [(13 + dx, 27 + dy), (33 + dx, 27 + dy), (33 + dx, 34 + dy), (27 + dx, 37 + dy), (13 + dx, 37 + dy), (10 + dx, 33 + dy)])
    return b

def leg(m, x, top=36, shin=40, foot=44, lift=0, forward=0):
    # a thigh, a shin and a forward-pointing foot; `lift` raises the whole leg, `forward` slides the foot
    rect(m, x, top - lift, x + 8, shin - lift)
    rect(m, x + 2, shin - lift, x + 6, foot - lift)
    rect(m, x + 1 + forward, foot - lift, x + 10 + forward, 46 - lift)

def body(dx=0, dy=0, tail_up=False, mouth_open=0):
    m = blank()
    tail(m, tail_up, dx, dy)
    torso(m, dx, dy)
    neck(m, dx, dy)
    head(m, dx, dy, mouth_open)
    arm(m, dx, dy)
    return m

# ---------------------------------------------------------------- poses

poses = {}
eyes = {}

def standing(name, legs, tail_up=False, mouth_open=0, dy=0, lines_extra=()):
    m = body(0, dy, tail_up, mouth_open)
    for args in legs:
        leg(m, *args)
    poses[name] = render(m, belly_mask(0, dy), list(head_lines(0, dy, mouth_open)) + list(lines_extra))
    eyes[name] = (29, 4 + dy)

standing('idle', [(11,), (23,)])
standing('tail_up', [(11,), (23,)], tail_up=True)
standing('yawn', [(11,), (23,)], mouth_open=4)
# walk cycle: four frames, legs alternate stride / together / stride / together
standing('walk1', [(9, 36, 40, 44, 0, 0), (25, 36, 40, 44, 1, 1)])
standing('walk2', [(12, 36, 40, 44, 2, 0), (23, 36, 40, 44, 0, 0)])
standing('walk3', [(14, 36, 40, 44, 1, 1), (21, 36, 40, 44, 0, 0)])
standing('walk4', [(11, 36, 40, 44, 0, 0), (23, 36, 40, 44, 2, 0)])
standing('jump', [(12, 36, 39, 41, 0, 1), (22, 36, 39, 41, 0, 1)])          # legs tucked
standing('scratch1', [(11, 36, 40, 44, 6, 2), (23,)])                        # back leg up, scratching the belly
standing('scratch2', [(11, 36, 40, 44, 5, 3), (23,)])
standing('stretch', [(11, 34, 39, 44), (23, 34, 39, 44)], dy=-2)             # up on tiptoes, body raised

# sitting / sleeping: body dropped, legs folded under
def sitting(name):
    m = body(0, 6)
    rect(m, 10, 40, 32, 46)      # folded legs / haunches
    poses[name] = render(m, belly_mask(0, 6), head_lines(0, 6))
    eyes[name] = (29, 10)
sitting('sit')

# lying down asleep: flat on the ground, head resting forward, tail out behind, legs tucked
def lying(name):
    m = blank()
    poly(m, [(10, 36), (1, 38), (0, 42), (10, 45)])                           # tail along the ground
    poly(m, [(8, 30), (28, 28), (34, 32), (34, 44), (28, 46), (10, 46), (6, 41)])  # low torso
    poly(m, [(24, 30), (32, 28), (36, 40), (28, 42)])                        # short neck forward
    round_rect(m, 30, 31, 47, 42, 3)                                          # head resting on the ground
    rect(m, 29, 37, 45, 43)
    rect(m, 29, 43, 42, 46)                                                   # jaw on the ground
    rect(m, 12, 41, 24, 46)                                                   # folded legs
    rect(m, 34, 37, 37, 39)                                                   # arm tucked
    b = blank(); poly(b, [(12, 36), (30, 36), (30, 42), (26, 44), (12, 44), (9, 41)])
    poses[name] = render(m, b, [(x, 42) for x in range(38, 47)])
    eyes[name] = (36, 34)
lying('lie')

# ducking: the whole dino lowered and stretched forward (head level with the back)
def ducking(name, step):
    m = blank()
    poly(m, [(8, 30), (0, 32), (0, 36), (8, 40)])                            # tail
    poly(m, [(8, 26), (30, 26), (34, 30), (34, 38), (28, 42), (10, 42), (6, 38)])  # long low torso
    rect(m, 26, 22, 44, 34)                                                   # head forward
    round_rect(m, 26, 20, 46, 32, 3)
    rect(m, 36, 32, 42, 35)                                                   # jaw
    rect(m, 30, 34, 36, 36)                                                   # arm
    rect(m, 34, 36, 36, 39)
    for x, lift in ((10, step), (22, 0)):
        rect(m, x, 42, x + 6, 44 - lift)
        rect(m, x - 1, 44 - lift, x + 8, 46 - lift)
    b = blank(); poly(b, [(12, 33), (30, 33), (30, 39), (24, 41), (12, 41), (9, 38)])
    poses[name] = render(m, b, [(x, 31) for x in range(38, 46)])
    eyes[name] = (32, 23)
ducking('duck1', 0)
ducking('duck2', 1)

# eating: head and neck dipped toward the ground
def eating(name):
    m = blank()
    tail(m)
    torso(m)
    poly(m, [(23, 20), (31, 18), (40, 36), (28, 40)])                        # neck bent down
    round_rect(m, 29, 30, 47, 41, 3)                                          # head low, snout at the ground
    rect(m, 28, 36, 44, 41)
    rect(m, 28, 42, 42, 45)                                                   # jaw
    arm(m)
    leg(m, 11); leg(m, 23)
    poses[name] = render(m, belly_mask(), [(x, 41) for x in range(38, 47)])
    eyes[name] = (35, 33)
eating('eat')

# ---------------------------------------------------------------- game art (same pixel density as the dino)

game = {}

def cactus(name, w, h, arms):
    m = [[0] * w for _ in range(h)]
    def r(x0, y0, x1, y1):
        for y in range(max(0, y0), min(h, y1 + 1)):
            for x in range(max(0, x0), min(w, x1 + 1)):
                m[y][x] = 1
    cx = w // 2
    r(cx - 3, 0, cx + 2, h - 1)                       # trunk
    r(cx - 2, 0, cx + 1, 0)
    for side, top, join in arms:                      # side: -1 left / +1 right; top: arm top row; join: row where it meets the trunk
        if side < 0:
            r(cx - 9, top, cx - 6, join); r(cx - 9, join - 2, cx - 3, join)
        else:
            r(cx + 5, top, cx + 8, join); r(cx + 2, join - 2, cx + 8, join)
    rows = []
    for y in range(h):
        row = ''
        for x in range(w):
            row += '#' if m[y][x] else '.'
        rows.append(row)
    game[name] = rows

cactus('cactus_small', 24, 36, [(-1, 6, 16), (1, 10, 20)])
cactus('cactus_large', 32, 48, [(-1, 6, 22), (1, 12, 26), (-1, 26, 36)])

def ptero(name, wings_up):
    w, h = 48, 32
    m = [[0] * w for _ in range(h)]
    def P(pts):
        n = len(pts)
        for y in range(h):
            xs = []
            cy = y + 0.5
            for i in range(n):
                (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
                if (y0 <= cy < y1) or (y1 <= cy < y0):
                    xs.append(x0 + (cy - y0) * (x1 - x0) / (y1 - y0))
            xs.sort()
            for i in range(0, len(xs) - 1, 2):
                for x in range(int(round(xs[i])), int(round(xs[i + 1]))):
                    if 0 <= x < w: m[y][x] = 1
    # body + beak (facing left, flies right-to-left)
    P([(6, 18), (16, 14), (34, 14), (40, 18), (34, 22), (16, 22)])
    P([(0, 19), (8, 15), (8, 23)])                     # beak
    P([(36, 14), (46, 10), (44, 16)])                  # head crest
    if wings_up:
        P([(14, 16), (20, 0), (30, 0), (30, 16)])      # wing raised
    else:
        P([(14, 20), (20, 31), (30, 31), (30, 20)])    # wing lowered
    rows = []
    for y in range(h):
        rows.append(''.join('#' if m[y][x] else '.' for x in range(w)))
    game[name] = rows

ptero('ptero1', True)
ptero('ptero2', False)

def cloud(name):
    w, h = 56, 20
    m = [[0] * w for _ in range(h)]
    for (cx, cy, r) in ((12, 13, 6), (24, 9, 9), (38, 11, 8), (48, 14, 5)):
        for y in range(h):
            for x in range(w):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                    m[y][x] = 1
    for y in range(10, 19):              # flat underside: fill between the outer circles
        xs = [x for x in range(w) if m[y][x]]
        if xs:
            for x in range(xs[0], xs[-1] + 1):
                m[y][x] = 1
    for y in range(h):
        for x in range(w):
            if y > 18: m[y][x] = 0
    rows = []
    for y in range(h):
        rows.append(''.join('#' if m[y][x] else '.' for x in range(w)))
    game[name] = [r for r in rows if '#' in r]

cloud('cloud')

def ground(name):
    w, h = 96, 8
    rows = ['#' * 40 + '..' + '#' * 54, '.' * w, '.' * w, '.' * w, '.' * w, '.' * w, '.' * w, '.' * w]
    pebbles = [(2, 6), (3, 6), (20, 3), (21, 3), (22, 3), (44, 5), (45, 5), (60, 2), (61, 2), (62, 2), (63, 2), (80, 4), (81, 4), (90, 6)]
    g = [list(r) for r in rows]
    for x, y in pebbles:
        g[y][x] = '#'
    game[name] = [''.join(r) for r in g]

ground('ground')

def write_game_cs(path):
    lines = ['// <auto-generated> by tools/dino-art.py — edit the script, not this file. </auto-generated>',
             '',
             'namespace TaskbarDino.Core.Content;',
             '',
             "/// <summary>Game obstacle and decor grids ('#' body, '.' transparent), at the dino's pixel density.</summary>",
             'internal static class GameArt',
             '{']
    for name, rows in game.items():
        lines.append(f'    public static readonly string[] {name.title().replace("_", "")} =')
        lines.append('    [')
        for r in rows:
            lines.append(f'        "{r}",')
        lines.append('    ];')
        lines.append('')
    lines[-1] = '}'
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')

def write_cs(path):
    lines = ['// <auto-generated> by tools/dino-art.py — edit the script, not this file. </auto-generated>',
             '',
             'namespace TaskbarDino.Core.Content;',
             '',
             '/// <summary>The dino pose grids, 48×48 each: \'o\' outline, \'#\' body, \'=\' belly, \'.\' transparent.</summary>',
             'internal static class DinoArt',
             '{']
    for name, rows in poses.items():
        lines.append(f'    public static readonly string[] {name.title().replace("_", "")} =')
        lines.append('    [')
        for r in rows:
            lines.append(f'        "{r}",')
        lines.append('    ];')
        lines.append('')
    lines.append('    /// <summary>Eye top-left per pose (the eye is 5×5 with a 2×2 pupil).</summary>')
    lines.append('    public static (int X, int Y) Eye(string pose) => pose switch')
    lines.append('    {')
    for name, (x, y) in eyes.items():
        lines.append(f'        "{name}" => ({x}, {y}),')
    lines.append('        _ => (29, 4),')
    lines.append('    };')
    lines.append('}')
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')

if __name__ == '__main__':
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(root, 'src', 'TaskbarDino.Core', 'Content', 'DinoArt.cs')
    write_cs(out)
    write_game_cs(os.path.join(root, 'src', 'TaskbarDino.Core', 'Content', 'GameArt.cs'))
    print(f'wrote {out} ({len(poses)} poses, {len(game)} game sprites)')
    for name in ('cactus_small', 'ptero1', 'cloud', 'ground'):
        print(name)
        print('\n'.join(game[name]))
