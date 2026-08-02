using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
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
        private const int MinimumWidth = 240;
        private const int MinimumHeight = 120;
        private const double DragThreshold = 4.0;

        private readonly MiniGraphState _state;
        private readonly Settings _settings;
        private readonly DispatcherTimer _savePlacementTimer;
        private readonly IntPtr _hwnd;
        private bool _placementRestored;
        private bool _pointerDown;
        private bool _dragging;
        private Point _dragOrigin;
        private PointInt32 _dragWindowOrigin;

        public MiniGraphWindow(MiniGraphViewModel viewModel, MiniGraphState state, Settings settings)
        {
            ViewModel = viewModel;
            _state = state;
            _settings = settings;
            InitializeComponent();

            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            _savePlacementTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _savePlacementTimer.Tick += OnSavePlacementTimerTick;

            ConfigureWindow();
            ApplyLayout();
            RestorePlacement();

            _state.Changed += OnStateChanged;
            AppWindow.Changed += OnAppWindowChanged;
        }

        public MiniGraphViewModel ViewModel
        {
            get;
        }

        public void ShowWidget()
        {
            ViewModel.Attach();
            AppWindow.Show();
        }

        public void HideWidget()
        {
            ViewModel.Detach();
            AppWindow.Hide();
        }

        public void CloseWidget()
        {
            _savePlacementTimer.Stop();
            _state.Changed -= OnStateChanged;
            ViewModel.Detach();
            Close();
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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

            long exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
            exStyle |= WsExToolWindow;

            SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle));
        }

        private void RestorePlacement()
        {
            int width = Math.Max(MinimumWidth, _settings.MiniGraphWidth);
            int height = Math.Max(MinimumHeight, _settings.MiniGraphHeight);
            int positionX = _settings.MiniGraphX;
            int positionY = _settings.MiniGraphY;
            bool onScreen = false;

            if (positionX > int.MinValue && positionY > int.MinValue && _settings.MiniGraphX >= 0)
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
            SpeedTestLine.Visibility = _state.ShowSpeedTest ? Visibility.Visible : Visibility.Collapsed;
            UnknownDevicesLine.Visibility = _state.ShowUnknownDevices ? Visibility.Visible : Visibility.Collapsed;
            FooterPanel.Visibility = _state.ShowSpeedTest || _state.ShowUnknownDevices ? Visibility.Visible : Visibility.Collapsed;
            EmptyHint.Visibility = _state.HasAnySection ? Visibility.Collapsed : Visibility.Visible;

            // The strips are fixed height and the charts share everything left over, so switching Local
            // off makes Internet twice as tall rather than shrinking the window.
            SectionsPanel.RowDefinitions[0].Height = _state.ShowInternet ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            SectionsPanel.RowDefinitions[1].Height = _state.ShowLocal ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }

        private void OnStateChanged(object? sender, EventArgs args)
        {
            DispatcherQueue.TryEnqueue(ApplyLayout);
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
                _dragOrigin = args.GetCurrentPoint(null).Position;
                _dragWindowOrigin = AppWindow.Position;
                RootLayer.CapturePointer(args.Pointer);
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
                    _dragging = true;

                    AppWindow.Move(new PointInt32(
                        _dragWindowOrigin.X + (int)Math.Round(deltaX),
                        _dragWindowOrigin.Y + (int)Math.Round(deltaY)));
                }

            }

        }

        private void RootPointerReleased(object sender, PointerRoutedEventArgs args)
        {
            _pointerDown = false;
            _dragging = false;
            RootLayer.ReleasePointerCapture(args.Pointer);
        }

        private void RootPointerEntered(object sender, PointerRoutedEventArgs args)
        {
        }

        private void RootPointerExited(object sender, PointerRoutedEventArgs args)
        {
        }

        private void InternetSectionDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
        }

        private void LocalSectionDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
        }

        private void SpeedLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
        }

        private void DevicesLineDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
        {
        }

        private void CloseGlyphClick(object sender, RoutedEventArgs args)
        {
            _state.IsVisible = false;
        }
    }
}
