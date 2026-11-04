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


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=512)
    ap.add_argument("--preview")
    a = ap.parse_args()
    for f in (concrete, plaster, metal, wood, dirt):
        f(a.size, a.preview)
