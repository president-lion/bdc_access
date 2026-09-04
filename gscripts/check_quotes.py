# A raw " inside a C# verbatim string (@"...") ends the string early, and the compiler
# then reports a syntax error somewhere else entirely. This has bitten three times, every
# time from a quote inside a GML comment.
#
# Counting quotes per line does NOT catch it: the extraction stops at the stray quote, so
# the evidence lands outside the region being checked, and a comment with two stray quotes
# passes a parity test anyway. What is reliable is where the string ENDS - valid C# after
# a verbatim string continues with punctuation, never with prose.
import io, sys

p = sys.argv[1] if len(sys.argv) > 1 else 'gscripts/inject_a11y.csx'
u = io.open(p, encoding='utf-8').read()

OK_AFTER = set(';.,)+ \t\r\n')
i, n, bad = 0, 0, []
while True:
    m = u.find('@"', i)
    if m < 0:
        break
    j = m + 2
    while True:                       # find the terminator: a quote not part of a "" pair
        k = u.find('"', j)
        if k < 0:
            j = len(u)
            break
        if u[k:k + 2] == '""':
            j = k + 2
            continue
        j = k + 1
        break
    n += 1
    line = u[:j].count('\n') + 1
    nxt = u[j:j + 1]
    if nxt and nxt not in OK_AFTER:
        bad.append((line, repr(u[max(0, j - 60):j + 20])))
    i = j

print('verbatim strings: %d, strings ending mid-text: %d' % (n, len(bad)))
for ln, ctx in bad:
    print('  line %d ends here: %s' % (ln, ctx))
sys.exit(1 if bad else 0)
