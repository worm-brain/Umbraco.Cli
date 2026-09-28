#!/usr/bin/env bash
# compare_sites.sh <portA> <portB> - crawl both test sites (home, blog, contact, every post in en + da) and
# diff each page after normalising the host, antiforgery/ufprt tokens and image cache-buster params.
# Blank-line differences are ignored. Prints the differing pages and "<n> identical, <m> different".
# Exit code 1 if any page differs.
A=https://localhost:$1; B=https://localhost:$2
norm() { sed -E "s#localhost:$1#HOST#g; s#localhost:$2#HOST#g; s/(__RequestVerificationToken\" type=\"hidden\" value=\")[^\"]+/\1X/g; s/(name=\"ufprt\" type=\"hidden\" value=\")[^\"]+/\1X/g; s/value=\"[^\"]{40,}\"/value=\"X\"/g; s/([?&](amp;)?(v|hmac)=)[a-z0-9]+/\1X/g"; }
pages="/ /blog/ /contact/ /da/ /da/blog/ /da/kontakt/"
for base in /blog/ /da/blog/; do pages="$pages $(curl -sk $A$base | grep -oE "href=\"${base}[^\"]+\"" | cut -d'"' -f2 | sort -u | tr '\n' ' ')"; done
same=0; diffn=0
for p in $pages; do
  a=$(curl -sk -w '\n%{http_code}' $A$p | norm $1 $2); b=$(curl -sk -w '\n%{http_code}' $B$p | norm $1 $2)
  if [[ "$(grep -v "^\s*$" <<<"$a")" == "$(grep -v "^\s*$" <<<"$b")" ]]; then same=$((same+1)); else diffn=$((diffn+1)); echo "DIFF $p"; diff <(echo "$a") <(echo "$b") | head -6; fi
done
echo "$same identical, $diffn different"
[[ $diffn -eq 0 ]]
