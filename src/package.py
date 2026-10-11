"""Make runtime-only GIN LifeHacks ZIPs with stable installed file names."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
source = Path(__file__).resolve().parent
parts = [('Timeline', 'GIN Timeline life hack.dll', 'GIN_Timeline_LifeHack'),
         ('Advanced', 'GIN Advance mode life hack.dll', 'GIN_Advance_Mode_LifeHack')]
files = []
for folder, filename, archive_name in parts:
    entry = source / folder / 'bin' / filename
    if not entry.is_file():
        raise SystemExit(f'Build first: {entry}')
    plugin = (source / folder / 'Plugin.cs').read_text(encoding='utf-8-sig')
    match = re.search(r'\[BepInPlugin\(Guid,\s*"[^"]+",\s*"([0-9.]+)"\)\]', plugin)
    if not match:
        raise SystemExit(f'Plugin version not found: {folder}')
    files.append((entry, f'BepInEx/plugins/{filename}', match.group(1), archive_name))
args.output.mkdir(parents=True, exist_ok=True)
archives = []

def pack(name, members):
    path = args.output / name
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for entry, relative, _, _ in members:
            archive.write(entry, relative)
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None
        assert archive.namelist() == [member[1] for member in members]
        for entry, relative, _, _ in members:
            assert archive.read(relative) == entry.read_bytes()
    archives.append({'file': path.name, 'bytes': path.stat().st_size,
                     'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})

for item in files:
    pack(f'{item[3]}_v{item[2]}.zip', [item])
pack(f'GIN_LifeHacks_v{files[0][2]}.zip', files)
print(json.dumps(archives, indent=2))
