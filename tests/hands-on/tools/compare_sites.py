#!/usr/bin/env python3
"""compare_sites.py <portA> <portB> - crawl both test sites (home, blog, contact, every post in en + da) and
diff each page after normalising the host, antiforgery/ufprt tokens and image cache-buster params.
Blank-line differences are ignored. Prints the differing pages and "<n> identical, <m> different".
Exit code 1 if any page differs.

Usage (from tests/hands-on): python3 tools/compare_sites.py 44800 44801
"""
import difflib
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import harness  # noqa: E402

PAGES = ["/", "/blog/", "/contact/", "/da/", "/da/blog/", "/da/kontakt/"]


def normalise(text, ports):
    """Replace everything that legitimately differs between two sites (host, tokens, cache-busters)."""
    for port in ports:
        text = text.replace(f"localhost:{port}", "HOST")
    text = re.sub(r'(__RequestVerificationToken" type="hidden" value=")[^"]+', r"\1X", text)
    text = re.sub(r'(name="ufprt" type="hidden" value=")[^"]+', r"\1X", text)
    text = re.sub(r'value="[^"]{40,}"', 'value="X"', text)
    return re.sub(r"([?&](amp;)?(v|hmac)=)[a-z0-9]+", r"\1X", text)


def fetch(base, path, ports):
    """A page's normalised lines, with its HTTP status as the last line (so a 404 vs 200 counts as a difference)."""
    try:
        status, body = harness.get_text(base + path)
    except OSError as e:
        status, body = "unreachable", str(e)
    return (normalise(body, ports) + f"\n{status}").split("\n")


def main():
    harness.utf8_stdio()
    if len(sys.argv) != 3:
        harness.die("usage: python3 tools/compare_sites.py <portA> <portB>", 2)
    ports = sys.argv[1:3]
    a, b = (f"https://localhost:{p}" for p in ports)

    # Every post linked from the blog listing, in both languages (taken from site A).
    pages = list(PAGES)
    for listing in ("/blog/", "/da/blog/"):
        _, html = harness.get_text(a + listing)
        pages += sorted(set(re.findall(rf'href="({re.escape(listing)}[^"]+)"', html)))

    same = different = 0
    for page in pages:
        left, right = fetch(a, page, ports), fetch(b, page, ports)
        if [line for line in left if line.strip()] == [line for line in right if line.strip()]:
            same += 1
            continue
        different += 1
        print(f"DIFF {page}")
        print("\n".join(list(difflib.unified_diff(left, right, lineterm="", n=0))[2:8]))
    print(f"{same} identical, {different} different")
    sys.exit(1 if different else 0)


if __name__ == "__main__":
    main()
