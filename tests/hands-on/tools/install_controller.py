#!/usr/bin/env python3
"""install_controller.py <site-dir> - add the contact/sign-up form controller (site code, not CLI) to a
test site, rebuild it and restart it. Needed before tools/submit_forms.py can pass.

Usage (from tests/hands-on): python3 tools/install_controller.py sites/<name>
"""
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import harness  # noqa: E402


def main():
    harness.utf8_stdio()
    if len(sys.argv) != 2:
        harness.die("usage: python3 tools/install_controller.py sites/<name>", 2)
    site = harness.Site(sys.argv[1])
    controllers = site.project / "Controllers"
    controllers.mkdir(exist_ok=True)
    shutil.copy2(harness.ROOT / "assets" / "ContactSurfaceController.cs", controllers)

    site.stop()
    log = site.dir / "logs" / "build.log"
    with open(log, "wb") as f:
        res = harness.run(["dotnet", "build", site.project, "-nologo", "-v", "q"], stdout=f, stderr=f)
    if res.returncode:
        print("\n".join(log.read_text(encoding="utf-8", errors="replace").splitlines()[-30:]))
        harness.die(f"Build failed (full log: {log})")
    site.start()


if __name__ == "__main__":
    main()
