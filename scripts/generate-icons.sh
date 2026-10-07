#!/bin/bash
# Generate Icons.axaml with Heroicons Solid path data (MIT)
# Manual fallback paths for icons Heroicons doesn't have
set -e

cd "$(dirname "$0")/.."
OUTPUT="OpenUtau/Assets/Icons.axaml"

# ── Heroicons mapping (all via jsDelivr) ──
declare -A ICON_MAP
ICON_MAP=(
  [icon-file-plus]="document-plus"
  [icon-folder-open]="folder-open"
  [icon-save]="document-arrow-down"
  [icon-plus]="plus"
  [icon-x]="x-mark"
  [icon-search]="magnifying-glass"
  [icon-pencil]="pencil-square"
  [icon-trash]="trash"
  [icon-music]="musical-note"
  [icon-mic]="microphone"
  [icon-volume-x]="speaker-x-mark"
  [icon-headphones]="speaker-wave"
  [icon-sliders]="adjustments-horizontal"
  [icon-sparkles]="sparkles"
  [icon-globe]="globe-alt"
  [icon-external-link]="arrow-top-right-on-square"
  [icon-book-open]="book-open"
  [icon-package]="cube"
  [icon-play]="play"
  [icon-pause]="pause"
  [icon-layout]="squares-2x2"
  [icon-info]="information-circle"
  [icon-alert]="exclamation-triangle"
  [icon-chevron-down]="chevron-down"
  [icon-chevron-right]="chevron-right"
  [icon-check]="check"
  [icon-copy]="document-duplicate"
  [icon-minus]="minus"
  [icon-square]="MANUAL_SQUARE"
  [icon-github]="MANUAL_CODE"
  [icon-monitor]="computer-desktop"
  [icon-user]="user"
  [icon-palette]="swatch"
  [icon-cpu]="cpu-chip"
  [icon-wrench]="wrench"
  [icon-equalizer]="chart-bar"
  [icon-undo]="arrow-uturn-left"
  [icon-redo]="arrow-uturn-right"
  [icon-zoom-in]="magnifying-glass-plus"
  [icon-zoom-out]="magnifying-glass-minus"
  [icon-chevron-up]="chevron-up"
  [icon-chevron-left]="chevron-left"
  [icon-home]="home"
  [icon-refresh-cw]="arrow-path"
  [icon-stop]="stop"
  [icon-skip-forward]="forward"
  [icon-skip-back]="backward"
  [icon-folder]="folder"
  [icon-file]="document"
  [icon-download]="arrow-down-tray"
  [icon-clock]="clock"
  [icon-lock]="lock-closed"
  [icon-settings]="cog-6-tooth"
  [icon-list]="bars-3"
)

TEMP_DIR=$(mktemp -d)
trap "rm -rf $TEMP_DIR" EXIT

fetch_heroicons() {
  curl -sf --max-time 10 "https://cdn.jsdelivr.net/npm/heroicons@2.1.1/24/solid/${1}.svg"
}

extract_path() {
  python3 -c "
import sys, re
svg = sys.stdin.read()
# Also extract from outline if solid not available
matches = re.findall(r'<path[^>]*\sd=\"([^\"]+)\"', svg)
if matches:
    print(' '.join(matches))
" 2>/dev/null
}

# ── Manual fallback paths (24x24 viewBox) ──
# Simple outlined square (maximize window)
MANUAL_SQUARE="M6 3h12a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3Zm1.5 3a1.5 1.5 0 0 0-1.5 1.5v9a1.5 1.5 0 0 0 1.5 1.5h12a1.5 1.5 0 0 0 1.5-1.5v-9a1.5 1.5 0 0 0-1.5-1.5h-12Z"
# Code bracket (generic code icon)
MANUAL_CODE="M14.447 3.026a.75.75 0 0 1 .527.921l-4.5 16.5a.75.75 0 0 1-1.448-.394l4.5-16.5a.75.75 0 0 1 .921-.527ZM16.72 6.22a.75.75 0 0 1 1.06 0l5.25 5.25a.75.75 0 0 1 0 1.06l-5.25 5.25a.75.75 0 1 1-1.06-1.06L21.44 12l-4.72-4.72a.75.75 0 0 1 0-1.06Zm-9.44 0a.75.75 0 0 1 0 1.06L2.56 12l4.72 4.72a.75.75 0 0 1-1.06 1.06L.97 12.53a.75.75 0 0 1 0-1.06l5.25-5.25a.75.75 0 0 1 1.06 0Z"

# ── Generate file ──
cat > "$OUTPUT" << 'HEADER'
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- ============================================================
       Icons — Heroicons Solid + Outline (MIT License)
       https://heroicons.com — 24×24 viewBox, filled & outlined
       Generated: $(date +%Y-%m-%d)
       ============================================================ -->

HEADER

for key in "${!ICON_MAP[@]}"; do
  name="${ICON_MAP[$key]}"

  if [ "$name" = "MANUAL_SQUARE" ]; then
    path_d="$MANUAL_SQUARE"
    echo "  <!-- $key: manual (outlined square for window maximize) -->" >> "$OUTPUT"
  elif [ "$name" = "MANUAL_CODE" ]; then
    path_d="$MANUAL_CODE"
    echo "  <!-- $key: manual (code bracket generic) -->" >> "$OUTPUT"
  else
    echo "  Fetching: $key → heroicons/$name" >&2
    svg=$(fetch_heroicons "$name")
    if [ -z "$svg" ]; then
      echo "    WARNING: Not found, skipping $key" >&2
      continue
    fi
    path_d=$(echo "$svg" | extract_path)
    if [ -z "$path_d" ]; then
      echo "    WARNING: No path data, skipping $key" >&2
      continue
    fi
    echo "    OK" >&2
    echo "  <!-- heroicons: $name -->" >> "$OUTPUT"
  fi

  echo "  <StreamGeometry x:Key=\"$key\">$path_d</StreamGeometry>" >> "$OUTPUT"
done

echo '</ResourceDictionary>' >> "$OUTPUT"
echo "Done! Generated $OUTPUT ($(wc -l < "$OUTPUT") lines)" >&2
