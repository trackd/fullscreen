BeforeAll {
    $projectRoot = Split-Path $PSScriptRoot
}

Describe 'Repository documentation' {
    It 'Links every exported cmdlet from the documentation index' {
        $index = Get-Content (Join-Path $projectRoot 'docs/README.md') -Raw
        $readme = Get-Content (Join-Path $projectRoot 'README.md') -Raw
        $manifest = Import-PowerShellDataFile (Join-Path $projectRoot 'module/fullscreen.psd1')
        $links = @([regex]::Matches($index, '\[[^\]]+\]\(en-US/([^/)]+)\.md\)'))
        $links.Count | Should -Be $manifest.CmdletsToExport.Count
        foreach ($name in $manifest.CmdletsToExport) {
            $index | Should -Match ([regex]::Escape("[$name](en-US/$name.md)"))
        }
        $readme | Should -Match ([regex]::Escape('(docs/README.md)'))
    }

    It 'Resolves local Markdown links in the README, guides and command help' {
        $files = @(
            Get-Item (Join-Path $projectRoot 'README.md')
            Get-Item (Join-Path $projectRoot 'examples/README.md')
            Get-ChildItem (Join-Path $projectRoot 'docs') -Recurse -Filter '*.md' -File
        )
        foreach ($file in $files) {
            $text = Get-Content $file.FullName -Raw
            foreach ($match in [regex]::Matches($text, '\]\(([^)]+)\)')) {
                $target = $match.Groups[1].Value.Trim('<', '>')
                if ($target -match '^(https?:|mailto:|#)') {
                    continue
                }
                $target = ($target -split '#')[0]
                if ($target) {
                    Join-Path $file.DirectoryName $target | Should -Exist -Because "$($file.Name) links to $target"
                }
            }
        }
    }

    It 'Keeps documentation in the public guide, help and maintainer locations' {
        $files = @(git -C $projectRoot ls-files --cached --others --exclude-standard '*.md')
        $unexpected = @($files | Where-Object {
            $_ -notin @('README.md', 'AGENTS.md', 'docs/README.md', 'examples/README.md') -and
            $_ -notlike 'docs/en-US/*' -and $_ -notlike 'docs/guides/*' -and
            $_ -notlike 'docs/maintainers/*' -and
            (Test-Path -LiteralPath (Join-Path $projectRoot $_))
        })
        $unexpected | Should -BeNullOrEmpty
    }

    It 'Keeps PowerShell examples in guides and the README parseable' {
        $files = @(Get-Item (Join-Path $projectRoot 'README.md')) +
            @(Get-ChildItem (Join-Path $projectRoot 'docs/guides') -Filter '*.md' -File)
        $blocks = 0
        foreach ($file in $files) {
            $text = Get-Content $file.FullName -Raw
            foreach ($match in [regex]::Matches($text, '(?ms)^```powershell\s*\r?\n(.*?)^```')) {
                $tokens = $null
                $parseErrors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput($match.Groups[1].Value, [ref]$tokens, [ref]$parseErrors)
                $parseErrors | Should -BeNullOrEmpty -Because "$($file.Name) contains runnable PowerShell examples"
                $blocks++
            }
        }
        $blocks | Should -BeGreaterThan 0
    }

    It 'Resolves example paths in README commands and solution project references' {
        $readme = Get-Content -LiteralPath (Join-Path $projectRoot 'README.md') -Raw
        foreach ($match in [regex]::Matches($readme, '(?m)(?:\./)?(examples/[A-Za-z0-9_./-]+)')) {
            Join-Path $projectRoot $match.Groups[1].Value | Should -Exist
        }
        [xml]$solution = Get-Content -LiteralPath (Join-Path $projectRoot 'fullscreen.slnx') -Raw
        foreach ($project in $solution.SelectNodes('//Project')) {
            Join-Path $projectRoot $project.Path | Should -Exist
        }
    }
}
