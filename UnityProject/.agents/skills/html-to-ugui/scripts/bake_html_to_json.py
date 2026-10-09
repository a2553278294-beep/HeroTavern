#!/usr/bin/env python3
"""Bake UI-DSL HTML into Unity HtmlToUGUI JSON.

The output keeps the original v1 fields consumed by HtmlToUGUIBaker and adds
schemaVersion=2 metadata for image binding and adaptive Prefab generation.
"""

import argparse
import html
import json
import os
import sys
import tempfile
from pathlib import Path
from html.parser import HTMLParser


class SafeMarkup(HTMLParser):
    def handle_starttag(self, tag, attrs):
        if tag.lower() in {"script", "iframe", "object", "embed", "base", "link", "meta"}:
            raise ValueError(f"Unsupported active/external markup: {tag}")
        for key, value in attrs:
            if key.lower().startswith("on") or (value or "").lower().strip().startswith("javascript:"):
                raise ValueError("Executable HTML attributes are not supported.")


def bake_html_to_json(
    html_content: str,
    width: int = 1920,
    height: int = 1080,
    source_path: str = "",
    screenshot_path: str = "",
) -> dict:
    if not all(isinstance(v, int) and 0 < v <= 8192 for v in (width, height)):
        raise ValueError("Design dimensions must be integers in 1..8192.")
    SafeMarkup().feed(html_content)
    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print(
            "Error: playwright is required. Run: pip install playwright && playwright install chromium",
            file=sys.stderr,
        )
        sys.exit(1)

    injected_html = json.dumps(html_content).replace("</", "<\\/")
    base_url = Path(source_path).resolve().parent.as_uri() + "/" if source_path else ""
    full_html = f"""<!DOCTYPE html>
<html><head><meta charset="UTF-8">
<base href="{html.escape(base_url, quote=True)}">
<style>
body {{ margin: 0; padding: 0; }}
#canvas-sandbox {{ position: relative; width: {width}px; height: {height}px; }}
#canvas-sandbox * {{ box-sizing: border-box !important; }}
#canvas-sandbox [data-u-type] {{ min-width: 0; min-height: 0; }}
</style>
</head><body>
<div id="canvas-sandbox"></div>
<script>
const sandbox = document.getElementById('canvas-sandbox');
sandbox.innerHTML = {injected_html};
for (const element of sandbox.querySelectorAll('[data-u-src]')) {{
    const source = element.getAttribute('data-u-src');
    if (element.tagName.toLowerCase() === 'img') element.setAttribute('src', source);
    else element.style.backgroundImage = 'url(' + JSON.stringify(source) + ')';
}}
for (const element of sandbox.querySelectorAll('[data-u-type="slider"]')) {{
    if (element.tagName.toLowerCase() === 'input') {{
        element.min = '0';
        element.max = '1';
        element.step = 'any';
        element.value = element.getAttribute('data-u-value') ?? '0.5';
    }}
}}
for (const element of sandbox.querySelectorAll('[data-u-type="toggle"]')) {{
    if (element.tagName.toLowerCase() === 'input') element.checked = element.getAttribute('data-u-checked') === 'true';
}}
let groupId = 0;

function rgb2hex(rgb) {{
    if (!rgb || rgb === 'rgba(0, 0, 0, 0)' || rgb === 'transparent') return '#FFFFFF00';
    const match = rgb.match(/^rgba?\\((\\d+),\\s*(\\d+),\\s*(\\d+)(?:,\\s*([\\d.]+))?\\)$/);
    if (!match) return '#FFFFFF';
    const r = ('0' + parseInt(match[1], 10).toString(16)).slice(-2);
    const g = ('0' + parseInt(match[2], 10).toString(16)).slice(-2);
    const b = ('0' + parseInt(match[3], 10).toString(16)).slice(-2);
    const a = match[4] ? ('0' + Math.round(parseFloat(match[4]) * 255).toString(16)).slice(-2) : 'ff';
    return `#${{r}}${{g}}${{b}}${{a === 'ff' ? '' : a}}`;
}}

function firstCssUrl(value) {{
    if (!value || value === 'none') return '';
    const match = value.match(/url\\((['"]?)(.*?)\\1\\)/);
    return match ? match[2] : '';
}}

function readAttr(element, names) {{
    for (const name of names) {{
        const value = element.getAttribute(name);
        if (value !== null && value !== '') return value;
    }}
    return '';
}}

function inferLayoutHint(element, rect, parentRect, style) {{
    const explicit = readAttr(element, ['data-u-layout', 'data-u-anchor']);
    if (explicit) return explicit;

    const left = rect.left - parentRect.left;
    const top = rect.top - parentRect.top;
    const right = parentRect.right - rect.right;
    const bottom = parentRect.bottom - rect.bottom;
    const tol = Math.max(2, Math.min(parentRect.width, parentRect.height) * 0.01);

    if (left <= tol && top <= tol && right <= tol && bottom <= tol) return 'stretch';
    if (top <= tol && left <= tol && right <= tol) return 'top-bar';
    if (bottom <= tol && left <= tol && right <= tol) return 'bottom-bar';
    if (left <= tol && top <= tol && bottom <= tol) return 'left-panel';
    if (right <= tol && top <= tol && bottom <= tol) return 'right-panel';

    const centerX = left + rect.width * 0.5;
    const centerY = top + rect.height * 0.5;
    if (Math.abs(centerX - parentRect.width * 0.5) <= tol * 2 &&
        Math.abs(centerY - parentRect.height * 0.5) <= tol * 2 &&
        rect.width < parentRect.width * 0.95 &&
        rect.height < parentRect.height * 0.95) {{
        return 'center';
    }}

    const inlineStyle = element.style;
    const hasHorizontalEdges = !!inlineStyle.left && !!inlineStyle.right && inlineStyle.right !== 'auto';
    const hasVerticalEdges = !!inlineStyle.top && !!inlineStyle.bottom && inlineStyle.bottom !== 'auto';
    if (hasHorizontalEdges && hasVerticalEdges) return 'stretch';
    if (hasHorizontalEdges) return top <= parentRect.height * 0.5 ? 'stretch-x-top' : 'stretch-x-bottom';
    if (hasVerticalEdges) return left <= parentRect.width * 0.5 ? 'stretch-y-left' : 'stretch-y-right';

    return 'fixed';
}}

function traverseAndBake(element, rootRect, parentRect) {{
    const uType = element.getAttribute('data-u-type');
    const uName = element.getAttribute('data-u-name');
    let nodeData = null;

    if (uType && uName) {{
        const rect = element.getBoundingClientRect();
        const style = window.getComputedStyle(element);
        const relativeX = rect.left - rootRect.left;
        const relativeY = rect.top - rootRect.top;
        const realWidth = rect.width;
        const realHeight = rect.height;

        let textContent = element.innerText || '';
        if (element.tagName.toLowerCase() === 'input') {{
            textContent = element.value || element.placeholder || '';
        }}

        let fontSize = 14;
        if (style.fontSize) fontSize = parseFloat(style.fontSize);

        const backgroundImageSrc = firstCssUrl(style.backgroundImage);
        const imageSrc = readAttr(element, ['data-u-src', 'src']);
        const imageFit = readAttr(element, ['data-u-fit']) || style.objectFit || style.backgroundSize || '';
        const layoutHint = inferLayoutHint(element, rect, parentRect || rootRect, style);
        const safeArea = readAttr(element, ['data-u-safe-area']);
        const uDir = element.getAttribute('data-u-dir') || 'v';
        const rawValue = element.getAttribute('data-u-value');
        const parsedValue = rawValue === null ? 0.5 : Number(rawValue);
        if (!Number.isFinite(parsedValue)) throw new Error('Invalid control value: ' + uName);
        const uValue = parsedValue;
        const uChecked = element.getAttribute('data-u-checked') === 'true';
        const uOptions = [];

        if (uType === 'dropdown' && element.tagName.toLowerCase() === 'select') {{
            const opts = element.querySelectorAll('option');
            opts.forEach(opt => uOptions.push(opt.innerText.trim()));
        }}

        nodeData = {{
            schemaVersion: 2,
            name: uName,
            type: uType,
            dir: uDir,
            value: uValue,
            isChecked: uChecked,
            options: uOptions,
            x: Math.round(relativeX),
            y: Math.round(relativeY),
            width: Math.round(realWidth),
            height: Math.round(realHeight),
            color: rgb2hex(style.backgroundColor),
            fontColor: rgb2hex(style.color),
            fontSize: Math.round(fontSize),
            textAlign: style.textAlign || 'center',
            text: textContent.trim(),
            imageSrc: imageSrc,
            backgroundImageSrc: backgroundImageSrc,
            imageFit: imageFit,
            layoutHint: layoutHint,
            safeArea: safeArea,
            anchorPreset: readAttr(element, ['data-u-anchor']),
            anchorMin: readAttr(element, ['data-u-anchor-min']),
            anchorMax: readAttr(element, ['data-u-anchor-max']),
            pivot: readAttr(element, ['data-u-pivot']),
            offsetMin: readAttr(element, ['data-u-offset-min']),
            offsetMax: readAttr(element, ['data-u-offset-max']),
            cssPosition: style.position,
            cssLeft: style.left,
            cssRight: style.right,
            cssTop: style.top,
            cssBottom: style.bottom,
            cssWidth: style.width,
            cssHeight: style.height,
            cssObjectFit: style.objectFit,
            cssBackgroundSize: style.backgroundSize,
            children: []
        }};
    }}

    const childrenData = [];
    const nextParentRect = nodeData ? element.getBoundingClientRect() : parentRect;
    for (let i = 0; i < element.children.length; i++) {{
        if (element.tagName.toLowerCase() === 'select' && element.children[i].tagName.toLowerCase() === 'option') {{
            continue;
        }}
        const childResult = traverseAndBake(element.children[i], rootRect, nextParentRect);
        if (childResult) childrenData.push(childResult);
    }}

    if (nodeData) {{
        nodeData.children = childrenData;
        return nodeData;
    }}

    if (childrenData.length > 0) {{
        const rect = element.getBoundingClientRect();
        return childrenData.length === 1 ? childrenData[0] : {{
            schemaVersion: 2,
            name: 'layoutGroup_' + (++groupId),
            type: 'div',
            dir: 'v',
            value: 0,
            isChecked: false,
            options: [],
            x: Math.round(rect.left - rootRect.left),
            y: Math.round(rect.top - rootRect.top),
            width: Math.round(rect.width),
            height: Math.round(rect.height),
            color: '#FFFFFF00',
            fontColor: '#000000',
            fontSize: 14,
            textAlign: 'center',
            text: '',
            layoutHint: 'fixed',
            children: childrenData
        }};
    }}

    return null;
}}

async function bake() {{
// Force layout before waiting so CSS font faces have begun loading.
sandbox.getBoundingClientRect();
await document.fonts.ready;
if ([...document.fonts].some(font => font.status === 'error'))
    throw new Error('A required font failed to load.');
const nodes = [...sandbox.querySelectorAll('*')];
const imageSources = new Set();
for (const element of nodes) {{
    const cssUrl = firstCssUrl(getComputedStyle(element).backgroundImage);
    const src = element.tagName.toLowerCase() === 'img' ? element.getAttribute('src') : '';
    for (const value of [src, cssUrl]) {{
        if (!value) continue;
        const url = new URL(value, document.baseURI);
        if (url.protocol !== 'file:') throw new Error('Only local image sources are supported: ' + value);
        imageSources.add(url.href);
    }}
}}
await Promise.all([...imageSources].map(src => new Promise((resolve, reject) => {{
    const image = new Image();
    image.onload = () => image.naturalWidth > 0 ? resolve() : reject(new Error('Empty image: ' + src));
    image.onerror = () => reject(new Error('Missing image: ' + src));
    image.src = src;
}})));
const roots = [...sandbox.querySelectorAll('[data-u-name]')]
    .filter(element => !element.parentElement.closest('[data-u-name]'));
if (roots.length !== 1) throw new Error('UI-DSL requires exactly one named root.');
const rootElement = roots[0];
if (!rootElement) {{
    throw new Error('No root node with data-u-name was found.');
}}
const rootRect = rootElement.getBoundingClientRect();
const result = traverseAndBake(rootElement, rootRect, sandbox.getBoundingClientRect());
result.schemaVersion = 2;
result.designWidth = {width};
result.designHeight = {height};
window.__BAKE_RESULT__ = result;
}}
bake().catch(error => {{ window.__BAKE_ERROR__ = String(error); }});
</script>
</body></html>"""

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page(viewport={"width": width, "height": height})
            page.route("http://**", lambda route: route.abort())
            page.route("https://**", lambda route: route.abort())
            # A real file origin permits local images/fonts without disabling browser security.
            with tempfile.TemporaryDirectory(prefix="ugui-bake-") as directory:
                page_file = Path(directory) / "bake.html"
                page_file.write_text(full_html, encoding="utf-8")
                page.goto(page_file.as_uri(), wait_until="load")
                page.wait_for_function("window.__BAKE_RESULT__ || window.__BAKE_ERROR__", timeout=15000)
                error = page.evaluate("window.__BAKE_ERROR__ || null")
                if error:
                    raise ValueError(error)
                result = page.evaluate("window.__BAKE_RESULT__")
                if screenshot_path:
                    page.locator("#canvas-sandbox").screenshot(path=screenshot_path)
        finally:
            browser.close()

    if result is None:
        raise ValueError("Bake failed: no valid UI-DSL node was found.")
    if result["width"] <= 0 or result["height"] <= 0:
        raise ValueError("Root layout must have nonzero dimensions.")

    if source_path:
        full_source = os.path.abspath(source_path)
        result["sourcePath"] = full_source
        result["htmlFilePath"] = full_source
        result["sourceDirectory"] = os.path.dirname(full_source)

    return result


def main():
    parser = argparse.ArgumentParser(description="Bake UI-DSL HTML into HtmlToUGUI JSON")
    parser.add_argument("input", help="Input HTML file path")
    parser.add_argument("-o", "--output", help="Output JSON path. Defaults to same name with .json")
    parser.add_argument("-w", "--width", type=int, default=1920, help="Design canvas width")
    parser.add_argument("-H", "--height", type=int, default=1080, help="Design canvas height")
    parser.add_argument("--stdout", action="store_true", help="Print JSON instead of writing a file")
    parser.add_argument("--screenshot", help="Optional browser evidence PNG")
    args = parser.parse_args()

    if not os.path.exists(args.input):
        print(f"Error: file does not exist: {args.input}", file=sys.stderr)
        sys.exit(1)

    with open(args.input, "r", encoding="utf-8") as f:
        html_content = f.read()

    result = bake_html_to_json(html_content, args.width, args.height, args.input, args.screenshot or "")
    json_str = json.dumps(result, ensure_ascii=False, indent=2)

    if args.stdout:
        print(json_str)
    else:
        output_path = args.output or os.path.splitext(args.input)[0] + ".json"
        with open(output_path, "w", encoding="utf-8") as f:
            f.write(json_str)
        print(f"Bake complete: {output_path}")


if __name__ == "__main__":
    main()
