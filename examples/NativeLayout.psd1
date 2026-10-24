# Portable layout data. Load with Import-TuiDefinition or Import-PowerShellDataFile.
# Callbacks and live objects are supplied separately by your .ps1 script.
@{
    Version = 1
    Title = 'Native PowerShell layout'
    State = @{
        name = 'World'
        load = 50.0
    }
    Root = @{
        Id = 'root'
        Type = 'VStack'
        Spacing = 1
        Children = @(
            @{
                Id = 'heading'
                Type = 'Text'
                Text = 'A TUI from PowerShell data'
                Foreground = '#58D5E6'
                Bold = $true
            }
            @{
                Id = 'name'
                Type = 'TextBox'
                Bind = 'name'
            }
            @{
                Id = 'chart'
                Type = 'Chart'
                Bind = 'load'
                Text = 'Load %'
                GraphKind = 'Area'
                Maximum = 100
                Height = 8
            }
            @{
                Id = 'accept'
                Type = 'Button'
                Text = 'Accept'
                Close = $true
            }
        )
    }
}
