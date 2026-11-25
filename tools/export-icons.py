# © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
from __future__ import annotations

from pathlib import Path
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw
from svgpathtools import parse_path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets" / "icons"
OUTPUT = SOURCE / "generated"
ICON_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
ICO_NAMES = {"app", "tray-online", "tray-blocked", "tray-warning"}
RENDER_SIZE = 1024


def color_value(value: str) -> tuple[int, int, int, int]:
    if not value.startswith("#") or len(value) != 7:
        raise ValueError(f"Unsupported SVG color: {value}")
    return tuple(bytes.fromhex(value[1:])) + (255,)


def point_to_pixel(point: complex, scale_x: float, scale_y: float) -> tuple[int, int]:
    return round(point.real * scale_x), round(point.imag * scale_y)


def draw_path(draw: ImageDraw.ImageDraw, element: ET.Element, scale_x: float, scale_y: float) -> None:
    stroke = element.attrib.get("stroke", "none")
    if stroke == "none":
        return

    color = color_value(stroke)
    width = max(1, round(float(element.attrib.get("stroke-width", "2")) * ((scale_x + scale_y) / 2)))
    line_cap = element.attrib.get("stroke-linecap", "butt")
    data = element.attrib.get("d")
    if not data:
        return

    for segment in parse_path(data):
        length = float(segment.length(error=0.01))
        steps = max(2, min(512, round(length * max(scale_x, scale_y) / 3)))
        points = [point_to_pixel(segment.point(index / steps), scale_x, scale_y) for index in range(steps + 1)]
        draw.line(points, fill=color, width=width, joint="curve")
        if line_cap == "round":
            radius = width / 2
            for x, y in (points[0], points[-1]):
                draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=color)


def draw_shape(draw: ImageDraw.ImageDraw, element: ET.Element, scale_x: float, scale_y: float) -> None:
    tag = element.tag.rsplit("}", 1)[-1]
    stroke = element.attrib.get("stroke", "none")
    if stroke == "none":
        return

    color = color_value(stroke)
    width = max(1, round(float(element.attrib.get("stroke-width", "2")) * ((scale_x + scale_y) / 2)))
    if tag == "circle":
        cx = float(element.attrib["cx"]) * scale_x
        cy = float(element.attrib["cy"]) * scale_y
        radius_x = float(element.attrib["r"]) * scale_x
        radius_y = float(element.attrib["r"]) * scale_y
        draw.ellipse((cx - radius_x, cy - radius_y, cx + radius_x, cy + radius_y), outline=color, width=width)
    elif tag == "rect":
        x = float(element.attrib.get("x", "0")) * scale_x
        y = float(element.attrib.get("y", "0")) * scale_y
        rectangle_width = float(element.attrib["width"]) * scale_x
        rectangle_height = float(element.attrib["height"]) * scale_y
        radius = float(element.attrib.get("rx", "0")) * ((scale_x + scale_y) / 2)
        bounds = (x, y, x + rectangle_width, y + rectangle_height)
        if radius > 0:
            draw.rounded_rectangle(bounds, radius=radius, outline=color, width=width)
        else:
            draw.rectangle(bounds, outline=color, width=width)


def render_icon(svg_path: Path) -> Image.Image:
    root = ET.parse(svg_path).getroot()
    view_box = [float(part) for part in root.attrib["viewBox"].split()]
    if len(view_box) != 4 or view_box[2] <= 0 or view_box[3] <= 0:
        raise ValueError(f"Invalid viewBox in {svg_path.name}")
    _, _, view_width, view_height = view_box
    scale_x = RENDER_SIZE / view_width
    scale_y = RENDER_SIZE / view_height
    canvas = Image.new("RGBA", (RENDER_SIZE, RENDER_SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)

    for element in root.iter():
        tag = element.tag.rsplit("}", 1)[-1]
        if tag == "path":
            draw_path(draw, element, scale_x, scale_y)
        elif tag in {"circle", "rect"}:
            draw_shape(draw, element, scale_x, scale_y)
        elif tag not in {"svg"}:
            raise ValueError(f"Unsupported SVG element <{tag}> in {svg_path.name}")

    return canvas.resize((256, 256), Image.Resampling.LANCZOS)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    svg_files = sorted(SOURCE.glob("*.svg"))
    if not svg_files:
        raise SystemExit("No SVG icons were found.")

    for svg_path in svg_files:
        image = render_icon(svg_path)
        image.save(OUTPUT / f"{svg_path.stem}.png", format="PNG", optimize=True)
        if svg_path.stem in ICO_NAMES:
            image.save(OUTPUT / f"{svg_path.stem}.ico", format="ICO", sizes=ICON_SIZES)
        image.close()

    print(f"Exported {len(svg_files)} PNG icons to {OUTPUT}")
    print("Exported application and tray ICO files at 16–256 px.")


if __name__ == "__main__":
    main()
