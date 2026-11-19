"""Learn the default Expression Map of GIN Pad life hack from finished Studio scenes.

Reads the Timeline data stored in scene PNG files (or already extracted XML), collects
every keyed face pose of KKPE "BS (Face, L)" / "BS (Eyebrows, L)" rows and the single
teeth row, and writes src/Pad/DefaultExpressionMap.cs:

  * eye poses   -> ROWS of the map    (ordered open -> closed)
  * mouth poses -> COLUMNS of the map (ordered closed -> open)
  * brow poses  -> the brow strip under the map

Each row / column is a pose that was really keyed (a cluster medoid), so every stop on the
map is an expression the animator has used. Neighbouring stops are ordered to be as similar
as possible, which keeps the blend between them smooth. No two stops are near-duplicates:
they are at least --apart weight units from each other.

Usage:  python learn_expression_map.py scene1.png scene2.png [--rows 20] [--columns 28] [--brows 11]
Needs:  numpy
"""
from pathlib import Path
import argparse
import collections
import itertools
import re
import xml.etree.ElementTree as ET

import numpy as np

FACE, BROWS, TEETH = 'cf_O_face', 'cf_O_mayuge', 'cf_O_tooth'
EYE_PREFIX = 'eye_face.'


def timeline_roots(path):
    """Every Timeline <root> element found in a scene PNG or XML file."""
    data = Path(path).read_bytes()
    for match in re.finditer(rb'<root[ >]', data):
        end = data.find(b'</root>', match.start())
        if end < 0:
            continue
        try:
            root = ET.fromstring(data[match.start():end + 7])
        except ET.ParseError:
            continue
        if root.find('.//interpolable') is not None:
            yield root


def value_at(track, time):
    """Value of the last key at or before a time (the first key before the track starts)."""
    times = sorted(track)
    earlier = [t for t in times if t <= time]
    return track[earlier[-1]] if earlier else track[times[0]]


def collect(paths):
    """Face poses of all characters: eye, mouth (+ teeth) and brow vectors, one per face key."""
    names = {}
    eyes, mouths, brows = [], [], []
    for path in paths:
        for root in timeline_roots(path):
            characters = collections.defaultdict(dict)
            for row in root.iter('interpolable'):
                keys = row.findall('keyframe')
                if row.get('owner') != 'KKPE' or not keys:
                    continue
                renderer = (row.get('parameter') or row.get('parameter1') or '').split('/')[-1]
                tracks = characters[row.get('objectIndex')]
                if row.get('id') in ('groupBlendShape', 'groupBlendShapeLink') and renderer in (FACE, BROWS):
                    count = int(keys[0].get('valueCount'))
                    names.setdefault(renderer, [keys[0].get('name%d' % i) for i in range(count)])
                    tracks[renderer] = {
                        float(k.get('time')): [float(k.get('value%d' % i)) for i in range(count)] for k in keys}
                elif row.get('id') == 'lastBlendShape' and renderer == TEETH:
                    tracks[TEETH] = {float(k.get('time')): float(k.get('value')) for k in keys}
            split = sum(1 for name in names.get(FACE, []) if name.startswith(EYE_PREFIX))
            for tracks in characters.values():
                if FACE not in tracks:
                    continue
                for time, face in sorted(tracks[FACE].items()):
                    eyes.append(face[:split])
                    mouths.append(face[split:] + [value_at(tracks[TEETH], time) if TEETH in tracks else 0.0])
                    brows.append(value_at(tracks[BROWS], time) if BROWS in tracks else [0.0] * len(names[BROWS]))
    split = sum(1 for name in names[FACE] if name.startswith(EYE_PREFIX))
    return (names[FACE][:split], names[FACE][split:] + ['teeth'], names[BROWS],
            np.array(eyes), np.array(mouths), np.array(brows))


def distances(a, b):
    """Euclidean distance between every pose of a and every pose of b."""
    return np.sqrt(((a[:, None, :] - b[None]) ** 2).sum(-1))


def medoids(poses, count, apart, seed=7):
    """k-means on the poses, then each centre snapped to the nearest pose that was really keyed.

    Stops closer to each other than `apart` would look like the same expression twice, so one
    of such a pair is replaced by the keyed pose that is farthest from every stop.
    """
    unique = np.unique(np.round(poses), axis=0)
    count = min(count, len(unique))
    rng = np.random.default_rng(seed)
    best = None
    for _ in range(60):
        centres = unique[rng.choice(len(unique), count, replace=False)].astype(float)
        for _ in range(80):
            nearest = np.argmin(distances(poses, centres), axis=1)
            moved = np.array([poses[nearest == i].mean(0) if (nearest == i).any() else centres[i] for i in range(count)])
            if np.allclose(moved, centres):
                break
            centres = moved
        stops = unique[np.unique(np.argmin(distances(centres, unique), axis=1))]
        for _ in range(4 * count):
            between = distances(stops, stops) + np.eye(len(stops)) * 1e9
            if len(stops) == count and between.min() >= apart:
                break
            if len(stops) == count:
                stops = np.delete(stops, np.unravel_index(between.argmin(), between.shape)[0], axis=0)
            stops = np.vstack([stops, unique[np.argmax(distances(unique, stops).min(1))]])
        error = distances(poses, stops).min(1).mean()
        gap = (distances(stops, stops) + np.eye(len(stops)) * 1e9).min()
        if len(stops) == count and (best is None or (gap >= apart, -error) > (best[2] >= apart, -best[0])):
            best = (error, stops, gap)
    return best[1], best[0], best[2]


def smooth_order(poses):
    """Order with a small total change between neighbours.

    Exact (Held-Karp) for up to 13 poses; beyond that the best of many 2-opt / or-opt searches.
    """
    n = len(poses)
    distance = distances(poses, poses)
    if n <= 13:
        cost = {(1 << i, i): (0.0, None) for i in range(n)}
        for size in range(2, n + 1):
            for subset in itertools.combinations(range(n), size):
                mask = sum(1 << i for i in subset)
                for last in subset:
                    previous = mask ^ (1 << last)
                    cost[(mask, last)] = min((cost[(previous, k)][0] + distance[k][last], k) for k in subset if k != last)
        mask = (1 << n) - 1
        last = min(range(n), key=lambda i: cost[(mask, i)][0])
        order = []
        while last is not None:
            order.append(last)
            mask, last = mask ^ (1 << last), cost[(mask, last)][1]
        return order

    def length(order):
        return sum(distance[a][b] for a, b in zip(order, order[1:]))

    rng = np.random.default_rng(11)
    best = None
    for start in range(4 * n):
        if start < n:
            # nearest neighbour from every pose, then random starts
            order, left = [start], set(range(n)) - {start}
            while left:
                nearest = min(left, key=lambda j: distance[order[-1]][j])
                order.append(nearest)
                left.remove(nearest)
        else:
            order = list(rng.permutation(n))
        improved = True
        while improved:
            improved = False
            current = length(order)
            for i in range(n - 1):
                for j in range(i + 1, n):
                    reversed_part = order[:i] + order[i:j + 1][::-1] + order[j + 1:]        # 2-opt
                    if length(reversed_part) < current - 1e-9:
                        order, current, improved = reversed_part, length(reversed_part), True
            for size in (1, 2, 3):                                                         # or-opt
                for i in range(n - size + 1):
                    piece, rest = order[i:i + size], order[:i] + order[i + size:]
                    for j in range(len(rest) + 1):
                        for candidate in (rest[:j] + piece + rest[j:], rest[:j] + piece[::-1] + rest[j:]):
                            if length(candidate) < current - 1e-9:
                                order, current, improved = candidate, length(candidate), True
        if best is None or length(order) < length(best):
            best = order
    return best


def weight_of(names, suffix_is_closed):
    """1 for shapes whose name ends in _cl (or does not, when suffix_is_closed is False)."""
    return np.array([1.0 if name.endswith('_cl') == suffix_is_closed else 0.0 for name in names])


def csharp_rows(rows, used):
    return '\n'.join(
        '            new float[] { ' + ', '.join('%gf' % round(float(row[i])) for i in used) + ' },' for row in rows)


def csharp_names(names, used):
    return ', '.join('"%s"' % names[i] for i in used)


def short(name):
    return name.split('.')[-1].replace('f00_', '').replace('mayu00_', '')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('scenes', nargs='+')
    parser.add_argument('--rows', type=int, default=20, help='eye poses')
    parser.add_argument('--columns', type=int, default=28, help='mouth poses')
    parser.add_argument('--brows', type=int, default=11, help='brow poses on the strip')
    parser.add_argument('--apart', type=float, default=20.0,
                        help='smallest allowed distance between two eye or two mouth poses (weight units)')
    parser.add_argument('--brows-apart', type=float, default=8.0,
                        help='smallest allowed distance between two brow poses; keyed brows vary far less')
    parser.add_argument('--output', type=Path,
                        default=Path(__file__).resolve().parent.parent / 'Pad' / 'DefaultExpressionMap.cs')
    args = parser.parse_args()

    eye_names, mouth_names, brow_names, eyes, mouths, brows = collect(args.scenes)
    eye_rows, eye_error, eye_gap = medoids(eyes, args.rows, args.apart)
    mouth_columns, mouth_error, mouth_gap = medoids(mouths, args.columns, args.apart)
    brow_stops, brow_error, brow_gap = medoids(brows, args.brows, args.brows_apart)

    eye_rows = eye_rows[smooth_order(eye_rows)]
    if eye_rows[0] @ weight_of(eye_names, True) > eye_rows[-1] @ weight_of(eye_names, True):
        eye_rows = eye_rows[::-1]                      # top row: most open eyes
    mouth_columns = mouth_columns[smooth_order(mouth_columns)]
    if mouth_columns[0] @ weight_of(mouth_names, False) > mouth_columns[-1] @ weight_of(mouth_names, False):
        mouth_columns = mouth_columns[::-1]            # left column: most closed mouth
    brow_stops = brow_stops[smooth_order(brow_stops)]

    teeth = len(mouth_names) - 1
    eye_used = [i for i in range(len(eye_names)) if eye_rows[:, i].max() > 0.5]
    mouth_used = [i for i in range(teeth) if mouth_columns[:, i].max() > 0.5]
    brow_used = [i for i in range(len(brow_names)) if brow_stops[:, i].max() > 0.5]

    args.output.write_text(TEMPLATE % {
        'poses': len(eyes),
        'eye_error': eye_error, 'mouth_error': mouth_error, 'brow_error': brow_error,
        'eye_names': csharp_names(eye_names, eye_used), 'eyes': csharp_rows(eye_rows, eye_used),
        'mouth_names': csharp_names(mouth_names, mouth_used), 'mouths': csharp_rows(mouth_columns, mouth_used),
        'teeth': ', '.join('%gf' % round(float(v)) for v in mouth_columns[:, teeth]),
        'brow_names': csharp_names(brow_names, brow_used), 'brows': csharp_rows(brow_stops, brow_used),
    }, encoding='utf-8', newline='\n')

    print('%d poses -> %d eye rows (mean error %.1f, closest pair %.1f), %d mouth columns (%.1f, %.1f), %d brow stops (%.1f, %.1f)' % (
        len(eyes), len(eye_rows), eye_error, eye_gap, len(mouth_columns), mouth_error, mouth_gap,
        len(brow_stops), brow_error, brow_gap))
    for label, rows, names, used in (('eyes', eye_rows, eye_names, eye_used),
                                     ('mouth', mouth_columns, mouth_names, mouth_used + [teeth]),
                                     ('brows', brow_stops, brow_names, brow_used)):
        for index, row in enumerate(rows):
            print('  %-9s %2d  %s' % (label, index, '  '.join(
                '%s %d' % (short(names[i]), round(row[i])) for i in used if row[i] > 0.5)))


TEMPLATE = """// Generated by src/tools/learn_expression_map.py from %(poses)d keyed face poses of finished scenes.
// Do not edit by hand: run the tool again to learn a new default map.
//
// Every row and column is a pose that was really keyed. Weights are KKPE Blend Shape weights (0-100).
// Mean distance of a keyed pose to its nearest stop: eyes %(eye_error).1f, mouth %(mouth_error).1f, brows %(brow_error).1f.
namespace GinPadHack
{
    internal static class DefaultExpressionMap
    {
        // Rows, top to bottom: eyes from open to closed.
        internal static readonly string[] EyeShapes = { %(eye_names)s };
        internal static readonly float[][] Eyes =
        {
%(eyes)s
        };

        // Columns, left to right: mouth from closed to open.
        // Teeth: weight of the single teeth row (Blend Shape 29 of cf_O_tooth) for each column.
        internal static readonly string[] MouthShapes = { %(mouth_names)s };
        internal static readonly float[][] Mouths =
        {
%(mouths)s
        };
        internal static readonly float[] Teeth = { %(teeth)s };

        // Brow strip under the map, left to right.
        internal static readonly string[] BrowShapes = { %(brow_names)s };
        internal static readonly float[][] Brows =
        {
%(brows)s
        };
    }
}
"""

if __name__ == '__main__':
    main()
