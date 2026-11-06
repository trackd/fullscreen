BeforeDiscovery {
    $manifest = Import-PowerShellDataFile (Join-Path $PSScriptRoot '../module/fullscreen.psd1')
    $commandCases = $manifest.CmdletsToExport | ForEach-Object { @{ CommandName = $_ } }
    $aliasCases = @(
        @{ AliasName = 'ftop'; CommandName = 'Show-SystemMonitor' }
        @{ AliasName = 'ffile'; CommandName = 'Show-FileManager' }
        @{ AliasName = 'fmc'; CommandName = 'Show-Commander' }
        @{ AliasName = 'fobject'; CommandName = 'Show-ObjectTable' }
        @{ AliasName = 'flog'; CommandName = 'Show-LogViewer' }
        @{ AliasName = 'fgit'; CommandName = 'Show-GitManager' }
        @{ AliasName = 'fgh'; CommandName = 'Show-GitHubManager' }
        @{ AliasName = 'fado'; CommandName = 'Show-AzureDevOpsManager' }
        @{ AliasName = 'fdesign'; CommandName = 'Show-TuiDesigner' }
        @{ AliasName = 'ftui'; CommandName = 'Invoke-Tui' }
    )
}

BeforeAll {
    $manifestPath = Join-Path $PSScriptRoot '../output/fullscreen.psd1'
    Import-Module $manifestPath -Force
}

Describe 'Module exports and packaging' {
    It 'Exports exactly the documented cmdlets' {
        (Test-ModuleManifest $manifestPath).ExportedCmdlets.Count | Should -Be 17
        @(Get-Command -Module fullscreen -CommandType Cmdlet).Count | Should -Be 17
    }

    It 'Exports <CommandName> through the private assembly context' -ForEach $commandCases {
        $command = Get-Command "Fullscreen\$CommandName"
        $context = [Runtime.Loader.AssemblyLoadContext]::GetLoadContext($command.ImplementingType.Assembly)
        $context.Name | Should -BeExactly 'fullscreen'
        if ($command.Parameters.ContainsKey('Theme')) {
            $command.Parameters['Minimal'].ParameterType | Should -Be ([switch])
            $line = "$CommandName -Theme "
            $completions = [Management.Automation.CommandCompletion]::CompleteInput($line, $line.Length, $null).CompletionMatches
            $completions.Count | Should -Be 20
            $completions.ListItemText | Should -Contain 'Dark Modern'
            $completions.ListItemText | Should -Contain 'Default'
            $line = "$CommandName -Theme 'Dark"
            [Management.Automation.CommandCompletion]::CompleteInput($line, $line.Length, $null).CompletionMatches.CompletionText | Should -Contain "'Dark Modern'"
        }
    }

    It 'Maps <AliasName> to <CommandName>' -ForEach $aliasCases {
        (Get-Command $AliasName).Definition | Should -Be $CommandName
    }

    It 'Supports themes and minimal mode on all ten interactive commands' {
        @(Get-Command -Module fullscreen -CommandType Cmdlet | Where-Object { $_.Parameters.ContainsKey('Theme') }).Count | Should -Be 11
    }

    It 'Keeps removed dependencies, compatibility copies and debug symbols out of the package' {
        $assembly = (Get-Command fullscreen\Show-SystemMonitor).ImplementingType.Assembly
        $context = [Runtime.Loader.AssemblyLoadContext]::GetLoadContext($assembly)
        $forbidden = '^(SkiaSharp|libSkiaSharp|TextMateSharp|Onigwrap|Microsoft.Identity.Client|Microsoft.IdentityModel.Abstractions|XenoAtom.Terminal.Graphics|XenoAtom.Terminal.UI.Graphics|XenoAtom.Terminal.UI.Extensions.CodeEditor.TextMateSharp|Microsoft.Diagnostics\.|Microsoft.Extensions.DependencyInjection.Abstractions|Microsoft.Extensions.Logging.Abstractions|TraceReloggerLib|Dia2Lib)'
        @($context.Assemblies | Where-Object { $_.GetName().Name -match $forbidden }).Count | Should -Be 0
        $packaged = @(Get-ChildItem (Join-Path $PSScriptRoot '../output/lib') -Recurse -File)
        @($packaged | Where-Object { $_.BaseName -match $forbidden -or $_.Extension -eq '.pdb' }).Count | Should -Be 0
        @($packaged | Where-Object Name -In 'System.Memory.dll', 'System.Buffers.dll', 'System.Runtime.CompilerServices.Unsafe.dll', 'PolySharp.dll').Count | Should -Be 0
        Join-Path $PSScriptRoot '../output/lib/compat' | Should -Not -Exist
        Join-Path $PSScriptRoot '../output/lib/AzureCli.ps1' | Should -Not -Exist
        Get-Command fullscreen\Show-CodeEditor -ErrorAction SilentlyContinue | Should -BeNullOrEmpty
        Get-TuiControl | Where-Object Name -EQ CodeEditor | Should -BeNullOrEmpty
    }

    It 'Uses current Azure CLI authentication parameters and rejects a missing CLI before opening a terminal' {
        $command = Get-Command fullscreen\Show-AzureDevOpsManager
        foreach ($name in 'Organization', 'Project', 'Team', 'StoryType', 'TenantId', 'ReadOnly', 'View', 'RefreshInterval', 'PageSize') {
            $command.Parameters.ContainsKey($name) | Should -BeTrue
        }
        foreach ($name in 'AccessToken', 'PersonalAccessToken', 'Interactive', 'UseAzureCli', 'ClientId', 'LoginHint') {
            $command.Parameters.ContainsKey($name) | Should -BeFalse
        }
        $originalPath = $env:PATH
        try {
            $env:PATH = ''
            { Show-AzureDevOpsManager -Organization fixture -Project fixture -ErrorAction Stop } | Should -Throw '*Azure CLI is required*'
        }
        finally { $env:PATH = $originalPath }
    }

    It 'Retains monitor parameter sets and optional table columns' {
        $command = Get-Command fullscreen\Show-SystemMonitor
        @($command.ParameterSets.Name | Sort-Object) -join ',' | Should -Be 'Computer,Local,Session'
        foreach ($name in 'Session', 'ComputerName', 'RefreshRate', 'Tree', 'Column', 'ExcludeColumn', 'NetworkColumn', 'ExcludeNetworkColumn', 'GpuColumn', 'ExcludeGpuColumn') {
            $command.Parameters.ContainsKey($name) | Should -BeTrue
        }

        $command.Parameters['Tree'].ParameterType | Should -Be ([switch])
        [Enum]::GetNames($command.Parameters['Column'].ParameterType.GetElementType()) | Should -Contain 'CpuTime'
    }

    It 'Completes Azure tab exclusions and rejects hiding all pages before authenticating' {
        $command = Get-Command fullscreen\Show-AzureDevOpsManager
        $command.Parameters['ExcludeTab'].ParameterType | Should -Be ([string[]])
        $line = 'Show-AzureDevOpsManager -ExcludeTab '
        $completionNames = [Management.Automation.CommandCompletion]::CompleteInput($line, $line.Length, $null).CompletionMatches.ListItemText
        foreach ($name in 'Boards', 'WorkItems', 'Stories', 'Tasks', 'Backlog', 'PullRequests', 'Pipelines') {
            $completionNames | Should -Contain $name
        }
        { Show-AzureDevOpsManager -Organization fixture -Project fixture -ExcludeTab Boards,WorkItems,Stories,Tasks,Backlog,PullRequests,Pipelines -ErrorAction Stop } |
            Should -Throw '*At least one tab must remain visible*'
    }

    It 'Retains the file manager path, selection and navigation parameters' {
        $command = Get-Command fullscreen\Show-FileManager
        foreach ($name in 'Path', 'ShowHidden', 'SortBy', 'Descending', 'NoPreview', 'PassThru', 'SetLocation') {
            $command.Parameters.ContainsKey($name) | Should -BeTrue
        }
    }

    It 'Remains usable after removing and re-importing the module' {
        Remove-Module fullscreen
        Import-Module $manifestPath -Force
        Get-Command fullscreen\Show-SystemMonitor | Should -Not -BeNullOrEmpty
        (Get-Help Show-SystemMonitor -Full).Synopsis | Should -Match 'system monitor'
    }
}
