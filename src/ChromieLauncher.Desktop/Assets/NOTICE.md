# Design references and bundled assets

The desktop theme follows https://chromiecraft.com/en/: charcoal backgrounds,
the Lich King artwork, Marcellus SC headings, Roboto Condensed body text, and
thin outlined buttons. Assets are bundled to support offline use.

## ChromieCraft visuals

- `lich-background.jpg`: optimized JPEG of https://www.chromiecraft.com/wp-content/uploads/2025/08/Lich_Background_1920x1080.png
- `chromiecraft-wordmark.png`: cropped/resized from https://www.chromiecraft.com/wp-content/uploads/2025/11/ChromieCraft-logo-hq.png
- `chromiecraft-emblem.png`: converted/resized from https://www.chromiecraft.com/wp-content/uploads/2025/11/CC-new-navbar-logo.webp
- `chromiecraft.ico`: the same emblem, padded to a square with 16, 20, 24, 32, 40, 48, 64, 128 and 256 px variants. Embedded in the Windows executable and used by application windows and the taskbar.

These third-party visuals and trademarks belong to their respective owners.
Their presence does not imply affiliation, endorsement, or a new license from
this project. Verify permission for the artwork and marks before wider public
distribution. The application identifies itself as an independent community
launcher; it is not an official ChromieCraft product.

## Fonts

- Marcellus SC: https://github.com/google/fonts/tree/main/ofl/marcellussc
- Roboto Condensed: https://github.com/google/fonts/tree/main/ofl/robotocondensed

Both fonts are distributed under the SIL Open Font License 1.1. The complete
copyright notices and licenses are bundled next to the fonts in `Fonts/`.
Roboto Condensed is bundled as static 400/600 weight instances generated from
the variable font with fontTools, for correct weight selection in Avalonia 11.

The development-only script `tools/fetch_design_assets.py` records the source
URLs and image transformations. No external asset requests occur at runtime.
