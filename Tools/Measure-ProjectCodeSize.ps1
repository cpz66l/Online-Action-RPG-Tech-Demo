param(
    [string]$Root = "",
    [string[]]$ExcludeDirectoryNames = @(
        ".git",
        ".vs",
        "bin",
        "obj",
        "Library",
        "Temp",
        "Logs",
        "UserSettings",
        "Builds"
    ),
    [string[]]$ExcludePathPrefixes = @(
        "Client\UnityProject\Assets\TextMesh Pro",
        "Client\UnityProject\Assets\TutorialInfo"
    ),
    [switch]$IncludeThirdPartyAndSamples,
    [switch]$IncludeThisTool,
    [switch]$IncludeShadersAsCode,
    [switch]$IncludeUnityMeta,
    [int]$TopExtensions = 20,
    [int]$TopFiles = 50,
    [switch]$AllFileDetails,
    [switch]$NoPause,
    [string]$OutputPath = ""
)

# 用于统计项目体量与代码量：文件数、体积、文本行数、空行、注释行、脚本数、逐文件代码明细。
# 默认排除 Git / IDE / 构建产物 / Unity 缓存目录，并跳过第三方/模板内容与本统计脚本自身。
# 默认脚本数只统计常规源码脚本；shader / hlsl / cginc 单独列为 Shader，不混入有效代码行。
# TopFiles 控制逐文件明细条数；传 0 或负数表示输出全部。AllFileDetails 会把非代码文本文件也纳入明细。
# 默认运行结束会等待按 Enter，方便双击或右键 PowerShell 运行查看结果；自动化运行可加 -NoPause。
$ErrorActionPreference = "Stop"

trap {
    Write-Host ""
    Write-Host ("Error: {0}" -f $_.Exception.Message) -ForegroundColor Red
    if (-not $NoPause) {
        Write-Host ""
        [void](Read-Host "按 Enter 退出")
    }
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Root)) {
    $resolvedRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
}
else {
    $resolvedRoot = Resolve-Path -LiteralPath $Root
}

$Root = $resolvedRoot.ProviderPath

function New-StringSet {
    param([string[]]$Values)

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($value in $Values) {
        [void]$set.Add($value)
    }

    return $set
}

function Get-ExtensionKey {
    param([System.IO.FileInfo]$File)

    if ([string]::IsNullOrEmpty($File.Extension)) {
        return "[no extension]"
    }

    return $File.Extension.ToLowerInvariant()
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    $base = $BasePath
    if (-not $base.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $base += [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = New-Object System.Uri($base)
    $pathUri = New-Object System.Uri($Path)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Get-TopLevelName {
    param([string]$RelativePath)

    $parts = $RelativePath -split '[\\/]'
    if ($parts.Length -le 1 -or [string]::IsNullOrWhiteSpace($parts[0])) {
        return "."
    }

    return $parts[0]
}

function Test-RelativePathPrefix {
    param(
        [string]$RelativePath,
        [string[]]$Prefixes
    )

    $normalizedPath = $RelativePath.Replace('/', '\')
    foreach ($prefix in $Prefixes) {
        if ([string]::IsNullOrWhiteSpace($prefix)) {
            continue
        }

        $normalizedPrefix = $prefix.Replace('/', '\').TrimEnd('\')
        if ($normalizedPath.Equals($normalizedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
        if ($normalizedPath.StartsWith($normalizedPrefix + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function ConvertTo-HumanSize {
    param([double]$Bytes)

    if ($Bytes -ge 1GB) {
        return ("{0:N2} GB" -f ($Bytes / 1GB))
    }
    if ($Bytes -ge 1MB) {
        return ("{0:N2} MB" -f ($Bytes / 1MB))
    }
    if ($Bytes -ge 1KB) {
        return ("{0:N2} KB" -f ($Bytes / 1KB))
    }

    return ("{0:N0} B" -f $Bytes)
}

function Get-ProjectFiles {
    param(
        [string]$Path,
        [System.Collections.Generic.HashSet[string]]$ExcludedDirectories
    )

    Get-ChildItem -LiteralPath $Path -File -Force -ErrorAction SilentlyContinue

    foreach ($directory in Get-ChildItem -LiteralPath $Path -Directory -Force -ErrorAction SilentlyContinue) {
        if ($ExcludedDirectories.Contains($directory.Name)) {
            continue
        }

        Get-ProjectFiles -Path $directory.FullName -ExcludedDirectories $ExcludedDirectories
    }
}

function Get-FileCategory {
    param(
        [string]$Extension,
        [System.Collections.Generic.HashSet[string]]$CodeExtensions,
        [System.Collections.Generic.HashSet[string]]$ShaderExtensions,
        [System.Collections.Generic.HashSet[string]]$UnityContentExtensions,
        [System.Collections.Generic.HashSet[string]]$ConfigExtensions,
        [System.Collections.Generic.HashSet[string]]$DocExtensions,
        [bool]$IsTextFile
    )

    if ($Extension -eq ".meta") {
        return "Unity Meta"
    }
    if ($CodeExtensions.Contains($Extension)) {
        return "Code"
    }
    if ($ShaderExtensions.Contains($Extension)) {
        return "Shader"
    }
    if ($UnityContentExtensions.Contains($Extension)) {
        return "Unity Content"
    }
    if ($ConfigExtensions.Contains($Extension)) {
        return "Config"
    }
    if ($DocExtensions.Contains($Extension)) {
        return "Docs"
    }
    if ($IsTextFile) {
        return "Other Text"
    }

    return "Binary / Asset"
}

function Get-CodeFileKind {
    param([string]$Extension)

    switch ($Extension) {
        ".cs" { return "C# / Unity" }
        ".ps1" { return "PowerShell" }
        ".psm1" { return "PowerShell Module" }
        ".psd1" { return "PowerShell Data" }
        ".shader" { return "Unity Shader" }
        ".hlsl" { return "HLSL" }
        ".cginc" { return "Shader Include" }
        ".glsl" { return "GLSL" }
        ".uss" { return "Unity UI Style" }
        ".uxml" { return "Unity UI Layout" }
        ".js" { return "JavaScript" }
        ".jsx" { return "JavaScript React" }
        ".ts" { return "TypeScript" }
        ".tsx" { return "TypeScript React" }
        ".py" { return "Python" }
        ".sh" { return "Shell" }
        ".bash" { return "Shell" }
        ".bat" { return "Batch" }
        ".cmd" { return "Batch" }
        default { return "Code" }
    }
}

function Test-CommentLine {
    param(
        [string]$TrimmedLine,
        [string]$Extension
    )

    if ([string]::IsNullOrWhiteSpace($TrimmedLine)) {
        return $false
    }

    switch -Regex ($Extension) {
        '^\.(cs|js|jsx|ts|tsx|java|c|cc|cpp|h|hpp|shader|hlsl|cginc|glsl|uss)$' {
            return ($TrimmedLine.StartsWith("//") -or $TrimmedLine.StartsWith("/*") -or $TrimmedLine.StartsWith("*") -or $TrimmedLine.StartsWith("*/"))
        }
        '^\.(ps1|psm1|psd1)$' {
            return ($TrimmedLine.StartsWith("#") -or $TrimmedLine.StartsWith("<#") -or $TrimmedLine.StartsWith("#>"))
        }
        '^\.(sh|bash|zsh|py|rb|yaml|yml)$' {
            return $TrimmedLine.StartsWith("#")
        }
        '^\.(xml|csproj|props|targets|config|html|htm|uxml)$' {
            return ($TrimmedLine.StartsWith("<!--") -or $TrimmedLine.StartsWith("-->"))
        }
        default {
            return $false
        }
    }
}

function Measure-TextFile {
    param(
        [System.IO.FileInfo]$File,
        [string]$Extension
    )

    $lineCount = 0
    $blankLineCount = 0
    $commentLineCount = 0

    foreach ($line in [System.IO.File]::ReadLines($File.FullName)) {
        $lineCount += 1
        $trimmed = $line.Trim()

        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            $blankLineCount += 1
        }
        elseif (Test-CommentLine -TrimmedLine $trimmed -Extension $Extension) {
            $commentLineCount += 1
        }
    }

    return [pscustomobject]@{
        Lines = $lineCount
        BlankLines = $blankLineCount
        CommentLines = $commentLineCount
    }
}

function Add-Stats {
    param(
        [hashtable]$Table,
        [string]$Key,
        [System.IO.FileInfo]$File,
        [bool]$IsTextFile,
        [bool]$IsCodeFile,
        [int]$Lines,
        [int]$BlankLines,
        [int]$CommentLines
    )

    if (-not $Table.ContainsKey($Key)) {
        $Table[$Key] = [pscustomobject]@{
            Name = $Key
            Files = 0
            TextFiles = 0
            CodeFiles = 0
            Bytes = [Int64]0
            Lines = 0
            BlankLines = 0
            CommentLines = 0
            EstimatedCodeLines = 0
        }
    }

    $stats = $Table[$Key]
    $stats.Files += 1
    $stats.Bytes += $File.Length

    if ($IsTextFile) {
        $stats.TextFiles += 1
        $stats.Lines += $Lines
        $stats.BlankLines += $BlankLines
        $stats.CommentLines += $CommentLines
    }

    if ($IsCodeFile) {
        $stats.CodeFiles += 1
        $stats.EstimatedCodeLines += [Math]::Max(0, $Lines - $BlankLines - $CommentLines)
    }
}

function Convert-StatsForDisplay {
    param([object[]]$Rows)

    foreach ($row in $Rows) {
        [pscustomobject]@{
            Name = $row.Name
            Files = $row.Files
            TextFiles = $row.TextFiles
            ScriptFiles = $row.CodeFiles
            Size = ConvertTo-HumanSize -Bytes $row.Bytes
            Lines = $row.Lines
            BlankLines = $row.BlankLines
            CommentLines = $row.CommentLines
            EstimatedCodeLines = $row.EstimatedCodeLines
        }
    }
}

function Convert-StatsToMarkdownTable {
    param([object[]]$Rows)

    $lines = New-Object 'System.Collections.Generic.List[string]'
    [void]$lines.Add("| Name | Files | Text Files | Script Files | Size | Lines | Blank | Comments | Estimated Code |")
    [void]$lines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")

    foreach ($row in $Rows) {
        [void]$lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f $row.Name, $row.Files, $row.TextFiles, $row.CodeFiles, (ConvertTo-HumanSize -Bytes $row.Bytes), $row.Lines, $row.BlankLines, $row.CommentLines, $row.EstimatedCodeLines))
    }

    return $lines
}

function Convert-FileDetailsForDisplay {
    param([object[]]$Rows)

    foreach ($row in $Rows) {
        [pscustomobject]@{
            Lines = $row.Lines
            Blank = $row.BlankLines
            Comments = $row.CommentLines
            CodeLines = $row.EstimatedCodeLines
            Size = ConvertTo-HumanSize -Bytes $row.Bytes
            Kind = $row.Kind
            Path = $row.Path
        }
    }
}

function Convert-FileDetailsToMarkdownTable {
    param([object[]]$Rows)

    $lines = New-Object 'System.Collections.Generic.List[string]'
    [void]$lines.Add("| Path | Kind | Size | Lines | Blank | Comments | Estimated Code |")
    [void]$lines.Add("| --- | --- | ---: | ---: | ---: | ---: | ---: |")

    foreach ($row in $Rows) {
        [void]$lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |" -f $row.Path.Replace("|", "\|"), $row.Kind, (ConvertTo-HumanSize -Bytes $row.Bytes), $row.Lines, $row.BlankLines, $row.CommentLines, $row.EstimatedCodeLines))
    }

    return $lines
}

$excludedDirectorySet = New-StringSet -Values $ExcludeDirectoryNames
$codeExtensionSet = New-StringSet -Values @(
    ".cs", ".ps1", ".psm1", ".psd1", ".sh", ".bash",
    ".py", ".js", ".jsx", ".ts", ".tsx", ".java", ".c", ".cc", ".cpp", ".h", ".hpp",
    ".lua", ".sql"
)
$shaderExtensionSet = New-StringSet -Values @(
    ".shader", ".hlsl", ".cginc", ".glsl"
)
$unityContentExtensionSet = New-StringSet -Values @(
    ".unity", ".prefab", ".asset", ".mat", ".controller", ".anim", ".inputactions",
    ".shadergraph", ".shadersubgraph", ".wlt", ".uss", ".uxml"
)
$configExtensionSet = New-StringSet -Values @(
    ".json", ".xml", ".yaml", ".yml", ".csproj", ".sln", ".props", ".targets",
    ".config", ".asmdef", ".rsp", ".editorconfig", ".gitattributes", ".gitignore"
)
$docExtensionSet = New-StringSet -Values @(".md", ".txt")
$textExtensionSet = New-StringSet -Values @(
    ".cs", ".ps1", ".psm1", ".psd1", ".sh", ".bash", ".bat", ".cmd",
    ".py", ".js", ".jsx", ".ts", ".tsx", ".java", ".c", ".cc", ".cpp", ".h", ".hpp",
    ".lua", ".sql", ".shader", ".hlsl", ".cginc", ".glsl", ".uss", ".uxml",
    ".unity", ".prefab", ".asset", ".mat", ".controller", ".anim", ".inputactions",
    ".shadergraph", ".shadersubgraph", ".wlt", ".json", ".xml", ".yaml", ".yml",
    ".csproj", ".sln", ".props", ".targets", ".config", ".asmdef", ".rsp",
    ".editorconfig", ".gitattributes", ".gitignore", ".md", ".txt", ".meta"
)

if ($IncludeShadersAsCode) {
    foreach ($extension in $shaderExtensionSet) {
        [void]$codeExtensionSet.Add($extension)
    }
}

$thisToolRelativePathSet = New-StringSet -Values @(
    "Tools\Measure-ProjectCodeSize.ps1",
    "Tools\Run-Measure-ProjectCodeSize.cmd"
)

$categoryStats = @{}
$extensionStats = @{}
$topLevelStats = @{}
$fileDetails = New-Object 'System.Collections.Generic.List[object]'
$totalStats = @{
    Name = "Total"
    Files = 0
    TextFiles = 0
    CodeFiles = 0
    Bytes = [Int64]0
    Lines = 0
    BlankLines = 0
    CommentLines = 0
    EstimatedCodeLines = 0
}

$skippedMetaFiles = 0
$skippedMetaBytes = [Int64]0
$skippedPathPrefixFiles = 0
$skippedPathPrefixBytes = [Int64]0
$skippedThisToolFiles = 0
$skippedThisToolBytes = [Int64]0

foreach ($file in Get-ProjectFiles -Path $Root -ExcludedDirectories $excludedDirectorySet) {
    $extension = Get-ExtensionKey -File $file

    $relativePath = Get-RelativePath -BasePath $Root -Path $file.FullName
    $normalizedRelativePath = $relativePath.Replace('/', '\')

    if (-not $IncludeThisTool) {
        if ((-not [string]::IsNullOrWhiteSpace($PSCommandPath) -and $file.FullName.Equals($PSCommandPath, [System.StringComparison]::OrdinalIgnoreCase)) -or $thisToolRelativePathSet.Contains($normalizedRelativePath)) {
            $skippedThisToolFiles += 1
            $skippedThisToolBytes += $file.Length
            continue
        }
    }

    if (-not $IncludeThirdPartyAndSamples -and (Test-RelativePathPrefix -RelativePath $relativePath -Prefixes $ExcludePathPrefixes)) {
        $skippedPathPrefixFiles += 1
        $skippedPathPrefixBytes += $file.Length
        continue
    }

    if ($extension -eq ".meta" -and -not $IncludeUnityMeta) {
        $skippedMetaFiles += 1
        $skippedMetaBytes += $file.Length
        continue
    }

    $topLevelName = Get-TopLevelName -RelativePath $relativePath
    $isTextFile = $textExtensionSet.Contains($extension)
    $isCodeFile = $codeExtensionSet.Contains($extension)
    $category = Get-FileCategory -Extension $extension -CodeExtensions $codeExtensionSet -ShaderExtensions $shaderExtensionSet -UnityContentExtensions $unityContentExtensionSet -ConfigExtensions $configExtensionSet -DocExtensions $docExtensionSet -IsTextFile $isTextFile

    $lineStats = [pscustomobject]@{ Lines = 0; BlankLines = 0; CommentLines = 0 }
    if ($isTextFile) {
        try {
            $lineStats = Measure-TextFile -File $file -Extension $extension
        }
        catch {
            $isTextFile = $false
            $category = "Binary / Asset"
        }
    }

    $estimatedCodeLines = 0
    if ($isCodeFile) {
        $estimatedCodeLines = [Math]::Max(0, $lineStats.Lines - $lineStats.BlankLines - $lineStats.CommentLines)
    }

    $totalStats.Files += 1
    $totalStats.Bytes += $file.Length
    if ($isTextFile) {
        $totalStats.TextFiles += 1
        $totalStats.Lines += $lineStats.Lines
        $totalStats.BlankLines += $lineStats.BlankLines
        $totalStats.CommentLines += $lineStats.CommentLines
    }
    if ($isCodeFile) {
        $totalStats.CodeFiles += 1
        $totalStats.EstimatedCodeLines += $estimatedCodeLines
    }

    [void]$fileDetails.Add([pscustomobject]@{
        Path = $relativePath
        Extension = $extension
        Kind = if ($isCodeFile) { Get-CodeFileKind -Extension $extension } else { $category }
        IsTextFile = $isTextFile
        IsCodeFile = $isCodeFile
        Bytes = $file.Length
        Lines = $lineStats.Lines
        BlankLines = $lineStats.BlankLines
        CommentLines = $lineStats.CommentLines
        EstimatedCodeLines = $estimatedCodeLines
    })

    Add-Stats -Table $categoryStats -Key $category -File $file -IsTextFile $isTextFile -IsCodeFile $isCodeFile -Lines $lineStats.Lines -BlankLines $lineStats.BlankLines -CommentLines $lineStats.CommentLines
    Add-Stats -Table $extensionStats -Key $extension -File $file -IsTextFile $isTextFile -IsCodeFile $isCodeFile -Lines $lineStats.Lines -BlankLines $lineStats.BlankLines -CommentLines $lineStats.CommentLines
    Add-Stats -Table $topLevelStats -Key $topLevelName -File $file -IsTextFile $isTextFile -IsCodeFile $isCodeFile -Lines $lineStats.Lines -BlankLines $lineStats.BlankLines -CommentLines $lineStats.CommentLines
}

$categoryRows = @($categoryStats.Values | Sort-Object -Property @{ Expression = "EstimatedCodeLines"; Descending = $true }, @{ Expression = "Bytes"; Descending = $true })
$extensionRows = @($extensionStats.Values | Sort-Object -Property @{ Expression = "EstimatedCodeLines"; Descending = $true }, @{ Expression = "Bytes"; Descending = $true } | Select-Object -First $TopExtensions)
$topLevelRows = @($topLevelStats.Values | Sort-Object -Property @{ Expression = "EstimatedCodeLines"; Descending = $true }, @{ Expression = "Bytes"; Descending = $true })
$scriptTypeRows = @($extensionStats.Values | Where-Object { $codeExtensionSet.Contains($_.Name) } | Sort-Object -Property @{ Expression = "EstimatedCodeLines"; Descending = $true }, @{ Expression = "Files"; Descending = $true })
$shaderTypeRows = @($extensionStats.Values | Where-Object { $shaderExtensionSet.Contains($_.Name) } | Sort-Object -Property @{ Expression = "Lines"; Descending = $true }, @{ Expression = "Bytes"; Descending = $true })

if ($categoryStats.ContainsKey("Code")) {
    $codeCategory = $categoryStats["Code"]
    $sourceCodeSummary = [pscustomobject]@{
        Name = "Source Code"
        Files = $codeCategory.Files
        TextFiles = $codeCategory.TextFiles
        CodeFiles = $codeCategory.CodeFiles
        Bytes = $codeCategory.Bytes
        Lines = $codeCategory.Lines
        BlankLines = $codeCategory.BlankLines
        CommentLines = $codeCategory.CommentLines
        EstimatedCodeLines = $codeCategory.EstimatedCodeLines
    }
}
else {
    $sourceCodeSummary = [pscustomobject]@{
        Name = "Source Code"
        Files = 0
        TextFiles = 0
        CodeFiles = 0
        Bytes = [Int64]0
        Lines = 0
        BlankLines = 0
        CommentLines = 0
        EstimatedCodeLines = 0
    }
}

if ($AllFileDetails) {
    $fileDetailSource = @($fileDetails | Where-Object { $_.IsTextFile })
    $fileDetailScope = "text file"
}
else {
    $fileDetailSource = @($fileDetails | Where-Object { $_.IsCodeFile })
    $fileDetailScope = "code/script file"
}

$fileDetailRows = @($fileDetailSource | Sort-Object -Property @{ Expression = "EstimatedCodeLines"; Descending = $true }, @{ Expression = "Lines"; Descending = $true }, "Path")
if ($TopFiles -gt 0) {
    $fileDetailRows = @($fileDetailRows | Select-Object -First $TopFiles)
}

$fileDetailLimitText = if ($TopFiles -gt 0) { "top $TopFiles" } else { "all" }

$summary = [pscustomobject]@{
    Root = $Root
    GeneratedAt = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    ExcludedDirectories = $ExcludeDirectoryNames
    ExcludedPathPrefixes = if ($IncludeThirdPartyAndSamples) { @() } else { $ExcludePathPrefixes }
    IncludeThirdPartyAndSamples = [bool]$IncludeThirdPartyAndSamples
    IncludeThisTool = [bool]$IncludeThisTool
    IncludeShadersAsCode = [bool]$IncludeShadersAsCode
    IncludeUnityMeta = [bool]$IncludeUnityMeta
    SkippedThirdPartyAndSampleFiles = $skippedPathPrefixFiles
    SkippedThirdPartyAndSampleSize = ConvertTo-HumanSize -Bytes $skippedPathPrefixBytes
    SkippedThisToolFiles = $skippedThisToolFiles
    SkippedThisToolSize = ConvertTo-HumanSize -Bytes $skippedThisToolBytes
    SkippedUnityMetaFiles = $skippedMetaFiles
    SkippedUnityMetaSize = ConvertTo-HumanSize -Bytes $skippedMetaBytes
    Files = $totalStats.Files
    TextFiles = $totalStats.TextFiles
    CodeFiles = $totalStats.CodeFiles
    Size = ConvertTo-HumanSize -Bytes $totalStats.Bytes
    Lines = $totalStats.Lines
    BlankLines = $totalStats.BlankLines
    CommentLines = $totalStats.CommentLines
    EstimatedCodeLines = $totalStats.EstimatedCodeLines
}

Write-Host "Project Code Size Report" -ForegroundColor Cyan
Write-Host ("Root: {0}" -f $summary.Root)
Write-Host ("Generated: {0}" -f $summary.GeneratedAt)
Write-Host ("Excluded directories: {0}" -f ($summary.ExcludedDirectories -join ", "))
Write-Host "Counting rules: ScriptFiles = counted source/script files after exclusions; EstimatedCodeLines = Lines - BlankLines - whole-line CommentLines for ScriptFiles only."
if ($IncludeShadersAsCode) {
    Write-Host "Shader counting: shader/hlsl/cginc are included in ScriptFiles and EstimatedCodeLines."
}
else {
    Write-Host "Shader counting: shader/hlsl/cginc are reported separately and are not included in ScriptFiles or EstimatedCodeLines."
}
if (-not $IncludeThirdPartyAndSamples -and $ExcludePathPrefixes.Length -gt 0) {
    Write-Host ("Third-party/sample skipped: {0} files, {1} ({2})" -f $summary.SkippedThirdPartyAndSampleFiles, $summary.SkippedThirdPartyAndSampleSize, ($ExcludePathPrefixes -join "; "))
}
if (-not $IncludeThisTool) {
    Write-Host ("This tool skipped: {0} files, {1} (use -IncludeThisTool to include)" -f $summary.SkippedThisToolFiles, $summary.SkippedThisToolSize)
}
if (-not $IncludeUnityMeta) {
    Write-Host ("Unity .meta skipped: {0} files, {1} (use -IncludeUnityMeta to include)" -f $summary.SkippedUnityMetaFiles, $summary.SkippedUnityMetaSize)
}
Write-Host ""

Write-Host "Source code summary" -ForegroundColor Cyan
Convert-StatsForDisplay -Rows @($sourceCodeSummary) | Format-Table -AutoSize

Write-Host "Project file summary" -ForegroundColor Cyan
Convert-StatsForDisplay -Rows @([pscustomobject]$totalStats) | Format-Table -AutoSize

Write-Host "By category" -ForegroundColor Cyan
Convert-StatsForDisplay -Rows $categoryRows | Format-Table -AutoSize

Write-Host "By top-level directory" -ForegroundColor Cyan
Convert-StatsForDisplay -Rows $topLevelRows | Format-Table -AutoSize

Write-Host "By script/code type" -ForegroundColor Cyan
Convert-StatsForDisplay -Rows $scriptTypeRows | Format-Table -AutoSize

if ($shaderTypeRows.Length -gt 0 -and -not $IncludeShadersAsCode) {
    Write-Host "By shader type (not counted as script code)" -ForegroundColor Cyan
    Convert-StatsForDisplay -Rows $shaderTypeRows | Format-Table -AutoSize
}

Write-Host ("Top {0} extensions" -f $TopExtensions) -ForegroundColor Cyan
Convert-StatsForDisplay -Rows $extensionRows | Format-Table -AutoSize

Write-Host ("File details: {0} {1}s by estimated code lines" -f $fileDetailLimitText, $fileDetailScope) -ForegroundColor Cyan
Convert-FileDetailsForDisplay -Rows $fileDetailRows | Format-Table -AutoSize -Wrap

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $report = New-Object 'System.Collections.Generic.List[string]'
    [void]$report.Add("# Project Code Size Report")
    [void]$report.Add("")
    [void]$report.Add(('- Root: `{0}`' -f $summary.Root))
    [void]$report.Add(("- Generated: {0}" -f $summary.GeneratedAt))
    [void]$report.Add(("- Excluded directories: {0}" -f ($summary.ExcludedDirectories -join ", ")))
    [void]$report.Add(("- Excluded third-party/sample paths: {0}" -f ($(if ($summary.ExcludedPathPrefixes.Count -gt 0) { $summary.ExcludedPathPrefixes -join ", " } else { "None" }))))
    [void]$report.Add(("- Include third-party and samples: {0}" -f $summary.IncludeThirdPartyAndSamples))
    [void]$report.Add(("- Include this tool: {0}" -f $summary.IncludeThisTool))
    [void]$report.Add(("- Include shaders as code: {0}" -f $summary.IncludeShadersAsCode))
    [void]$report.Add(("- Include Unity .meta: {0}" -f $summary.IncludeUnityMeta))
    [void]$report.Add(("- Skipped third-party/sample files: {0} files, {1}" -f $summary.SkippedThirdPartyAndSampleFiles, $summary.SkippedThirdPartyAndSampleSize))
    [void]$report.Add(("- Skipped this tool: {0} files, {1}" -f $summary.SkippedThisToolFiles, $summary.SkippedThisToolSize))
    [void]$report.Add(("- Skipped Unity .meta: {0} files, {1}" -f $summary.SkippedUnityMetaFiles, $summary.SkippedUnityMetaSize))
    [void]$report.Add("")
    [void]$report.Add("## Counting Rules")
    [void]$report.Add("")
    [void]$report.Add("- Script Files: counted source/script files after exclusions.")
    [void]$report.Add("- Estimated Code: Lines minus blank lines minus whole-line comments for Script Files only.")
    [void]$report.Add("- Shaders: reported separately by default; pass `-IncludeShadersAsCode` to include shader/hlsl/cginc in Script Files and Estimated Code.")
    [void]$report.Add("")
    [void]$report.Add("## Source Code Summary")
    [void]$report.Add("")
    foreach ($line in Convert-StatsToMarkdownTable -Rows @($sourceCodeSummary)) { [void]$report.Add($line) }
    [void]$report.Add("")
    [void]$report.Add("## Project File Summary")
    [void]$report.Add("")
    foreach ($line in Convert-StatsToMarkdownTable -Rows @([pscustomobject]$totalStats)) { [void]$report.Add($line) }
    [void]$report.Add("")
    [void]$report.Add("## By Category")
    [void]$report.Add("")
    foreach ($line in Convert-StatsToMarkdownTable -Rows $categoryRows) { [void]$report.Add($line) }
    [void]$report.Add("")
    [void]$report.Add("## By Top-Level Directory")
    [void]$report.Add("")
    foreach ($line in Convert-StatsToMarkdownTable -Rows $topLevelRows) { [void]$report.Add($line) }
    [void]$report.Add("")
    [void]$report.Add("## By Script/Code Type")
    [void]$report.Add("")
    foreach ($line in Convert-StatsToMarkdownTable -Rows $scriptTypeRows) { [void]$report.Add($line) }
    [void]$report.Add("")
    if ($shaderTypeRows.Length -gt 0 -and -not $IncludeShadersAsCode) {
        [void]$report.Add("## By Shader Type (Not Counted as Script Code)")
        [void]$report.Add("")
        foreach ($line in Convert-StatsToMarkdownTable -Rows $shaderTypeRows) { [void]$report.Add($line) }
        [void]$report.Add("")
    }
    [void]$report.Add(("## Top {0} Extensions" -f $TopExtensions))
    [void]$report.Add("")
    foreach ($line in Convert-StatsToMarkdownTable -Rows $extensionRows) { [void]$report.Add($line) }
    [void]$report.Add("")
    [void]$report.Add(("## File Details: {0} {1}s by Estimated Code Lines" -f $fileDetailLimitText, $fileDetailScope))
    [void]$report.Add("")
    foreach ($line in Convert-FileDetailsToMarkdownTable -Rows $fileDetailRows) { [void]$report.Add($line) }

    $resolvedOutputPath = $OutputPath
    if (-not [System.IO.Path]::IsPathRooted($resolvedOutputPath)) {
        $resolvedOutputPath = Join-Path $Root $resolvedOutputPath
    }

    $outputDirectory = Split-Path -Parent $resolvedOutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path -LiteralPath $outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    }

    $report | Out-File -LiteralPath $resolvedOutputPath -Encoding UTF8
    Write-Host ("Report written: {0}" -f $resolvedOutputPath) -ForegroundColor Green
}

if (-not $NoPause) {
    Write-Host ""
    [void](Read-Host "统计完成，按 Enter 退出")
}
