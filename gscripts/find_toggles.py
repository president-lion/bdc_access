# Derives the switch table baked into inject_a11y.csx. Re-run against research/gml after a
# game update and diff the output against the table in the injector.
#
# Looks for the two shapes a toggle takes in this game's decompiled source:
#   if (V) { V = 0; } else { V = 1; }      (and the negated form)
#   V = !V;
# where V may be plain (self), dotted (Other.var), or sit inside a with (Other) block.
import io, os, re, sys

g = sys.argv[1] if len(sys.argv) > 1 else 'research/gml'
V = r'[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?'
pat_with = re.compile(r'with \((?P<tgt>[A-Za-z_]\w*)\)\s*\{(?P<body>(?:[^{}]|\{[^{}]*\})*)\}', re.S)


def toggles(src):
    out = []
    for m in re.finditer(r'if \((?P<v>%s)\)\s*\{\s*(?P=v)\s*=\s*0;\s*\}\s*else\s*\{\s*(?P=v)\s*=\s*1;\s*\}' % V, src, re.S):
        out.append((m.start(), m.end(), m.group('v')))
    for m in re.finditer(r'if \(!(?P<v>%s)\)\s*\{\s*(?P=v)\s*=\s*1;\s*\}\s*else\s*\{\s*(?P=v)\s*=\s*0;\s*\}' % V, src, re.S):
        out.append((m.start(), m.end(), m.group('v')))
    for m in re.finditer(r'(?m)^\s*(?P<v>%s)\s*=\s*!\s*(?P=v)\s*;' % V, src):
        out.append((m.start(), m.end(), m.group('v')))
    return out


hits = set()
for fn in sorted(os.listdir(g)):
    m = re.match(r'gml_Object_(.+)_Other_10\.gml$', fn)
    if not m:
        continue
    src = io.open(os.path.join(g, fn), encoding='utf-8', errors='replace').read()
    withs = list(pat_with.finditer(src))
    for s, e, v in toggles(src):
        inw = [w for w in withs if s > w.start() and e < w.end()]
        tgt = inw[-1].group('tgt') if inw else 'self'
        if '.' in v:
            tgt, v = v.split('.', 1)
        hits.add((m.group(1), tgt, v))

print('%d toggles' % len(hits))
for h in sorted(hits):
    print('  %-44s %-30s %s' % h)
