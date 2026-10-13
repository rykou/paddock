# Paddock brag plan

**What it is:** Paddock puts translucent panels ("fences") on the Windows desktop that hold files and shortcuts.
**Who it's for:** anyone whose desktop is covered in icons.
**What sets it apart:** every fence is a real folder, it's a standalone app that loads nothing into Explorer, and the installer is 725 KB.
**Visual hook:** a desktop buried in scattered icons, then one drag draws a fence and everything flies into place.
**Real UI shown:** the fence panel (title bar, roll-up arrow, icon grid), draw-to-create with the "Create fence here" button, roll-up, quick hide, colour presets. Rebuilt in HTML from `FenceForm.cs` (exact preset colours, 85% default opacity) because the app is WinForms and can't be embedded.
**Tone:** default. Punchy, clean, soft transitions.
**Share caption:** "My desktop was a junk drawer, so I built Paddock."

## Stand-ins
The README demo GIF shows a real desktop with real installed apps. The video uses fictional file and app names and a
drawn wallpaper instead.

## Visual identity
- Fence colours from the app's presets: Graphite `#202A3A`, Ocean `#0E7490`, Forest `#166534`, Plum `#6B21A8`, Rose `#BE185D`, Amber `#D97706`, Slate `#475569`.
- Segoe UI (what the app itself renders with), Consolas for the folder path.
- The app icon (`paddock.ico`) on the outro.
- Dusk-over-water wallpaper, drawn in CSS.

## Storyboard (20.0s, 1920x1080, 30fps)

| # | Time | Scene | On-screen text |
|---|---|---|---|
| 1 | 0.0–2.8 | Hook. 23 icons scattered across the desktop. | "Your desktop is a junk drawer." |
| 2 | 2.8–7.0 | Reveal. Cursor drags a rectangle, clicks **Create fence here**, the Games fence appears; Work and Tools follow and the icons fly into them. | "Paddock: fences for your desktop." |
| 3 | 7.0–10.6 | Highlight. A stray file is dragged onto Games; a path chip shows where it went. | "Every fence is a real folder." + `%USERPROFILE%\Paddock\Games` |
| 4 | 10.6–14.4 | Highlight. Double-click a title: fences roll up. Double-click the desktop: they all fade out. | "Roll them up." then "Double-click. Desktop cleared." |
| 5 | 14.4–17.2 | Highlight. Fences return and change colour through the presets; swatch row. | "Nine colour presets. Or pick any." |
| 6 | 17.2–20.0 | Outro. Dip to dark, app icon and name. | "Paddock" / "Desktop fences for Windows" / "Free · MIT · 725 KB installer" / `github.com/rykou/paddock` |

Transitions: captions always fade out before the next fades in; the outro dips through dark rather than crossfading.

## Sound
One piece in A minor at 120 bpm, so scene changes land on beats. Soft pad and plucked arpeggio; a light pulse joins at
the reveal. Effects are notes from the same scale through the same reverb: click pips, a whoosh as icons fly, a low
note on the drop, falling notes on roll-up, rising chime notes on the colour changes, a held chord on the outro.
