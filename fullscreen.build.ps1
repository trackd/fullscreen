#!/usr/bin/env pwsh
#Requires -Version 7.6 -Modules InvokeBuild
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipHelp,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$moduleName = [IO.Path]::GetFileName($PSCommandPath) -replace '\.build\.ps1$'
$script:folders = @{
    ModuleName       = $moduleName
    ProjectRoot      = $PSScriptRoot
    OutputPath       = Join-Path $PSScriptRoot 'output'
    LibraryPath      = Join-Path $PSScriptRoot 'output' 'lib'
    ModuleSourcePath = Join-Path $PSScriptRoot 'module'
    DocsPath         = Join-Path $PSScriptRoot 'docs' 'en-US'
    TestPath         = Join-Path $PSScriptRoot 'tests'
    TestResultPath   = Join-Path $PSScriptRoot 'artifacts' 'test-results'
    CsprojPath       = Join-Path $PSScriptRoot 'src' $moduleName "$moduleName.csproj"
    TestCsprojPath   = Join-Path $PSScriptRoot 'tests' "$moduleName.Tests" "$moduleName.Tests.csproj"
}

function Remove-BuiltinParameterHelp {
    param([object[]]$CommandHelp)
    $builtins = @([System.Management.Automation.PSCmdlet]::CommonParameters) +
        @([System.Management.Automation.PSCmdlet]::OptionalCommonParameters)
    foreach ($model in $CommandHelp) {
        for ($index = $model.Parameters.Count - 1; $index -ge 0; $index--) {
            if ($model.Parameters[$index].Name -in $builtins) {
                $model.Parameters.RemoveAt($index)
            }
        }
    }
}

task Clean {
    if (Test-Path -LiteralPath $folders.OutputPath) {
        if ((Get-Item -LiteralPath $folders.OutputPath).LinkType) { throw 'Build output must not be a directory link.' }
        Remove-Item -LiteralPath $folders.OutputPath -Recurse -Force
    }
    New-Item -Path $folders.LibraryPath -ItemType Directory -Force | Out-Null
}

task Build {
    if (-not (Test-Path $folders.CsprojPath)) {
        Write-Warning 'C# project not found, skipping Build'
        return
    }

    $ModuleFile = Get-Module (Join-Path $script:folders.ProjectRoot 'module' "$($script:folders.ModuleName).psd1") -ListAvailable
    [xml]$csproj = Get-Content -Path $folders.CsprojPath -Raw
    $frameworks = $csproj.
    SelectNodes('//TargetFramework | //TargetFrameworks').
    '#text'.
    Split(';', [StringSplitOptions]::RemoveEmptyEntries)

    $dotnetArgs = @(
        'publish'
        $folders.CsprojPath
        '--configuration', $Configuration
        '--nologo'
        '-m:2'
        '--verbosity', 'minimal'
        ('-p:Version={0}' -f $ModuleFile.Version.ToString())
    )

    foreach ($fwork in $frameworks) {
        exec { dotnet @dotnetArgs --framework $fwork --output $folders.LibraryPath }
    }
}

task ModuleFiles {
    Get-ChildItem -LiteralPath $folders.ModuleSourcePath -File |
        Copy-Item -Destination $folders.OutputPath -Force
    Copy-Item -LiteralPath (Join-Path $folders.ModuleSourcePath 'lib' 'XenoAtom.txt') -Destination $folders.LibraryPath -Force
}

task GenerateHelp -If (-not $SkipHelp.IsPresent) {
    if (-not (Get-Module -ListAvailable -Name Microsoft.PowerShell.PlatyPS)) {
        Install-Module -Name Microsoft.PowerShell.PlatyPS -Scope CurrentUser -Force -AllowClobber
    }
    Import-Module Microsoft.PowerShell.PlatyPS -ErrorAction Stop

    $helpOutputPath = Join-Path $folders.OutputPath 'en-US'
    $tempOutputPath = Join-Path $helpOutputPath 'temp'
    # Start fresh because PlatyPS can leave trailing XML when overwriting a longer file.
    if (Test-Path -LiteralPath $tempOutputPath) {
        Remove-Item -LiteralPath $tempOutputPath -Recurse -Force
    }
    $commandHelp = @(Get-ChildItem -LiteralPath $folders.DocsPath -Filter '*.md' -File |
        Where-Object { $_.Name -ne "$moduleName.md" } |
        Import-MarkdownCommandHelp)
    Remove-BuiltinParameterHelp -CommandHelp $commandHelp
    Export-MamlCommandHelp -CommandHelp $commandHelp -OutputFolder $tempOutputPath | Out-Null
    $builtins = @([System.Management.Automation.PSCmdlet]::CommonParameters) +
        @([System.Management.Automation.PSCmdlet]::OptionalCommonParameters)
    foreach ($file in Get-ChildItem -LiteralPath $tempOutputPath -Filter '*.xml' -File -Recurse) {
        $xml = [xml](Get-Content -LiteralPath $file.FullName -Raw)
        foreach ($model in $commandHelp) {
            $syntaxItems = $xml.SelectNodes("//*[local-name()='command'][*[local-name()='details']/*[local-name()='name']='$($model.Title)']/*[local-name()='syntax']/*[local-name()='syntaxItem']")
            for ($index = 0; $index -lt $syntaxItems.Count; $index++) {
                $item = $syntaxItems[$index]
                # PlatyPS omits built-ins from MAML syntax when their authored sections are absent.
                foreach ($parameter in $model.Syntax[$index].SyntaxParameters | Where-Object { $_.ParameterName -in $builtins }) {
                    $node = $xml.CreateElement('command', 'parameter', $item.NamespaceURI)
                    $node.SetAttribute('required', 'false')
                    $node.SetAttribute('position', 'Named')
                    $name = $xml.CreateElement('maml', 'name', 'http://schemas.microsoft.com/maml/2004/10')
                    $name.InnerText = $parameter.ParameterName
                    $null = $node.AppendChild($name)
                    if (-not $parameter.IsSwitchParameter) {
                        $value = $xml.CreateElement('command', 'parameterValue', $item.NamespaceURI)
                        $value.SetAttribute('required', 'true')
                        $value.InnerText = $parameter.ParameterType
                        $null = $node.AppendChild($value)
                    }
                    $null = $item.AppendChild($node)
                }
            }
        }
        $xml.Save($file.FullName)
        Move-Item -LiteralPath $file.FullName -Destination $helpOutputPath -Force
    }
    Remove-Item -LiteralPath $tempOutputPath -Recurse -Force
}

task CleanAfter {
    Get-ChildItem -LiteralPath $folders.OutputPath -File -Recurse |
        Where-Object { $_.Extension -in @('.pdb', '.json') } |
        Remove-Item -Force
    # Compiler XML docs are not shipped; cmdlet help lives in en-US at the module root.
    Get-ChildItem -LiteralPath $folders.LibraryPath -Filter '*.xml' -File |
        Remove-Item -Force
}

task Test -If (-not $SkipTests.IsPresent) {
    if ($SkipHelp.IsPresent) { throw 'Pester tests require generated help. Omit SkipHelp or use SkipTests.' }
    Import-Module Pester -MinimumVersion 6.0.0 -ErrorAction Stop
    $pesterConfig = New-PesterConfiguration
    $pesterConfig.Run.Path = $folders.TestPath
    $pesterConfig.Run.Throw = $true
    $pesterConfig.Run.PassThru = $true
    $pesterConfig.Filter.ExcludeTag = @('Interactive', 'TextMate')
    $pesterConfig.Output.CIFormat = 'Auto'
    $pesterConfig.TestResult.Enabled = $true
    $pesterConfig.TestResult.OutputFormat = 'JUnitXml'
    $pesterConfig.TestResult.OutputPath = Join-Path $folders.TestResultPath 'pester.xml'
    New-Item -Path $folders.TestResultPath -ItemType Directory -Force | Out-Null
    $result = Invoke-Pester -Configuration $pesterConfig
    if ($result.TotalCount -eq 0) { throw 'Pester discovered no tests.' }
}

task DotNetTest -If (-not $SkipTests.IsPresent) {
    exec { dotnet test $folders.TestCsprojPath --configuration $Configuration --nologo -m:2 --logger 'trx;LogFileName=dotnet.trx' --results-directory $folders.TestResultPath }
}

task UpdateHelp {
    if (-not (Get-Module -ListAvailable -Name Microsoft.PowerShell.PlatyPS)) {
        Install-Module -Name Microsoft.PowerShell.PlatyPS -Scope CurrentUser -Force -AllowClobber
    }
    Import-Module Microsoft.PowerShell.PlatyPS -ErrorAction Stop
    Import-Module -Name (Join-Path $folders.OutputPath "$moduleName.psd1") -Force -ErrorAction Stop
    foreach ($file in Get-ChildItem -LiteralPath $folders.DocsPath -Filter '*.md' -File) {
        $source = Microsoft.PowerShell.PlatyPS\Import-MarkdownCommandHelp -LiteralPath $file.FullName -ErrorAction Stop
        $model = Microsoft.PowerShell.PlatyPS\Update-CommandHelp -LiteralPath $file.FullName -ErrorAction Stop
        Remove-BuiltinParameterHelp -CommandHelp $model
        $model.Locale = [cultureinfo]'en-US'
        $model.Metadata['Locale'] = 'en-US'
        # PlatyPS 1.0.1 strips [] from imported input type headings, then appends
        # a second array entry when updating. Keep one documented pipeline element type.
        $inputs = @{}
        foreach ($entry in @($source.Inputs) + @($model.Inputs)) {
            $key = $entry.Typename.TrimEnd('[', ']')
            if (-not $inputs.ContainsKey($key) -or $inputs[$key].Description -match '\{\{') {
                $entry.Typename = $key
                $inputs[$key] = $entry
            }
        }
        $model.Inputs.Clear()
        foreach ($key in $inputs.Keys | Sort-Object) { $model.Inputs.Add($inputs[$key]) }
        $staging = Join-Path $folders.ProjectRoot 'artifacts/help-update'
        $null = Microsoft.PowerShell.PlatyPS\Export-MarkdownCommandHelp -CommandHelp $model -OutputFolder $staging -Force -ErrorAction Stop
        $text = [IO.File]::ReadAllText((Join-Path $staging "Fullscreen/$($file.Name)"))
        [IO.File]::WriteAllText($file.FullName, $text.Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
    }
    Write-Output 'Updated Markdown command metadata. Review the changes and fill any new parameter descriptions before building help.'
}

task Format {
    $collectorProject = Join-Path $folders.ProjectRoot 'src/fullscreen.Collector/fullscreen.Collector.csproj'
    exec { dotnet format whitespace $collectorProject --verify-no-changes --no-restore }
    exec { dotnet format style $collectorProject --diagnostics IDE0011 --verify-no-changes --no-restore }
}

task Validate {
    $null = Test-ModuleManifest -Path (Join-Path $folders.OutputPath "$moduleName.psd1") -ErrorAction Stop
}

task All -Jobs Clean, Build, ModuleFiles, GenerateHelp, CleanAfter, Validate, Test, DotNetTest, Format
task BuildAndTest All
task . All
