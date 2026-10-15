# HtmlToUGUI JSON Schema

The baker accepts two compatible shapes:

- v1: coordinate-only node JSON with no `schemaVersion`.
- v2: additive metadata on the same node shape.

If `schemaVersion` is missing, Unity treats the JSON as v1 and uses fixed top-left placement.

## v1 Fields

Every node keeps these existing fields:

```json
{
  "name": "m_panelRoot",
  "type": "div",
  "dir": "v",
  "value": 0.5,
  "isChecked": false,
  "options": [],
  "x": 0,
  "y": 0,
  "width": 1920,
  "height": 1080,
  "color": "#FFFFFF00",
  "fontColor": "#000000",
  "fontSize": 24,
  "textAlign": "center",
  "text": "",
  "children": []
}
```

Coordinates are root-relative in top-left HTML space. The Unity baker converts them to UGUI coordinates.

## v2 Root Fields

The root node MAY include:

```json
{
  "schemaVersion": 2,
  "designWidth": 1920,
  "designHeight": 1080,
  "sourcePath": "prototypes/main.html",
  "htmlFilePath": "prototypes/main.html",
  "sourceDirectory": "prototypes"
}
```

Meaning:

- `schemaVersion`: `2` enables smart image and adaptive layout behavior.
- `designWidth` / `designHeight`: CanvasScaler reference resolution.
- `sourcePath` / `htmlFilePath` / `sourceDirectory`: used to resolve relative image sources.
  The browser writes resolved local absolute paths at runtime; do not commit machine-specific results.
  Hand-authored relative metadata is resolved against the Unity project. Omitting it uses the JSON directory.

## v2 Node Fields

Every node MAY include:

```json
{
  "imageSrc": "images/avatar.png",
  "backgroundImageSrc": "images/bg.png",
  "imageFit": "contain",
  "layoutHint": "center",
  "safeArea": "true",
  "anchorPreset": "stretch",
  "anchorMin": "0,0",
  "anchorMax": "1,1",
  "pivot": "0.5,0.5",
  "offsetMin": "0,0",
  "offsetMax": "0,0",
  "cssPosition": "absolute",
  "cssLeft": "0px",
  "cssRight": "auto",
  "cssTop": "0px",
  "cssBottom": "auto",
  "cssWidth": "1920px",
  "cssHeight": "1080px",
  "cssObjectFit": "contain",
  "cssBackgroundSize": "cover"
}
```

Unity consumes these fields:

- `imageSrc`, `backgroundImageSrc`
- `imageFit`, `cssObjectFit`, `cssBackgroundSize`
- `layoutHint`, `safeArea`, `anchorPreset`
- `anchorMin`, `anchorMax`, `pivot`, `offsetMin`, `offsetMax`

The remaining CSS fields are retained as diagnostics and future extension points.

## Layout Hint Values

| Value | RectTransform behavior |
| --- | --- |
| `stretch` / `fill` / `full-screen` | anchors `(0,0)` to `(1,1)` |
| `top-bar` / `stretch-x-top` | horizontal stretch, pinned top |
| `bottom-bar` / `stretch-x-bottom` | horizontal stretch, pinned bottom |
| `left-panel` / `stretch-y-left` | vertical stretch, pinned left |
| `right-panel` / `stretch-y-right` | vertical stretch, pinned right |
| `center` / `dialog` | centered fixed-size rect |
| `fixed` | v1 fixed top-left placement |

## Complete v2 Example

```json
{
  "schemaVersion": 2,
  "designWidth": 1920,
  "designHeight": 1080,
  "sourceDirectory": "prototypes",
  "name": "m_mainWindow",
  "type": "div",
  "dir": "v",
  "value": 0.5,
  "isChecked": false,
  "options": [],
  "x": 0,
  "y": 0,
  "width": 1920,
  "height": 1080,
  "color": "#FFFFFF00",
  "fontColor": "#000000",
  "fontSize": 14,
  "textAlign": "center",
  "text": "",
  "layoutHint": "stretch",
  "children": [
    {
      "schemaVersion": 2,
      "name": "m_imgAvatar",
      "type": "image",
      "dir": "v",
      "value": 0.5,
      "isChecked": false,
      "options": [],
      "x": 40,
      "y": 120,
      "width": 96,
      "height": 96,
      "color": "#FFFFFF00",
      "fontColor": "#000000",
      "fontSize": 14,
      "textAlign": "center",
      "text": "",
      "imageSrc": "images/avatar.png",
      "imageFit": "contain",
      "layoutHint": "fixed",
      "children": []
    }
  ]
}
```
