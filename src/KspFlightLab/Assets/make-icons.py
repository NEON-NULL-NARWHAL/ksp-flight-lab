# KSP Flight Lab · src/KspFlightLab/Assets/make-icons.py
# Makes icon.png (256 px, the window's icon) and icon.ico (16-256 px, the exe's and the taskbar's) from icon.svg and
# icon-small.svg (32 px and below). Renders with ffmpeg's librsvg, packs the .ico itself (PNG entries, no libraries).
# Run: python make-icons.py <ffmpeg.exe>
import os, struct, subprocess, sys, tempfile

here = os.path.dirname(os.path.abspath(__file__))
ffmpeg = sys.argv[1]
sizes = [16, 24, 32, 48, 64, 128, 256]

def render(size):
    source = open(os.path.join(here, 'icon-small.svg' if size <= 32 else 'icon.svg'), encoding='utf-8').read()
    # The svg's own width and height set librsvg's output size.
    source = source.replace('width="256" height="256"', f'width="{size}" height="{size}"', 1)
    source = source.replace('width="32" height="32"', f'width="{size}" height="{size}"', 1)
    folder = tempfile.mkdtemp()
    svg, png = os.path.join(folder, 'in.svg'), os.path.join(folder, 'out.png')
    open(svg, 'w', encoding='utf-8').write(source)
    subprocess.run([ffmpeg, '-loglevel', 'error', '-y', '-i', svg, '-frames:v', '1', '-pix_fmt', 'rgba', png], check=True)
    return open(png, 'rb').read()

pngs = {size: render(size) for size in sizes}
open(os.path.join(here, 'icon.png'), 'wb').write(pngs[256])

# ICO: a header, one 16-byte entry per size, then each PNG (Windows Vista+ reads PNG entries at every size).
header = struct.pack('<HHH', 0, 1, len(sizes))
entries, data = b'', b''
offset = 6 + 16 * len(sizes)
for size in sizes:
    png = pngs[size]
    entries += struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(png), offset + len(data))
    data += png
open(os.path.join(here, 'icon.ico'), 'wb').write(header + entries + data)
print('icon.png and icon.ico:', ', '.join(f'{s}px {len(p)} B' for s, p in pngs.items()))
