# © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
from __future__ import annotations

import json
import sys
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[1]
SITE = ROOT / "site"


class SiteParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.ids: set[str] = set()
        self.fragments: list[str] = []
        self.assets: list[str] = []
        self.locales: dict[str, tuple[str | None, str | None]] = {}
        self.errors: list[str] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        element_id = values.get("id")
        if element_id:
            if element_id in self.ids:
                self.errors.append(f"duplicate id: {element_id}")
            self.ids.add(element_id)

        href = values.get("href")
        if href:
            if href.startswith("#"):
                self.fragments.append(href[1:])
            elif tag == "link":
                self.assets.append(href)

        src = values.get("src")
        if src and tag == "script":
            self.assets.append(src)

        locale = values.get("data-locale-content")
        if locale:
            self.locales[locale] = (values.get("lang"), values.get("dir"))

        if values.get("target") == "_blank":
            rel = set((values.get("rel") or "").lower().split())
            if not {"noopener", "noreferrer"}.issubset(rel):
                self.errors.append(f"target=_blank missing rel=noopener noreferrer: {href or src or tag}")

        if href:
            parsed = urlparse(href)
            if parsed.scheme and parsed.scheme not in {"https", "mailto"}:
                self.errors.append(f"disallowed URL scheme {parsed.scheme!r}: {href}")


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def main() -> None:
    pages = sorted(SITE.glob("*.html"))
    if not pages:
        fail("site has no HTML pages")

    for page in pages:
        parser = SiteParser()
        try:
            parser.feed(page.read_text(encoding="utf-8"))
            parser.close()
        except Exception as error:  # HTMLParser errors should be surfaced in CI.
            fail(f"could not parse {page.relative_to(ROOT)}: {error}")

        for error in parser.errors:
            fail(f"{page.relative_to(ROOT)}: {error}")

        missing_fragments = sorted(set(parser.fragments) - parser.ids)
        if missing_fragments:
            fail(f"{page.relative_to(ROOT)}: missing in-page targets: {', '.join(missing_fragments)}")

        if not {"ar", "en"}.issubset(parser.locales):
            fail(f"{page.relative_to(ROOT)}: both Arabic and English content panels are required")
        if parser.locales["ar"] != ("ar", "rtl") or parser.locales["en"] != ("en", "ltr"):
            fail(f"{page.relative_to(ROOT)}: locale lang/dir attributes are incorrect")

        for asset in parser.assets:
            parsed = urlparse(asset)
            if parsed.scheme or asset.startswith("//"):
                continue
            asset_path = (page.parent / parsed.path).resolve()
            if not asset_path.is_file():
                fail(f"{page.relative_to(ROOT)}: missing local asset {asset}")

    config_path = ROOT / "config" / "shared-links.json"
    config = json.loads(config_path.read_text(encoding="utf-8"))
    if "updates" in config:
        fail("obsolete updates link must not be reintroduced; use the Releases page only when a release exists")
    for key, value in config.items():
        if key == "wallets" or not value:
            continue
        parsed = urlparse(value)
        if parsed.scheme != "https" or not parsed.netloc or parsed.username or parsed.password:
            fail(f"shared link {key!r} must be a safe HTTPS URL")

    print(f"Validated {len(pages)} documentation page(s), locale panels, links, assets, and shared-link configuration.")


if __name__ == "__main__":
    main()
