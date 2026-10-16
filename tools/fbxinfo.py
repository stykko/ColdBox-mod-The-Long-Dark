import struct, zlib, sys
import numpy as np

def read_prop(f):
    t = f.read(1).decode()
    if t in 'CYIFDL':
        fmt = {'C': '<?', 'Y': '<h', 'I': '<i', 'F': '<f', 'D': '<d', 'L': '<q'}[t]
        return struct.unpack(fmt, f.read(struct.calcsize(fmt)))[0]
    if t in 'fdilb':
        n, enc, clen = struct.unpack('<III', f.read(12))
        raw = f.read(clen)
        if enc == 1:
            raw = zlib.decompress(raw)
        dt = {'f': '<f4', 'd': '<f8', 'i': '<i4', 'l': '<i8', 'b': '?'}[t]
        return np.frombuffer(raw, dtype=dt)
    if t in 'SR':
        n = struct.unpack('<I', f.read(4))[0]
        raw = f.read(n)
        return raw if t == 'R' else raw.decode('utf-8', 'replace')
    raise ValueError(t)

def read_node(f, ver):
    hdr = 25 if ver >= 7500 else 13
    if ver >= 7500:
        end, nprops, plen, nlen = struct.unpack('<QQQB', f.read(25))
    else:
        end, nprops, plen, nlen = struct.unpack('<IIIB', f.read(13))
    if end == 0:
        return None
    name = f.read(nlen).decode()
    props = [read_prop(f) for _ in range(nprops)]
    kids = []
    while f.tell() < end:
        k = read_node(f, ver)
        if k is None:
            break
        kids.append(k)
    f.seek(end)
    return (name, props, kids)

def parse(path):
    f = open(path, 'rb')
    f.read(21 + 2)
    ver = struct.unpack('<I', f.read(4))[0]
    nodes = []
    while True:
        n = read_node(f, ver)
        if n is None:
            break
        nodes.append(n)
    return ver, nodes

def find(nodes, name):
    return [n for n in nodes if n[0] == name]

if __name__ == '__main__':
    ver, top = parse(sys.argv[1])
    print('FBX version', ver)
    for n in top:
        print('top:', n[0])
    gs = find(top, 'GlobalSettings')
    if gs:
        for p in find(find(gs[0][2], 'Properties70')[0][2], 'P'):
            if p[1][0] in ('UpAxis', 'FrontAxis', 'CoordAxis', 'UnitScaleFactor', 'OriginalUnitScaleFactor'):
                print('  global', p[1][0], p[1][4])
    obj = find(top, 'Objects')[0][2]
    models = {}
    geoms = {}
    mats = []
    texs = []
    for n in obj:
        if n[0] == 'Model':
            models[n[1][0]] = n
        elif n[0] == 'Geometry':
            geoms[n[1][0]] = n
        elif n[0] == 'Material':
            mats.append(n)
        elif n[0] == 'Texture':
            texs.append(n)
    # connections
    conns = find(top, 'Connections')[0][2]
    parent = {}
    geom_of = {}
    mat_of = {}
    for c in conns:
        kind, a, b = c[1][0], c[1][1], c[1][2]
        if kind == 'OO':
            if a in models and b in models:
                parent[a] = b
            if a in geoms and b in models:
                geom_of[b] = a
            if any(m[1][0] == a for m in mats) and b in models:
                mat_of.setdefault(b, []).append(a)
    def mname(n):
        return n[1][1].split('\x00')[0]
    print('\nMODELS')
    for mid, m in models.items():
        props = {}
        p70 = find(m[2], 'Properties70')
        if p70:
            for p in find(p70[0][2], 'P'):
                props[p[1][0]] = p[1][4:]
        pn = mname(models[parent[mid]]) if mid in parent else '(root)'
        print(f' - {mname(m)!r} type={m[1][2]} parent={pn}')
        for k in ('Lcl Translation', 'Lcl Rotation', 'Lcl Scaling', 'PreRotation', 'GeometricTranslation'):
            if k in props:
                print(f'     {k}: {[round(float(x),4) for x in props[k]]}')
        if mid in geom_of:
            g = geoms[geom_of[mid]]
            verts = None; idx = None; uv = None
            for k in g[2]:
                if k[0] == 'Vertices':
                    verts = k[1][0].reshape(-1, 3)
                if k[0] == 'PolygonVertexIndex':
                    idx = k[1][0]
                if k[0] == 'LayerElementUV':
                    for kk in k[2]:
                        if kk[0] == 'UV':
                            uv = kk[1][0].reshape(-1, 2)
            nface = int((idx < 0).sum())
            tris = 0
            cur = 0
            for i in idx:
                cur += 1
                if i < 0:
                    tris += cur - 2
                    cur = 0
            mn, mx = verts.min(0), verts.max(0)
            print(f'     verts={len(verts)} polys={nface} tris={tris} uv={"yes" if uv is not None else "NO"}')
            print(f'     bbox min={np.round(mn,3).tolist()} max={np.round(mx,3).tolist()} size={np.round(mx-mn,3).tolist()}')
            if uv is not None:
                print(f'     uv range u[{uv[:,0].min():.3f},{uv[:,0].max():.3f}] v[{uv[:,1].min():.3f},{uv[:,1].max():.3f}]')
            ctr = (mn + mx) / 2
            print(f'     bbox center={np.round(ctr,3).tolist()}')
    print('\nMATERIALS', [mname(m) for m in mats])
    print('TEXTURES', [mname(t) for t in texs])
