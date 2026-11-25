# Optional integrations

The built-in apps need no other PowerShell modules. These are used when present;
executables must be on `PATH`.

| Dependency | Used by | Setup | Without it |
| --- | --- | --- | --- |
| [TextMate](https://www.powershellgallery.com/packages/TextMate) module | `Code` control; source and Markdown previews | `Install-Module TextMate -Scope CurrentUser` | Source shows as plain text; the `Code` control shows an install notice. |
| [Sixel](https://www.powershellgallery.com/packages/Sixel) module | Image previews in the file browser | `Install-Module Sixel -Scope CurrentUser`, plus a Sixel-capable terminal | A notice explains what is missing. Charts are unaffected. |
| [`git`](https://git-scm.com/) | [Show-GitManager](../en-US/Show-GitManager.md) | Install Git. | Git views are unavailable. |
| [GitHub CLI](https://cli.github.com/) (`gh`) | [Show-GitHubManager](../en-US/Show-GitHubManager.md) | Install `gh`, then `gh auth login`. | GitHub views are unavailable. |
| [Azure CLI](https://learn.microsoft.com/cli/azure/) (`az`) | [Show-AzureDevOpsManager](../en-US/Show-AzureDevOpsManager.md) | `az extension add --name azure-devops`, then `az login`. | Azure DevOps views are unavailable. |

Spectre controls display renderables created by the caller; load the module that
produces them, such as TextMate, first.
