"""Draws paddock.ico: a rounded panel with a title strip and a 2x2 grid of tiles."""
import sys
from PIL import Image, ImageDraw

S = 256
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((16, 28, 240, 228), radius=28, fill=(32, 42, 58, 255))
d.rounded_rectangle((16, 28, 240, 84), radius=28, fill=(74, 96, 130, 255))
d.rectangle((16, 60, 240, 84), fill=(74, 96, 130, 255))
for i, colour in enumerate([(255, 196, 77), (96, 200, 160), (240, 110, 110), (120, 170, 255)]):
    x, y = 48 + (i % 2) * 88, 104 + (i // 2) * 60
    d.rounded_rectangle((x, y, x + 72, y + 44), radius=10, fill=colour + (255,))

img.save(sys.argv[1], sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)], bitmap_format="bmp")
