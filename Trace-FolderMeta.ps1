param(
    [string]$TjaPath = 'D:\koioto\Songs\Taiko Charts\Robot Revolution\Robot Revolution.tja'
)

$ErrorActionPreference = 'Stop'

function Test-LooksLikeUtf8 {
    param([byte[]]$Bytes)

    $i = 0
    while ($i -lt $Bytes.Length) {
        $b = $Bytes[$i]

        if ($b -le 0x7F) {
            $i++
            continue
        }

        if (($b -band 0xE0) -eq 0xC0) {
            $expected = 1
        } elseif (($b -band 0xF0) -eq 0xE0) {
            $expected = 2
        } elseif (($b -band 0xF8) -eq 0xF0) {
            $expected = 3
        } else {
            return $false
        }

        if (($i + $expected) -ge $Bytes.Length) {
            return $false
        }

        for ($j = 1; $j -le $expected; $j++) {
            if (($Bytes[$i + $j] -band 0xC0) -ne 0x80) {
                return $false
            }
        }

        $i += ($expected + 1)
    }

    return $true
}

function Read-TextWithDetection {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $enc = [System.Text.UTF8Encoding]::new($true)
        return [pscustomobject]@{
            Encoding = 'utf-8-bom'
            ByteCount = $bytes.Length
            Text = $enc.GetString($bytes).TrimStart([char]0xFEFF)
        }
    }

    if (Test-LooksLikeUtf8 $bytes) {
        return [pscustomobject]@{
            Encoding = 'utf-8'
            ByteCount = $bytes.Length
            Text = [System.Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF)
        }
    }

    return [pscustomobject]@{
        Encoding = 'shift_jis'
        ByteCount = $bytes.Length
        Text = [System.Text.Encoding]::GetEncoding(932).GetString($bytes).TrimStart([char]0xFEFF)
    }
}

function Parse-GenreIni {
    param([string]$Path)

    $read = Read-TextWithDetection $Path
    Write-Host "ParseGenreIni path=$Path bytes=$($read.ByteCount) encoding=$($read.Encoding)"
    $lines = $read.Text -split "`r`n|`n|`r"
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        Write-Host ("  line[{0}]={1}" -f $i, $line)
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith(';')) {
            continue
        }

        if ($line.StartsWith('GenreName', [System.StringComparison]::OrdinalIgnoreCase)) {
            $parts = $line.Split('=')
            Write-Host "  GenreName candidate parts=$($parts.Length)"
            if ($parts.Length -ge 2 -and -not [string]::IsNullOrWhiteSpace($parts[1])) {
                $value = $parts[1].Trim()
                Write-Host "  GenreName parsed=$value"
                return $value
            }
        }
    }

    Write-Host '  GenreName not found'
    return $null
}

function Parse-BoxDef {
    param([string]$Path)

    $read = Read-TextWithDetection $Path
    Write-Host "ParseBoxDef path=$Path bytes=$($read.ByteCount) encoding=$($read.Encoding)"
    $lines = $read.Text -split "`r`n|`n|`r"
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $trimmed = $lines[$i].Trim()
        Write-Host ("  line[{0}]={1}" -f $i, $trimmed)
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith(';')) {
            continue
        }

        if ($trimmed.StartsWith('#GENRE', [System.StringComparison]::OrdinalIgnoreCase)) {
            $colonIdx = $trimmed.IndexOf(':')
            if ($colonIdx -gt 0) {
                $value = $trimmed.Substring($colonIdx + 1).Trim()
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    Write-Host "  Box genre parsed=$value"
                    return $value
                }
            }
        }
    }

    Write-Host '  #GENRE not found'
    return $null
}

$currentFolder = Split-Path -Parent $TjaPath
$parentFolder = Split-Path -Parent $currentFolder
$searchFolders = @($currentFolder, $parentFolder)

Write-Output "TJA path: $TjaPath"
Write-Output "Current folder: $currentFolder"
Write-Output "Parent folder: $parentFolder"
Write-Output 'Search order:'
$searchFolders | ForEach-Object { Write-Output "  $_" }

$folderMeta = [ordered]@{
    Name = $null
    Description = $null
    Albumart = $null
    GenreName = $null
}

foreach ($folder in $searchFolders) {
    Write-Output ''
    Write-Output "Inspecting folder: $folder"
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
        Write-Output '  Folder missing'
        continue
    }

    $folderJson = Join-Path $folder 'folder.json'
    $boxDef = Join-Path $folder 'box.def'
    $genreIni = Join-Path $folder 'genre.ini'

    Write-Output "  folder.json exists=$([System.IO.File]::Exists($folderJson)) path=$folderJson"
    Write-Output "  box.def exists=$([System.IO.File]::Exists($boxDef)) path=$boxDef"
    Write-Output "  genre.ini exists=$([System.IO.File]::Exists($genreIni)) path=$genreIni"

    if ([System.IO.File]::Exists($boxDef) -and [string]::IsNullOrWhiteSpace($folderMeta.GenreName)) {
        $folderMeta.GenreName = Parse-BoxDef $boxDef
    }

    if ([System.IO.File]::Exists($genreIni)) {
        $genre = Parse-GenreIni $genreIni
        if (-not [string]::IsNullOrWhiteSpace($genre)) {
            $folderMeta.GenreName = $genre
        }
    }
}

if ([string]::IsNullOrWhiteSpace($folderMeta.Name)) {
    $folderMeta.Name = Split-Path -Leaf $currentFolder
}

Write-Output ''
Write-Output 'Final folderMeta:'
$folderMeta.GetEnumerator() | ForEach-Object {
    Write-Output ("  {0}={1}" -f $_.Key, $(if ($null -eq $_.Value) { '<null>' } else { $_.Value }))
}
