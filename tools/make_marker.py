#!/usr/bin/env python3
"""Generate a printable, feature-dense, asymmetric scale/alignment marker.

Two consumers, one sheet:
  - ARCore / AR Foundation tracked image  -> wants dense non-repetitive detail
  - COLMAP marker detection for unitScale -> wants a crisp measurable outer edge

Nominal sizes are printed ON the sheet, but the number that goes into
docs/FRAMES.md is the one you measure with a ruler after printing.
Deterministic: same seed -> byte-identical output, so a reprint is the same marker.
"""
import random

MM = 72.0 / 25.4  # PDF points per millimetre


def build(rng, page_w, page_h, target_mm, cells, label):
    """Return (pdf_content_stream_ops, svg_body) for one sheet."""
    ops, svg = [], []

    def rect(x, y, w, h, col):
        ops.append(f"{col} rg {x*MM:.3f} {y*MM:.3f} {w*MM:.3f} {h*MM:.3f} re f")
        # SVG y axis is flipped relative to PDF
        r, g, b = col.split()
        c = "#%02x%02x%02x" % (int(float(r)*255), int(float(g)*255), int(float(b)*255))
        svg.append(f'<rect x="{x:.3f}" y="{page_h-y-h:.3f}" width="{w:.3f}" '
                   f'height="{h:.3f}" fill="{c}"/>')

    def tri(pts, col):
        p = " ".join(f"{x*MM:.3f} {y*MM:.3f}" for x, y in pts)
        parts = p.split()
        ops.append(f"{col} rg {parts[0]} {parts[1]} m {parts[2]} {parts[3]} l "
                   f"{parts[4]} {parts[5]} l h f")
        r, g, b = col.split()
        c = "#%02x%02x%02x" % (int(float(r)*255), int(float(g)*255), int(float(b)*255))
        pl = " ".join(f"{x:.3f},{page_h-y:.3f}" for x, y in pts)
        svg.append(f'<polygon points="{pl}" fill="{c}"/>')

    def text(x, y, size, s):
        esc = s.replace("\\", r"\\").replace("(", r"\(").replace(")", r"\)")
        ops.append(f"0 0 0 rg BT /F1 {size:.1f} Tf {x*MM:.3f} {y*MM:.3f} Td ({esc}) Tj ET")
        svg.append(f'<text x="{x:.3f}" y="{page_h-y:.3f}" font-family="Helvetica" '
                   f'font-size="{size*25.4/72:.3f}" fill="#000">'
                   f'{s.replace("&","&amp;").replace("<","&lt;")}</text>')

    BLACK, WHITE, GREY = "0 0 0", "1 1 1", "0.28 0.28 0.28"

    # --- target: solid black ring, white quiet zone, dense interior ---------
    border = target_mm * 0.045          # ring thickness
    quiet  = target_mm * 0.022          # white gap inside the ring
    tx = (page_w - target_mm) / 2.0
    ty = page_h - 32.0 - target_mm      # 32mm of header space above

    rect(tx, ty, target_mm, target_mm, BLACK)                      # measure THIS edge
    inset = border
    rect(tx+inset, ty+inset, target_mm-2*inset, target_mm-2*inset, WHITE)

    pat = tx + border + quiet
    pat_s = target_mm - 2*(border + quiet)
    step = pat_s / cells
    pat_y = ty + border + quiet

    for row in range(cells):
        for col_i in range(cells):
            x, y = pat + col_i*step, pat_y + row*step
            # orientation glyphs: an L in the top-left, a lone block bottom-right.
            # These break rotational symmetry so a 90/180 deg error is visible.
            if row >= cells-3 and col_i < 3 and not (row == cells-3 and col_i > 0):
                rect(x, y, step, step, BLACK); continue
            if row < 2 and col_i >= cells-2:
                rect(x, y, step, step, BLACK); continue
            k = rng.random()
            if k < 0.30:
                continue                                            # white space
            shade = GREY if rng.random() < 0.18 else BLACK
            kind = rng.randrange(6)
            g = step * 0.10
            if kind == 0:
                rect(x+g, y+g, step-2*g, step-2*g, shade)
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
                if rng.random() < 0.5:
                    rect(x, y+step*0.32, step, step*0.36, shade)
                else:
                    rect(x+step*0.32, y, step*0.36, step, shade)
            else:
                h = step/2.0
                rect(x + (h if rng.random() < 0.5 else 0), y, h, step, shade)

    # --- header ------------------------------------------------------------
    text(tx, page_h-16, 13, "RECON  scale + AR alignment marker")
    text(tx, page_h-23, 9,
         f"Nominal outer edge {target_mm:.1f} x {target_mm:.1f} mm  ({label})."
         "  PRINT AT 100% / ACTUAL SIZE - disable Fit to Page.")

    # --- 100 mm print-scale check ruler ------------------------------------
    ry = ty - 18
    rect(tx, ry, 100.0, 0.35, BLACK)
    for i in range(11):
        maj = (i % 5 == 0)
        rect(tx + i*10.0, ry, 0.35, 6.0 if maj else 3.5, BLACK)
        if maj:
            text(tx + i*10.0 - 2.0, ry + 7.5, 7, str(i*10))
    text(tx + 104, ry + 1, 8, "mm - this line MUST measure 100.0 mm. If not, reprint at 100%.")

    # --- fields to fill in by hand -----------------------------------------
    by = ry - 12
    text(tx, by,      9, "MEASURED outer edge (steel ruler, mm, one decimal):")
    text(tx, by-7.5,  9, "   width  W = ______________ mm        height H = ______________ mm")
    text(tx, by-15,   9, "   printed by ____________   date ____________")
    text(tx, by-24,   8, "Record W and H in docs/FRAMES.md (Scale). Tape flat at scene origin, all four edges.")
    text(tx, by-31,   8, "Matte paper only - gloss highlights break COLMAP matching and ARCore tracking.")
    text(tx, by-38,   8, "W and H should agree; if they differ the printer scaled the axes unevenly - record both.")

    return ops, svg


def emit_pdf(path, page_w, page_h, ops):
    stream = "\n".join(ops).encode("latin-1")
    objs = [
        b"<</Type/Catalog/Pages 2 0 R>>",
        b"<</Type/Pages/Kids[3 0 R]/Count 1>>",
        ("<</Type/Page/Parent 2 0 R/MediaBox[0 0 %.3f %.3f]/Contents 4 0 R"
         "/Resources<</Font<</F1 5 0 R>>>>>>" % (page_w*MM, page_h*MM)).encode(),
        b"<</Length %d>>\nstream\n" % len(stream) + stream + b"\nendstream",
        b"<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
    ]
    out = bytearray(b"%PDF-1.4\n")
    offs = []
    for i, o in enumerate(objs, 1):
        offs.append(len(out))
        out += b"%d 0 obj\n" % i + o + b"\nendobj\n"
    xref = len(out)
    out += b"xref\n0 %d\n" % (len(objs)+1) + b"0000000000 65535 f \n"
    for o in offs:
        out += b"%010d 00000 n \n" % o
    out += (b"trailer\n<</Size %d/Root 1 0 R>>\nstartxref\n%d\n%%%%EOF\n"
            % (len(objs)+1, xref))
    open(path, "wb").write(out)
    return len(out)


def emit_svg(path, page_w, page_h, svg):
    body = "\n".join(svg)
    open(path, "w").write(
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{page_w}mm" '
        f'height="{page_h}mm" viewBox="0 0 {page_w} {page_h}">'
        f'<rect width="{page_w}" height="{page_h}" fill="#fff"/>\n{body}\n</svg>\n')


for label, pw, ph, tgt, n in (("A4", 210.0, 297.0, 170.0, 17),
                              ("A3", 297.0, 420.0, 250.0, 21)):
    rng = random.Random(20260822)   # fixed: a reprint is the identical marker
    ops, svg = build(rng, pw, ph, tgt, n, label)
    base = f"marker-{label.lower()}-{int(tgt)}mm"
    size = emit_pdf(base + ".pdf", pw, ph, ops)
    emit_svg(base + ".svg", pw, ph, svg)
    print(f"{label}: target {tgt}mm, {n}x{n} cells -> {base}.pdf ({size} bytes) + .svg")
