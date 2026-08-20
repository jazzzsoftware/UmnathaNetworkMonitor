using FlaUI.Core.AutomationElements;
using FlaUI.Core.Patterns;

namespace NetworkMonitor.UITests.Driving
{
    // Reads a CommunityToolkit.WinUI DataGrid through the UIA Grid and GridItem patterns rather
    // than by enumerating children, because a virtualising panel only realises the rows currently
    // in (or scrolled through) the viewport — an unrealised row is invisible to UIA even though
    // GridPattern.RowCount still reports it. RowCount is read straight off the pattern for that
    // reason; CellText scrolls the target row into view before reading it.
    //
    // UNVERIFIED: this has not been run against the real app (not installed on this machine — see
    // the Task 6 report). Whether CommunityToolkit's DataGrid actually needs the scroll step, and
    // whether IScrollItemPattern sits on the row or has to be reached some other way, is exactly
    // what the spec's risk table asks to be confirmed once the suite can drive the app for real.
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

            AutomationElement anchorCell = gridPattern.GetItem(row, 0);
            AutomationElement containingRow = anchorCell.Parent;

            IScrollItemPattern? scrollItemPattern = containingRow.Patterns.ScrollItem.PatternOrDefault;

            if (scrollItemPattern is not null)
            {
                scrollItemPattern.ScrollIntoView();
            }

            // Re-fetched rather than reusing anchorCell/containingRow: scrolling a virtualised
            // panel can recycle the automation elements it just realised, so the reference taken
            // before the scroll is not trustworthy afterwards.
            AutomationElement realisedCell = gridPattern.GetItem(row, column);
            string cellText = realisedCell.Name;

            return cellText;
        }

        // WinUI's ToolTipService is documented to publish an element's tooltip content through UI
        // Automation's HelpText property, which is how AllDevicesPage/ApprovedDevicesPage/
        // UnapprovedDevicesPage attach a device's Notes to its Name-column cell (NotesToolTipConverter
        // returns null — no ToolTip object, hence no HelpText — for a device with no notes).
        //
        // UNVERIFIED, same caveat as CellText above: never run against the real app. Whether the
        // CommunityToolkit DataGrid's cell-level automation peer actually forwards the inner
        // TextBlock's HelpText up to the cell GridPattern.GetItem returns, rather than leaving it
        // on a descendant, is exactly what running this suite for real would confirm. ValueOrDefault
        // returns empty rather than throwing if HelpText is unsupported on this element.
        public static string CellHelpText(AutomationElement grid, int row, int column)
        {
            IGridPattern gridPattern = grid.Patterns.Grid.Pattern;
            AutomationElement cell = gridPattern.GetItem(row, column);
            string helpText = cell.Properties.HelpText.ValueOrDefault ?? string.Empty;

            return helpText;
        }
    }
}
