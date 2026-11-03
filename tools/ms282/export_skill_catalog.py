#!/usr/bin/env python3
"""Export the TMS 282 5th/6th job skill catalogue from the client's .ms packs.

282 splits skill data in two:

* ``Data/Packs/Skill_*.ms``      -> ``Skill/<job>.img``  (metadata: common/info/req,
                                    effect frame stubs carrying origin/z/delay + ``_outlink``)
* ``Data/Skill/_Canvas/<job>.img`` (legacy WZ)             -> pixels (canvas 292x146 ...)

The pack image is the authoritative one: it holds ``common`` (numeric stats) and the
per-frame ``origin``/``delay``/``z`` that the legacy WZ tree no longer stores.

Usage (packs must already be unpacked with tools/ms282/MsUnpack):

  python3 tools/ms282/export_skill_catalog.py --packs /tmp/packs \
      --out docs/tms282-skill/data/skills.json
"""
import argparse
import json
import os
import sys

WZPY = '/Users/lizixian/Documents/mxd/Beidou-Server/tool/wz-python'
sys.path.insert(0, WZPY)

from wzpy import WzImage, WzKey  # noqa: E402
from wzpy.properties import WzSubProperty, WzCanvasProperty  # noqa: E402

MS_DATA = '/Users/lizixian/Documents/mxd/MapleStory/Data'

# effect layers the client draws, in source-tree order (not draw order)
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
)
SCALAR_NODES = ('common', 'info', 'info2', 'req', 'action', 'summon',
                'type4UI', 'weapon', 'skillList', 'psdSkill', 'reqLev')


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
    return str(getattr(node, 'value', None))


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


def frames_of(node):
    """effect layer -> [{i, origin, z, delay, outlink, size}]"""
    out = []
    for c in node.children():
        if not isinstance(c, WzCanvasProperty):
            continue
        f = {'i': c.name}
        for k in ('origin', 'z', 'delay', 'a0', 'a1', 'flip', 'lt', 'rb'):
            sub = c.get(k)
            if sub is not None:
                f[k] = value_of(sub)
        link = c.get('_outlink')
        if link is not None:
            f['outlink'] = str(link.value)
        out.append(f)
    return out


def load_img(path):
    img = WzImage.from_bytes(open(path, 'rb').read(), key=WzKey.for_region('BMS'),
                             name=os.path.basename(path))
    img.parse()
    if img.truncated:
        raise RuntimeError('truncated ' + path)
    return img


def load_names():
    img = load_img(os.path.join(MS_DATA, 'String/Skill.img'))
    names, books = {}, {}
    for c in img.root.children():
        n = c.get('name')
        if n is not None:
            names[c.name] = {'name': str(n.value),
                             'desc': str(getattr(c.get('desc'), 'value', '')),
                             'h': str(getattr(c.get('h'), 'value', ''))}
        b = c.get('bookName')
        if b is not None:
            books[c.name] = str(b.value)
    return names, books


def load_cores(fname, skill_node, job_node):
    img = load_img(os.path.join(MS_DATA, 'Etc', fname))
    root = img.root
    holder = root.get(skill_node)
    # VCore spells it ``CoreData``, HexaCore ``coreData``
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
            rec['job'] = [value_of(x) for x in jb.children()] if isinstance(jb, WzSubProperty) else value_of(jb)
        for f in ('connectSkill', 'subSkill'):
            v = c.get(f)
            if v is not None:
                rec[f] = [value_of(x) for x in v.children()]
        out[c.name] = rec
    layout = {}
    jn = root.get(skill_node).get(job_node)
    if jn is not None:
        for job in jn.children():
            layout[job.name] = {g.name: [value_of(x) for x in g.children()]
                                for g in job.children() if isinstance(g, WzSubProperty)}
    return out, layout


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--packs', default='/tmp/packs')
    ap.add_argument('--out', default='docs/tms282-skill/data/skills.json')
    ap.add_argument('--only', default='', help='comma separated job files, e.g. 114,1114')
    ap.add_argument('--compact', default='',
                    help='also write a 5th/6th-only catalogue (reachable from V/HEXA cores)')
    args = ap.parse_args()

    names, books = load_names()
    vcore, vlayout = load_cores('VCore.img', 'vSkill', 'coreGroup')
    hcore, hlayout = load_cores('HexaCore.img', 'hexaSkill', 'jobCore')

    only = [x.strip() for x in args.only.split(',') if x.strip()]
    files = sorted(f for f in os.listdir(args.packs) if f.endswith('.img'))
    jobs = []
    for fn in files:
        key = fn[:-4]
        if key.startswith('Skill_'):
            key = key[len('Skill_'):]
        if not key.isdigit():
            continue
        if only and key not in only:
            continue
        try:
            img = load_img(os.path.join(args.packs, fn))
        except Exception as exc:  # noqa: BLE001
            print('skip', fn, exc, file=sys.stderr)
            continue
        sk = img.root.get('skill')
        if sk is None:
            continue
        rec = {'file': key, 'book': books.get(key), 'skills': []}
        for s in sk.children():
            if not isinstance(s, WzSubProperty):
                continue
            item = {'id': s.name, 'name': names.get(s.name, {}).get('name'),
                    'keys': [c.name for c in s.children()]}
            common = s.get('common')
            if common is not None:
                item['common'] = scalar_dict(common)
            for k in ('info', 'info2', 'req'):
                node = s.get(k)
                if node is not None:
                    item[k] = scalar_dict(node, 1)
            act = s.get('action')
            if act is not None:
                item['action'] = [str(x.value) for x in act.children()]
            for k in ('type4UI', 'weapon'):
                node = s.get(k)
                if node is not None:
                    item[k] = value_of(node)
            sl = s.get('skillList')
            if sl is not None:
                item['skillList'] = [value_of(x) for x in sl.children()]
            layers = {}
            for lay in EFFECT_LAYERS:
                node = s.get(lay)
                if node is None:
                    continue
                fr = frames_of(node)
                if fr:
                    layers[lay] = fr
                else:
                    extra = {c.name: value_of(c) for c in node.children()
                             if not isinstance(c, WzSubProperty)}
                    sub = {c.name: [value_of(x) for x in c.children()]
                           for c in node.children() if isinstance(c, WzSubProperty)}
                    layers[lay] = {'scalars': extra, 'subs': sub}
            if layers:
                item['layers'] = layers
            rec['skills'].append(item)
        jobs.append(rec)

    out = {'jobs': jobs, 'vCore': vcore, 'vCoreLayout': vlayout,
           'hexaCore': hcore, 'hexaLayout': hlayout}
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    with open(args.out, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=1, sort_keys=False)
    n_skill = sum(len(j['skills']) for j in jobs)
    print('jobs=%d skills=%d vcore=%d hexa=%d -> %s' % (
        len(jobs), n_skill, len(vcore), len(hcore), args.out))

    if args.compact:
        index = {s['id']: s for j in jobs for s in j['skills']}
        keep = {}
        for job, lay in vlayout.items():
            for grp, ids in lay.items():
                for cid in ids:
                    for sid in vcore.get(str(cid), {}).get('connectSkill', []):
                        keep[str(sid)] = index.get(str(sid))
        for job, lay in hlayout.items():
            for grp, ids in lay.items():
                for cid in ids:
                    core = hcore.get(str(cid), {})
                    for sid in core.get('connectSkill', []) + core.get('subSkill', []):
                        keep[str(sid)] = index.get(str(sid))
        missing = [k for k, v in keep.items() if v is None]
        for k in missing:
            keep.pop(k)
        with open(args.compact, 'w', encoding='utf-8') as f:
            json.dump({'skills': keep, 'vCore': vcore, 'vCoreLayout': vlayout,
                       'hexaCore': hcore, 'hexaLayout': hlayout},
                      f, ensure_ascii=False, separators=(',', ':'))
        print('compact skills=%d missing=%d -> %s' % (len(keep), len(missing), args.compact))


if __name__ == '__main__':
    main()
