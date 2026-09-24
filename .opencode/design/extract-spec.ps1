param(
  [Parameter(Mandatory = $true)][string]$Path,
  [int]$MaxDepth = 99,
  [switch]$NoText
)

$text = Get-Content -LiteralPath $Path -Raw
$rx = [regex]'<!--.*?-->|<([a-zA-Z][\w:-]*)((?:"[^"]*"|[^>"])*?)(/?)>|</([a-zA-Z][\w:-]*)>'
$void = @('path', 'circle', 'rect', 'line', 'polygon', 'polyline', 'img', 'br', 'hr', 'input', 'use', 'stop', 'ellipse', 'meta', 'link')

$keepRe = '^(w-|h-|min-w|min-h|max-w|max-h|shrink|grow|basis-|flex|grid|gap-|p-|px-|py-|pt-|pb-|pl-|pr-|rounded|border|text-\[|text-(xs|sm|base|lg|xl|2xl)|font-|items-|justify-|self-|absolute|relative|inset-|top-|left-|right-|bottom-|overflow|col-|row-|z-|opacity|leading|tracking|line-clamp|whitespace|\[white-space|\[border-width)'
$dropRe = '^(bg-\[#|text-\[#|fill-\[#|stroke-\[#|\[border-color)'

$depth = 0
$lastEnd = 0
$count = 0
$maxSeen = 0
$out = New-Object System.Collections.Generic.List[string]

foreach ($m in $rx.Matches($text)) {
  # text between previous match and this one
  if (-not $NoText) {
    $between = $text.Substring($lastEnd, $m.Index - $lastEnd)
    $bt = $between.Trim()
    if ($bt -ne '' -and $bt.Length -lt 120 -and $depth -le $MaxDepth) {
      $out.Add(('  ' * $depth) + '"' + ($bt -replace '\s+', ' ') + '"')
    }
  }
  $lastEnd = $m.Index + $m.Length

  if ($m.Value.StartsWith('<!--')) { continue }

  if ($m.Groups[1].Success) {
    $tag = $m.Groups[1].Value
    $attrs = $m.Groups[2].Value
    $selfClose = $m.Groups[3].Value -eq '/'
    $name = $null; $cls = ''; $icon = $null
    if ($attrs -match 'data-pencil-name="([^"]*)"') { $name = $Matches[1] }
    if ($attrs -match 'data-icon-name="([^"]*)"') { $icon = $Matches[1] }
    if ($attrs -match 'class="([^"]*)"') { $cls = $Matches[1] }
    if ($name) {
      $count++
      if ($depth -gt $maxSeen) { $maxSeen = $depth }
      if ($depth -le $MaxDepth) {
        $keep = @()
        foreach ($c in ($cls -split '\s+')) {
          if ($c -eq '') { continue }
          if ($c -match $dropRe) { continue }
          if ($c -match $keepRe) { $keep += $c }
        }
        $line = ('  ' * $depth) + $name
        if ($icon) { $line += " {icon:$icon}" }
        if ($keep.Count) { $line += '  ·  ' + ($keep -join ' ') }
        $out.Add($line)
      }
    }
    if (-not $selfClose -and ($void -notcontains $tag)) { $depth++ }
  }
  elseif ($m.Groups[4].Success) {
    $depth--
    if ($depth -lt 0) { $depth = 0 }
  }
}

"# $([System.IO.Path]::GetFileName($Path)) — 命名元素 $count 个，最大嵌套深度 $maxSeen"
$out
