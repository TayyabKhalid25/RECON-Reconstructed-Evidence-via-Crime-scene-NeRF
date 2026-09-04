#!/usr/bin/env python3
"""Generate a cropped version of the 170mm AR marker.

This script outputs an SVG containing ONLY the 170mm x 170mm marker, 
without the A4 margins, rulers, or text. Useful for AR Reference Image 
Libraries or calibration tasks that require just the tracking feature pattern.
"""
import random
import os

def build_cropped(rng, target_mm, cells):
    svg = []
    page_h = target_mm
    page_w = target_mm
    
    def rect(x, y, w, h, col):
        r, g, b = col.split()
        c = "#%02x%02x%02x" % (int(float(r)*255), int(float(g)*255), int(float(b)*255))
        svg.append(f'<rect x="{x:.3f}" y="{page_h-y-h:.3f}" width="{w:.3f}" height="{h:.3f}" fill="{c}"/>')

    def tri(pts, col):
        r, g, b = col.split()
        c = "#%02x%02x%02x" % (int(float(r)*255), int(float(g)*255), int(float(b)*255))
        pl = " ".join(f"{x:.3f},{page_h-y:.3f}" for x, y in pts)
        svg.append(f'<polygon points="{pl}" fill="{c}"/>')

    BLACK, WHITE, GREY = "0 0 0", "1 1 1", "0.28 0.28 0.28"
    border = target_mm * 0.045
    quiet  = target_mm * 0.022
    tx = 0.0
    ty = 0.0

    rect(tx, ty, target_mm, target_mm, BLACK)
    inset = border
    rect(tx+inset, ty+inset, target_mm-2*inset, target_mm-2*inset, WHITE)

    pat = tx + border + quiet
    pat_s = target_mm - 2*(border + quiet)
    step = pat_s / cells
    pat_y = ty + border + quiet

    for row in range(cells):
        for col_i in range(cells):
            x, y = pat + col_i*step, pat_y + row*step
            if row >= cells-3 and col_i < 3 and not (row == cells-3 and col_i > 0):
                rect(x, y, step, step, BLACK); continue
            if row < 2 and col_i >= cells-2:
                rect(x, y, step, step, BLACK); continue
            k = rng.random()
            if k < 0.30: continue
            shade = GREY if rng.random() < 0.18 else BLACK
            kind = rng.randrange(6)
            g = step * 0.10
            if kind == 0: rect(x+g, y+g, step-2*g, step-2*g, shade)
            elif kind == 1:
                o = rng.randrange(4)
                c = [(x, y), (x+step, y), (x+step, y+step), (x, y+step)]
                tri([c[o], c[(o+1) % 4], c[(o+2) % 4]], shade)
            elif kind == 2:
                h = step/2.0
                rect(x, y, h, h, shade); rect(x+h, y+h, h, h, shade)
            elif kind == 3:
                rect(x+g, y+g, step-2*g, step-2*g, shade)
                rect(x+2.4*g, y+2.4*g, step-4.8*g, step-4.8*g, WHITE)
            elif kind == 4:
                if rng.random() < 0.5: rect(x, y+step*0.32, step, step*0.36, shade)
                else: rect(x+step*0.32, y, step*0.36, step, shade)
            else:
                h = step/2.0
                rect(x + (h if rng.random() < 0.5 else 0), y, h, step, shade)

    return svg

def emit_svg(path, size_mm, svg):
    body = "\n".join(svg)
    with open(path, "w") as f:
        f.write(f'<svg xmlns="http://www.w3.org/2000/svg" width="{size_mm}mm" height="{size_mm}mm" viewBox="0 0 {size_mm} {size_mm}">')
        f.write(f'<rect width="{size_mm}" height="{size_mm}" fill="#fff"/>\n{body}\n</svg>\n')

if __name__ == "__main__":
    rng = random.Random(20260822) # Identical seed to original marker
    svg = build_cropped(rng, 170.0, 17)
    
    # Save to repo root
    output_path = os.path.join(os.path.dirname(__file__), "..", "marker-cropped-170mm.svg")
    emit_svg(output_path, 170.0, svg)
    print(f"Generated {os.path.abspath(output_path)}")
