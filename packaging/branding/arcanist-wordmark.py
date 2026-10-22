"""The ARCANIST wordmark, in the geometry of Ultimate Desktop's ULTIMATE one: straight segments only, cap height 70,
vertical strokes 11.6 (11.1 in M/N), horizontal bars 9.9, outer chamfers 11.7 and inner ones 5.7."""
import sys

# Glyphs drawn from x = 0; y up is negative (as in the original). Each is a list of closed polygons; counters are
# separate polygons (the path uses fill-rule evenodd).
A = [  # from ULTIMATE's A (439 -> 0)
    [(0, 0), (25.4, -70), (36.4, -70), (61.7, 0), (49.9, 0), (43.9, -16.6), (17.8, -16.6), (11.8, 0)],
    [(20.6, -26.3), (41.2, -26.3), (30.9, -55.9), (30.7, -55.9)],
]
R = [
    [(0, 0), (0, -70), (40.3, -70), (52, -58.3), (52, -42), (43.7, -33.7), (52, 0), (39.9, 0), (31.5, -30.3), (11.6, -30.3), (11.6, 0)],
    [(11.6, -60.1), (34.7, -60.1), (40.4, -54.4), (40.4, -45.9), (34.7, -40.2), (11.6, -40.2)],
]
C = [
    [(52, 0), (11.7, 0), (0, -11.7), (0, -58.3), (11.7, -70), (52, -70), (52, -60.1), (17.3, -60.1), (11.6, -54.4),
     (11.6, -15.6), (17.3, -9.9), (52, -9.9)],
]
N = [
    [(0, 0), (0, -70), (11.1, -70), (43.7, -19.5), (43.9, -19.5), (43.9, -70), (55, -70), (55, 0), (43.9, 0), (11.3, -50.5),
     (11.1, -50.5), (11.1, 0)],
]
I = [[(0, 0), (0, -70), (11.6, -70), (11.6, 0)]]
S = [
    [(52, -70), (11.7, -70), (0, -58.3), (0, -41.8), (11.7, -30.3), (34.7, -30.3), (40.4, -24.6), (40.4, -15.6), (34.7, -9.9),
     (0, -9.9), (0, 0), (40.3, 0), (52, -11.7), (52, -28.4), (40.4, -40.1), (17.3, -40.1), (11.6, -45.8), (11.6, -54.4),
     (17.3, -60.1), (52, -60.1)],
]
T = [  # from ULTIMATE's T (533.7 -> 0)
    [(21, 0), (21, -60.1), (0, -60.1), (0, -70), (53.6, -70), (53.6, -60.1), (32.6, -60.1), (32.6, 0)],
]

WIDTH = {id(A): 61.7, id(R): 52, id(C): 52, id(N): 55, id(I): 11.6, id(S): 52, id(T): 53.6}
# Letters and the space before each, as ULTIMATE spaces them: about 44 between straight sides, less by diagonals and T.
LETTERS = [(A, 0), (R, 36), (C, 40), (A, 34), (N, 38), (I, 44), (S, 40), (T, 34)]


def path():
    x = 7.0
    parts = []
    for glyph, gap in LETTERS:
        x += gap
        for polygon in glyph:
            points = [f"{px + x:.2f} {py:.2f}".replace(".00", "") for px, py in polygon]
            parts.append("M" + "L".join(points) + "Z")
        x += WIDTH[id(glyph)]
    return " ".join(parts), x


d, end = path()
width = end + 7
svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 -80 {width:.1f} 90" role="img" aria-label="Arcanist">\n'
       f'  <title>Arcanist</title>\n  <path fill="#8E51FF" fill-rule="evenodd" d="{d}" />\n</svg>\n')
open(sys.argv[1], 'w', encoding='utf-8', newline='\n').write(svg)
print(f"{width:.1f}")
print(d)
