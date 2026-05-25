# SpoofGUI Design

## Direction

SpoofGUI is a compact operational tool, not a marketing app and not a generic VPN dashboard. The UI should feel quiet, technical, and trustworthy.

Default theme is dark. Light theme exists and uses the same layout and controls with inverted text and surfaces.

## Color Tokens

Runtime colors are stored as mutable XAML brushes in `../app/SpoofGUI/GUI/Styles/Tokens.xaml` and updated by `ThemeService`.

| Token | Dark | Light |
| --- | --- | --- |
| `SurfaceBase` | `#1B1E25` | `#F6F7F9` |
| `SurfaceRaised` | `#22262F` | `#FFFFFF` |
| `SurfaceSunken` | `#15181E` | `#EBEEF3` |
| `BorderSubtle` | `#2E333D` | `#D4DAE3` |
| `BorderStrong` | `#4A505C` | `#A4ADBC` |
| `TextPrimary` | `#F2F3F5` | `#181B21` |
| `TextSecondary` | `#A8ADB7` | `#4A525F` |
| `TextTertiary` | `#6E737E` | `#717B8A` |
| `AccentBase` | `#F4B656` | `#BE7E1C` |
| `AccentDim` | `#7A5A2C` | `#F7DEB8` |
| `StatusDanger` | `#D44B3A` | `#B2372A` |
| `StatusWarn` | `#D8A23B` | `#976712` |

## Typography

- UI font: Segoe UI / system UI.
- Mono font: used for IPs, ports, SNI values, timings, and logs.
- Display text is reserved for the page state: `SpoofGUI`, `starting`, `ready`, `error`.

## Icon System

Use built-in WinUI `SymbolIcon` glyphs instead of downloaded third-party icon packs.

Reasons:

- Clean licensing.
- No asset duplication for dark and light mode.
- Icons inherit `TextPrimary`, `StatusDanger`, or button foreground brushes.
- Consistent Windows visual language.

Current icon usage:

- Sidebar: Home, Edit, Setting, Document.
- Main page: Edit, Stop, Play.
- Config page: Undo, Save.
- Settings page: Sync.
- Logs page: Copy, Delete.

## Layout

- Left rail: `200px` wide, dark sunken surface.
- Main window default size: `1280x810`.
- Content padding: `32px`, with extra top padding on Logs to avoid title-bar overlap.
- Sections use spacing and text hierarchy rather than nested cards.
- Buttons are compact, 36px minimum height, small radius.

## Copy Rules

- Do not call SpoofGUI a VPN.
- Main page copy: `Connect and use your X-Ray Client.`
- Config labels use title case: `Fake SNI`, `Connect IP`, `Connect Port`.
- Keep text direct and technical.

## Logo and Icon

The sidebar uses the Canva-generated full logo:

```text
app/SpoofGUI/Assets/SpoofGUILogo.png
```

The Windows app icon uses a simplified stacked yellow text mark:

```text
Spoof
      GUI
```

Icon files:

```text
app/SpoofGUI/Assets/SpoofGUI.ico
app/SpoofGUI/Assets/SpoofGUIIcon.png
```

## Avoid

- Shield/VPN/globe metaphors.
- Pure black backgrounds.
- Neon cyber styling.
- Gradient text.
- Card-heavy dashboards.
- Big decorative hero sections.
