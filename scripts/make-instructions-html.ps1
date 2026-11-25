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
    $listTag = $null
    $inTable = $false
    $isFirstTableRow = $true
    $inCodeBlock = $false

    $closeList = {
        if ($listTag) {
            [void]$htmlBody.AppendLine("</$listTag>")
            $script:listTag = $null
        }
    }

    foreach ($line in $lines) {
        $trimmed = $line.Trim()

        # Fenced code block
        if ($trimmed -match '^```') {
            if (-not $inCodeBlock) {
                if ($listTag) { [void]$htmlBody.AppendLine("</$listTag>"); $listTag = $null }
                if ($inTable) { [void]$htmlBody.AppendLine("</tbody></table></div>"); $inTable = $false; $isFirstTableRow = $true }
                [void]$htmlBody.Append("<pre><code>")
                $inCodeBlock = $true
            } else {
                [void]$htmlBody.AppendLine("</code></pre>")
                $inCodeBlock = $false
            }
            continue
        }

        if ($inCodeBlock) {
            $encodedCode = [System.Net.WebUtility]::HtmlEncode($line)
            [void]$htmlBody.AppendLine($encodedCode)
            continue
        }

        # Separator line
        if ($trimmed -eq '---') {
            if ($listTag) { [void]$htmlBody.AppendLine("</$listTag>"); $listTag = $null }
            if ($inTable) { [void]$htmlBody.AppendLine("</tbody></table></div>"); $inTable = $false; $isFirstTableRow = $true }
            [void]$htmlBody.AppendLine("<hr/>")
            continue
        }

        # Table rows
        if ($trimmed.StartsWith('|') -and $trimmed.EndsWith('|')) {
            if ($listTag) { [void]$htmlBody.AppendLine("</$listTag>"); $listTag = $null }

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
            if ($listTag) { [void]$htmlBody.AppendLine("</$listTag>"); $listTag = $null }
            $level = $matches[1].Length
            $headingText = Format-InlineText $matches[2]
            [void]$htmlBody.AppendLine("<h$level>$headingText</h$level>")
            continue
        }

        # Blockquote
        if ($trimmed.StartsWith('>')) {
            if ($listTag) { [void]$htmlBody.AppendLine("</$listTag>"); $listTag = $null }
            $quoteText = Format-InlineText ($trimmed.Substring(1).Trim())
            [void]$htmlBody.AppendLine("<blockquote>$quoteText</blockquote>")
            continue
        }

        # Unordered list
        if ($trimmed -match '^[-*]\s+(.*)$') {
            if ($listTag -ne 'ul') {
                if ($listTag) { [void]$htmlBody.AppendLine("</$listTag>") }
                [void]$htmlBody.AppendLine("<ul>")
                $listTag = 'ul'
            }
            $itemText = Format-InlineText $matches[1]
            [void]$htmlBody.AppendLine("<li>$itemText</li>")
            continue
        }

        # Ordered list
        if ($trimmed -match '^\d+\.\s+(.*)$') {
            if ($listTag -ne 'ol') {
                if ($listTag) { [void]$htmlBody.AppendLine("</$listTag>") }
                [void]$htmlBody.AppendLine("<ol>")
                $listTag = 'ol'
            }
            $itemText = Format-InlineText $matches[1]
            [void]$htmlBody.AppendLine("<li>$itemText</li>")
            continue
        }

        if ($listTag) { [void]$htmlBody.AppendLine("</$listTag>"); $listTag = $null }

        # Empty line
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        # Regular paragraph
        $pText = Format-InlineText $trimmed
        [void]$htmlBody.AppendLine("<p>$pText</p>")
    }

    if ($inCodeBlock) { [void]$htmlBody.AppendLine("</code></pre>") }
    if ($listTag) { [void]$htmlBody.AppendLine("</$listTag>") }
    if ($inTable) { [void]$htmlBody.AppendLine("</tbody></table></div>") }

    $footerText = if ($Language -eq "it") {
        "AeroSpot — Motore Spotlight &amp; QuickLook per Windows 11 &bull; Documentazione Offline"
    } else {
        "AeroSpot — Windows 11 Spotlight &amp; QuickLook Engine &bull; Offline Documentation"
    }

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
    a {
        color: var(--accent);
        text-decoration: none;
    }
    a:hover {
        text-decoration: underline;
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
    pre {
        background-color: var(--code-bg);
        border: 1px solid var(--border-color);
        border-radius: 8px;
        padding: 14px 16px;
        margin: 12px 0 18px 0;
        overflow-x: auto;
    }
    pre code {
        background: transparent;
        padding: 0;
        border-radius: 0;
        font-size: 0.9em;
        line-height: 1.5;
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
            $footerText
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

    # Inline links [text](url)
    $text = [Regex]::Replace($text, '\[([^\]]+)\]\(([^)]+)\)', '<a href="$2">$1</a>')

    # Bold **text**
    $text = [Regex]::Replace($text, '\*\*([^*]+)\*\*', '<strong>$1</strong>')

    # Italic *text*
    $text = [Regex]::Replace($text, '(?<!\*)\*([^*]+)\*(?!\*)', '<em>$1</em>')

    # Code `code`
    $text = [Regex]::Replace($text, '`([^`]+)`', '<code>$1</code>')

    return $text
}

# 1. Convert IT (preferisci sempre Docs/ISTRUZIONI.md del repository rispetto a OutputDir)
$itCandidate1 = Join-Path $SourceDir "Docs\ISTRUZIONI.md"
$itCandidate2 = Join-Path $OutputDir "ISTRUZIONI.md"
$itCandidate3 = Join-Path $SourceDir "ISTRUZIONI.md"
$itSource = if (Test-Path $itCandidate1) { $itCandidate1 } elseif (Test-Path $itCandidate2) { $itCandidate2 } else { $itCandidate3 }

if (Test-Path $itSource) {
    Write-Output "Elaborazione guida italiana: $itSource"
    $itContent = Get-Content -Path $itSource -Raw -Encoding UTF8
    $itHtml = Convert-MarkdownToHtml -MarkdownText $itContent -Title "Guida Utente AeroSpot" -Language "it"
    $itOutPath = Join-Path $OutputDir "instructions.it.html"
    Set-Content -Path $itOutPath -Value $itHtml -Encoding UTF8
    if ($itSource -ne (Join-Path $OutputDir "ISTRUZIONI.md")) {
        Copy-Item -Path $itSource -Destination (Join-Path $OutputDir "ISTRUZIONI.md") -Force
    }
    Write-Output "Generato con successo: $itOutPath ($((Get-Item $itOutPath).Length) bytes)"
}

# 2. Convert EN (preferisci sempre Docs/INSTRUCTIONS.md del repository rispetto a OutputDir)
$enCandidate1 = Join-Path $SourceDir "Docs\INSTRUCTIONS.md"
$enCandidate2 = Join-Path $OutputDir "INSTRUCTIONS.md"
$enCandidate3 = Join-Path $SourceDir "INSTRUCTIONS.md"
$enSource = if (Test-Path $enCandidate1) { $enCandidate1 } elseif (Test-Path $enCandidate2) { $enCandidate2 } else { $enCandidate3 }

if (Test-Path $enSource) {
    Write-Output "Elaborazione guida inglese: $enSource"
    $enContent = Get-Content -Path $enSource -Raw -Encoding UTF8
    $enHtml = Convert-MarkdownToHtml -MarkdownText $enContent -Title "AeroSpot User Guide" -Language "en"
    $enOutPath = Join-Path $OutputDir "instructions.en.html"
    Set-Content -Path $enOutPath -Value $enHtml -Encoding UTF8
    if ($enSource -ne (Join-Path $OutputDir "INSTRUCTIONS.md")) {
        Copy-Item -Path $enSource -Destination (Join-Path $OutputDir "INSTRUCTIONS.md") -Force
    }
    Write-Output "Generato con successo: $enOutPath ($((Get-Item $enOutPath).Length) bytes)"
}

Write-Output "Generazione documentazione completata."
