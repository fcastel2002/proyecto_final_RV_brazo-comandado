# -*- coding: utf-8 -*-
"""
Genera de forma procedural las texturas de la caja de madera de transporte
(el cajon de pallet en el que vienen las celdas de bateria "Eternity" antes
de ser colocadas en el rack) para las cajas agarrables de la escena Planta.

Sigue el mismo patron que generar_textura_bateria.py: un atlas 2x2 con una
celda por cara relevante, mas su normal map derivado de un mapa de alturas.

Salidas (Assets/Props/CajaMadera/):
    - T_CajaMadera_Atlas.png       2048, sRGB
    - T_CajaMadera_AtlasNormal.png 2048, lineal

  Distribucion del atlas (coordenadas de imagen, origen arriba-izquierda):
    +------------------+------------------+
    | FRENTE (rotulo)  | LATERAL (tablas) |   -> UV v en [0.5, 1.0]
    +------------------+------------------+
    | TAPA (tablas)    | FONDO (patines)  |   -> UV v en [0.0, 0.5]
    +------------------+------------------+
       UV u en [0,0.5]    UV u en [0.5,1]

  El mapeo cara -> celda esta replicado en CajaMaderaMeshTool.cs; si movés
  una celda acá, movela alla tambien.

Uso:  python generar_textura_caja_madera.py
Requisitos: pillow, numpy
"""

import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

# --------------------------------------------------------------------------
# Configuracion
# --------------------------------------------------------------------------
TILE = 1024          # resolucion final de cada celda del atlas
SS = 4               # supersampling para bordes y texto suaves
T = TILE * SS        # resolucion de trabajo
ATLAS = TILE * 2

AQUI = os.path.dirname(os.path.abspath(__file__))
SALIDA = os.path.normpath(os.path.join(AQUI, "..", "Assets", "Props", "CajaMadera"))

FUENTES = "C:/Windows/Fonts"
F_BOLD = os.path.join(FUENTES, "arialbd.ttf")
F_NARROW_BOLD = os.path.join(FUENTES, "ARIALNB.TTF")

# Paleta madera de pino sin tratar, tipo pallet
MADERA_CLARA = (206, 172, 121)
MADERA_BASE = (188, 153, 103)
MADERA_OSCURA = (162, 129, 84)
VETA_OSCURA = (120, 92, 58)
JUNTA = (66, 48, 30)
METAL_FLEJE = (150, 148, 140)
NEGRO_ROTULO = (40, 36, 32)
KRAFT = (196, 168, 122)
ROJO_FRAGIL = (150, 40, 34)


def p(v):
    """Fraccion del lado -> pixeles de la resolucion de trabajo."""
    return int(T * v)


def fuente(ruta, px):
    return ImageFont.truetype(ruta, px)


def texto_centrado(d, cx, y, txt, font, fill, tracking=0):
    if tracking == 0:
        w = d.textlength(txt, font=font)
        d.text((cx - w / 2, y), txt, font=font, fill=fill)
        return w
    anchos = [d.textlength(c, font=font) for c in txt]
    total = sum(anchos) + tracking * (len(txt) - 1)
    x = cx - total / 2
    for c, a in zip(txt, anchos):
        d.text((x, y), c, font=font, fill=fill)
        x += a + tracking
    return total


def veta(img, semilla, fuerza=14):
    """Textura de veta de madera: rayas horizontales onduladas + ruido fino."""
    rng = np.random.default_rng(semilla)
    a = np.asarray(img, dtype=np.float32)

    y = np.linspace(0, 1, T, dtype=np.float32)[:, None]
    x = np.linspace(0, 1, T, dtype=np.float32)[None, :]
    ondas = np.zeros((T, T), dtype=np.float32)
    for k in range(5):
        freq = rng.uniform(18, 55)
        fase = rng.uniform(0, 6.28)
        amp = rng.uniform(0.4, 1.0) / (k + 1)
        ondas += amp * np.sin(y * freq + np.sin(x * 3.0 + fase) * 1.5 + fase)
    ondas = ondas / np.abs(ondas).max()
    a += (ondas[:, :, None] * fuerza)

    a += rng.normal(0.0, 5.0, size=(T, T, 1)).astype(np.float32)
    return Image.fromarray(a.clip(0, 255).astype(np.uint8))


def base_planchas(vertical, semilla):
    """Fondo de tablas de madera (juntas verticales u horizontales)."""
    img = Image.new("RGB", (T, T), MADERA_BASE)
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(semilla)

    n = 6
    paso = T / n
    for i in range(n + 1):
        pos = int(i * paso)
        tono = MADERA_CLARA if i % 2 == 0 else MADERA_OSCURA
        x0, x1 = (pos, min(T, pos + int(paso))) if vertical else (0, T)
        y0, y1 = (0, T) if vertical else (pos, min(T, pos + int(paso)))
        if i < n:
            box = [x0, y0, x1, y1] if vertical else [x0, y0, x1, y1]
            offset = int(rng.normal(0, 3))
            tono2 = tuple(np.clip(np.array(tono) + offset, 0, 255).astype(int))
            d.rectangle(box, fill=tono2)

    img = veta(img, semilla)
    d = ImageDraw.Draw(img)
    for i in range(1, n):
        pos = int(i * paso)
        if vertical:
            d.line([pos, 0, pos, T], fill=JUNTA, width=p(0.006))
            d.line([pos + p(0.006), 0, pos + p(0.006), T], fill=(0, 0, 0, 0))
        else:
            d.line([0, pos, T, pos], fill=JUNTA, width=p(0.006))

    return img


def nudos(img, cantidad, semilla):
    d = ImageDraw.Draw(img)
    rng = np.random.default_rng(semilla)
    for _ in range(cantidad):
        cx, cy = rng.uniform(0.08, 0.92) * T, rng.uniform(0.08, 0.92) * T
        r = rng.uniform(0.012, 0.022) * T
        for k, tono in ((1.0, VETA_OSCURA), (0.55, (94, 70, 42)), (0.22, (60, 44, 26))):
            d.ellipse([cx - r * k, cy - r * k * 0.7, cx + r * k, cy + r * k * 0.7],
                      outline=tono, width=max(1, int(r * 0.12)))
    return img


def fleje_metalico(d, y0, y1):
    """Banda metalica horizontal (embalaje reforzado)."""
    d.rectangle([0, y0, T, y1], fill=METAL_FLEJE)
    d.line([0, y0 + p(0.004), T, y0 + p(0.004)], fill=(196, 194, 186), width=p(0.003))
    d.line([0, y1 - p(0.004), T, y1 - p(0.004)], fill=(104, 102, 96), width=p(0.003))


def cruz_refuerzo(d, x0, y0, x1, y1, grosor):
    """Refuerzo diagonal en cruz, tipico de cajones de pallet."""
    for (ax0, ay0, ax1, ay1) in [(x0, y0, x1, y1), (x0, y1, x1, y0)]:
        largo = np.hypot(ax1 - ax0, ay1 - ay0)
        ang = np.arctan2(ay1 - ay0, ax1 - ax0)
        nx, ny = -np.sin(ang) * grosor / 2, np.cos(ang) * grosor / 2
        d.polygon([
            (ax0 + nx, ay0 + ny), (ax1 + nx, ay1 + ny),
            (ax1 - nx, ay1 - ny), (ax0 - nx, ay0 - ny),
        ], fill=MADERA_OSCURA, outline=JUNTA)


# --------------------------------------------------------------------------
# FRENTE: tablas + refuerzo en cruz + rotulo de embalaje
# --------------------------------------------------------------------------
def tile_frente():
    img = base_planchas(vertical=True, semilla=101)
    img = nudos(img, 3, 202)
    d = ImageDraw.Draw(img)

    m = p(0.03)
    cruz_refuerzo(d, m, m, T - m, T - m, p(0.052))
    fleje_metalico(d, p(0.10), p(0.145))
    fleje_metalico(d, p(0.855), p(0.90))

    # Rotulo de carton kraft
    rx0, ry0, rx1, ry1 = p(0.30), p(0.36), p(0.70), p(0.60)
    d.rectangle([rx0 + p(0.006), ry0 + p(0.006), rx1 + p(0.006), ry1 + p(0.006)],
                fill=(40, 30, 20))
    d.rectangle([rx0, ry0, rx1, ry1], fill=KRAFT, outline=(120, 96, 62), width=p(0.004))

    cx = T // 2
    texto_centrado(d, cx, ry0 + p(0.025), "ETERNITY", fuente(F_BOLD, p(0.05)), NEGRO_ROTULO)
    texto_centrado(d, cx, ry0 + p(0.10), "TECHNOLOGIES", fuente(F_NARROW_BOLD, p(0.022)),
                   (80, 60, 40), tracking=p(0.008))

    # Icono fragil (triangulo + copa) y texto
    tri_cy = ry0 + p(0.165)
    d.polygon([(cx - p(0.20), tri_cy + p(0.028)), (cx - p(0.155), tri_cy - p(0.006)),
               (cx - p(0.11), tri_cy + p(0.028))], outline=NEGRO_ROTULO, width=p(0.0035))
    texto_centrado(d, cx + p(0.02), tri_cy - p(0.012), "FRAGIL", fuente(F_NARROW_BOLD, p(0.026)),
                   ROJO_FRAGIL, tracking=p(0.004))

    return img


# --------------------------------------------------------------------------
# LATERAL: tablas lisas con un fleje metalico
# --------------------------------------------------------------------------
def tile_lateral():
    img = base_planchas(vertical=True, semilla=303)
    img = nudos(img, 2, 404)
    d = ImageDraw.Draw(img)
    fleje_metalico(d, p(0.10), p(0.145))
    fleje_metalico(d, p(0.855), p(0.90))
    return img


# --------------------------------------------------------------------------
# TAPA: tablas horizontales lisas (vista desde arriba)
# --------------------------------------------------------------------------
def tile_tapa():
    img = base_planchas(vertical=False, semilla=505)
    img = nudos(img, 3, 606)
    return img


# --------------------------------------------------------------------------
# FONDO: patines de apoyo (skids) del pallet
# --------------------------------------------------------------------------
def tile_fondo():
    img = base_planchas(vertical=False, semilla=707)
    d = ImageDraw.Draw(img)
    for i in range(3):
        y = p(0.145) + i * p(0.36)
        d.rectangle([p(0.04), y, p(0.96), y + p(0.11)], fill=MADERA_OSCURA,
                    outline=JUNTA, width=p(0.005))
    return img


# --------------------------------------------------------------------------
# Mapas de altura -> normal map
# --------------------------------------------------------------------------
def altura_planchas(vertical):
    h = Image.new("L", (T, T), 128)
    d = ImageDraw.Draw(h)
    n = 6
    paso = T / n
    for i in range(1, n):
        pos = int(i * paso)
        if vertical:
            d.line([pos, 0, pos, T], fill=90, width=p(0.006))
        else:
            d.line([0, pos, T, pos], fill=90, width=p(0.006))
    return h


def altura_frente():
    h = altura_planchas(True)
    d = ImageDraw.Draw(h)
    m = p(0.03)
    grosor = p(0.052)
    for (ax0, ay0, ax1, ay1) in [(m, m, T - m, T - m), (m, T - m, T - m, m)]:
        ang = np.arctan2(ay1 - ay0, ax1 - ax0)
        nx, ny = -np.sin(ang) * grosor / 2, np.cos(ang) * grosor / 2
        d.polygon([(ax0 + nx, ay0 + ny), (ax1 + nx, ay1 + ny),
                   (ax1 - nx, ay1 - ny), (ax0 - nx, ay0 - ny)], fill=150)
    for y0 in (p(0.10), p(0.855)):
        d.rectangle([0, y0, T, y0 + p(0.045)], fill=176)
    d.rectangle([p(0.30), p(0.36), p(0.70), p(0.60)], fill=140)
    return h


def altura_lateral():
    h = altura_planchas(True)
    d = ImageDraw.Draw(h)
    for y0 in (p(0.10), p(0.855)):
        d.rectangle([0, y0, T, y0 + p(0.045)], fill=176)
    return h


def altura_fondo():
    h = altura_planchas(False)
    d = ImageDraw.Draw(h)
    for i in range(3):
        y = p(0.145) + i * p(0.36)
        d.rectangle([p(0.04), y, p(0.96), y + p(0.11)], fill=168)
    return h


def normal_desde_altura(h, semilla, fuerza=5.0):
    h = h.resize((TILE, TILE), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1.0))
    a = np.asarray(h, dtype=np.float32) / 255.0
    rng = np.random.default_rng(semilla)
    a += rng.normal(0.0, 0.006, size=(TILE, TILE)).astype(np.float32)

    dx = np.gradient(a, axis=1) * fuerza
    dy = np.gradient(a, axis=0) * fuerza
    nz = np.ones_like(a)
    largo = np.sqrt(dx ** 2 + dy ** 2 + nz ** 2)
    n = np.stack([-dx / largo * 0.5 + 0.5,
                  dy / largo * 0.5 + 0.5,
                  nz / largo * 0.5 + 0.5], axis=-1)
    return Image.fromarray((n * 255).clip(0, 255).astype(np.uint8))


def reducir(img):
    return img.resize((TILE, TILE), Image.LANCZOS)


# --------------------------------------------------------------------------
def main():
    os.makedirs(SALIDA, exist_ok=True)

    print("Dibujando celdas...")
    frente = reducir(tile_frente())
    lateral = reducir(tile_lateral())
    tapa = reducir(tile_tapa())
    fondo = reducir(tile_fondo())

    n_frente = normal_desde_altura(altura_frente(), 4342)
    n_lateral = normal_desde_altura(altura_lateral(), 4343)
    n_tapa = normal_desde_altura(altura_planchas(False), 4344)
    n_fondo = normal_desde_altura(altura_fondo(), 4345)

    for nombre, celdas in [
        ("T_CajaMadera_Atlas.png", (frente, lateral, tapa, fondo)),
        ("T_CajaMadera_AtlasNormal.png", (n_frente, n_lateral, n_tapa, n_fondo)),
    ]:
        atlas = Image.new("RGB", (ATLAS, ATLAS))
        atlas.paste(celdas[0], (0, 0))
        atlas.paste(celdas[1], (TILE, 0))
        atlas.paste(celdas[2], (0, TILE))
        atlas.paste(celdas[3], (TILE, TILE))
        atlas.save(os.path.join(SALIDA, nombre), optimize=True)
        print("OK ->", nombre)


if __name__ == "__main__":
    main()
