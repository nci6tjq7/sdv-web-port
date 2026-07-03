"""Generate a 256x256 test sprite PNG for the KNI rendering PoC.

The sprite has visible features (diagonal stripes in 4 colors) so we can
visually confirm rendering is working if needed.
"""
from PIL import Image, ImageDraw
import os

OUTPUT_PATH = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    '..', 'src', 'SdvWebPort.PoC.Render', 'Content', 'test_sprite.png'
)
OUTPUT_PATH = os.path.abspath(OUTPUT_PATH)

os.makedirs(os.path.dirname(OUTPUT_PATH), exist_ok=True)

img = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)

# Diagonal stripes in 4 distinct colors for visual verification
colors = [
    (255, 100, 100, 255),  # red
    (100, 255, 100, 255),  # green
    (100, 100, 255, 255),  # blue
    (255, 255, 100, 255),  # yellow
]

for i, color in enumerate(colors):
    offset = i * 16
    for x in range(-256, 512, 64):
        draw.line([(x + offset, 0), (x + offset - 256, 256)], fill=color, width=8)

# Add a solid border for clarity
draw.rectangle([(0, 0), (255, 255)], outline=(255, 255, 255, 255), width=2)

img.save(OUTPUT_PATH)
print(f"[+] Test sprite created: {OUTPUT_PATH}")
print(f"    Size: {os.path.getsize(OUTPUT_PATH)} bytes")
