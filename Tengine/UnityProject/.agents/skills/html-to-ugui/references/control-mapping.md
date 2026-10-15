# Control Mapping

`HtmlToUGUIBakeService` maps `data-u-type` values to Unity UGUI objects; the window and CLI share it.

## Component Mapping

| data-u-type | Unity structure | Main supported data |
| --- | --- | --- |
| `div` | GameObject + `RectTransform` + `Image` | color, optional background image, layout |
| `image` | GameObject + `RectTransform` + `Image` | `imageSrc`, `backgroundImageSrc`, `imageFit`, layout |
| `text` | GameObject + `TextMeshProUGUI` or Legacy `Text` | text, font color, font size, alignment |
| `button` | GameObject + `Image` + `Button` + child text | color, text, layout |
| `input` | GameObject + `Image` + input field hierarchy | placeholder/text style |
| `scroll` | GameObject + `Image` + `ScrollRect` + Viewport + Content | direction and child mounting |
| `toggle` | GameObject + `Toggle` + Background + Checkmark + Label | checked state and label |
| `slider` | GameObject + `Slider` + Background + Fill + Handle | numeric value |
| `dropdown` | GameObject + dropdown hierarchy | option list |

## Image Binding

For `div` and `image`, the baker creates an `Image` component first, then tries to bind a Sprite from:

1. `imageSrc`
2. `backgroundImageSrc`

Supported path resolution:

- `Assets/...`
- `Packages/...`
- absolute paths inside the Unity project
- relative paths from the explicit configured/JSON source root
- absolute local paths inside the explicit source root, copied into `HtmlToUGUIConfig.importedImageFolder`

The automated path fails preflight for unsupported or missing required images:

- remote HTTP/HTTPS URLs
- `data:` URIs
- missing local files

The interactive window retains its older warning-only image behavior. It is not the CI acceptance path.
Imported copies have deterministic relative-source hashes; repeated bakes do not allocate new filenames.
Package textures must already be imported Sprites; automation never changes package importers.

`imageFit` behavior:

- `contain`, `cover`, `scale-down`: set `Image.preserveAspect = true`
- `stretch`: no aspect preservation
- `slice`: use `Image.Type.Sliced` when the Sprite has a border

`cover` currently preserves aspect, but does not implement browser-style crop-to-fill.
CSS fonts are not converted into TMP assets. The CLI requires an existing TMP default font when text is generated.
Validate glyph coverage and metrics in Unity. Zero slider values and unchecked toggles must survive round trips.

## RectTransform Mapping

v1 JSON or disabled adaptive layout:

```text
anchorMin = anchorMax = (0, 1)
pivot = (0, 1)
anchoredPosition = (localX, -localY)
sizeDelta = (width, height)
```

v2 JSON with adaptive layout:

1. Use explicit `anchorMin`, `anchorMax`, `pivot`, `offsetMin`, `offsetMax` when all required values exist.
2. Use `safeArea`, `layoutHint`, or `anchorPreset`.
3. Infer common patterns from geometry.
4. Fall back to fixed v1 placement.

Supported adaptive patterns:

| Pattern | Anchors |
| --- | --- |
| `stretch` | `(0,0)` to `(1,1)` |
| `top-bar` | `(0,1)` to `(1,1)` |
| `bottom-bar` | `(0,0)` to `(1,0)` |
| `left-panel` | `(0,0)` to `(0,1)` |
| `right-panel` | `(1,0)` to `(1,1)` |
| `center` | `(0.5,0.5)` fixed size |

## Complex Control Hierarchies

### Button

```text
Button (Image + Button)
└── Text or Text (TMP)
```

### Input

```text
InputField (Image + TMP_InputField/InputField)
└── Text Area (RectMask2D)
    ├── Placeholder
    └── Text
```

### Scroll

```text
ScrollRect (Image + ScrollRect)
└── Viewport (RectMask2D)
    └── Content
```

### Toggle

```text
Toggle
├── Background (Image)
│   └── Checkmark (Image)
└── Label
```

### Slider

```text
Slider
├── Background (Image)
├── Fill Area
│   └── Fill (Image)
└── Handle Slide Area
    └── Handle (Image)
```

### Dropdown

```text
Dropdown
├── Label
├── Arrow
└── Template
    └── Viewport
        └── Content
            └── Item
```
