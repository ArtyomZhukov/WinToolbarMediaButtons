
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

function Write-U16LE([int]$v) { $bw.Write([byte]($v -band 0xFF)); $bw.Write([byte](($v -shr 8) -band 0xFF)) }
function Write-U32LE([long]$v) {
    $bw.Write([byte]($v -band 0xFF)); $bw.Write([byte](($v -shr 8) -band 0xFF))
    $bw.Write([byte](($v -shr 16) -band 0xFF)); $bw.Write([byte](($v -shr 24) -band 0xFF))
}
function Write-U32BE([long]$v) {
    $bw.Write([byte](($v -shr 24) -band 0xFF)); $bw.Write([byte](($v -shr 16) -band 0xFF))
    $bw.Write([byte](($v -shr 8) -band 0xFF)); $bw.Write([byte]($v -band 0xFF))
}
function Write-ASCII([string]$s) { foreach ($c in $s.ToCharArray()) { $bw.Write([byte][int]$c) } }
function Write-ASCIZ([string]$s) { Write-ASCII $s; $bw.Write([byte]0) }
function Write-Pad([int]$size) { if ($size % 2 -ne 0) { $bw.Write([byte]0x0A) } }

function Write-ArHdr([string]$name, [int]$size) {
    Write-ASCII $name.PadRight(16).Substring(0,16)
    Write-ASCII "0           "   # date  12
    Write-ASCII "0     "         # uid    6
    Write-ASCII "0     "         # gid    6
    Write-ASCII "0       "       # mode   8
    Write-ASCII $size.ToString().PadRight(10)  # size 10
    $bw.Write([byte]0x60); $bw.Write([byte]0x0A)  # fmag  2
}

# [dll, stdcall_decorated_name]
# IAT symbol = "__imp__" + name_without_leading_underscore
$entries = @(
    @("KERNEL32.DLL", "_ExitProcess@4"),
    @("KERNEL32.DLL", "_GetModuleHandleW@4"),
    @("KERNEL32.DLL", "_LoadLibraryW@4"),
    @("KERNEL32.DLL", "_GetProcAddress@8"),
    @("KERNEL32.DLL", "_LoadLibraryA@4"),
    @("USER32.DLL",   "_MessageBoxA@16")
)

$FLAGS = [int]((3 -shl 2) -bor 0)   # NameType=UNDECORATE(3), Type=CODE(0) = 0x0C

# Размеры import objects (20-байтный заголовок + имя + имя DLL)
$objSizes = @()
foreach ($e in $entries) {
    $objSizes += 20 + ($e[1].Length+1) + ($e[0].Length+1)
}

# Символы для first linker member
# IAT = "__imp__" + decorated_name_without_leading_underscore
$allSyms = @()
foreach ($e in $entries) {
    $sym = $e[1]
    $allSyms += $sym
    $allSyms += ("__imp__" + $sym.Substring(1))
}

# Размер first linker member
$nameDataSize = 0; foreach ($s in $allSyms) { $nameDataSize += $s.Length+1 }
$fmSize = 4 + 4*$allSyms.Count + $nameDataSize
$fmPad  = $fmSize % 2

# Смещения объектов
$firstObjOff = 8 + 60 + $fmSize + $fmPad
$objOffsets = @()
$curOff = $firstObjOff
for ($i = 0; $i -lt $entries.Count; $i++) {
    $objOffsets += $curOff
    $pad = $objSizes[$i] % 2
    $curOff += 60 + $objSizes[$i] + $pad
}

# ── Архив ──────────────────────────────────────────────

# Magic
Write-ASCII "!<arch>"; $bw.Write([byte]0x0A)

# First linker member
Write-ArHdr "/" $fmSize
Write-U32BE $allSyms.Count
for ($i = 0; $i -lt $entries.Count; $i++) {
    Write-U32BE $objOffsets[$i]   # _func@N
    Write-U32BE $objOffsets[$i]   # __imp__func@N
}
foreach ($s in $allSyms) { Write-ASCIZ $s }
Write-Pad $fmSize

# Import objects
for ($i = 0; $i -lt $entries.Count; $i++) {
    $dll  = $entries[$i][0]
    $sym  = $entries[$i][1]
    $size = $objSizes[$i]
    Write-ArHdr ("f" + $i + ".obj") $size
    Write-U16LE 0x0000          # Sig1
    Write-U16LE 0xFFFF          # Sig2
    Write-U16LE 0x0000          # Version
    Write-U16LE 0x014C          # Machine = I386
    Write-U32LE 0               # TimeDateStamp
    Write-U32LE ($sym.Length+1+$dll.Length+1)  # SizeOfData
    Write-U16LE 0               # Hint
    Write-U16LE $FLAGS          # Type=CODE, NameType=UNDECORATE
    Write-ASCIZ $sym
    Write-ASCIZ $dll
    Write-Pad $size
}

$bw.Flush()
$bytes = $ms.ToArray()
$out = Join-Path $PSScriptRoot "kernel32_stdcall.lib"
[IO.File]::WriteAllBytes($out, $bytes)
Write-Host ("Created: " + $bytes.Length + " bytes -> " + $out)
