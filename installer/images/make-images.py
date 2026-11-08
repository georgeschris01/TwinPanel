# TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
# Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
#
# Fabrique les images de l'installateur (Inno Setup 7), à relancer seulement si le logo ou les captures changent :
#   python installer/images/make-images.py      (depuis la racine du projet ; Windows, Pillow)
# - accueil-<langue>-<l>x<h>.png : bande de gauche des pages d'accueil et de fin (logo, nom, petite capture),
#   une taille par réglage d'affichage (100 % à 250 %), proportions 164:314 demandées par Inno Setup ;
# - logo-<n>.png : logo carré du bandeau des autres pages (fond transparent).
# Captures : docs/screenshots/windows-fr.png et windows-en.png (faites avec des fichiers d'exemple).
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "installer", "images")
TOP, BOTTOM = (24, 58, 98), (14, 36, 64)   # bleu foncé du bandeau (même couleur que dans TwinPanel.iss)
YELLOW = (255, 196, 0)
WELCOME_SIZES = [(202, 386), (269, 515), (336, 643), (403, 772), (430, 824), (534, 1022)]
LOGO_SIZES = [58, 77, 97, 116, 124, 143, 159]

logo = Image.open(os.path.join(ROOT, "logo", "logo-TwinPanel-1024.png")).convert("RGBA")
bold = lambda px: ImageFont.truetype(os.path.join(os.environ["WINDIR"], "Fonts", "segoeuib.ttf"), px)


def welcome(shot, w, h):
    im = Image.new("RGB", (w, h)); d = ImageDraw.Draw(im)
    for y in range(h):
        t = y / (h - 1); d.line([(0, y), (w, y)], fill=tuple(round(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3)))
    u = w / 200                                                    # unité : 1/200 de la largeur
    s = round(110 * u); lg = logo.resize((s, s), Image.LANCZOS); im.paste(lg, ((w - s) // 2, round(34 * u)), lg)
    f = bold(round(27 * u)); name = "TwinPanel"; tw = d.textlength(name, font=f)
    d.text(((w - tw) / 2, round(150 * u)), name, font=f, fill=(255, 255, 255))
    d.line([(round(60 * u), round(190 * u)), (w - round(60 * u), round(190 * u))], fill=YELLOW, width=max(2, round(2 * u)))
    tw = round(164 * u); th = round(tw * shot.height / shot.width)
    t = shot.resize((tw, th), Image.LANCZOS)
    b = max(2, round(2 * u)); frame = Image.new("RGBA", (tw + 2 * b, th + 2 * b), (255, 255, 255, 255)); frame.paste(t, (b, b))
    m = round(10 * u); shadow = Image.new("RGBA", (frame.width + 2 * m, frame.height + 2 * m), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rectangle([m, m + round(2 * u), m + frame.width, m + frame.height + round(2 * u)], fill=(0, 0, 0, 150))
    shadow = shadow.filter(ImageFilter.GaussianBlur(round(5 * u))); shadow.alpha_composite(frame, (m, m))
    im.paste(shadow, ((w - shadow.width) // 2, round(206 * u) - m), shadow)
    return im


for lang in ("fr", "en"):
    shot = Image.open(os.path.join(ROOT, "docs", "screenshots", f"windows-{lang}.png")).convert("RGB")
    for w, h in WELCOME_SIZES:
        welcome(shot, w, h).save(os.path.join(OUT, f"accueil-{lang}-{w}x{h}.png"), optimize=True)
for n in LOGO_SIZES:
    logo.resize((n, n), Image.LANCZOS).save(os.path.join(OUT, f"logo-{n}.png"), optimize=True)
