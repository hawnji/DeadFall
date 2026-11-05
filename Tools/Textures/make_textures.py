#!/usr/bin/env python3
"""
Generates the game's tileable surface textures (Resources/Textures): an albedo and a normal map per surface.
Albedos are near-white detail (the game multiplies them by each object's colour), normals use the OpenGL (+Y)
convention Unity expects. Everything is built from FFT-filtered noise, so every map tiles seamlessly.

    python3 Tools/Textures/make_textures.py [--size 512] [--preview out_dir]
"""
import argparse, os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "../../Assets/_Project/Resources/Textures"))


def rng(seed): return np.random.default_rng(seed)


def fbm(n, beta, seed, aniso=(1.0, 1.0)):
    """Tileable 1/f^beta noise in 0..1. aniso stretches frequencies (x, y): >1 means smoother along that axis."""
    w = rng(seed).standard_normal((n, n))
    fy = np.fft.fftfreq(n)[:, None] * aniso[1]
    fx = np.fft.fftfreq(n)[None, :] * aniso[0]
    f = np.sqrt(fx * fx + fy * fy)
    f[0, 0] = 1.0
    spec = np.fft.fft2(w) / (f ** (beta * 0.5 + 0.5))
    spec[0, 0] = 0
    out = np.real(np.fft.ifft2(spec))
    out -= out.min(); out /= max(out.max(), 1e-9)
    return out


def band(n, lo, hi, seed, aniso=(1.0, 1.0)):
    """Tileable band-limited noise (frequencies lo..hi cycles per tile), 0..1."""
    w = rng(seed).standard_normal((n, n))
    fy = np.fft.fftfreq(n, 1.0 / n)[:, None] * aniso[1]
    fx = np.fft.fftfreq(n, 1.0 / n)[None, :] * aniso[0]
    f = np.sqrt(fx * fx + fy * fy)
    spec = np.fft.fft2(w) * ((f >= lo) & (f <= hi))
    out = np.real(np.fft.ifft2(spec))
    out -= out.min(); out /= max(out.max(), 1e-9)
    return out


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5 * strength
    # Image rows go down; Unity's +Y (green) points up the texture, so flip dy.
    nx, ny, nz = -dx, dy, np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / l, ny / l, nz / l], axis=-1) * 0.5 + 0.5


def save(name, albedo, height, strength, preview):
    os.makedirs(OUT, exist_ok=True)
    a = np.clip(albedo, 0, 1)
    if a.ndim == 2: a = np.stack([a, a, a], axis=-1)
    Image.fromarray((a * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, f"{name}_albedo.png"), optimize=True)
    nrm = normal_from_height(height, strength)
    Image.fromarray((nrm * 255 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, f"{name}_normal.png"), optimize=True)
    if preview:
        os.makedirs(preview, exist_ok=True)
        tile = np.tile((a * 255).astype(np.uint8), (2, 2, 1))
        Image.fromarray(tile, "RGB").save(os.path.join(preview, f"{name}_albedo_2x2.png"))
    print(f"{name}: albedo mean {a.mean():.2f}")


def concrete(n, p):
    grain = band(n, n // 8, n // 2, 1)
    mid = fbm(n, 1.6, 2)
    stains = smoothstep(0.55, 0.85, fbm(n, 2.4, 3))
    pits = smoothstep(0.82, 0.95, band(n, n // 10, n // 4, 4))
    albedo = 0.86 + (mid - 0.5) * 0.16 + (grain - 0.5) * 0.10 - stains * 0.14 - pits * 0.22
    height = mid * 0.5 + grain * 0.35 - pits * 1.2
    save("concrete", albedo, height, 2.2, p)


def plaster(n, p):
    grain = band(n, n // 6, n // 2, 11)
    mid = fbm(n, 2.0, 12)
    streaks = fbm(n, 2.0, 13, aniso=(1.0, 8.0))  # long vertical streaks
    grime = smoothstep(0.6, 0.9, fbm(n, 2.6, 14))
    albedo = 0.9 + (mid - 0.5) * 0.08 + (grain - 0.5) * 0.05 - smoothstep(0.55, 0.9, streaks) * 0.12 - grime * 0.1
    height = grain * 0.4 + mid * 0.3
    save("plaster", albedo, height, 1.2, p)


def metal(n, p):
    brushed = band(n, 4, n // 2, 21, aniso=(12.0, 1.0))  # streaks along X
    mid = fbm(n, 2.0, 22)
    y, x = np.mgrid[0:n, 0:n] / n
    # Panel seams every half tile, rivets near the corners of each panel.
    sx, sy = (x * 2) % 1.0, (y * 2) % 1.0
    seam = np.maximum(smoothstep(0.012, 0.0, np.minimum(sx, 1 - sx)), smoothstep(0.012, 0.0, np.minimum(sy, 1 - sy)))
    rivet = np.zeros_like(x)
    for cx in (0.06, 0.94):
        for cy in (0.06, 0.94):
            d = np.sqrt((sx - cx) ** 2 + (sy - cy) ** 2)
            rivet = np.maximum(rivet, smoothstep(0.018, 0.008, d))
    rust = smoothstep(0.66, 0.86, fbm(n, 2.2, 23)) * (0.4 + 0.6 * band(n, n // 8, n // 3, 24))
    base = 0.84 + (brushed - 0.5) * 0.12 + (mid - 0.5) * 0.08 - seam * 0.35 + rivet * 0.08
    albedo = np.stack([base, base, base], axis=-1)
    rust_col = np.array([0.62, 0.36, 0.2])
    albedo = albedo * (1 - rust[..., None] * 0.75) + rust_col * rust[..., None] * 0.75
    height = brushed * 0.15 - seam * 1.0 + rivet * 0.8 + rust * 0.25
    save("metal", albedo, height, 3.0, p)


def wood(n, p):
    y, x = np.mgrid[0:n, 0:n] / n
    planks = 4
    idx = np.floor(y * planks).astype(int)
    py = (y * planks) % 1.0
    r = rng(31)
    offs = r.uniform(0, 1, planks); tone = r.uniform(-0.08, 0.08, planks)
    warp = fbm(n, 2.2, 32, aniso=(6.0, 1.0))
    grain = 0.5 + 0.5 * np.sin((py * 9 + warp * 6 + offs[idx] * 10) * np.pi * 2)
    fine = band(n, n // 6, n // 2, 33, aniso=(10.0, 1.0))
    gap = smoothstep(0.035, 0.0, np.minimum(py, 1 - py))
    # Butt joints: one end per plank at a random X.
    jx = (x - offs[idx]) % 1.0
    joint = smoothstep(0.006, 0.0, np.minimum(jx, 1 - jx))
    knots = smoothstep(0.9, 0.97, band(n, 6, 14, 34))
    albedo = 0.8 + tone[idx] + (grain - 0.5) * 0.14 + (fine - 0.5) * 0.06 - knots * 0.25 - np.maximum(gap, joint) * 0.5
    height = grain * 0.25 + fine * 0.2 - np.maximum(gap, joint) * 1.2 - knots * 0.3
    save("wood", albedo, height, 2.5, p)


def dirt(n, p):
    mid = fbm(n, 1.8, 41)
    grain = band(n, n // 6, n // 2, 42)
    pebbles = smoothstep(0.78, 0.9, band(n, n // 16, n // 6, 43))
    patches = smoothstep(0.4, 0.75, fbm(n, 2.6, 44))
    albedo = 0.82 + (mid - 0.5) * 0.22 + (grain - 0.5) * 0.12 + pebbles * 0.12 - patches * 0.12
    height = mid * 0.6 + grain * 0.4 + pebbles * 1.0
    save("dirt", albedo, height, 2.0, p)


# ------------------------------------------------------------------ map surfaces (structured, higher contrast)

def cells(n, count, seed):
    """Per-cell random values for a count x count grid laid over the tile: (index map, cell-local u, v)."""
    y, x = np.mgrid[0:n, 0:n] / n
    ix, iy = np.floor(x * count).astype(int), np.floor(y * count).astype(int)
    return ix, iy, (x * count) % 1.0, (y * count) % 1.0


def edge_dist(u, v):
    return np.minimum(np.minimum(u, 1 - u), np.minimum(v, 1 - v))


def floortile(n, p):
    """Worn 1 m industrial floor tiles, 4 x 4 per 3.92 m floor plate: grout, per-tile tone, scuffs, stains, cracks."""
    count = 4
    ix, iy, u, v = cells(n, count, 51)
    r = rng(52)
    tone = r.uniform(-0.07, 0.07, (count, count))
    grout = smoothstep(0.022, 0.008, edge_dist(u, v))
    bevel = smoothstep(0.05, 0.022, edge_dist(u, v)) * (1 - grout)
    mid = fbm(n, 1.8, 53)
    grain = band(n, n // 10, n // 2, 54)
    scuffs = smoothstep(0.62, 0.9, band(n, 3, 40, 55, aniso=(5.0, 1.0)))   # long scrapes
    stains = smoothstep(0.6, 0.92, fbm(n, 2.6, 56)) * 0.6
    dirt_edges = smoothstep(0.12, 0.0, edge_dist(u, v)) * (0.5 + 0.5 * fbm(n, 2.0, 57))
    crack_field = band(n, 6, 22, 58)
    crack = smoothstep(0.005, 0.0, np.abs(crack_field - 0.5)) * smoothstep(0.72, 0.9, fbm(n, 2.4, 59))
    albedo = (0.8 + tone[iy, ix] + (mid - 0.5) * 0.14 + (grain - 0.5) * 0.08
              - scuffs * 0.12 - stains * 0.2 - dirt_edges * 0.12 - crack * 0.35 - grout * 0.5 - bevel * 0.06)
    warm = np.stack([albedo * 1.0, albedo * 0.97, albedo * 0.92], axis=-1)
    stain_col = np.array([0.55, 0.45, 0.32])
    warm = warm * (1 - stains[..., None] * 0.35) + stain_col * albedo[..., None] * stains[..., None] * 0.35
    height = grain * 0.2 + mid * 0.2 - grout * 1.2 - bevel * 0.4 - crack * 0.8 + scuffs * 0.05
    save("floortile", warm, height, 3.0, p)


def wallpanel(n, p):
    """Painted steel wall panels, 1 m wide x 2 m tall (2 per 2 m tile): seams, bolts, drips and scuffs."""
    y, x = np.mgrid[0:n, 0:n] / n
    u = (x * 2) % 1.0
    vv = y % 1.0
    ix = np.floor(x * 2).astype(int)
    r = rng(61)
    tone = r.uniform(-0.05, 0.05, 2)
    seam = np.maximum(smoothstep(0.012, 0.004, np.minimum(u, 1 - u)), smoothstep(0.008, 0.002, np.minimum(vv, 1 - vv)))
    lip = smoothstep(0.03, 0.012, np.minimum(u, 1 - u)) * (1 - seam)
    bolts = np.zeros_like(x)
    for bx in (0.05, 0.95):
        for by in np.linspace(0.06, 0.94, 6):
            d = np.sqrt((u - bx) ** 2 + ((vv - by) * 2) ** 2)  # u spans 1 m, vv spans 2 m
            bolts = np.maximum(bolts, smoothstep(0.014, 0.007, d))
    mid = fbm(n, 2.0, 62)
    grain = band(n, n // 8, n // 2, 63)
    drips = smoothstep(0.55, 0.9, fbm(n, 2.0, 64, aniso=(1.0, 14.0))) * (0.4 + 0.6 * fbm(n, 2.0, 65))
    chips = smoothstep(0.86, 0.95, band(n, n // 16, n // 5, 66))
    scuffs = smoothstep(0.7, 0.92, band(n, 4, 50, 67, aniso=(6.0, 1.0)))
    albedo = (0.84 + tone[ix] + (mid - 0.5) * 0.1 + (grain - 0.5) * 0.05
              - drips * 0.22 - scuffs * 0.1 - seam * 0.55 - lip * 0.08 + bolts * 0.1 - chips * 0.3)
    col = np.stack([albedo, albedo, albedo], axis=-1)
    rust = (drips * smoothstep(0.6, 0.85, fbm(n, 2.2, 68))) + chips * 0.8
    rust_col = np.array([0.5, 0.3, 0.18])
    col = col * (1 - rust[..., None] * 0.5) + rust_col * rust[..., None] * 0.5
    height = grain * 0.12 + mid * 0.1 - seam * 1.0 + lip * 0.3 + bolts * 0.9 - chips * 0.4
    save("wallpanel", col, height, 3.0, p)


def ceilingtile(n, p):
    """Drop ceiling: 6 x 6 acoustic tiles per 4 m with a metal T-bar grid, water stains and a few dark gaps."""
    count = 6
    ix, iy, u, v = cells(n, count, 71)
    r = rng(72)
    tone = r.uniform(-0.05, 0.05, (count, count))
    missing = r.uniform(0, 1, (count, count)) < 0.03
    stained = r.uniform(0, 1, (count, count)) < 0.25
    sag = r.uniform(0, 1, (count, count))
    bar = smoothstep(0.03, 0.018, edge_dist(u, v))
    speck = band(n, n // 4, n // 2, 73)
    pores = smoothstep(0.78, 0.9, band(n, n // 6, n // 2, 74))
    mid = fbm(n, 2.0, 75)
    # Water stains: ring-edged blotches, only on some tiles.
    blot = fbm(n, 2.4, 76)
    fade = smoothstep(0.0, 0.15, edge_dist(u, v))  # stains soak in from inside the tile
    stain = smoothstep(0.5, 0.8, blot) * stained[iy, ix] * fade
    ring = smoothstep(0.025, 0.0, np.abs(blot - 0.52)) * stained[iy, ix] * fade * 0.6
    tile = 0.9 + tone[iy, ix] + (speck - 0.5) * 0.08 - pores * 0.1 + (mid - 0.5) * 0.06 - stain * 0.18 - ring * 0.18
    tile = np.where(missing[iy, ix], 0.22 + mid * 0.06, tile)
    albedo = tile * (1 - bar) + 0.62 * bar
    col = np.stack([albedo, albedo, albedo], axis=-1)
    stain_col = np.array([0.62, 0.5, 0.32])
    s = (stain + ring)[..., None] * 0.6
    col = col * (1 - s) + stain_col * col * s
    height = speck * 0.15 - pores * 0.3 - bar * 0.0 + (1 - bar) * (0.4 + sag[iy, ix] * 0.1) - missing[iy, ix] * 1.0
    height = np.where(bar > 0.5, 0.6, height)
    save("ceilingtile", col, height, 2.5, p)


def brick(n, p):
    """Sooty running-bond brick for the service tunnels, 2 m tile (8 x 26 bricks)."""
    rows, per_row = 26, 8
    y, x = np.mgrid[0:n, 0:n] / n
    iy = np.floor(y * rows).astype(int)
    shift = (iy % 2) * 0.5 / per_row
    bx = ((x + shift) * per_row) % 1.0
    ix = np.floor(((x + shift) % 1.0) * per_row).astype(int)
    by = (y * rows) % 1.0
    r = rng(81)
    tone = r.uniform(-0.12, 0.12, (rows, per_row))
    mortar = np.maximum(smoothstep(0.03, 0.012, np.minimum(bx, 1 - bx)), smoothstep(0.1, 0.04, np.minimum(by, 1 - by)))
    grain = band(n, n // 8, n // 2, 82)
    mid = fbm(n, 2.0, 83)
    soot = smoothstep(0.45, 0.85, fbm(n, 2.2, 84, aniso=(1.0, 4.0)))
    chips = smoothstep(0.85, 0.95, band(n, n // 12, n // 4, 85))
    b = 0.78 + tone[iy, ix] + (grain - 0.5) * 0.12 + (mid - 0.5) * 0.1 - chips * 0.2
    m = 0.62 + (grain - 0.5) * 0.1
    albedo = b * (1 - mortar) + m * mortar
    albedo = albedo * (1 - soot * 0.35)
    col = np.stack([albedo, albedo, albedo], axis=-1)
    height = (1 - mortar) * (0.6 + grain * 0.2) - chips * 0.4 + mortar * grain * 0.1
    save("brick", col, height, 3.5, p)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=512)
    ap.add_argument("--preview")
    ap.add_argument("--only", help="comma-separated surface names")
    a = ap.parse_args()
    only = set(a.only.split(",")) if a.only else None
    for f in (concrete, plaster, metal, wood, dirt):
        if only is None or f.__name__ in only: f(a.size, a.preview)
    # Map surfaces cover large areas, so they get twice the resolution.
    for f in (floortile, wallpanel, ceilingtile, brick):
        if only is None or f.__name__ in only: f(a.size * 2, a.preview)
