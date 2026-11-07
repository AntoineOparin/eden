# Source: UI font and icons

## fonts/nunito-variable.ttf

- Font: Nunito (variable weight), The Nunito Project Authors, via Google Fonts
- URL: https://github.com/google/fonts/raw/main/ofl/nunito/Nunito%5Bwght%5D.ttf
- Downloaded: 2026-11-06
- License: SIL Open Font License 1.1 (`fonts/OFL.txt`, kept verbatim)

## icons/*.png

- Pack: Kenney "Game Icons" and "Game Icons Expansion" (white, 2x, 100 px)
- URLs: https://kenney.nl/assets/game-icons, https://kenney.nl/assets/game-icons-expansion
- Downloaded: 2026-11-06
- License: CC0 1.0 (`icons/License.txt`, kept verbatim)
- Kept and renamed: `pointer.png` (expansion), `pause.png`, `forward.png` (from `right.png`), `fast-forward.png`
  (from `fastForward.png`), `gear.png`.

The packs have no lightning, speech, sun or moon glyphs; those are drawn in code (`src/Eden.Game/Ui/UiTheme.cs`,
`Icons`), see `../generated/README.md`.
