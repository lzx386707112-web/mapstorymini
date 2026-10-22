#!/usr/bin/env python3
"""Read-only export of native UI/Basic.img/Cursor, retaining origin and delay."""
import json
from pathlib import Path
import wzmap

OUT = Path(__file__).resolve().parents[1] / 'Assets/Resources/Art/ui/cursor'

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    states = []
    for state in sorted(wzmap._wz('UI/Basic').get('Cursor').children(), key=lambda n: int(n.name)):
        frames = []
        for frame in sorted(state.children(), key=lambda n: int(n.name)):
            image = wzmap._canvas('UI/Basic', f'Cursor/{state.name}/{frame.name}')
            if image is None:
                continue
            file = f'{state.name}_{frame.name}.bytes'
            image.save(OUT / file, format='PNG')
            x, y = wzmap._vec(frame, 'origin')
            frames.append(dict(file=file, x=x, y=y, delay=wzmap._num(frame, 'delay', 100)))
        states.append(dict(id=int(state.name), frames=frames))
    (OUT / 'manifest.json').write_text(json.dumps(dict(states=states), indent=2) + '\n')

if __name__ == '__main__':
    main()
