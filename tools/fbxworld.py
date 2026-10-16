import sys, numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from fbxinfo import parse, find

def rotmat(deg, order='XYZ'):
    rx, ry, rz = np.radians(deg)
    cx, sx = np.cos(rx), np.sin(rx); cy, sy = np.cos(ry), np.sin(ry); cz, sz = np.cos(rz), np.sin(rz)
    RX = np.array([[1,0,0],[0,cx,-sx],[0,sx,cx]])
    RY = np.array([[cy,0,sy],[0,1,0],[-sy,0,cy]])
    RZ = np.array([[cz,-sz,0],[sz,cz,0],[0,0,1]])
    # FBX eulerXYZ: R = Rz * Ry * Rx
    return RZ @ RY @ RX

def load(path):
    ver, top = parse(path)
    obj = find(top, 'Objects')[0][2]
    models = {n[1][0]: n for n in obj if n[0] == 'Model'}
    geoms = {n[1][0]: n for n in obj if n[0] == 'Geometry'}
    conns = find(top, 'Connections')[0][2]
    geom_of = {}
    for c in conns:
        if c[1][0] == 'OO' and c[1][1] in geoms and c[1][2] in models:
            geom_of[c[1][2]] = c[1][1]
    out = []
    for mid, m in models.items():
        props = {}
        p70 = find(m[2], 'Properties70')
        if p70:
            for p in find(p70[0][2], 'P'):
                props[p[1][0]] = np.array([float(x) for x in p[1][4:]]) if len(p[1]) > 4 and not isinstance(p[1][4], str) else p[1][4:]
        name = m[1][1].split('\x00')[0]
        T = props.get('Lcl Translation', np.zeros(3))
        R = props.get('Lcl Rotation', np.zeros(3))
        S = props.get('Lcl Scaling', np.ones(3))
        pre = props.get('PreRotation', np.zeros(3))
        rp = props.get('RotationPivot', np.zeros(3))
        sp = props.get('ScalingPivot', np.zeros(3))
        g = geoms[geom_of[mid]]
        verts = idx = None
        for k in g[2]:
            if k[0] == 'Vertices': verts = k[1][0].reshape(-1, 3)
            if k[0] == 'PolygonVertexIndex': idx = k[1][0]
        # triangulate fan
        tris = []; poly = []
        for i in idx:
            if i < 0:
                poly.append(~i)
                for j in range(1, len(poly) - 1):
                    tris.append((poly[0], poly[j], poly[j+1]))
                poly = []
            else:
                poly.append(i)
        tris = np.array(tris)
        Rm = rotmat(pre) @ rotmat(R)
        w = (verts * S) @ Rm.T + T
        out.append(dict(name=name, props=props, verts=verts, world=w, tris=tris, T=T, R=R, S=S, pre=pre, rp=rp, sp=sp))
    return out

def render(objs, path, scale_div=1.0):
    allw = np.vstack([o['world'] for o in objs])
    mn, mx = allw.min(0), allw.max(0)
    ctr = (mn + mx) / 2
    ext = (mx - mn).max()
    W = 520
    views = {'front (X,Y) -Z': (0, 1, 2, 1), 'side (Z,Y)': (2, 1, 0, 1), 'top (X,Z)': (0, 2, 1, -1)}
    img = Image.new('RGB', (W * 3, W), (24, 26, 30))
    cols = [(120, 170, 220), (230, 150, 90), (140, 220, 140), (220, 100, 200)]
    light = np.array([0.4, 0.8, 0.5]); light /= np.linalg.norm(light)
    for vi, (title, (ax, ay, az, sg)) in enumerate(views.items()):
        d = ImageDraw.Draw(img)
        tri_list = []
        for oi, o in enumerate(objs):
            w = o['world']
            for t in o['tris']:
                p = w[t]
                n = np.cross(p[1] - p[0], p[2] - p[0]); nn = np.linalg.norm(n)
                if nn == 0: continue
                n /= nn
                depth = p[:, az].mean() * sg
                shade = 0.35 + 0.65 * abs(float(n @ light))
                tri_list.append((depth, oi, p, shade))
        tri_list.sort(key=lambda x: x[0])
        for depth, oi, p, shade in tri_list:
            pts = [((q[ax] - ctr[ax]) / ext * (W * 0.85) + W / 2 + vi * W,
                    W / 2 - (q[ay] - ctr[ay]) / ext * (W * 0.85)) for q in p]
            c = tuple(int(x * shade) for x in cols[oi % len(cols)])
            d.polygon(pts, fill=c)
        d.text((vi * W + 8, 6), title, fill=(255, 255, 255))
    img.save(path)

if __name__ == '__main__':
    objs = load(sys.argv[1])
    for i, o in enumerate(objs):
        mn, mx = o['world'].min(0), o['world'].max(0)
        print(f"[{i}] {o['name']!r}: pre={o['pre']} R={o['R']} S={o['S']} rp={o['rp']} sp={o['sp']}")
        print(f"     world(cm) min={np.round(mn,1).tolist()} max={np.round(mx,1).tolist()} size={np.round(mx-mn,1).tolist()}")
    render(objs, sys.argv[2])
    print('rendered', sys.argv[2])
