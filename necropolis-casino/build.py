"""Bundles a game page into one self-contained HTML file.

- Card pictures from img/ are embedded as data URIs (the build:card-art block),
  so the page shows them even when opened on its own, without the img/ folder.
- Tailwind styles are precompiled into the page (the build:tailwind block), so
  there is no Tailwind CDN script. This step needs Node.js (it runs npx).

Run it after changing the card art or the Tailwind classes in a page:
    python3 build.py                          # index.html: pictures and styles
    python3 build.py multiplayer/client.html  # the two-player page
    python3 build.py --art-only               # pictures only, no Node.js needed
"""
import base64
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).parent
TAILWIND = 'tailwindcss@3.4.17'


def replace_block(html, name, body):
    """Swaps the content between <!-- build:name ... --> and <!-- /build:name -->."""
    pattern = re.compile(rf'(<!-- build:{name}\b[^>]*-->\n)(.*?)(\n[ \t]*<!-- /build:{name} -->)', re.S)
    html, count = pattern.subn(lambda m: m.group(1) + body + m.group(3), html)
    if count != 1:
        raise SystemExit(f'build:{name} block not found in the page')
    return html


def card_art_block():
    entries = []
    for png in sorted((HERE / 'img').glob('*.png')):
        data = base64.b64encode(png.read_bytes()).decode('ascii')
        entries.append(f'        {json.dumps(png.stem)}: "data:image/png;base64,{data}"')
    return '    <script id="card-art">\n    const CARD_ART = {\n' + ',\n'.join(entries) + '\n    };\n    </script>'


def tailwind_block(html):
    npx = shutil.which('npx')
    if not npx:
        raise SystemExit('Node.js (npx) is needed to rebuild the styles; use --art-only to skip them')
    # Scan only the real markup and scripts, not the embedded pictures or the old styles
    scan = replace_block(replace_block(html, 'card-art', ''), 'tailwind', '')
    with tempfile.TemporaryDirectory() as tmp:
        tmp = Path(tmp)
        (tmp / 'page.html').write_text(scan, encoding='utf-8')
        (tmp / 'in.css').write_text('@tailwind base;\n@tailwind components;\n@tailwind utilities;\n')
        subprocess.run([npx, '--yes', TAILWIND, '-i', str(tmp / 'in.css'), '-o', str(tmp / 'out.css'),
                        '--content', str(tmp / 'page.html'), '--minify'], check=True)
        css = (tmp / 'out.css').read_text(encoding='utf-8').strip()
    return f'    <style id="tailwind">{css}</style>'


def main():
    pages = [arg for arg in sys.argv[1:] if not arg.startswith('--')]
    page = Path(pages[0]) if pages else HERE / 'index.html'
    html = page.read_text(encoding='utf-8')
    html = replace_block(html, 'card-art', card_art_block())
    if '--art-only' not in sys.argv:
        html = replace_block(html, 'tailwind', tailwind_block(html))
    page.write_text(html, encoding='utf-8')
    print(f'built {page} ({page.stat().st_size // 1024} KB)')


if __name__ == '__main__':
    main()
