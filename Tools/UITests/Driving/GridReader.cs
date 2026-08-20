using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;

namespace NetworkMonitor.UITests.Driving
{
    // Reads a CommunityToolkit.WinUI DataGrid through the UIA Grid and GridItem patterns rather
    // than by enumerating children, because a virtualising panel only realises the rows currently
    // in (or scrolled through) the viewport — an unrealised row is invisible to UIA even though
    // GridPattern.RowCount still reports it. RowCount is read straight off the pattern for that
    // reason; CellText scrolls the target row into view before reading it.
    //
    // Fix round 1 (2026-08-20): confirmed live against a DataGridTemplateColumn cell
    // (AllDevicesGrid) that GridPattern.GetItem(row, column) returns the cell's own generic
    // "Custom" UIA peer, and reading .Name directly on it throws — "The requested property 'Name
    // [#30005]' is not supported" — aborting the whole run on the very first cell read. The
    // template's actual visible text lives on a nested Text child instead (the Name column has
    // two: the device name, then the "Private MAC"/"Host" badge, in that document order — the
    // first is always the value). A DataGridTextColumn cell (Vendor, Model) is itself a Text
    // element already carrying the right Name, and — confirmed in the same tree dump — also has
    // its own nested Text child with the same value, so searching for the first Text descendant
    // reads both column kinds correctly through one code path.
    public static class GridReader
    {
        public static int RowCount(AutomationElement grid)
        {
            IGridPattern gridPattern = grid.Patterns.Grid.Pattern;

            int rowCount = gridPattern.RowCount.Value;

            return rowCount;
        }

        public static string CellText(AutomationElement grid, int row, int column)
        {
            IGridPattern gridPattern = grid.Patterns.Grid.Pattern;

            ScrollRowIntoView(grid, row);

            // Re-fetched rather than reused from inside ScrollRowIntoView: scrolling a virtualised
            // panel can recycle the automation elements it just realised, so a reference taken
            // before the scroll is not trustworthy afterwards.
            AutomationElement realisedCell = gridPattern.GetItem(row, column);
            string cellText = ReadCellDisplayName(realisedCell);

            return cellText;
        }

        // WinUI's ToolTipService attaches a device's Notes to the same TextBlock that carries the
        // Name column's display text (AllDevicesPage.xaml:168-171: one TextBlock with both
        // Text="{x:Bind DisplayName}" and ToolTipService.ToolTip="{x:Bind Notes, ...}"), so — once
        // CellText's fix-round-1 finding is accounted for — the HelpText lives on that same nested
        // Text descendant, not on the outer cell wrapper CellText itself used to (wrongly) read
        // Name from. ValueOrDefault returns empty rather than throwing if HelpText is unsupported
        // on whichever element is actually read.
        public static string CellHelpText(AutomationElement grid, int row, int column)
        {
            IGridPattern gridPattern = grid.Patterns.Grid.Pattern;

            ScrollRowIntoView(grid, row);

            AutomationElement cell = gridPattern.GetItem(row, column);
            AutomationElement textElement = FindTextDescendantOrSelf(cell);
            string helpText = textElement.Properties.HelpText.ValueOrDefault ?? string.Empty;

            return helpText;
        }

        // Fix round 1 (2026-08-20): DevicesPhase's row-action buttons (Edit, Delete) were clicked
        // without this — GetItem still returns an element for an unrealised, off-screen row, but
        // FlaUI's Click() lands on that element's (stale or degenerate) on-screen bounding
        // rectangle, not on the actual control, silently clicking nothing or the wrong thing. Every
        // row-scoped read or action in this class and its callers should scroll first; this is the
        // one place that logic lives.
        public static void ScrollRowIntoView(AutomationElement grid, int row)
        {
            IGridPattern gridPattern = grid.Patterns.Grid.Pattern;
            AutomationElement anchorCell = gridPattern.GetItem(row, 0);
            AutomationElement containingRow = anchorCell.Parent;
            IScrollItemPattern? scrollItemPattern = containingRow.Patterns.ScrollItem.PatternOrDefault;

            if (scrollItemPattern is not null)
            {
                scrollItemPattern.ScrollIntoView();
            }

        }

        private static string ReadCellDisplayName(AutomationElement cell)
        {
            AutomationElement textElement = FindTextDescendantOrSelf(cell);
            string text = textElement.Name;

            return text;
        }

        private static AutomationElement FindTextDescendantOrSelf(AutomationElement cell)
        {
            AutomationElement? textDescendant = cell.FindFirstDescendant(conditionFactory => conditionFactory.ByControlType(ControlType.Text));
            AutomationElement resolved = textDescendant ?? cell;

            return resolved;
        }
    }
}
