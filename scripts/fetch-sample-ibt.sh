#!/usr/bin/env bash
# Downloads public, Apache-2.0-licensed sample iRacing recordings into samples/ibt/ (git-ignored).
# Each file is pinned to a source commit and verified against its SHA-256 before it is kept.
#
#   scripts/fetch-sample-ibt.sh            # default set, about 46 MB (5 cars, 4 tracks)
#   scripts/fetch-sample-ibt.sh --large    # also the large set, about 300 MB more
#   scripts/fetch-sample-ibt.sh --dir DIR  # somewhere other than samples/ibt/
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
manifest="$root/scripts/sample-ibt-manifest.tsv"
dest="$root/samples/ibt"
sets="default"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --large) sets="default large"; shift ;;
    --dir) dest="$2"; shift 2 ;;
    -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

if command -v sha256sum >/dev/null 2>&1; then
  sha() { sha256sum "$1" | cut -d' ' -f1; }
else
  sha() { shasum -a 256 "$1" | cut -d' ' -f1; }
fi

mkdir -p "$dest"
failed=0
while IFS=$'\t' read -r set file hash bytes license url; do
  [[ -z "$set" || "$set" == \#* ]] && continue
  [[ " $sets " == *" $set "* ]] || continue

  target="$dest/$file"
  if [[ -f "$target" && "$(sha "$target")" == "$hash" ]]; then
    echo "ok       $file"
    continue
  fi

  echo "fetching $file ($((bytes / 1048576)) MiB, $license)"
  tmp="$target.part"
  if curl -fsSL --retry 3 -o "$tmp" "$url" && [[ "$(sha "$tmp")" == "$hash" ]]; then
    mv "$tmp" "$target"
  else
    rm -f "$tmp"
    echo "FAILED   $file (download or checksum mismatch): $url" >&2
    failed=1
  fi
done < "$manifest"

echo "samples are in $dest (sources and licenses: scripts/sample-ibt-manifest.tsv)"
exit $failed
