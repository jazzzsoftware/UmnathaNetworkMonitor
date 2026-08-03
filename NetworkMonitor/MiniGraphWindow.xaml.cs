using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NetworkMonitor.Services.Data;
using NetworkMonitor.Services.Platform;
using NetworkMonitor.ViewModels;
using Windows.Foundation;
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
        private const int MinimumWidth = 240;
        private const int MinimumHeight = 120;
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
        private bool _placementRestored;
        private bool _pointerDown;
        private bool _dragging;
        private bool _pointerCaptured;
        private bool _pointerInside;
        private bool _teardownStarted;
        private int _alphaPercent = 100;
        private int _targetAlphaPercent = 100;
        private Point _dragOrigin;

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
                SizeInt32 size = AppWindow.Size;

                if (size.Width >= MinimumWidth && size.Height >= MinimumHeight)
                {
                    PointInt32 position = AppWindow.Position;

                    _state.SavePlacement(position.X, position.Y, size.Width, size.Height);
                }

            }

        }

        private void SetChartsLive(bool isLive)
        {
            InternetSection.IsLive = isLive;
            LocalSection.IsLive = isLive;
        }

        // Every size the widget can be dragged to is a legitimate one, and text fixed at 12 point looks
        // cramped at 600 wide and swamps the charts at 240. The reference size is the default placement.
        private void SectionsPanelSizeChanged(object sender, SizeChangedEventArgs args)
        {
            double widthScale = args.NewSize.Width / ReferenceWidth;
            double heightScale = args.NewSize.Height / ReferenceHeight;
            double scale = Math.Clamp(Math.Min(widthScale, heightScale), MinimumFontScale, MaximumFontScale);

            InternetSection.FontScale = scale;
            LocalSection.FontScale = scale;
            SpeedTestLine.FontSize = FooterFontSize * scale;
            UnknownDevicesLine.FontSize = FooterFontSize * scale;
            CloseGlyph.FontSize = FooterFontSize * scale;
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
        }

        private void RestorePlacement()
        {
            int width = Math.Max(MinimumWidth, _settings.MiniGraphWidth);
            int height = Math.Max(MinimumHeight, _settings.MiniGraphHeight);
            int positionX = _settings.MiniGraphX;
            int positionY = _settings.MiniGraphY;
            bool onScreen = false;

            if (_settings.MiniGraphX != int.MinValue && _settings.MiniGraphY != int.MinValue)
            {
                DisplayArea area = DisplayArea.GetFromPoint(new PointInt32(positionX, positionY), DisplayAreaFallback.None);
                onScreen = area is not null;
            }

            if (!onScreen)
            {
                DisplayArea primary = DisplayArea.Primary;
                RectInt32 workArea = primary.WorkArea;
                positionX = workArea.X + workArea.Width - width - 16;
                positionY = workArea.Y + workArea.Height - height - 16;
            }

            AppWindow.MoveAndResize(new RectInt32(positionX, positionY, width, height));
            _placementRestored = true;
        }

        private void ApplyLayout()
        {
            InternetSection.Visibility = _state.ShowInternet ? Visibility.Visible : Visibility.Collapsed;
            LocalSection.Visibility = _state.ShowLocal ? Visibility.Visible : Visibility.Collapsed;
            SpeedTestBand.Visibility = _state.ShowSpeedTest ? Visibility.Visible : Visibility.Collapsed;
            UnknownDevicesBand.Visibility = _state.ShowUnknownDevices ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Visibility = _state.HasAnySection ? Visibility.Collapsed : Visibility.Visible;

            GridLength fill = new GridLength(1, GridUnitType.Star);
            GridLength none = new GridLength(0);

            // The strips are fixed height and the charts share everything left over, so switching Local
            // off makes Internet twice as tall rather than shrinking the window. With both charts off
            // row 0 stays a star and acts as a spacer, otherwise the footer would pin to the top edge
            // with the empty space below it.
            bool spacerNeeded = !_state.ShowInternet && !_state.ShowLocal;

            SectionsPanel.RowDefinitions[0].Height = _state.ShowInternet || spacerNeeded ? fill : none;
            SectionsPanel.RowDefinitions[1].Height = _state.ShowLocal ? fill : none;
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
            if (args.PropertyName is null || args.PropertyName == nameof(MiniGraphViewModel.SpeedTestText))
            {
                DispatcherQueue.TryEnqueue(ApplySpeedTestText);
            }

        }

        private void ApplySpeedTestText()
        {
            SpeedTestDetail.Text = ViewModel.SpeedTestText;
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
            DispatcherQueue.TryEnqueue(ApplyLayout);
            DispatcherQueue.TryEnqueue(ApplyRestingOpacity);
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

        private void ClampMinimumSize()
        {
            SizeInt32 size = AppWindow.Size;

            if (size.Width < MinimumWidth || size.Height < MinimumHeight)
            {
                int width = Math.Max(MinimumWidth, size.Width);
                int height = Math.Max(MinimumHeight, size.Height);

                AppWindow.Resize(new SizeInt32(width, height));
            }

        }

        private void OnSavePlacementTimerTick(object? sender, object args)
        {
            _savePlacementTimer.Stop();
            _state.SavePlacement(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        }

        private void RootPointerPressed(object sender, PointerRoutedEventArgs args)
        {

            if (args.GetCurrentPoint(RootLayer).Properties.IsLeftButtonPressed)
            {
                _pointerDown = true;
                _dragging = false;

                // Window-relative, so it is the grab point on the widget rather than a screen point.
                // That frame moves with the window, which is exactly what makes the delta below
                // self-correcting once the window starts following the pointer.
                _dragOrigin = args.GetCurrentPoint(null).Position;
            }

        }

        private void RootPointerMoved(object sender, PointerRoutedEventArgs args)
        {

            if (_pointerDown)
            {
                Point current = args.GetCurrentPoint(null).Position;
                double deltaX = current.X - _dragOrigin.X;
                double deltaY = current.Y - _dragOrigin.Y;

                // Below the threshold nothing moves, so a double-click still reaches the section
                // underneath instead of being eaten by a one-pixel drag.
                if (_dragging || Math.Abs(deltaX) > DragThreshold || Math.Abs(deltaY) > DragThreshold)
                {

                    // Capturing on press would route every later pointer event here and the child
                    // sections would never complete their tap gestures, killing all four drill-ins.
                    // Capture only once the press has actually become a drag.
                    if (!_dragging)
                    {
                        _dragging = true;
                        _pointerCaptured = RootLayer.CapturePointer(args.Pointer);
                    }

                    // Both origin and current are measured against the window, so the delta is how far
                    // the grab point has slipped from under the pointer. Applying it to the *current*
                    // window position drives that slip to zero: W' = W + (P - W/s - g) * s = (P - g) * s,
                    // an absolute target that cannot accumulate. Applying it to the press-time position
                    // instead would subtract the travel already made and oscillate in place.
                    // GetCurrentPoint yields DIPs while AppWindow.Move takes physical pixels, hence the
                    // rasterization scale — without it a drag runs at 1/2 speed on a 200% display.
                    double scale = RootLayer.XamlRoot?.RasterizationScale ?? 1.0;
                    PointInt32 position = AppWindow.Position;

                    AppWindow.Move(new PointInt32(
                        position.X + (int)Math.Round(deltaX * scale),
                        position.Y + (int)Math.Round(deltaY * scale)));
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
