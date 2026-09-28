#!/usr/bin/env python3
"""Submit the contact and sign-up forms like a browser: cookies + antiforgery token + ufprt.

Usage: UMB_SITE=sites/<name> python3 tools/submit_forms.py   (host from $UMB_SITE/credentials.json; pages /contact/ and /da/kontakt/)
"""
import http.cookiejar
import json
import os
from pathlib import Path
import re
import ssl
import sys
import urllib.parse
import urllib.request

sys.path.insert(0, str(Path(__file__).resolve().parent))
import harness  # noqa: E402

HOST = harness.Site(os.environ["UMB_SITE"]).host
CTX = ssl._create_unverified_context()
RUN = os.environ.get("RUN_ID") or __import__("time").strftime("%H%M%S")  # makes member emails unique per run


class Browser:
    def __init__(self):
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),
            urllib.request.HTTPSHandler(context=CTX),
        )

    def get(self, path):
        with self.opener.open(HOST + path) as r:
            return r.read().decode("utf-8")

    def post_form(self, path, form_id, fields):
        html = self.get(path)
        form = re.search(rf'<form[^>]*id="{form_id}".*?</form>', html, re.S)
        if not form:
            raise SystemExit(f"form #{form_id} not found on {path}")
        hidden = dict(re.findall(r'<input name="([^"]+)" type="hidden" value="([^"]*)"', form.group(0)))
        hidden.update(re.findall(r'<input type="hidden" name="([^"]+)" value="([^"]*)"', form.group(0)))
        data = urllib.parse.urlencode({**hidden, **fields}).encode()
        with self.opener.open(HOST + path, data=data) as r:
            return r.geturl(), r.read().decode("utf-8")


def check(label, html, marker):
    ok = marker in html
    errors = re.findall(r'<li>([^<]+)</li>', html.split('id="contact"')[-1]) if not ok else []
    print(f"{'PASS' if ok else 'FAIL'} {label}" + (f"  errors={errors}" if errors else ""))
    return ok


def main():
    harness.utf8_stdio()
    results = []
    for path, lang, name, company in [("/contact/", "en", "Jane Tester", "Acme Ltd"),
                                      ("/da/kontakt/", "da", "Søren Prøve", "Dansk ApS")]:
        b = Browser()
        _, html = b.post_form(path, "contact-form", {
            "Name": name, "Email": f"{lang}.contact.{RUN}@example.com", "Subject": f"Hello from {lang}",
            "Message": f"This is a test message sent from the {lang} contact page."})
        results.append(check(f"{lang} contact form", html, 'id="contact-success"'))

        _, html = b.post_form(path, "signup-form", {
            "Name": name, "Email": f"{lang}.member.{RUN}@example.com", "Password": "Member-Password-123",
            "Company": company, "MarketingOptIn": "true" if lang == "en" else "false"})
        results.append(check(f"{lang} sign-up form", html, 'id="signup-success"'))
        results.append(check(f"{lang} signed in after sign-up", html, 'id="signed-in"'))

        # Signing up again with the same email must be rejected.
        b2 = Browser()
        _, html = b2.post_form(path, "signup-form", {
            "Name": name, "Email": f"{lang}.member.{RUN}@example.com", "Password": "Member-Password-123"})
        results.append(check(f"{lang} duplicate sign-up rejected", html, "already exists"))
    print(f"{sum(results)}/{len(results)} checks passed")
    sys.exit(0 if all(results) else 1)


if __name__ == "__main__":
    main()
