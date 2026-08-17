# -*- coding: utf-8 -*-
"""
Genera de forma procedural las texturas de la caja de baterias "Eternity"
que se usan en las cajas agarrables de la escena Planta.

Salidas (Assets/Props/CajaBateria/):

  Panel unico (para el mesh Cube nativo de Unity, cuyo UV manda las 6 caras
  al mismo cuadrado 0..1):
    - T_CajaBateria_Eternity_Albedo.png    1024, sRGB
    - T_CajaBateria_Eternity_Normal.png    1024, lineal

  Atlas 2x2 (para SM_CajaBateria, el mesh con UV por cara que genera
  Assets/Editor/CajaBateriaMeshTool.cs):
    - T_CajaBateria_Eternity_Atlas.png       2048, sRGB
    - T_CajaBateria_Eternity_AtlasNormal.png 2048, lineal

  Distribucion del atlas (coordenadas de imagen, origen arriba-izquierda):
    +------------------+------------------+
    | LADO_A (logo)    | LADO_B (rejilla) |   -> UV v en [0.5, 1.0]
    +------------------+------------------+
    | TAPA (vasos)     | FONDO            |   -> UV v en [0.0, 0.5]
    +------------------+------------------+
       UV u en [0,0.5]    UV u en [0.5,1]

  El mapeo cara -> celda esta replicado en CajaBateriaMeshTool.cs; si movés
  una celda acá, movela alla tambien.

Uso:  python generar_textura_bateria.py
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
SALIDA = os.path.normpath(os.path.join(AQUI, "..", "Assets", "Props", "CajaBateria"))

FUENTES = "C:/Windows/Fonts"
F_BOLD = os.path.join(FUENTES, "arialbd.ttf")
F_REG = os.path.join(FUENTES, "arial.ttf")
F_NARROW_BOLD = os.path.join(FUENTES, "ARIALNB.TTF")
F_BOLD_IT = os.path.join(FUENTES, "arialbi.ttf")

# Paleta tomada de la foto de referencia
GRIS_CLARO = (214, 217, 219)
GRIS_BASE = (196, 199, 201)
GRIS_OSCURO = (168, 171, 173)
GRIS_SOMBRA = (138, 141, 143)
VERDE = (58, 170, 71)
VERDE_OSC = (38, 132, 52)
TINTA = (42, 44, 46)
TINTA_SUAVE = (92, 95, 98)
AMARILLO = (238, 210, 60)

# Interior de la caja (vista desde arriba)
GRIS_BANDEJA = (104, 107, 110)
PLASTICO = (178, 180, 176)
PLASTICO_OSC = (146, 148, 145)
NEGRO_TAPON = (34, 35, 38)
GRIS_BARRA = (62, 64, 68)
BLANCO_TANQUE = (238, 238, 234)
TUBO = (198, 204, 208)
ROJO_POLO = (176, 48, 44)
AZUL_POLO = (46, 78, 150)


def p(v):
    """Fraccion del lado -> pixeles de la resolucion de trabajo."""
    return int(T * v)


def fuente(ruta, px):
    return ImageFont.truetype(ruta, px)


def texto_centrado(d, cx, y, txt, font, fill, tracking=0):
    """Dibuja texto centrado en cx con espaciado entre letras opcional."""
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
    """Base con degradado vertical."""
    px = np.zeros((T, T, 3), dtype=np.float32)
    t = np.linspace(0.0, 1.0, T, dtype=np.float32)[:, None]
    for c in range(3):
        px[:, :, c] = arriba[c] * (1.0 - t) + abajo[c] * t
    return Image.fromarray(px.astype(np.uint8))


def marco_chapa(d, remaches=True):
    """Bisel exterior comun a las cuatro celdas: define el borde de la caja."""
    m = p(0.045)
    d.rounded_rectangle([m, m, T - m, T - m], radius=p(0.02),
                        outline=GRIS_SOMBRA, width=p(0.006))
    d.rounded_rectangle([m + p(0.008), m + p(0.008), T - m - p(0.008), T - m - p(0.008)],
                        radius=p(0.018), outline=(212, 215, 217), width=p(0.003))
    if remaches:
        r = p(0.011)
        off = p(0.082)
        for cx, cy in [(off, off), (T - off, off), (off, T - off), (T - off, T - off)]:
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(150, 153, 155))
            d.ellipse([cx - r * 0.6, cy - r * 0.6, cx + r * 0.35, cy + r * 0.35],
                      fill=(206, 209, 211))


def etiqueta_specs(d, y0f=0.715, y1f=0.828):
    """Etiqueta amarilla de datos + cartel de peligro."""
    x0, y0 = p(0.665), p(y0f)
    x1, y1 = p(0.895), p(y1f)
    d.rounded_rectangle([x0 + p(0.004), y0 + p(0.004), x1 + p(0.004), y1 + p(0.004)],
                        radius=p(0.006), fill=(150, 150, 145))
    d.rounded_rectangle([x0, y0, x1, y1], radius=p(0.006),
                        fill=AMARILLO, outline=(190, 165, 40), width=p(0.002))

    f_tit = fuente(F_NARROW_BOLD, p(0.020))
    f_dat = fuente(F_REG, p(0.0145))
    d.text((x0 + p(0.012), y0 + p(0.010)), "48V  620Ah", font=f_tit, fill=TINTA)
    yy = y0 + p(0.038)
    for fila in ["MODELO: 24-EPzS-620", "SERIE: AE-2024-0187", "MASA: 20 kg"]:
        d.text((x0 + p(0.012), yy), fila, font=f_dat, fill=(58, 50, 20))
        yy += p(0.020)

    xw = p(0.105)
    d.rounded_rectangle([xw, y0, xw + p(0.115), y1], radius=p(0.006),
                        fill=(232, 234, 236), outline=GRIS_SOMBRA, width=p(0.0018))
    f_w = fuente(F_NARROW_BOLD, p(0.017))
    for i, fila in enumerate(["PELIGRO", "ACIDO", "NO FUMAR"]):
        d.text((xw + p(0.012), y0 + p(0.016) + i * p(0.026)), fila, font=f_w,
               fill=(178, 46, 40) if i == 0 else TINTA_SUAVE)


def isotipo(d, cx, cy, r, grosor):
    """Simbolo de infinito: dos anillos entrelazados."""
    sep = int(r * 0.86)
    der = [cx + sep - r, cy - r * 0.80, cx + sep + r, cy + r * 0.80]
    izq = [cx - sep - r, cy - r * 0.80, cx - sep + r, cy + r * 0.80]
    d.ellipse(der, outline=VERDE, width=grosor)
    # El anillo izquierdo va ultimo para que el cruce lea como "infinito"
    d.ellipse(izq, outline=VERDE_OSC, width=int(grosor * 1.25))
    d.ellipse(izq, outline=VERDE, width=grosor)


# --------------------------------------------------------------------------
# LADO_A: cara frontal con el logotipo completo
# --------------------------------------------------------------------------
def tile_lado_logo():
    img = lienzo(GRIS_CLARO, GRIS_OSCURO)
    d = ImageDraw.Draw(img)
    marco_chapa(d)
    m = p(0.045)

    # Costuras de tapa y base
    d.line([m, p(0.135), T - m, p(0.135)], fill=GRIS_SOMBRA, width=p(0.005))
    d.line([m, p(0.141), T - m, p(0.141)], fill=(224, 227, 229), width=p(0.0025))
    d.line([m, p(0.875), T - m, p(0.875)], fill=GRIS_SOMBRA, width=p(0.004))
    d.line([m, p(0.880), T - m, p(0.880)], fill=(220, 223, 225), width=p(0.002))

    cx = T // 2
    isotipo(d, cx, p(0.300), p(0.070), p(0.020))
    texto_centrado(d, cx, p(0.395), "Eternity", fuente(F_BOLD, p(0.105)), TINTA)
    texto_centrado(d, cx, p(0.525), "TECHNOLOGIES", fuente(F_NARROW_BOLD, p(0.032)),
                   TINTA_SUAVE, tracking=p(0.012))
    texto_centrado(d, cx, p(0.570), "SOUTH AMERICA", fuente(F_NARROW_BOLD, p(0.024)),
                   VERDE_OSC, tracking=p(0.010))

    d.line([cx - p(0.30), p(0.625), cx + p(0.30), p(0.625)],
           fill=GRIS_SOMBRA, width=p(0.0025))
    f_cred, f_auto = fuente(F_REG, p(0.024)), fuente(F_BOLD_IT, p(0.030))
    w1 = d.textlength("DISEÑADO POR: ", font=f_cred)
    w2 = d.textlength("Autoelec", font=f_auto)
    x0 = cx - (w1 + w2) / 2
    d.text((x0, p(0.652)), "DISEÑADO POR: ", font=f_cred, fill=TINTA_SUAVE)
    d.text((x0 + w1, p(0.648)), "Autoelec", font=f_auto, fill=TINTA)

    etiqueta_specs(d)
    return img


# --------------------------------------------------------------------------
# LADO_B: cara lateral con rejilla de ventilacion y logo chico
# --------------------------------------------------------------------------
def tile_lado_rejilla():
    img = lienzo(GRIS_CLARO, GRIS_OSCURO)
    d = ImageDraw.Draw(img)
    marco_chapa(d)
    m = p(0.045)

    d.line([m, p(0.135), T - m, p(0.135)], fill=GRIS_SOMBRA, width=p(0.005))
    d.line([m, p(0.141), T - m, p(0.141)], fill=(224, 227, 229), width=p(0.0025))
    d.line([m, p(0.875), T - m, p(0.875)], fill=GRIS_SOMBRA, width=p(0.004))

    # Persiana de ventilacion
    x0, x1 = p(0.215), p(0.785)
    for i in range(7):
        y = p(0.225) + i * p(0.042)
        d.rounded_rectangle([x0, y, x1, y + p(0.022)], radius=p(0.008),
                            fill=(96, 99, 102))
        d.line([x0 + p(0.006), y + p(0.019), x1 - p(0.006), y + p(0.019)],
               fill=(206, 209, 211), width=p(0.0035))

    # Tornillos de la persiana
    for x in (x0 - p(0.035), x1 + p(0.035)):
        for y in (p(0.215), p(0.520)):
            d.ellipse([x - p(0.009), y - p(0.009), x + p(0.009), y + p(0.009)],
                      fill=(150, 153, 155))

    cx = T // 2
    isotipo(d, cx, p(0.625), p(0.042), p(0.013))
    texto_centrado(d, cx, p(0.678), "Eternity", fuente(F_BOLD, p(0.062)), TINTA)
    texto_centrado(d, cx, p(0.756), "TECHNOLOGIES", fuente(F_NARROW_BOLD, p(0.020)),
                   TINTA_SUAVE, tracking=p(0.008))

    # Placa de identificacion grabada
    d.rounded_rectangle([p(0.100), p(0.700), p(0.100) + p(0.150), p(0.700) + p(0.095)],
                        radius=p(0.006), fill=(186, 189, 191),
                        outline=GRIS_SOMBRA, width=p(0.002))
    f_p = fuente(F_NARROW_BOLD, p(0.017))
    for i, fila in enumerate(["EPzS", "620 Ah", "48 V"]):
        d.text((p(0.112), p(0.714) + i * p(0.024)), fila, font=f_p, fill=TINTA_SUAVE)
    return img


# --------------------------------------------------------------------------
# TAPA: vista cenital con los vasos, barras, cables y el tanque de agua
# --------------------------------------------------------------------------
CELDAS_COL, CELDAS_FIL = 6, 4
INT0, INT1 = 0.085, 0.915          # interior de la bandeja
CELDA_W = (INT1 - INT0) / CELDAS_COL
CELDA_H = (INT1 - INT0) / CELDAS_FIL
TAPON_Y = 0.40                     # altura del tapon dentro del vaso (0..1)


def _vasos():
    """(columna, fila, x0, y0) de cada vaso, en fracciones del lado."""
    for f in range(CELDAS_FIL):
        for c in range(CELDAS_COL):
            yield c, f, INT0 + c * CELDA_W, INT0 + f * CELDA_H


def _centro_tapon(c, f):
    return (INT0 + (c + 0.5) * CELDA_W, INT0 + f * CELDA_H + TAPON_Y * CELDA_H)


def tile_tapa():
    img = lienzo((196, 199, 201), (176, 179, 181))
    d = ImageDraw.Draw(img)

    # Bandeja: borde de chapa y hueco interior en sombra
    marco_chapa(d, remaches=False)
    d.rounded_rectangle([p(0.068), p(0.068), p(0.932), p(0.932)], radius=p(0.012),
                        fill=GRIS_BANDEJA, outline=(124, 127, 130), width=p(0.005))

    rng = np.random.default_rng(7)

    # 1) Vasos: bloques de plastico separados por juntas oscuras
    for c, f, x, y in _vasos():
        g = p(0.006)
        tono = int(rng.normal(0, 4))
        cuerpo = tuple(np.clip(np.array(PLASTICO) + tono, 0, 255).astype(int))
        d.rounded_rectangle([p(x) + g, p(y) + g, p(x + CELDA_W) - g, p(y + CELDA_H) - g],
                            radius=p(0.007), fill=cuerpo,
                            outline=PLASTICO_OSC, width=p(0.0022))
        # Bisel superior del vaso
        d.line([p(x) + g * 2, p(y) + g * 2, p(x + CELDA_W) - g * 2, p(y) + g * 2],
               fill=(200, 202, 198), width=p(0.0028))

    # 2) Tuberia de llenado, por debajo de las barras
    for f in range(CELDAS_FIL):
        y = INT0 + f * CELDA_H + TAPON_Y * CELDA_H - 0.062
        x_fin = INT1 - 0.02 if f in (0, 3) else 0.615
        d.line([p(INT0 + 0.02), p(y), p(x_fin), p(y)], fill=TUBO, width=p(0.0055))
        for c in range(CELDAS_COL):
            cxx, ccy = (p(v) for v in _centro_tapon(c, f))
            if cxx > p(x_fin):
                continue
            d.line([cxx, p(y), cxx, ccy - p(0.024)], fill=TUBO, width=p(0.0042))

    # 3) Interconexiones: barras cortas que solo cruzan la junta entre vasos
    largo, grueso = p(0.045), p(0.017)
    for f in range(CELDAS_FIL):
        yb = p(INT0 + f * CELDA_H + TAPON_Y * CELDA_H)
        for c in range(CELDAS_COL - 1):
            xb = p(INT0 + (c + 1) * CELDA_W)
            d.rounded_rectangle([xb - largo, yb - grueso, xb + largo, yb + grueso],
                                radius=grueso, fill=GRIS_BARRA)
            d.line([xb - largo + p(0.006), yb - grueso * 0.45,
                    xb + largo - p(0.006), yb - grueso * 0.45],
                   fill=(96, 99, 103), width=p(0.0035))
        # Puente entre filas, alternando extremo
        if f < CELDAS_FIL - 1:
            col = CELDAS_COL - 1 if f % 2 == 0 else 0
            xb = p(INT0 + (col + 0.5) * CELDA_W)
            y1 = p(INT0 + f * CELDA_H + TAPON_Y * CELDA_H)
            y2 = p(INT0 + (f + 1) * CELDA_H + TAPON_Y * CELDA_H)
            d.rounded_rectangle([xb - grueso, y1, xb + grueso, y2],
                                radius=grueso, fill=GRIS_BARRA)

    # 4) Mazo de cables hacia el conector, antes que los tapones
    conector = (0.500, 0.052)
    for col, destino in ((0, conector[0] - 0.055), (CELDAS_COL - 1, conector[0] + 0.055)):
        bx, by = (p(v) for v in _centro_tapon(col, 0))
        d.line([bx, by, bx, p(0.128), p(destino), p(0.098), p(destino), p(conector[1])],
               fill=(24, 24, 26), width=p(0.019), joint="curve")
    d.rounded_rectangle([p(0.395), p(0.028), p(0.605), p(0.086)], radius=p(0.010),
                        fill=(26, 26, 28), outline=(70, 70, 74), width=p(0.004))
    d.rounded_rectangle([p(0.420), p(0.044), p(0.580), p(0.070)], radius=p(0.006),
                        fill=(58, 58, 62))

    # 5) Tapones de ventilacion, por encima de barras y cables
    for c, f, x, y in _vasos():
        ccx, ccy = (p(v) for v in _centro_tapon(c, f))
        rx, ry = p(0.030), p(0.033)
        d.ellipse([ccx - rx - p(0.005), ccy - ry - p(0.005),
                   ccx + rx + p(0.005), ccy + ry + p(0.005)], fill=(22, 23, 25))
        d.ellipse([ccx - rx, ccy - ry, ccx + rx, ccy + ry], fill=NEGRO_TAPON)
        d.ellipse([ccx - rx * 0.45, ccy - ry * 0.55, ccx + rx * 0.10, ccy - ry * 0.05],
                  fill=(80, 82, 86))

    # 6) Bornes de salida
    for col, color in ((0, ROJO_POLO), (CELDAS_COL - 1, AZUL_POLO)):
        bx, by = (p(v) for v in _centro_tapon(col, 0))
        d.ellipse([bx - p(0.026), by - p(0.026), bx + p(0.026), by + p(0.026)],
                  fill=color, outline=(24, 24, 26), width=p(0.005))
        d.ellipse([bx - p(0.010), by - p(0.010), bx + p(0.010), by + p(0.010)],
                  fill=(34, 34, 36))

    # Tanque de agua desmineralizada, visto desde arriba
    tcx, tcy, tr = p(0.762), p(0.520), p(0.148)
    d.ellipse([tcx - tr + p(0.014), tcy - tr + p(0.016),
               tcx + tr + p(0.014), tcy + tr + p(0.016)], fill=(48, 50, 52))
    d.ellipse([tcx - tr, tcy - tr, tcx + tr, tcy + tr],
              fill=BLANCO_TANQUE, outline=(184, 186, 182), width=p(0.004))
    # Hombro del bidon: dos anillos concentricos
    for k, tono in ((0.88, (214, 214, 210)), (0.62, (226, 226, 222))):
        d.ellipse([tcx - tr * k, tcy - tr * k, tcx + tr * k, tcy + tr * k],
                  outline=tono, width=p(0.005))
    # Canto de la etiqueta verde, apenas visible desde arriba
    d.arc([tcx - tr * 0.97, tcy - tr * 0.97, tcx + tr * 0.97, tcy + tr * 0.97],
          start=25, end=200, fill=VERDE_OSC, width=p(0.005))
    # Tapa roscada
    d.ellipse([tcx - tr * 0.30, tcy - tr * 0.30, tcx + tr * 0.30, tcy + tr * 0.30],
              fill=(212, 212, 208), outline=(168, 170, 166), width=p(0.004))
    for i in range(18):
        ang = i * (2 * np.pi / 18)
        d.line([tcx + np.cos(ang) * tr * 0.23, tcy + np.sin(ang) * tr * 0.23,
                tcx + np.cos(ang) * tr * 0.30, tcy + np.sin(ang) * tr * 0.30],
               fill=(176, 178, 174), width=p(0.0028))

    # Manguera del tanque al colector
    d.line([tcx - tr * 0.82, tcy + tr * 0.58, p(0.640), p(0.700), p(0.560), p(0.742)],
           fill=TUBO, width=p(0.0065), joint="curve")
    return img


# --------------------------------------------------------------------------
# FONDO: cara inferior, chapa lisa con patas
# --------------------------------------------------------------------------
def tile_fondo():
    img = lienzo((168, 171, 173), (148, 151, 153))
    d = ImageDraw.Draw(img)
    marco_chapa(d, remaches=False)

    # Nervios de refuerzo
    for i in range(3):
        y = p(0.290) + i * p(0.210)
        d.rounded_rectangle([p(0.120), y, p(0.880), y + p(0.048)], radius=p(0.014),
                            fill=(156, 159, 161), outline=(132, 135, 137), width=p(0.002))
        d.line([p(0.126), y + p(0.006), p(0.874), y + p(0.006)],
               fill=(196, 199, 201), width=p(0.004))

    # Patas de goma
    for cx, cy in [(p(0.150), p(0.150)), (p(0.850), p(0.150)),
                   (p(0.150), p(0.850)), (p(0.850), p(0.850))]:
        r = p(0.055)
        d.rounded_rectangle([cx - r, cy - r, cx + r, cy + r], radius=p(0.014),
                            fill=(48, 48, 50))
        d.rounded_rectangle([cx - r * 0.6, cy - r * 0.6, cx + r * 0.6, cy + r * 0.6],
                            radius=p(0.008), fill=(70, 70, 73))

    texto_centrado(d, T // 2, p(0.478), "Eternity", fuente(F_BOLD, p(0.048)),
                   (128, 131, 133))
    texto_centrado(d, T // 2, p(0.545), "INDUSTRIA ARGENTINA",
                   fuente(F_NARROW_BOLD, p(0.020)), (132, 135, 137), tracking=p(0.006))
    return img


# --------------------------------------------------------------------------
# Mapas de altura (para derivar los normales)
# --------------------------------------------------------------------------
def _marco_altura(d):
    m = p(0.045)
    d.rounded_rectangle([m, m, T - m, T - m], radius=p(0.02), outline=90, width=p(0.006))


def altura_lado_logo():
    h = Image.new("L", (T, T), 128)
    d = ImageDraw.Draw(h)
    _marco_altura(d)
    m = p(0.045)
    d.line([m, p(0.135), T - m, p(0.135)], fill=86, width=p(0.005))
    d.line([m, p(0.875), T - m, p(0.875)], fill=92, width=p(0.004))
    r, off = p(0.011), p(0.082)
    for cx, cy in [(off, off), (T - off, off), (off, T - off), (T - off, T - off)]:
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=185)
    d.rounded_rectangle([p(0.665), p(0.715), p(0.895), p(0.828)], radius=p(0.006), fill=150)
    d.rounded_rectangle([p(0.105), p(0.715), p(0.220), p(0.828)], radius=p(0.006), fill=148)
    return h


def altura_lado_rejilla():
    h = Image.new("L", (T, T), 128)
    d = ImageDraw.Draw(h)
    _marco_altura(d)
    m = p(0.045)
    d.line([m, p(0.135), T - m, p(0.135)], fill=86, width=p(0.005))
    for i in range(7):
        y = p(0.225) + i * p(0.042)
        d.rounded_rectangle([p(0.215), y, p(0.785), y + p(0.022)], radius=p(0.008), fill=72)
    d.rounded_rectangle([p(0.100), p(0.700), p(0.250), p(0.795)], radius=p(0.006), fill=146)
    return h


def altura_tapa():
    h = Image.new("L", (T, T), 128)
    d = ImageDraw.Draw(h)
    _marco_altura(d)
    d.rounded_rectangle([p(0.068), p(0.068), p(0.932), p(0.932)], radius=p(0.012), fill=92)

    for c, f, x, y in _vasos():
        g = p(0.006)
        d.rounded_rectangle([p(x) + g, p(y) + g, p(x + CELDA_W) - g, p(y + CELDA_H) - g],
                            radius=p(0.007), fill=152)
        ccx, ccy = (p(v) for v in _centro_tapon(c, f))
        d.ellipse([ccx - p(0.028), ccy - p(0.031), ccx + p(0.028), ccy + p(0.031)], fill=188)

    for f in range(CELDAS_FIL):
        y = INT0 + f * CELDA_H + TAPON_Y * CELDA_H - 0.062
        d.line([p(INT0 + 0.02), p(y), p(INT1 - 0.02), p(y)], fill=168, width=p(0.0055))

    largo, grueso = p(0.052), p(0.020)
    for f in range(CELDAS_FIL):
        yb = p(INT0 + f * CELDA_H + TAPON_Y * CELDA_H)
        for c in range(CELDAS_COL - 1):
            xb = p(INT0 + (c + 1) * CELDA_W)
            d.rounded_rectangle([xb - largo, yb - grueso, xb + largo, yb + grueso],
                                radius=grueso, fill=178)
        if f < CELDAS_FIL - 1:
            col = CELDAS_COL - 1 if f % 2 == 0 else 0
            xb = p(INT0 + (col + 0.5) * CELDA_W)
            d.rounded_rectangle([xb - grueso, p(INT0 + f * CELDA_H + TAPON_Y * CELDA_H),
                                 xb + grueso,
                                 p(INT0 + (f + 1) * CELDA_H + TAPON_Y * CELDA_H)],
                                radius=grueso, fill=178)

    for col, destino in ((0, 0.445), (CELDAS_COL - 1, 0.555)):
        bx, by = (p(v) for v in _centro_tapon(col, 0))
        d.line([bx, by, bx, p(0.135), p(destino), p(0.100), p(destino), p(0.052)],
               fill=198, width=p(0.021), joint="curve")
    d.rounded_rectangle([p(0.395), p(0.028), p(0.605), p(0.086)], radius=p(0.010), fill=208)

    tcx, tcy, tr = p(0.762), p(0.520), p(0.148)
    d.ellipse([tcx - tr, tcy - tr, tcx + tr, tcy + tr], fill=236)
    d.ellipse([tcx - tr * 0.30, tcy - tr * 0.30, tcx + tr * 0.30, tcy + tr * 0.30], fill=249)
    return h


def altura_fondo():
    h = Image.new("L", (T, T), 128)
    d = ImageDraw.Draw(h)
    _marco_altura(d)
    for i in range(3):
        y = p(0.290) + i * p(0.210)
        d.rounded_rectangle([p(0.120), y, p(0.880), y + p(0.048)], radius=p(0.014), fill=168)
    for cx, cy in [(p(0.150), p(0.150)), (p(0.850), p(0.150)),
                   (p(0.150), p(0.850)), (p(0.850), p(0.850))]:
        r = p(0.055)
        d.rounded_rectangle([cx - r, cy - r, cx + r, cy + r], radius=p(0.014), fill=196)
    return h


# --------------------------------------------------------------------------
# Post-proceso
# --------------------------------------------------------------------------
def envejecer(img, semilla):
    """Cepillado, grano, suciedad y viñeta sobre una celda ya reducida."""
    rng = np.random.default_rng(semilla)
    a = np.asarray(img, dtype=np.float32)

    lineas = np.repeat(rng.normal(0.0, 3.2, size=(TILE, 1)).astype(np.float32),
                       TILE, axis=1)
    a += lineas[:, :, None]
    a += rng.normal(0.0, 1.2, size=(TILE, TILE, 1)).astype(np.float32)

    manchas = rng.normal(0.0, 1.0, size=(TILE // 8, TILE // 8)).astype(np.float32)
    manchas = Image.fromarray(
        ((manchas * 0.5 + 0.5) * 255).clip(0, 255).astype(np.uint8)
    ).resize((TILE, TILE), Image.BICUBIC).filter(ImageFilter.GaussianBlur(6))
    a += (np.asarray(manchas, dtype=np.float32)[:, :, None] - 128.0) * 0.075

    g = np.linspace(-1.0, 1.0, TILE, dtype=np.float32)
    xx, yy = np.meshgrid(g, g)
    vig = 1.0 - 0.13 * np.clip(np.sqrt(xx ** 2 + yy ** 2) / 1.414, 0, 1) ** 1.5
    a *= vig[:, :, None]
    return Image.fromarray(a.clip(0, 255).astype(np.uint8))


def normal_desde_altura(h, semilla, fuerza=6.0):
    """Sobel sobre el mapa de alturas -> normal map en espacio tangente."""
    h = h.resize((TILE, TILE), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1.2))
    a = np.asarray(h, dtype=np.float32) / 255.0
    rng = np.random.default_rng(semilla)
    a += np.repeat(rng.normal(0.0, 0.006, size=(TILE, 1)).astype(np.float32), TILE, axis=1)

    dx = np.gradient(a, axis=1) * fuerza
    dy = np.gradient(a, axis=0) * fuerza
    nz = np.ones_like(a)
    largo = np.sqrt(dx ** 2 + dy ** 2 + nz ** 2)
    n = np.stack([-dx / largo * 0.5 + 0.5,
                  dy / largo * 0.5 + 0.5,
                  nz / largo * 0.5 + 0.5], axis=-1)
    return Image.fromarray((n * 255).clip(0, 255).astype(np.uint8))


def reducir(img, semilla):
    return envejecer(img.resize((TILE, TILE), Image.LANCZOS), semilla)


# --------------------------------------------------------------------------
def main():
    os.makedirs(SALIDA, exist_ok=True)

    print("Dibujando celdas...")
    lado_a = reducir(tile_lado_logo(), 20260817)
    lado_b = reducir(tile_lado_rejilla(), 20260818)
    tapa = reducir(tile_tapa(), 20260819)
    fondo = reducir(tile_fondo(), 20260820)

    n_lado_a = normal_desde_altura(altura_lado_logo(), 4242)
    n_lado_b = normal_desde_altura(altura_lado_rejilla(), 4243)
    n_tapa = normal_desde_altura(altura_tapa(), 4244)
    n_fondo = normal_desde_altura(altura_fondo(), 4245)

    # Panel unico (mesh Cube nativo): reutiliza la cara frontal
    for nombre, im in [("T_CajaBateria_Eternity_Albedo.png", lado_a),
                       ("T_CajaBateria_Eternity_Normal.png", n_lado_a)]:
        im.save(os.path.join(SALIDA, nombre), optimize=True)
        print("OK ->", nombre)

    # Atlas 2x2 (mesh SM_CajaBateria con UV por cara)
    for nombre, celdas in [
        ("T_CajaBateria_Eternity_Atlas.png", (lado_a, lado_b, tapa, fondo)),
        ("T_CajaBateria_Eternity_AtlasNormal.png", (n_lado_a, n_lado_b, n_tapa, n_fondo)),
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
