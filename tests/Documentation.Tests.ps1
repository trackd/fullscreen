BeforeAll { $projectRoot = Split-Path $PSScriptRoot }

Describe 'Repository documentation' {
    It 'Links every exported cmdlet to its Markdown help in the README table' {
        $readme = Get-Content (Join-Path $projectRoot 'README.md') -Raw
        $manifest = Import-PowerShellDataFile (Join-Path $projectRoot 'module/fullscreen.psd1')
        $links = @([regex]::Matches($readme, '(?m)^\| \[([^\]]+)\]\(docs/en-US/([^/)]+)\.md\)'))
        $links.Count | Should -Be $manifest.CmdletsToExport.Count
        foreach ($name in $manifest.CmdletsToExport) {
            $readme | Should -Match ([regex]::Escape("[$name](docs/en-US/$name.md)"))
        }
    }

    It 'Resolves local Markdown links in the README and command help' {
        $files = @(
            Get-Item (Join-Path $projectRoot 'README.md')
            Get-ChildItem (Join-Path $projectRoot 'docs/en-US') -Filter '*.md' -File
        )
        foreach ($file in $files) {
            $text = Get-Content $file.FullName -Raw
            foreach ($match in [regex]::Matches($text, '\]\(([^)]+)\)')) {
                $target = $match.Groups[1].Value.Trim('<','>')
                if ($target -match '^(https?:|mailto:|#)') { continue }
                $target = ($target -split '#')[0]
                if ($target) { Join-Path $file.DirectoryName $target | Should -Exist -Because "$($file.Name) links to $target" }
            }
        }
    }

    It 'Keeps Markdown limited to the main README, agent instructions and cmdlet help' {
        $files = @(git -C $projectRoot ls-files --cached --others --exclude-standard '*.md')
        $unexpected = @($files | Where-Object {
            $_ -notin @('README.md', 'AGENTS.md') -and $_ -notlike 'docs/en-US/*' -and
            (Test-Path -LiteralPath (Join-Path $projectRoot $_))
        })
        $unexpected | Should -BeNullOrEmpty
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
