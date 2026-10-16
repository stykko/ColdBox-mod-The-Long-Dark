"""FBX (binary 7400) -> parts.bin + meta.txt, to be embedded in the mod DLL.
Converts to Unity space: meters, scaled so the lid is TARGET_LEN long, floor at y=0, centered on XZ with the lid,
mirrored on X (FBX is right-handed, Unity is left-handed) with the triangle order reversed."""
import sys, struct
import numpy as np
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from fbxinfo import parse, find
from fbxworld import rotmat

TARGET_LEN = 1.2          # meters: length of the lid
fbx_path, out_bin, out_meta = sys.argv[1:4]
ver, top = parse(fbx_path)
obj = find(top, 'Objects')[0][2]
models = {n[1][0]: n for n in obj if n[0] == 'Model'}
geoms = {n[1][0]: n for n in obj if n[0] == 'Geometry'}
conns = find(top, 'Connections')[0][2]
geom_of = {c[1][2]: c[1][1] for c in conns if c[1][0] == 'OO' and c[1][1] in geoms and c[1][2] in models}


def kid(node, name):
    r = [k for k in node[2] if k[0] == name]
    return r[0] if r else None


def load_mesh(mid):
    m = models[mid]
    props = {}
    p70 = kid(m, 'Properties70')
    for p in (p70[2] if p70 else []):
        if p[0] == 'P' and len(p[1]) > 4 and not isinstance(p[1][4], str):
            props[p[1][0]] = np.array([float(x) for x in p[1][4:]])
    T = props.get('Lcl Translation', np.zeros(3)); R = props.get('Lcl Rotation', np.zeros(3)); S = props.get('Lcl Scaling', np.ones(3))
    pre = props.get('PreRotation', np.zeros(3))
    Rm = rotmat(pre) @ rotmat(R)
    g = geoms[geom_of[mid]]
    verts = kid(g, 'Vertices')[1][0].reshape(-1, 3)
    pvi = kid(g, 'PolygonVertexIndex')[1][0]
    # normals and UVs per polygon-vertex
    ln = kid(g, 'LayerElementNormal'); lu = kid(g, 'LayerElementUV')
    normals = kid(ln, 'Normals')[1][0].reshape(-1, 3)
    n_ref = kid(ln, 'ReferenceInformationType')[1][0]; n_map = kid(ln, 'MappingInformationType')[1][0]
    n_idx = kid(ln, 'NormalsIndex')[1][0] if kid(ln, 'NormalsIndex') else None
    uvs = kid(lu, 'UV')[1][0].reshape(-1, 2)
    u_idx = kid(lu, 'UVIndex')[1][0] if kid(lu, 'UVIndex') else None
    u_map = kid(lu, 'MappingInformationType')[1][0]
    assert n_map in ('ByPolygonVertex', 'ByVertice', 'ByVertex'), n_map
    assert u_map in ('ByPolygonVertex', 'ByVertice', 'ByVertex'), u_map
    # world transform
    wpos = (verts * S) @ Rm.T + T
    # normals: inverse transpose of S, then R
    def wnorm(n):
        v = (n / S) @ Rm.T
        l = np.linalg.norm(v, axis=-1, keepdims=True)
        return v / np.where(l == 0, 1, l)
    polys = []; cur = []
    for k, i in enumerate(pvi):
        cur.append(k)
        if i < 0:
            polys.append(cur); cur = []
    corners = []   # (pos_index, normal, uv) per polygon corner; fan triangulation
    tris = []      # indices into corners
    corner_pos, corner_n, corner_uv = [], [], []
    for poly in polys:
        base = len(corner_pos)
        for k in poly:
            p = pvi[k]; p = ~p if p < 0 else p
            corner_pos.append(p)
            kn = k if n_map == 'ByPolygonVertex' else p      # FBX mapping: per polygon corner or per control point
            ku = k if u_map == 'ByPolygonVertex' else p
            corner_n.append(normals[n_idx[kn]] if n_idx is not None else normals[kn])
            corner_uv.append(uvs[u_idx[ku]] if u_idx is not None else uvs[ku])
        for j in range(1, len(poly) - 1):
            tris.append((base, base + j, base + j + 1))
    corner_pos = np.array(corner_pos); corner_n = wnorm(np.array(corner_n)); corner_uv = np.array(corner_uv)
    return dict(name=m[1][1].split('\x00')[0], wpos=wpos, cpos=corner_pos, cn=corner_n, cuv=corner_uv, tris=np.array(tris), nverts=len(verts), det=float(np.linalg.det(np.diag(S))))


ms = [load_mesh(mid) for mid in models]
# identify by size: body > lid > latch
def bsize(m):
    w = m['wpos']; return float(np.linalg.norm(w.max(0) - w.min(0)))
ms.sort(key=bsize, reverse=True)
by_name = {m['name']: m for m in ms}
body = ms[0]
lid = by_name.get('Cube.007') or ms[1]
snowm = by_name.get('Cube.001')              # the snow slab (new model files only)
others = [m for m in ms if m is not body and m is not lid and m is not snowm]
latch = others[0] if others else None        # the old model had a separate latch/handle mesh
lid_w = lid['wpos']; body_w = body['wpos']
lid_len_cm = lid_w[:, 2].max() - lid_w[:, 2].min()
SC = TARGET_LEN / (lid_len_cm * 0.01)          # factor extra sobre cm->m
K = 0.01 * SC                                  # cm -> metros finales
floor = np.array([(lid_w[:, 0].max() + lid_w[:, 0].min()) / 2, body_w[:, 1].min(), (lid_w[:, 2].max() + lid_w[:, 2].min()) / 2])

def to_unity_pos(p):
    q = (p - floor) * K
    q[..., 0] *= -1.0
    return q
def to_unity_n(n):
    q = n.copy(); q[..., 0] *= -1.0; return q

def build_part(m, tri_subset=None):
    """Returns (pos, nrm, uv, idx) with unique vertices per (position, normal, uv)."""
    tris = m['tris'] if tri_subset is None else m['tris'][tri_subset]
    cmap = {}; P = []; N = []; U = []; I = []
    for tri in tris:
        row = []
        for c in tri:
            key = (int(m['cpos'][c]), tuple(np.round(m['cn'][c], 3)), tuple(np.round(m['cuv'][c], 4)))
            if key not in cmap:
                cmap[key] = len(P)
                P.append(to_unity_pos(m['wpos'][m['cpos'][c]].copy()))
                N.append(to_unity_n(m['cn'][c]))
                U.append(m['cuv'][c])
            row.append(cmap[key])
        a, b, c3 = row
        I += [a, c3, b]            # mirrored on X -> reverse the winding order
    return np.array(P, np.float32), np.array(N, np.float32), np.array(U, np.float32), np.array(I, np.int32)

# ---- body: connected components ----
par = list(range(body['nverts']))
def f(x):
    while par[x] != x:
        par[x] = par[par[x]]; x = par[x]
    return x
for t in body['tris']:
    a, b, c = (int(body['cpos'][i]) for i in t)
    par[f(b)] = f(a); par[f(c)] = f(a)
comp_of_tri = np.array([f(int(body['cpos'][t[0]])) for t in body['tris']])
roots = {}
for ti, r in enumerate(comp_of_tri):
    roots.setdefault(r, []).append(ti)
def comp_size(tis):
    pts = body['wpos'][np.unique(body['cpos'][body['tris'][tis]].ravel())]
    return pts.max(0) - pts.min(0)
jug, rest = [], []
for r, tis in roots.items():
    sz = comp_size(tis) / lid_len_cm
    is_jug = sz[1] > 0.40 and sz[0] < 0.40 and sz[2] < 0.40 and abs(sz[0] - sz[2]) < 0.3 * sz[0]
    (jug if is_jug else rest).append((r, tis))
nv = lambda tis: len(np.unique(body['cpos'][body['tris'][tis]].ravel()))
snow_r, snow_t = max(rest, key=lambda rt: nv(rt[1]))
shell_t = [ti for r, tis in rest if r != snow_r for ti in tis]
jug_t = [ti for r, tis in jug for ti in tis]
print(f'components={len(roots)} jugs={len(jug)} ({len(jug_t)} tris) snow={len(snow_t)} tris shell={len(shell_t)} tris')

parts = [('Shell', build_part(body, shell_t)), ('Tarp', build_part(body, snow_t)), ('Jugs', build_part(body, jug_t)),
         ('Lid', build_part(lid))]
if snowm is not None: parts.append(('Snow', build_part(snowm)))
if latch is not None: parts.append(('Latch', build_part(latch)))
# nothing from the file may be left out
used = len(shell_t) + len(snow_t) + len(jug_t) + len(lid['tris']) + (len(snowm['tris']) if snowm else 0) + (len(latch['tris']) if latch else 0)
total = sum(len(m['tris']) for m in ms)
assert used == total, f'triangles lost: {used} of {total}'
print('all', total, 'triangles are in the parts')
P_ = {n: p for n, p in parts}
with open(out_bin, 'wb') as fo:
    fo.write(struct.pack('<ii', 0x314D4243, len(parts)))
    for name, (P, N, U, I) in parts:
        nb = name.encode()
        fo.write(struct.pack('<i', len(nb))); fo.write(nb)
        fo.write(struct.pack('<ii', len(P), len(I)))
        fo.write(P.tobytes()); fo.write(N.tobytes()); fo.write(U.tobytes()); fo.write(I.tobytes())
    print({n: (len(P), len(I) // 3) for n, (P, N, U, I) in parts})

# ---- metadata: hinge, collision ----
lidP = P_['Lid'][0]; shellP = P_['Shell'][0]
latch_x = float(P_['Latch'][0][:, 0].mean()) if 'Latch' in P_ else -0.38   # no latch mesh: keep the previous side (hinge at +X)
lid_min, lid_max = lidP.min(0), lidP.max(0)
# the hinge sits on the long edge of the lid OPPOSITE the latch, axis parallel to Z, at the bottom of the lid
hinge_at_max_x = latch_x < 0
hinge = np.array([lid_max[0] if hinge_at_max_x else lid_min[0], lid_min[1], (lid_min[2] + lid_max[2]) / 2])
open_sign = -1 if hinge_at_max_x else 1     # with the hinge at +X, a negative rotation around Z lifts the free edge (Unity)
# collision box: shell + water jugs + lid, so the player cannot walk through the jugs either
jugP = P_['Jugs'][0]
allP = np.vstack([shellP, jugP, lidP])
col_min, col_max = allP.min(0), allP.max(0)
col_h = float(col_max[1])
meta = {
    'hingeX': hinge[0], 'hingeY': hinge[1], 'hingeZ': hinge[2], 'openSign': open_sign,
    'colSizeX': float(col_max[0] - col_min[0]), 'colSizeY': col_h, 'colSizeZ': float(col_max[2] - col_min[2]),
    'colCenterX': float((col_max[0] + col_min[0]) / 2), 'colCenterY': col_h / 2, 'colCenterZ': float((col_max[2] + col_min[2]) / 2),
    'snowTopY': float(P_['Snow'][0][:, 1].max()) if 'Snow' in P_ else 0.0,
    'snowBottomY': float(P_['Snow'][0][:, 1].min()) if 'Snow' in P_ else 0.0,
    'latchX': latch_x, 'lidMinY': float(lid_min[1]), 'lidMaxY': float(lid_max[1]), 'scaleApplied': K,
}
with open(out_meta, 'w') as fm:
    for k, v in meta.items():
        fm.write(f'{k}={v:.6f}\n' if isinstance(v, float) else f'{k}={v}\n')
print(meta)
