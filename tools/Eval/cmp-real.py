import re,sys,os
# usage: cmp-real.py <before dir> <after dir>; prints, per country, field F1 and full-parse of the "model" (no hint) and "model-hint" rows, before -> after, with the delta against libpostal and its interval.
pat=re.compile(r'^address (field F1|full-parse accuracy) \(coarse\)\s+(\S+)\s+n=(\d+)\s+([\d.]+) \[\s*([\d.]+),\s*([\d.]+)\](?:\s+vs_libpostal ([+-][\d.]+) \[\s*([+-][\d.]+),\s*([+-][\d.]+)\])?')
def load(p):
    d={}
    if not os.path.exists(p): return d
    for l in open(p):
        m=pat.match(l)
        if m: d[(m[1][:5],m[2])]=(float(m[4]),float(m[5]),float(m[6]),m[7] and float(m[7]),m[8] and float(m[8]),m[9] and float(m[9]),int(m[3]))
    return d
b,a=sys.argv[1],sys.argv[2]
for f in sorted(os.listdir(a)):
    if not f.endswith('.txt'): continue
    cc=f[:-4]; x=load(os.path.join(b,f)); y=load(os.path.join(a,f))
    for sysname in ('model-hint','model'):
        for kind in ('field','full-'):
            k=(kind,sysname)
            if k in x and k in y:
                p,q=x[k],y[k]
                dl='' if q[3] is None else f' vs_lp {q[3]:+.2f} [{q[4]:+.2f},{q[5]:+.2f}]'
                lp=y.get((kind,'libpostal'))
                print(f'{cc} n={q[6]:3} {sysname:10} {kind:5} {p[0]:6.2f} -> {q[0]:6.2f} ({q[0]-p[0]:+5.2f}) [{q[1]:.1f},{q[2]:.1f}]{dl}')
