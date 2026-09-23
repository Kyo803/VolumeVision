# Making brushes for V² (Wallpaper Studio)

V² loads **PNG stamp brushes** from a folder — no code changes needed. Drop files in, hit **Reload brushes**, pick from the dropdown.

## Where they live

```
%AppData%\VolumeOSD\brushes\
```

Paste that path into Explorer's address bar. (In the Studio: **Reload brushes** also prints the path in the status line.)

## The format

| Thing | Rule |
|---|---|
| File type | `.png` with **transparent background** |
| Canvas | **Square** — 128×128 up to 512×512 (256×256 is ideal) |
| Shape colour | **White or greyscale** — the app reads brightness as *coverage* (alpha) |
| Tint | Automatic — the tip is recoloured to your pen colour, so **one tip works with every colour** |
| Soft edges | Just paint soft/gradient edges in the PNG — softness is preserved |

That's it. The tip is stamped along your stroke at the **Spacing** you set (a % of the brush size), and scaled to the **Pen size** slider.

## How to make one (Krita / Photoshop / GIMP)

1. New image, **square**, transparent background (e.g. 256×256).
2. Draw your tip shape in **white** on that transparent layer.
   - Hard round brush → crisp stamp
   - Soft airbrush → soft stamp
   - A leaf, star, splatter, chalk texture, logo — anything
3. Trim to a square, keep the shape centred with a little padding.
4. **Export as PNG** (keep transparency!).
5. Save into `%AppData%\VolumeOSD\brushes\` — e.g. `soft-round.png`, `chalk.png`, `star.png`.
6. In the Studio press **Reload brushes** and pick it from the dropdown.

> Tip: make the shape fill most of the canvas but leave ~5% padding, otherwise large brush sizes clip at the edges.

## Brush settings in the Studio

- **Spacing** — distance between stamps (5%–200% of size). Low = smooth, high = dotted/beaded.
- **Pen size** — overall stamp size.
- **Pen colour** — tints the stamp.
- **Reload brushes** — re-scans the folder (no restart needed).

## Notes

- `Round pen` is always the first entry — the built-in smooth round brush.
- Brushes are per-user; they are **not** bundled with the app.
- Want opacity/flow/jitter per brush? Say the word — the engine is set up to take it.
