using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;
using AeroSpot.Worker.Controls;
using AeroSpot.Worker.Services;

namespace AeroSpot.Worker
{
    public partial class PreviewWindow : Window
    {
        private string? _currentFilePath;
        private CancellationTokenSource? _folderSizeCts;
        private DispatcherTimer? _videoTimer;
        private bool _isScrubbing;
        private bool _wasPlayingBeforeScrub;
        private bool _isPlayingVideo;
        private bool _isMuted;
        private double _lastVolume = 0.5;
        private AeroSpot.Core.Config.AppConfig _config;
        private int _loadRequestId = 0;
        private ScrollViewer? _markdownScrollViewer;
        private double _currentAudioFraction = 0.0;
        private double _currentVinylAngle = 0.0;

        private IntPtr _hwnd = IntPtr.Zero;
        private IntPtr _previousForegroundHwnd = IntPtr.Zero;
        public bool IsPreviewVisible { get; private set; }

        private TimeSpan _pendingSeekTarget = TimeSpan.Zero;
        private DateTime _holdTimerUntil = DateTime.MinValue;
        private DateTime _lastScrubThrottle = DateTime.MinValue;

        private double _imageScale = 1.0;
        private Point _imagePanStart;
        private bool _isPanningImage;
        private double _pdfScale = 1.0;
        private string _currentFileSizeText = string.Empty;

        // Campi per la visualizzazione e interazione 3D STL
        private PerspectiveCamera? _stlCamera;
        private Point3D _stlCenter;
        private double _stlDistance;
        private double _stlTheta = 45;
        private double _stlPhi = 35;
        private Point _stlLastMouse;
        private bool _stlIsDragging;

        private SpreadsheetModel? _currentSpreadsheetModel;
        private int _currentSpreadsheetSheetIndex = 0;

        public bool IsLaunchedFromSpotlight { get; set; }
        public event Action<int>? NavigateSelectionRequested;
        public event Action? HideRequested;
        public event Action? CloseAllRequested;
        public event Action<string>? PreviewOpened;
        public event Action? PreviewClosed;

        public enum PreviewCategory
        {
            Image,
            Video,
            Audio,
            Pdf,
            Document,
            Generic,
            Folder
        }

        private PreviewCategory _currentCategory = PreviewCategory.Generic;
        private readonly System.Collections.Generic.Dictionary<PreviewCategory, (double Width, double Height)> _sessionCustomSizes = new();

        public PreviewWindow()
        {
            InitializeComponent();
            _config = AeroSpot.Core.Config.AppConfig.Load();

            var helper = new WindowInteropHelper(this);
            _hwnd = helper.EnsureHandle();
            int exStyle = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, exStyle | Win32.WS_EX_TOOLWINDOW);
            Win32.SetWindowCloak(_hwnd, true);

            this.Deactivated += (s, e) =>
            {
                // Se aperta da Spotlight e l'utente ci naviga dentro, non chiudere
                if (!App.IsShuttingDown && IsPreviewVisible && !IsLaunchedFromSpotlight)
                {
                    HidePreview();
                }
            };

            if (SpreadsheetDataGrid != null)
            {
                SpreadsheetDataGrid.LoadingRow += (s, e) =>
                {
                    e.Row.Header = (e.Row.GetIndex() + 1).ToString();
                };
            }
        }

        public async Task ShowPreviewForFileAsync(string filePath, bool isFromSpotlight = false)
        {
            if (IsPreviewVisible && string.Equals(_currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                HidePreview();
                return;
            }

            if (!isFromSpotlight)
            {
                var fg = Win32.GetForegroundWindow();
                if (fg != _hwnd && fg != IntPtr.Zero)
                {
                    _previousForegroundHwnd = fg;
                }
            }

            IsLaunchedFromSpotlight = isFromSpotlight;
            _currentFilePath = filePath;
            int requestId = Interlocked.Increment(ref _loadRequestId);

            // Ferma eventuale video in esecuzione sul thread UI
            Dispatcher.Invoke(() =>
            {
                if (_isPlayingVideo)
                {
                    StopAndUnloadVideo();
                }
            });

            // Carica l'anteprima in modo asincrono
            var model = await PreviewLoader.LoadAsync(filePath);

            // Se nel frattempo l'utente ha premuto una freccia richiedendo un nuovo file, scarta il modello vecchio
            if (requestId != _loadRequestId)
            {
                return;
            }

            Func<Task> applyAction = async () =>
            {
                if (requestId != _loadRequestId) return;

                bool wasVisible = IsPreviewVisible;

                ResetZoom();
                ApplyModelToUI(model);
                PositionAndSizeWindow(model);

                if (!wasVisible)
                {
                    if (!this.IsVisible)
                    {
                        this.Show();
                    }
                    this.Opacity = 1;
                    this.UpdateLayout();

                    await WaitRenderedAsync();

                    if (_hwnd != IntPtr.Zero)
                    {
                        Win32.SetWindowCloak(_hwnd, false);
                    }
                    IsPreviewVisible = true;

                    if (_isPlayingVideo)
                    {
                        VideoPlayer.Play();
                    }
                }
                else
                {
                    this.Opacity = 1;
                    this.UpdateLayout();
                }

                if (this.IsVisible)
                {
                    try
                    {
                        this.Activate();
                        this.Focus();
                    }
                    catch { }
                }

                if (_hwnd != IntPtr.Zero)
                {
                    try
                    {
                        Win32.SetForegroundWindow(_hwnd);
                    }
                    catch { }
                }

                PreviewOpened?.Invoke(filePath);
            };

            if (Dispatcher.CheckAccess())
            {
                await applyAction();
            }
            else
            {
                await Dispatcher.InvokeAsync(applyAction).Task.Unwrap();
            }
        }

        private async Task WaitRenderedAsync()
        {
            this.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

            var tcs = new TaskCompletionSource();
            EventHandler handler = null!;
            handler = (s, e) =>
            {
                CompositionTarget.Rendering -= handler;
                tcs.TrySetResult();
            };
            CompositionTarget.Rendering += handler;
            await Task.WhenAny(tcs.Task, Task.Delay(40));
            CompositionTarget.Rendering -= handler;
            await Task.Delay(16);
        }

        private void ApplyModelToUI(PreviewModel model)
        {
            // Pulisci viste e timer precedenti
            _folderSizeCts?.Cancel();
            _folderSizeCts?.Dispose();
            _folderSizeCts = null;

            StopAndUnloadVideo();

            // Elimina lo sfarfallio e il flash nero: se il nuovo modello è un'immagine, NON collassare ImageView
            if (model.Type != PreviewType.Image)
            {
                ImageView.Visibility = Visibility.Collapsed;
                PreviewImage.Source = null;
            }
            else
            {
                ImageView.Visibility = Visibility.Visible;
                PreviewImage.Source = model.Image;
            }

            VideoView.Visibility = Visibility.Collapsed;
            if (AudioVisualizerPanel != null) AudioVisualizerPanel.Visibility = Visibility.Collapsed;
            if (BtnVideoFullscreen != null) BtnVideoFullscreen.Visibility = Visibility.Visible;
            TextView.Visibility = Visibility.Collapsed;
            MarkdownView.Visibility = Visibility.Collapsed;
            PdfView.Visibility = Visibility.Collapsed;
            PdfPagesPanel.Children.Clear();
            if (StlView != null) StlView.Visibility = Visibility.Collapsed;
            if (StlViewport != null) StlViewport.Children.Clear();
            HandlerView.Visibility = Visibility.Collapsed;
            GenericView.Visibility = Visibility.Collapsed;
            ErrorView.Visibility = Visibility.Collapsed;

            PreviewTextBox.Text = string.Empty;

            FolderSizeLoadingPanel.Visibility = Visibility.Collapsed;
            FolderSizeCalculatedText.Visibility = Visibility.Collapsed;

            if (HandlerHostBorder.Child is PreviewHandlerHostControl oldHost)
            {
                oldHost.UnloadHandler();
                HandlerHostBorder.Child = null;
            }

            _currentFileSizeText = model.FileSizeText;
            HeaderTitle.Text = model.Title;
            HeaderSubtitle.Text = model.Subtitle;
            FooterDetails.Text = !string.IsNullOrWhiteSpace(model.FooterText) ? model.FooterText : model.DetailsText;

            // Recupera l'icona reale di sistema
            var icon = IconService.GetIcon(model.FilePath, model.Type == PreviewType.Image ? "Immagine" : (model.Type == PreviewType.Pdf ? "Documento" : (model.Type == PreviewType.Audio ? "Audio" : (model.Type == PreviewType.Stl3D ? "Modello 3D" : (model.IsFolder ? "Cartella" : "File")))));
            FileIcon.Source = icon;

            switch (model.Type)
            {
                case PreviewType.Image:
                    // Sorgente e visibilità già assegnate sopra in modo atomico senza flash nero
                    break;

                case PreviewType.Pdf:
                    PdfView.Visibility = Visibility.Visible;
                    if (model.PdfPages != null)
                    {
                        foreach (var pageBmp in model.PdfPages)
                        {
                            var border = new Border
                            {
                                Background = Brushes.White,
                                CornerRadius = new CornerRadius(4),
                                Margin = new Thickness(0, 0, 0, 18),
                                UseLayoutRounding = true,
                                SnapsToDevicePixels = true,
                                Effect = new System.Windows.Media.Effects.DropShadowEffect
                            {
                                BlurRadius = 14,
                                ShadowDepth = 3,
                                Direction = 270,
                                Color = Colors.Black,
                                Opacity = 0.5
                            }
                            };
                            var img = new Image
                            {
                                Source = pageBmp,
                                Stretch = Stretch.Uniform,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                UseLayoutRounding = true,
                                SnapsToDevicePixels = true
                            };
                            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                            border.Child = img;
                            PdfPagesPanel.Children.Add(border);
                        }
                    }
                    PdfView.ScrollToTop();
                    break;

                case PreviewType.Video:
                    VideoView.Visibility = Visibility.Visible;
                    if (AudioVisualizerPanel != null) AudioVisualizerPanel.Visibility = Visibility.Collapsed;
                    if (BtnVideoFullscreen != null) BtnVideoFullscreen.Visibility = Visibility.Visible;
                    LoadAndPlayVideo(model.FilePath);
                    break;

                case PreviewType.Audio:
                    VideoView.Visibility = Visibility.Visible;
                    if (AudioVisualizerPanel != null) AudioVisualizerPanel.Visibility = Visibility.Visible;
                    if (BtnVideoFullscreen != null) BtnVideoFullscreen.Visibility = Visibility.Collapsed;
                    LoadAndPlayVideo(model.FilePath);
                    break;

                case PreviewType.Stl3D:
                    if (StlView != null) StlView.Visibility = Visibility.Visible;
                    SetupStl3DView(model.StlModel);
                    break;

                case PreviewType.Text:
                    TextView.Visibility = Visibility.Visible;
                    PreviewTextBox.Text = model.TextContent ?? string.Empty;
                    break;

                case PreviewType.Markdown:
                    MarkdownView.Visibility = Visibility.Visible;
                    MarkdownView.Document = MarkdownToFlowDocumentConverter.Convert(model.TextContent ?? string.Empty);
                    break;

                case PreviewType.Spreadsheet:
                    SpreadsheetView.Visibility = Visibility.Visible;
                    SetupSpreadsheetView(model.Spreadsheet);
                    break;

                case PreviewType.PreviewHandler:
                    try
                    {
                        var hostControl = new PreviewHandlerHostControl(model.FilePath, model.PreviewHandlerClsid);
                        hostControl.CloseRequested += HidePreview;
                        hostControl.PreviewFailed += (s, ex) =>
                        {
                            Dispatcher.BeginInvoke(new Action(() => FallbackToGenericView(model, "Visualizzatore di anteprima non riuscito")));
                        };

                        HandlerHostBorder.Child = hostControl;
                        HandlerView.Visibility = Visibility.Visible;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Attivazione PreviewHandler fallita per {model.FilePath}: {ex.Message}");
                        FallbackToGenericView(model, "Impossibile attivare l'anteprima");
                    }
                    break;

                case PreviewType.Generic:
                    GenericView.Visibility = Visibility.Visible;
                    GenericIcon.Source = icon;
                    GenericTitle.Text = model.Title;
                    GenericDetails.Text = model.DetailsText;
                    if (OpenDefaultButton != null)
                    {
                        OpenDefaultButton.Content = model.IsFolder
                            ? AeroSpot.Core.Localization.LocalizationService.Get("Preview_Btn_OpenFolder")
                            : AeroSpot.Core.Localization.LocalizationService.Get("Preview_Btn_OpenDefaultAction");
                    }
                    if (model.IsFolder)
                    {
                        StartFolderSizeCalculation(model.FilePath);
                    }
                    break;

                case PreviewType.Error:
                    ErrorView.Visibility = Visibility.Visible;
                    ErrorMessageText.Text = model.ErrorMessage ?? "Errore sconosciuto";
                    break;
            }
        }

        private void FallbackToGenericView(PreviewModel model, string reason)
        {
            HandlerView.Visibility = Visibility.Collapsed;
            VideoView.Visibility = Visibility.Collapsed;
            StopAndUnloadVideo();

            if (HandlerHostBorder.Child is PreviewHandlerHostControl host)
            {
                host.UnloadHandler();
                HandlerHostBorder.Child = null;
            }

            GenericView.Visibility = Visibility.Visible;
            var icon = IconService.GetIcon(model.FilePath, model.IsFolder ? "Cartella" : "Documento");
            GenericIcon.Source = icon;
            GenericTitle.Text = model.Title;
            GenericDetails.Text = $"{reason}.\n{model.DetailsText}";
            if (model.IsFolder)
            {
                StartFolderSizeCalculation(model.FilePath);
            }
        }

        private void GetActiveMonitorWorkArea(out double workLeft, out double workTop, out double monitorWidth, out double monitorHeight)
        {
            Win32.GetCursorPos(out Win32.POINT cursorPt);
            IntPtr hMonitor = Win32.MonitorFromPoint(cursorPt, Win32.MONITOR_DEFAULTTONEAREST);

            var mi = new Win32.MONITORINFO();
            mi.cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>();

            monitorWidth = 1920;
            monitorHeight = 1080;
            workLeft = 0;
            workTop = 0;

            if (Win32.GetMonitorInfo(hMonitor, ref mi))
            {
                Matrix transform = Matrix.Identity;
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    transform = source.CompositionTarget.TransformFromDevice;
                }

                Point topLeft = transform.Transform(new Point(mi.rcWork.Left, mi.rcWork.Top));
                Point bottomRight = transform.Transform(new Point(mi.rcWork.Right, mi.rcWork.Bottom));

                workLeft = topLeft.X;
                workTop = topLeft.Y;
                monitorWidth = bottomRight.X - topLeft.X;
                monitorHeight = bottomRight.Y - topLeft.Y;
            }
        }

        public void AdaptWindowToAspectRatio(double contentWidth, double contentHeight)
        {
            if (this.WindowState == WindowState.Maximized || contentWidth <= 0 || contentHeight <= 0) return;
            if (_sessionCustomSizes.ContainsKey(_currentCategory) || IsPersistentRememberActive(_currentCategory)) return;

            GetActiveMonitorWorkArea(out double workLeft, out double workTop, out double monitorWidth, out double monitorHeight);

            double ratio = contentWidth / contentHeight;
            bool isVertical = contentHeight > contentWidth;

            // Contenuti verticali: limitati a 1/2 dello schermo in larghezza (max 50%)
            double maxWidth = isVertical ? monitorWidth * 0.50 : monitorWidth * 0.82;
            double maxHeight = monitorHeight * 0.82;
            double minWidth = Math.Min(340, maxWidth);
            double minHeight = Math.Min(240, maxHeight);

            double availableContentWidth = maxWidth - 28;
            double availableContentHeight = maxHeight - 85;

            double targetContentWidth = Math.Min(contentWidth, availableContentWidth);
            double targetContentHeight = targetContentWidth / ratio;

            if (targetContentHeight > availableContentHeight)
            {
                targetContentHeight = availableContentHeight;
                targetContentWidth = targetContentHeight * ratio;
            }

            double targetWidth = Math.Clamp(targetContentWidth + 28, minWidth, maxWidth);
            double targetHeight = Math.Clamp(targetContentHeight + 85, minHeight, maxHeight);

            PositionWindowRelative(targetWidth, targetHeight, workLeft, workTop, monitorWidth, monitorHeight);
        }

        private void PositionWindowRelative(double targetWidth, double targetHeight, double workLeft, double workTop, double monitorWidth, double monitorHeight)
        {
            this.Width = targetWidth;
            this.Height = targetHeight;

            Window? mainWin = null;
            try
            {
                if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
                {
                    mainWin = Application.Current.MainWindow;
                }
            }
            catch { }

            if (IsLaunchedFromSpotlight && mainWin != null && mainWin.IsVisible)
            {
                double spotLeft = mainWin.Left;
                double spotTop = mainWin.Top;
                double spotWidth = mainWin.ActualWidth;

                // Prova prima a posizionare l'anteprima a sinistra dello Spotlight per non coprirlo
                if (spotLeft - targetWidth - 16 >= workLeft)
                {
                    this.Left = spotLeft - targetWidth - 16;
                    this.Top = Math.Max(workTop + 20, Math.Min(spotTop, workTop + monitorHeight - targetHeight - 20));
                    return;
                }
                // Altrimenti posiziona a destra dello Spotlight
                else if (spotLeft + spotWidth + targetWidth + 16 <= workLeft + monitorWidth)
                {
                    this.Left = spotLeft + spotWidth + 16;
                    this.Top = Math.Max(workTop + 20, Math.Min(spotTop, workTop + monitorHeight - targetHeight - 20));
                    return;
                }
            }

            // Se la finestra è già visibile (es. scorrimento lista file), preserva il centro corrente
            if (IsPreviewVisible && this.ActualWidth > 0 && this.ActualHeight > 0)
            {
                double currentCenterX = this.Left + this.ActualWidth / 2.0;
                double currentCenterY = this.Top + this.ActualHeight / 2.0;

                double newLeft = currentCenterX - targetWidth / 2.0;
                double newTop = currentCenterY - targetHeight / 2.0;

                this.Left = Math.Clamp(newLeft, workLeft + 10, workLeft + monitorWidth - targetWidth - 10);
                this.Top = Math.Clamp(newTop, workTop + 10, workTop + monitorHeight - targetHeight - 10);
            }
            else
            {
                // Centra la finestra nell'area di lavoro del monitor attivo
                this.Left = workLeft + (monitorWidth - targetWidth) / 2.0;
                this.Top = workTop + (monitorHeight - targetHeight) / 2.0;
            }
        }

        private void PositionAndSizeWindow(PreviewModel model)
        {
            GetActiveMonitorWorkArea(out double workLeft, out double workTop, out double monitorWidth, out double monitorHeight);

            if (model.IsFolder)
                _currentCategory = PreviewCategory.Folder;
            else if (model.Type == PreviewType.Image)
                _currentCategory = PreviewCategory.Image;
            else if (model.Type == PreviewType.Video)
                _currentCategory = PreviewCategory.Video;
            else if (model.Type == PreviewType.Audio)
                _currentCategory = PreviewCategory.Audio;
            else if (model.Type == PreviewType.Pdf)
                _currentCategory = PreviewCategory.Pdf;
            else if (model.Type == PreviewType.Text || model.Type == PreviewType.Markdown || model.Type == PreviewType.PreviewHandler || model.Type == PreviewType.Stl3D || model.Type == PreviewType.Spreadsheet)
                _currentCategory = PreviewCategory.Document;
            else
                _currentCategory = PreviewCategory.Generic;

            if (_currentCategory == PreviewCategory.Folder)
            {
                ResizeThumbsContainer.Visibility = Visibility.Collapsed;
                BtnRememberSize.Visibility = Visibility.Collapsed;
            }
            else
            {
                ResizeThumbsContainer.Visibility = Visibility.Visible;
                BtnRememberSize.Visibility = Visibility.Visible;
                UpdateRememberSizeButtonUi();
            }

            // 1. Se è attiva la memorizzazione persistente su disco per questa categoria, usa le dimensioni salvate
            var persistentSize = GetPersistentSavedSize(_currentCategory);
            if (persistentSize.HasValue)
            {
                double userW = Math.Clamp(persistentSize.Value.Width, 380, monitorWidth * 0.95);
                double userH = Math.Clamp(persistentSize.Value.Height, 260, monitorHeight * 0.95);
                AdjustPdfPageSizes(model, userW, userH);
                PositionWindowRelative(userW, userH, workLeft, workTop, monitorWidth, monitorHeight);
                return;
            }

            // 2. Se l'utente ha ridimensionato nella sessione corrente per questa categoria, riusa la dimensione di sessione
            if (_sessionCustomSizes.TryGetValue(_currentCategory, out var sessionSize))
            {
                double sessionW = Math.Clamp(sessionSize.Width, 380, monitorWidth * 0.95);
                double sessionH = Math.Clamp(sessionSize.Height, 260, monitorHeight * 0.95);
                AdjustPdfPageSizes(model, sessionW, sessionH);
                PositionWindowRelative(sessionW, sessionH, workLeft, workTop, monitorWidth, monitorHeight);
                return;
            }

            // 3. Altrimenti usa la dimensione standard ottimale per il tipo di contenuto
            double targetWidth = 780;
            double targetHeight = 540;

            if (model.Type == PreviewType.Image && model.Image is BitmapSource bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
            {
                double ratio = (double)bmp.PixelWidth / bmp.PixelHeight;
                bool isVertical = bmp.PixelHeight > bmp.PixelWidth;

                // Contenuti verticali: limitati a 1/2 dello schermo in larghezza (max 50%)
                double maxWidth = isVertical ? monitorWidth * 0.50 : monitorWidth * 0.82;
                double maxHeight = monitorHeight * 0.82;
                double minWidth = Math.Min(340, maxWidth);
                double minHeight = Math.Min(240, maxHeight);

                double availableContentWidth = maxWidth - 28;
                double availableContentHeight = maxHeight - 85;

                double targetContentWidth = Math.Min(bmp.PixelWidth, availableContentWidth);
                double targetContentHeight = targetContentWidth / ratio;

                if (targetContentHeight > availableContentHeight)
                {
                    targetContentHeight = availableContentHeight;
                    targetContentWidth = targetContentHeight * ratio;
                }

                targetWidth = Math.Clamp(targetContentWidth + 28, minWidth, maxWidth);
                targetHeight = Math.Clamp(targetContentHeight + 85, minHeight, maxHeight);
            }
            else if (model.Type == PreviewType.Video)
            {
                double maxWidth = monitorWidth * 0.80;
                double maxHeight = monitorHeight * 0.80;
                double minWidth = Math.Min(480, maxWidth);
                double minHeight = Math.Min(320, maxHeight);

                targetWidth = Math.Clamp(780, minWidth, maxWidth);
                targetHeight = Math.Clamp(480, minHeight, maxHeight);
            }
            else if (model.Type == PreviewType.PreviewHandler)
            {
                targetWidth = Math.Min(960, monitorWidth * 0.82);
                targetHeight = Math.Min(720, monitorHeight * 0.82);
            }
            else if (model.Type == PreviewType.Text || model.Type == PreviewType.Markdown)
            {
                targetWidth = Math.Min(820, monitorWidth * 0.70);
                targetHeight = Math.Min(600, monitorHeight * 0.75);
            }
            else if (model.Type == PreviewType.Spreadsheet)
            {
                targetWidth = Math.Min(980, monitorWidth * 0.85);
                targetHeight = Math.Min(680, monitorHeight * 0.85);
            }
            else if (model.Type == PreviewType.Pdf)
            {
                if (model.PdfWidth > 0 && model.PdfHeight > 0)
                {
                    double ratio = model.PdfWidth / model.PdfHeight;
                    if (ratio >= 1.0)
                    {
                        // Landscape PDF (orizzontale) - 10% più grande per una lettura ottimale
                        double maxHeight = monitorHeight * 0.90;
                        double targetH = Math.Clamp(638, 460, maxHeight);
                        double availableH = targetH - 120;
                        double targetW = Math.Clamp(availableH * ratio + 88, 660, monitorWidth * 0.90);
                        targetWidth = targetW;
                        targetHeight = targetH;
                    }
                    else
                    {
                        // Portrait PDF (verticale) - 10% più grande
                        double maxHeight = monitorHeight * 0.92;
                        double targetH = Math.Min(968, maxHeight);
                        double availableH = targetH - 120;
                        double targetW = Math.Clamp((availableH * ratio + 88) * 1.10, 572, monitorWidth * 0.85);
                        targetWidth = targetW;
                        targetHeight = targetH;
                    }
                }
                else
                {
                    targetWidth = Math.Min(924, monitorWidth * 0.80);
                    targetHeight = Math.Min(968, monitorHeight * 0.90);
                }
            }
            else if (model.Type == PreviewType.Audio)
            {
                targetWidth = 558;
                targetHeight = 408;
            }
            else if (model.Type == PreviewType.Stl3D)
            {
                targetWidth = Math.Min(860, monitorWidth * 0.80);
                targetHeight = Math.Min(680, monitorHeight * 0.80);
            }
            else if (model.Type == PreviewType.Generic || model.Type == PreviewType.Error || model.IsFolder)
            {
                targetWidth = 560;
                targetHeight = 380;
            }

            AdjustPdfPageSizes(model, targetWidth, targetHeight);
            PositionWindowRelative(targetWidth, targetHeight, workLeft, workTop, monitorWidth, monitorHeight);
        }

        private void AdjustPdfPageSizes(PreviewModel model, double windowWidth, double windowHeight)
        {
            if (model.Type != PreviewType.Pdf || PdfPagesPanel == null || PdfPagesPanel.Children.Count == 0)
                return;

            double availableH = Math.Max(260, windowHeight - 125);
            double availableW = Math.Max(260, windowWidth - 70);

            double fitW = availableW;
            double fitH = availableH;

            if (model.PdfWidth > 0 && model.PdfHeight > 0)
            {
                double ratio = model.PdfWidth / model.PdfHeight;
                fitW = availableW;
                fitH = fitW / ratio;
                if (fitH > availableH)
                {
                    fitH = availableH;
                    fitW = fitH * ratio;
                }
            }

            fitW = Math.Round(fitW);
            fitH = Math.Round(fitH);

            foreach (UIElement child in PdfPagesPanel.Children)
            {
                if (child is Border b)
                {
                    b.Width = fitW;
                    b.Height = fitH;
                    if (b.Child is Image img)
                    {
                        img.Width = fitW;
                        img.Height = fitH;
                    }
                }
            }
        }

        private void SaveWindowBounds()
        {
            if (this.WindowState == WindowState.Normal && this.Left > 0 && this.Top > 0)
            {
                _config.PreviewWindowLeft = this.Left;
                _config.PreviewWindowTop = this.Top;
                _config.Save();
            }
        }

        public void HidePreview()
        {
            if (IsPreviewVisible)
            {
                IsPreviewVisible = false;
                _sessionCustomSizes.Clear();

                // 1. Cloak immediato nel DWM e Hide: la finestra sparisce istantaneamente e non lascia tracce in Gestione Attività
                if (_hwnd != IntPtr.Zero)
                {
                    Win32.SetWindowCloak(_hwnd, true);
                }
                this.Hide();

                _folderSizeCts?.Cancel();
                _folderSizeCts?.Dispose();
                _folderSizeCts = null;

                StopAndUnloadVideo();
                ResetZoom();

                // 2. Rilascia e resetta le viste
                if (HandlerHostBorder.Child is PreviewHandlerHostControl host)
                {
                    host.UnloadHandler();
                    HandlerHostBorder.Child = null;
                }
                HandlerView.Visibility = Visibility.Collapsed;
                ImageView.Visibility = Visibility.Collapsed;
                VideoView.Visibility = Visibility.Collapsed;
                if (AudioVisualizerPanel != null) AudioVisualizerPanel.Visibility = Visibility.Collapsed;
                if (BtnVideoFullscreen != null) BtnVideoFullscreen.Visibility = Visibility.Visible;
                TextView.Visibility = Visibility.Collapsed;
                MarkdownView.Visibility = Visibility.Collapsed;
                MarkdownView.Document = null;
                PdfView.Visibility = Visibility.Collapsed;
                PdfPagesPanel.Children.Clear();
                if (SpreadsheetView != null) SpreadsheetView.Visibility = Visibility.Collapsed;
                if (SpreadsheetDataGrid != null) SpreadsheetDataGrid.ItemsSource = null;
                if (SpreadsheetTabsPanel != null) SpreadsheetTabsPanel.Children.Clear();
                _currentSpreadsheetModel = null;
                if (StlView != null) StlView.Visibility = Visibility.Collapsed;
                if (StlViewport != null) StlViewport.Children.Clear();
                GenericView.Visibility = Visibility.Collapsed;
                ErrorView.Visibility = Visibility.Collapsed;

                PreviewImage.Source = null;
                PreviewTextBox.Text = string.Empty;
                _currentFilePath = null;

                // 3. Restituisci il focus se aperta da Explorer o Desktop
                if (!IsLaunchedFromSpotlight && _previousForegroundHwnd != IntPtr.Zero)
                {
                    Win32.SetForegroundWindow(_previousForegroundHwnd);
                    _previousForegroundHwnd = IntPtr.Zero;
                }

                PreviewClosed?.Invoke();
                HideRequested?.Invoke();
            }
        }

        private void StartFolderSizeCalculation(string folderPath)
        {
            FolderSizeLoadingPanel.Visibility = Visibility.Visible;
            FolderSizeCalculatedText.Visibility = Visibility.Collapsed;
            FolderSizeStatusText.Text = AeroSpot.Core.Localization.LocalizationService.Get("Preview_Folder_Calculating");

            _folderSizeCts = new System.Threading.CancellationTokenSource();
            var token = _folderSizeCts.Token;

            _ = Task.Run(async () =>
            {
                var res = await PreviewLoader.CalculateDirectorySizeAsync(folderPath, token);
                if (res.HasValue && !token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested || !string.Equals(_currentFilePath, folderPath, StringComparison.OrdinalIgnoreCase)) return;
                        FolderSizeLoadingPanel.Visibility = Visibility.Collapsed;
                        string filesLabel = AeroSpot.Core.Localization.LocalizationService.CurrentLanguage == "it" ? "file" : "files";
                        string foldersLabel = AeroSpot.Core.Localization.LocalizationService.CurrentLanguage == "it" ? "cartelle" : "folders";
                        string folderCategory = AeroSpot.Core.Localization.LocalizationService.Get("Spotlight_Category_Folder");
                        string formatted = $"{PreviewLoader.FormatFileSize(res.Value.TotalBytes)} ({res.Value.FileCount:N0} {filesLabel}, {res.Value.DirCount:N0} {foldersLabel})";
                        FolderSizeCalculatedText.Text = formatted;
                        FolderSizeCalculatedText.Visibility = Visibility.Visible;
                        HeaderSubtitle.Text = $"{folderCategory} • {PreviewLoader.FormatFileSize(res.Value.TotalBytes)}";
                    });
                }
            }, token);
        }

        private void UpdatePlayPauseIcon(bool isPlaying)
        {
            if (PlayPauseIcon == null) return;
            if (isPlaying)
            {
                // Due barre verticali di pausa senza alcun riquadro o bordo
                PlayPauseIcon.Data = Geometry.Parse("M 0,0 L 3.5,0 L 3.5,14 L 0,14 Z M 6.5,0 L 10,0 L 10,14 L 6.5,14 Z");
                PlayPauseIcon.Width = 12;
                PlayPauseIcon.Height = 13;
            }
            else
            {
                // Triangolo play verso destra
                PlayPauseIcon.Data = Geometry.Parse("M 1,0 L 12,7 L 1,14 Z");
                PlayPauseIcon.Width = 13;
                PlayPauseIcon.Height = 13;
            }
        }

        private void LoadAndPlayVideo(string path)
        {
            try
            {
                VideoPlayer.Source = new Uri(path, UriKind.Absolute);
                VideoPlayer.Volume = _isMuted ? 0 : (VolumeSlider != null ? VolumeSlider.Value : 0.5);
                _isPlayingVideo = true;
                UpdatePlayPauseIcon(true);

                if (VideoPlayer.IsLoaded)
                {
                    VideoPlayer.Play();
                }
                else
                {
                    RoutedEventHandler onLoaded = null!;
                    onLoaded = (s, e) =>
                    {
                        VideoPlayer.Loaded -= onLoaded;
                        if (_isPlayingVideo && VideoPlayer.Source != null)
                        {
                            VideoPlayer.Play();
                        }
                    };
                    VideoPlayer.Loaded += onLoaded;
                }

                if (AudioVisualizerPanel != null && AudioVisualizerPanel.Visibility == Visibility.Visible)
                {
                    StartTurntableAnimation();
                }

                if (_videoTimer == null)
                {
                    _videoTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    _videoTimer.Tick += VideoTimer_Tick;
                }
                _videoTimer.Start();
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Errore avvio riproduzione video per {path}", ex);
            }
        }

        private void StopAndUnloadVideo()
        {
            _videoTimer?.Stop();
            _currentAudioFraction = 0.0;
            _currentVinylAngle = 0.0;
            StopTurntableAnimation();
            if (ToneArmRotateTransform != null)
            {
                ToneArmRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
                ToneArmRotateTransform.Angle = -18.0;
            }
            if (VinylRotateTransform != null)
            {
                VinylRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
                VinylRotateTransform.Angle = 0.0;
            }
            try
            {
                VideoPlayer.Stop();
                VideoPlayer.Close();
                VideoPlayer.Source = null;
            }
            catch { }
            _isPlayingVideo = false;
            _isScrubbing = false;
            UpdatePlayPauseIcon(false);
            VideoTimeText.Text = "00:00 / 00:00";
            SetTimelineFraction(0);
        }

        private void SetTimelineFraction(double fraction)
        {
            fraction = Math.Clamp(fraction, 0.0, 1.0);
            double totalWidth = TimelineBar.ActualWidth > 0 ? TimelineBar.ActualWidth : 190.0;
            double fillWidth = fraction * totalWidth;
            TimelineFill.Width = fillWidth;
            TimelineThumb.Margin = new Thickness(Math.Max(0, fillWidth - 5), 0, 0, 0);
        }

        private void TimelineBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!VideoPlayer.NaturalDuration.HasTimeSpan || VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds <= 0) return;

            TimelineBar.CaptureMouse();
            _isScrubbing = true;
            _wasPlayingBeforeScrub = _isPlayingVideo;
            if (_isPlayingVideo)
            {
                VideoPlayer.Pause();
            }

            double clickX = e.GetPosition(TimelineBar).X;
            ApplySeekFromX(clickX, isFinal: false);
            e.Handled = true;
        }

        private void TimelineBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isScrubbing || !TimelineBar.IsMouseCaptured) return;

            // Aggiorna subito la fill fraction visuale e la stanghetta per fluidità 60fps
            double moveX = e.GetPosition(TimelineBar).X;
            ApplySeekFromX(moveX, isFinal: false);
        }

        private void TimelineBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isScrubbing) return;

            _isScrubbing = false;
            if (TimelineBar.IsMouseCaptured)
            {
                TimelineBar.ReleaseMouseCapture();
            }

            double upX = e.GetPosition(TimelineBar).X;
            ApplySeekFromX(upX, isFinal: true);

            if (_wasPlayingBeforeScrub)
            {
                VideoPlayer.Play();
                _isPlayingVideo = true;
                UpdatePlayPauseIcon(true);
                _videoTimer?.Start();
                if (AudioVisualizerPanel != null && AudioVisualizerPanel.Visibility == Visibility.Visible)
                {
                    StartTurntableAnimation();
                }
            }

            e.Handled = true;
        }

        private void ApplySeekFromX(double x, bool isFinal)
        {
            if (TimelineBar.ActualWidth <= 0 || !VideoPlayer.NaturalDuration.HasTimeSpan) return;

            double totalSeconds = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            if (totalSeconds <= 0) return;

            double fraction = Math.Clamp(x / TimelineBar.ActualWidth, 0.0, 1.0);
            SetTimelineFraction(fraction);
            if (_currentCategory == PreviewCategory.Audio)
            {
                UpdateToneArmForProgress(fraction, animate: true);
            }

            var targetTime = TimeSpan.FromSeconds(fraction * totalSeconds);
            var totalTime = TimeSpan.FromSeconds(totalSeconds);
            VideoTimeText.Text = FormatPlaybackTime(targetTime, totalTime);

            if (isFinal)
            {
                _pendingSeekTarget = targetTime;
                VideoPlayer.Position = targetTime;
                _holdTimerUntil = DateTime.UtcNow.AddMilliseconds(1200);
            }
        }

        private void VideoTimer_Tick(object? sender, EventArgs e)
        {
            if (_isScrubbing) return;

            // Previene il rimbalzo (la sovrascrittura della UI prima che il player DirectShow abbia completato il seek)
            if (DateTime.UtcNow < _holdTimerUntil && Math.Abs((VideoPlayer.Position - _pendingSeekTarget).TotalSeconds) > 1.5)
            {
                return;
            }

            // Garanzia di avvio effettivo della riproduzione DirectShow se rimasto temporaneamente in pausa all'apertura
            if (_isPlayingVideo && VideoPlayer.Position == TimeSpan.Zero && VideoPlayer.NaturalDuration.HasTimeSpan && VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds > 0)
            {
                VideoPlayer.Play();
            }

            if (VideoPlayer.NaturalDuration.HasTimeSpan && VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds > 0)
            {
                var total = VideoPlayer.NaturalDuration.TimeSpan;
                var current = VideoPlayer.Position;

                double fraction = current.TotalSeconds / total.TotalSeconds;
                SetTimelineFraction(fraction);
                VideoTimeText.Text = FormatPlaybackTime(current, total);

                if (_currentCategory == PreviewCategory.Audio)
                {
                    UpdateToneArmForProgress(fraction, animate: false);
                }
            }
        }

        private void VideoContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }

            TogglePlayPause();
            e.Handled = true;
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePlayPause();
            e.Handled = true;
        }

        private void TogglePlayPause()
        {
            if (_isPlayingVideo)
            {
                _videoTimer?.Stop();
                VideoPlayer.Pause();
                _isPlayingVideo = false;
                UpdatePlayPauseIcon(false);
                StopTurntableAnimation();
            }
            else
            {
                VideoPlayer.Play();
                _isPlayingVideo = true;
                UpdatePlayPauseIcon(true);
                _videoTimer?.Start();
                if (AudioVisualizerPanel != null && AudioVisualizerPanel.Visibility == Visibility.Visible)
                {
                    StartTurntableAnimation();
                }
            }
        }

        public void ScrollContent(int delta)
        {
            if (TextView.Visibility == Visibility.Visible)
            {
                if (delta < 0)
                {
                    for (int i = 0; i < 3; i++) PreviewTextBox.LineDown();
                }
                else if (delta > 0)
                {
                    for (int i = 0; i < 3; i++) PreviewTextBox.LineUp();
                }
            }
            else if (MarkdownView.Visibility == Visibility.Visible)
            {
                _markdownScrollViewer ??= FindVisualChild<ScrollViewer>(MarkdownView);
                if (_markdownScrollViewer != null)
                {
                    if (delta < 0)
                    {
                        for (int i = 0; i < 3; i++) _markdownScrollViewer.LineDown();
                    }
                    else if (delta > 0)
                    {
                        for (int i = 0; i < 3; i++) _markdownScrollViewer.LineUp();
                    }
                }
            }
            else if (PdfView.Visibility == Visibility.Visible)
            {
                PdfView.ScrollToVerticalOffset(PdfView.VerticalOffset - (delta * 0.8));
            }
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (PdfView.Visibility == Visibility.Visible)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    ZoomPdf(e);
                    e.Handled = true;
                    return;
                }
                ScrollContent(e.Delta);
                e.Handled = true;
                return;
            }

            if (TextView.Visibility == Visibility.Visible || MarkdownView.Visibility == Visibility.Visible)
            {
                ScrollContent(e.Delta);
                e.Handled = true;
            }
        }

        private void PreviewTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollContent(e.Delta);
            e.Handled = true;
        }

        private void MarkdownView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollContent(e.Delta);
            e.Handled = true;
        }

        private void PdfView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ZoomPdf(e);
                e.Handled = true;
                return;
            }

            ScrollContent(e.Delta);
            e.Handled = true;
        }

        private void ZoomPdf(MouseWheelEventArgs e)
        {
            if (PdfView == null) return;

            double oldScale = _pdfScale;
            double zoomFactor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            double newScale = Math.Clamp(oldScale * zoomFactor, 0.4, 4.0);

            if (Math.Abs(newScale - oldScale) < 0.001) return;

            Point mousePos = e.GetPosition(PdfView);
            double contentX = PdfView.HorizontalOffset + mousePos.X;
            double contentY = PdfView.VerticalOffset + mousePos.Y;

            double ratio = newScale / oldScale;
            double targetHOffset = (contentX * ratio) - mousePos.X;
            double targetVOffset = (contentY * ratio) - mousePos.Y;

            _pdfScale = newScale;
            if (PdfScaleTransform != null)
            {
                PdfScaleTransform.ScaleX = newScale;
                PdfScaleTransform.ScaleY = newScale;
            }

            if (BtnResetZoom != null)
            {
                BtnResetZoom.Visibility = Math.Abs(_pdfScale - 1.0) > 0.05 ? Visibility.Visible : Visibility.Collapsed;
            }

            PdfView.UpdateLayout();
            PdfView.ScrollToHorizontalOffset(targetHOffset);
            PdfView.ScrollToVerticalOffset(targetVOffset);
        }

        private bool _isPanningPdf;
        private Point _pdfPanStart;
        private double _pdfStartHOffset;
        private double _pdfStartVOffset;

        private void PdfView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }

            if (_pdfScale > 1.0 || PdfView.ScrollableWidth > 0 || PdfView.ScrollableHeight > 0)
            {
                _isPanningPdf = true;
                _pdfPanStart = e.GetPosition(PdfView);
                _pdfStartHOffset = PdfView.HorizontalOffset;
                _pdfStartVOffset = PdfView.VerticalOffset;
                PdfView.CaptureMouse();
                PdfView.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        private void PdfView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanningPdf && PdfView.IsMouseCaptured)
            {
                Point current = e.GetPosition(PdfView);
                double dx = current.X - _pdfPanStart.X;
                double dy = current.Y - _pdfPanStart.Y;

                PdfView.ScrollToHorizontalOffset(_pdfStartHOffset - dx);
                PdfView.ScrollToVerticalOffset(_pdfStartVOffset - dy);
                e.Handled = true;
            }
        }

        private void PdfView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanningPdf)
            {
                _isPanningPdf = false;
                PdfView.ReleaseMouseCapture();
                PdfView.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private static string FormatPlaybackTime(TimeSpan current, TimeSpan total)
        {
            bool showHours = total.TotalHours >= 1.0 || current.TotalHours >= 1.0;
            string curStr = showHours
                ? $"{(int)current.TotalHours:D2}:{current.Minutes:D2}:{current.Seconds:D2}"
                : $"{current.Minutes:D2}:{current.Seconds:D2}";
            string totStr = showHours
                ? $"{(int)total.TotalHours:D2}:{total.Minutes:D2}:{total.Seconds:D2}"
                : $"{total.Minutes:D2}:{total.Seconds:D2}";
            return $"{curStr} / {totStr}";
        }

        private void UpdateToneArmForProgress(double fraction, bool animate = false)
        {
            if (ToneArmRotateTransform == null) return;
            _currentAudioFraction = Math.Clamp(fraction, 0.0, 1.0);
            if (!_isPlayingVideo) return; // Se in pausa/stop, la stanghetta rimane a riposo al lato (-18°)

            // Il braccio traccia i solchi del vinile dall'esterno all'interno:
            // 0% (inizio brano): 14.0° (bordo esterno del vinile)
            // 100% (fine brano): 32.0° (solco interno vicino all'etichetta)
            double targetAngle = 14.0 + (_currentAudioFraction * 18.0);

            if (animate)
            {
                var anim = new DoubleAnimation
                {
                    To = targetAngle,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ToneArmRotateTransform.BeginAnimation(RotateTransform.AngleProperty, anim);
            }
            else
            {
                ToneArmRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
                ToneArmRotateTransform.Angle = targetAngle;
            }
        }

        private void StartTurntableAnimation()
        {
            if (VinylRotateTransform == null || ToneArmRotateTransform == null) return;

            // 1. Sposta la stanghetta del giradischi sul vinile (dalla posizione di riposo al lato verso il solco corrente)
            double targetAngle = 14.0 + (_currentAudioFraction * 18.0);
            var armAnim = new DoubleAnimation
            {
                To = targetAngle,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ToneArmRotateTransform.BeginAnimation(RotateTransform.AngleProperty, armAnim);

            // 2. Ruota continuamente il vinile a 33 giri/min ripartendo dall'angolo congelato
            var spinAnim = new DoubleAnimation
            {
                From = _currentVinylAngle,
                To = _currentVinylAngle + 360.0,
                Duration = TimeSpan.FromSeconds(2.2), // ~33 RPM
                RepeatBehavior = RepeatBehavior.Forever
            };
            VinylRotateTransform.BeginAnimation(RotateTransform.AngleProperty, spinAnim);
        }

        private void StopTurntableAnimation()
        {
            if (VinylRotateTransform == null || ToneArmRotateTransform == null) return;

            // 1. La stanghetta torna al lato in posizione di riposo (-18°)
            var armAnim = new DoubleAnimation
            {
                To = -18.0,
                Duration = TimeSpan.FromMilliseconds(350),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ToneArmRotateTransform.BeginAnimation(RotateTransform.AngleProperty, armAnim);

            // 2. Ferma la rotazione del vinile congelando l'angolo esatto corrente (senza tornare centrata a 0°)
            double currentAngle = (double)VinylRotateTransform.GetValue(RotateTransform.AngleProperty);
            _currentVinylAngle = ((currentAngle % 360.0) + 360.0) % 360.0;
            VinylRotateTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            VinylRotateTransform.Angle = _currentVinylAngle;
        }

        private static T? FindVisualChild<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T match) return match;
                var childItem = FindVisualChild<T>(child);
                if (childItem != null) return childItem;
            }
            return null;
        }

        private void VolumeButton_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            if (_isMuted)
            {
                _lastVolume = VideoPlayer.Volume;
                VideoPlayer.Volume = 0;
                VolumeButton.Content = "🔇";
            }
            else
            {
                VideoPlayer.Volume = _lastVolume > 0 ? _lastVolume : 0.5;
                VolumeSlider.Value = VideoPlayer.Volume;
                VolumeButton.Content = "🔊";
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VideoPlayer != null)
            {
                VideoPlayer.Volume = e.NewValue;
                if (e.NewValue > 0)
                {
                    _isMuted = false;
                    if (VolumeButton != null) VolumeButton.Content = "🔊";
                }
                else
                {
                    _isMuted = true;
                    if (VolumeButton != null) VolumeButton.Content = "🔇";
                }
            }
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (_isPlayingVideo)
            {
                VideoPlayer.Play();
            }

            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var total = VideoPlayer.NaturalDuration.TimeSpan;
                SetTimelineFraction(0);
                VideoTimeText.Text = FormatPlaybackTime(TimeSpan.Zero, total);
            }

            if (VideoPlayer.NaturalVideoWidth > 0 && VideoPlayer.NaturalVideoHeight > 0)
            {
                HeaderSubtitle.Text = $"{VideoPlayer.NaturalVideoWidth} × {VideoPlayer.NaturalVideoHeight} • {_currentFileSizeText}";

                // Adatta dinamicamente le dimensioni della finestra al rapporto di aspetto nativo del video (nessuna banda nera)
                if (this.WindowState == WindowState.Normal)
                {
                    AdaptWindowToAspectRatio(VideoPlayer.NaturalVideoWidth, VideoPlayer.NaturalVideoHeight);
                }
            }
            else if (_currentCategory == PreviewCategory.Audio && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var total = VideoPlayer.NaturalDuration.TimeSpan;
                string durationStr = total.TotalHours >= 1 ? $"{(int)total.TotalHours:D2}:{total.Minutes:D2}:{total.Seconds:D2}" : $"{total:mm\\:ss}";
                HeaderSubtitle.Text = $"Audio ({durationStr}) • {_currentFileSizeText}";
            }
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Position = TimeSpan.Zero;
            VideoPlayer.Play(); // Loop continuo
        }

        private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            AppLogger.Warn($"Riproduzione video fallita: {e.ErrorException?.Message}");
            FallbackToGenericView(new PreviewModel { FilePath = _currentFilePath ?? string.Empty, Title = HeaderTitle.Text }, "Codec video non disponibile");
        }

        private void ImageView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double oldScale = _imageScale;
            double zoomFactor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            double newScale = Math.Clamp(oldScale * zoomFactor, 0.5, 10.0);

            if (Math.Abs(newScale - oldScale) < 0.001)
            {
                e.Handled = true;
                return;
            }

            Point mousePos = e.GetPosition(ImageView);
            _imageScale = newScale;

            if (ImageScaleTransform != null && ImageTranslateTransform != null)
            {
                if (newScale <= 1.05 && newScale >= 0.95)
                {
                    _imageScale = 1.0;
                    ImageScaleTransform.ScaleX = 1.0;
                    ImageScaleTransform.ScaleY = 1.0;
                    ImageTranslateTransform.X = 0;
                    ImageTranslateTransform.Y = 0;
                }
                else
                {
                    // Calcola il nuovo offset per zoomare esattamente ancorati al punto dove si trova il cursore
                    double ratio = newScale / oldScale;
                    ImageTranslateTransform.X = mousePos.X - (mousePos.X - ImageTranslateTransform.X) * ratio;
                    ImageTranslateTransform.Y = mousePos.Y - (mousePos.Y - ImageTranslateTransform.Y) * ratio;

                    ImageScaleTransform.ScaleX = newScale;
                    ImageScaleTransform.ScaleY = newScale;
                }
            }

            if (BtnResetZoom != null)
            {
                BtnResetZoom.Visibility = Math.Abs(_imageScale - 1.0) > 0.05 ? Visibility.Visible : Visibility.Collapsed;
            }
            e.Handled = true;
        }

        private void ImageView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }

            if (_imageScale > 1.0)
            {
                _isPanningImage = true;
                _imagePanStart = e.GetPosition(ImageView);
                ImageView.CaptureMouse();
                e.Handled = true;
            }
        }

        private void ImageView_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanningImage && ImageTranslateTransform != null)
            {
                Point current = e.GetPosition(ImageView);
                Vector diff = current - _imagePanStart;
                _imagePanStart = current;
                ImageTranslateTransform.X += diff.X;
                ImageTranslateTransform.Y += diff.Y;
                e.Handled = true;
            }
        }

        private void ImageView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanningImage)
            {
                _isPanningImage = false;
                ImageView.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            ResetZoom();
        }

        private void ResetZoom()
        {
            _imageScale = 1.0;
            _pdfScale = 1.0;
            if (ImageScaleTransform != null)
            {
                ImageScaleTransform.ScaleX = 1.0;
                ImageScaleTransform.ScaleY = 1.0;
            }
            if (ImageTranslateTransform != null)
            {
                ImageTranslateTransform.X = 0;
                ImageTranslateTransform.Y = 0;
            }
            if (PdfScaleTransform != null)
            {
                PdfScaleTransform.ScaleX = 1.0;
                PdfScaleTransform.ScaleY = 1.0;
            }

            if (BtnResetZoom != null)
            {
                BtnResetZoom.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void ToggleFullscreen()
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                BtnFullscreen.Content = "⛶";
                if (BtnVideoFullscreen != null) BtnVideoFullscreen.Content = "⛶";
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                BtnFullscreen.Content = "🗗";
                if (BtnVideoFullscreen != null) BtnVideoFullscreen.Content = "🗗";
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Space:
                    HidePreview();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    CloseAllRequested?.Invoke();
                    HidePreview();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    OpenInDefaultProgram();
                    CloseAllRequested?.Invoke();
                    HidePreview();
                    e.Handled = true;
                    break;

                case Key.F11:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;

                case Key.Up:
                    NavigateSelectionRequested?.Invoke(-1);
                    e.Handled = true;
                    break;

                case Key.Down:
                    NavigateSelectionRequested?.Invoke(1);
                    e.Handled = true;
                    break;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed && this.WindowState == WindowState.Normal)
            {
                this.DragMove();
                SaveWindowBounds();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HidePreview();
        }

        private void OpenDefaultButton_Click(object sender, RoutedEventArgs e)
        {
            OpenInDefaultProgram();
            HidePreview();
        }

        private void OpenInDefaultProgram()
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath)) return;

            try
            {
                AppLogger.Info($"Apertura file da anteprima: {_currentFilePath}");
                var psi = new ProcessStartInfo
                {
                    FileName = _currentFilePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Impossibile aprire il file {_currentFilePath}", ex);
            }
        }

        private void ResizeLeft_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized || _currentCategory == PreviewCategory.Folder) return;

            double minW = 380;
            double maxW = SystemParameters.PrimaryScreenWidth * 0.95;
            double newWidth = this.Width - e.HorizontalChange;

            if (newWidth >= minW && newWidth <= maxW)
            {
                this.Left += e.HorizontalChange;
                this.Width = newWidth;
                SaveUserCustomSize();
            }
        }

        private void ResizeRight_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized || _currentCategory == PreviewCategory.Folder) return;

            double minW = 380;
            double maxW = SystemParameters.PrimaryScreenWidth * 0.95;
            double newWidth = this.Width + e.HorizontalChange;

            if (newWidth >= minW && newWidth <= maxW)
            {
                this.Width = newWidth;
                SaveUserCustomSize();
            }
        }

        private void ResizeTop_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized || _currentCategory == PreviewCategory.Folder) return;

            double minH = 260;
            double maxH = SystemParameters.PrimaryScreenHeight * 0.95;
            double newHeight = this.Height - e.VerticalChange;

            if (newHeight >= minH && newHeight <= maxH)
            {
                this.Top += e.VerticalChange;
                this.Height = newHeight;
                SaveUserCustomSize();
            }
        }

        private void ResizeBottom_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized || _currentCategory == PreviewCategory.Folder) return;

            double minH = 260;
            double maxH = SystemParameters.PrimaryScreenHeight * 0.95;
            double newHeight = this.Height + e.VerticalChange;

            if (newHeight >= minH && newHeight <= maxH)
            {
                this.Height = newHeight;
                SaveUserCustomSize();
            }
        }

        private void ResizeTopLeft_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            ResizeLeft_DragDelta(sender, e);
            ResizeTop_DragDelta(sender, e);
        }

        private void ResizeTopRight_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            ResizeRight_DragDelta(sender, e);
            ResizeTop_DragDelta(sender, e);
        }

        private void ResizeBottomLeft_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            ResizeLeft_DragDelta(sender, e);
            ResizeBottom_DragDelta(sender, e);
        }

        private void ResizeBottomRight_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            ResizeRight_DragDelta(sender, e);
            ResizeBottom_DragDelta(sender, e);
        }

        private void SaveUserCustomSize()
        {
            if (_currentCategory == PreviewCategory.Folder) return;
            if (this.WindowState == WindowState.Normal && this.Width >= 300 && this.Height >= 200)
            {
                _sessionCustomSizes[_currentCategory] = (this.Width, this.Height);
                if (IsPersistentRememberActive(_currentCategory))
                {
                    SetPersistentSavedSize(_currentCategory, this.Width, this.Height);
                }
            }
        }

        private void SetupSpreadsheetView(SpreadsheetModel? model)
        {
            _currentSpreadsheetModel = model;
            if (SpreadsheetTabsPanel != null) SpreadsheetTabsPanel.Children.Clear();
            if (SpreadsheetDataGrid != null)
            {
                SpreadsheetDataGrid.Columns.Clear();
                SpreadsheetDataGrid.ItemsSource = null;
            }

            if (model == null || model.Sheets == null || model.Sheets.Count == 0)
            {
                return;
            }

            // Popola i tab per ciascun foglio di calcolo
            for (int i = 0; i < model.Sheets.Count; i++)
            {
                int sheetIdx = i;
                var sheet = model.Sheets[i];
                var tabBtn = new Button
                {
                    Content = sheet.Name,
                    Margin = new Thickness(0, 0, 8, 0),
                    Padding = new Thickness(14, 6, 14, 6),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand,
                    Tag = sheetIdx
                };

                var template = new ControlTemplate(typeof(Button));
                var elemFactory = new FrameworkElementFactory(typeof(Border));
                elemFactory.Name = "border";
                elemFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                elemFactory.SetValue(Border.BackgroundProperty, i == 0 ? new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)) : new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)));
                elemFactory.SetValue(Border.BorderBrushProperty, i == 0 ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)) : new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)));
                elemFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

                var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                elemFactory.AppendChild(contentPresenter);

                template.VisualTree = elemFactory;
                tabBtn.Template = template;
                tabBtn.Foreground = i == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

                tabBtn.Click += (s, e) => SwitchSpreadsheetSheet(sheetIdx);
                SpreadsheetTabsPanel?.Children.Add(tabBtn);
            }

            SwitchSpreadsheetSheet(0);
        }

        private void SwitchSpreadsheetSheet(int sheetIdx)
        {
            if (_currentSpreadsheetModel == null || sheetIdx < 0 || sheetIdx >= _currentSpreadsheetModel.Sheets.Count) return;
            _currentSpreadsheetSheetIndex = sheetIdx;

            if (SpreadsheetTabsPanel != null)
            {
                for (int i = 0; i < SpreadsheetTabsPanel.Children.Count; i++)
                {
                    if (SpreadsheetTabsPanel.Children[i] is Button btn)
                    {
                        bool isSelected = (i == sheetIdx);
                        if (btn.Template?.FindName("border", btn) is Border border)
                        {
                            border.Background = isSelected ? new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)) : new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
                            border.BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)) : new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));
                        }
                        btn.Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                    }
                }
            }

            var sheet = _currentSpreadsheetModel.Sheets[sheetIdx];
            if (SpreadsheetDataGrid == null) return;

            var dt = new DataTable();
            SpreadsheetDataGrid.Columns.Clear();

            for (int c = 0; c < sheet.ColumnHeaders.Count; c++)
            {
                string colName = sheet.ColumnHeaders[c];
                string colProp = $"Col_{c}";
                dt.Columns.Add(colProp, typeof(string));

                var gridCol = new DataGridTextColumn
                {
                    Header = colName,
                    Binding = new System.Windows.Data.Binding($"[{colProp}]"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    MinWidth = 60
                };
                SpreadsheetDataGrid.Columns.Add(gridCol);
            }

            foreach (var row in sheet.Rows)
            {
                var dr = dt.NewRow();
                for (int c = 0; c < row.Count && c < sheet.ColumnHeaders.Count; c++)
                {
                    dr[c] = row[c] ?? string.Empty;
                }
                dt.Rows.Add(dr);
            }

            SpreadsheetDataGrid.ItemsSource = dt.DefaultView;
        }

        private void SetupStl3DView(StlModel? stl)
        {
            if (StlViewport == null) return;
            StlViewport.Children.Clear();
            if (stl == null || stl.Mesh.Positions.Count == 0) return;

            _stlCenter = stl.Center;
            _stlDistance = stl.MaxDimension * 2.2;
            _stlTheta = 45;
            _stlPhi = 35;

            _stlCamera = new PerspectiveCamera
            {
                FieldOfView = 45,
                NearPlaneDistance = 0.1,
                FarPlaneDistance = Math.Max(100.0, stl.MaxDimension * 20)
            };
            UpdateStlCameraPosition();
            StlViewport.Camera = _stlCamera;

            var modelGroup = new Model3DGroup();

            // Luci direzionali e ambiente
            modelGroup.Children.Add(new AmbientLight(Color.FromRgb(70, 75, 85)));
            modelGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -1.5, -2)));
            modelGroup.Children.Add(new DirectionalLight(Color.FromRgb(90, 110, 140), new Vector3D(1, 1, 1.5)));

            // Materiale AeroSpot SkyBlue con riflesso speculare
            var materialGroup = new MaterialGroup();
            materialGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8))));
            materialGroup.Children.Add(new SpecularMaterial(Brushes.White, 35));

            var backMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7)));

            var geoModel = new GeometryModel3D
            {
                Geometry = stl.Mesh,
                Material = materialGroup,
                BackMaterial = backMaterial
            };
            modelGroup.Children.Add(geoModel);

            var visual = new ModelVisual3D { Content = modelGroup };
            StlViewport.Children.Add(visual);
        }

        private void UpdateStlCameraPosition()
        {
            if (_stlCamera == null) return;

            double radTheta = _stlTheta * Math.PI / 180.0;
            double radPhi = _stlPhi * Math.PI / 180.0;

            double x = _stlCenter.X + _stlDistance * Math.Cos(radPhi) * Math.Sin(radTheta);
            double y = _stlCenter.Y + _stlDistance * Math.Sin(radPhi);
            double z = _stlCenter.Z + _stlDistance * Math.Cos(radPhi) * Math.Cos(radTheta);

            _stlCamera.Position = new Point3D(x, y, z);
            _stlCamera.LookDirection = new Vector3D(_stlCenter.X - x, _stlCenter.Y - y, _stlCenter.Z - z);
            _stlCamera.UpDirection = new Vector3D(0, 1, 0);
        }

        private void StlViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (StlViewport == null) return;
            _stlLastMouse = e.GetPosition(StlViewport);
            _stlIsDragging = true;
            StlViewport.CaptureMouse();
        }

        private void StlViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_stlIsDragging || StlViewport == null) return;

            Point current = e.GetPosition(StlViewport);
            double dx = current.X - _stlLastMouse.X;
            double dy = current.Y - _stlLastMouse.Y;
            _stlLastMouse = current;

            _stlTheta -= dx * 0.7;
            _stlPhi = Math.Clamp(_stlPhi + dy * 0.7, -85, 85);

            UpdateStlCameraPosition();
        }

        private void StlViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_stlIsDragging && StlViewport != null)
            {
                _stlIsDragging = false;
                StlViewport.ReleaseMouseCapture();
            }
        }

        private void StlViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 0.88 : 1.13;
            _stlDistance = Math.Max(0.5, _stlDistance * factor);
            UpdateStlCameraPosition();
            e.Handled = true;
        }

        private bool IsPersistentRememberActive(PreviewCategory cat) => cat switch
        {
            PreviewCategory.Image => _config.RememberSizeImages,
            PreviewCategory.Video => _config.RememberSizeVideos,
            PreviewCategory.Audio => _config.RememberSizeAudio,
            PreviewCategory.Pdf => _config.RememberSizePdf,
            PreviewCategory.Document => _config.RememberSizeDocuments,
            PreviewCategory.Generic => _config.RememberSizeGeneric,
            _ => false
        };

        private (double Width, double Height)? GetPersistentSavedSize(PreviewCategory cat)
        {
            if (!IsPersistentRememberActive(cat)) return null;
            (double w, double h) = cat switch
            {
                PreviewCategory.Image => (_config.SavedWidthImage, _config.SavedHeightImage),
                PreviewCategory.Video => (_config.SavedWidthVideo, _config.SavedHeightVideo),
                PreviewCategory.Audio => (_config.SavedWidthAudio, _config.SavedHeightAudio),
                PreviewCategory.Pdf => (_config.SavedWidthPdf, _config.SavedHeightPdf),
                PreviewCategory.Document => (_config.SavedWidthDocument, _config.SavedHeightDocument),
                PreviewCategory.Generic => (_config.SavedWidthGeneric, _config.SavedHeightGeneric),
                _ => (0, 0)
            };
            if (w >= 300 && h >= 200) return (w, h);
            return null;
        }

        private void SetPersistentSavedSize(PreviewCategory cat, double w, double h)
        {
            switch (cat)
            {
                case PreviewCategory.Image:
                    _config.SavedWidthImage = w;
                    _config.SavedHeightImage = h;
                    break;
                case PreviewCategory.Video:
                    _config.SavedWidthVideo = w;
                    _config.SavedHeightVideo = h;
                    break;
                case PreviewCategory.Audio:
                    _config.SavedWidthAudio = w;
                    _config.SavedHeightAudio = h;
                    break;
                case PreviewCategory.Pdf:
                    _config.SavedWidthPdf = w;
                    _config.SavedHeightPdf = h;
                    break;
                case PreviewCategory.Document:
                    _config.SavedWidthDocument = w;
                    _config.SavedHeightDocument = h;
                    break;
                case PreviewCategory.Generic:
                    _config.SavedWidthGeneric = w;
                    _config.SavedHeightGeneric = h;
                    break;
            }
            _config.Save();
        }

        private void TogglePersistentRemember(PreviewCategory cat)
        {
            if (cat == PreviewCategory.Folder) return;
            bool newVal = !IsPersistentRememberActive(cat);
            switch (cat)
            {
                case PreviewCategory.Image: _config.RememberSizeImages = newVal; break;
                case PreviewCategory.Video: _config.RememberSizeVideos = newVal; break;
                case PreviewCategory.Audio: _config.RememberSizeAudio = newVal; break;
                case PreviewCategory.Pdf: _config.RememberSizePdf = newVal; break;
                case PreviewCategory.Document: _config.RememberSizeDocuments = newVal; break;
                case PreviewCategory.Generic: _config.RememberSizeGeneric = newVal; break;
            }
            if (newVal)
            {
                SetPersistentSavedSize(cat, this.Width, this.Height);
            }
            else
            {
                _config.Save();
            }
            UpdateRememberSizeButtonUi();
        }

        private void UpdateRememberSizeButtonUi()
        {
            if (_currentCategory == PreviewCategory.Folder)
            {
                BtnRememberSize.Visibility = Visibility.Collapsed;
                return;
            }
            BtnRememberSize.Visibility = Visibility.Visible;
            bool isActive = IsPersistentRememberActive(_currentCategory);
            BtnRememberSize.Foreground = isActive
                ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8))
                : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        }

        private void BtnRememberSize_Click(object sender, RoutedEventArgs e)
        {
            TogglePersistentRemember(_currentCategory);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!App.IsShuttingDown)
            {
                e.Cancel = true;
                HidePreview();
            }
        }
    }
}
