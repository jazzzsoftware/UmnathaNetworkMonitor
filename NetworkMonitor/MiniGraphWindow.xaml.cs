using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NetworkMonitor.Core.Widget;
using NetworkMonitor.Models.Widget;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.ViewModels;
using Windows.Graphics;

namespace NetworkMonitor
{
    public sealed partial class MiniGraphWindow : Window
    {
        private const int GwlExStyle = -20;
        private const long WsExToolWindow = 0x00000080;
        private const long WsExLayered = 0x00080000;
        private const uint LwaAlpha = 0x00000002;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaExtendedFrameBounds = 9;
        private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
        private const int DwmwaColorDefault = unchecked((int)0xFFFFFFFF);
        private const int MinimumWidth = 240;
        private const int MinimumHeight = 120;
        private const int EdgeMargin = 16;
        // AppWindow works in physical pixels, every size above is in DIPs.
        private const double DefaultDpi = 96.0;
        private const uint MonitorDefaultToNearest = 2;
        private const int MdtEffectiveDpi = 0;
        private const double ReferenceWidth = 320.0;
        private const double ReferenceHeight = 220.0;
        // The sizes chosen in Settings are the sizes at the widget's minimum, so nothing shrinks below
        // them: scaling only ever grows the text from there. Letting the scale fall under one made a
        // small widget — or a large one carrying all four sections — illegible.
        private const double MinimumFontScale = 1.0;
        private const double MaximumFontScale = 2.0;
        private const double FooterFontSize = 11.0;
        private const double DragThreshold = 4.0;
        private static readonly TimeSpan HoverRiseDelay = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan HoverFallDelay = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan OpacityFadeDuration = TimeSpan.FromMilliseconds(120);
        private static readonly TimeSpan AlphaStepInterval = TimeSpan.FromMilliseconds(16);
        private const int AlphaStepPercent = 5;

        private readonly MiniGraphState _state;
        private readonly Settings _settings;
        private readonly DispatcherTimer _savePlacementTimer;
        private readonly DispatcherTimer _hoverRiseTimer;
        private readonly DispatcherTimer _hoverFallTimer;
        private readonly DispatcherTimer _alphaFadeTimer;
        private readonly IntPtr _hwnd;
        private readonly TaskbarTopmostGuard _topmostGuard;
        private MiniGraphOrientation _appliedOrientation;
        private bool _placementRestored;
        private bool _pointerDown;
        private bool _dragging;
        private bool _pointerCaptured;
        private bool _pointerInside;
        private bool _teardownStarted;
        private int _alphaPercent = 100;
        private int _targetAlphaPercent = 100;
        private int _dragOffsetX;
        private int _dragOffsetY;
        private int _dragStartX;
        private int _dragStartY;

        public MiniGraphWindow(MiniGraphViewModel viewModel, MiniGraphState state, Settings settings)
        {
            ViewModel = viewModel;
            _state = state;
            _settings = settings;
            InitializeComponent();

            CloseGlyph.OpacityTransition = new ScalarTransition
            {
                Duration = OpacityFadeDuration
            };

            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _topmostGuard = new TaskbarTopmostGuard(_hwnd);

            _savePlacementTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _savePlacementTimer.Tick += OnSavePlacementTimerTick;

            _hoverRiseTimer = new DispatcherTimer
            {
                Interval = HoverRiseDelay
            };
            _hoverRiseTimer.Tick += OnHoverRiseTick;

            _hoverFallTimer = new DispatcherTimer
            {
                Interval = HoverFallDelay
            };
            _hoverFallTimer.Tick += OnHoverFallTick;

            // A layered window's alpha is a single Win32 call with no animation behind it, so the
            // fade the XAML OpacityTransition used to give for free has to be stepped by hand.
            _alphaFadeTimer = new DispatcherTimer
            {
                Interval = AlphaStepInterval
            };
            _alphaFadeTimer.Tick += OnAlphaFadeTick;

            _appliedOrientation = _state.Orientation;

            ConfigureWindow();
            ApplyLayout();

            // Snap rather than fade on the way in: the widget should appear at its resting opacity,
            // not brighten into it.
            _targetAlphaPercent = _state.Opacity;

            SetWindowAlpha(_state.Opacity);
            ApplyUnknownDevicesBrush();
            ApplySpeedTestText();
            RestorePlacement();

            _state.Changed += OnStateChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            AppWindow.Changed += OnAppWindowChanged;
            Closed += OnWindowClosed;
        }

        public MiniGraphViewModel ViewModel
        {
            get;
        }

        public void ShowWidget()
        {

            // An orientation change that arrived while the widget was hidden was deliberately not
            // applied then, to avoid moving a hidden window. This is where that debt is settled,
            // before the window becomes visible, so it is never seen in the wrong layout.
            if (_appliedOrientation != _state.Orientation)
            {
                _appliedOrientation = _state.Orientation;

                ApplyLayout();
                RestorePlacement();
            }

            ViewModel.Attach();
            SetChartsLive(true);
            AppWindow.Show();
        }

        public void HideWidget()
        {
            FlushPlacement();
            _savePlacementTimer.Stop();
            _hoverRiseTimer.Stop();
            _hoverFallTimer.Stop();
            _alphaFadeTimer.Stop();
            ViewModel.Detach();

            // AppWindow.Hide leaves the XAML tree loaded, so the charts keep their per-frame
            // rendering hook. Only IsLive stops them redrawing a widget nobody can see.
            SetChartsLive(false);
            AppWindow.Hide();
        }

        public void CloseWidget()
        {
            Teardown();
            Close();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out NativeRect value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {

            // The widget carries a resize border, so Alt+F4 destroys it behind the app's back. Without
            // this the tray item, the toolbar toggle and Settings all keep reporting it as visible and
            // the next show call lands on a dead window.
            if (!_teardownStarted)
            {
                Teardown();
                App.ForgetMiniGraph();
                _state.IsVisible = false;
            }

        }

        private void Teardown()
        {
            _teardownStarted = true;

            try
            {
                FlushPlacement();
                AppWindow.Changed -= OnAppWindowChanged;
            }
            catch (Exception exception)
            {
                // Alt+F4 reaches here after the window is destroyed, so AppWindow may already be gone.
                AppLog.Error("MiniGraphWindow.Teardown", exception);
            }

            _savePlacementTimer.Stop();
            _hoverRiseTimer.Stop();
            _hoverFallTimer.Stop();
            _alphaFadeTimer.Stop();
            _topmostGuard.Dispose();
            _state.Changed -= OnStateChanged;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Detach();
        }

        // Placement is written on a 400 ms debounce, so a drag followed straight away by a hide or an
        // exit would stop the timer before it ever fired and lose the new position.
        private void FlushPlacement()
        {

            if (_placementRestored && _savePlacementTimer.IsEnabled)
            {
                SaveCurrentPlacement();
            }

        }

        // The size is stored in DIPs so the widget keeps the same apparent size across displays of
        // different scaling; the position stays in physical pixels because that is the coordinate
        // space of the virtual desktop that DisplayArea and AppWindow both work in.
        private void SaveCurrentPlacement()
        {
            double scale = GetCurrentScale();
            SizeInt32 size = AppWindow.Size;
            int width = (int)Math.Round(size.Width / scale);
            int height = (int)Math.Round(size.Height / scale);
            PointInt32 position = AppWindow.Position;

            if (_appliedOrientation == MiniGraphOrientation.Horizontal)
            {
                _state.SaveStripPlacement(position.X, position.Y, height);
            }
            else if (width >= MinimumWidth && height >= MinimumHeight)
            {
                _state.SavePlacement(position.X, position.Y, width, height);
            }

        }

        private double GetCurrentScale()
        {
            uint dpi = GetDpiForWindow(_hwnd);
            double scale = dpi > 0 ? dpi / DefaultDpi : 1.0;

            return scale;
        }

        // The widget is placed before it is ever shown, so its own window cannot be asked for a DPI
        // yet — the display that the restored position lands on has to be measured directly.
        private static double GetScaleForPoint(int positionX, int positionY)
        {
            NativePoint point = new NativePoint
            {
                X = positionX,
                Y = positionY
            };

            IntPtr monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
            double scale = 1.0;

            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out uint dpiY) == 0 && dpiX > 0)
            {
                scale = dpiX / DefaultDpi;
            }

            return scale;
        }

        private void SetChartsLive(bool isLive)
        {
            InternetSection.IsLive = isLive;
            LocalSection.IsLive = isLive;
        }

        // Every size the widget can be dragged to is a legitimate one, and text fixed at 12 point looks
        // cramped at 600 wide and swamps the charts at 240. The reference size is the default placement.
        // Horizontal takes its scale from the height alone: the strip's width grows with every section
        // switched on, so a width term would inflate the text as sections were added.
        private void SectionsPanelSizeChanged(object sender, SizeChangedEventArgs args)
        {
            double scale;

            if (_state.IsHorizontal)
            {
                scale = HorizontalStripMetrics.FontScale(args.NewSize.Height);
            }
            else
            {
                double widthScale = args.NewSize.Width / ReferenceWidth;
                double heightScale = args.NewSize.Height / ReferenceHeight;

                scale = Math.Clamp(Math.Min(widthScale, heightScale), MinimumFontScale, MaximumFontScale);
            }

            InternetSection.FontScale = scale;
            LocalSection.FontScale = scale;
            SpeedTestLine.FontSize = FooterFontSize * scale;
            UnknownDevicesLine.FontSize = FooterFontSize * scale;
            CloseGlyph.FontSize = FooterFontSize * scale;

            bool showPeak = ComputeShowPeak(args.NewSize.Height);

            InternetSection.ShowPeak = showPeak;
            LocalSection.ShowPeak = showPeak;
        }

        private bool ComputeShowPeak(double height)
        {
            bool showPeak = !_state.IsHorizontal || HorizontalStripMetrics.ShowsPeak(height);

            return showPeak;
        }

        private void ConfigureWindow()
        {
            AppWindow.IsShownInSwitchers = false;
            Title = "Umnatha mini graph";

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, false);
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = true;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            // WS_EX_LAYERED is what makes the opacity setting mean anything. Fading the XAML root
            // instead only blends the content towards the window's own opaque surface, which is why
            // 50% looked like a dimmed widget rather than a see-through one — there was nothing
            // behind the content to show. With the style set, DWM composites the whole window,
            // charts included, against whatever is underneath it.
            long exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
            exStyle |= WsExToolWindow | WsExLayered;

            SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle));

            int cornerPreference = DwmwcpRound;

            DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

            ApplyBorderVisibility();
        }

        // The frame itself always stays: it is what gives the window its resize edges, and dragging the
        // top or bottom edge is how the strip's height is set. Only its paint is optional, and
        // DWMWA_COLOR_NONE removes that while leaving the frame's hit-testing untouched. Both values
        // need Windows 11 22000+; older builds fail the call and keep the default border, exactly as
        // the corner preference does.
        private void ApplyBorderVisibility()
        {
            int borderColor = _state.ShowBorder ? DwmwaColorDefault : DwmwaColorNone;

            DwmSetWindowAttribute(_hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));
        }

        private void RestorePlacement()
        {
            bool horizontal = _state.IsHorizontal;
            int positionX = horizontal ? _settings.MiniGraphStripX : _settings.MiniGraphX;
            int positionY = horizontal ? _settings.MiniGraphStripY : _settings.MiniGraphY;
            DisplayArea? saved = null;

            if (positionX != int.MinValue && positionY != int.MinValue)
            {
                // Nearest rather than None: a widget dragged a few pixels past a screen edge saves a
                // position that is inside no display at all, and None returns null for it. That sent
                // the restore down the never-placed path, which repositioned to the work area's
                // bottom-right corner — so a strip parked on the taskbar came back above it, and the
                // saved position was lost. Nearest resolves the display anyway, and the existing clamp
                // below pulls the position back on-screen while keeping where the user put it.
                saved = DisplayArea.GetFromPoint(new PointInt32(positionX, positionY), DisplayAreaFallback.Nearest);
            }

            DisplayArea target = saved ?? DisplayArea.Primary;
            RectInt32 workArea = target.WorkArea;

            // Without this the stored DIP size went to AppWindow verbatim, so on a 200% display the
            // widget came up at half the size it was asked for — small enough that the font scale
            // bottomed out and the sections were unreadable.
            int scaleSampleX = saved is null ? workArea.X : positionX;
            int scaleSampleY = saved is null ? workArea.Y : positionY;
            double scale = GetScaleForPoint(scaleSampleX, scaleSampleY);
            int width;
            int height;

            if (horizontal)
            {
                double heightInDips = HorizontalStripMetrics.ClampHeight(_settings.MiniGraphStripHeight);
                double fontScale = HorizontalStripMetrics.FontScale(heightInDips);
                double widthInDips = HorizontalStripMetrics.Width(_state.ShowInternet, _state.ShowLocal, _state.ShowSpeedTest, _state.ShowUnknownDevices, fontScale);

                width = (int)Math.Round(widthInDips * scale);
                height = (int)Math.Round(heightInDips * scale);
            }
            else
            {
                width = (int)Math.Round(Math.Max(MinimumWidth, _settings.MiniGraphWidth) * scale);
                height = (int)Math.Round(Math.Max(MinimumHeight, _settings.MiniGraphHeight) * scale);
            }

            if (saved is null)
            {
                int margin = (int)Math.Round(EdgeMargin * scale);

                positionX = workArea.X + workArea.Width - width - margin;
                positionY = workArea.Y + workArea.Height - height - margin;
            }

            // Sitting over the taskbar is the entire point of the horizontal strip, and the work
            // area excludes the taskbar by definition, so clamping the strip to it would push a
            // taskbar-docked position back up above the taskbar every time. The vertical widget is
            // a floating panel with no such intent, so it keeps clamping to the work area.
            RectInt32 bounds = horizontal ? target.OuterBounds : workArea;

            // The window carries an invisible resize border — 7px on left, right and bottom at 96 DPI —
            // that GetWindowRect and AppWindow both count but nobody can see. Clamping the raw rect to
            // the display therefore refused any position that put the *visible* strip flush with the
            // bottom of the screen, and dragged it up by exactly the overhang. Worse, the restore's own
            // MoveAndResize then triggered the debounced save, writing the clamped position back and
            // destroying the one the user chose. Growing the clamp by the insets keeps the test on what
            // is actually visible.
            RectInt32 clampArea = ExpandByFrameInsets(bounds);

            // Only the top-left corner was ever tested against a display, so a widget saved near a
            // right or bottom edge could come back mostly off-screen — and scaling the size on
            // restore makes that easier to hit, because the widget can now be wider than it was
            // when the position was written.
            int maximumX = Math.Max(clampArea.X, clampArea.X + clampArea.Width - width);
            int maximumY = Math.Max(clampArea.Y, clampArea.Y + clampArea.Height - height);

            positionX = Math.Clamp(positionX, clampArea.X, maximumX);
            positionY = Math.Clamp(positionY, clampArea.Y, maximumY);

            AppWindow.MoveAndResize(new RectInt32(positionX, positionY, width, height));
            _placementRestored = true;
        }

        // DWM is asked rather than GetSystemMetrics because the two disagree: the metrics come to 8 at
        // 96 DPI where the frame actually measures 7, and a clamp that is a pixel out is the whole
        // defect. A failed call leaves the area untouched, which is the behaviour this had before.
        private RectInt32 ExpandByFrameInsets(RectInt32 area)
        {
            RectInt32 expanded = area;

            if (DwmGetWindowAttribute(_hwnd, DwmwaExtendedFrameBounds, out NativeRect visible, Marshal.SizeOf<NativeRect>()) == 0
                && GetWindowRect(_hwnd, out NativeRect outer))
            {
                int left = visible.Left - outer.Left;
                int top = visible.Top - outer.Top;
                int right = outer.Right - visible.Right;
                int bottom = outer.Bottom - visible.Bottom;

                // A window that has never been composed can report a degenerate frame, and negative
                // insets would push the clamp inwards rather than out.
                if (left >= 0 && top >= 0 && right >= 0 && bottom >= 0)
                {
                    expanded = new RectInt32(
                        area.X - left,
                        area.Y - top,
                        area.Width + left + right,
                        area.Height + top + bottom);
                }

            }

            return expanded;
        }

        private void ApplyLayout()
        {
            InternetSection.Visibility = _state.ShowInternet ? Visibility.Visible : Visibility.Collapsed;
            LocalSection.Visibility = _state.ShowLocal ? Visibility.Visible : Visibility.Collapsed;
            SpeedTestBand.Visibility = _state.ShowSpeedTest ? Visibility.Visible : Visibility.Collapsed;
            UnknownDevicesBand.Visibility = _state.ShowUnknownDevices ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Visibility = _state.HasAnySection ? Visibility.Collapsed : Visibility.Visible;

            if (_state.IsHorizontal)
            {
                ApplyHorizontalLayout();
            }
            else
            {
                ApplyVerticalLayout();
            }

            bool showPeak = ComputeShowPeak(SectionsPanel.ActualHeight);

            InternetSection.ShowPeak = showPeak;
            LocalSection.ShowPeak = showPeak;

            ApplySpeedTestText();
        }

        private void ApplyVerticalLayout()
        {
            SectionsPanel.ColumnDefinitions.Clear();
            SectionsPanel.Padding = new Thickness(4, 4, 4, 0);
            InternetSection.MinHeight = 40;
            LocalSection.MinHeight = 40;

            GridLength fill = new GridLength(1, GridUnitType.Star);
            GridLength none = new GridLength(0);

            // The strips are fixed height and the charts share everything left over, so switching Local
            // off makes Internet twice as tall rather than shrinking the window. With both charts off
            // row 0 stays a star and acts as a spacer, otherwise the footer would pin to the top edge
            // with the empty space below it.
            bool spacerNeeded = !_state.ShowInternet && !_state.ShowLocal;

            SectionsPanel.RowDefinitions[0].Height = _state.ShowInternet || spacerNeeded ? fill : none;
            SectionsPanel.RowDefinitions[1].Height = _state.ShowLocal ? fill : none;
            SectionsPanel.RowDefinitions[2].Height = GridLength.Auto;
            SectionsPanel.RowDefinitions[3].Height = GridLength.Auto;

            Grid.SetRow(InternetSection, 0);
            Grid.SetColumn(InternetSection, 0);
            Grid.SetRow(LocalSection, 1);
            Grid.SetColumn(LocalSection, 0);
            Grid.SetRow(SpeedTestBand, 2);
            Grid.SetColumn(SpeedTestBand, 0);
            Grid.SetRow(UnknownDevicesBand, 3);
            Grid.SetColumn(UnknownDevicesBand, 0);

            Grid.SetRow(CloseGlyph, 0);
            Grid.SetRowSpan(CloseGlyph, 4);
            Grid.SetColumn(CloseGlyph, 0);
            CloseGlyph.HorizontalAlignment = HorizontalAlignment.Right;
            CloseGlyph.VerticalAlignment = VerticalAlignment.Top;
            CloseGlyph.Margin = new Thickness(0, 2, 2, 0);

            InternetSection.Margin = new Thickness(0, 0, 0, 4);
            LocalSection.Margin = new Thickness(0, 0, 0, 4);
            SpeedTestBand.Margin = new Thickness(0, 0, 0, 4);
            UnknownDevicesBand.Margin = new Thickness(0, 0, 0, 4);

            SpeedTestLine.HorizontalAlignment = HorizontalAlignment.Stretch;
            SpeedTestLine.VerticalAlignment = VerticalAlignment.Stretch;
            SpeedTestLine.TextAlignment = TextAlignment.Left;
            UnknownDevicesLine.HorizontalAlignment = HorizontalAlignment.Stretch;
            UnknownDevicesLine.VerticalAlignment = VerticalAlignment.Stretch;
            UnknownDevicesLine.TextAlignment = TextAlignment.Left;
        }

        // Every visible section takes a column of its own natural width, in the same order the vertical
        // widget stacks them, and the close glyph takes a narrow trailing column. Left floating over the
        // top-right corner it would land on the unknown-devices text: the 26px right reserve inside
        // MiniTrafficSection's header does not apply to the plain Border bands.
        private void ApplyHorizontalLayout()
        {
            SectionsPanel.ColumnDefinitions.Clear();
            SectionsPanel.Padding = new Thickness(4);
            InternetSection.MinHeight = 0;
            LocalSection.MinHeight = 0;

            GridLength single = new GridLength(1, GridUnitType.Star);

            SectionsPanel.RowDefinitions[0].Height = single;
            SectionsPanel.RowDefinitions[1].Height = new GridLength(0);
            SectionsPanel.RowDefinitions[2].Height = new GridLength(0);
            SectionsPanel.RowDefinitions[3].Height = new GridLength(0);

            int column = 0;

            column = PlaceHorizontalCell(InternetSection, _state.ShowInternet, column, HorizontalStripMetrics.InternetCellWidth);
            column = PlaceHorizontalCell(LocalSection, _state.ShowLocal, column, HorizontalStripMetrics.LocalCellWidth);
            column = PlaceHorizontalCell(SpeedTestBand, _state.ShowSpeedTest, column, HorizontalStripMetrics.SpeedCellWidth);
            column = PlaceHorizontalCell(UnknownDevicesBand, _state.ShowUnknownDevices, column, HorizontalStripMetrics.UnknownDevicesCellWidth);

            SectionsPanel.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            Grid.SetRow(CloseGlyph, 0);
            Grid.SetRowSpan(CloseGlyph, 1);
            Grid.SetColumn(CloseGlyph, column);
            CloseGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            CloseGlyph.VerticalAlignment = VerticalAlignment.Center;
            CloseGlyph.Margin = new Thickness(0);

            SpeedTestLine.HorizontalAlignment = HorizontalAlignment.Center;
            SpeedTestLine.VerticalAlignment = VerticalAlignment.Center;
            SpeedTestLine.TextAlignment = TextAlignment.Center;
            UnknownDevicesLine.HorizontalAlignment = HorizontalAlignment.Center;
            UnknownDevicesLine.VerticalAlignment = VerticalAlignment.Center;
            UnknownDevicesLine.TextAlignment = TextAlignment.Center;
        }

        private int PlaceHorizontalCell(FrameworkElement cell, bool isVisible, int column, double nominalWidth)
        {
            int next = column;
            int cellColumn = isVisible ? column : 0;

            if (isVisible)
            {
                SectionsPanel.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(nominalWidth, GridUnitType.Star)
                });

                next = column + 1;
            }

            Grid.SetRow(cell, 0);
            Grid.SetColumn(cell, cellColumn);
            cell.Margin = isVisible ? new Thickness(0, 0, 4, 0) : new Thickness(0);

            return next;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {

            if (args.PropertyName is null || args.PropertyName == nameof(MiniGraphViewModel.HasUnknownDevices))
            {
                DispatcherQueue.TryEnqueue(ApplyUnknownDevicesBrush);
            }

            // The label half of this line is a bold Run and the readings are a second Run, so the text
            // is assigned here rather than bound: a Run is not a FrameworkElement and carries no
            // binding of its own.
            if (args.PropertyName is null
                || args.PropertyName == nameof(MiniGraphViewModel.SpeedTestText)
                || args.PropertyName == nameof(MiniGraphViewModel.SpeedTestShortText))
            {
                DispatcherQueue.TryEnqueue(ApplySpeedTestText);
            }

        }

        private void ApplySpeedTestText()
        {
            SpeedTestLabel.Text = _state.IsHorizontal ? "Speed" : "Speed Test";
            SpeedTestDetail.Text = _state.IsHorizontal ? ViewModel.SpeedTestShortText : ViewModel.SpeedTestText;
        }

        private void ApplyUnknownDevicesBrush()
        {
            string resourceKey = ViewModel.HasUnknownDevices ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush";

            if (Application.Current.Resources.TryGetValue(resourceKey, out object? resource) && resource is Brush brush)
            {
                UnknownDevicesLine.Foreground = brush;
            }

        }

        private void OnStateChanged(object? sender, EventArgs args)
        {
            DispatcherQueue.TryEnqueue(OnStateChangedOnUiThread);
        }

        private void OnStateChangedOnUiThread()
        {

            if (_appliedOrientation != _state.Orientation)
            {
                _savePlacementTimer.Stop();

                // Moving a hidden window risks surfacing it, and a hidden XAML island may not run a
                // layout pass — which would leave the font scale from the orientation being left,
                // because SectionsPanelSizeChanged is its only writer. _appliedOrientation is left
                // stale on purpose: it is what tells ShowWidget the relayout is still owed.
                if (_state.IsVisible)
                {
                    _appliedOrientation = _state.Orientation;

                    ApplyLayout();
                    RestorePlacement();
                }

            }
            else
            {
                ApplyLayout();
                ClampMinimumSize();
            }

            ApplyRestingOpacity();
            ApplyBorderVisibility();
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {

            if (_placementRestored && (args.DidPositionChange || args.DidSizeChange))
            {
                ClampMinimumSize();
                _savePlacementTimer.Stop();
                _savePlacementTimer.Start();
            }

        }

        private double DerivedStripWidth()
        {
            double height = AppWindow.Size.Height / GetCurrentScale();
            double clampedHeight = HorizontalStripMetrics.ClampHeight(height);
            double fontScale = HorizontalStripMetrics.FontScale(clampedHeight);
            double width = HorizontalStripMetrics.Width(_state.ShowInternet, _state.ShowLocal, _state.ShowSpeedTest, _state.ShowUnknownDevices, fontScale);

            return width;
        }

        private void ClampMinimumSize()
        {

            if (_state.IsHorizontal)
            {
                ClampStripSize();
            }
            else
            {
                ClampWidgetSize();
            }

        }

        // Width is derived from the visible sections rather than dragged, and the presenter cannot lock
        // one axis while leaving the other free, so a side-edge drag is undone here on the next change.
        private void ClampStripSize()
        {
            double scale = GetCurrentScale();
            SizeInt32 size = AppWindow.Size;
            double heightInDips = HorizontalStripMetrics.ClampHeight(size.Height / scale);
            int height = (int)Math.Round(heightInDips * scale);
            int width = (int)Math.Round(DerivedStripWidth() * scale);

            if (size.Width != width || size.Height != height)
            {
                AppWindow.Resize(new SizeInt32(width, height));
            }

        }

        private void ClampWidgetSize()
        {
            double scale = GetCurrentScale();
            int minimumWidth = (int)Math.Round(MinimumWidth * scale);
            int minimumHeight = (int)Math.Round(MinimumHeight * scale);
            SizeInt32 size = AppWindow.Size;

            if (size.Width < minimumWidth || size.Height < minimumHeight)
            {
                int width = Math.Max(minimumWidth, size.Width);
                int height = Math.Max(minimumHeight, size.Height);

                AppWindow.Resize(new SizeInt32(width, height));
            }

        }

        private void OnSavePlacementTimerTick(object? sender, object args)
        {
            _savePlacementTimer.Stop();
            SaveCurrentPlacement();
        }

        private void RootPointerPressed(object sender, PointerRoutedEventArgs args)
        {

            if (args.GetCurrentPoint(RootLayer).Properties.IsLeftButtonPressed && GetCursorPos(out NativePoint cursor))
            {
                PointInt32 position = AppWindow.Position;

                _pointerDown = true;
                _dragging = false;
                _dragStartX = cursor.X;
                _dragStartY = cursor.Y;

                // The grab point is held as a fixed screen-space offset from the window's own origin,
                // in the physical pixels AppWindow.Move already speaks, so every move below is an
                // absolute target that needs no scale conversion and cannot drift.
                _dragOffsetX = cursor.X - position.X;
                _dragOffsetY = cursor.Y - position.Y;
            }

        }

        private void RootPointerMoved(object sender, PointerRoutedEventArgs args)
        {

            if (_pointerDown && GetCursorPos(out NativePoint cursor))
            {
                double threshold = DragThreshold * GetCurrentScale();

                // Below the threshold nothing moves, so a double-click still reaches the section
                // underneath instead of being eaten by a one-pixel drag.
                if (_dragging || Math.Abs(cursor.X - _dragStartX) > threshold || Math.Abs(cursor.Y - _dragStartY) > threshold)
                {

                    // Capturing on press would route every later pointer event here and the child
                    // sections would never complete their tap gestures, killing all four drill-ins.
                    // Capture only once the press has actually become a drag.
                    if (!_dragging)
                    {
                        _dragging = true;
                        _pointerCaptured = RootLayer.CapturePointer(args.Pointer);
                    }

                    // The live cursor and the press-time offset are both absolute screen positions, so
                    // the target never depends on where the window currently is. Deriving it from
                    // AppWindow.Position instead made the drag a feedback loop: the pointer in args was
                    // measured against the window as it stood when the input was sampled, while the
                    // position read here had already moved on, and that residue re-entered every
                    // iteration — visibly diverging at 200%, where each step travels twice as far.
                    AppWindow.Move(new PointInt32(cursor.X - _dragOffsetX, cursor.Y - _dragOffsetY));
                }

            }

        }

        private void RootPointerReleased(object sender, PointerRoutedEventArgs args)
        {
            _pointerDown = false;
            _dragging = false;

            if (_pointerCaptured)
            {
                _pointerCaptured = false;
                RootLayer.ReleasePointerCapture(args.Pointer);
            }

        }

        private void RootPointerCaptureLost(object sender, PointerRoutedEventArgs args)
        {
            _pointerDown = false;
            _dragging = false;
            _pointerCaptured = false;
        }

        private void RootPointerEntered(object sender, PointerRoutedEventArgs args)
        {
            _pointerInside = true;
            CloseGlyph.Opacity = 1.0;
            _hoverFallTimer.Stop();

            // A pointer merely clipping a corner while dragging across the screen should not
            // make the widget flash to full opacity, so the rise waits for a real dwell.
            if (_alphaPercent < 100)
            {
                _hoverRiseTimer.Stop();
                _hoverRiseTimer.Start();
            }

        }

        private void RootPointerExited(object sender, PointerRoutedEventArgs args)
        {
            _pointerInside = false;
            CloseGlyph.Opacity = 0.0;
            _hoverRiseTimer.Stop();
            _hoverFallTimer.Stop();
            _hoverFallTimer.Start();
        }

        private void OnHoverRiseTick(object? sender, object args)
        {
            _hoverRiseTimer.Stop();

            if (_pointerInside)
            {
                FadeAlphaTo(100);
            }

        }

        private void OnHoverFallTick(object? sender, object args)
        {
            _hoverFallTimer.Stop();
            ApplyRestingOpacity();
        }

        private void ApplyRestingOpacity()
        {

            if (!_pointerInside)
            {
                FadeAlphaTo(_state.Opacity);
            }

        }

        private void FadeAlphaTo(int percent)
        {
            _targetAlphaPercent = Math.Clamp(percent, 10, 100);

            if (_targetAlphaPercent == _alphaPercent)
            {
                _alphaFadeTimer.Stop();
            }
            else
            {
                _alphaFadeTimer.Start();
            }

        }

        private void OnAlphaFadeTick(object? sender, object args)
        {
            int distance = _targetAlphaPercent - _alphaPercent;

            if (Math.Abs(distance) <= AlphaStepPercent)
            {
                _alphaFadeTimer.Stop();
                SetWindowAlpha(_targetAlphaPercent);
            }
            else
            {
                SetWindowAlpha(_alphaPercent + Math.Sign(distance) * AlphaStepPercent);
            }

        }

        private void SetWindowAlpha(int percent)
        {
            byte alpha = (byte)Math.Round(percent * 255.0 / 100.0);

            _alphaPercent = percent;

            SetLayeredWindowAttributes(_hwnd, 0, alpha, LwaAlpha);
        }

        private void InternetSectionDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
            ShowMainWindow("Internet");
        }

        private void LocalSectionDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
            ShowMainWindow("Local");
        }

        private void SpeedLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
            ShowMainWindow("SpeedTest");
        }

        private void DevicesLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
            App.ShowMainWindow();
            MainWindow.Current?.NavigateToUnapprovedDevices();
        }

        private void CloseGlyphClick(object sender, RoutedEventArgs args)
        {
            _state.IsVisible = false;
        }

        private void RootRightTapped(object sender, RightTappedRoutedEventArgs args)
        {
            WidgetMenu.Items.Clear();
            WidgetMenu.Items.Add(BuildSectionItem("Internet", _state.ShowInternet, value => _state.ShowInternet = value));
            WidgetMenu.Items.Add(BuildSectionItem("Local", _state.ShowLocal, value => _state.ShowLocal = value));
            WidgetMenu.Items.Add(BuildSectionItem("Speed test", _state.ShowSpeedTest, value => _state.ShowSpeedTest = value));
            WidgetMenu.Items.Add(BuildSectionItem("Unknown devices", _state.ShowUnknownDevices, value => _state.ShowUnknownDevices = value));
            WidgetMenu.Items.Add(BuildOpacitySubmenu());
            WidgetMenu.Items.Add(BuildOrientationSubmenu());
            WidgetMenu.Items.Add(BuildBorderItem());
            WidgetMenu.Items.Add(new MenuFlyoutSeparator());

            MenuFlyoutItem openItem = new MenuFlyoutItem
            {
                Text = "Open Network Monitor"
            };
            openItem.Click += (itemSender, itemArgs) => ShowMainWindow(null);
            WidgetMenu.Items.Add(openItem);

            MenuFlyoutItem closeItem = new MenuFlyoutItem
            {
                Text = "Close"
            };
            closeItem.Click += (itemSender, itemArgs) => _state.IsVisible = false;
            WidgetMenu.Items.Add(closeItem);

            WidgetMenu.ShowAt(RootLayer, args.GetPosition(RootLayer));
        }

        private ToggleMenuFlyoutItem BuildSectionItem(string text, bool isChecked, Action<bool> assign)
        {

            // The state refuses to hide the last section. Greying it out here says so before the click
            // instead of leaving a tick that silently springs back.
            bool isLastRemaining = isChecked && _state.VisibleSectionCount <= 1;

            ToggleMenuFlyoutItem item = new ToggleMenuFlyoutItem
            {
                Text = text,
                IsChecked = isChecked,
                IsEnabled = !isLastRemaining
            };

            item.Click += (sender, args) => assign(item.IsChecked);

            return item;
        }

        private ToggleMenuFlyoutItem BuildBorderItem()
        {
            ToggleMenuFlyoutItem item = new ToggleMenuFlyoutItem
            {
                Text = "Show window border",
                IsChecked = _state.ShowBorder
            };

            item.Click += (sender, args) => _state.ShowBorder = item.IsChecked;

            return item;
        }

        private MenuFlyoutSubItem BuildOpacitySubmenu()
        {
            MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
            {
                Text = "Opacity"
            };

            int[] levels = { 50, 60, 70, 80, 90, 100 };
            int current = _state.Opacity;

            foreach (int level in levels)
            {
                RadioMenuFlyoutItem item = new RadioMenuFlyoutItem
                {
                    Text = $"{level}%",
                    GroupName = "MiniGraphOpacity",
                    IsChecked = level == current
                };

                item.Click += (sender, args) => _state.Opacity = level;
                submenu.Items.Add(item);
            }

            return submenu;
        }

        private MenuFlyoutSubItem BuildOrientationSubmenu()
        {
            MenuFlyoutSubItem submenu = new MenuFlyoutSubItem
            {
                Text = "Orientation"
            };

            MiniGraphOrientation current = _state.Orientation;

            submenu.Items.Add(BuildOrientationItem("Vertical", MiniGraphOrientation.Vertical, current));
            submenu.Items.Add(BuildOrientationItem("Horizontal", MiniGraphOrientation.Horizontal, current));

            return submenu;
        }

        private RadioMenuFlyoutItem BuildOrientationItem(string text, MiniGraphOrientation orientation, MiniGraphOrientation current)
        {
            RadioMenuFlyoutItem item = new RadioMenuFlyoutItem
            {
                Text = text,
                GroupName = "MiniGraphOrientation",
                IsChecked = orientation == current
            };

            item.Click += (sender, args) => _state.Orientation = orientation;

            return item;
        }

        private void ShowMainWindow(string? trafficTabTag)
        {
            App.ShowMainWindow();

            if (trafficTabTag is not null)
            {
                MainWindow.Current?.NavigateToTraffic(trafficTabTag);
            }

        }
    }
}
