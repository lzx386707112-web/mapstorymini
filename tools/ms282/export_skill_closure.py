#!/usr/bin/env python3
"""Export the TMS 282 5th/6th-job skill dependency closure (read-only).

The 282 client keeps the authoritative skill definitions in the unpacked
``Data/Packs/Skill_*.ms`` images (``Skill/<job>.img``).  Every effect canvas in
those images is a 1x1 / 10-byte placeholder carrying an ``_outlink`` into the
legacy ``Data/Skill/_Canvas`` tree, so the *definition* layer is the only place
that still holds ``common``/``info``/``multiAttackInfo``/frame ``origin``+``delay``.

This tool walks the V / HEXA core layout for the 17 terminal job routes, seeds
from every core's ``connectSkill`` + ``subSkill``, then recursively follows
skill-id references inside the raw IMG nodes.  Nothing is written except the
output JSON.

Usage::

  python3 tools/ms282/export_skill_closure.py \
      --packs /tmp/packs \
      --out docs/tms282-skill/data/skill-closure.json

Notes on the reference walk
---------------------------
* References are only followed when the leaf value is an integer/numeric string
  of 7..9 digits that exists in the pack skill index -- no guessing.
* ``psdSkill/<skillId>`` stores the id in the *node name*, not in a value.
* ``common``/``req``/icon subtrees are stats, never followed (they contain
  numbers that could collide with a skill id by accident).
* ``_outlink`` targets are recorded separately as ``artOutlinks``: they point at
  the ``_Canvas`` tree and often at *other skills'* sprites, so they are an art
  dependency, not a gameplay dependency.
"""
import argparse
import json
import os
import random
import sys
from collections import Counter, OrderedDict, deque

WZPY = '/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python'
sys.path.insert(0, WZPY)

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzSubProperty, WzCanvasProperty  # noqa: E402

MS_DATA = '/Users/lizixian/Documents/mxd/MapleStory/Data'

# 17 terminal (4th job) routes audited by this tool.
JOBS = ('112', '122', '132', '212', '222', '232', '312', '322', '412',
        '422', '512', '522', '1112', '1212', '1312', '1412', '1512')

# Source-tree layer names the client draws.  Any other top-level node that
# carries canvases is treated as a layer too (effectFlash, preloadEff, ...).
EFFECT_LAYERS = (
    'effect', 'effect0', 'effect1', 'effect2',
    'special', 'special0',
    'screen', 'screen0',
    'hit', 'hit0',
    'ball', 'ball0',
    'mob', 'mob0',
    'tile', 'tile0',
    'summon', 'summon0',
    'affected', 'affected0',
    'repeat', 'keydown', 'keydown0', 'keydownend',
    'prepare', 'prepare0', 'finish', 'finish0',
    'afterimage', 'afterimage0',
    'effectFlash', 'effectFlash0', 'effectFlash2',
    'preloadEff', 'trembleEffect',
)

# Container nodes whose children are known skill-id lists.
SKILL_LIST_CONTAINERS = ('skillList', 'skillList2', 'subSkill', 'connectSkill',
                         'psdSkill', 'cancelableSkillID', 'removedSkillID')

# Top-level nodes that are *known* to carry skill ids (either as children or as
# node names).  Anything outside this set is only followed when the leaf name
# itself looks like a skill field -- see RefWalker.classify().
REF_ROOTS = SKILL_LIST_CONTAINERS + (
    'finalAttack', 'changeSkill', 'addAttack', 'extraSkillInfo',
    'checkSkillDamR', 'VsubSkill', 'limitSamePosCountReplaceSkill',
    'newDotParent', 'psdWeaponBooster', 'exceedInfo',
)

# Numeric fields that are geometry/timing, never skill ids.  Without this a
# horizontal offset such as ``multiAttackInfo/0/x = 1141500`` would be mistaken
# for a skill reference (1141500 really is a skill id).
NON_REF_FIELDS = {
    'x', 'y', 'z', 'w', 'h', 'v', 's', 'q', 'u', 'r', 'delay', 'time', 'time2',
    'time3', 'attackTime', 'attackTime2', 'attackTime3', 'hitDelay', 'start',
    'prepare', 'end', 'alpha', 'color', 'origin', 'lt', 'rb', 'range', 'force',
    'reduction', 'interval', 'period', 'duration', 'cooltime', 'mpCon', 'hpCon',
    'damage', 'attackCount', 'bulletCount', 'mobCount', 'cr', 'mdamage',
    'maxLevel', 'index', 'idx', 'count', 'type', 'shape', 'angle', 'speed',
    'gravity', 'zIndex', 'delayTime', 'lifeTime', 'repeat', 'layer',
}

# Subtrees that are pure numeric stats and must never be scanned for ids.
STAT_SUBTREES = ('common', 'req', 'reqLev', 'icon', 'iconMouseOver',
                 'iconDisabled', 'icon2', 'icon3')

MAX_FRAME_META = 16      # per-layer frame detail cap (counts are always full)
MAX_TREE_DEPTH = 8       # multiAttackInfo / nested dump depth cap


# ── low level helpers (same conventions as export_skill_catalog.py) ────
def value_of(node):
    t = type(node).__name__
    if t == 'WzVectorProperty':
        v = getattr(node, 'value', (0, 0))
        return [int(v[0]), int(v[1])]
    if t in ('WzIntProperty', 'WzShortProperty', 'WzLongProperty'):
        return int(node.value)
    if t == 'WzFloatProperty':
        return float(node.value)
    if t == 'WzStringProperty':
        return str(node.value)
    if t == 'WzNullProperty':
        return None
    if t == 'WzUolProperty':
        return str(getattr(node, 'value', None))
    return str(getattr(node, 'value', None))


def load_img(path):
    img = WzImage.from_bytes(open(path, 'rb').read(), key=WzKey.for_region('BMS'),
                             name=os.path.basename(path))
    img.parse()
    if img.truncated:
        raise RuntimeError('truncated ' + path)
    return img


def scalar_dict(node, depth=2):
    out = {}
    for c in node.children():
        if isinstance(c, WzCanvasProperty):
            out[c.name] = {'canvas': [c.width, c.height]}
        elif isinstance(c, WzSubProperty):
            out[c.name] = scalar_dict(c, depth - 1) if depth > 0 else {'sub': True}
        else:
            out[c.name] = value_of(c)
    return out


def dump_tree(node, depth=0, max_depth=MAX_TREE_DEPTH):
    """Lossless-enough recursive dump of a definition subtree.

    Keeps scalars, vectors, canvas placeholders (size + _outlink) and nested
    frame metadata (origin/delay/z/_outlink), i.e. everything a runtime
    importer needs from ``multiAttackInfo`` and friends.
    """
    if isinstance(node, WzCanvasProperty):
        rec = {'_canvas': [node.width, node.height], '_pngBytes': node._png_length}
        if depth < max_depth:
            for c in node.children():
                if isinstance(c, WzCanvasProperty):
                    rec[c.name] = dump_tree(c, depth + 1, max_depth)
                elif isinstance(c, WzSubProperty):
                    rec[c.name] = dump_tree(c, depth + 1, max_depth)
                else:
                    rec[c.name] = value_of(c)
        return rec
    out = OrderedDict()
    for c in node.children():
        if isinstance(c, (WzCanvasProperty, WzSubProperty)):
            out[c.name] = dump_tree(c, depth + 1, max_depth)
        else:
            out[c.name] = value_of(c)
    return out


def int_of(v):
    """Leading integer of a WZ stat string ('14' / '500+7*x' / 14) or None."""
    if isinstance(v, bool):
        return None
    if isinstance(v, int):
        return v
    if isinstance(v, float):
        return int(v)
    if isinstance(v, str):
        s = v.strip()
        buf = ''
        for ch in s:
            if ch.isdigit():
                buf += ch
            elif buf:
                break
            elif ch in '+-':
                continue
            else:
                break
        return int(buf) if buf else None
    return None


def is_skill_id_str(s):
    return isinstance(s, str) and 7 <= len(s) <= 9 and s.isdigit()


# ── pack index / resolver ─────────────────────────────────────────────
class PackIndex:
    """id -> file index over every ``Skill_*.img`` in the unpack dir."""

    def __init__(self, packs):
        self.packs = packs
        self.index = {}
        self.by_file = {}
        self._imgs = {}
        self.files = sorted(f for f in os.listdir(packs) if f.endswith('.img'))
        for fn in self.files:
            try:
                img = self._img(fn)
            except Exception as exc:  # noqa: BLE001
                print('skip %s (%s)' % (fn, exc), file=sys.stderr)
                continue
            sk = img.root.get('skill')
            if sk is None:
                continue
            ids = [c.name for c in sk.children() if isinstance(c, WzSubProperty)]
            self.by_file[fn] = ids
            for sid in ids:
                self.index[sid] = fn

    def _img(self, fn):
        if fn not in self._imgs:
            self._imgs[fn] = load_img(os.path.join(self.packs, fn))
        return self._imgs[fn]

    def get(self, sid):
        fn = self.index.get(str(sid))
        if fn is None:
            return None, None
        return self._img(fn).root.get('skill').get(str(sid)), fn


# ── core layout ───────────────────────────────────────────────────────
def load_cores(path, skill_node, job_node):
    img = load_img(path)
    holder = img.root.get(skill_node)
    core = holder.get('coreData') or holder.get('CoreData')
    out = {}
    for c in core.children():
        rec = {'coreId': c.name}
        nm = c.get('name')
        if nm is not None:
            rec['name'] = str(nm.value)
        ds = c.get('desc')
        if ds is not None:
            rec['desc'] = str(ds.value)
        for f in ('type', 'maxLevel'):
            v = c.get(f)
            if v is not None:
                rec[f] = value_of(v)
        jb = c.get('job')
        if jb is not None:
            rec['job'] = ([value_of(x) for x in jb.children()]
                          if isinstance(jb, WzSubProperty) else value_of(jb))
        for f in ('connectSkill', 'subSkill', 'connectSkillForDesc'):
            v = c.get(f)
            if v is not None:
                rec[f] = [str(value_of(x)) for x in v.children()]
        out[c.name] = rec
    layout = {}
    jn = holder.get(job_node)
    if jn is not None:
        for job in jn.children():
            layout[job.name] = {g.name: [str(value_of(x)) for x in g.children()]
                                for g in job.children() if isinstance(g, WzSubProperty)}
    return out, layout


def seeds_for(job, cores, layout, source):
    """Core ids listed for one job -> seed records (connectSkill + subSkill)."""
    groups = ('skill', 'enforce', 'common') if source == 'VCore' else \
             ('mastery', 'enforce', 'skill', 'common')
    out, desc_only = [], []
    for grp in groups:
        for cid in layout.get(job, {}).get(grp, []):
            core = cores.get(cid)
            if core is None:
                out.append({'id': None, 'coreId': cid, 'group': grp,
                            'source': source, 'missingCore': True})
                continue
            for field in ('connectSkill', 'subSkill'):
                for sid in core.get(field, []):
                    out.append({'id': sid, 'coreId': cid, 'coreName': core.get('name'),
                                'group': grp, 'source': source, 'field': field})
            for sid in core.get('connectSkillForDesc', []):
                desc_only.append({'id': sid, 'coreId': cid, 'field': 'connectSkillForDesc'})
    return out, desc_only


# ── reference walk ────────────────────────────────────────────────────
class RefWalker:
    """Collects skill-id references (with the exact node path) from one skill."""

    def __init__(self, pack):
        self.pack = pack

    @staticmethod
    def classify(path, node_name):
        """True when ``path``/``node_name`` is a *source-backed* skill reference.

        Only three shapes qualify, so no id is ever inferred from raw numbers:

        1. the root container is a known skill-id list / link node (REF_ROOTS);
        2. the leaf name contains ``skill`` (skill, skillId, attackSkill,
           MainSkillID_WZ2, CooltimeCheckSkillID, ...);
        3. an ancestor segment contains ``skill`` and the leaf is not a
           geometry/timing field.
        """
        parts = path.split('/')
        if parts[0] in REF_ROOTS:
            return True
        if 'skill' in node_name.lower():
            return True
        if node_name in NON_REF_FIELDS:
            return False
        return any('skill' in seg.lower() for seg in parts[:-1])

    def walk(self, skill_id):
        node, _ = self.pack.get(skill_id)
        refs, missing, art = [], [], {}
        if node is None:
            return refs, missing, art, {'ignoredIdLike': 0}
        stats = {'ignoredIdLike': 0}
        self._walk(node, '', refs, missing, stats)
        self._outlinks(node, '', art, skill_id)
        return refs, missing, art, stats

    def _walk(self, node, path, refs, missing, stats):
        for c in node.children():
            name = c.name
            p = path + '/' + name if path else name
            if isinstance(c, WzCanvasProperty):
                continue                       # 1x1 placeholder, only _outlink
            if isinstance(c, WzSubProperty):
                if path == '' and name in STAT_SUBTREES:
                    continue
                # psdSkill / finalAttack style: the id lives in the node name
                if is_skill_id_str(name):
                    if self.classify(p, name) and name in self.pack.index:
                        refs.append({'path': p, 'value': name, 'shape': 'node-name',
                                     'declared': True})
                    elif self.classify(p, name):
                        missing.append({'value': name, 'path': p, 'shape': 'node-name'})
                self._walk(c, p, refs, missing, stats)
                continue
            v = c.value
            cand = None
            if isinstance(v, int) and not isinstance(v, bool):
                if 7 <= len(str(abs(v))) <= 9 and v > 0:
                    cand = str(v)
            elif isinstance(v, str) and is_skill_id_str(v.strip()):
                cand = v.strip()
            if cand is None:
                continue
            declared = self.classify(p, name)
            if cand in self.pack.index:
                if declared:
                    refs.append({'path': p, 'value': cand, 'shape': 'value',
                                 'declared': True})
                else:
                    stats['ignoredIdLike'] += 1
            elif declared:
                missing.append({'value': cand, 'path': p, 'shape': 'value'})

    def _outlinks(self, node, path, art, owner):
        """Collect ``_outlink`` targets -> aggregated per target skill.

        Every effect canvas in the packs is a 1x1 placeholder whose pixels live
        in ``Skill/_Canvas``.  Targets frequently belong to *another* skill
        (sprite reuse), so this is an art dependency, kept separate from the
        gameplay ``references``.
        """
        for c in node.children():
            p = path + '/' + c.name if path else c.name
            if isinstance(c, WzCanvasProperty):
                link = c.get('_outlink')
                if link is None:
                    continue
                target = str(link.value)
                sid = None
                if '/skill/' in target:
                    seg = target.split('/skill/', 1)[1].split('/')[0]
                    if seg.isdigit():
                        sid = seg
                key = sid or target
                rec = art.setdefault(key, {'frames': 0, 'targetSkill': sid,
                                           'samplePath': p, 'sampleOutlink': target})
                rec['frames'] += 1
            elif isinstance(c, WzSubProperty):
                self._outlinks(c, p, art, owner)


# ── layer / canvas statistics ─────────────────────────────────────────
def canvas_stats(node, layer_name):
    """Frame / outlink statistics for one graphic layer."""
    frames = 0
    outlinks = 0
    placeholders = 0
    real_pixels = 0
    with_meta = 0
    stub_only = 0
    meta = []
    targets = Counter()
    sub_nodes = []

    def walk(n, path, depth):
        nonlocal frames, outlinks, placeholders, real_pixels, with_meta, stub_only
        for c in n.children():
            if isinstance(c, WzCanvasProperty):
                frames += 1
                link = c.get('_outlink')
                has_meta = any(c.get(k) is not None for k in ('origin', 'delay', 'z'))
                is_ph = (c.width <= 1 and c.height <= 1)
                if is_ph:
                    placeholders += 1
                else:
                    real_pixels += 1
                if link is not None:
                    outlinks += 1
                    t = str(link.value)
                    if '/skill/' in t:
                        seg = t.split('/skill/', 1)[1].split('/')[0]
                        if seg.isdigit():
                            targets[seg] += 1
                if has_meta:
                    with_meta += 1
                else:
                    stub_only += 1
                if len(meta) < MAX_FRAME_META:
                    f = {'i': c.name, 'canvas': [c.width, c.height], 'pngBytes': c._png_length}
                    for k in ('origin', 'z', 'delay', 'a0', 'a1', 'flip', 'lt', 'rb'):
                        sub = c.get(k)
                        if sub is not None:
                            f[k] = value_of(sub)
                    if link is not None:
                        f['outlink'] = str(link.value)
                    meta.append(f)
            elif isinstance(c, WzSubProperty):
                if depth == 0:
                    sub_nodes.append(c.name)
                walk(c, path + '/' + c.name, depth + 1)

    walk(node, layer_name, 0)
    return {
        'frames': frames,
        'outlinkFrames': outlinks,
        'placeholderFrames': placeholders,
        'realPixelFrames': real_pixels,
        'framesWithMeta': with_meta,
        'framesWithoutMeta': stub_only,
        # every frame is a 1x1 placeholder pointing at Skill/_Canvas
        'stubOnly': frames > 0 and outlinks == frames and real_pixels == 0,
        'noFrameMeta': frames > 0 and with_meta == 0,
        'subNodes': sub_nodes,
        'outlinkSkillTargets': dict(sorted(targets.items())),
        'frameMeta': meta,
        'frameMetaTruncated': frames > len(meta),
    }


# ── per-skill record ──────────────────────────────────────────────────
def has_canvas(node, depth=4):
    for c in node.children():
        if isinstance(c, WzCanvasProperty):
            return True
        if isinstance(c, WzSubProperty) and depth > 0 and has_canvas(c, depth - 1):
            return True
    return False


def skill_record(pack, sid):
    node, fn = pack.get(sid)
    if node is None:
        return None
    rec = OrderedDict()
    rec['id'] = sid
    rec['definition'] = {'file': fn, 'path': 'root/skill/' + sid}
    names = getattr(pack, '_names', None)
    if names is not None and sid in names:
        rec['name'] = names[sid].get('name')
        rec['desc'] = names[sid].get('desc')
    rec['topKeys'] = [c.name for c in node.children()]

    for k in ('common', 'info', 'info2', 'req'):
        sub = node.get(k)
        if sub is not None:
            rec[k] = scalar_dict(sub, 2)

    act = node.get('action')
    if act is not None:
        rec['action'] = [str(value_of(x)) for x in act.children()]

    for k in ('type4UI', 'weapon', 'processtype', 'vSkill'):
        sub = node.get(k)
        if sub is not None:
            rec[k] = value_of(sub)

    mai = node.get('multiAttackInfo')
    if mai is not None:
        rec['multiAttackInfo'] = dump_tree(mai)
        rec['multiAttackInfoFrameCount'] = len([c for c in mai.children()
                                                if isinstance(c, WzSubProperty)])

    layers = OrderedDict()
    for c in node.children():
        if not isinstance(c, WzSubProperty) or c.name in STAT_SUBTREES:
            continue
        is_layer = has_canvas(c) if isinstance(c, WzSubProperty) else False
        if not is_layer and c.name not in EFFECT_LAYERS:
            continue
        layers[c.name] = canvas_stats(c, c.name)
    if layers:
        rec['layers'] = layers

    walker = getattr(pack, '_walker', None)
    if walker is not None:
        refs, missing, art, stats = walker.walk(sid)
        rec['references'] = refs
        if missing:
            rec['unresolvedReferences'] = missing
        if art:
            total = sum(v['frames'] for v in art.values())
            cross = sum(v['frames'] for v in art.values()
                        if v['targetSkill'] and v['targetSkill'] != sid)
            rec['artOutlinks'] = OrderedDict(
                sorted(art.items(), key=lambda kv: (-kv[1]['frames'], kv[0])))
            rec['artOutlinkSummary'] = {
                'total': total, 'crossSkill': cross,
                'distinctTargets': len(art),
                'crossSkillTargets': sorted(
                    {v['targetSkill'] for v in art.values()
                     if v['targetSkill'] and v['targetSkill'] != sid}, key=int),
            }
        if stats['ignoredIdLike']:
            rec['ignoredIdLikeValues'] = stats['ignoredIdLike']
    return rec


# ── multi-attack derivation ───────────────────────────────────────────
def multi_attack_summary(rec):
    common = rec.get('common') or {}
    attack = int_of(common.get('attackCount'))
    bullet = int_of(common.get('bulletCount'))
    mob = int_of(common.get('mobCount'))
    mai = rec.get('multiAttackInfoFrameCount')
    segments = max([x for x in (attack, bullet) if x is not None], default=None)
    times = []
    if rec.get('multiAttackInfo'):
        for k, v in rec['multiAttackInfo'].items():
            if isinstance(v, dict):
                t = v.get('attackTime')
                if isinstance(t, int):
                    times.append(t)
    return {
        'skill': rec['id'],
        'attackCount': attack,
        'bulletCount': bullet,
        'mobCount': mob,
        'hasMultiAttackInfo': 'multiAttackInfo' in rec,
        'multiAttackFrames': mai,
        'attackTimeSum': sum(times) if times else None,
        'derivedSegments': segments,
        'segmentsSource': ('max(bulletCount,attackCount)'
                           if segments is not None else None),
        'multiAttackFramesVsSegments': ({'frames': mai, 'segments': segments}
                                        if mai is not None and segments is not None else None),
    }


def load_skill_names():
    img = load_img(os.path.join(MS_DATA, 'String/Skill.img'))
    out = {}
    for c in img.root.children():
        n = c.get('name')
        if n is not None:
            out[c.name] = {'name': str(n.value),
                           'desc': str(getattr(c.get('desc'), 'value', ''))}
    return out


# ── main ──────────────────────────────────────────────────────────────
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--packs', default='/tmp/packs')
    ap.add_argument('--out', default='docs/tms282-skill/data/skill-closure.json')
    ap.add_argument('--jobs', default='', help='comma separated subset of the 17 job ids')
    ap.add_argument('--sample', type=int, default=3, help='random skill ids to print verbatim')
    ap.add_argument('--seed', type=int, default=282, help='rng seed for --sample')
    ap.add_argument('--indent', type=int, default=1)
    ap.add_argument('--legacy', default='',
                    help='legacy skills-5th6th.json for diffing (auto-detected by default)')
    args = ap.parse_args()

    jobs = [j.strip() for j in args.jobs.split(',') if j.strip()] or list(JOBS)

    print('indexing %s ...' % args.packs, file=sys.stderr)
    pack = PackIndex(args.packs)
    print('skill ids in packs: %d across %d files'
          % (len(pack.index), len(pack.by_file)), file=sys.stderr)

    pack._names = load_skill_names()
    pack._walker = RefWalker(pack)

    vcore, vlayout = load_cores(os.path.join(MS_DATA, 'Etc', 'VCore.img'),
                                'vSkill', 'coreGroup')
    hcore, hlayout = load_cores(os.path.join(MS_DATA, 'Etc', 'HexaCore.img'),
                                'hexaSkill', 'jobCore')

    jobs_out = OrderedDict()
    missing_by_id = {}     # id -> [ {skill, path} ]
    skills_out = OrderedDict()
    record_cache = {}
    reference_index = {}   # id -> [ {skill, path} ]  (resolved edges, reverse)
    per_job_missing = {}

    for job in jobs:
        vseeds, vdesc = seeds_for(job, vcore, vlayout, 'VCore')
        hseeds, hdesc = seeds_for(job, hcore, hlayout, 'HexaCore')
        seeds = vseeds + hseeds
        seed_ids = [s['id'] for s in seeds if s.get('id')]

        closure = set(seed_ids)
        depth = {sid: 0 for sid in seed_ids}
        queue = deque(seed_ids)
        missing_for_job = set()
        while queue:
            sid = queue.popleft()
            rec = record_cache.get(sid)
            if rec is None:
                rec = skill_record(pack, sid)
                if rec is None:
                    continue
                record_cache[sid] = rec
            for ref in rec.get('references', []):
                reference_index.setdefault(ref['value'], []).append(
                    {'skill': sid, 'path': ref['path']})
                if ref['value'] not in depth:
                    depth[ref['value']] = depth[sid] + 1
                    closure.add(ref['value'])
                    queue.append(ref['value'])
            for miss in rec.get('unresolvedReferences', []):
                missing_for_job.add(miss['value'])
                missing_by_id.setdefault(miss['value'], []).append(
                    {'skill': sid, 'path': miss['path']})

        for sid in seed_ids:
            if sid not in record_cache:
                rec = skill_record(pack, sid)
                if rec is not None:
                    record_cache[sid] = rec

        per_job_missing[job] = missing_for_job
        depth_hist = Counter(depth.values())
        def uniq(rows):
            seen, out = set(), []
            for r in rows:
                k = tuple(sorted(r.items()))
                if k not in seen:
                    seen.add(k)
                    out.append(r)
            return out
        jobs_out[job] = OrderedDict([
            ('vCoreIds', sorted({s['coreId'] for s in vseeds if s.get('coreId')}, key=int)),
            ('hexaCoreIds', sorted({s['coreId'] for s in hseeds if s.get('coreId')}, key=int)),
            ('missingCoreIds', sorted({s['coreId'] for s in vseeds + hseeds
                                       if s.get('missingCore')}, key=int)),
            ('vSeeds', uniq([s for s in vseeds if s.get('id')])),
            ('hexaSeeds', uniq([s for s in hseeds if s.get('id')])),
            ('vSeedIds', sorted(set(s['id'] for s in vseeds if s.get('id')), key=int)),
            ('hexaSeedIds', sorted(set(s['id'] for s in hseeds if s.get('id')), key=int)),
            ('descOnlyRefs', vdesc + hdesc),
            ('closure', sorted(closure, key=int)),
            ('closureSize', len(closure)),
            ('closureByDepth', {str(k): depth_hist[k] for k in sorted(depth_hist)}),
            ('closureDepth', OrderedDict((sid, depth[sid])
                                         for sid in sorted(closure, key=int))),
            ('missing', sorted(missing_for_job, key=int)),
            ('multiAttackSkills', sorted(
                {sid for sid in closure if 'multiAttackInfo' in (record_cache.get(sid) or {})},
                key=int)),
        ])

    skills_out = OrderedDict((sid, record_cache[sid])
                             for sid in sorted(record_cache, key=int))

    # per-route exclusivity + shared hubs (a few 5th-job common skills such as
    # 400001042 pull a 4th-job buff list into every route, which is real but
    # makes all closures look alike -- keep the distinction visible)
    all_jobs_closure = [set(jobs_out[j]['closure']) for j in jobs_out]
    common_to_all = set.intersection(*all_jobs_closure) if all_jobs_closure else set()
    hub_count = Counter()
    for cl in all_jobs_closure:
        hub_count.update(cl)
    for job, rec in jobs_out.items():
        own = set(rec['closure'])
        union_others = set().union(*[c for c in all_jobs_closure if c is not own]) \
            if len(all_jobs_closure) > 1 else set()
        rec['directClosureSize'] = rec['closureByDepth'].get('0', 0) + \
            rec['closureByDepth'].get('1', 0)
        rec['exclusiveSkills'] = sorted(own - union_others, key=int)
        rec['sharedWithAllRoutes'] = len(own & common_to_all)

    # ── aggregate summaries ───────────────────────────────────────────
    missing_out = OrderedDict()
    for mid in sorted(missing_by_id, key=int):
        missing_out[mid] = OrderedDict([
            ('id', mid),
            ('referencedBy', missing_by_id[mid]),
            ('referenceCount', len(missing_by_id[mid])),
        ])

    ma_summary = OrderedDict()
    for sid in sorted(skills_out, key=int):
        rec = skills_out[sid]
        if 'common' not in rec and 'multiAttackInfo' not in rec:
            continue
        ma_summary[sid] = multi_attack_summary(rec)

    key_counter = Counter()
    for sid, rec in skills_out.items():
        for k in rec.get('topKeys', []):
            key_counter[k] += 1
    node_cov = OrderedDict()
    for k, n in key_counter.most_common():
        node_cov[k] = {'skills': n, 'of': len(skills_out),
                       'coveragePct': round(100.0 * n / max(1, len(skills_out)), 2)}

    # stub / pixel audit
    stub_skills, no_layer_skills, real_pixel_skills = [], [], []
    for sid, rec in skills_out.items():
        layers = rec.get('layers') or {}
        if not layers:
            no_layer_skills.append(sid)
            continue
        frames = sum(l['frames'] for l in layers.values())
        meta = sum(l['framesWithMeta'] for l in layers.values())
        real = sum(l['realPixelFrames'] for l in layers.values())
        if real:
            real_pixel_skills.append(sid)
        if frames and meta == 0:
            stub_skills.append(sid)

    # ── legacy diff (skills-5th6th.json only kept V connectSkill) ──────
    legacy_path = args.legacy
    if not legacy_path:
        root = os.path.dirname(os.path.dirname(os.path.dirname(
            os.path.dirname(os.path.abspath(__file__)))))
        legacy_path = os.path.join(root, 'docs/tms282-skill/data/skills-5th6th.json')
    legacy_diff = OrderedDict([('legacyFile', None), ('note', 'legacy file not found')])
    if os.path.exists(legacy_path):
        try:
            legacy = json.load(open(legacy_path, encoding='utf-8'))
            legacy_ids = set((legacy.get('skills') or {}).keys())
            new_ids = set(skills_out)
            missing_in_legacy = sorted(new_ids - legacy_ids, key=int)
            only_in_legacy = sorted(legacy_ids - new_ids, key=int)
            mai_missing = [i for i in missing_in_legacy
                           if 'multiAttackInfo' in (skills_out.get(i) or {})]
            legacy_diff = OrderedDict([
                ('legacyFile', legacy_path),
                ('legacySkillCount', len(legacy_ids)),
                ('newSkillCount', len(new_ids)),
                ('missingInLegacy', missing_in_legacy),
                ('missingInLegacyCount', len(missing_in_legacy)),
                ('missingInLegacyWithMultiAttackInfo', mai_missing),
                ('onlyInLegacy', only_in_legacy),
                ('onlyInLegacyCount', len(only_in_legacy)),
            ])
        except Exception as exc:  # noqa: BLE001
            legacy_diff = OrderedDict([('legacyFile', legacy_path),
                                       ('error', str(exc))])

    out = OrderedDict()
    out['meta'] = OrderedDict([
        ('tool', 'MapleDash/tools/ms282/export_skill_closure.py'),
        ('client', 'TMS 282'),
        ('packs', os.path.abspath(args.packs)),
        ('msData', MS_DATA),
        ('routes', jobs),
        ('note', 'Reference walk follows only 7..9 digit ids that resolve to a '
                 'Skill_*.img definition, except under skill-named fields where '
                 'unresolved ids are reported as missingDefinitions.'),
        ('layersNote', 'Every effect canvas in the packs is a 1x1/10-byte placeholder '
                       'with _outlink into Skill/_Canvas; artOutlinks are art deps only.'),
    ])
    out['jobs'] = jobs_out
    out['skills'] = OrderedDict((sid, skills_out[sid]) for sid in sorted(skills_out, key=int))
    out['referenceIndex'] = OrderedDict(
        (sid, reference_index[sid]) for sid in sorted(reference_index, key=int))
    out['missingDefinitions'] = missing_out
    out['multiAttackSummary'] = ma_summary
    out['nodeCoverage'] = node_cov
    ref_roots = Counter()
    ignored_total = 0
    for sid, rec in skills_out.items():
        for ref in rec.get('references', []):
            ref_roots[ref['path'].split('/')[0]] += 1
        ignored_total += rec.get('ignoredIdLikeValues', 0)
    out['referenceStats'] = OrderedDict([
        ('edges', sum(ref_roots.values())),
        ('byRoot', dict(ref_roots.most_common())),
        ('ignoredIdLikeValues',
         'numbers of 7..9 digits outside skill-named fields (offsets/timers) '
         'that happen to equal a skill id and were deliberately not followed'),
        ('ignoredIdLikeTotal', ignored_total),
    ])
    out['sharedHubs'] = OrderedDict(
        (sid, {'routes': hub_count[sid],
               'name': (skills_out.get(sid) or {}).get('name')})
        for sid in sorted(hub_count, key=int) if hub_count[sid] == len(jobs_out))
    out['legacyDiff'] = legacy_diff
    out['frameAudit'] = OrderedDict([
        ('noLayers', sorted(no_layer_skills, key=int)),
        ('noFrameMeta', sorted(stub_skills, key=int)),
        ('withRealPixels', sorted(real_pixel_skills, key=int)),
    ])

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=args.indent)
    size = os.path.getsize(args.out)

    # ── console summary ───────────────────────────────────────────────
    print('\n== per route ==')
    print('%-6s %6s %6s %7s %8s %8s %8s %6s' %
          ('job', 'Vseed', 'Hseed', 'direct', 'closure', 'missing', 'multiAtk', 'stub#'))
    for job, rec in jobs_out.items():
        stub_n = sum(1 for sid in rec['closure'] if sid in set(stub_skills))
        print('%-6s %6d %6d %7d %8d %8d %8d %6d' % (
            job, len(rec['vSeedIds']), len(rec['hexaSeedIds']),
            rec['directClosureSize'], rec['closureSize'],
            len(rec['missing']), len(rec['multiAttackSkills']), stub_n))

    print('\ntotal skills recorded: %d' % len(skills_out))
    print('shared by all %d routes (hub-driven): %d' % (len(jobs_out), len(out['sharedHubs'])))
    art_total = sum((r.get('artOutlinkSummary') or {}).get('total', 0)
                    for r in skills_out.values())
    art_cross = sum((r.get('artOutlinkSummary') or {}).get('crossSkill', 0)
                    for r in skills_out.values())
    print('art _outlink frames: %d (cross-skill %d, %.1f%%)'
          % (art_total, art_cross, 100.0 * art_cross / max(1, art_total)))
    print('missing definitions: %d unique ids' % len(missing_out))
    for mid, m in list(missing_out.items())[:40]:
        src = ', '.join('%s@%s' % (r['skill'], r['path']) for r in m['referencedBy'][:3])
        print('  %s <- %d refs: %s' % (mid, m['referenceCount'], src))

    print('\nmulti-attack skills (has multiAttackInfo): %d'
          % sum(1 for v in ma_summary.values() if v['hasMultiAttackInfo']))
    print('skills whose frames carry no origin/delay: %d' % len(stub_skills))
    print('skills with no graphic layers: %d' % len(no_layer_skills))
    print('skills with real pixels in packs: %d' % len(real_pixel_skills))

    if 'missingInLegacyCount' in legacy_diff:
        print('\nlegacy skills-5th6th.json: %d skills; new %d'
              % (legacy_diff['legacySkillCount'], legacy_diff['newSkillCount']))
        print('  missing from legacy: %d (of which %d have multiAttackInfo)'
              % (legacy_diff['missingInLegacyCount'],
                 len(legacy_diff['missingInLegacyWithMultiAttackInfo'])))
        print('  legacy-only ids: %d' % legacy_diff['onlyInLegacyCount'])
        print('  first 30 missing: %s' % ', '.join(legacy_diff['missingInLegacy'][:30]))

    print('\nnode coverage (top-level keys over %d skills):' % len(skills_out))
    for k, v in list(node_cov.items())[:20]:
        print('  %-24s %5d  %6.1f%%' % (k, v['skills'], v['coveragePct']))

    # ── random proof samples ──────────────────────────────────────────
    rng = random.Random(args.seed)
    ids = sorted(skills_out, key=int)
    picks = rng.sample(ids, min(args.sample, len(ids)))
    print('\n== random proof samples (%d) ==' % len(picks))
    for sid in picks:
        rec = skills_out[sid]
        print('-- %s  %s  (%s)' % (sid, rec.get('name'), rec['definition']['file']))
        print('   topKeys: %s' % ', '.join(rec['topKeys']))
        c = rec.get('common') or {}
        print('   common.attackCount=%r bulletCount=%r mobCount=%r damage=%r'
              % (c.get('attackCount'), c.get('bulletCount'), c.get('mobCount'),
                 c.get('damage')))
        if rec.get('multiAttackInfo'):
            print('   multiAttackInfo frames=%d first=%s'
                  % (rec.get('multiAttackInfoFrameCount'),
                     json.dumps(list(rec['multiAttackInfo'].items())[:2], ensure_ascii=False)))
        if rec.get('layers'):
            lay = list(rec['layers'].items())[0]
            print('   layer %s frames=%d outlink=%d meta=%d stubOnly=%s'
                  % (lay[0], lay[1]['frames'], lay[1]['outlinkFrames'],
                     lay[1]['framesWithMeta'], lay[1]['stubOnly']))

    print('\nwrote %s (%.2f MiB)' % (args.out, size / 1048576.0))


if __name__ == '__main__':
    main()
