using CommunityToolkit.WinUI.UI.Controls;
using NetworkMonitor.Services.Data;

namespace NetworkMonitor.Views
{
    internal static class DeviceGridSort
    {
        public static void RegisterDeviceColumns(Dictionary<DataGridColumn, string> sortPaths, DataGrid grid)
        {
            sortPaths[grid.Columns[0]] = "IsOnline";
            sortPaths[grid.Columns[1]] = "Type";
            sortPaths[grid.Columns[2]] = "DisplayName";
            sortPaths[grid.Columns[3]] = "IpAddress";
            sortPaths[grid.Columns[4]] = "MacAddress";
            sortPaths[grid.Columns[5]] = "Vendor";
        }

        public static void ApplyIndicator(DataGrid grid, Dictionary<DataGridColumn, string> sortPaths, string sortProperty, bool sortAscending)
        {

            foreach (DataGridColumn column in grid.Columns)
            {
                bool isSort = sortPaths.TryGetValue(column, out string? path) && path == sortProperty;
                column.SortDirection = isSort
                    ? sortAscending ? DataGridSortDirection.Ascending : DataGridSortDirection.Descending
                    : null;
            }

        }

        public static void HandleSorting(DataGridColumnEventArgs args, DataGrid grid, Dictionary<DataGridColumn, string> sortPaths, string? pageKey, Action<string, bool> sort)
        {

            if (sortPaths.TryGetValue(args.Column, out string? property))
            {
                bool ascending = args.Column.SortDirection != DataGridSortDirection.Ascending;

                foreach (DataGridColumn column in grid.Columns)
                {
                    column.SortDirection = null;
                }

                args.Column.SortDirection = ascending ? DataGridSortDirection.Ascending : DataGridSortDirection.Descending;
                sort(property, ascending);

                if (pageKey is not null)
                {
                    new SortPreference(property, ascending).Save(pageKey);
                }

            }

        }
    }
}
