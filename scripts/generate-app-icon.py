"""Generate the original notepad-and-lock icon (requires Pillow).

Run from any directory: python scripts/generate-app-icon.py
The ICO contains 16, 24, 32, 48, 64, 128 and 256 pixel images.
"""

from pathlib import Path

from PIL import Image, ImageDraw


def draw_icon(size):
    scale = 4
    image = Image.new("RGBA", (size * scale, size * scale))
    draw = ImageDraw.Draw(image)
    unit = size * scale / 256

    def box(coords):
        return tuple(round(value * unit) for value in coords)

    def rounded(coords, radius, fill):
        draw.rounded_rectangle(box(coords), radius=round(radius * unit), fill=fill)

    navy = "#17354A"
    paper = "#F5F2E7"
    teal = "#35B6AB"
    gold = "#F6C65B"

    # A sturdy notebook silhouette with a contrasting binding and paper inset.
    rounded((22, 12, 216, 244), 27, navy)
    rounded((54, 28, 200, 228), 15, paper)
    rounded((32, 30, 43, 225), 5, teal)
    for y in (66, 95, 124):
        rounded((76, y, 174 if y != 124 else 144, y + 9), 4, "#81969F")

    # The dark outline keeps the lock legible against both paper and the desktop.
    draw.arc(box((157, 125, 221, 197)), 180, 360, fill=navy, width=round(24 * unit))
    draw.arc(box((157, 125, 221, 197)), 180, 360, fill=gold, width=round(12 * unit))
    rounded((143, 157, 237, 237), 18, navy)
    rounded((151, 165, 229, 229), 11, gold)
    draw.ellipse(box((182, 182, 198, 198)), fill=navy)
    rounded((186, 192, 194, 211), 3, navy)
    return image.resize((size, size), Image.Resampling.LANCZOS)


if __name__ == "__main__":
    assets = Path(__file__).resolve().parents[1] / "src" / "PortableSecureNotepad" / "Assets"
    assets.mkdir(parents=True, exist_ok=True)
    sizes = (16, 24, 32, 48, 64, 128, 256)
    frames = [draw_icon(size) for size in sizes]
    frames[-1].save(
        assets / "App.ico", format="ICO", sizes=[(size, size) for size in sizes], append_images=frames[:-1]
    )
    print(f"Generated {assets / 'App.ico'}")
