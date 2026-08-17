# -*- coding: utf-8 -*-
"""
Genera de forma procedural las texturas del rack metalico negro "Eternity"
que se monta en el autoelevador y aloja las celdas de bateria. A diferencia
de la caja de baterias, el rack se modela vacio (sin vasos): es una bandeja
con hueco interior para que el brazo robotico pueda colocar las celdas.

Sigue el mismo patron que generar_textura_bateria.py y
generar_textura_caja_madera.py: un atlas 2x2 con una celda por cara
relevante, mas su normal map derivado de un mapa de alturas. La celda TAPA
se usa tanto para el borde superior de la bandeja como para el piso interior
(marcas guia del lugar de cada celda), ya que ambas superficies pertenecen a
la misma vista cenital de chapa negra.

Salidas (Assets/Props/RackEternity/):
    - T_RackEternity_Atlas.png       2048, sRGB
    - T_RackEternity_AtlasNormal.png 2048, lineal

  Distribucion del atlas (coordenadas de imagen, origen arriba-izquierda):
    +------------------+------------------+
    | FRENTE (logo)    | LATERAL (rejilla)|   -> UV v en [0.5, 1.0]
    +------------------+------------------+
    | TAPA (borde+piso)| FONDO (patines)  |   -> UV v en [0.0, 0.5]
    +------------------+------------------+
       UV u en [0,0.5]    UV u en [0.5,1]

  El mapeo cara -> celda esta replicado en RackEternityMeshTool.cs; si movés
  una celda acá, movela alla tambien.

Uso:  python generar_textura_rack.py
Requisitos: pillow, numpy
"""

import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

# --------------------------------------------------------------------------
# Configuracion
# --------------------------------------------------------------------------
TILE = 1024
SS = 4
T = TILE * SS
ATLAS = TILE * 2

AQUI = os.path.dirname(os.path.abspath(__file__))
SALIDA = os.path.normpath(os.path.join(AQUI, "..", "Assets", "Props", "RackEternity"))

FUENTES = "C:/Windows/Fonts"
F_BOLD = os.path.join(FUENTES, "arialbd.ttf")
F_REG = os.path.join(FUENTES, "arial.ttf")
F_NARROW_BOLD = os.path.join(FUENTES, "ARIALNB.TTF")

# Paleta chapa negra
NEGRO_CLARO = (52, 54, 57)
NEGRO_BASE = (36, 37, 40)
NEGRO_OSCURO = (22, 23, 25)
NEGRO_SOMBRA = (12, 13, 14)
GRIS_BORDE = (78, 80, 84)
GRIS_REMACHE = (98, 100, 104)
VERDE = (58, 170, 71)
VERDE_OSC = (38, 132, 52)
TINTA_CLARA = (206, 208, 210)
TINTA_SUAVE = (150, 152, 155)
CORREA = (46, 138, 66)
CORREA_OSC = (30, 100, 46)
HEBILLA = (150, 152, 156)
GRIS_BANDEJA = (44, 46, 49)
GRIS_GUIA = (90, 92, 96)
AMARILLO = (210, 180, 40)


def p(v):
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


def lienzo(arriba, abajo):
    px = np.zeros((T, T, 3), dtype=np.float32)
    t = np.linspace(0.0, 1.0, T, dtype=np.float32)[:, None]
    for c in range(3):
        px[:, :, c] = arriba[c] * (1.0 - t) + abajo[c] * t
    return Image.fromarray(px.astype(np.uint8))


def cepillado(img, semilla, fuerza=6.0):
    """Rayado horizontal fino tipico de chapa cepillada + grano."""
    rng = np.random.default_rng(semilla)
    a = np.asarray(img, dtype=np.float32)
    lineas = np.repeat(rng.normal(0.0, fuerza, size=(T, 1)).astype(np.float32), T, axis=1)
    a += lineas[:, :, None]
    a += rng.normal(0.0, 1.4, size=(T, T, 1)).astype(np.float32)
    return Image.fromarray(a.clip(0, 255).astype(np.uint8))


def marco_chapa(d, remaches=True):
    m = p(0.045)
    d.rounded_rectangle([m, m, T - m, T - m], radius=p(0.02),
                        outline=GRIS_BORDE, width=p(0.006))
    d.rounded_rectangle([m + p(0.008), m + p(0.008), T - m - p(0.008), T - m - p(0.008)],
                        radius=p(0.018), outline=(60, 62, 65), width=p(0.003))
    if remaches:
        r = p(0.011)
        off = p(0.082)
        for cx, cy in [(off, off), (T - off, off), (off, T - off), (T - off, T - off)]:
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=GRIS_REMACHE)
            d.ellipse([cx - r * 0.6, cy - r * 0.6, cx + r * 0.35, cy + r * 0.35],
                      fill=(120, 122, 125))


def isotipo(d, cx, cy, r, grosor):
    sep = int(r * 0.86)
    der = [cx + sep - r, cy - r * 0.80, cx + sep + r, cy + r * 0.80]
    izq = [cx - sep - r, cy - r * 0.80, cx - sep + r, cy + r * 0.80]
    d.ellipse(der, outline=VERDE, width=grosor)
    d.ellipse(izq, outline=VERDE_OSC, width=int(grosor * 1.25))
    d.ellipse(izq, outline=VERDE, width=grosor)


def correa_horizontal(d, y0, y1, con_hebilla=False):
    d.rectangle([0, y0, T, y1], fill=CORREA)
    d.line([0, y0 + p(0.004), T, y0 + p(0.004)], fill=(90, 200, 108), width=p(0.003))
    d.line([0, y1 - p(0.004), T, y1 - p(0.004)], fill=CORREA_OSC, width=p(0.004))
    if con_hebilla:
        cx = T // 2
        hx0, hx1 = cx - p(0.09), cx + p(0.09)
        d.rectangle([hx0, y0 - p(0.012), hx1, y1 + p(0.012)], fill=HEBILLA,
                    outline=(60, 61, 64), width=p(0.004))
        for x in (hx0 + p(0.02), hx1 - p(0.02)):
            d.line([x, y0 - p(0.012), x, y1 + p(0.012)], fill=(60, 61, 64), width=p(0.003))


# --------------------------------------------------------------------------
# FRENTE: chapa con logo Eternity y correas de sujecion
# --------------------------------------------------------------------------
def tile_frente():
    img = lienzo(NEGRO_CLARO, NEGRO_OSCURO)
    img = cepillado(img, 111)
    d = ImageDraw.Draw(img)
    marco_chapa(d)

    correa_horizontal(d, p(0.145), p(0.205), con_hebilla=True)
    correa_horizontal(d, p(0.795), p(0.855), con_hebilla=True)

    cx = T // 2
    isotipo(d, cx, p(0.400), p(0.075), p(0.021))
    texto_centrado(d, cx, p(0.500), "Eternity", fuente(F_BOLD, p(0.095)), TINTA_CLARA)
    texto_centrado(d, cx, p(0.610), "TECHNOLOGIES", fuente(F_NARROW_BOLD, p(0.028)),
                   TINTA_SUAVE, tracking=p(0.010))
    texto_centrado(d, cx, p(0.648), "RACK AUTOELEVADOR", fuente(F_NARROW_BOLD, p(0.019)),
                   VERDE_OSC, tracking=p(0.008))
    return img


# --------------------------------------------------------------------------
# LATERAL: chapa con persiana de ventilacion
# --------------------------------------------------------------------------
def tile_lateral():
    img = lienzo(NEGRO_CLARO, NEGRO_OSCURO)
    img = cepillado(img, 222)
    d = ImageDraw.Draw(img)
    marco_chapa(d)

    x0, x1 = p(0.20), p(0.80)
    for i in range(8):
        y = p(0.20) + i * p(0.052)
        d.rounded_rectangle([x0, y, x1, y + p(0.028)], radius=p(0.009), fill=NEGRO_SOMBRA,
                            outline=GRIS_BORDE, width=p(0.002))

    cx = T // 2
    isotipo(d, cx, p(0.86), p(0.035), p(0.011))
    texto_centrado(d, cx, p(0.895), "Eternity", fuente(F_BOLD, p(0.05)), TINTA_SUAVE)
    return img


# --------------------------------------------------------------------------
# TAPA: borde superior de la bandeja + piso con marcas guia (vacio)
# --------------------------------------------------------------------------
CELDAS_COL, CELDAS_FIL = 6, 4
INT0, INT1 = 0.10, 0.90
CELDA_W = (INT1 - INT0) / CELDAS_COL
CELDA_H = (INT1 - INT0) / CELDAS_FIL


def _guias():
    for f in range(CELDAS_FIL):
        for c in range(CELDAS_COL):
            yield (INT0 + (c + 0.5) * CELDA_W, INT0 + (f + 0.5) * CELDA_H)


def tile_tapa():
    img = lienzo((58, 60, 63), (40, 42, 45))
    d = ImageDraw.Draw(img)
    marco_chapa(d, remaches=False)

    d.rounded_rectangle([p(0.068), p(0.068), p(0.932), p(0.932)], radius=p(0.012),
                        fill=GRIS_BANDEJA, outline=(64, 66, 70), width=p(0.005))

    # Rieles guia laterales, por donde entrarian las celdas
    for x in (p(INT0) - p(0.02), p(INT1) + p(0.02)):
        d.rectangle([x - p(0.010), p(0.075), x + p(0.010), p(0.925)], fill=(58, 60, 64),
                    outline=(76, 78, 82), width=p(0.002))

    # Marcas guia (silueta punteada) de cada celda, sin dibujarlas llenas
    for cx, cy in _guias():
        rx, ry = p(CELDA_W * 0.36), p(CELDA_H * 0.34)
        ccx, ccy = p(cx), p(cy)
        n = 18
        for i in range(n):
            a0 = (i / n) * 2 * np.pi
            if i % 2 == 0:
                continue
            a1 = ((i + 1) / n) * 2 * np.pi
            x0, y0 = ccx + np.cos(a0) * rx, ccy + np.sin(a0) * ry
            x1, y1 = ccx + np.cos(a1) * rx, ccy + np.sin(a1) * ry
            d.line([x0, y0, x1, y1], fill=GRIS_GUIA, width=p(0.0032))
        d.line([ccx - p(0.010), ccy, ccx + p(0.010), ccy], fill=GRIS_GUIA, width=p(0.0026))
        d.line([ccx, ccy - p(0.010), ccx, ccy + p(0.010)], fill=GRIS_GUIA, width=p(0.0026))

    texto_centrado(d, T // 2, p(0.035), "48V 620Ah  x24", fuente(F_NARROW_BOLD, p(0.020)),
                   TINTA_SUAVE, tracking=p(0.004))
    return img


# --------------------------------------------------------------------------
# FONDO: chapa con canales para las horquillas del autoelevador
# --------------------------------------------------------------------------
def tile_fondo():
    img = lienzo(NEGRO_OSCURO, NEGRO_SOMBRA)
    img = cepillado(img, 333, fuerza=4.0)
    d = ImageDraw.Draw(img)
    marco_chapa(d, remaches=False)

    for i in range(2):
        y = p(0.235) + i * p(0.40)
        d.rounded_rectangle([p(0.10), y, p(0.90), y + p(0.155)], radius=p(0.016),
                            fill=(30, 31, 34), outline=GRIS_BORDE, width=p(0.004))
        d.rectangle([p(0.10), y + p(0.010), p(0.90), y + p(0.022)], fill=AMARILLO)
        d.rectangle([p(0.10), y + p(0.133), p(0.90), y + p(0.145)], fill=AMARILLO)

    return img


# --------------------------------------------------------------------------
# Mapas de altura -> normal map
# --------------------------------------------------------------------------
def _marco_altura(d):
    m = p(0.045)
    d.rounded_rectangle([m, m, T - m, T - m], radius=p(0.02), outline=90, width=p(0.006))


def altura_frente():
    h = Image.new("L", (T, T), 128)
    d = ImageDraw.Draw(h)
    _marco_altura(d)
    for y0 in (p(0.145), p(0.795)):
        d.rectangle([0, y0, T, y0 + p(0.06)], fill=176)
    return h


def altura_lateral():
    h = Image.new("L", (T, T), 128)
    d = ImageDraw.Draw(h)
    _marco_altura(d)
    x0, x1 = p(0.20), p(0.80)
    for i in range(8):
        y = p(0.20) + i * p(0.052)
        d.rounded_rectangle([x0, y, x1, y + p(0.028)], radius=p(0.009), fill=88)
    return h


def altura_tapa():
    h = Image.new("L", (T, T), 128)
    d = ImageDraw.Draw(h)
    _marco_altura(d)
    d.rounded_rectangle([p(0.068), p(0.068), p(0.932), p(0.932)], radius=p(0.012), fill=112)
    for x in (p(INT0) - p(0.02), p(INT1) + p(0.02)):
        d.rectangle([x - p(0.010), p(0.075), x + p(0.010), p(0.925)], fill=150)
    for cx, cy in _guias():
        rx, ry = p(CELDA_W * 0.36), p(CELDA_H * 0.34)
        d.ellipse([p(cx) - rx, p(cy) - ry, p(cx) + rx, p(cy) + ry], outline=140, width=p(0.004))
    return h


def altura_fondo():
    h = Image.new("L", (T, T), 128)
    d = ImageDraw.Draw(h)
    _marco_altura(d)
    for i in range(2):
        y = p(0.235) + i * p(0.40)
        d.rounded_rectangle([p(0.10), y, p(0.90), y + p(0.155)], radius=p(0.016), fill=92)
    return h


def normal_desde_altura(h, semilla, fuerza=5.5):
    h = h.resize((TILE, TILE), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1.2))
    a = np.asarray(h, dtype=np.float32) / 255.0
    rng = np.random.default_rng(semilla)
    a += rng.normal(0.0, 0.005, size=(TILE, TILE)).astype(np.float32)

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

    n_frente = normal_desde_altura(altura_frente(), 5142)
    n_lateral = normal_desde_altura(altura_lateral(), 5143)
    n_tapa = normal_desde_altura(altura_tapa(), 5144)
    n_fondo = normal_desde_altura(altura_fondo(), 5145)

    for nombre, celdas in [
        ("T_RackEternity_Atlas.png", (frente, lateral, tapa, fondo)),
        ("T_RackEternity_AtlasNormal.png", (n_frente, n_lateral, n_tapa, n_fondo)),
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
