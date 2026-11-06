BeforeDiscovery {
    $commands = (Import-PowerShellDataFile (Join-Path $PSScriptRoot '../module/fullscreen.psd1')).CmdletsToExport |
        ForEach-Object { @{ CommandName = $_ } }
}

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../output/fullscreen.psd1') -Force
    Import-Module Microsoft.PowerShell.PlatyPS -ErrorAction Stop
    $commonParameters = @([System.Management.Automation.PSCmdlet]::CommonParameters) +
        @([System.Management.Automation.PSCmdlet]::OptionalCommonParameters)
}

Describe 'Cmdlet help' {
    It 'Generates complete chart input in the error bounds and column line examples without opening a host' {
        $text = Get-Content (Join-Path $PSScriptRoot '../docs/en-US/Out-Chart.md') -Raw
        foreach ($kind in 'ErrorBars', 'ColumnLine') {
            $block = @([regex]::Matches($text, '(?ms)^```powershell\s*\r?\n(.*?)^```') |
                Where-Object { $_.Groups[1].Value -match "-GraphType $kind\b" })
            $block.Count | Should -Be 1
            $tokens = $null; $parseErrors = $null
            $ast = [Management.Automation.Language.Parser]::ParseInput($block[0].Groups[1].Value, [ref]$tokens, [ref]$parseErrors)
            $assignment = @($ast.EndBlock.Statements | Where-Object {
                $_ -is [Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -eq '$measurements'
            })
            $assignment.Count | Should -Be 1
            $data = @(& ([scriptblock]::Create($assignment[0].Extent.Text + "`n`$measurements")))
            $data.Count | Should -Be 12
            if ($kind -eq 'ErrorBars') {
                @($data | Where-Object { $_.Low -ge $_.Mean -or $_.High -le $_.Mean }).Count | Should -Be 0
                ($data.Time -join ',') | Should -Be (1..12 -join ',')
            }
            else {
                @($data | Where-Object { $_.Target -ne 30 -or $null -eq $_.Actual -or $null -eq $_.Forecast }).Count | Should -Be 0
                ($data.Sample -join ',') | Should -Be (1..12 -join ',')
            }
        }
    }
    It 'Keeps shipped examples independent of repository files and undeclared input variables' {
        $automatic = @('true', 'false', 'null', '_', 'PSItem', 'PWD', 'HOME', 'LASTEXITCODE')
        foreach ($path in Get-ChildItem (Join-Path $PSScriptRoot '../docs/en-US') -Filter '*.md' -File) {
            $text = Get-Content -LiteralPath $path.FullName -Raw
            $text | Should -Not -Match '(?i)(?:examples[/\\]|docs[/\\]images[/\\])' -Because "$($path.Name) ships without repository examples or images"
            foreach ($match in [regex]::Matches($text, '(?ms)^```powershell\s*\r?\n(.*?)^```')) {
                $tokens = $null; $parseErrors = $null
                $ast = [Management.Automation.Language.Parser]::ParseInput($match.Groups[1].Value, [ref]$tokens, [ref]$parseErrors)
                $parseErrors | Should -BeNullOrEmpty
                $declared = @($automatic)
                foreach ($node in $ast.FindAll({
                    param($node)
                    $node -is [Management.Automation.Language.AssignmentStatementAst] -or
                    $node -is [Management.Automation.Language.ParameterAst] -or
                    $node -is [Management.Automation.Language.ForEachStatementAst]
                }, $true)) {
                    if ($node -is [Management.Automation.Language.ParameterAst]) {
                        $declared += $node.Name.VariablePath.UserPath
                    }
                    elseif ($node -is [Management.Automation.Language.ForEachStatementAst]) {
                        $declared += $node.Variable.VariablePath.UserPath
                    }
                    else {
                        $declared += @($node.Left.FindAll({ param($child) $child -is [Management.Automation.Language.VariableExpressionAst] }, $true) |
                            ForEach-Object { $_.VariablePath.UserPath })
                    }
                }
                foreach ($variable in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.VariableExpressionAst] }, $true)) {
                    $declared | Should -Contain $variable.VariablePath.UserPath -Because "$($path.Name) must define $($variable.Extent.Text) in the same code block"
                }
            }
        }
    }
    It 'Uses current Fullscreen command parameters in every documented PowerShell example' {
        $paths = @((Join-Path $PSScriptRoot '../README.md')) + @(Get-ChildItem (Join-Path $PSScriptRoot '../docs/en-US') -Filter '*.md' -File | ForEach-Object FullName)
        foreach ($path in $paths) {
            $text = Get-Content -LiteralPath $path -Raw
            foreach ($match in [regex]::Matches($text, '(?ms)^```powershell\s*\r?\n(.*?)^```')) {
                $tokens = $null; $parseErrors = $null
                $ast = [Management.Automation.Language.Parser]::ParseInput($match.Groups[1].Value, [ref]$tokens, [ref]$parseErrors)
                $parseErrors | Should -BeNullOrEmpty -Because "$path examples must parse"
                foreach ($literal in $ast.FindAll({
                    param($node)
                    $node -is [Management.Automation.Language.StringConstantExpressionAst]
                }, $true)) {
                    if ($literal.Value -match '\.(ps1|psd1|json|jsonc)$') {
                        $literal.Value | Should -Not -Match '[\[\]]' -Because "$path example filenames must be plain, readable names"
                    }
                }
                foreach ($call in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true)) {
                    $name = $call.GetCommandName()
                    if (-not $name) { continue }
                    $command = Get-Command -Name $name -Module fullscreen -ErrorAction SilentlyContinue
                    if (-not $command) { continue }
                    foreach ($parameter in $call.CommandElements | Where-Object { $_ -is [Management.Automation.Language.CommandParameterAst] }) {
                        $candidates = @($command.Parameters.Values | Where-Object {
                            $_.Name -like ($parameter.ParameterName + '*') -or $_.Aliases -contains $parameter.ParameterName
                        })
                        $candidates.Count | Should -BeGreaterThan 0 -Because "$path uses $name -$($parameter.ParameterName), which must exist"
                    }
                }
            }
        }
    }
    It 'Keeps source help aligned with <CommandName>' -ForEach $commands {
        $path = Join-Path $PSScriptRoot "../docs/en-US/$CommandName.md"
        $null = Test-MarkdownCommandHelp -LiteralPath $path -ErrorAction Stop
        Get-Content -LiteralPath $path -Raw | Should -Not -Match '\{\{'
        $model = Import-MarkdownCommandHelp -LiteralPath $path -ErrorAction Stop
        $model.Diagnostics.HadErrors | Should -BeFalse
        $model.Title | Should -Be $CommandName
        $model.Locale.Name | Should -Be 'en-US'
        $model.ExternalHelpFile | Should -Be 'fullscreen.dll-Help.xml'
        @($model.Parameters.Name | Where-Object { $_ -in $commonParameters }).Count |
            Should -Be 0 -Because 'PowerShell built-ins must not have individually authored parameter sections'
        $command = Get-Command "Fullscreen\$CommandName"
        $actual = @($command.Parameters.Keys | Where-Object { $_ -notin $commonParameters })
        $documented = @($model.Parameters.Name | Where-Object { $_ -notin $commonParameters })
        Compare-Object -ReferenceObject $actual -DifferenceObject $documented | Should -BeNullOrEmpty
    }

    It 'Packages a single help file at the module root' {
        $output = Join-Path $PSScriptRoot '../output'
        Join-Path $output 'en-US/fullscreen.dll-Help.xml' | Should -Exist
        Join-Path $output 'lib/en-US' | Should -Not -Exist
        Join-Path $output 'LICENSE' | Should -Not -Exist
        @(Get-ChildItem $output -Filter '*.xml' -File -Recurse).Count | Should -Be 1
        @(Get-ChildItem (Join-Path $PSScriptRoot '../docs/en-US') -Filter '*.md').Count | Should -Be 17
        $xml = [xml](Get-Content (Join-Path $output 'en-US/fullscreen.dll-Help.xml') -Raw)
        $documentedNames = @($xml.SelectNodes("//*[local-name()='parameters']/*[local-name()='parameter']/*[local-name()='name']") |
            ForEach-Object InnerText)
        @($documentedNames | Where-Object { $_ -in $commonParameters }).Count | Should -Be 0
        $syntaxNames = @($xml.SelectNodes("//*[local-name()='command'][*[local-name()='details']/*[local-name()='name']='Export-TuiDefinition']/*[local-name()='syntax']/*[local-name()='syntaxItem']/*[local-name()='parameter']/*[local-name()='name']") |
            ForEach-Object InnerText)
        $syntaxNames | Should -Contain 'WhatIf'
        $syntaxNames | Should -Contain 'Confirm'
        $export = Get-Command fullscreen\Export-TuiDefinition
        $export.Parameters.ContainsKey('WhatIf') | Should -BeTrue
        $export.Parameters.ContainsKey('Confirm') | Should -BeTrue
    }

    It 'Documents every parameter and three parseable examples for <CommandName>' -ForEach $commands {
        $command = Get-Command "Fullscreen\$CommandName"
        $help = Get-Help "Fullscreen\$CommandName" -Full
        $help.Description.Text | Should -Not -BeNullOrEmpty
        $help.Synopsis | Should -Not -Match ('^\s*' + [regex]::Escape($CommandName) + '\s*\[')
        @($help.Examples.Example).Count | Should -BeGreaterOrEqual 3
        foreach ($example in $help.Examples.Example) {
            $text = @($example.Code; $example.Introduction.Text; $example.Remarks.Text) -join "`n"
            $blocks = @([regex]::Matches($text, '(?ms)^```powershell\s*\r?\n(.*?)^```') | ForEach-Object { $_.Groups[1].Value })
            if ($blocks.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace($example.Code)) { $blocks = @($example.Code) }
            $blocks.Count | Should -BeGreaterThan 0 -Because "$CommandName $($example.Title) needs runnable code"
            foreach ($code in $blocks) {
                $tokens = $null; $parseErrors = $null
                $null = [Management.Automation.Language.Parser]::ParseInput($code, [ref]$tokens, [ref]$parseErrors)
                $parseErrors | Should -BeNullOrEmpty -Because "$CommandName $($example.Title) must parse"
            }
        }
        foreach ($parameter in $command.Parameters.Keys | Where-Object { $_ -notin $commonParameters }) {
            $description = (Get-Help "Fullscreen\$CommandName" -Parameter $parameter).Description.Text -join ' '
            $description | Should -Not -BeNullOrEmpty -Because "$CommandName -$parameter needs help"
            $description | Should -Not -Match '\{\{'
        }
    }
}
