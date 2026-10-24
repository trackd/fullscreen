Describe 'Build task graph' {
    It 'exposes publish, help, tests and format checks through the root task runner' {
        $projectRoot = Split-Path -Parent $PSScriptRoot
        $buildScript = Join-Path $projectRoot 'build.ps1'
        $powerShell = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
        $taskList = & $powerShell -NoProfile -File $buildScript -Task '?' | Out-String -Width 240
        $LASTEXITCODE | Should -Be 0

        $taskList | Should -Match 'UpdateHelp'
        $taskList | Should -Match 'Format'
        $taskList | Should -Match 'All\s+\{Clean, Build, ModuleFiles, GenerateHelp'
        $taskList | Should -Match 'BuildAndTest\s+All'
    }

    It 'routes CI through the root entry point instead of invoking tool scripts directly' {
        $workflow = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '.github/workflows/validate.yml') -Raw

        $workflow | Should -Match '(?m)^\s+\./build\.ps1 -Configuration Release\s*$'
        $workflow | Should -Not -Match 'tools/|dotnet format'
    }
}

Describe 'CI ownership and release boundaries' {
    BeforeAll {
        $projectRoot = Split-Path -Parent $PSScriptRoot
        $releaseWorkflow = Get-Content -LiteralPath (Join-Path $projectRoot '.github/workflows/release.yml') -Raw
        $validationWorkflow = Get-Content -LiteralPath (Join-Path $projectRoot '.github/workflows/validate.yml') -Raw
        $codeOwnersContent = Get-Content -LiteralPath (Join-Path $projectRoot '.github/CODEOWNERS') -Raw
    }

    It 'Assigns CI, release metadata and build configuration to trackd' {
        foreach ($pattern in @('/.github/', '/build.ps1', '/*.build.ps1', '*.csproj', '*.props', '*.targets', '/global.json', 'NuGet.Config', 'nuget.config', '/.config/dotnet-tools.json', '/module/fullscreen.psd1')) {
            $codeOwnersContent | Should -Match ('(?m)^' + [regex]::Escape($pattern) + '\s+@trackd\s*$')
        }
    }

    It 'Restricts the <JobName> release job to the canonical repository and default branch' -ForEach @(
        @{ JobName = 'prepare' }
        @{ JobName = 'publish' }
    ) {
        $job = [regex]::Match($releaseWorkflow, "(?ms)^  ${JobName}:\r?\n(?<Body>.*?)(?=^  [a-z][a-z0-9_-]*:\r?$|\z)")
        $job.Success | Should -BeTrue
        $condition = [regex]::Match($job.Groups['Body'].Value, '(?m)^    if: (.+)\r?$')
        $condition.Success | Should -BeTrue
        $condition.Groups[1].Value.Trim() | Should -BeExactly "github.repository == 'trackd/fullscreen' && github.ref == format('refs/heads/{0}', github.event.repository.default_branch)"
    }

    It 'Keeps releases manual and validation unprivileged' {
        [regex]::Match($releaseWorkflow, '(?ms)^on:\r?\n(?<Triggers>.*?)(?=^[^\s])').Groups['Triggers'].Value.Trim() | Should -BeExactly 'workflow_dispatch:'
        $validationWorkflow | Should -Match '(?m)^permissions:\r?\n  contents: read\s*$'
        $validationWorkflow | Should -Not -Match 'pull_request_target|workflow_run|secrets\.'
        $validationWorkflow | Should -Match '(?m)^\s+persist-credentials: false\s*$'
    }
}

Describe 'Release artifact selection' {
    BeforeAll {
        $assetScript = Join-Path $PSScriptRoot '../.github/Get-ReleaseAsset.ps1'
    }

    It 'Returns the exact lowercase module package without changing its contents' {
        $directory = Join-Path $TestDrive 'valid'
        New-Item -Path $directory -ItemType Directory | Out-Null
        $package = Join-Path $directory 'fullscreen.2.3.0.nupkg'
        Set-Content -LiteralPath $package -Value 'package fixture'
        $hash = (Get-FileHash -LiteralPath $package).Hash

        $asset = & $assetScript -Version '2.3.0' -Path $directory

        $asset | Should -BeOfType ([IO.FileInfo])
        $asset.FullName | Should -BeExactly $package
        (Get-FileHash -LiteralPath $package).Hash | Should -BeExactly $hash
    }

    It 'Rejects missing, mismatched and incorrectly cased packages (<Name>)' -ForEach @(
        @{
            Name = 'missing'
            FileName = $null
        }
        @{
            Name = 'wrong version'
            FileName = 'fullscreen.2.2.0.nupkg'
        }
        @{
            Name = 'wrong case'
            FileName = 'Fullscreen.2.3.0.nupkg'
        }
    ) {
        $directory = Join-Path $TestDrive $Name
        New-Item -Path $directory -ItemType Directory | Out-Null
        if ($FileName) {
            Set-Content -LiteralPath (Join-Path $directory $FileName) -Value 'fixture'
        }
        { & $assetScript -Version '2.3.0' -Path $directory } | Should -Throw '*Expected exactly one release asset*'
    }

    It 'Rejects duplicate packages from separate downloaded artifact folders' {
        $directory = Join-Path $TestDrive 'duplicates'
        foreach ($folder in @('first', 'second')) {
            $child = Join-Path $directory $folder
            New-Item -Path $child -ItemType Directory -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $child 'fullscreen.2.3.0.nupkg') -Value 'fixture'
        }
        { & $assetScript -Version '2.3.0' -Path $directory } | Should -Throw '*found 2*'
    }

    It 'Rejects unexpected files alongside the expected module package' {
        $directory = Join-Path $TestDrive 'unexpected'
        New-Item -Path $directory -ItemType Directory | Out-Null
        Set-Content -LiteralPath (Join-Path $directory 'fullscreen.2.3.0.nupkg') -Value 'fixture'
        Set-Content -LiteralPath (Join-Path $directory 'extra.txt') -Value 'fixture'
        { & $assetScript -Version '2.3.0' -Path $directory } | Should -Throw '*unexpected files*'
    }
}

Describe 'Release version' {
    It 'Updates only the manifest version and preserves the module contract' {
        $projectRoot = Split-Path -Parent $PSScriptRoot
        $path = Join-Path $TestDrive 'fullscreen.psd1'
        $source = Get-Content -LiteralPath (Join-Path $projectRoot 'module/fullscreen.psd1') -Raw
        Set-Content -LiteralPath $path -Value $source -NoNewline

        & (Join-Path $projectRoot '.github/Set-ModuleVersion.ps1') -Version '2.3.0' -Path $path

        $manifest = Import-PowerShellDataFile -LiteralPath $path
        $manifest.ModuleVersion | Should -Be '2.3.0'
        $expected = [regex]::Replace($source, "(?m)^(\s*ModuleVersion\s*=\s*')[^']+(')", '${1}2.3.0${2}')
        (Get-Content -LiteralPath $path -Raw) | Should -BeExactly $expected
    }

    It 'Rejects ambiguous manifest versions without changing the file' {
        $path = Join-Path $TestDrive 'ambiguous.psd1'
        $source = "ModuleVersion = '1.0.0'`nModuleVersion = '1.1.0'"
        Set-Content -LiteralPath $path -Value $source -NoNewline

        { & (Join-Path $PSScriptRoot '../.github/Set-ModuleVersion.ps1') -Version '2.3.0' -Path $path } | Should -Throw '*Expected one ModuleVersion*'
        (Get-Content -LiteralPath $path -Raw) | Should -BeExactly $source
    }

    It 'Ships the manifest version in both module assemblies' {
        $projectRoot = Split-Path -Parent $PSScriptRoot
        $manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $projectRoot 'output/fullscreen.psd1')
        foreach ($name in @('fullscreen', 'fullscreen.ALC')) {
            $assemblyPath = Join-Path $projectRoot "output/lib/$name.dll"
            [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion.Split('+')[0] |
                Should -Be $manifest.ModuleVersion
        }
    }
}
