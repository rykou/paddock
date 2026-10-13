"""Render index.html frame by frame. Usage: render.py stills | render.py full"""
import os
import sys
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
FPS, DUR = 30, 20.0
mode = sys.argv[1] if len(sys.argv) > 1 else "stills"

with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page(viewport={"width": 1920, "height": 1080})
    page.goto("file://" + os.path.join(HERE, "index.html"))
    page.evaluate("window.ready")
    if mode == "stills":
        out = os.path.join(HERE, "stills")
        os.makedirs(out, exist_ok=True)
        times = [float(x) for x in sys.argv[2:]] or [1.5, 3.7, 4.3, 4.7, 5.5, 6.3, 8.0, 9.5, 11.4, 12.2, 13.3, 14.6, 15.4, 16.7, 17.4, 19.0]
        for t in times:
            page.evaluate("t => window.render(t)", t)
            page.screenshot(path=os.path.join(out, f"t{t:05.2f}.jpg"), type="jpeg", quality=80)
    else:
        out = os.path.join(HERE, "frames")
        os.makedirs(out, exist_ok=True)
        n = int(FPS * DUR)
        for i in range(n):
            page.evaluate("t => window.render(t)", i / FPS)
            page.screenshot(path=os.path.join(out, f"f{i:04d}.jpg"), type="jpeg", quality=96)
    browser.close()
