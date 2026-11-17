using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using AeroSpot.Core.Logging;
using AeroSpot.Core.Native;
using AeroSpot.Core.Localization;
using AeroSpot.Worker.Controls;
using AeroSpot.Worker.Services;

namespace AeroSpot.Worker
{
    public partial class PreviewWindow : Window
    {
        private string? _currentFilePath;
        private PreviewModel? _currentModel;
        private CancellationTokenSource? _folderSizeCts;
        private DispatcherTimer? _videoTimer;
        private bool _isScrubbing;
        private bool _wasPlayingBeforeScrub;
        private bool _isPlayingVideo;
        private bool _playRequested;
        private bool _hasRetriedPlay;
        private bool _isMuted;
        private double _lastVolume = 0.5;
        private AeroSpot.Core.Config.AppConfig _config;
        private int _loadRequestId = 0;
        private ScrollViewer? _markdownScrollViewer;
        private double _currentAudioFraction = 0.0;
        private double _currentVinylAngle = 0.0;

        private IntPtr _hwnd = IntPtr.Zero;
        private IntPtr _previousForegroundHwnd = IntPtr.Zero;
        public enum PreviewState
        {
            Hidden,
            Loading,
            Visible,
            Closing
        }

        public PreviewState CurrentState { get; private set; } = PreviewState.Hidden;
        public bool IsPreviewVisible { get; private set; }

        private TimeSpan _pendingSeekTarget = TimeSpan.Zero;
        private DateTime _holdTimerUntil = DateTime.MinValue;
        private DateTime _lastScrubThrottle = DateTime.MinValue;

        private double _imageScale = 1.0;
        private Point _imagePanStart;
        private bool _isPanningImage;
        private string _currentFileSizeText = string.Empty;
        private CancellationTokenSource? _remuxCts;

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
        private bool _isUpdatingHandlerScrollBar;

        private PdfSession? _currentPdfSession;
        private int _currentPdfPageIndex = 0;
        private int _totalPdfPages = 1;
        private double _pdfScale = 1.0;
        private bool _isPanningPdf = false;
        private Point _pdfPanStart;
        private double _startHOffset = 0;
        private double _startVOffset = 0;
        private readonly System.Collections.Generic.Dictionary<int, BitmapSource> _renderedPagesCache = new();
        private readonly System.Collections.Generic.HashSet<int> _loadingPages = new();
        private readonly System.Collections.Generic.Dictionary<int, BitmapSource> _thumbnailsCache = new();
        private readonly System.Collections.Generic.HashSet<int> _loadingThumbnails = new();

        public bool IsLaunchedFromSpotlight { get; set; }
        public event Action<int>? NavigateSelectionRequested;
        public event Action? HideRequested;
        public event Action? CloseAllRequested;
        public event Action<string>? PreviewOpened;
        public event Action? PreviewClosed;
        public event Action<string>? FileLaunched;

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

            this.Activated += (s, e) =>
            {
                this.Focus();
                Keyboard.Focus(this);
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
            if (CurrentState == PreviewState.Visible && string.Equals(_currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                // Se QuickLook è attiva o Topmost (visibile sopra Explorer), Spazio la chiude (toggle).
                // Se invece aveva perso il focus ed era finita nascosta dietro Explorer (Topmost == false),
                // non chiuderla ma riportala subito in primo piano!
                if (this.IsActive || this.Topmost)
                {
                    HidePreview();
                    return;
                }

                try
                {
                    this.Topmost = false;
                    this.Topmost = true;
                    if (_hwnd != IntPtr.Zero)
                    {
                        Win32.ForceForegroundWindow(_hwnd, topmost: true);
                    }
                    this.Activate();
                    this.Focus();
                }
                catch { }
                return;
            }

            if (CurrentState == PreviewState.Loading && string.Equals(_currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                // Toggle rapido durante il caricamento: l'utente ha ripremuto Spazio per annullare l'anteprima
                HidePreview();
                return;
            }

            if (CurrentState == PreviewState.Closing)
            {
                return;
            }

            CurrentState = PreviewState.Loading;

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

            // Se nel frattempo l'utente ha premuto una freccia o chiuso l'anteprima, scarta il modello vecchio
            if (requestId != _loadRequestId || CurrentState != PreviewState.Loading)
            {
                return;
            }

            Func<Task> applyAction = async () =>
            {
                if (requestId != _loadRequestId || CurrentState != PreviewState.Loading) return;

                bool wasVisible = IsPreviewVisible;

                if (!wasVisible)
                {
                    this.Opacity = 0;
                    if (_hwnd != IntPtr.Zero)
                    {
                        Win32.SetWindowCloak(_hwnd, true);
                    }
                }

                ResetZoom();
                ApplyModelToUI(model);
                PositionAndSizeWindow(model);

                if (!wasVisible && !this.IsVisible)
                {
                    this.Show();
                }

                this.UpdateLayout();

                if (model.Type == PreviewType.Pdf)
                {
                    PdfPagesStack?.InvalidateMeasure();
                    PdfPagesContainer?.InvalidateMeasure();
                    PdfScrollViewer?.InvalidateMeasure();
                    PdfPagesContainer?.UpdateLayout();
                    PdfScrollViewer?.UpdateLayout();
                    this.UpdateLayout();
                    UpdateVisiblePdfPages();
                }


                if (!wasVisible)
                {
                    await WaitRenderedAsync();

                    if (requestId != _loadRequestId || CurrentState != PreviewState.Loading)
                    {
                        AppLogger.Debug($"ShowPreviewForFileAsync: richiesta annullata durante WaitRenderedAsync (req={requestId}, current={_loadRequestId}, state={CurrentState})");
                        return;
                    }

                    if (_hwnd != IntPtr.Zero)
                    {
                        Win32.SetWindowCloak(_hwnd, false);
                    }
                    this.Opacity = 1;
                    IsPreviewVisible = true;
                    CurrentState = PreviewState.Visible;

                    if (_isPlayingVideo)
                    {
                        VideoPlayer.Play();
                    }
                }
                else
                {
                    this.Opacity = 1;
                    CurrentState = PreviewState.Visible;
                    this.UpdateLayout();
                }

                if (this.IsVisible)
                {
                    try
                    {
                        this.Topmost = false;
                        this.Topmost = true;
                        if (_hwnd != IntPtr.Zero)
                        {
                            Win32.ForceForegroundWindow(_hwnd, topmost: true);
                        }
                        this.Activate();
                        this.Focus();
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
            int frames = 0;
            handler = (s, e) =>
            {
                frames++;
                if (frames >= 2)
                {
                    CompositionTarget.Rendering -= handler;
                    tcs.TrySetResult();
                }
            };
            CompositionTarget.Rendering += handler;
            await Task.WhenAny(tcs.Task, Task.Delay(60));
            CompositionTarget.Rendering -= handler;
            await Task.Delay(16);
        }

        private void ApplyModelToUI(PreviewModel model)
        {
            _currentModel = model;
            // Pulisci viste e timer precedenti
            _folderSizeCts?.Cancel();
            _folderSizeCts?.Dispose();
            _folderSizeCts = null;

            StopAndUnloadVideo();

            // La rotazione manuale è per-anteprima: si azzera ad ogni nuovo file
            _manualRotation = 0;
            PreviewImage.LayoutTransform = Transform.Identity;
            if (BtnRotate != null)
            {
                BtnRotate.Visibility = (model.Type == PreviewType.Image || model.Type == PreviewType.Video)
                    ? Visibility.Visible : Visibility.Collapsed;
            }

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
            if (StlView != null) StlView.Visibility = Visibility.Collapsed;
            if (StlViewport != null) StlViewport.Children.Clear();
            if (HtmlView != null) HtmlView.Visibility = Visibility.Collapsed;
            if (HtmlDocumentViewer != null) HtmlDocumentViewer.Document = null;
            if (HtmlCodeTextBox != null) HtmlCodeTextBox.Text = string.Empty;
            HandlerView.Visibility = Visibility.Collapsed;
            if (HandlerHorizontalScrollBar != null) HandlerHorizontalScrollBar.Visibility = Visibility.Collapsed;
            if (model.Type != PreviewType.Pdf)
            {
                CleanupPdfView();
                if (PdfView != null) PdfView.Visibility = Visibility.Collapsed;
            }
            GenericView.Visibility = Visibility.Collapsed;
            ErrorView.Visibility = Visibility.Collapsed;
            if (BtnExportPdf != null) BtnExportPdf.Visibility = Visibility.Collapsed;

            PreviewTextBox.Text = string.Empty;

            FolderSizeLoadingPanel.Visibility = Visibility.Collapsed;
            FolderSizeCalculatedText.Visibility = Visibility.Collapsed;

            CleanupHandlerHost();

            _currentFileSizeText = model.FileSizeText;
            HeaderTitle.Text = model.Title;
            HeaderSubtitle.Text = model.Subtitle;
            FooterDetails.Text = !string.IsNullOrWhiteSpace(model.FooterText) ? model.FooterText : model.DetailsText;

            // Recupera l'icona reale di sistema
            var icon = IconService.GetIcon(model.FilePath, model.Type == PreviewType.Image ? "Immagine" : (model.Type == PreviewType.Audio ? "Audio" : (model.Type == PreviewType.Stl3D ? "Modello 3D" : (model.IsFolder ? "Cartella" : "File"))));
            FileIcon.Source = icon;

            switch (model.Type)
            {
                case PreviewType.Image:
                    // Sorgente e visibilità già assegnate sopra in modo atomico senza flash nero
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

                case PreviewType.Html:
                    if (HtmlView != null) HtmlView.Visibility = Visibility.Visible;
                    SetupHtmlView(model);
                    break;

                case PreviewType.Text:
                    TextView.Visibility = Visibility.Visible;
                    if (BtnExportPdf != null) BtnExportPdf.Visibility = Visibility.Visible;
                    PreviewTextBox.Text = model.TextContent ?? string.Empty;
                    break;

                case PreviewType.Markdown:
                    MarkdownView.Visibility = Visibility.Visible;
                    if (BtnExportPdf != null) BtnExportPdf.Visibility = Visibility.Visible;
                    MarkdownView.Document = MarkdownToFlowDocumentConverter.Convert(model.TextContent ?? string.Empty);
                    break;

                case PreviewType.Spreadsheet:
                    SpreadsheetView.Visibility = Visibility.Visible;
                    SetupSpreadsheetView(model.Spreadsheet);
                    break;

                case PreviewType.Pdf:
                    if (PdfView != null) PdfView.Visibility = Visibility.Visible;
                    SetupPdfView(model);
                    break;

                case PreviewType.PreviewHandler:
                    SetupShellPreviewHandler(model);
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

            if (!string.IsNullOrEmpty(model.FilePath) && Path.GetExtension(model.FilePath).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                if (BtnExportPdf != null) BtnExportPdf.Visibility = Visibility.Visible;
            }
        }

        private void CleanupHandlerHost()
        {
            var oldChild = HandlerHostBorder?.Child;
            if (HandlerHostBorder != null)
            {
                HandlerHostBorder.Child = null;
            }

            if (oldChild is PreviewHandlerHostControl oldHost)
            {
                try { oldHost.UnloadHandler(); } catch { }
                try { oldHost.Dispose(); } catch { }
            }
        }

        private void SetupShellPreviewHandler(PreviewModel model)
        {
            if (model.PreviewHandlerClsid == Guid.Empty)
            {
                FallbackToGenericView(model, "Impossibile attivare l'anteprima");
                return;
            }

            try
            {
                var hostControl = new PreviewHandlerHostControl(model.FilePath, model.PreviewHandlerClsid);
                hostControl.CloseRequested += HidePreview;
                hostControl.ViewportChanged += (scale, hFraction, viewportRatio) =>
                {
                    if (BtnResetZoom != null)
                    {
                        BtnResetZoom.Visibility = Math.Abs(scale - 1.0) > 0.05 ? Visibility.Visible : Visibility.Collapsed;
                    }

                    if (HandlerHorizontalScrollBar != null)
                    {
                        if (scale > 1.02)
                        {
                            _isUpdatingHandlerScrollBar = true;
                            try
                            {
                                HandlerHorizontalScrollBar.Visibility = Visibility.Visible;
                                HandlerHorizontalScrollBar.ViewportSize = Math.Clamp(viewportRatio, 0.1, 1.0);
                                HandlerHorizontalScrollBar.Value = Math.Clamp(hFraction, 0.0, 1.0);
                            }
                            finally
                            {
                                _isUpdatingHandlerScrollBar = false;
                            }
                        }
                        else
                        {
                            HandlerHorizontalScrollBar.Visibility = Visibility.Collapsed;
                        }
                    }
                };
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
        }


        private void SetupPdfView(PreviewModel model)
        {
            CleanupPdfView();

            _currentPdfSession = model.PdfSession;
            if (_currentPdfSession == null && !string.IsNullOrEmpty(model.FilePath) && File.Exists(model.FilePath))
            {
                _currentPdfSession = PdfService.Open(model.FilePath, defaultScale: 2.0);
            }

            _currentPdfPageIndex = 0;
            _pdfScale = 1.0;
            _isPanningPdf = false;

            if (PdfScaleTransform != null)
            {
                PdfScaleTransform.ScaleX = 1.0;
                PdfScaleTransform.ScaleY = 1.0;
            }
            if (BtnResetZoom != null)
            {
                BtnResetZoom.Visibility = Visibility.Collapsed;
            }
            if (PdfScrollViewer != null)
            {
                PdfScrollViewer.ScrollToHome();
                PdfScrollViewer.Cursor = Cursors.Arrow;
            }

            int totalPages = _currentPdfSession?.PageCount ?? model.PdfPageCount;
            if (totalPages <= 0) totalPages = 1;
            _totalPdfPages = totalPages;

            double baseW = model.PdfWidth > 0 ? model.PdfWidth : 595.0;
            double baseH = model.PdfHeight > 0 ? model.PdfHeight : 842.0;

            // Inquadratura pagina base: adatta all'ampiezza confortevole della finestra (es. 680-880px)
            double pageDisplayWidth = Math.Clamp(baseW * 1.25, 560, 920);
            double pageDisplayHeight = pageDisplayWidth * (baseH / baseW);

            if (PdfPagesStack != null)
            {
                PdfPagesStack.Children.Clear();

                for (int i = 0; i < totalPages; i++)
                {
                    // Foglio di carta bianco candido con ombra elegante in stile Apple/QuickLook
                    var pageBorder = new Border
                    {
                        Width = pageDisplayWidth,
                        Height = pageDisplayHeight,
                        Background = Brushes.White, // SFONDO BIANCO PURO!
                        BorderBrush = new SolidColorBrush(Color.FromArgb(35, 0, 0, 0)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(2),
                        Margin = new Thickness(0, 0, 0, 16),
                        SnapsToDevicePixels = true,
                        Tag = i
                    };

                    pageBorder.Effect = new DropShadowEffect
                    {
                        BlurRadius = 14,
                        Direction = 270,
                        ShadowDepth = 3,
                        Color = Colors.Black,
                        Opacity = 0.35
                    };

                    var pageImg = new Image
                    {
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        SnapsToDevicePixels = true
                    };
                    RenderOptions.SetBitmapScalingMode(pageImg, BitmapScalingMode.HighQuality);
                    pageBorder.Child = pageImg;

                    PdfPagesStack.Children.Add(pageBorder);
                }

                // Assegna subito la prima pagina già renderizzata
                if (totalPages > 0 && PdfPagesStack.Children[0] is Border b0 && b0.Child is Image img0)
                {
                    if (model.Image is BitmapSource bmp0)
                    {
                        img0.Source = bmp0;
                        _renderedPagesCache[0] = bmp0;
                    }
                    else if (_currentPdfSession != null)
                    {
                        var rendered = _currentPdfSession.RenderPage(0, customScale: 2.0);
                        if (rendered != null)
                        {
                            img0.Source = rendered;
                            _renderedPagesCache[0] = rendered;
                        }
                    }
                }

                PdfPagesStack.InvalidateMeasure();
                PdfPagesStack.InvalidateArrange();
                if (PdfPagesContainer != null)
                {
                    PdfPagesContainer.InvalidateMeasure();
                    PdfPagesContainer.InvalidateArrange();
                }
                if (PdfScrollViewer != null)
                {
                    PdfScrollViewer.InvalidateMeasure();
                    PdfScrollViewer.InvalidateScrollInfo();
                    PdfScrollViewer.ScrollToHome();
                    PdfScrollViewer.ScrollToVerticalOffset(0);
                    PdfScrollViewer.ScrollToHorizontalOffset(0);
                }
            }

            if (totalPages > 1)
            {
                if (PdfSidebar != null) PdfSidebar.Visibility = Visibility.Visible;
                if (PdfSidebarColumn != null) PdfSidebarColumn.Width = new GridLength(144);
                if (PdfPageCapsule != null) PdfPageCapsule.Visibility = Visibility.Visible;
            }
            else
            {
                if (PdfSidebar != null) PdfSidebar.Visibility = Visibility.Collapsed;
                if (PdfSidebarColumn != null) PdfSidebarColumn.Width = new GridLength(0);
                if (PdfPageCapsule != null) PdfPageCapsule.Visibility = Visibility.Collapsed;
            }

            // Popola la barra laterale con le miniature delle pagine
            if (PdfThumbnailsStack != null)
            {
                PdfThumbnailsStack.Children.Clear();

                if (totalPages > 1)
                {
                    double thumbW = 94.0;
                    double thumbH = Math.Clamp(thumbW * (baseH / baseW), 60.0, 140.0);

                    for (int i = 0; i < totalPages; i++)
                    {
                        int pageIndex = i;
                        var itemCard = new Border
                        {
                            CornerRadius = new CornerRadius(6),
                            Padding = new Thickness(6, 6, 6, 4),
                            Margin = new Thickness(0, 0, 0, 8),
                            Background = i == 0 ? new SolidColorBrush(Color.FromArgb(30, 0x38, 0xBD, 0xF8)) : Brushes.Transparent,
                            BorderBrush = i == 0 ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)) : Brushes.Transparent,
                            BorderThickness = new Thickness(1.5),
                            Cursor = Cursors.Hand,
                            Tag = pageIndex
                        };

                        var sp = new StackPanel
                        {
                            Orientation = Orientation.Vertical,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };

                        var thumbPaper = new Border
                        {
                            Width = thumbW,
                            Height = thumbH,
                            Background = Brushes.White,
                            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(2),
                            SnapsToDevicePixels = true
                        };

                        thumbPaper.Effect = new DropShadowEffect
                        {
                            BlurRadius = 6,
                            Direction = 270,
                            ShadowDepth = 1.5,
                            Color = Colors.Black,
                            Opacity = 0.3
                        };

                        var thumbImg = new Image
                        {
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            SnapsToDevicePixels = true
                        };
                        RenderOptions.SetBitmapScalingMode(thumbImg, BitmapScalingMode.LowQuality);
                        thumbPaper.Child = thumbImg;
                        sp.Children.Add(thumbPaper);

                        var pageLabel = new TextBlock
                        {
                            Text = (i + 1).ToString(),
                            Foreground = i == 0 ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)) : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                            FontSize = 10.5,
                            FontWeight = i == 0 ? FontWeights.Bold : FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Margin = new Thickness(0, 4, 0, 0),
                            FontFamily = new FontFamily("Segoe UI, -apple-system, Cascadia Mono")
                        };
                        sp.Children.Add(pageLabel);

                        itemCard.Child = sp;

                        itemCard.MouseLeftButtonDown += (s, e) =>
                        {
                            if (s is FrameworkElement fe && fe.Tag is int idx)
                            {
                                _currentPdfPageIndex = idx;
                                UpdatePdfPageIndicator(idx, totalPages);
                                ScrollToPage(idx);
                            }
                        };

                        itemCard.MouseEnter += (s, e) =>
                        {
                            if (s is Border b && b.Tag is int idx && idx != _currentPdfPageIndex)
                            {
                                b.Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
                            }
                        };
                        itemCard.MouseLeave += (s, e) =>
                        {
                            if (s is Border b && b.Tag is int idx && idx != _currentPdfPageIndex)
                            {
                                b.Background = Brushes.Transparent;
                            }
                        };

                        PdfThumbnailsStack.Children.Add(itemCard);
                    }
                }
            }

            UpdatePdfPageIndicator(0, totalPages);

            // Carica le prime pagine in background
            UpdateVisiblePdfPages();

            // Carica le prime miniature in background
            if (totalPages > 1)
            {
                int initThumbCount = Math.Min(6, totalPages);
                for (int t = 0; t < initThumbCount; t++)
                {
                    EnsureThumbnailLoaded(t);
                }
            }
        }

        private void EnsureThumbnailLoaded(int pageIndex)
        {
            if (_currentPdfSession == null || pageIndex < 0 || pageIndex >= _totalPdfPages) return;
            if (_thumbnailsCache.ContainsKey(pageIndex) || _loadingThumbnails.Contains(pageIndex)) return;

            _loadingThumbnails.Add(pageIndex);
            _ = Task.Run(() =>
            {
                var bmp = _currentPdfSession?.RenderPage(pageIndex, customScale: 0.25);
                Dispatcher.InvokeAsync(() =>
                {
                    _loadingThumbnails.Remove(pageIndex);
                    if (_currentPdfSession != null && bmp != null)
                    {
                        _thumbnailsCache[pageIndex] = bmp;
                        if (PdfThumbnailsStack != null && pageIndex < PdfThumbnailsStack.Children.Count)
                        {
                            if (PdfThumbnailsStack.Children[pageIndex] is Border card &&
                                card.Child is StackPanel sp &&
                                sp.Children.Count > 0 &&
                                sp.Children[0] is Border paper &&
                                paper.Child is Image img)
                            {
                                img.Source = bmp;
                            }
                        }
                    }
                });
            });
        }

        private void PdfThumbnailsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateVisibleThumbnails();
        }

        private void UpdateVisibleThumbnails()
        {
            if (PdfThumbnailsScrollViewer == null || PdfThumbnailsStack == null || _totalPdfPages <= 0) return;

            double vOffset = PdfThumbnailsScrollViewer.VerticalOffset;
            double viewportH = PdfThumbnailsScrollViewer.ViewportHeight;

            double accumulated = 0;
            for (int i = 0; i < PdfThumbnailsStack.Children.Count; i++)
            {
                if (PdfThumbnailsStack.Children[i] is FrameworkElement fe)
                {
                    double h = fe.ActualHeight > 0 ? fe.ActualHeight : 140;
                    if (accumulated + h >= vOffset - 120 && accumulated <= vOffset + viewportH + 120)
                    {
                        EnsureThumbnailLoaded(i);
                    }
                    accumulated += h;
                }
            }
        }

        private void HighlightThumbnail(int activeIndex)
        {
            if (PdfThumbnailsStack == null || PdfThumbnailsStack.Children.Count == 0) return;

            for (int i = 0; i < PdfThumbnailsStack.Children.Count; i++)
            {
                if (PdfThumbnailsStack.Children[i] is Border card && card.Child is StackPanel sp)
                {
                    bool isActive = (i == activeIndex);
                    card.BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)) : Brushes.Transparent;
                    card.Background = isActive ? new SolidColorBrush(Color.FromArgb(30, 0x38, 0xBD, 0xF8)) : Brushes.Transparent;

                    if (sp.Children.Count > 1 && sp.Children[1] is TextBlock lbl)
                    {
                        lbl.Foreground = isActive ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)) : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
                        lbl.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
                    }

                    if (isActive)
                    {
                        card.BringIntoView();
                    }
                }
            }
        }

        private void PdfScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateVisiblePdfPages();
        }

        private void UpdateVisiblePdfPages()
        {
            if (_currentPdfSession == null || PdfPagesStack == null || PdfPagesStack.Children.Count == 0 || PdfScrollViewer == null) return;

            int total = PdfPagesStack.Children.Count;
            double vOffset = PdfScrollViewer.VerticalOffset;
            double viewportH = PdfScrollViewer.ViewportHeight;
            double scale = PdfScaleTransform != null ? PdfScaleTransform.ScaleY : 1.0;
            if (scale < 0.1) scale = 1.0;

            // Rileva la pagina attualmente più visibile nel viewport
            int currentPage = 0;
            double accumulatedHeight = 0;
            for (int i = 0; i < total; i++)
            {
                if (PdfPagesStack.Children[i] is FrameworkElement fe)
                {
                    double itemH = (fe.Height + fe.Margin.Bottom) * scale;
                    if (vOffset >= accumulatedHeight - (viewportH * 0.4))
                    {
                        currentPage = i;
                    }
                    accumulatedHeight += itemH;
                }
            }

            _currentPdfPageIndex = currentPage;
            UpdatePdfPageIndicator(currentPage, total);

            // Carica la pagina visibile e le 2 adiacenti
            int startPage = Math.Max(0, currentPage - 1);
            int endPage = Math.Min(total - 1, currentPage + 2);

            for (int p = startPage; p <= endPage; p++)
            {
                if (!_renderedPagesCache.ContainsKey(p) && !_loadingPages.Contains(p))
                {
                    int pageToLoad = p;
                    _loadingPages.Add(pageToLoad);
                    _ = Task.Run(() =>
                    {
                        var bmp = _currentPdfSession?.RenderPage(pageToLoad, customScale: 2.0);
                        Dispatcher.InvokeAsync(() =>
                        {
                            _loadingPages.Remove(pageToLoad);
                            if (_currentPdfSession != null && pageToLoad < PdfPagesStack.Children.Count)
                            {
                                if (bmp != null)
                                {
                                    _renderedPagesCache[pageToLoad] = bmp;
                                    if (PdfPagesStack.Children[pageToLoad] is Border b && b.Child is Image img)
                                    {
                                        img.Source = bmp;
                                    }
                                }
                            }
                        });
                    });
                }
            }

            // Bonifica LRU delle pagine lontane se la cache supera le 16 pagine
            if (_renderedPagesCache.Count > 16)
            {
                var toRemove = new System.Collections.Generic.List<int>();
                foreach (var kvp in _renderedPagesCache)
                {
                    if (kvp.Key < startPage - 4 || kvp.Key > endPage + 4)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
                foreach (int rem in toRemove)
                {
                    _renderedPagesCache.Remove(rem);
                    if (rem < PdfPagesStack.Children.Count && PdfPagesStack.Children[rem] is Border b && b.Child is Image img)
                    {
                        img.Source = null;
                    }
                }
            }
        }

        private void UpdatePdfPageIndicator(int currentZeroBased, int total)
        {
            if (PdfPageIndicator != null)
            {
                int currentDisplay = total > 0 ? (currentZeroBased + 1) : 1;
                PdfPageIndicator.Text = $"{currentDisplay} / {Math.Max(1, total)}";
            }
            if (BtnPdfPrevPage != null)
            {
                BtnPdfPrevPage.IsEnabled = currentZeroBased > 0;
                BtnPdfPrevPage.Opacity = currentZeroBased > 0 ? 1.0 : 0.4;
            }
            if (BtnPdfNextPage != null)
            {
                BtnPdfNextPage.IsEnabled = currentZeroBased < total - 1;
                BtnPdfNextPage.Opacity = currentZeroBased < total - 1 ? 1.0 : 0.4;
            }

            HighlightThumbnail(currentZeroBased);
            EnsureThumbnailLoaded(currentZeroBased);
        }

        private void NextPdfPage()
        {
            if (_totalPdfPages <= 0) return;
            int next = Math.Min(_totalPdfPages - 1, _currentPdfPageIndex + 1);
            _currentPdfPageIndex = next;
            UpdatePdfPageIndicator(next, _totalPdfPages);
            ScrollToPage(next);
        }

        private void PrevPdfPage()
        {
            if (_totalPdfPages <= 0) return;
            int prev = Math.Max(0, _currentPdfPageIndex - 1);
            _currentPdfPageIndex = prev;
            UpdatePdfPageIndicator(prev, _totalPdfPages);
            ScrollToPage(prev);
        }

        private void ScrollToPage(int pageIndex)
        {
            if (PdfPagesStack == null || pageIndex < 0 || pageIndex >= PdfPagesStack.Children.Count || PdfScrollViewer == null) return;
            if (PdfPagesStack.Children[pageIndex] is FrameworkElement targetBorder)
            {
                try
                {
                    GeneralTransform transform = targetBorder.TransformToAncestor(PdfPagesContainer);
                    Point point = transform.Transform(new Point(0, 0));
                    double scale = PdfScaleTransform != null ? PdfScaleTransform.ScaleY : 1.0;
                    PdfScrollViewer.ScrollToVerticalOffset(point.Y * scale);
                }
                catch
                {
                    double targetV = 0;
                    double scale = PdfScaleTransform != null ? PdfScaleTransform.ScaleY : 1.0;
                    for (int i = 0; i < pageIndex; i++)
                    {
                        if (PdfPagesStack.Children[i] is FrameworkElement fe)
                            targetV += (fe.Height + fe.Margin.Bottom);
                    }
                    PdfScrollViewer.ScrollToVerticalOffset(targetV * scale);
                }
            }
        }

        private void BtnPdfPrevPage_Click(object sender, RoutedEventArgs e)
        {
            PrevPdfPage();
        }

        private void BtnPdfNextPage_Click(object sender, RoutedEventArgs e)
        {
            NextPdfPage();
        }

        private void PdfScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool isCtrlDown = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            if (isCtrlDown && PdfScrollViewer != null && PdfScaleTransform != null)
            {
                // ZOOM fluido continuo ancorato al punto del puntatore
                double oldScale = _pdfScale;
                double zoomFactor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
                double newScale = Math.Clamp(oldScale * zoomFactor, 0.5, 4.0);

                if (Math.Abs(newScale - oldScale) < 0.001)
                {
                    e.Handled = true;
                    return;
                }

                Point mouseInViewer = e.GetPosition(PdfScrollViewer);

                _pdfScale = newScale;
                PdfScaleTransform.ScaleX = newScale;
                PdfScaleTransform.ScaleY = newScale;

                double ratio = newScale / oldScale;
                double targetH = (PdfScrollViewer.HorizontalOffset + mouseInViewer.X) * ratio - mouseInViewer.X;
                double targetV = (PdfScrollViewer.VerticalOffset + mouseInViewer.Y) * ratio - mouseInViewer.Y;

                PdfScrollViewer.ScrollToHorizontalOffset(targetH);
                PdfScrollViewer.ScrollToVerticalOffset(targetV);

                if (BtnResetZoom != null)
                {
                    BtnResetZoom.Visibility = Math.Abs(_pdfScale - 1.0) > 0.05 ? Visibility.Visible : Visibility.Collapsed;
                }

                e.Handled = true;
                return;
            }

            // Con scorrimento continuo, se Ctrl NON è premuto, lo ScrollViewer gestisce naturalmente la rotellina!
        }

        private void PdfScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && PdfScrollViewer != null)
            {
                if (e.ClickCount == 2)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                    return;
                }

                // Clicca e trascina con il tasto sinistro (pan a manina)
                _isPanningPdf = true;
                _pdfPanStart = e.GetPosition(PdfScrollViewer);
                _startHOffset = PdfScrollViewer.HorizontalOffset;
                _startVOffset = PdfScrollViewer.VerticalOffset;
                PdfScrollViewer.CaptureMouse();
                PdfScrollViewer.Cursor = Cursors.SizeAll;
            }
        }

        private void PdfScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanningPdf && PdfScrollViewer != null)
            {
                Point current = e.GetPosition(PdfScrollViewer);
                double deltaX = current.X - _pdfPanStart.X;
                double deltaY = current.Y - _pdfPanStart.Y;

                PdfScrollViewer.ScrollToHorizontalOffset(_startHOffset - deltaX);
                PdfScrollViewer.ScrollToVerticalOffset(_startVOffset - deltaY);
                e.Handled = true;
            }
        }

        private void PdfScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanningPdf && e.ChangedButton == MouseButton.Left && PdfScrollViewer != null)
            {
                _isPanningPdf = false;
                PdfScrollViewer.ReleaseMouseCapture();
                PdfScrollViewer.Cursor = Cursors.Arrow;
            }
        }

        private void CleanupPdfView()
        {
            _currentPdfSession?.Dispose();
            _currentPdfSession = null;
            _pdfScale = 1.0;
            _isPanningPdf = false;
            _renderedPagesCache.Clear();
            _loadingPages.Clear();
            _thumbnailsCache.Clear();
            _loadingThumbnails.Clear();

            if (PdfThumbnailsStack != null)
            {
                foreach (UIElement child in PdfThumbnailsStack.Children)
                {
                    if (child is Border b && b.Child is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is Border paper && paper.Child is Image img)
                    {
                        img.Source = null;
                    }
                }
                PdfThumbnailsStack.Children.Clear();
            }

            if (PdfPagesStack != null)
            {
                foreach (UIElement child in PdfPagesStack.Children)
                {
                    if (child is Border b && b.Child is Image img)
                    {
                        img.Source = null;
                    }
                }
                PdfPagesStack.Children.Clear();
            }

            if (PdfScaleTransform != null)
            {
                PdfScaleTransform.ScaleX = 1.0;
                PdfScaleTransform.ScaleY = 1.0;
            }
            if (PdfSidebar != null)
            {
                PdfSidebar.Visibility = Visibility.Collapsed;
            }
            if (PdfSidebarColumn != null)
            {
                PdfSidebarColumn.Width = new GridLength(0);
            }
            if (PdfPageCapsule != null)
            {
                PdfPageCapsule.Visibility = Visibility.Collapsed;
            }
            if (PdfScrollViewer != null)
            {
                if (PdfScrollViewer.IsMouseCaptured)
                {
                    PdfScrollViewer.ReleaseMouseCapture();
                }
                PdfScrollViewer.ScrollToHome();
                PdfScrollViewer.ScrollToVerticalOffset(0);
                PdfScrollViewer.ScrollToHorizontalOffset(0);
                PdfScrollViewer.Cursor = Cursors.Arrow;
            }
            PdfPagesContainer?.InvalidateMeasure();
            PdfPagesStack?.InvalidateMeasure();
        }

        private void FallbackToGenericView(PreviewModel model, string reason)
        {
            HandlerView.Visibility = Visibility.Collapsed;
            VideoView.Visibility = Visibility.Collapsed;
            StopAndUnloadVideo();

            CleanupHandlerHost();

            if (!string.IsNullOrWhiteSpace(model.TextContent))
            {
                // Se c'è testo estratto (es. fallback DocxReader), mostra il visualizzatore testo anziché una scheda generica
                TextView.Visibility = Visibility.Visible;
                PreviewTextBox.Text = model.TextContent;

                string fallbackNotice = LocalizationService.CurrentLanguage == "it"
                    ? "Anteprima solo testo (Fallback)"
                    : "Plain Text Preview (Fallback)";
                HeaderSubtitle.Text = $"Word Document • {fallbackNotice} • {_currentFileSizeText}";
                FooterDetails.Text = LocalizationService.CurrentLanguage == "it"
                    ? "Visualizzatore avanzato non disponibile. Mostrato testo estratto dal documento."
                    : "Advanced previewer unavailable. Showing text extracted from document.";

                return;
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
                double dpiScaleX = 1.0;
                double dpiScaleY = 1.0;

                try
                {
                    if (Win32.GetDpiForMonitor(hMonitor, Win32.MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 && dpiX > 0 && dpiY > 0)
                    {
                        dpiScaleX = dpiX / 96.0;
                        dpiScaleY = dpiY / 96.0;
                    }
                    else
                    {
                        PresentationSource source = PresentationSource.FromVisual(this);
                        if (source?.CompositionTarget != null)
                        {
                            dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                            dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                        }
                    }
                }
                catch
                {
                    PresentationSource source = PresentationSource.FromVisual(this);
                    if (source?.CompositionTarget != null)
                    {
                        dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                        dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                    }
                }

                if (dpiScaleX <= 0) dpiScaleX = 1.0;
                if (dpiScaleY <= 0) dpiScaleY = 1.0;

                workLeft = mi.rcWork.Left / dpiScaleX;
                workTop = mi.rcWork.Top / dpiScaleY;
                monitorWidth = (mi.rcWork.Right - mi.rcWork.Left) / dpiScaleX;
                monitorHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpiScaleY;
            }
        }

        public void AdaptWindowToAspectRatio(double contentWidth, double contentHeight, bool force = false)
        {
            if (this.WindowState == WindowState.Maximized || contentWidth <= 0 || contentHeight <= 0) return;
            if (!force && (_sessionCustomSizes.ContainsKey(_currentCategory) || IsPersistentRememberActive(_currentCategory))) return;

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

            // Se la finestra ha già le dimensioni corrette (entro 2px), non eseguire riposizionamenti né sfarfallii
            if (Math.Abs(this.Width - targetWidth) < 2 && Math.Abs(this.Height - targetHeight) < 2)
            {
                return;
            }

            PositionWindowRelative(targetWidth, targetHeight, workLeft, workTop, monitorWidth, monitorHeight);
        }

        private void PositionWindowRelative(double targetWidth, double targetHeight, double workLeft, double workTop, double monitorWidth, double monitorHeight)
        {
            // Se la finestra è già visibile a schermo (es. navigazione o scorrimento lista file),
            // ridimensiona sempre espandendosi simmetricamente dal centro attuale della finestra.
            bool isAlreadyVisible = IsPreviewVisible &&
                                    double.IsFinite(this.Left) && double.IsFinite(this.Top) &&
                                    (this.ActualWidth > 0 || this.Width > 0) &&
                                    (this.ActualHeight > 0 || this.Height > 0);

            if (isAlreadyVisible)
            {
                double curW = this.ActualWidth > 0 ? this.ActualWidth : this.Width;
                double curH = this.ActualHeight > 0 ? this.ActualHeight : this.Height;
                double currentCenterX = this.Left + curW / 2.0;
                double currentCenterY = this.Top + curH / 2.0;

                double newLeft = currentCenterX - targetWidth / 2.0;
                double newTop = currentCenterY - targetHeight / 2.0;

                // Mantieni la finestra interamente all'interno dell'area di lavoro del monitor
                double maxLeft = Math.Max(workLeft + 10, workLeft + monitorWidth - targetWidth - 10);
                double maxTop = Math.Max(workTop + 10, workTop + monitorHeight - targetHeight - 10);

                this.Left = Math.Clamp(newLeft, workLeft + 10, maxLeft);
                this.Top = Math.Clamp(newTop, workTop + 10, maxTop);
                this.Width = targetWidth;
                this.Height = targetHeight;
                return;
            }

            // Primo avvio (finestra non ancora visibile)
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

            // Centra la finestra nell'area di lavoro del monitor attivo
            this.Left = workLeft + (monitorWidth - targetWidth) / 2.0;
            this.Top = workTop + (monitorHeight - targetHeight) / 2.0;
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
            else if (model.Type == PreviewType.Pdf || (model.Type == PreviewType.PreviewHandler && (model.PdfWidth > 0 || model.FilePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))))
                _currentCategory = PreviewCategory.Pdf;
            else if (model.Type == PreviewType.Text || model.Type == PreviewType.Markdown || model.Type == PreviewType.Html || model.Type == PreviewType.PreviewHandler || model.Type == PreviewType.Stl3D || model.Type == PreviewType.Spreadsheet)
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
                PositionWindowRelative(userW, userH, workLeft, workTop, monitorWidth, monitorHeight);
                return;
            }

            // 2. Se l'utente ha ridimensionato nella sessione corrente per questa categoria, riusa la dimensione di sessione
            if (_sessionCustomSizes.TryGetValue(_currentCategory, out var sessionSize))
            {
                double sessionW = Math.Clamp(sessionSize.Width, 380, monitorWidth * 0.95);
                double sessionH = Math.Clamp(sessionSize.Height, 260, monitorHeight * 0.95);
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
                double vw = model.VideoWidth;
                double vh = model.VideoHeight;
                if (model.VideoRotation == 90 || model.VideoRotation == 270)
                {
                    double tmp = vw;
                    vw = vh;
                    vh = tmp;
                }

                if (vw > 0 && vh > 0)
                {
                    double ratio = vw / vh;
                    bool isVertical = vh > vw;

                    // Contenuti verticali: limitati a 1/2 dello schermo in larghezza (max 50%)
                    double maxWidth = isVertical ? monitorWidth * 0.50 : monitorWidth * 0.82;
                    double maxHeight = monitorHeight * 0.82;
                    double minWidth = Math.Min(340, maxWidth);
                    double minHeight = Math.Min(240, maxHeight);

                    double availableContentWidth = maxWidth - 28;
                    double availableContentHeight = maxHeight - 85;

                    double targetContentWidth = Math.Min(vw, availableContentWidth);
                    double targetContentHeight = targetContentWidth / ratio;

                    if (targetContentHeight > availableContentHeight)
                    {
                        targetContentHeight = availableContentHeight;
                        targetContentWidth = targetContentHeight * ratio;
                    }

                    targetWidth = Math.Clamp(targetContentWidth + 28, minWidth, maxWidth);
                    targetHeight = Math.Clamp(targetContentHeight + 85, minHeight, maxHeight);
                }
                else
                {
                    double maxWidth = monitorWidth * 0.80;
                    double maxHeight = monitorHeight * 0.80;
                    double minWidth = Math.Min(480, maxWidth);
                    double minHeight = Math.Min(320, maxHeight);

                    targetWidth = Math.Clamp(780, minWidth, maxWidth);
                    targetHeight = Math.Clamp(480, minHeight, maxHeight);
                }
            }
            else if (model.Type == PreviewType.Pdf || model.Type == PreviewType.PreviewHandler)
            {
                double extraSidebarWidth = model.PdfPageCount > 1 ? 144.0 : 0.0;
                if (model.PdfWidth > 0 && model.PdfHeight > 0)
                {
                    double ratio = model.PdfWidth / model.PdfHeight;
                    if (ratio >= 1.0)
                    {
                        // Landscape PDF (orizzontale) — +15% più ampia di default
                        double targetH = Math.Clamp(782, 520, monitorHeight * 0.94);
                        double availableH = targetH - 110;
                        double targetW = Math.Clamp((availableH * ratio + 80) * 1.15 + extraSidebarWidth, 782 + extraSidebarWidth, monitorWidth * 0.96);
                        targetWidth = targetW;
                        targetHeight = targetH;
                    }
                    else
                    {
                        // Portrait PDF (verticale, proporzione pagina es. A4) — +15% più ampia di default
                        double targetH = Math.Min(1104, monitorHeight * 0.96);
                        double availableH = targetH - 105;
                        double targetW = Math.Clamp((availableH * ratio + 60) * 1.15 + extraSidebarWidth, 598 + extraSidebarWidth, monitorWidth * 0.85);
                        targetWidth = targetW;
                        targetHeight = targetH;
                    }
                }
                else
                {
                    targetWidth = Math.Min(1104, monitorWidth * 0.90);
                    targetHeight = Math.Min(828, monitorHeight * 0.90);
                }
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
            else if (model.Type == PreviewType.Html)
            {
                targetWidth = Math.Min(960, monitorWidth * 0.85);
                targetHeight = Math.Min(720, monitorHeight * 0.85);
            }
            else if (model.Type == PreviewType.Generic || model.Type == PreviewType.Error || model.IsFolder)
            {
                targetWidth = 560;
                targetHeight = 380;
            }

            PositionWindowRelative(targetWidth, targetHeight, workLeft, workTop, monitorWidth, monitorHeight);
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
            Interlocked.Increment(ref _loadRequestId);
            if (IsPreviewVisible || CurrentState == PreviewState.Loading)
            {
                CurrentState = PreviewState.Closing;
                IsPreviewVisible = false;
                _sessionCustomSizes.Clear();

                // 1. Cloak immediato nel DWM, opacità zero, reset Topmost e Hide: la finestra sparisce istantaneamente e non lascia tracce in Gestione Attività
                if (!_config.QuickLookAlwaysOnTop)
                {
                    try { this.Topmost = false; } catch { }
                }
                this.Opacity = 0;
                if (_hwnd != IntPtr.Zero)
                {
                    if (!_config.QuickLookAlwaysOnTop)
                    {
                        Win32.SetWindowPos(_hwnd, Win32.HWND_NOTOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                    }
                    Win32.SetWindowCloak(_hwnd, true);
                }
                this.Hide();

                _folderSizeCts?.Cancel();
                _folderSizeCts?.Dispose();
                _folderSizeCts = null;

                StopAndUnloadVideo();
                ResetZoom();

                // 2. Rilascia e resetta le viste
                CleanupPdfView();
                if (PdfView != null) PdfView.Visibility = Visibility.Collapsed;
                CleanupHandlerHost();
                HandlerView.Visibility = Visibility.Collapsed;
                if (HandlerHorizontalScrollBar != null) HandlerHorizontalScrollBar.Visibility = Visibility.Collapsed;
                ImageView.Visibility = Visibility.Collapsed;
                VideoView.Visibility = Visibility.Collapsed;
                if (AudioVisualizerPanel != null) AudioVisualizerPanel.Visibility = Visibility.Collapsed;
                if (BtnVideoFullscreen != null) BtnVideoFullscreen.Visibility = Visibility.Visible;
                TextView.Visibility = Visibility.Collapsed;
                MarkdownView.Visibility = Visibility.Collapsed;
                MarkdownView.Document = null;
                if (SpreadsheetView != null) SpreadsheetView.Visibility = Visibility.Collapsed;
                if (SpreadsheetDataGrid != null) SpreadsheetDataGrid.ItemsSource = null;
                if (SpreadsheetTabsPanel != null) SpreadsheetTabsPanel.Children.Clear();
                _currentSpreadsheetModel = null;
                if (StlView != null) StlView.Visibility = Visibility.Collapsed;
                if (StlViewport != null) StlViewport.Children.Clear();
                if (HtmlView != null) HtmlView.Visibility = Visibility.Collapsed;
                if (HtmlDocumentViewer != null) HtmlDocumentViewer.Document = null;
                if (HtmlCodeTextBox != null) HtmlCodeTextBox.Text = string.Empty;
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

                CurrentState = PreviewState.Hidden;
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
                _playRequested = true;
                _isPlayingVideo = true;
                _hasRetriedPlay = false;
                UpdatePlayPauseIcon(true);

                ApplyVideoRotation();

                // Annulla eventuali remux pendenti per un video precedente
                _remuxCts?.Cancel();
                _remuxCts?.Dispose();
                _remuxCts = null;

                bool isBitstream = _currentModel?.IsBitstreamAudio == true || 
                    MediaCompatibilityService.IsRemuxNeeded(_currentModel?.IsBitstreamAudio == true, _currentModel?.VideoAudioCodec, _currentModel?.VideoAudioChannels ?? 2);

                if (isBitstream)
                {
                    // Controlla prima la cache: se è già stato convertito in passato, riproducilo direttamente in AAC stereo
                    if (MediaCompatibilityService.TryGetCachedCompatibleVideo(path, _currentModel?.VideoAudioCodec, _currentModel?.VideoAudioChannels ?? 2, out string? cachedPath) && !string.IsNullOrEmpty(cachedPath))
                    {
                        VideoPlayer.Source = new Uri(cachedPath, UriKind.Absolute);
                        _isMuted = false;
                        VideoPlayer.Volume = _lastVolume > 0 ? _lastVolume : 0.5;
                        if (VolumeSlider != null) VolumeSlider.Value = VideoPlayer.Volume;
                        if (VolumeButton != null)
                        {
                            VolumeButton.Content = "🔊";
                            VolumeButton.ToolTip = LocalizationService.Get("Preview_Audio_CompatibleReady");
                        }
                        HeaderSubtitle.Text = $"{LocalizationService.Get("Spotlight_Category_Video")} (Stereo AAC) • {_currentFileSizeText}";
                        AppLogger.Info($"LoadAndPlayVideo: Utilizzato file compatibile in cache per {path} -> {cachedPath}");
                    }
                    else
                    {
                        // File bitstream non ancora in cache: NON impostiamo VideoPlayer.Source = path
                        // perché Windows Media Foundation invierebbe i pacchetti compressi AC-3 a 0 dBFS
                        // causando rumore assordante. Mostriamo l'overlay di preparazione audio compatibile.
                        VideoPlayer.Stop();
                        VideoPlayer.Source = null;
                        _isMuted = false;

                        if (VideoConversionOverlay != null)
                        {
                            VideoConversionOverlay.Visibility = Visibility.Visible;
                            if (VideoConversionProgress != null) VideoConversionProgress.Value = 0;
                            if (VideoConversionSubtext != null)
                            {
                                string codecName = _currentModel?.VideoAudioCodec?.ToUpperInvariant() ?? "AC-3";
                                VideoConversionSubtext.Text = $"Conversione audio {codecName} in formato stereo compatibile...";
                            }
                        }

                        HeaderSubtitle.Text = $"{LocalizationService.Get("Spotlight_Category_Video")} • {LocalizationService.Get("Preview_Audio_CompatiblePreparing")} • {_currentFileSizeText}";
                        AppLogger.Warn($"LoadAndPlayVideo: Rilevata traccia bitstream per {path}. Avvio preparazione remux compatibile in background...");

                        string originalPath = path;
                        var modelAtStart = _currentModel;

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var progress = new Progress<double>(pct =>
                                {
                                    _ = Dispatcher.InvokeAsync(new Action(() =>
                                    {
                                        if (_currentModel == modelAtStart && VideoConversionProgress != null)
                                        {
                                            VideoConversionProgress.Value = pct;
                                            HeaderSubtitle.Text = $"{LocalizationService.Get("Spotlight_Category_Video")} • {LocalizationService.Get("Preview_Audio_CompatiblePreparing")} ({pct:0}%) • {_currentFileSizeText}";
                                        }
                                    }));
                                });

                                string compatibleVideo = await MediaCompatibilityService.EnsureCompatibleVideoAsync(
                                    originalPath, 
                                    modelAtStart?.VideoAudioCodec, 
                                    modelAtStart?.VideoAudioChannels ?? 2, 
                                    TimeSpan.Zero, 
                                    progress, 
                                    CancellationToken.None);

                                await Dispatcher.InvokeAsync(() =>
                                {
                                    if (VideoConversionOverlay != null)
                                    {
                                        VideoConversionOverlay.Visibility = Visibility.Collapsed;
                                    }

                                    if (_currentModel == modelAtStart && File.Exists(compatibleVideo))
                                    {
                                        VideoPlayer.Source = new Uri(compatibleVideo, UriKind.Absolute);
                                        _isMuted = false;
                                        VideoPlayer.Volume = _lastVolume > 0 ? _lastVolume : 0.5;
                                        if (VolumeSlider != null) VolumeSlider.Value = VideoPlayer.Volume;
                                        if (VolumeButton != null)
                                        {
                                            VolumeButton.Content = "🔊";
                                            VolumeButton.ToolTip = LocalizationService.Get("Preview_Audio_CompatibleReady");
                                        }
                                        HeaderSubtitle.Text = $"{LocalizationService.Get("Spotlight_Category_Video")} (Stereo AAC) • {_currentFileSizeText}";
                                        if (_isPlayingVideo)
                                        {
                                            VideoPlayer.Play();
                                        }
                                        AppLogger.Info($"LoadAndPlayVideo: File compatibile attivato con successo: {compatibleVideo}");
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Warn($"LoadAndPlayVideo: Remux compatibile fallito per {originalPath}: {ex.Message}");
                                _ = Dispatcher.InvokeAsync(new Action(() =>
                                {
                                    if (VideoConversionOverlay != null)
                                    {
                                        VideoConversionOverlay.Visibility = Visibility.Collapsed;
                                    }
                                    if (_currentModel == modelAtStart)
                                    {
                                        HeaderSubtitle.Text = $"{LocalizationService.Get("Spotlight_Category_Video")} • 🔇 Audio non compatibile • {_currentFileSizeText}";
                                        if (VolumeButton != null)
                                        {
                                            VolumeButton.ToolTip = LocalizationService.Get("Preview_Audio_MutedBitstream");
                                        }
                                    }
                                }));
                            }
                        });
                    }
                }
                else
                {
                    // Traccia nativa già compatibile (AAC, MP3, FLAC, ecc.): riproduzione diretta invariata
                    VideoPlayer.Source = new Uri(path, UriKind.Absolute);
                    _isMuted = false;
                    VideoPlayer.Volume = _lastVolume > 0 ? _lastVolume : 0.5;
                    if (VolumeSlider != null) VolumeSlider.Value = VideoPlayer.Volume;
                    if (VolumeButton != null)
                    {
                        VolumeButton.Content = "🔊";
                        VolumeButton.ToolTip = null;
                    }
                }

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
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Errore avvio riproduzione video per {path}", ex);
            }
        }

        private void StopAndUnloadVideo()
        {
            _playRequested = false;
            _hasRetriedPlay = false;
            _videoTimer?.Stop();
            _currentAudioFraction = 0.0;
            _currentVinylAngle = 0.0;
            _isPlayingVideo = false;

            if (VideoConversionOverlay != null)
            {
                VideoConversionOverlay.Visibility = Visibility.Collapsed;
            }

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
                VideoPlayer.LayoutTransform = Transform.Identity;
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"Errore Stop/Unload MediaElement: {ex.Message}");
            }
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

            // Garanzia di avvio effettivo della riproduzione DirectShow (singolo tentativo controllato se bloccato a 00:00)
            if (_isPlayingVideo && VideoPlayer.Position == TimeSpan.Zero && !_hasRetriedPlay)
            {
                _hasRetriedPlay = true;
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
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (HandlerHostBorder.Visibility == Visibility.Visible && HandlerHostBorder.Child is PreviewHandlerHostControl host)
            {
                bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                host.ForwardMouseWheel(e.Delta, isCtrl);
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
            AppLogger.Debug($"VideoPlayer_MediaOpened: Duration={VideoPlayer.NaturalDuration}");
            if (_isPlayingVideo || _playRequested)
            {
                _isPlayingVideo = true;
                VideoPlayer.Play();
                _videoTimer?.Start();
            }

            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var total = VideoPlayer.NaturalDuration.TimeSpan;
                SetTimelineFraction(0);
                VideoTimeText.Text = FormatPlaybackTime(TimeSpan.Zero, total);
            }

            if (VideoPlayer.NaturalVideoWidth > 0 && VideoPlayer.NaturalVideoHeight > 0)
            {
                int rot = _currentModel?.VideoRotation ?? 0;
                bool isSwapped = rot == 90 || rot == 270;
                double effW = isSwapped ? VideoPlayer.NaturalVideoHeight : VideoPlayer.NaturalVideoWidth;
                double effH = isSwapped ? VideoPlayer.NaturalVideoWidth : VideoPlayer.NaturalVideoHeight;

                string audioNotice = string.Empty;
                if (_currentModel?.IsBitstreamAudio == true)
                {
                    string codec = _currentModel.VideoAudioCodec ?? "AC-3";
                    int channels = _currentModel.VideoAudioChannels;
                    string chStr = channels > 2 ? (channels == 6 ? "5.1" : $"{channels}ch") : "";
                    audioNotice = LocalizationService.CurrentLanguage == "it"
                        ? $" • 🔇 Audio {codec} {chStr} (Silenziato)".Trim()
                        : $" • 🔇 {codec} {chStr} Audio (Muted)".Trim();
                }

                HeaderSubtitle.Text = $"{(int)effW} × {(int)effH} • {_currentFileSizeText}{audioNotice}";

                // Adatta dinamicamente le dimensioni della finestra al rapporto di aspetto nativo del video (nessuna banda nera)
                if (this.WindowState == WindowState.Normal)
                {
                    AdaptWindowToAspectRatio(effW, effH);
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
            AppLogger.Warn($"Riproduzione media fallita: {e.ErrorException?.Message}");
            _playRequested = false;
            _isPlayingVideo = false;
            _videoTimer?.Stop();
            StopTurntableAnimation();
            string errorMsg = _currentCategory == PreviewCategory.Audio
                ? (LocalizationService.CurrentLanguage == "it" ? "Formato audio non supportato o file danneggiato" : "Audio format not supported or corrupt file")
                : (LocalizationService.CurrentLanguage == "it" ? "Codec video non disponibile" : "Video codec not available");
            FallbackToGenericView(new PreviewModel { FilePath = _currentFilePath ?? string.Empty, Title = HeaderTitle.Text }, errorMsg);
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

        /// <summary>Rotazione manuale della sola vista (0/90/180/270), sopra l'orientamento automatico. Non tocca il file.</summary>
        private int _manualRotation;

        private void BtnRotate_Click(object sender, RoutedEventArgs e)
        {
            _manualRotation = (_manualRotation + 90) % 360;
            if (_currentModel?.Type == PreviewType.Video)
            {
                ApplyVideoRotation();
                if (VideoPlayer.NaturalVideoWidth > 0 && VideoPlayer.NaturalVideoHeight > 0)
                {
                    bool swapped = (((_currentModel.VideoRotation + _manualRotation) % 360) % 180) != 0;
                    double w = swapped ? VideoPlayer.NaturalVideoHeight : VideoPlayer.NaturalVideoWidth;
                    double h = swapped ? VideoPlayer.NaturalVideoWidth : VideoPlayer.NaturalVideoHeight;
                    AdaptWindowToAspectRatio(w, h, force: true);
                }
            }
            else if (_currentModel?.Type == PreviewType.Image)
            {
                // L'immagine ha già l'orientamento EXIF applicato in decodifica; qui si aggiunge solo la correzione manuale.
                // LayoutTransform: lo Stretch=Uniform ricalcola l'adattamento alla finestra senza tagli.
                PreviewImage.LayoutTransform = _manualRotation == 0 ? Transform.Identity : new RotateTransform(_manualRotation);
                ResetZoom();
                if (PreviewImage.Source is BitmapSource bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
                {
                    bool swapped = _manualRotation % 180 != 0;
                    AdaptWindowToAspectRatio(swapped ? bmp.PixelHeight : bmp.PixelWidth, swapped ? bmp.PixelWidth : bmp.PixelHeight, force: true);
                }
            }
        }

        private void ApplyVideoRotation()
        {
            int total = ((_currentModel?.VideoRotation ?? 0) + _manualRotation) % 360;
            VideoPlayer.LayoutTransform = total == 0 ? Transform.Identity : new RotateTransform(total);
        }

        private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            ResetZoom();
        }

        private void ResetZoom()
        {
            _imageScale = 1.0;
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

            _pdfScale = 1.0;
            if (PdfScaleTransform != null)
            {
                PdfScaleTransform.ScaleX = 1.0;
                PdfScaleTransform.ScaleY = 1.0;
            }
            if (PdfScrollViewer != null)
            {
                PdfScrollViewer.ScrollToHome();
                PdfScrollViewer.Cursor = Cursors.Arrow;
            }

            if (HandlerHostBorder?.Child is PreviewHandlerHostControl handlerHost)
            {
                handlerHost.ResetZoom();
            }

            if (BtnResetZoom != null)
            {
                BtnResetZoom.Visibility = Visibility.Collapsed;
            }

            if (HandlerHorizontalScrollBar != null)
            {
                HandlerHorizontalScrollBar.Visibility = Visibility.Collapsed;
            }
        }

        private void HandlerHorizontalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingHandlerScrollBar) return;
            if (HandlerHostBorder?.Child is PreviewHandlerHostControl handlerHost)
            {
                handlerHost.SetHorizontalScrollFraction(e.NewValue);
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.IsRepeat) return;

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

                case Key.PageUp:
                    if (PdfView != null && PdfView.Visibility == Visibility.Visible)
                    {
                        PrevPdfPage();
                        e.Handled = true;
                    }
                    break;

                case Key.PageDown:
                    if (PdfView != null && PdfView.Visibility == Visibility.Visible)
                    {
                        NextPdfPage();
                        e.Handled = true;
                    }
                    break;

                case Key.Home:
                    if (PdfView != null && PdfView.Visibility == Visibility.Visible)
                    {
                        ScrollToPage(0);
                        e.Handled = true;
                    }
                    break;

                case Key.End:
                    if (PdfView != null && PdfView.Visibility == Visibility.Visible && _currentPdfSession != null)
                    {
                        ScrollToPage(_currentPdfSession.PageCount - 1);
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            Window_PreviewKeyDown(sender, e);
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

        private async void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentFilePath) || !File.Exists(_currentFilePath)) return;

            string sourcePath = _currentFilePath;

            try
            {
                string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsDir)) Directory.CreateDirectory(downloadsDir);
                string fileName = Path.GetFileNameWithoutExtension(sourcePath);
                string destCandidate = Path.Combine(downloadsDir, fileName + ".pdf");
                string destPath = MediaConversionService.GenerateNonConflictingPath(destCandidate, ".pdf");

                string result = await MediaConversionService.ConvertDocumentToPdfAsync(sourcePath, destPath);
                var toast = FloatingNotificationWindow.Spawn(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), "📕");
                toast.SetSuccess(LocalizationService.Get("Notification_Success", Path.GetFileName(result)), result, result);

                if (_config.OpenFolderAfterConversion && File.Exists(result))
                {
                    FileActionService.RevealFileInExplorer(result);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Esportazione PDF fallita per '{sourcePath}': {ex.Message}");
            }
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
                FileLaunched?.Invoke(_currentFilePath);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Impossibile aprire il file {_currentFilePath}", ex);
            }
        }

        private void PreviewContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilePath) || (!File.Exists(_currentFilePath) && !Directory.Exists(_currentFilePath)))
            {
                e.Handled = true;
                return;
            }

            string ext = Path.GetExtension(_currentFilePath).ToLowerInvariant();
            bool isJpg = ext is ".jpg" or ".jpeg";
            bool isPng = ext is ".png";
            bool isPdf = ext is ".pdf";
            bool isImage = isJpg || isPng || ext is ".webp" or ".bmp" or ".gif" or ".tiff" or ".ico";
            bool isVideo = ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".flv" or ".m4v" or ".webm" or ".ts";
            bool isMp3 = ext is ".mp3";
            bool isWav = ext is ".wav";
            bool isAudio = isMp3 || isWav || ext is ".m4a" or ".flac" or ".aac" or ".wma" or ".ogg" or ".aiff" or ".opus";

            bool canConvertJpg = isImage && !isJpg;
            bool canConvertPng = isImage && !isPng;
            bool canConvertPdf = isImage && !isPdf;
            bool canRemoveBg = isImage;
            bool canConvertMp3 = (isVideo || isAudio) && !isMp3;
            bool canConvertWav = (isVideo || isAudio) && !isWav;
            bool canConvertMp4 = isVideo && ext != ".mp4";

            bool hasMediaAction = canConvertJpg || canConvertPng || canConvertPdf || canConvertMp3 || canConvertWav || canConvertMp4 || canRemoveBg;

            PreviewMenuSeparatorMedia.Visibility = hasMediaAction ? Visibility.Visible : Visibility.Collapsed;
            PreviewMenuConvertJpg.Visibility = canConvertJpg ? Visibility.Visible : Visibility.Collapsed;
            PreviewMenuConvertPng.Visibility = canConvertPng ? Visibility.Visible : Visibility.Collapsed;
            PreviewMenuConvertPdf.Visibility = canConvertPdf ? Visibility.Visible : Visibility.Collapsed;
            PreviewMenuConvertMp3.Visibility = canConvertMp3 ? Visibility.Visible : Visibility.Collapsed;
            PreviewMenuConvertWav.Visibility = canConvertWav ? Visibility.Visible : Visibility.Collapsed;
            PreviewMenuConvertMp4.Visibility = canConvertMp4 ? Visibility.Visible : Visibility.Collapsed;
            PreviewMenuRemoveBg.Visibility = canRemoveBg ? Visibility.Visible : Visibility.Collapsed;
            PreviewMenuFullscreen.Visibility = (isImage || isVideo) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PreviewMenuOpenPath_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                FileActionService.RevealFileInExplorer(_currentFilePath);
            }
        }

        private void PreviewMenuOpenDefault_Click(object sender, RoutedEventArgs e)
        {
            OpenInDefaultProgram();
            HidePreview();
        }

        private void PreviewMenuCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                try
                {
                    Clipboard.SetText(_currentFilePath);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Impossibile copiare percorso negli appunti: {ex.Message}");
                }
            }
        }

        private void PreviewMenuConvertJpg_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                _ = FileActionService.ExecuteConvertJpgAsync(_currentFilePath, _config);
            }
        }

        private void PreviewMenuConvertPng_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                _ = FileActionService.ExecuteConvertPngAsync(_currentFilePath, _config);
            }
        }

        private void PreviewMenuConvertPdf_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                _ = FileActionService.ExecuteConvertPdfAsync(_currentFilePath, _config);
            }
        }

        private void PreviewMenuConvertMp3_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                _ = FileActionService.ExecuteExtractMp3Async(_currentFilePath, _config);
            }
        }

        private void PreviewMenuConvertWav_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                _ = FileActionService.ExecuteConvertWavAsync(_currentFilePath, _config);
            }
        }

        private void PreviewMenuConvertMp4_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                _ = FileActionService.ExecuteConvertMp4Async(_currentFilePath, _config);
            }
        }

        private void PreviewMenuRemoveBg_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                _ = FileActionService.ExecuteRemoveBgAsync(_currentFilePath, _config);
            }
        }

        private void PreviewMenuRememberSize_Click(object sender, RoutedEventArgs e)
        {
            BtnRememberSize_Click(this, new RoutedEventArgs());
        }

        private void PreviewMenuFullscreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void PreviewMenuClose_Click(object sender, RoutedEventArgs e)
        {
            HidePreview();
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

        private string? _currentHtmlFilePath;

        private void SetupHtmlView(PreviewModel model)
        {
            _currentHtmlFilePath = model.FilePath;
            string htmlContent = model.TextContent ?? string.Empty;

            // 1. Rendering visuale immediato ad alte prestazioni tramite GPU DirectX (FlowDocument)
            try
            {
                if (HtmlDocumentViewer != null)
                {
                    HtmlDocumentViewer.Document = HtmlToFlowDocumentConverter.Convert(
                        htmlContent,
                        Path.GetFileNameWithoutExtension(model.FilePath));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"HtmlToFlowDocumentConverter fallito per '{model.FilePath}': {ex.Message}");
            }

            // 2. Popola la casella codice sorgente
            if (HtmlCodeTextBox != null)
            {
                HtmlCodeTextBox.Text = htmlContent;
            }

            // 3. Mostra inizialmente l'anteprima visuale
            SetHtmlMode(previewMode: true);
        }

        private void SetHtmlMode(bool previewMode)
        {
            if (previewMode)
            {
                if (HtmlDocumentViewer != null) HtmlDocumentViewer.Visibility = Visibility.Visible;
                if (HtmlCodeTextBox != null) HtmlCodeTextBox.Visibility = Visibility.Collapsed;

                BtnHtmlModePreview.Background = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7));
                BtnHtmlModePreview.Foreground = Brushes.White;

                BtnHtmlModeCode.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
                BtnHtmlModeCode.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            }
            else
            {
                if (HtmlDocumentViewer != null) HtmlDocumentViewer.Visibility = Visibility.Collapsed;
                if (HtmlCodeTextBox != null) HtmlCodeTextBox.Visibility = Visibility.Visible;

                BtnHtmlModeCode.Background = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7));
                BtnHtmlModeCode.Foreground = Brushes.White;

                BtnHtmlModePreview.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
                BtnHtmlModePreview.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            }
        }

        private void BtnHtmlModePreview_Click(object sender, RoutedEventArgs e)
        {
            SetHtmlMode(previewMode: true);
        }

        private void BtnHtmlModeCode_Click(object sender, RoutedEventArgs e)
        {
            SetHtmlMode(previewMode: false);
        }

        private void BtnHtmlOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_currentHtmlFilePath) && File.Exists(_currentHtmlFilePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _currentHtmlFilePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Impossibile aprire il file HTML nel browser predefinito: {ex.Message}");
                }
            }
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

        public void UpdateConfig(AeroSpot.Core.Config.AppConfig config)
        {
            _config = config;
            if (_config.QuickLookAlwaysOnTop)
            {
                try { Topmost = true; } catch { }
            }
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            // Comportamento contestuale: quando l'utente clicca su un'altra finestra
            // (es. Esplora Risorse, selettore file, browser), rilascia Topmost a meno che l'opzione
            // QuickLookAlwaysOnTop non sia abilitata nelle impostazioni.
            if (!_config.QuickLookAlwaysOnTop)
            {
                try
                {
                    Topmost = false;
                }
                catch { }
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Quando QuickLook torna attiva o viene cliccata dall'utente, ripristina Topmost in primo piano.
            try
            {
                Topmost = true;
            }
            catch { }
        }
    }
}
