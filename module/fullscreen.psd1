@{
    RootModule           = 'fullscreen.psm1'
    ModuleVersion        = '0.4.0'
    GUID                 = 'f3fb06ad-95c3-47ae-9469-0cc3bb5f8112'
    Author               = 'trackd'
    Copyright            = 'Copyright (c) 2026 trackd. Licensed under MIT.'
    Description          = 'fullscreen terminal apps, visual TUI designer and custom PowerShell TUIs.'
    PowerShellVersion    = '7.6'
    CompatiblePSEditions = @('Core')
    FunctionsToExport    = @()
    FormatsToProcess     = @('fullscreen.format.ps1xml')
    CmdletsToExport      = @(
        'Show-SystemMonitor'
        'Show-FileManager'
        'Show-Commander'
        'Show-ObjectTable'
        'Out-Chart'
        'Show-LogViewer'
        'Show-GitManager'
        'Show-GitHubManager'
        'Show-AzureDevOpsManager'
        'Show-TuiDesigner'
        'Invoke-Tui'
        'Get-FullscreenTheme'
        'New-TuiDefinition'
        'Import-TuiDefinition'
        'Export-TuiDefinition'
        'Get-TuiControl'
        'New-TuiControl'
    )
    AliasesToExport      = @(
        'ftop'
        'ffile'
        'fmc'
        'fobject'
        'flog'
        'fgit'
        'fgh'
        'fado'
        'fdesign'
        'ftui'
    )
    VariablesToExport    = @()
    PrivateData          = @{
        PSData = @{
            Tags       = @(
                'TUI'
                'Performance'
                'CrossPlatform'
            )
            LicenseUri = 'https://github.com/trackd/fullscreen/blob/main/LICENSE'
            ProjectUri = 'https://github.com/trackd/fullscreen'
        }
    }
}
