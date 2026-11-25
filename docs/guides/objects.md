# Object exploration

See [Show-ObjectTable](../en-US/Show-ObjectTable.md) for parameters and pipeline examples.

Collects pipeline input and opens it in an interactive table.
Formatted uses the object's normal PowerShell table formatting. Inspect shows
the selected object's property kinds, short types, values and highlighted method declarations,
or collection items and dictionary entries. JSON shows a highlighted snapshot of the current object.
Browsing methods never invokes them. Circular references,
depth limits and failing properties are marked rather than causing errors. Switch
tabs with Tab, Shift+Tab, 1-3 or the tab headers.

Enter or O browses the selected object's properties or collection items in the
main Formatted view. I opens the separate Inspect/Details view. Backspace,
Escape or Back returns to the parent with its selection, filters, sort and scroll
restored. Filter with / using
plain text, wildcard: or regex: patterns; sort by clicking a column or with S,
and reverse with R. G groups, C chooses columns, Space marks rows, and M, F10 or
right-click opens the menu. Use F or right-click a column header for a
case-insensitive column filter.
Column filters combine with AND and with the global search. Headers show ▾ when
filtering is available and ◆ when a filter is active. Clear removes the current
column filter; F also offers Clear column filters. Escape clears all filters
before returning to a parent node. Filters are retained when returning from an
inspected child or switching views. In JSON, Up/Down navigates lines, Left collapses
the current object/array or its parent, Right expands, and Enter/Space toggles a
branch. Click ▾/▸ in the gutter to fold with the mouse. Fold state is retained for
the latest 16 JSON snapshots. Search with / expands folded content to find hidden
values; Enter during search or N finds the next match. Drag or use Shift+arrows
to select JSON text. Ctrl+A/C copies the complete original JSON, including folded
branches. Truncated JSON remains available as plain text.
Values are colored by type, and ANSI colors in displayed values are kept.

With PassThru, Enter in the root Formatted table returns the marked objects or the current selection; Q
cancels. O still browses the current object in that mode. Displaying and serializing objects runs their property getters, which can
execute custom code.
