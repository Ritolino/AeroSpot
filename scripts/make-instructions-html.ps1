# AeroSpot — Generatore di documentazione HTML offline
# Converte ISTRUZIONI.md e INSTRUCTIONS.md in HTML a file singolo con tema scuro AeroSpot (zero dipendenze esterne/CDN)

param(
    [string]$SourceDir = "",
    [string]$OutputDir = ""
)

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = $repoRoot
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "Docs"
}

$ErrorActionPreference = "Stop"

if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

function Convert-MarkdownToHtml {
    param(
        [string]$MarkdownText,
        [string]$Title,
        [string]$Language
    )

    $lines = $MarkdownText -split "`r?`n"
    $htmlBody = [System.Text.StringBuilder]::new()
    $inList = $false
    $inTable = $false
    $isFirstTableRow = $true

    foreach ($line in $lines) {
        $trimmed = $line.Trim()

        # Separator line
        if ($trimmed -eq '---') {
            if ($inList) { [void]$htmlBody.AppendLine("</ul>"); $inList = $false }
            if ($inTable) { [void]$htmlBody.AppendLine("</tbody></table>"); $inTable = $false; $isFirstTableRow = $true }
            [void]$htmlBody.AppendLine("<hr/>")
            continue
        }

        # Table rows
        if ($trimmed.StartsWith('|') -and $trimmed.EndsWith('|')) {
            if ($inList) { [void]$htmlBody.AppendLine("</ul>"); $inList = $false }
            
            # Check if separator row (|---|---|)
            if ($trimmed -match '^\|(\s*[-:]+\s*\|)+$') {
                continue
            }

            if (!$inTable) {
                [void]$htmlBody.AppendLine("<div class='table-container'><table>")
                $inTable = $true
                $isFirstTableRow = $true
            }

            $cells = $trimmed.Trim('|').Split('|') | ForEach-Object { $_.Trim() }

            if ($isFirstTableRow) {
                [void]$htmlBody.AppendLine("<thead><tr>")
                foreach ($c in $cells) {
                    $cFormatted = Format-InlineText $c
                    [void]$htmlBody.AppendLine("<th>$cFormatted</th>")
                }
                [void]$htmlBody.AppendLine("</tr></thead><tbody>")
                $isFirstTableRow = $false
            } else {
                [void]$htmlBody.AppendLine("<tr>")
                foreach ($c in $cells) {
                    $cFormatted = Format-InlineText $c
                    [void]$htmlBody.AppendLine("<td>$cFormatted</td>")
                }
                [void]$htmlBody.AppendLine("</tr>")
            }
            continue
        } else {
            if ($inTable) {
                [void]$htmlBody.AppendLine("</tbody></table></div>")
                $inTable = $false
                $isFirstTableRow = $true
            }
        }

        # Headings
        if ($trimmed -match '^(#{1,6})\s+(.*)$') {
            if ($inList) { [void]$htmlBody.AppendLine("</ul>"); $inList = $false }
            $level = $matches[1].Length
            $headingText = Format-InlineText $matches[2]
            [void]$htmlBody.AppendLine("<h$level>$headingText</h$level>")
            continue
        }

        # Blockquote
        if ($trimmed.StartsWith('>')) {
            if ($inList) { [void]$htmlBody.AppendLine("</ul>"); $inList = $false }
            $quoteText = Format-InlineText ($trimmed.Substring(1).Trim())
            [void]$htmlBody.AppendLine("<blockquote>$quoteText</blockquote>")
            continue
        }

        # Unordered list
        if ($trimmed -match '^[-*]\s+(.*)$') {
            if (!$inList) { [void]$htmlBody.AppendLine("<ul>"); $inList = $true }
            $itemText = Format-InlineText $matches[1]
            [void]$htmlBody.AppendLine("<li>$itemText</li>")
            continue
        } else {
            if ($inList) { [void]$htmlBody.AppendLine("</ul>"); $inList = $false }
        }

        # Empty line
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        # Regular paragraph
        $pText = Format-InlineText $trimmed
        [void]$htmlBody.AppendLine("<p>$pText</p>")
    }

    if ($inList) { [void]$htmlBody.AppendLine("</ul>") }
    if ($inTable) { [void]$htmlBody.AppendLine("</tbody></table></div>") }

    $css = @"
    :root {
        --bg-color: #0F172A;
        --card-bg: #1E293B;
        --text-main: #F8FAFC;
        --text-sub: #CBD5E1;
        --accent: #38BDF8;
        --accent-hover: #0284C7;
        --border-color: #334155;
        --code-bg: #090D16;
        --th-bg: #162032;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
        font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
        background-color: var(--bg-color);
        color: var(--text-main);
        line-height: 1.6;
        padding: 40px 20px;
    }
    .container {
        max-width: 860px;
        margin: 0 auto;
        background: var(--card-bg);
        border: 1px solid var(--border-color);
        border-radius: 12px;
        padding: 40px 48px;
        box-shadow: 0 10px 30px rgba(0, 0, 0, 0.4);
    }
    h1 {
        font-size: 2.2rem;
        color: var(--accent);
        border-bottom: 2px solid var(--border-color);
        padding-bottom: 12px;
        margin-bottom: 24px;
    }
    h2 {
        font-size: 1.5rem;
        color: var(--accent);
        margin-top: 32px;
        margin-bottom: 16px;
    }
    h3 {
        font-size: 1.15rem;
        color: var(--text-main);
        margin-top: 20px;
        margin-bottom: 10px;
    }
    p {
        margin-bottom: 14px;
        color: var(--text-sub);
    }
    ul, ol {
        margin: 10px 0 18px 26px;
        color: var(--text-sub);
    }
    li {
        margin-bottom: 6px;
    }
    strong {
        color: #FFFFFF;
    }
    hr {
        border: none;
        border-top: 1px solid var(--border-color);
        margin: 30px 0;
    }
    blockquote {
        background: rgba(56, 189, 248, 0.08);
        border-left: 4px solid var(--accent);
        padding: 12px 18px;
        border-radius: 4px;
        margin: 16px 0;
        color: #E2E8F0;
    }
    code {
        background-color: var(--code-bg);
        color: #38BDF8;
        padding: 2px 6px;
        border-radius: 4px;
        font-family: "Cascadia Code", Consolas, monospace;
        font-size: 0.9em;
    }
    .table-container {
        overflow-x: auto;
        margin: 20px 0;
        border-radius: 8px;
        border: 1px solid var(--border-color);
    }
    table {
        width: 100%;
        border-collapse: collapse;
        font-size: 0.95rem;
    }
    th, td {
        padding: 12px 16px;
        text-align: left;
        border-bottom: 1px solid var(--border-color);
    }
    th {
        background-color: var(--th-bg);
        color: var(--accent);
        font-weight: 600;
    }
    tr:last-child td {
        border-bottom: none;
    }
    tr:hover td {
        background-color: rgba(255, 255, 255, 0.02);
    }
    .footer {
        margin-top: 40px;
        padding-top: 20px;
        border-top: 1px solid var(--border-color);
        font-size: 0.85rem;
        color: #64748B;
        text-align: center;
    }
"@

    return @"
<!DOCTYPE html>
<html lang="$Language">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>$Title — AeroSpot</title>
    <style>
$css
    </style>
</head>
<body>
    <div class="container">
$($htmlBody.ToString())
        <div class="footer">
            AeroSpot — Windows 11 Spotlight &amp; QuickLook Engine &bull; Offline Documentation
        </div>
    </div>
</body>
</html>
"@
}

function Format-InlineText {
    param([string]$text)

    # Encode HTML entities
    $text = [System.Net.WebUtility]::HtmlEncode($text)

    # Bold **text**
    $text = [Regex]::Replace($text, '\*\*([^*]+)\*\*', '<strong>$1</strong>')

    # Italic *text*
    $text = [Regex]::Replace($text, '(?<!\*)\*([^*]+)\*(?!\*)', '<em>$1</em>')

    # Code `code`
    $text = [Regex]::Replace($text, '`([^`]+)`', '<code>$1</code>')

    return $text
}

# 1. Convert IT
$itSource = Join-Path $SourceDir "ISTRUZIONI.md"
if (Test-Path $itSource) {
    Write-Output "Elaborazione guida italiana: $itSource"
    $itContent = Get-Content -Path $itSource -Raw -Encoding UTF8
    $itHtml = Convert-MarkdownToHtml -MarkdownText $itContent -Title "Guida Utente AeroSpot" -Language "it"
    $itOutPath = Join-Path $OutputDir "instructions.it.html"
    Set-Content -Path $itOutPath -Value $itHtml -Encoding UTF8
    Copy-Item -Path $itSource -Destination (Join-Path $OutputDir "ISTRUZIONI.md") -Force
    Write-Output "Generato con successo: $itOutPath ($((Get-Item $itOutPath).Length) bytes)"
}

# 2. Convert EN
$enSource = Join-Path $SourceDir "INSTRUCTIONS.md"
if (Test-Path $enSource) {
    Write-Output "Elaborazione guida inglese: $enSource"
    $enContent = Get-Content -Path $enSource -Raw -Encoding UTF8
    $enHtml = Convert-MarkdownToHtml -MarkdownText $enContent -Title "AeroSpot User Guide" -Language "en"
    $enOutPath = Join-Path $OutputDir "instructions.en.html"
    Set-Content -Path $enOutPath -Value $enHtml -Encoding UTF8
    Copy-Item -Path $enSource -Destination (Join-Path $OutputDir "INSTRUCTIONS.md") -Force
    Write-Output "Generato con successo: $enOutPath ($((Get-Item $enOutPath).Length) bytes)"
}

Write-Output "Generazione documentazione completata."
