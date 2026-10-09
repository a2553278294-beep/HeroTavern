# UI-DSL Spec For HtmlToUGUI

This spec defines the HTML subset that can be baked into Unity UGUI by the current `HtmlToUGUIBaker`.

## 1. Root And Resolution

- Use exactly one outer root node.
- The root MUST include `data-u-type="div"` and `data-u-name`.
- The root MUST declare an explicit design size using CSS `width` and `height`.
- Keep all generated nodes inside the root bounds.

Example:

```html
<div data-u-type="div" data-u-name="m_mainWindow"
     data-u-layout="stretch"
     style="width: 1920px; height: 1080px; position: relative;">
</div>
```

## 2. Required Attributes

Every Unity node MUST include:

- `data-u-name`: stable node name.
- `data-u-type`: one of `div`, `image`, `text`, `button`, `input`, `scroll`, `toggle`, `slider`, `dropdown`.

Recommended naming:

| data-u-type | Prefix | Example |
| --- | --- | --- |
| `div` | `m_` | `m_panelRoot` |
| `image` | `m_img` | `m_imgAvatar` |
| `text` | `m_text` | `m_textTitle` |
| `button` | `m_btn` | `m_btnClose` |
| `input` | `m_input` | `m_inputAccount` |
| `scroll` | `m_scroll` | `m_scrollItems` |
| `toggle` | `m_toggle` | `m_toggleSound` |
| `slider` | `m_slider` | `m_sliderVolume` |
| `dropdown` | `m_dropdown` | `m_dropdownQuality` |

## 3. Image Attributes

Use one of these supported image sources:

```html
<img data-u-type="image" data-u-name="m_imgAvatar" src="images/avatar.png">

<div data-u-type="image" data-u-name="m_imgIcon" data-u-src="images/icon.png"></div>

<div data-u-type="image" data-u-name="m_imgBg"
     style="background-image: url(images/bg.png); background-size: cover;"></div>
```

Supported source forms:

- `Assets/...` or `Packages/...` Unity asset paths.
- Relative local paths, resolved from the HTML/JSON source directory or configured source root.
- Absolute local file paths, copied into the configured Unity import folder.

For browser baking, use HTML-relative local image paths (or file URLs) that the browser can resolve.
`Assets/...` and `Packages/...` are primarily JSON/Unity input forms, not a browser alias.
All required files must be supplied; examples below need matching local images.

Unsupported source forms:

- `http://` or `https://` URLs.
- `data:` URIs.

Use `data-u-fit` to communicate image intent:

| data-u-fit | Unity behavior |
| --- | --- |
| `contain` | Assign Sprite and preserve aspect |
| `cover` | Assign Sprite and preserve aspect |
| `stretch` | Assign Sprite without preserve-aspect |
| `slice` | Use Sliced image when Sprite border exists |

## 4. Layout Attributes

`data-u-layout` is the preferred way to communicate multi-terminal intent.

| data-u-layout | Meaning |
| --- | --- |
| `stretch` | Stretch to all four parent edges |
| `top-bar` | Stretch horizontally and pin to top |
| `bottom-bar` | Stretch horizontally and pin to bottom |
| `left-panel` | Stretch vertically and pin to left |
| `right-panel` | Stretch vertically and pin to right |
| `center` | Fixed size centered in parent |
| `fixed` | v1-compatible top-left fixed placement |

Optional advanced explicit RectTransform fields:

- `data-u-anchor-min="0,0"`
- `data-u-anchor-max="1,1"`
- `data-u-pivot="0.5,0.5"`
- `data-u-offset-min="0,0"`
- `data-u-offset-max="0,0"`

These explicit fields are used only when both anchors and offsets are provided.

Use `data-u-safe-area="true"` on fullscreen or edge-pinned nodes that should be treated as safe-area aware. The current baker maps this to stretch-style layout intent; runtime safe-area components are outside this skill's scope.

## 5. Control-Specific Attributes

| Control | Attribute | Meaning |
| --- | --- | --- |
| `scroll` | `data-u-dir="v|h"` | Scroll direction |
| `toggle` | `data-u-checked="true|false"` | Initial state |
| `slider` | `data-u-value="0.0-1.0"` | Initial value |
| `dropdown` | `<option>` children | Option text |
| `input` | `placeholder` | Placeholder text |

## 6. CSS Guidance

- Use `px` sizes for bake-time measurement.
- Use real HTML layout (`flex`, absolute positioning, margins, padding) as needed; Playwright computes final rectangles.
- Avoid CSS animation and transition properties during baking.
- Scripts, event-handler attributes, external frames, links and metadata navigation are rejected.
- Keep fonts local via CSS; wait for their load, then separately validate the Unity TMP font.
- Use `background-color`, `color`, `font-size`, and `text-align` for visual style.
- Prefer explicit `data-u-layout` when a node needs to adapt across PC/mobile/pad.

## 7. Complete Example

```html
<div data-u-type="div" data-u-name="m_mainWindow" data-u-layout="stretch"
     style="width: 1920px; height: 1080px; position: relative; overflow: hidden;">
  <div data-u-type="image" data-u-name="m_imgBg"
       data-u-layout="stretch" data-u-fit="cover"
       style="position:absolute; inset:0; background-image:url(images/bg.png); background-size:cover;"></div>

  <div data-u-type="div" data-u-name="m_topBar" data-u-layout="top-bar"
       style="position:absolute; left:0; top:0; width:1920px; height:96px; background-color:#00000080;">
    <span data-u-type="text" data-u-name="m_textTitle"
          style="position:absolute; left:40px; top:24px; width:400px; height:48px; color:#fff; font-size:36px; text-align:left;">
      Main
    </span>
  </div>

  <img data-u-type="image" data-u-name="m_imgAvatar"
       src="images/avatar.png" data-u-layout="fixed" data-u-fit="contain"
       style="position:absolute; left:40px; top:120px; width:96px; height:96px;">

  <button data-u-type="button" data-u-name="m_btnStart" data-u-layout="center"
          style="position:absolute; left:810px; top:500px; width:300px; height:96px; background-color:#27ae60; color:#fff; font-size:32px;">
    Start
  </button>
</div>
```
