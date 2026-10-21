"""Download the visual reference assets and open-source fonts used by ChromieCraft.

Development-only script: Python 3.11 + Pillow + fontTools. The desktop uses embedded assets,
never requests images or fonts from the website at runtime. See Assets/NOTICE.md.
"""
import io
import pathlib
import urllib.request
import sys

from PIL import Image
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

ROOT = pathlib.Path(__file__).resolve().parents[1] / "src/ChromieLauncher.Desktop/Assets"
ASSETS = {
    "lich-background.jpg": "https://www.chromiecraft.com/wp-content/uploads/2025/08/Lich_Background_1920x1080.png",
    "chromiecraft-wordmark.png": "https://www.chromiecraft.com/wp-content/uploads/2025/11/ChromieCraft-logo-hq.png",
    "chromiecraft-emblem.png": "https://www.chromiecraft.com/wp-content/uploads/2025/11/CC-new-navbar-logo.webp",
    "Fonts/MarcellusSC-Regular.ttf": "https://raw.githubusercontent.com/google/fonts/main/ofl/marcellussc/MarcellusSC-Regular.ttf",
    "Fonts/MarcellusSC-OFL.txt": "https://raw.githubusercontent.com/google/fonts/main/ofl/marcellussc/OFL.txt",
    "Fonts/RobotoCondensed.ttf": "https://raw.githubusercontent.com/google/fonts/main/ofl/robotocondensed/RobotoCondensed%5Bwght%5D.ttf",
    "Fonts/RobotoCondensed-OFL.txt": "https://raw.githubusercontent.com/google/fonts/main/ofl/robotocondensed/OFL.txt",
}


def create_icon(image):
    image = image.convert("RGBA")
    bounds = image.getbbox()
    if bounds:
        image = image.crop(bounds)
    image.thumbnail((256, 256))
    canvas = Image.new("RGBA", (256, 256))
    canvas.alpha_composite(image, ((256 - image.width) // 2, (256 - image.height) // 2))
    canvas.save(ROOT / "chromiecraft.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    print("chromiecraft.ico: 9 embedded resolutions (16–256 px)")


def main():
    for name, url in ASSETS.items():
        request = urllib.request.Request(url, headers={"User-Agent": "ChromieLauncher-Design/0.1"})
        with urllib.request.urlopen(request, timeout=90) as response:
            data = response.read()
        path = ROOT / name
        path.parent.mkdir(parents=True, exist_ok=True)
        if name.endswith((".png", ".jpg")):
            image = Image.open(io.BytesIO(data))
            if name.endswith(".jpg"):
                image.convert("RGB").save(path, quality=90, optimize=True)
            else:
                image = image.convert("RGBA")
                bounds = image.getbbox()
                if bounds:
                    image = image.crop(bounds)
                if name == "chromiecraft-emblem.png":
                    create_icon(image.copy())
                image.thumbnail((640, 320) if "wordmark" in name else (128, 128))
                image.save(path, optimize=True)
            print(name, image.size, path.stat().st_size)
        elif name == "Fonts/RobotoCondensed.ttf":
            # Avalonia 11 does not reliably select the correct weight of variable
            # fonts. Embed static instances instead of displaying the thin master.
            for weight, target in [(400, path), (600, path.with_name("RobotoCondensed-SemiBold.ttf"))]:
                font = TTFont(io.BytesIO(data))
                instance = instantiateVariableFont(font, {"wght": weight}, inplace=True, updateFontNames=True)
                instance.save(target)
                print(target.name, target.stat().st_size)
        else:
            path.write_bytes(data)
            print(name, len(data))


if __name__ == "__main__":
    if sys.argv[1:] == ["--icon-only"]:
        request = urllib.request.Request(ASSETS["chromiecraft-emblem.png"], headers={"User-Agent": "ChromieLauncher-Design/0.1"})
        with urllib.request.urlopen(request, timeout=90) as response:
            create_icon(Image.open(io.BytesIO(response.read())))
    else:
        main()
