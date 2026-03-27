using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace CyberConcubineSelection
{
    public partial class 赛博选妃 : Form
    {
        // --- 防闪烁优化 ---
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED - 彻底消灭闪烁
                return cp;
            }
        }

        // --- 核心变量定义区 ---
        private string workPath = "";
        private string currentVideoPath = ""; // 记录当前抽中的路径
        private System.Windows.Media.MediaPlayer? mediaPlayer; // WPF MediaPlayer（无UI）
        private string displayFolderName = ""; // 绘图专用变量
        private string displayFileName = "";   // 绘图专用变量
        private bool isMuted = true; // 默认静音
        
        // 黑白名单的便捷访问属性（从BlackWhiteListManager获取）
        private List<string> stopWords => blackWhiteListManager?.StopWords.ToList() ?? new List<string>();
        private List<string> blackList => blackWhiteListManager?.BlackList.ToList() ?? new List<string>();

        // --- Favorites Manager ---
        private FavoritesManager favoritesManager = null!;

        // --- Path Manager and Mode Manager ---
        private PathManager pathManager = null!;
        private ModeManager modeManager = null!;

        // --- Black/White List Manager ---
        private BlackWhiteListManager blackWhiteListManager = null!;

        // --- History Manager ---
        private HistoryManager historyManager = null!;
        private ImageHistoryManager imageHistoryManager = null!;

        // --- Debouncing Timer for Config Writes ---
        private System.Windows.Forms.Timer configDebounceTimer = null!;

        // 静音配置文件路径
        string muteFilePath = Path.Combine(Application.StartupPath, "audio", "mute.txt");

        // --- 史官变量区 ---
        // 记录文件存放在程序运行目录下的 config/History.log
        private string historyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");
        private System.Windows.Forms.Timer scrollTimer = new System.Windows.Forms.Timer();

        // --- 路径统一配置区 (确保这里一个不漏) ---
        static string imgDir = Path.Combine(Application.StartupPath, "img");

        private Form? annualReportFormInstance = null;
        private Form? historyFormInstance = null;
        private Form? configFormInstance = null;
        private Form? heatmapFormInstance = null;
        private Form? monthlyStatsFormInstance = null;
        private Form? inactiveStreakFormInstance = null;
        private Form? hourlyActivityFormInstance = null;
        private Form? pathHeatmapFormInstance = null;
        private Form? backgroundHeatmapFormInstance = null;
        private Form? videoHeatmapFormInstance = null;
        private Form? teacherRankingFormInstance = null;
        private Form? unwatchedFolderRankingFormInstance = null;
        private Form? favoriteCodeRankingFormInstance = null;
        private Form? favoriteTeacherCollectionRankingFormInstance = null;
        private Action? historyUpdateDataAction = null; // 历史记录窗口的数据刷新委托
        private Action? heatmapUpdateDataAction = null; // 热力图窗口的数据刷新委托
        private Action? monthlyStatsUpdateDataAction = null; // 月度统计窗口的数据刷新委托
        private Action? inactiveStreakUpdateDataAction = null; // 不活跃天数窗口的数据刷新委托
        private Action? hourlyActivityUpdateDataAction = null; // 24小时活跃度窗口的数据刷新委托
        private Action? pathHeatmapUpdateDataAction = null; // 路径热力图窗口的数据刷新委托
        private Action? backgroundHeatmapUpdateDataAction = null; // 背景热力图窗口的数据刷新委托
        private Action? videoHeatmapUpdateDataAction = null; // 视频热力图窗口的数据刷新委托
        private Action? teacherRankingUpdateDataAction = null; // 最爱老师排行榜窗口的数据刷新委托
        private Action? unwatchedFolderRankingUpdateDataAction = null; // 未看过视频文件夹分布窗口的数据刷新委托
        private Action? favoriteCodeRankingUpdateDataAction = null; // 最爱番号排行榜窗口的数据刷新委托
        private Action? favoriteTeacherCollectionRankingUpdateDataAction = null; // 老师库存排行榜窗口的数据刷新委托
        private readonly Dictionary<Form, Action> annualReportRefreshHandlers = new Dictionary<Form, Action>();
        private System.Windows.Forms.Timer? annualReportRefreshTimer;
        private List<string>? cachedEnabledVideos;
        private string cachedEnabledPathsSignature = "";
        private DateTime cachedEnabledVideosAt = DateTime.MinValue;
        private YearlyStatistics? cachedYearlyStatistics;
        private string cachedYearlyStatisticsKey = "";
        private readonly object yearlyStatisticsRefreshLock = new object();
        private bool freezeAnnualReportCardRandoms = false;
        private readonly object inventorySnapshotLock = new object();
        private readonly object inventoryScanStatusLock = new object();
        private bool isInventorySnapshotScanInProgress = false;
        private string inventorySnapshotScanStatusMessage = "";
        private readonly object unwatchedScanLock = new object();
        private bool isUnwatchedScanInProgress = false;
        private string unwatchedScanStatusMessage = "";
        private int cachedUnwatchedVideoCount = -1;
        private List<(string folder, int count)> cachedUnwatchedFolderDistribution = new List<(string folder, int count)>();
        private readonly object teacherCollectionScanLock = new object();
        private bool isTeacherCollectionScanInProgress = false;
        private string teacherCollectionScanStatusMessage = "";
        private string cachedFavoriteTeacherCollection = "";
        private int cachedFavoriteTeacherCollectionCount = -1;
        private List<(string teacher, int count)> cachedTeacherCollectionDistribution = new List<(string teacher, int count)>();
        private readonly object qualitySnapshotScanLock = new object();
        private bool isQualitySnapshotScanInProgress = false;
        private string qualitySnapshotScanStatusMessage = "";
        private readonly object videoCodecScanLock = new object();
        private bool isVideoCodecScanInProgress = false;
        private string videoCodecScanStatusMessage = "";
        private readonly Dictionary<string, string> lastBackgroundBySubDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const int UnwatchedVideoCacheVersion = 2;
        private const int TeacherCollectionCacheVersion = 2;
        private const int VideoQualitySnapshotVersion = 2;

        private void RefreshAnnualReportWindowCore()
        {
            if (annualReportFormInstance == null || annualReportFormInstance.IsDisposed)
                return;

            try
            {
                freezeAnnualReportCardRandoms = false;
                peakHourRandomIndex = -1;
                annualReportFormInstance.Invalidate(true);
                annualReportFormInstance.Update();
                freezeAnnualReportCardRandoms = true;
            }
            catch { }
        }

        private void SafeInvoke(Action? action)
        {
            if (action == null) return;

            try
            {
                action.Invoke();
            }
            catch { }
        }

        private void EnsureAnnualReportRefreshTimer()
        {
            if (annualReportRefreshTimer != null)
                return;

            annualReportRefreshTimer = new System.Windows.Forms.Timer
            {
                Interval = 120
            };
            annualReportRefreshTimer.Tick += (s, e) =>
            {
                annualReportRefreshTimer.Stop();
                FlushAnnualReportRefresh();
            };
        }

        private void RegisterAnnualReportRefresh(Form form, Action refreshAction)
        {
            if (form == null)
                return;

            annualReportRefreshHandlers[form] = refreshAction;
        }

        private void UnregisterAnnualReportRefresh(Form? form)
        {
            if (form == null)
                return;

            annualReportRefreshHandlers.Remove(form);
        }

        private void FlushAnnualReportRefresh()
        {
            var staleForms = annualReportRefreshHandlers.Keys
                .Where(form => form == null || form.IsDisposed)
                .ToList();

            foreach (var staleForm in staleForms)
            {
                annualReportRefreshHandlers.Remove(staleForm);
            }

            foreach (var handler in annualReportRefreshHandlers.Values.ToList())
            {
                SafeInvoke(handler);
            }
        }

        private void RequestAnnualReportRefresh(bool immediate = false)
        {
            EnsureAnnualReportRefreshTimer();

            if (immediate)
            {
                annualReportRefreshTimer?.Stop();
                FlushAnnualReportRefresh();
                return;
            }

            annualReportRefreshTimer?.Stop();
            annualReportRefreshTimer?.Start();
        }

        // --- 按钮音效播放器 ---
        private System.Windows.Media.MediaPlayer soundPlayer = new System.Windows.Media.MediaPlayer();
        // 最大公约数（用于简化宽高比）
        private int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }
        // 1. 获取全局倍率
        private float GetScale()
        {
            using (Graphics g = this.CreateGraphics()) return g.DpiX / 96f;
        }

        // 根据屏幕分辨率计算ListView填充倍数
        private int GetListViewFillMultiplier()
        {
            float screenScale = Math.Max(Screen.PrimaryScreen?.Bounds.Width ?? 1920 / 1920f, Screen.PrimaryScreen?.Bounds.Height ?? 1080 / 1080f);
            return screenScale > 1.5f ? 4 : (screenScale > 1.2f ? 3 : 2); // 4K用4倍，2K用3倍，1080p用2倍
        }

        // 2. 暴力换算坐标和大小
        // 使用 Math.Ceiling 向上取整，宁可多给 1 像素，也不少给
        private int S(int value) => (int)Math.Ceiling(value * GetScale());

        // 3. 窗口缩放（只管外壳）
        private void ApplyFormSize(Form frm, int baseWidth, int baseHeight)
        {
            frm.Width = S(baseWidth);
            frm.Height = S(baseHeight);
        }
        // 定义点击类型：0-随机翻牌(浏览)，1-确定宠幸(选中)
        // --- Win32 隐藏滚动条黑科技 ---
        [DllImport("user32.dll")]
        static extern int ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        [DllImport("user32.dll")]
        static extern bool GetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi);

        [DllImport("user32.dll")]
        static extern int SetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO lpsi, bool fRedraw);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        const int WM_VSCROLL = 0x115;
        const int SB_THUMBPOSITION = 4;

        static IntPtr MakeLParam(int low, int high)
        {
            return (IntPtr)((high << 16) | (low & 0xFFFF));
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SCROLLINFO
        {
            public int cbSize;
            public int fMask;
            public int nMin;
            public int nMax;
            public int nPage;
            public int nPos;
            public int nTrackPos;
        }

        const int SB_VERT = 1;
        const int SIF_RANGE = 0x1;
        const int SIF_PAGE = 0x2;
        const int SIF_POS = 0x4;
        const int SIF_ALL = SIF_RANGE | SIF_PAGE | SIF_POS;

        // 【新增】设置窗口扩展样式，支持透明
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_LAYERED = 0x00080000;

        // 【新增】检测其他窗口全屏/最大化的Win32 API
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool IsZoomed(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // 检测是否有其他窗口全屏或最大化
        private bool IsAnyOtherWindowMaximizedOrFullscreen()
        {
            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();

                // 如果前台窗口是本程序，则不暂停
                if (foregroundWindow == this.Handle)
                    return false;

                // 检查前台窗口是否最大化
                if (IsZoomed(foregroundWindow))
                    return true;

                // 检查前台窗口是否全屏（覆盖整个屏幕）
                RECT rect;
                if (GetWindowRect(foregroundWindow, out rect))
                {
                    Screen screen = Screen.FromHandle(foregroundWindow);
                    if (rect.Left <= screen.Bounds.Left &&
                        rect.Top <= screen.Bounds.Top &&
                        rect.Right >= screen.Bounds.Right &&
                        rect.Bottom >= screen.Bounds.Bottom)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // 1. 定义比例计算
        // 获取系统当前的缩放倍数


        private void ApplyRescaling(Form frm, int baseWidth, int baseHeight)
        {
            float scale;
            using (Graphics g = this.CreateGraphics())
            {
                scale = g.DpiX / 96f; // 获取系统实际缩放比
            }

            // 1. 调整窗口外壳
            frm.Width = (int)(baseWidth * scale);
            frm.Height = (int)(baseHeight * scale);

            // 2. 如果缩放不是 1.0，就手动遍历所有控件调坐标
            if (Math.Abs(scale - 1.0f) > 0.001f)
            {
                foreach (Control ctrl in frm.Controls)
                {
                    // 按比例挪位置
                    ctrl.Left = (int)(ctrl.Left * scale);
                    ctrl.Top = (int)(ctrl.Top * scale);
                    // 按比例改大小
                    ctrl.Width = (int)(ctrl.Width * scale);
                    ctrl.Height = (int)(ctrl.Height * scale);

                    // 针对文本框或按钮，稍微调大一下字体，防止字太小
                    ctrl.Font = new Font(ctrl.Font.FontFamily, ctrl.Font.Size * scale, ctrl.Font.Style);
                }
            }
        }
        public 赛博选妃()
        {
            // 在InitializeComponent之前启用双缓冲，减少闪烁
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint, true);
            this.UpdateStyles();

            InitializeComponent();

            // === 1. Configuration Migration (MUST BE FIRST) ===
            // Migrate old config files to config/ directory before loading anything
            ConfigMigration migration = new ConfigMigration();
            migration.MigrateOldConfig();

            // 假设你现在的窗口在 1080p 下是 800x500
            ApplyRescaling(this, 450, 820);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 1. 确保 img 和 audio 文件夹存在
            if (!Directory.Exists(imgDir)) Directory.CreateDirectory(imgDir);

            string audioDir = Path.Combine(Application.StartupPath, "audio");
            if (!Directory.Exists(audioDir)) Directory.CreateDirectory(audioDir);

            // --- 2. 主界面背景随机摇号逻辑 (从 img/Main 文件夹独立抽取，支持视频和静态图片) ---
            string? mainImgPath = GetRandomImageFromSubDir("Main");
            if (!string.IsNullOrEmpty(mainImgPath) && File.Exists(mainImgPath))
            {
                string ext = Path.GetExtension(mainImgPath).ToLower();
                bool isVideo = ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".wmv" || ext == ".flv";

                if (isVideo)
                {
                    // === 视频背景 ===
                    try
                    {
                        mediaPlayer = new System.Windows.Media.MediaPlayer();
                        mediaPlayer.Open(new Uri(mainImgPath, UriKind.Absolute));
                        mediaPlayer.Volume = isMuted ? 0 : 0.8; // 根据设置决定音量
                        mediaPlayer.MediaEnded += (s, e) =>
                        {
                            mediaPlayer.Position = TimeSpan.Zero;
                            mediaPlayer.Play();
                        };

                        // 使用ScrubbingEnabled提高性能
                        mediaPlayer.ScrubbingEnabled = true;
                        mediaPlayer.Play();

                        // 预先计算缩放参数，避免每帧重复计算
                        int targetWidth = this.ClientSize.Width;
                        int targetHeight = this.ClientSize.Height;

                        // 使用CompositionTarget.Rendering事件，与屏幕刷新率同步
                        System.Windows.Media.CompositionTarget.Rendering += (s, e) =>
                        {
                            try
                            {
                                if (mediaPlayer != null && mediaPlayer.NaturalVideoWidth > 0 && mediaPlayer.NaturalVideoHeight > 0)
                                {
                                    // 【主界面视频】始终渲染，除非有其他程序全屏/最大化
                                    // 只在视频尺寸或窗口尺寸改变时重新创建RenderTargetBitmap
                                    int videoWidth = mediaPlayer.NaturalVideoWidth;
                                    int videoHeight = mediaPlayer.NaturalVideoHeight;

                                    // 计算自适应缩放比例
                                    float ratioX = (float)targetWidth / videoWidth;
                                    float ratioY = (float)targetHeight / videoHeight;
                                    float ratio = Math.Max(ratioX, ratioY);

                                    int scaledWidth = (int)(videoWidth * ratio);
                                    int scaledHeight = (int)(videoHeight * ratio);
                                    int posX = (targetWidth - scaledWidth) / 2;
                                    int posY = (targetHeight - scaledHeight) / 2;

                                    // 创建视频帧
                                    var drawingVisual = new System.Windows.Media.DrawingVisual();
                                    using (var drawingContext = drawingVisual.RenderOpen())
                                    {
                                        drawingContext.DrawVideo(mediaPlayer, new System.Windows.Rect(0, 0, videoWidth, videoHeight));
                                    }

                                    // 使用较低分辨率渲染以提高性能
                                    int renderWidth = Math.Min(videoWidth, 1280);
                                    int renderHeight = (int)(videoHeight * ((float)renderWidth / videoWidth));

                                    var renderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                                        renderWidth,
                                        renderHeight,
                                        96, 96,
                                        System.Windows.Media.PixelFormats.Pbgra32);
                                    renderBitmap.Render(drawingVisual);

                                    // 使用JpegBitmapEncoder代替PngBitmapEncoder，速度更快
                                    using (var stream = new MemoryStream())
                                    {
                                        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                                        encoder.QualityLevel = 85; // 平衡质量和性能
                                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderBitmap));
                                        encoder.Save(stream);
                                        stream.Position = 0;

                                        using (var originalBitmap = new Bitmap(stream))
                                        {
                                            // 创建缩放后的图片
                                            var scaledBitmap = new Bitmap(targetWidth, targetHeight);
                                            using (Graphics g = Graphics.FromImage(scaledBitmap))
                                            {
                                                // 使用更快的插值模式
                                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                                                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                                                g.DrawImage(originalBitmap, posX, posY, scaledWidth, scaledHeight);
                                            }

                                            // 使用BeginInvoke异步更新UI，避免阻塞渲染线程
                                            this.BeginInvoke(new Action(() =>
                                            {
                                                if (this.BackgroundImage != null)
                                                {
                                                    var oldImage = this.BackgroundImage;
                                                    this.BackgroundImage = null;
                                                    oldImage.Dispose();
                                                }
                                                this.BackgroundImage = scaledBitmap;
                                                this.BackgroundImageLayout = ImageLayout.None;
                                            }));
                                        }
                                    }
                                }
                            }
                            catch { }
                        };
                    }
                    catch { }
                }
                else
                {
                    // === 静态图片背景 ===
                    try
                    {
                        using (FileStream fs = new FileStream(mainImgPath, FileMode.Open, FileAccess.Read))
                        {
                            using (Image original = Image.FromStream(fs))
                            {
                                // 预裁切优化：按窗口当前ClientSize裁切好
                                Bitmap readyBg = new Bitmap(this.ClientSize.Width, this.ClientSize.Height);
                                using (Graphics g = Graphics.FromImage(readyBg))
                                {
                                    // 调用DrawAspectFillBackground实现等比例填满
                                    DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                                }
                                this.BackgroundImage = readyBg;
                                this.BackgroundImageLayout = ImageLayout.None;
                            }
                        }
                    }
                    catch { }
                }
            }           
            // 加载配置：静音设置
            if (File.Exists(muteFilePath))
            {
                bool.TryParse(File.ReadAllText(muteFilePath).Trim(), out isMuted);
            }

            // 初始化 FavoritesManager 并加载收藏
            string favConfigDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            favoritesManager = new FavoritesManager(favConfigDir);
            favoritesManager.LoadFromConfig();

            // 4. 初始化 PathManager 和 ModeManager 并加载配置
            pathManager = new PathManager();
            pathManager.LoadFromConfig();

            modeManager = new ModeManager();
            modeManager.LoadFromConfig();

            // 4.5 初始化 BlackWhiteListManager 并加载黑白名单
            blackWhiteListManager = new BlackWhiteListManager();

            // 5. 初始化 HistoryManager
            historyManager = new HistoryManager();
            
            // 订阅历史记录事件，用于实时刷新历史窗口
            historyManager.OnHistoryRecorded += () =>
            {
                // 如果历史记录窗口是打开的，刷新显示
                if (historyFormInstance != null && !historyFormInstance.IsDisposed && historyUpdateDataAction != null)
                {
                    try
                    {
                        historyUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果热力图窗口是打开的，刷新显示
                if (heatmapFormInstance != null && !heatmapFormInstance.IsDisposed && heatmapUpdateDataAction != null)
                {
                    try
                    {
                        heatmapUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果月度统计窗口是打开的，刷新显示
                if (monthlyStatsFormInstance != null && !monthlyStatsFormInstance.IsDisposed && monthlyStatsUpdateDataAction != null)
                {
                    try
                    {
                        monthlyStatsUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果不活跃天数窗口是打开的，刷新显示
                if (inactiveStreakFormInstance != null && !inactiveStreakFormInstance.IsDisposed && inactiveStreakUpdateDataAction != null)
                {
                    try
                    {
                        inactiveStreakUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果24小时活跃度窗口是打开的，刷新显示
                if (hourlyActivityFormInstance != null && !hourlyActivityFormInstance.IsDisposed && hourlyActivityUpdateDataAction != null)
                {
                    try
                    {
                        hourlyActivityUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果路径热力图窗口是打开的，刷新显示
                if (pathHeatmapFormInstance != null && !pathHeatmapFormInstance.IsDisposed && pathHeatmapUpdateDataAction != null)
                {
                    try
                    {
                        pathHeatmapUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果背景热力图窗口是打开的，刷新显示
                if (backgroundHeatmapFormInstance != null && !backgroundHeatmapFormInstance.IsDisposed && backgroundHeatmapUpdateDataAction != null)
                {
                    try
                    {
                        backgroundHeatmapUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果视频统计窗口是打开的，刷新显示
                if (videoHeatmapFormInstance != null && !videoHeatmapFormInstance.IsDisposed && videoHeatmapUpdateDataAction != null)
                {
                    try
                    {
                        videoHeatmapUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果最爱老师窗口是打开的，刷新显示
                if (teacherRankingFormInstance != null && !teacherRankingFormInstance.IsDisposed && teacherRankingUpdateDataAction != null)
                {
                    try
                    {
                        teacherRankingUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                if (favoriteTeacherCollectionRankingFormInstance != null && !favoriteTeacherCollectionRankingFormInstance.IsDisposed && favoriteTeacherCollectionRankingUpdateDataAction != null)
                {
                    try
                    {
                        favoriteTeacherCollectionRankingUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果最爱番号窗口是打开的，刷新显示
                if (favoriteCodeRankingFormInstance != null && !favoriteCodeRankingFormInstance.IsDisposed && favoriteCodeRankingUpdateDataAction != null)
                {
                    try
                    {
                        favoriteCodeRankingUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                RequestAnnualReportRefresh();
            };
            
            // 5.5 初始化 ImageHistoryManager
            imageHistoryManager = new ImageHistoryManager();
            
            // 订阅图片历史记录事件，用于实时刷新历史窗口（同一个窗口）
            imageHistoryManager.OnHistoryRecorded += () =>
            {
                // 如果历史记录窗口是打开的，刷新显示
                if (historyFormInstance != null && !historyFormInstance.IsDisposed && historyUpdateDataAction != null)
                {
                    try
                    {
                        historyUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果热力图窗口是打开的，刷新显示
                if (heatmapFormInstance != null && !heatmapFormInstance.IsDisposed && heatmapUpdateDataAction != null)
                {
                    try
                    {
                        heatmapUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果月度统计窗口是打开的，刷新显示
                if (monthlyStatsFormInstance != null && !monthlyStatsFormInstance.IsDisposed && monthlyStatsUpdateDataAction != null)
                {
                    try
                    {
                        monthlyStatsUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果不活跃天数窗口是打开的，刷新显示
                if (inactiveStreakFormInstance != null && !inactiveStreakFormInstance.IsDisposed && inactiveStreakUpdateDataAction != null)
                {
                    try
                    {
                        inactiveStreakUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果24小时活跃度窗口是打开的，刷新显示
                if (hourlyActivityFormInstance != null && !hourlyActivityFormInstance.IsDisposed && hourlyActivityUpdateDataAction != null)
                {
                    try
                    {
                        hourlyActivityUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果路径热力图窗口是打开的，刷新显示
                if (pathHeatmapFormInstance != null && !pathHeatmapFormInstance.IsDisposed && pathHeatmapUpdateDataAction != null)
                {
                    try
                    {
                        pathHeatmapUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果背景热力图窗口是打开的，刷新显示
                if (backgroundHeatmapFormInstance != null && !backgroundHeatmapFormInstance.IsDisposed && backgroundHeatmapUpdateDataAction != null)
                {
                    try
                    {
                        backgroundHeatmapUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果视频统计窗口是打开的，刷新显示
                if (videoHeatmapFormInstance != null && !videoHeatmapFormInstance.IsDisposed && videoHeatmapUpdateDataAction != null)
                {
                    try
                    {
                        videoHeatmapUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果最爱老师窗口是打开的，刷新显示
                if (teacherRankingFormInstance != null && !teacherRankingFormInstance.IsDisposed && teacherRankingUpdateDataAction != null)
                {
                    try
                    {
                        teacherRankingUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                if (favoriteTeacherCollectionRankingFormInstance != null && !favoriteTeacherCollectionRankingFormInstance.IsDisposed && favoriteTeacherCollectionRankingUpdateDataAction != null)
                {
                    try
                    {
                        favoriteTeacherCollectionRankingUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                // 如果最爱番号窗口是打开的，刷新显示
                if (favoriteCodeRankingFormInstance != null && !favoriteCodeRankingFormInstance.IsDisposed && favoriteCodeRankingUpdateDataAction != null)
                {
                    try
                    {
                        favoriteCodeRankingUpdateDataAction.Invoke();
                    }
                    catch { }
                }
                RequestAnnualReportRefresh();
            };

            // 6. 初始化配置写入防抖定时器 (1秒延迟)
            configDebounceTimer = new System.Windows.Forms.Timer();
            configDebounceTimer.Interval = 1000; // 1 second
            configDebounceTimer.Tick += (s, e) =>
            {
                configDebounceTimer.Stop();
                // Save all manager configurations
                try
                {
                    pathManager.SaveToConfig();
                    modeManager.SaveToConfig();
                    favoritesManager.SaveToConfig();
                }
                catch (Exception ex)
                {
                    LogError($"Error saving configuration: {ex.Message}");
                }
            };

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;


            // 2. 标签样式设置（防隐身 & 透明化）
            lblFolderName.BackColor = Color.Transparent;
            lblFolderName.AutoSize = false;
            lblFolderName.Text = ""; // 必须为空
            lblFolderName.ForeColor = Color.Transparent;
            lblFolderName.Cursor = Cursors.Default; // 初始状态无小手

            lblFileName.BackColor = Color.Transparent;
            lblFileName.AutoSize = false;
            lblFileName.Text = ""; // 必须为空
            lblFileName.ForeColor = Color.Transparent;
            lblFileName.Cursor = Cursors.Default; // 初始状态无小手

            // 3. 绑定事件
            lblFolderName.Paint += Label_Paint;
            lblFileName.Paint += Label_Paint;

            // 动态控制光标显示
            lblFolderName.MouseEnter += (s, e) =>
            {
                if (!string.IsNullOrEmpty(displayFolderName))
                    lblFolderName.Cursor = Cursors.Hand;
            };
            lblFolderName.MouseLeave += (s, e) =>
            {
                lblFolderName.Cursor = Cursors.Default;
            };

            lblFileName.MouseEnter += (s, e) =>
            {
                if (!string.IsNullOrEmpty(displayFileName) && !string.IsNullOrEmpty(currentVideoPath))
                    lblFileName.Cursor = Cursors.Hand;
            };
            lblFileName.MouseLeave += (s, e) =>
            {
                lblFileName.Cursor = Cursors.Default;
            };

            // 4. 重播功能（点击 Label 触发）
            lblFileName.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(currentVideoPath) && File.Exists(currentVideoPath))
                {
                    Process.Start(new ProcessStartInfo(currentVideoPath) { UseShellExecute = true });
                }
            };

            lblFolderName.Click += LblFolderName_Click;

            // 强制Form重绘
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);

            // 创建静音切换按钮
            CreateMuteButton();

            // 绑定按钮音效
            BindButtonSounds();

            // 【主界面视频控制】检测其他程序是否全屏/最大化
            System.Windows.Forms.Timer mainVideoTimer = new System.Windows.Forms.Timer();
            mainVideoTimer.Interval = 500; // 每500ms检测一次
            mainVideoTimer.Tick += (s, e) =>
            {
                if (mediaPlayer != null)
                {
                    // 检测是否有其他窗口全屏或最大化
                    bool shouldPause = IsAnyOtherWindowMaximizedOrFullscreen();

                    if (shouldPause && mediaPlayer.Position != TimeSpan.Zero)
                    {
                        // 有其他程序全屏/最大化，暂停视频
                        mediaPlayer.Pause();
                    }
                    else if (!shouldPause)
                    {
                        // 没有其他程序全屏/最大化，播放视频
                        if (mediaPlayer.Position == TimeSpan.Zero ||
                            mediaPlayer.NaturalDuration.HasTimeSpan &&
                            mediaPlayer.Position < mediaPlayer.NaturalDuration.TimeSpan)
                        {
                            mediaPlayer.Play();
                        }
                    }
                }
            };
            mainVideoTimer.Start();

        }

        private void 赛博选妃_Paint(object sender, PaintEventArgs e)
        {
            if (this.BackgroundImage != null)
            {                // 核心调用：让主界面也实现“等比例铺满、不留黑边、不扭曲”
                DrawAspectFillBackground(e.Graphics, this.BackgroundImage, this.ClientRectangle);

                // 如果你觉得主界面背景太亮，也可以在这里加一层极浅的遮罩（可选）
                // using (SolidBrush mask = new SolidBrush(Color.FromArgb(30, 0, 0, 0)))
                //     e.Graphics.FillRectangle(mask, this.ClientRectangle);
            }
        }

        private string? GetRandomImageFromSubDir(string subDir)
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "img", subDir);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                // 只支持视频和静态图片，不支持GIF
                var files = Directory.GetFiles(path, "*.*")
                    .Where(s => s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                s.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count > 0)
                {
                    Random rng = new Random(Guid.NewGuid().GetHashCode());
                    string? lastFile = null;
                    lastBackgroundBySubDir.TryGetValue(subDir, out lastFile);

                    List<string> candidates = files;
                    if (!string.IsNullOrWhiteSpace(lastFile) && files.Count > 1)
                    {
                        candidates = files
                            .Where(file => !string.Equals(file, lastFile, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (candidates.Count == 0)
                        {
                            candidates = files;
                        }
                    }

                    string selectedFile = candidates[rng.Next(candidates.Count)];
                    lastBackgroundBySubDir[subDir] = selectedFile;
                    
                    // 记录背景使用到日志
                    LogBackgroundUsage(selectedFile);
                    
                    return selectedFile;
                }
            }
            catch { }
            return null;
        }

        private string? GetRandomStaticImageFromSubDir(string subDir)
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "img", subDir);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                var files = Directory.GetFiles(path, "*.*")
                    .Where(s => s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                s.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count > 0)
                {
                    Random rng = new Random(Guid.NewGuid().GetHashCode());
                    string selectedFile = files[rng.Next(files.Count)];
                    LogBackgroundUsage(selectedFile);
                    return selectedFile;
                }
            }
            catch { }
            return null;
        }

        // 记录背景使用
        private void LogBackgroundUsage(string backgroundPath)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "BackgroundUsage.log");
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{backgroundPath}";
                File.AppendAllText(logPath, logEntry + Environment.NewLine);
            }
            catch
            {
                // 静默失败，不影响主功能
            }
        }

        private string NormalizeBackgroundUsageKey(string backgroundPath)
        {
            if (string.IsNullOrWhiteSpace(backgroundPath))
                return string.Empty;

            string fileName = Path.GetFileName(backgroundPath.Trim());
            return string.IsNullOrWhiteSpace(fileName) ? backgroundPath.Trim() : fileName;
        }

        // 创建圆角矩形路径的辅助方法
        private GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int cornerRadius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = cornerRadius * 2;

            // 左上角
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            // 右上角
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            // 右下角
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            // 左下角
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);

            path.CloseFigure();
            return path;
        }

        // 支持 float 参数的重载方法
        private GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float cornerRadius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = cornerRadius * 2;

            // 左上角
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            // 右上角
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            // 右下角
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            // 左下角
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);

            path.CloseFigure();
            return path;
        }

        public class HistoryTransparentForm : Form
        {
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= 0x02000000; // 彻底消灭闪烁的黑科技
                    return cp;
                }
            }
        }


        private void btnSelectPath_Click(object? sender, EventArgs e)
        {
            if (configFormInstance != null && !configFormInstance.IsDisposed)
            {
                if (configFormInstance.WindowState == FormWindowState.Minimized)
                {
                    configFormInstance.WindowState = FormWindowState.Normal;
                }

                configFormInstance.Show();
                configFormInstance.Activate();
                return;
            }

            // 使用 HistoryTransparentForm 作为基类
            Form configForm = new HistoryTransparentForm();
            configFormInstance = configForm;

            // 暂停布局，减少初始化时的闪烁
            configForm.SuspendLayout();
            
            // --- 创建ToolTip控件用于显示完整文件夹名称 ---
            ToolTip imageFavToolTip = new ToolTip();
            imageFavToolTip.AutoPopDelay = 5000;
            imageFavToolTip.InitialDelay = 500;
            imageFavToolTip.ReshowDelay = 100;
            imageFavToolTip.ShowAlways = true;
            imageFavToolTip.UseAnimation = false; // 禁用动画，立即显示/隐藏
            imageFavToolTip.UseFading = false;    // 禁用淡入淡出效果
            
            ToolTip videoFavToolTip = new ToolTip();
            videoFavToolTip.AutoPopDelay = 5000;
            videoFavToolTip.InitialDelay = 500;
            videoFavToolTip.ReshowDelay = 100;
            videoFavToolTip.ShowAlways = true;
            videoFavToolTip.UseAnimation = false; // 禁用动画，立即显示/隐藏
            videoFavToolTip.UseFading = false;    // 禁用淡入淡出效果
            
            // --- 1. 基本属性设置 ---
            configForm.Text = "配置中心";
            configForm.Icon = this.Icon;
            configForm.MaximizeBox = false;
            configForm.MinimizeBox = false;
            configForm.FormBorderStyle = FormBorderStyle.FixedDialog;
            configForm.StartPosition = FormStartPosition.CenterParent;
            configForm.AutoScroll = false;
            configForm.BackColor = Color.Black;
            ApplyRescaling(configForm, 1015, 685);

            // --- 2. 随机背景（从 img/Config 文件夹，支持视频和静态图片）---
            System.Windows.Media.MediaPlayer? configMediaPlayer = null;
            string? configImgPath = GetRandomImageFromSubDir("Config");
            if (!string.IsNullOrEmpty(configImgPath) && File.Exists(configImgPath))
            {
                string ext = Path.GetExtension(configImgPath).ToLower();
                bool isVideo = ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".wmv" || ext == ".flv";

                if (isVideo)
                {
                    // === 视频背景 ===
                    try
                    {
                            configMediaPlayer = new System.Windows.Media.MediaPlayer();
                            configMediaPlayer.Open(new Uri(configImgPath, UriKind.Absolute));
                            configMediaPlayer.Volume = isMuted ? 0 : 1.0;
                            configMediaPlayer.MediaEnded += (s, e) =>
                            {
                                configMediaPlayer.Position = TimeSpan.Zero;
                                configMediaPlayer.Play();
                            };

                            configMediaPlayer.ScrubbingEnabled = true;
                            configMediaPlayer.Play();

                            int targetWidth = configForm.ClientSize.Width;
                            int targetHeight = configForm.ClientSize.Height;

                            // 视频渲染事件
                            System.Windows.Media.CompositionTarget.Rendering += (s, e) =>
                            {
                                try
                                {
                                    if (configMediaPlayer != null && configMediaPlayer.NaturalVideoWidth > 0 &&
                                        configMediaPlayer.NaturalVideoHeight > 0 && !configForm.IsDisposed)
                                    {
                                        int videoWidth = configMediaPlayer.NaturalVideoWidth;
                                        int videoHeight = configMediaPlayer.NaturalVideoHeight;

                                        var drawingVisual = new System.Windows.Media.DrawingVisual();
                                        using (var drawingContext = drawingVisual.RenderOpen())
                                        {
                                            drawingContext.DrawVideo(configMediaPlayer, new System.Windows.Rect(0, 0, videoWidth, videoHeight));
                                        }

                                        // 直接使用原始视频尺寸渲染
                                        var renderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                                            videoWidth, videoHeight, 96, 96,
                                            System.Windows.Media.PixelFormats.Pbgra32);
                                        renderBitmap.Render(drawingVisual);

                                        using (var stream = new MemoryStream())
                                        {
                                            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                                            encoder.QualityLevel = 75; // 降低质量以提高性能
                                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderBitmap));
                                            encoder.Save(stream);
                                            stream.Position = 0;

                                            using (var renderedFrame = new Bitmap(stream))
                                            {
                                                // 预计算缩放参数
                                                float ratio = Math.Max((float)targetWidth / renderedFrame.Width,
                                                                      (float)targetHeight / renderedFrame.Height);
                                                int newWidth = (int)(renderedFrame.Width * ratio);
                                                int newHeight = (int)(renderedFrame.Height * ratio);
                                                int posX = (targetWidth - newWidth) / 2;
                                                int posY = (targetHeight - newHeight) / 2;

                                                // 创建目标bitmap
                                                var scaledBitmap = new Bitmap(targetWidth, targetHeight);
                                                using (Graphics g = Graphics.FromImage(scaledBitmap))
                                                {
                                                    // 使用快速渲染模式
                                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                                                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                                                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                                                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                                                    g.DrawImage(renderedFrame, posX, posY, newWidth, newHeight);
                                                }

                                                if (!configForm.IsDisposed)
                                                {
                                                    configForm.BeginInvoke(new Action(() =>
                                                    {
                                                        if (!configForm.IsDisposed)
                                                        {
                                                            var oldImage = configForm.BackgroundImage;
                                                            configForm.BackgroundImage = scaledBitmap;
                                                            configForm.BackgroundImageLayout = ImageLayout.None;
                                                            oldImage?.Dispose();
                                                        }
                                                        else
                                                        {
                                                            scaledBitmap.Dispose();
                                                        }
                                                    }));
                                                }
                                                else
                                                {
                                                    scaledBitmap.Dispose();
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            };
                        }
                        catch { }
                    }
                    else
                    {
                        // === 静态图片背景 ===
                        try
                        {
                            using (FileStream fs = new FileStream(configImgPath, FileMode.Open, FileAccess.Read))
                            {
                                using (Image original = Image.FromStream(fs))
                                {
                                    Bitmap readyBg = new Bitmap(configForm.ClientSize.Width, configForm.ClientSize.Height);
                                    using (Graphics g = Graphics.FromImage(readyBg))
                                    {
                                        DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                                    }
                                    configForm.BackgroundImage = readyBg;
                                    configForm.BackgroundImageLayout = ImageLayout.None;
                                }
                            }
                        }
                        catch { }
                    }
                }

                // 窗口关闭时清理视频资源
                configForm.FormClosing += (s, e) =>
                {
                    if (configMediaPlayer != null)
                    {
                        configMediaPlayer.Stop();
                        configMediaPlayer.Close();
                        configMediaPlayer = null;
                    }
                    if (configForm.BackgroundImage != null)
                    {
                        var img = configForm.BackgroundImage;
                        configForm.BackgroundImage = null;
                        img.Dispose();
                    }
                };

                // 启用双缓冲
                typeof(Form).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, configForm, new object[] { true });

                var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (setStyleMethod != null)
                {
                    setStyleMethod.Invoke(configForm, new object[] {
                        ControlStyles.AllPaintingInWmPaint |
                        ControlStyles.UserPaint |
                        ControlStyles.OptimizedDoubleBuffer, true
                    });
                }

                // 绘制背景和半透明遮罩
                configForm.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;

                    if (configForm.BackgroundImage != null)
                    {
                        // 直接绘制（已经在渲染时处理好了缩放）
                        g.DrawImage(configForm.BackgroundImage, 0, 0);
                    }

                    // 绘制半透明黑色滤镜（降低透明度到60，让背景更明显）
                    using (SolidBrush mask = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                        g.FillRectangle(mask, configForm.ClientRectangle);
                };

                // ==================== 左上：路径选择区域 ====================
                Panel pathPanel = new Panel()
                {
                    Left = S(15),
                    Top = S(15),
                    Width = S(338),
                    Height = S(280),
                    BackColor = Color.Transparent,
                    BorderStyle = BorderStyle.None
                };

                // 启用双缓冲以减少闪烁和提升性能
                typeof(Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, pathPanel, new object[] { true });

                configForm.Controls.Add(pathPanel);

                // 绘制路径面板背景
                pathPanel.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    int cornerRadius = S(10); // 圆角半径
                    Rectangle panelRect = new Rectangle(0, 0, pathPanel.Width, pathPanel.Height);

                    using (GraphicsPath roundedPath = CreateRoundedRectanglePath(panelRect, cornerRadius))
                    {
                        if (configForm.BackgroundImage != null)
                        {
                            // 如果有背景图片，先绘制背景图片的对应区域
                            Rectangle bgRect = configForm.RectangleToClient(
                                pathPanel.RectangleToScreen(pathPanel.ClientRectangle)
                            );

                            // 设置裁剪区域为圆角矩形
                            g.SetClip(roundedPath);
                            g.DrawImage(configForm.BackgroundImage,
                                pathPanel.ClientRectangle,
                                bgRect,
                                GraphicsUnit.Pixel);
                            g.ResetClip();
                        }

                        // 绘制半透明白色背景（无论是否有背景图片都绘制）
                        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                            g.FillPath(bgBrush, roundedPath);
                    }
                };

                int pathY = S(10);

                // 路径选择标题
                Label lblPathTitle = new Label()
                {
                    Text = "多路径管理",
                    Left = S(10),
                    Top = pathY,
                    Width = S(320),
                    Height = S(25),
                    Font = new Font("微软雅黑", 12, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                    BackColor = Color.Transparent
                };
                pathPanel.Controls.Add(lblPathTitle);
                pathY += S(30);

                // 创建6个路径槽位
                CheckBox[] pathCheckBoxes = new CheckBox[6];
                TextBox[] pathTextBoxes = new TextBox[6];
                Button[] pathButtons = new Button[6];
                Label[] pathRadioLabels = new Label[6]; // 用Label绘制圆点

                for (int i = 0; i < 6; i++)
                {
                    int slotIndex = i;

                    // 使用Label绘制纯圆形RadioButton
                    pathRadioLabels[i] = new Label()
                    {
                        Text = "",
                        Left = S(5),
                        Top = pathY + S(3),
                        Width = S(18),
                        Height = S(18),
                        BackColor = Color.Transparent,
                        Cursor = Cursors.Hand,
                        Tag = false // 用Tag存储选中状态
                    };

                    // 绘制圆形
                    pathRadioLabels[i].Paint += (s, pe) =>
                    {
                        Label lbl = (Label)s!;
                        pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                        bool isChecked = (bool)(lbl.Tag ?? false);

                        // 绘制外圆（灰色边框）
                        using (Pen pen = new Pen(Color.Gray, 2))
                        {
                            pe.Graphics.DrawEllipse(pen, 1, 1, lbl.Width - 3, lbl.Height - 3);
                        }

                        // 绘制内部填充
                        if (isChecked)
                        {
                            // 选中：蓝色内圆填充
                            using (SolidBrush brush = new SolidBrush(Color.FromArgb(100, 150, 255)))
                            {
                                pe.Graphics.FillEllipse(brush, 4, 4, lbl.Width - 8, lbl.Height - 8);
                            }
                        }
                        else
                        {
                            // 未选中：白色内圆填充
                            using (SolidBrush brush = new SolidBrush(Color.White))
                            {
                                pe.Graphics.FillEllipse(brush, 3, 3, lbl.Width - 6, lbl.Height - 6);
                            }
                        }
                    };

                    // 点击切换选中状态
                    pathRadioLabels[i].Click += (s, ee) =>
                    {
                        Label lbl = (Label)s!;
                        bool currentState = (bool)(lbl.Tag ?? false);
                        lbl.Tag = !currentState;
                        lbl.Invalidate(); // 重绘
                    };

                    pathPanel.Controls.Add(pathRadioLabels[i]);

                    // 创建隐藏的CheckBox用于保存状态
                    pathCheckBoxes[i] = new CheckBox()
                    {
                        Visible = false,
                        Checked = false
                    };

                    // 路径标签
                    Label lblPath = new Label()
                    {
                        Text = $"路径{i + 1}",
                        Left = S(23),
                        Top = pathY,
                        Width = S(50),
                        Height = S(25),
                        TextAlign = ContentAlignment.MiddleLeft,
                        BackColor = Color.Transparent,
                        Font = new Font("微软雅黑", 10, FontStyle.Bold)
                    };
                    pathPanel.Controls.Add(lblPath);

                    // 路径显示框（使用Panel实现透明效果，只读显示）
                    pathTextBoxes[i] = new TextBox()
                    {
                        Left = S(73),
                        Top = pathY,
                        Width = S(190),
                        Height = S(25),
                        ReadOnly = true,
                        BackColor = Color.White,
                        BorderStyle = BorderStyle.FixedSingle,
                        Text = ""
                    };

                    // 创建透明背景Panel覆盖在TextBox上
                    Panel pathBgPanel = new Panel()
                    {
                        Left = pathTextBoxes[i].Left,
                        Top = pathTextBoxes[i].Top,
                        Width = pathTextBoxes[i].Width,
                        Height = pathTextBoxes[i].Height,
                        BackColor = Color.Transparent
                    };

                    // 绘制透明背景
                    pathBgPanel.Paint += (s, pe) =>
                    {
                        Graphics g = pe.Graphics;
                        Panel panel = (Panel)s!;

                        // 绘制背景图片
                        if (configForm.BackgroundImage != null)
                        {
                            Rectangle panelRect = configForm.RectangleToClient(panel.RectangleToScreen(panel.ClientRectangle));
                            g.DrawImage(configForm.BackgroundImage, panel.ClientRectangle, panelRect, GraphicsUnit.Pixel);

                            // 绘制半透明白色遮罩
                            using (SolidBrush mask = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                                g.FillRectangle(mask, panel.ClientRectangle);
                        }

                        // 绘制边框
                        using (Pen borderPen = new Pen(Color.Gray, 1))
                            g.DrawRectangle(borderPen, 0, 0, panel.Width - 1, panel.Height - 1);

                        // 绘制文本
                        if (!string.IsNullOrEmpty(pathTextBoxes[slotIndex].Text))
                        {
                            TextRenderer.DrawText(g, pathTextBoxes[slotIndex].Text, pathTextBoxes[slotIndex].Font,
                                panel.ClientRectangle, Color.Black, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                        }
                    };

                    // 当TextBox文本改变时，重绘Panel
                    pathTextBoxes[i].TextChanged += (s, e) =>
                    {
                        pathBgPanel.Invalidate();
                    };

                    // 隐藏原TextBox，只用它存储数据
                    pathTextBoxes[i].Visible = false;
                    pathPanel.Controls.Add(pathTextBoxes[i]);
                    pathPanel.Controls.Add(pathBgPanel);
                    pathBgPanel.BringToFront();

                    // 选择按钮
                    pathButtons[i] = new NoFocusButton()
                    {
                        Text = "选择",
                        Left = S(268),
                        Top = pathY,
                        Width = S(60),
                        Height = S(25),
                        FlatStyle = FlatStyle.Flat,
                        BackColor = Color.Transparent,
                        Cursor = Cursors.Hand
                    };
                    pathButtons[i].FlatAppearance.BorderSize = 0;
                    pathButtons[i].FlatAppearance.MouseOverBackColor = Color.Transparent;
                    pathButtons[i].FlatAppearance.MouseDownBackColor = Color.Transparent;

                    // 设置圆角区域（在HandleCreated中设置，确保控件已创建）
                    pathButtons[i].HandleCreated += (s, e) =>
                    {
                        Button btn = (Button)s!;
                        using (GraphicsPath path = CreateRoundedRectanglePath(
                            new Rectangle(0, 0, btn.Width, btn.Height), 5))
                        {
                            btn.Region = new Region(path);
                        }
                    };

                    // 绘制圆角背景和描边
                    pathButtons[i].Paint += (s, pe) =>
                    {
                        Button btn = (Button)s!;
                        Graphics g = pe.Graphics;
                        g.SmoothingMode = SmoothingMode.AntiAlias;

                        // 背景填充使用完整尺寸
                        using (GraphicsPath bgPath = CreateRoundedRectanglePath(
                            new Rectangle(0, 0, btn.Width, btn.Height), 5))
                        {
                            using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                            {
                                g.FillPath(brush, bgPath);
                            }
                        }

                        // 描边使用稍微内缩的路径（0.5像素），让描边更贴近边缘
                        RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 4.5f))
                        {
                            using (Pen pen = new Pen(Color.Gray, 1))
                            {
                                g.DrawPath(pen, borderPath);
                            }
                        }

                        // 绘制文本
                        TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                            btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    };

                    pathButtons[i].Click += (s, ee) =>
                    {
                        FolderBrowserDialog fbd = new FolderBrowserDialog();
                        if (fbd.ShowDialog() == DialogResult.OK)
                        {
                            string selectedPath = fbd.SelectedPath;
                            
                            // 验证路径：禁止C盘根目录
                            if (selectedPath.Length == 3 && selectedPath[1] == ':' && selectedPath[2] == '\\')
                            {
                                // 这是一个磁盘根目录（如 C:\, D:\）
                                char driveLetter = char.ToUpper(selectedPath[0]);
                                if (driveLetter == 'C')
                                {
                                    MessageBox.Show("不允许选择C盘根目录！\n\n原因：C盘根目录包含系统文件，扫描可能导致性能问题。\n\n建议：请选择C盘下的具体文件夹。",
                                        "路径限制", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    return;
                                }
                            }
                            
                            pathTextBoxes[slotIndex].Text = selectedPath;
                            pathRadioLabels[slotIndex].Tag = true;
                            pathRadioLabels[slotIndex].Invalidate();
                            pathCheckBoxes[slotIndex].Checked = true;
                        }
                    };
                    pathPanel.Controls.Add(pathButtons[i]);
                    pathY += S(40);
                }

                // ==================== 左下：Group B 模式区域 ====================
                Panel groupBPanel = new Panel()
                {
                    Left = S(15),
                    Top = S(310),
                    Width = S(338),
                    Height = S(320),
                    BackColor = Color.Transparent,
                    BorderStyle = BorderStyle.None
                };

                // 启用双缓冲
                typeof(Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, groupBPanel, new object[] { true });

                configForm.Controls.Add(groupBPanel);

                // 绘制 Group B 面板背景
                groupBPanel.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    int cornerRadius = S(10); // 圆角半径
                    Rectangle panelRect = new Rectangle(0, 0, groupBPanel.Width, groupBPanel.Height);

                    using (GraphicsPath roundedPath = CreateRoundedRectanglePath(panelRect, cornerRadius))
                    {
                        if (configForm.BackgroundImage != null)
                        {
                            // 如果有背景图片，先绘制背景图片的对应区域
                            Rectangle bgRect = configForm.RectangleToClient(
                                groupBPanel.RectangleToScreen(groupBPanel.ClientRectangle)
                            );

                            // 设置裁剪区域为圆角矩形
                            g.SetClip(roundedPath);
                            g.DrawImage(configForm.BackgroundImage,
                                groupBPanel.ClientRectangle,
                                bgRect,
                                GraphicsUnit.Pixel);
                            g.ResetClip();
                        }

                        // 绘制半透明白色背景（无论是否有背景图片都绘制）
                        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                            g.FillPath(bgBrush, roundedPath);
                    }
                };

                int groupBY = S(10);

                // 只看最爱按钮
                Button btnFavoritesOnly = new NoFocusButton()
                {
                    Text = "只看最爱",
                    Left = S(228),
                    Top = S(75),
                    Width = S(100),
                    Height = S(35),
                    Font = new Font("微软雅黑", 11, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnFavoritesOnly.FlatAppearance.BorderSize = 0;
                btnFavoritesOnly.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnFavoritesOnly.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnFavoritesOnly.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnFavoritesOnly.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    bool isSelected = btn.Tag as string == "selected";

                    // 绘制半透明白色背景
                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    if (isSelected)
                    {
                        // 选中状态：内层黑色细线
                        RectangleF innerBorderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath innerBorderPath = CreateRoundedRectanglePath(innerBorderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, innerBorderPath);
                            }
                        }

                        // 高亮半透明圆角边框
                        RectangleF highlightRect = new RectangleF(2f, 2f, btn.Width - 4, btn.Height - 4);
                        using (GraphicsPath highlightPath = CreateRoundedRectanglePath(highlightRect, 6.5f))
                        {
                            using (Pen highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 2))
                            {
                                g.DrawPath(highlightPen, highlightPath);
                            }
                        }

                        // 外层黑色细线
                        RectangleF outerBorderRect = new RectangleF(3.5f, 3.5f, btn.Width - 7, btn.Height - 7);
                        using (GraphicsPath outerBorderPath = CreateRoundedRectanglePath(outerBorderRect, 5.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, outerBorderPath);
                            }
                        }
                    }
                    else
                    {
                        // 未选中状态：黑色细线边框
                        RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, borderPath);
                            }
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                groupBPanel.Controls.Add(btnFavoritesOnly);

                // 从新到旧按钮
                Button btnNewestToOldest = new NoFocusButton()
                {
                    Text = "从新到旧",
                    Left = S(228),
                    Top = S(115),
                    Width = S(100),
                    Height = S(35),
                    Font = new Font("微软雅黑", 11, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnNewestToOldest.FlatAppearance.BorderSize = 0;
                btnNewestToOldest.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnNewestToOldest.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnNewestToOldest.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnNewestToOldest.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    bool isSelected = btn.Tag as string == "selected";

                    // 绘制半透明白色背景
                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    if (isSelected)
                    {
                        // 选中状态：内层黑色细线
                        RectangleF innerBorderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath innerBorderPath = CreateRoundedRectanglePath(innerBorderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, innerBorderPath);
                            }
                        }

                        // 高亮半透明圆角边框
                        RectangleF highlightRect = new RectangleF(2f, 2f, btn.Width - 4, btn.Height - 4);
                        using (GraphicsPath highlightPath = CreateRoundedRectanglePath(highlightRect, 6.5f))
                        {
                            using (Pen highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 2))
                            {
                                g.DrawPath(highlightPen, highlightPath);
                            }
                        }

                        // 外层黑色细线
                        RectangleF outerBorderRect = new RectangleF(3.5f, 3.5f, btn.Width - 7, btn.Height - 7);
                        using (GraphicsPath outerBorderPath = CreateRoundedRectanglePath(outerBorderRect, 5.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, outerBorderPath);
                            }
                        }
                    }
                    else
                    {
                        // 未选中状态：黑色细线边框
                        RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, borderPath);
                            }
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                groupBPanel.Controls.Add(btnNewestToOldest);
                groupBY += S(60);

                // 从旧到新按钮
                Button btnOldestToNewest = new NoFocusButton()
                {
                    Text = "从旧到新",
                    Left = S(228),
                    Top = S(155),
                    Width = S(100),
                    Height = S(35),
                    Font = new Font("微软雅黑", 11, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnOldestToNewest.FlatAppearance.BorderSize = 0;
                btnOldestToNewest.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnOldestToNewest.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnOldestToNewest.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnOldestToNewest.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    bool isSelected = btn.Tag as string == "selected";

                    // 绘制半透明白色背景
                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    if (isSelected)
                    {
                        // 选中状态：内层黑色细线
                        RectangleF innerBorderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath innerBorderPath = CreateRoundedRectanglePath(innerBorderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, innerBorderPath);
                            }
                        }

                        // 高亮半透明圆角边框
                        RectangleF highlightRect = new RectangleF(2f, 2f, btn.Width - 4, btn.Height - 4);
                        using (GraphicsPath highlightPath = CreateRoundedRectanglePath(highlightRect, 6.5f))
                        {
                            using (Pen highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 2))
                            {
                                g.DrawPath(highlightPen, highlightPath);
                            }
                        }

                        // 外层黑色细线
                        RectangleF outerBorderRect = new RectangleF(3.5f, 3.5f, btn.Width - 7, btn.Height - 7);
                        using (GraphicsPath outerBorderPath = CreateRoundedRectanglePath(outerBorderRect, 5.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, outerBorderPath);
                            }
                        }
                    }
                    else
                    {
                        // 未选中状态：黑色细线边框
                        RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, borderPath);
                            }
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                groupBPanel.Controls.Add(btnOldestToNewest);
                groupBY += S(60);

                // 从未看过按钮
                Button btnNeverWatched = new NoFocusButton()
                {
                    Text = "从未看过",
                    Left = S(228),
                    Top = S(195),
                    Width = S(100),
                    Height = S(35),
                    Font = new Font("微软雅黑", 11, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnNeverWatched.FlatAppearance.BorderSize = 0;
                btnNeverWatched.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnNeverWatched.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnNeverWatched.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnNeverWatched.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    bool isSelected = btn.Tag as string == "selected";

                    // 绘制半透明白色背景
                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    if (isSelected)
                    {
                        // 选中状态：内层黑色细线
                        RectangleF innerBorderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath innerBorderPath = CreateRoundedRectanglePath(innerBorderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, innerBorderPath);
                            }
                        }

                        // 高亮半透明圆角边框
                        RectangleF highlightRect = new RectangleF(2f, 2f, btn.Width - 4, btn.Height - 4);
                        using (GraphicsPath highlightPath = CreateRoundedRectanglePath(highlightRect, 6.5f))
                        {
                            using (Pen highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 2))
                            {
                                g.DrawPath(highlightPen, highlightPath);
                            }
                        }

                        // 外层黑色细线
                        RectangleF outerBorderRect = new RectangleF(3.5f, 3.5f, btn.Width - 7, btn.Height - 7);
                        using (GraphicsPath outerBorderPath = CreateRoundedRectanglePath(outerBorderRect, 5.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, outerBorderPath);
                            }
                        }
                    }
                    else
                    {
                        // 未选中状态：黑色细线边框
                        RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, borderPath);
                            }
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                groupBPanel.Controls.Add(btnNeverWatched);
                groupBY += S(60); // 增加 Y 坐标，避免与排除设置重叠

                // 超分按钮
                Button btnSuperResolution = new NoFocusButton()
                {
                    Text = "超分辨率",
                    Left = S(228),
                    Top = S(235),
                    Width = S(100),
                    Height = S(35),
                    Font = new Font("微软雅黑", 11, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnSuperResolution.FlatAppearance.BorderSize = 0;
                btnSuperResolution.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnSuperResolution.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnSuperResolution.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnSuperResolution.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    bool isSelected = btn.Tag as string == "selected";

                    // 绘制半透明白色背景
                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    if (isSelected)
                    {
                        // 选中状态：内层黑色细线
                        RectangleF innerBorderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath innerBorderPath = CreateRoundedRectanglePath(innerBorderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, innerBorderPath);
                            }
                        }

                        // 高亮半透明圆角边框
                        RectangleF highlightRect = new RectangleF(2f, 2f, btn.Width - 4, btn.Height - 4);
                        using (GraphicsPath highlightPath = CreateRoundedRectanglePath(highlightRect, 6.5f))
                        {
                            using (Pen highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 2))
                            {
                                g.DrawPath(highlightPen, highlightPath);
                            }
                        }

                        // 外层黑色细线
                        RectangleF outerBorderRect = new RectangleF(3.5f, 3.5f, btn.Width - 7, btn.Height - 7);
                        using (GraphicsPath outerBorderPath = CreateRoundedRectanglePath(outerBorderRect, 5.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, outerBorderPath);
                            }
                        }
                    }
                    else
                    {
                        // 未选中状态：黑色细线边框
                        RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, borderPath);
                            }
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                groupBPanel.Controls.Add(btnSuperResolution);
                groupBY += S(60);

                // 无码破解按钮
                Button btnUncensored = new NoFocusButton()
                {
                    Text = "无码破解",
                    Left = S(228),
                    Top = S(275),
                    Width = S(100),
                    Height = S(35),
                    Font = new Font("微软雅黑", 11, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnUncensored.FlatAppearance.BorderSize = 0;
                btnUncensored.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnUncensored.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnUncensored.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnUncensored.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    bool isSelected = btn.Tag as string == "selected";

                    // 绘制半透明白色背景
                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    if (isSelected)
                    {
                        // 选中状态：内层黑色细线
                        RectangleF innerBorderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath innerBorderPath = CreateRoundedRectanglePath(innerBorderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, innerBorderPath);
                            }
                        }

                        // 高亮半透明圆角边框
                        RectangleF highlightRect = new RectangleF(2f, 2f, btn.Width - 4, btn.Height - 4);
                        using (GraphicsPath highlightPath = CreateRoundedRectanglePath(highlightRect, 6.5f))
                        {
                            using (Pen highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 2))
                            {
                                g.DrawPath(highlightPen, highlightPath);
                            }
                        }

                        // 外层黑色细线
                        RectangleF outerBorderRect = new RectangleF(3.5f, 3.5f, btn.Width - 7, btn.Height - 7);
                        using (GraphicsPath outerBorderPath = CreateRoundedRectanglePath(outerBorderRect, 5.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, outerBorderPath);
                            }
                        }
                    }
                    else
                    {
                        // 未选中状态：黑色细线边框
                        RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, borderPath);
                            }
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                groupBPanel.Controls.Add(btnUncensored);
                groupBY += S(60);

                // 自定义关键词区域 - 使用ListView + 新增/删除按钮（类似黑白名单）
                Label lblCustomKeywords = new Label()
                {
                    Text = "自定义关键词：",
                    Left = S(10),
                    Top = S(195),  // 与无码破解按钮对齐
                    Width = S(100),
                    Height = S(20),
                    Font = new Font("微软雅黑", 9, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                groupBPanel.Controls.Add(lblCustomKeywords);

                // 使用ListView显示关键词列表（半透明背景）
                ListView lstCustomKeywords = new ListView()
                {
                    Left = S(10),
                    Top = S(215),  // 与无码破解按钮对齐
                    Width = S(150),
                    Height = S(95),
                    BackColor = Color.FromArgb(240, 240, 240), // 使用浅灰色背景作为后备
                    ForeColor = Color.Black,
                    BorderStyle = BorderStyle.FixedSingle,
                    View = View.Details,
                    OwnerDraw = true,
                    FullRowSelect = true,
                    HeaderStyle = ColumnHeaderStyle.None,
                    MultiSelect = false,
                    Scrollable = true,
                    LabelEdit = true
                };
                groupBPanel.Controls.Add(lstCustomKeywords);

                // 添加列
                lstCustomKeywords.Columns.Add("", lstCustomKeywords.ClientSize.Width);

                // 启用双缓冲（温和方式，不干扰OwnerDraw）
                typeof(ListView).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, lstCustomKeywords, new object[] { true });

                // 隐藏列头
                lstCustomKeywords.DrawColumnHeader += (s, e) => { };

                // 动态调整列宽度
                bool isResizingKeywordsList = false;
                lstCustomKeywords.Resize += (s, e) =>
                {
                    if (isResizingKeywordsList) return;
                    isResizingKeywordsList = true;
                    try
                    {
                        if (lstCustomKeywords.Columns.Count > 0 && lstCustomKeywords.ClientSize.Width > 0)
                        {
                            lstCustomKeywords.Columns[0].Width = lstCustomKeywords.ClientSize.Width;
                        }
                        if (lstCustomKeywords.IsHandleCreated)
                        {
                            ShowScrollBar(lstCustomKeywords.Handle, 0, false);
                            ShowScrollBar(lstCustomKeywords.Handle, SB_VERT, false);
                        }
                    }
                    finally
                    {
                        isResizingKeywordsList = false;
                    }
                };

                // 加载现有关键词
                string existingKeywords = modeManager.GetCustomKeywords();
                List<string> keywordList = new List<string>();
                if (!string.IsNullOrEmpty(existingKeywords))
                {
                    keywordList = existingKeywords.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim())
                        .Where(k => !string.IsNullOrEmpty(k))
                        .ToList();
                }

                int lastKeywordItemIndex = -1;
                foreach (string keyword in keywordList)
                {
                    ListViewItem lvi = new ListViewItem(keyword);
                    lstCustomKeywords.Items.Add(lvi);
                    lastKeywordItemIndex = lstCustomKeywords.Items.Count - 1;
                }

                // 添加填充项
                int keywordListHeight = lstCustomKeywords.ClientSize.Height;
                int keywordItemHeight = lstCustomKeywords.Font.Height + 4;
                // 根据屏幕分辨率动态调整填充倍数：4K屏需要更多填充项
                int fillMultiplier = GetListViewFillMultiplier();
                int keywordFillerCount = (int)Math.Ceiling((double)keywordListHeight / keywordItemHeight) * fillMultiplier;

                for (int i = 0; i < keywordFillerCount; i++)
                {
                    var fillerItem = new ListViewItem("");
                    fillerItem.Tag = "FILLER";
                    lstCustomKeywords.Items.Add(fillerItem);
                }

                // 绘制每个项（半透明背景）
                lstCustomKeywords.DrawItem += (s, e) =>
                {
                    if (e.Item == null || e.Item.Index < 0 || e.Item.Index >= lstCustomKeywords.Items.Count) return;
                    
                    Graphics g = e.Graphics;

                    // 绘制背景图片或默认背景
                    if (configForm.BackgroundImage != null)
                    {
                        try
                        {
                            Rectangle listRect = configForm.RectangleToClient(lstCustomKeywords.RectangleToScreen(e.Bounds));
                            g.DrawImage(configForm.BackgroundImage, e.Bounds, listRect, GraphicsUnit.Pixel);
                        }
                        catch
                        {
                            using (SolidBrush defaultBg = new SolidBrush(Color.FromArgb(240, 240, 240)))
                                g.FillRectangle(defaultBg, e.Bounds);
                        }
                    }
                    else
                    {
                        using (SolidBrush defaultBg = new SolidBrush(Color.FromArgb(240, 240, 240)))
                            g.FillRectangle(defaultBg, e.Bounds);
                    }

                    // 绘制半透明白色遮罩（与黑白名单一致的透明度）
                    using (SolidBrush mask = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                        g.FillRectangle(mask, e.Bounds);

                    // 填充项只绘制背景
                    if (e.Item != null && e.Item.Tag is string && (string)e.Item.Tag == "FILLER")
                    {
                        return;
                    }

                    // 选中项高亮
                    if (e.Item != null && e.Item.Selected)
                    {
                        using (SolidBrush highlightBrush = new SolidBrush(Color.FromArgb(100, 135, 206, 250)))
                            g.FillRectangle(highlightBrush, e.Bounds);
                    }

                    // 绘制文字
                    if (e.Item != null && e.Item.Index >= 0 && !string.IsNullOrEmpty(e.Item.Text))
                    {
                        TextRenderer.DrawText(g, e.Item.Text, lstCustomKeywords.Font, e.Bounds, Color.Black,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    }
                };

                // 双击编辑
                lstCustomKeywords.MouseDoubleClick += (s, e) =>
                {
                    if (lstCustomKeywords.SelectedItems.Count > 0)
                    {
                        var selectedItem = lstCustomKeywords.SelectedItems[0];
                        if (selectedItem.Tag == null || (selectedItem.Tag is string && (string)selectedItem.Tag != "FILLER"))
                        {
                            selectedItem.BeginEdit();
                        }
                    }
                };

                // 编辑完成后保存
                lstCustomKeywords.AfterLabelEdit += (s, e) =>
                {
                    if (e.Label != null && !string.IsNullOrWhiteSpace(e.Label))
                    {
                        lstCustomKeywords.Items[e.Item].Text = e.Label.Trim();
                    }
                    else if (e.Label != null && string.IsNullOrWhiteSpace(e.Label))
                    {
                        e.CancelEdit = true;
                    }
                };

                // 隐藏滚动条
                lstCustomKeywords.HandleCreated += (s, e) =>
                {
                    ShowScrollBar(lstCustomKeywords.Handle, 0, false);
                    ShowScrollBar(lstCustomKeywords.Handle, SB_VERT, false);
                };

                // 滚轮滚动
                lstCustomKeywords.MouseWheel += (s, e) =>
                {
                    if (lstCustomKeywords.Items.Count == 0) return;

                    int delta = e.Delta / 120;
                    int scrollAmount = delta * 1;

                    if (lstCustomKeywords.TopItem != null)
                    {
                        int currentIndex = lstCustomKeywords.TopItem.Index;
                        int newIndex = currentIndex - scrollAmount;

                        if (lastKeywordItemIndex >= 0)
                        {
                            int maxScrollIndex = Math.Max(0, lastKeywordItemIndex);
                            newIndex = Math.Max(0, Math.Min(maxScrollIndex, newIndex));

                            if (newIndex >= lastKeywordItemIndex && delta < 0)
                            {
                                newIndex = Math.Max(0, lastKeywordItemIndex - 1);
                            }
                        }
                        else
                        {
                            newIndex = Math.Max(0, Math.Min(lstCustomKeywords.Items.Count - 1, newIndex));
                        }

                        if (newIndex != currentIndex && newIndex >= 0 && newIndex < lstCustomKeywords.Items.Count)
                        {
                            lstCustomKeywords.TopItem = lstCustomKeywords.Items[newIndex];
                        }
                    }

                    if (lstCustomKeywords.IsHandleCreated)
                    {
                        ShowScrollBar(lstCustomKeywords.Handle, 0, false);
                        ShowScrollBar(lstCustomKeywords.Handle, SB_VERT, false);
                    }
                };

                // 新增按钮
                Button btnAddKeyword = new NoFocusButton()
                {
                    Text = "新增",
                    Left = S(165),
                    Top = S(270),  // 与无码破解按钮对齐
                    Width = S(35),
                    Height = S(18),
                    Font = new Font("微软雅黑", 8),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnAddKeyword.FlatAppearance.BorderSize = 0;
                btnAddKeyword.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnAddKeyword.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnAddKeyword.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btn.Width, btn.Height), 4))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnAddKeyword.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(new Rectangle(0, 0, btn.Width, btn.Height), 4))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                    using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 3.5f))
                    {
                        using (Pen pen = new Pen(Color.Gray, 1))
                        {
                            g.DrawPath(pen, borderPath);
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                btnAddKeyword.Click += (s, e) =>
                {
                    string newKeyword = Microsoft.VisualBasic.Interaction.InputBox("请输入新关键词：", "新增关键词", "");
                    if (!string.IsNullOrWhiteSpace(newKeyword))
                    {
                        // 移除所有填充项
                        for (int i = lstCustomKeywords.Items.Count - 1; i >= 0; i--)
                        {
                            if (lstCustomKeywords.Items[i].Tag is string tagValue && tagValue == "FILLER")
                            {
                                lstCustomKeywords.Items.RemoveAt(i);
                            }
                        }

                        // 添加新关键词
                        ListViewItem newItem = new ListViewItem(newKeyword.Trim());
                        lstCustomKeywords.Items.Add(newItem);
                        lastKeywordItemIndex = lstCustomKeywords.Items.Count - 1;

                        // 重新添加填充项
                        for (int i = 0; i < keywordFillerCount; i++)
                        {
                            var fillerItem = new ListViewItem("");
                            fillerItem.Tag = "FILLER";
                            lstCustomKeywords.Items.Add(fillerItem);
                        }
                    }
                };
                groupBPanel.Controls.Add(btnAddKeyword);

                // 删除按钮
                Button btnDeleteKeyword = new NoFocusButton()
                {
                    Text = "删除",
                    Left = S(165),
                    Top = S(292),  // 在新增按钮下方
                    Width = S(35),
                    Height = S(18),
                    Font = new Font("微软雅黑", 8),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnDeleteKeyword.FlatAppearance.BorderSize = 0;
                btnDeleteKeyword.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnDeleteKeyword.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnDeleteKeyword.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btn.Width, btn.Height), 4))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnDeleteKeyword.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(new Rectangle(0, 0, btn.Width, btn.Height), 4))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                    using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 3.5f))
                    {
                        using (Pen pen = new Pen(Color.Gray, 1))
                        {
                            g.DrawPath(pen, borderPath);
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                btnDeleteKeyword.Click += (s, e) =>
                {
                    if (lstCustomKeywords.SelectedItems.Count > 0)
                    {
                        var selectedItem = lstCustomKeywords.SelectedItems[0];
                        if (selectedItem.Tag == null || (selectedItem.Tag is string && (string)selectedItem.Tag != "FILLER"))
                        {
                            lstCustomKeywords.Items.Remove(selectedItem);

                            // 更新lastKeywordItemIndex
                            lastKeywordItemIndex = -1;
                            for (int i = 0; i < lstCustomKeywords.Items.Count; i++)
                            {
                                if (lstCustomKeywords.Items[i].Tag == null ||
                                    (lstCustomKeywords.Items[i].Tag is string tagValue && tagValue != "FILLER"))
                                {
                                    lastKeywordItemIndex = i;
                                }
                            }
                        }
                    }
                };
                groupBPanel.Controls.Add(btnDeleteKeyword);

                // ==================== 右下：图片模式 模式区域 ====================
                Panel groupAPanel = new Panel()
                {
                    Left = S(683),
                    Top = S(310),
                    Width = S(300),
                    Height = S(320),
                    BackColor = Color.Transparent,
                    BorderStyle = BorderStyle.None
                };

                // 启用双缓冲
                typeof(Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, groupAPanel, new object[] { true });

                configForm.Controls.Add(groupAPanel);

                // 绘制 Group A 面板背景
                groupAPanel.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    int cornerRadius = S(10); // 圆角半径
                    Rectangle panelRect = new Rectangle(0, 0, groupAPanel.Width, groupAPanel.Height);

                    using (GraphicsPath roundedPath = CreateRoundedRectanglePath(panelRect, cornerRadius))
                    {
                        if (configForm.BackgroundImage != null)
                        {
                            // 如果有背景图片，先绘制背景图片的对应区域
                            Rectangle bgRect = configForm.RectangleToClient(
                                groupAPanel.RectangleToScreen(groupAPanel.ClientRectangle)
                            );

                            // 设置裁剪区域为圆角矩形
                            g.SetClip(roundedPath);
                            g.DrawImage(configForm.BackgroundImage,
                                groupAPanel.ClientRectangle,
                                bgRect,
                                GraphicsUnit.Pixel);
                            g.ResetClip();
                        }

                        // 绘制半透明白色背景（无论是否有背景图片都绘制）
                        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                            g.FillPath(bgBrush, roundedPath);
                    }
                };

                // 创建 ListView 用于显示图片收藏列表
                ListView imageFavListView = new ListView
                {
                    Left = S(0),
                    Top = S(0),
                    Width = groupAPanel.Width,
                    Height = groupAPanel.Height,
                    BackColor = Color.FromArgb(240, 240, 240),
                    ForeColor = Color.Black,
                    BorderStyle = BorderStyle.None,
                    View = View.Details,
                    OwnerDraw = true,
                    FullRowSelect = true,
                    HeaderStyle = ColumnHeaderStyle.None,
                    MultiSelect = false,
                    Scrollable = true,
                    AllowDrop = true
                };
                groupAPanel.Controls.Add(imageFavListView);
                
                // 启用双缓冲（温和方式，不干扰OwnerDraw）
                typeof(ListView).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, imageFavListView, new object[] { true });

                // 为 ListView 设置圆角 Region
                imageFavListView.HandleCreated += (s, e) =>
                {
                    int cornerRadius = S(10);
                    using (GraphicsPath roundedPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, imageFavListView.Width, imageFavListView.Height),
                        cornerRadius))
                    {
                        imageFavListView.Region = new Region(roundedPath);
                    }
                };

                // 创建刷新图片收藏列表的方法
                Action refreshImageFavoritesList = () =>
                {
                    imageFavListView.BeginUpdate();
                    try
                    {
                        imageFavListView.Items.Clear();

                        // 重新加载收藏列表，只筛选图片文件
                        List<string> allFavorites = favoritesManager.GetAllFavorites();
                        string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
                        List<string> imageFavorites = allFavorites
                            .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()))
                            .ToList();

                        // 按文件夹分组，每个文件夹只显示一个卡片
                        var folderGroups = imageFavorites
                            .GroupBy(f => Path.GetDirectoryName(f))
                            .Where(g => !string.IsNullOrEmpty(g.Key))
                            .ToList();

                        int itemHeight = S(70);

                        // 为每个文件夹创建一个卡片
                        foreach (var group in folderGroups)
                        {
                            ListViewItem item = new ListViewItem("");
                            // Tag 存储文件夹路径
                            item.Tag = group.Key;
                            imageFavListView.Items.Add(item);
                        }

                        // 添加填充Item
                        int listViewHeight = imageFavListView.ClientSize.Height;
                        // 根据屏幕分辨率动态调整填充倍数：4K屏需要更多填充项
                        int fillMultiplier = GetListViewFillMultiplier();
                        int fillerCount = (int)Math.Ceiling((double)listViewHeight / itemHeight) * fillMultiplier;

                        for (int i = 0; i < fillerCount; i++)
                        {
                            var fillerItem = new ListViewItem("");
                            fillerItem.Tag = "FILLER";
                            imageFavListView.Items.Add(fillerItem);
                        }
                    }
                    finally
                    {
                        imageFavListView.EndUpdate();
                    }
                };

                // 添加列
                imageFavListView.Columns.Add("", imageFavListView.ClientSize.Width);

                // 启用双缓冲
                typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(imageFavListView, new object[] {
                        ControlStyles.OptimizedDoubleBuffer |
                        ControlStyles.AllPaintingInWmPaint,
                        true
                    });

                // 隐藏列头
                imageFavListView.DrawColumnHeader += (s, e) => { };

                // 隐藏滚动条
                imageFavListView.HandleCreated += (s, e) =>
                {
                    ShowScrollBar(imageFavListView.Handle, 0, false);
                    ShowScrollBar(imageFavListView.Handle, SB_VERT, false);
                };

                bool isResizingImageFavList = false;

                imageFavListView.Resize += (s, e) =>
                {
                    if (isResizingImageFavList) return;
                    isResizingImageFavList = true;
                    try
                    {
                        if (imageFavListView.Columns.Count > 0 && imageFavListView.ClientSize.Width > 0)
                        {
                            imageFavListView.Columns[0].Width = imageFavListView.ClientSize.Width;
                        }
                        if (imageFavListView.IsHandleCreated)
                        {
                            ShowScrollBar(imageFavListView.Handle, 0, false);
                            ShowScrollBar(imageFavListView.Handle, SB_VERT, false);
                        }
                    }
                    finally
                    {
                        isResizingImageFavList = false;
                    }
                };

                imageFavListView.VisibleChanged += (s, e) =>
                {
                    if (imageFavListView.IsHandleCreated)
                    {
                        ShowScrollBar(imageFavListView.Handle, 0, false);
                        ShowScrollBar(imageFavListView.Handle, SB_VERT, false);
                    }
                };

                imageFavListView.ClientSizeChanged += (s, e) =>
                {
                    if (imageFavListView.IsHandleCreated)
                    {
                        ShowScrollBar(imageFavListView.Handle, 0, false);
                        ShowScrollBar(imageFavListView.Handle, SB_VERT, false);
                    }
                };

                // 鼠标滚轮事件
                imageFavListView.MouseWheel += (s, e) =>
                {
                    if (imageFavListView.Items.Count == 0) return;

                    int delta = e.Delta / 120;
                    int scrollAmount = delta * 3;

                    if (imageFavListView.TopItem != null)
                    {
                        int currentIndex = imageFavListView.TopItem.Index;
                        int newIndex = currentIndex - scrollAmount;

                        int actualItemCount = imageFavListView.Items.Cast<ListViewItem>()
                            .Count(item => item.Tag is string tag && tag != "FILLER");

                        int maxScrollIndex = Math.Max(0, actualItemCount - 1);
                        newIndex = Math.Max(0, Math.Min(maxScrollIndex, newIndex));

                        if (newIndex >= actualItemCount - 1 && delta < 0)
                        {
                            newIndex = Math.Max(0, actualItemCount - 2);
                        }

                        if (newIndex != currentIndex && newIndex < imageFavListView.Items.Count)
                        {
                            imageFavListView.TopItem = imageFavListView.Items[newIndex];
                        }
                    }

                    if (imageFavListView.IsHandleCreated)
                    {
                        ShowScrollBar(imageFavListView.Handle, 0, false);
                        ShowScrollBar(imageFavListView.Handle, SB_VERT, false);
                    }
                };

                // 初始加载
                refreshImageFavoritesList();

                // 拖放事件
                imageFavListView.DragEnter += (s, ee) =>
                {
                    if (ee.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                    {
                        ee.Effect = DragDropEffects.Copy;
                    }
                };

                imageFavListView.DragDrop += (s, ee) =>
                {
                    try
                    {
                        string[]? files = (string[]?)ee.Data?.GetData(DataFormats.FileDrop);
                        if (files == null || files.Length == 0) return;

                        string[] validExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
                        List<string> validImages = new List<string>();
                        foreach (string file in files)
                        {
                            string ext = Path.GetExtension(file).ToLower();
                            if (validExtensions.Contains(ext))
                            {
                                validImages.Add(file);
                            }
                        }

                        int addedCount = 0;
                        foreach (string image in validImages)
                        {
                            if (!favoritesManager.IsFavorite(image))
                            {
                                favoritesManager.AddFavorite(image);
                                addedCount++;
                            }
                        }

                        if (addedCount > 0)
                        {
                            favoritesManager.SaveToConfig();
                            refreshImageFavoritesList();
                            MessageBox.Show($"已添加 {addedCount} 个图片到收藏", "添加成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else if (validImages.Count > 0)
                        {
                            MessageBox.Show("所选图片已在收藏中", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("未找到有效的图片文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"添加收藏失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                // 设置行高
                int imageItemHeight = S(70);
                ImageList imageImgList = new ImageList();
                imageImgList.ImageSize = new Size(1, imageItemHeight);
                imageFavListView.SmallImageList = imageImgList;

                // 绘制每个图片收藏项
                imageFavListView.DrawItem += (s, e) =>
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // 绘制背景
                    if (configForm.BackgroundImage != null)
                    {
                        Rectangle listRect = configForm.RectangleToClient(imageFavListView.RectangleToScreen(e.Bounds));
                        g.DrawImage(configForm.BackgroundImage, e.Bounds, listRect, GraphicsUnit.Pixel);
                        using (SolidBrush mask = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                            g.FillRectangle(mask, e.Bounds);
                    }

                    // 填充Item
                    if (e.Item != null && e.Item.Tag is string && (string)e.Item.Tag == "FILLER")
                    {
                        return;
                    }

                    if (e.Item == null || e.Item.Index < 0 || e.Item.Index >= imageFavListView.Items.Count) return;

                    ListViewItem item = imageFavListView.Items[e.Item.Index];
                    string? folderPath = item.Tag as string;
                    if (string.IsNullOrEmpty(folderPath)) return;

                    // 获取文件夹名
                    string folderName = GetSmartFolderName(folderPath);

                    // 绘制圆角卡片
                    int horizontalMargin = S(15);
                    Rectangle cardRect = new Rectangle(
                        e.Bounds.X + horizontalMargin - 1,
                        e.Bounds.Y + S(5),
                        e.Bounds.Width - (horizontalMargin * 2),
                        e.Bounds.Height - S(10)
                    );

                    using (GraphicsPath path = new GraphicsPath())
                    {
                        int d = S(30);
                        path.AddArc(cardRect.X, cardRect.Y, d, d, 180, 90);
                        path.AddArc(cardRect.Right - d, cardRect.Y, d, d, 270, 90);
                        path.AddArc(cardRect.Right - d, cardRect.Bottom - d, d, d, 0, 90);
                        path.AddArc(cardRect.X, cardRect.Bottom - d, d, d, 90, 90);
                        path.CloseFigure();

                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                            g.FillPath(sb, path);

                        using (Pen p = new Pen(Color.FromArgb(180, Color.Gold), 1))
                            g.DrawPath(p, path);
                    }

                    // 绘制收藏按钮
                    int btnSize = S(30);
                    int btnMargin = S(10);
                    Rectangle favButtonRect = new Rectangle(
                        cardRect.Right - btnSize - btnMargin,
                        cardRect.Y + (cardRect.Height - btnSize) / 2,
                        btnSize,
                        btnSize
                    );

                    e.Item.SubItems.Clear();
                    e.Item.SubItems.Add(favButtonRect.ToString());

                    Image? favIcon = null;
                    string favIconPath = "config/fav.png";
                    if (File.Exists(favIconPath))
                    {
                        try
                        {
                            favIcon = Image.FromFile(favIconPath);
                        }
                        catch { }
                    }

                    if (favIcon != null)
                    {
                        g.DrawImage(favIcon, favButtonRect);
                        favIcon.Dispose();
                    }
                    else
                    {
                        float fontScale = 1.0f + (GetScale() - 1.0f) * 0.10f;
                        using (Font starFont = new Font("Segoe UI Symbol", 16 * fontScale, FontStyle.Regular))
                        {
                            TextRenderer.DrawText(g, "★", starFont, favButtonRect, Color.Gold,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        }
                    }

                    // 绘制文字（固定字体大小，严格2行显示）
                    float fSc = 1.0f + (GetScale() - 1.0f) * 0.10f;
                    int leftMargin = S(15);
                    int rightMargin = btnSize + btnMargin * 2;

                    // 文件夹名居中显示，固定字体大小，严格限制为2行
                    Rectangle textRect = new Rectangle(
                        cardRect.X + leftMargin,
                        cardRect.Y + S(8),
                        cardRect.Width - leftMargin - rightMargin,
                        cardRect.Height - S(16)
                    );

                    using (Font cardFont = new Font("微软雅黑", 11 * fSc, FontStyle.Bold))
                    {
                        // 使用StringFormat进行精确的文字绘制控制
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center;
                            sf.LineAlignment = StringAlignment.Center;
                            sf.FormatFlags = StringFormatFlags.LineLimit; // 限制行数
                            sf.Trimming = StringTrimming.EllipsisWord; // 单词级别的省略号
                            
                            using (SolidBrush textBrush = new SolidBrush(Color.HotPink))
                            {
                                g.DrawString(folderName, cardFont, textBrush, textRect, sf);
                            }
                        }
                    }
                };

                // 点击事件
                imageFavListView.MouseClick += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;

                    ListViewHitTestInfo hit = imageFavListView.HitTest(e.Location);
                    if (hit.Item == null) return;

                    string? folderPath = hit.Item.Tag as string;
                    if (string.IsNullOrEmpty(folderPath)) return;

                    // 检查是否点击收藏按钮
                    if (hit.Item.SubItems.Count > 1)
                    {
                        string rectStr = hit.Item.SubItems[1].Text;
                        if (!string.IsNullOrEmpty(rectStr))
                        {
                            try
                            {
                                string[] parts = rectStr.Replace("{X=", "").Replace("Y=", "").Replace("Width=", "")
                                    .Replace("Height=", "").Replace("}", "").Split(',');
                                if (parts.Length == 4)
                                {
                                    int btnX = int.Parse(parts[0]);
                                    int btnY = int.Parse(parts[1]);
                                    int btnW = int.Parse(parts[2]);
                                    int btnH = int.Parse(parts[3]);
                                    Rectangle btnRect = new Rectangle(btnX, btnY, btnW, btnH);

                                    if (btnRect.Contains(e.Location))
                                    {
                                        // 取消收藏：删除该文件夹下的所有图片
                                        List<string> allFavorites = favoritesManager.GetAllFavorites();
                                        string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
                                        
                                        // 使用路径规范化进行比较
                                        string normalizedFolderPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                        
                                        var imagesToRemove = allFavorites
                                            .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLower()))
                                            .Where(f => {
                                                string? dir = Path.GetDirectoryName(f);
                                                if (string.IsNullOrEmpty(dir)) return false;
                                                string normalizedDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                                return normalizedDir.Equals(normalizedFolderPath, StringComparison.OrdinalIgnoreCase);
                                            })
                                            .ToList();

                                        foreach (string image in imagesToRemove)
                                        {
                                            favoritesManager.RemoveFavorite(image);
                                        }
                                        favoritesManager.SaveToConfig();
                                        refreshImageFavoritesList();
                                        return;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    // 点击卡片其他区域：从该文件夹中随机选择一张图片打开
                    if (Directory.Exists(folderPath))
                    {
                        // 从文件夹中获取所有图片文件（不限于收藏列表）
                        string[] imageExtensions = { "*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp", "*.webp" };
                        List<string> folderImages = new List<string>();
                        
                        foreach (string ext in imageExtensions)
                        {
                            try
                            {
                                folderImages.AddRange(Directory.GetFiles(folderPath, ext, SearchOption.TopDirectoryOnly));
                            }
                            catch { }
                        }

                        if (folderImages.Count > 0)
                        {
                            // 随机选择一张图片（使用Guid确保真随机）
                            Random rnd = new Random(Guid.NewGuid().GetHashCode());
                            string selectedImage = folderImages[rnd.Next(folderImages.Count)];

                            string folderName = GetSmartFolderName(folderPath);
                            string fileName = Path.GetFileName(selectedImage)!;

                            this.currentVideoPath = selectedImage;
                            this.displayFolderName = folderName;
                            this.displayFileName = fileName;
                            this.UpdateLabelLayout();
                            this.lblFolderName.Invalidate();
                            this.lblFileName.Invalidate();

                            Process.Start(new ProcessStartInfo(selectedImage) { UseShellExecute = true });

                            // 使用 ImageHistoryManager 记录重温
                            imageHistoryManager.RecordView(folderName, fileName, 2, selectedImage);
                            RecordSelectedRootPathUsage(selectedImage);
                        }
                        else
                        {
                            string folderName = GetSmartFolderName(folderPath);
                            MessageBox.Show($"陛下，此文件夹中已无佳人！\n\n【{folderName}】\n\n或许已移居他处，或已香消玉殒...",
                                "佳人失踪", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        string folderName = GetSmartFolderName(folderPath);
                        MessageBox.Show($"陛下，此文件夹已不知所踪！\n\n【{folderName}】\n\n或许已移居他处...",
                            "文件夹失踪", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };

                // 鼠标移动时改变光标并显示完整文件夹名称
                ListViewItem? lastHoverImageFavItem = null;
                imageFavListView.MouseMove += (s, e) =>
                {
                    var hitTest = imageFavListView.HitTest(e.Location);

                    if (hitTest.Item != lastHoverImageFavItem)
                    {
                        lastHoverImageFavItem = hitTest.Item;

                        if (hitTest.Item != null)
                        {
                            if (hitTest.Item.Tag is string && (string)hitTest.Item.Tag == "FILLER")
                            {
                                imageFavListView.Cursor = Cursors.Default;
                                imageFavToolTip.Hide(imageFavListView);
                            }
                            else
                            {
                                imageFavListView.Cursor = Cursors.Hand;
                                
                                // 显示完整的文件夹名称
                                string? folderPath = hitTest.Item.Tag as string;
                                if (!string.IsNullOrEmpty(folderPath))
                                {
                                    string fullFolderName = GetSmartFolderName(folderPath);
                                    imageFavToolTip.Show(fullFolderName, imageFavListView, e.X + 10, e.Y - 20);
                                }
                            }
                        }
                        else
                        {
                            imageFavListView.Cursor = Cursors.Default;
                            imageFavToolTip.Hide(imageFavListView);
                        }
                    }
                };

                // 鼠标进入图片收藏区域时隐藏视频收藏的ToolTip
                imageFavListView.MouseEnter += (s, e) =>
                {
                    videoFavToolTip.Hide(configForm); // 隐藏视频收藏的ToolTip
                };

                // 鼠标离开时隐藏ToolTip
                imageFavListView.MouseLeave += (s, e) =>
                {
                    imageFavToolTip.Hide(imageFavListView);
                    lastHoverImageFavItem = null;
                };

                int groupAY = S(10);

                // 视频模式和文件夹模式按钮
                Button btnVideoMode = new NoFocusButton()
                {
                    Text = "视频模式",
                    Left = S(29),
                    Top = groupAY,
                    Width = S(130),
                    Height = S(50),
                    Font = new Font("微软雅黑", 11, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnVideoMode.FlatAppearance.BorderSize = 0;
                btnVideoMode.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnVideoMode.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnVideoMode.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnVideoMode.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    bool isSelected = btn.Tag as string == "selected";

                    // 绘制半透明白色背景
                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    if (isSelected)
                    {
                        // 选中状态：内层黑色细线
                        RectangleF innerBorderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath innerBorderPath = CreateRoundedRectanglePath(innerBorderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, innerBorderPath);
                            }
                        }

                        // 高亮半透明圆角边框
                        RectangleF highlightRect = new RectangleF(2f, 2f, btn.Width - 4, btn.Height - 4);
                        using (GraphicsPath highlightPath = CreateRoundedRectanglePath(highlightRect, 6.5f))
                        {
                            using (Pen highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 2))
                            {
                                g.DrawPath(highlightPen, highlightPath);
                            }
                        }

                        // 外层黑色细线
                        RectangleF outerBorderRect = new RectangleF(3.5f, 3.5f, btn.Width - 7, btn.Height - 7);
                        using (GraphicsPath outerBorderPath = CreateRoundedRectanglePath(outerBorderRect, 5.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, outerBorderPath);
                            }
                        }
                    }
                    else
                    {
                        // 未选中状态：黑色细线边框
                        RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, borderPath);
                            }
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                groupBPanel.Controls.Add(btnVideoMode);

                Button btnFolderMode = new NoFocusButton()
                {
                    Text = "文件夹模式",
                    Left = S(179),
                    Top = groupAY,
                    Width = S(130),
                    Height = S(50),
                    Font = new Font("微软雅黑", 11, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                btnFolderMode.FlatAppearance.BorderSize = 0;
                btnFolderMode.FlatAppearance.MouseOverBackColor = Color.Transparent;
                btnFolderMode.FlatAppearance.MouseDownBackColor = Color.Transparent;
                btnFolderMode.HandleCreated += (s, e) =>
                {
                    Button btn = (Button)s!;
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        btn.Region = new Region(path);
                    }
                };
                btnFolderMode.Paint += (s, pe) =>
                {
                    Button btn = (Button)s!;
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    bool isSelected = btn.Tag as string == "selected";

                    // 绘制半透明白色背景
                    using (GraphicsPath bgPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, btn.Width, btn.Height), 8))
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                        {
                            g.FillPath(brush, bgPath);
                        }
                    }

                    if (isSelected)
                    {
                        // 选中状态：内层黑色细线
                        RectangleF innerBorderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath innerBorderPath = CreateRoundedRectanglePath(innerBorderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, innerBorderPath);
                            }
                        }

                        // 高亮半透明圆角边框
                        RectangleF highlightRect = new RectangleF(2f, 2f, btn.Width - 4, btn.Height - 4);
                        using (GraphicsPath highlightPath = CreateRoundedRectanglePath(highlightRect, 6.5f))
                        {
                            using (Pen highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 2))
                            {
                                g.DrawPath(highlightPen, highlightPath);
                            }
                        }

                        // 外层黑色细线
                        RectangleF outerBorderRect = new RectangleF(3.5f, 3.5f, btn.Width - 7, btn.Height - 7);
                        using (GraphicsPath outerBorderPath = CreateRoundedRectanglePath(outerBorderRect, 5.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, outerBorderPath);
                            }
                        }
                    }
                    else
                    {
                        // 未选中状态：黑色细线边框
                        RectangleF borderRect = new RectangleF(0.5f, 0.5f, btn.Width - 1, btn.Height - 1);
                        using (GraphicsPath borderPath = CreateRoundedRectanglePath(borderRect, 7.5f))
                        {
                            using (Pen pen = new Pen(Color.Black, 1))
                            {
                                g.DrawPath(pen, borderPath);
                            }
                        }
                    }

                    TextRenderer.DrawText(g, btn.Text, btn.Font, btn.ClientRectangle,
                        btn.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
                groupBPanel.Controls.Add(btnFolderMode);
                groupAY += S(60);

                // 删除了模式说明Label
                // groupAY += S(45); - 不再需要这个间距
                // groupAY -= 10; - 不再需要这个调整

                // 排除最近 x 个看过的
                Label lblExcludeCount1_1 = new Label()
                {
                    Text = "排除最近",
                    Left = S(10),
                    Top = groupAY,
                    Width = S(60),
                    Height = S(25),
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                groupBPanel.Controls.Add(lblExcludeCount1_1);

                NumericUpDown numExcludeCount1 = new NumericUpDown()
                {
                    Left = S(70),
                    Top = groupAY,
                    Width = S(40),
                    Height = S(20),
                    Minimum = 0,
                    Maximum = 1000,
                    Value = 0
                };
                // 添加圆角
                numExcludeCount1.HandleCreated += (s, e) =>
                {
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, numExcludeCount1.Width, numExcludeCount1.Height), 4))
                    {
                        numExcludeCount1.Region = new Region(path);
                    }
                };
                groupBPanel.Controls.Add(numExcludeCount1);

                Label lblExcludeCount1_2 = new Label()
                {
                    Text = "个看过的",
                    Left = S(115),
                    Top = groupAY,
                    Width = S(70),
                    Height = S(25),
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                groupBPanel.Controls.Add(lblExcludeCount1_2);

                groupAY += S(30);

                // 排除最近 x 天看过的
                Label lblExcludeDays1_1 = new Label()
                {
                    Text = "排除最近",
                    Left = S(10),
                    Top = groupAY,
                    Width = S(60),
                    Height = S(25),
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                groupBPanel.Controls.Add(lblExcludeDays1_1);

                NumericUpDown numExcludeDays1 = new NumericUpDown()
                {
                    Left = S(70),
                    Top = groupAY,
                    Width = S(40),
                    Height = S(20),
                    Minimum = 0,
                    Maximum = 365,
                    Value = 0
                };
                // 添加圆角
                numExcludeDays1.HandleCreated += (s, e) =>
                {
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, numExcludeDays1.Width, numExcludeDays1.Height), 4))
                    {
                        numExcludeDays1.Region = new Region(path);
                    }
                };
                groupBPanel.Controls.Add(numExcludeDays1);

                Label lblExcludeDays1_2 = new Label()
                {
                    Text = "天看过的",
                    Left = S(115),
                    Top = groupAY,
                    Width = S(70),
                    Height = S(25),
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                groupBPanel.Controls.Add(lblExcludeDays1_2);

                groupAY += S(30);

                // 文件夹模式说明
                Label lblFolderModeNote1 = new Label()
                {
                    Text = "文件夹模式：",
                    Left = S(10),
                    Top = groupAY,
                    Width = S(200),
                    Height = S(20),
                    Font = new Font("微软雅黑", 9, FontStyle.Bold),
                    BackColor = Color.Transparent
                };
                groupBPanel.Controls.Add(lblFolderModeNote1);

                Label lblFolderModeNote2_1 = new Label()
                {
                    Text = "少于",
                    Left = S(10),
                    Top = groupAY + S(20),
                    Width = S(35),
                    Height = S(25),
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                groupBPanel.Controls.Add(lblFolderModeNote2_1);

                NumericUpDown numMinVideos = new NumericUpDown()
                {
                    Left = S(45),
                    Top = groupAY + S(20),
                    Width = S(40),
                    Height = S(20),
                    Minimum = 0,
                    Maximum = 100,
                    Value = 10
                };
                // 添加圆角
                numMinVideos.HandleCreated += (s, e) =>
                {
                    using (GraphicsPath path = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, numMinVideos.Width, numMinVideos.Height), 4))
                    {
                        numMinVideos.Region = new Region(path);
                    }
                };
                groupBPanel.Controls.Add(numMinVideos);

                Label lblFolderModeNote2_2 = new Label()
                {
                    Text = "个视频的文件夹",
                    Left = S(95),
                    Top = groupAY + S(20),
                    Width = S(120),
                    Height = S(25),
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                groupBPanel.Controls.Add(lblFolderModeNote2_2);

                Label lblFolderModeNote3 = new Label()
                {
                    Text = "不使用排除逻辑",
                    Left = S(10),
                    Top = groupAY + S(45),
                    Width = S(200),
                    Height = S(20),
                    BackColor = Color.Transparent
                };
                groupBPanel.Controls.Add(lblFolderModeNote3);

                // ==================== 右上：黑名单区域 ====================
                Panel blackListPanel = new Panel()
                {
                    Left = S(683),
                    Top = S(15),
                    Width = S(300),
                    Height = S(280),
                    BackColor = Color.Transparent,
                    BorderStyle = BorderStyle.None
                };

                // 启用双缓冲
                typeof(Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, blackListPanel, new object[] { true });

                configForm.Controls.Add(blackListPanel);

                // 绘制黑名单面板背景
                blackListPanel.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    int cornerRadius = S(10); // 圆角半径
                    Rectangle panelRect = new Rectangle(0, 0, blackListPanel.Width, blackListPanel.Height);

                    using (GraphicsPath roundedPath = CreateRoundedRectanglePath(panelRect, cornerRadius))
                    {
                        if (configForm.BackgroundImage != null)
                        {
                            // 如果有背景图片，先绘制背景图片的对应区域
                            Rectangle bgRect = configForm.RectangleToClient(
                                blackListPanel.RectangleToScreen(blackListPanel.ClientRectangle)
                            );

                            // 设置裁剪区域为圆角矩形
                            g.SetClip(roundedPath);
                            g.DrawImage(configForm.BackgroundImage,
                                blackListPanel.ClientRectangle,
                                bgRect,
                                GraphicsUnit.Pixel);
                            g.ResetClip();
                        }

                        // 绘制半透明白色背景（无论是否有背景图片都绘制）
                        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                            g.FillPath(bgBrush, roundedPath);
                    }
                };

                Label lblBlackList = new Label()
                {
                    Text = "黑名单（影响实际匹配）",
                    Left = S(10),
                    Top = S(10),
                    Width = S(280),
                    Height = S(25),
                    Font = new Font("微软雅黑", 10, FontStyle.Bold),
                    BackColor = Color.Transparent
                };
                blackListPanel.Controls.Add(lblBlackList);

                // 使用ListView替代TextBox实现透明效果（参考最爱视频的实现）
                ListView txtBlackList = new ListView()
                {
                    Left = S(10),
                    Top = S(40),
                    Width = S(280),
                    Height = S(190),
                    BackColor = Color.FromArgb(240, 240, 240), // 使用浅灰色背景作为后备
                    ForeColor = Color.Black,
                    BorderStyle = BorderStyle.FixedSingle, // 保留边框
                    View = View.Details,
                    OwnerDraw = true,
                    FullRowSelect = true,
                    HeaderStyle = ColumnHeaderStyle.None,
                    MultiSelect = false,
                    Scrollable = true,
                    LabelEdit = true // 允许编辑
                };
                blackListPanel.Controls.Add(txtBlackList);

                // 添加一个占满宽度的列
                txtBlackList.Columns.Add("", txtBlackList.ClientSize.Width);

                // 启用双缓冲（温和方式，不干扰OwnerDraw）
                typeof(ListView).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, txtBlackList, new object[] { true });

                // 隐藏列头
                txtBlackList.DrawColumnHeader += (s, e) => { };

                // 防止Resize递归的标志
                bool isResizingBlackList = false;

                // 动态调整列宽度以填满整个ListView
                txtBlackList.Resize += (s, e) =>
                {
                    if (isResizingBlackList) return;

                    isResizingBlackList = true;
                    try
                    {
                        if (txtBlackList.Columns.Count > 0 && txtBlackList.ClientSize.Width > 0)
                        {
                            txtBlackList.Columns[0].Width = txtBlackList.ClientSize.Width;
                        }

                        if (txtBlackList.IsHandleCreated)
                        {
                            ShowScrollBar(txtBlackList.Handle, 0, false);
                            ShowScrollBar(txtBlackList.Handle, SB_VERT, false);
                        }
                    }
                    finally
                    {
                        isResizingBlackList = false;
                    }
                };

                // 添加黑名单项并记录最后一个真实Item的索引
                int lastBlackItemIndex = -1;
                foreach (string item in blackWhiteListManager.BlackList)
                {
                    ListViewItem lvi = new ListViewItem(item);
                    txtBlackList.Items.Add(lvi);
                    lastBlackItemIndex = txtBlackList.Items.Count - 1;
                }

                // 【关键】添加填充Item填满ListView高度（增加额外的填充项以应对滚动）
                int blackListHeight = txtBlackList.ClientSize.Height;
                int blackListItemHeight = txtBlackList.Font.Height + 4;
                // 根据屏幕分辨率动态调整填充倍数：4K屏需要更多填充项
                int blackListFillMultiplier = GetListViewFillMultiplier();
                int blackListFillerCount = (int)Math.Ceiling((double)blackListHeight / blackListItemHeight) * blackListFillMultiplier;

                for (int i = 0; i < blackListFillerCount; i++)
                {
                    var fillerItem = new ListViewItem("");
                    fillerItem.Tag = "FILLER";
                    txtBlackList.Items.Add(fillerItem);
                }

                // 绘制每个项（实现透明效果 + 高亮选中项）
                txtBlackList.DrawItem += (s, e) =>
                {
                    if (e.Item == null || e.Item.Index < 0 || e.Item.Index >= txtBlackList.Items.Count) return;
                    
                    Graphics g = e.Graphics;

                    // 先绘制背景图片或默认背景
                    if (configForm.BackgroundImage != null)
                    {
                        try
                        {
                            Rectangle listRect = configForm.RectangleToClient(txtBlackList.RectangleToScreen(e.Bounds));
                            g.DrawImage(configForm.BackgroundImage,
                                e.Bounds,
                                listRect,
                                GraphicsUnit.Pixel);
                        }
                        catch
                        {
                            // 如果绘制背景失败，使用默认背景
                            using (SolidBrush defaultBg = new SolidBrush(Color.FromArgb(240, 240, 240)))
                                g.FillRectangle(defaultBg, e.Bounds);
                        }
                    }
                    else
                    {
                        // 没有背景图片时使用浅灰色背景
                        using (SolidBrush defaultBg = new SolidBrush(Color.FromArgb(240, 240, 240)))
                            g.FillRectangle(defaultBg, e.Bounds);
                    }

                    // 绘制白色半透明遮罩
                    using (SolidBrush mask = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                        g.FillRectangle(mask, e.Bounds);

                    // 【填充Item】只绘制背景，不绘制内容
                    if (e.Item != null && e.Item.Tag is string && (string)e.Item.Tag == "FILLER")
                    {
                        return; // 只绘制背景，不绘制其他内容
                    }

                    // 如果是选中项，绘制高亮背景
                    if (e.Item != null && e.Item.Selected)
                    {
                        using (SolidBrush highlightBrush = new SolidBrush(Color.FromArgb(100, 135, 206, 250))) // 浅蓝色半透明
                            g.FillRectangle(highlightBrush, e.Bounds);
                    }

                    // 绘制文字
                    if (e.Item != null && e.Item.Index >= 0 && !string.IsNullOrEmpty(e.Item.Text))
                    {
                        TextRenderer.DrawText(g, e.Item.Text, txtBlackList.Font, e.Bounds, Color.Black,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    }
                };

                // 添加双击编辑功能
                txtBlackList.MouseDoubleClick += (s, e) =>
                {
                    if (txtBlackList.SelectedItems.Count > 0)
                    {
                        var selectedItem = txtBlackList.SelectedItems[0];
                        // 不编辑FILLER项
                        if (selectedItem.Tag == null || (selectedItem.Tag is string && (string)selectedItem.Tag != "FILLER"))
                        {
                            selectedItem.BeginEdit();
                        }
                    }
                };

                // 添加编辑完成后的保存逻辑
                txtBlackList.AfterLabelEdit += (s, e) =>
                {
                    if (e.Label != null && !string.IsNullOrWhiteSpace(e.Label))
                    {
                        // 更新项的文本（通过索引访问）
                        txtBlackList.Items[e.Item].Text = e.Label.Trim();

                        // 立即保存到文件
                        try
                        {
                            List<string> updatedBlackList = txtBlackList.Items.Cast<ListViewItem>()
                                .Where(x => x.Tag == null || (x.Tag is string && (string)x.Tag != "FILLER"))
                                .Select(x => x.Text)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(x => x.Trim())
                                .ToList();

                            blackWhiteListManager.SaveBlackList(updatedBlackList);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("保存黑名单失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else if (e.Label != null && string.IsNullOrWhiteSpace(e.Label))
                    {
                        // 如果编辑后为空，取消编辑
                        e.CancelEdit = true;
                    }
                };

                // 隐藏黑名单输入框的滚动条
                txtBlackList.HandleCreated += (s, e) =>
                {
                    ShowScrollBar(txtBlackList.Handle, 0, false);
                    ShowScrollBar(txtBlackList.Handle, SB_VERT, false);
                };

                txtBlackList.VisibleChanged += (s, e) =>
                {
                    if (txtBlackList.IsHandleCreated)
                    {
                        ShowScrollBar(txtBlackList.Handle, 0, false);
                        ShowScrollBar(txtBlackList.Handle, SB_VERT, false);
                    }
                };

                txtBlackList.ClientSizeChanged += (s, e) =>
                {
                    if (txtBlackList.IsHandleCreated)
                    {
                        ShowScrollBar(txtBlackList.Handle, 0, false);
                        ShowScrollBar(txtBlackList.Handle, SB_VERT, false);
                    }
                };

                // 保留滚轮滚动功能
                txtBlackList.MouseWheel += (s, e) =>
                {
                    if (txtBlackList.Items.Count == 0) return;

                    int delta = e.Delta / 120;
                    int scrollAmount = delta * 1; // 每次滚动1个Item

                    if (txtBlackList.TopItem != null)
                    {
                        int currentIndex = txtBlackList.TopItem.Index;
                        int newIndex = currentIndex - scrollAmount;

                        // 如果有真实Item，限制滚动范围
                        if (lastBlackItemIndex >= 0)
                        {
                            // 计算最大可滚动的索引
                            int maxScrollIndex = Math.Max(0, lastBlackItemIndex);
                            newIndex = Math.Max(0, Math.Min(maxScrollIndex, newIndex));

                            // 【限制滚动】如果滚动到最底部，回滚一个item
                            if (newIndex >= lastBlackItemIndex && delta < 0) // delta < 0 表示向下滚动
                            {
                                newIndex = Math.Max(0, lastBlackItemIndex - 1);
                            }
                        }
                        else
                        {
                            newIndex = Math.Max(0, Math.Min(txtBlackList.Items.Count - 1, newIndex));
                        }

                        if (newIndex != currentIndex && newIndex >= 0 && newIndex < txtBlackList.Items.Count)
                        {
                            txtBlackList.TopItem = txtBlackList.Items[newIndex];
                        }
                    }

                    if (txtBlackList.IsHandleCreated)
                    {
                        ShowScrollBar(txtBlackList.Handle, 0, false);
                        ShowScrollBar(txtBlackList.Handle, SB_VERT, false);
                    }
                };

                // 添加"新增"按钮
                Button btnAddBlack = new Button()
                {
                    Text = "新增",
                    Left = S(10),
                    Top = S(240),
                    Width = S(60),
                    Height = S(25),
                    BackColor = Color.FromArgb(200, 255, 255, 255),
                    ForeColor = Color.Black,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnAddBlack.FlatAppearance.BorderSize = 0;
                // 添加圆角
                using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btnAddBlack.Width, btnAddBlack.Height), 5))
                {
                    btnAddBlack.Region = new Region(path);
                }
                // 绘制圆角描边
                btnAddBlack.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btnAddBlack.Width - 1, btnAddBlack.Height - 1), 5))
                    {
                        using (Pen pen = new Pen(Color.Gray, 1))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                };
                blackListPanel.Controls.Add(btnAddBlack);

                btnAddBlack.Click += (s, e) =>
                {
                    // 弹出输入框
                    string input = Microsoft.VisualBasic.Interaction.InputBox("请输入要添加的黑名单关键词：", "添加黑名单", "");
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        // 移除所有FILLER项
                        for (int i = txtBlackList.Items.Count - 1; i >= 0; i--)
                        {
                            if (txtBlackList.Items[i].Tag is string && (string)txtBlackList.Items[i].Tag! == "FILLER")
                            {
                                txtBlackList.Items.RemoveAt(i);
                            }
                        }

                        // 添加新项
                        ListViewItem newItem = new ListViewItem(input.Trim());
                        txtBlackList.Items.Add(newItem);

                        // 重新添加FILLER项
                        int blackListHeight = txtBlackList.ClientSize.Height;
                        int blackListItemHeight = txtBlackList.Font.Height + 4;
                        int blackListFillerCount = (int)Math.Ceiling((double)blackListHeight / blackListItemHeight);

                        for (int i = 0; i < blackListFillerCount; i++)
                        {
                            var fillerItem = new ListViewItem("");
                            fillerItem.Tag = "FILLER";
                            txtBlackList.Items.Add(fillerItem);
                        }

                        txtBlackList.Refresh();

                        // 立即保存到文件
                        try
                        {
                            List<string> updatedBlackList = txtBlackList.Items.Cast<ListViewItem>()
                                .Where(x => x.Tag == null || (x.Tag is string && (string)x.Tag != "FILLER"))
                                .Select(x => x.Text)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(x => x.Trim())
                                .ToList();

                            blackWhiteListManager.SaveBlackList(updatedBlackList);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("保存黑名单失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                };

                // 添加"删除"按钮
                Button btnDelBlack = new Button()
                {
                    Text = "删除",
                    Left = S(75),
                    Top = S(240),
                    Width = S(60),
                    Height = S(25),
                    BackColor = Color.FromArgb(200, 255, 255, 255),
                    ForeColor = Color.Black,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnDelBlack.FlatAppearance.BorderSize = 0;
                // 添加圆角
                using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btnDelBlack.Width, btnDelBlack.Height), 5))
                {
                    btnDelBlack.Region = new Region(path);
                }
                // 绘制圆角描边
                btnDelBlack.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btnDelBlack.Width - 1, btnDelBlack.Height - 1), 5))
                    {
                        using (Pen pen = new Pen(Color.Gray, 1))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                };
                blackListPanel.Controls.Add(btnDelBlack);

                btnDelBlack.Click += (s, e) =>
                {
                    if (txtBlackList.SelectedItems.Count > 0)
                    {
                        var selectedItem = txtBlackList.SelectedItems[0];
                        // 不删除FILLER项
                        if (selectedItem.Tag == null || (selectedItem.Tag is string && (string)selectedItem.Tag != "FILLER"))
                        {
                            txtBlackList.Items.Remove(selectedItem);
                            txtBlackList.Refresh();

                            // 立即保存到文件
                            try
                            {
                                List<string> updatedBlackList = txtBlackList.Items.Cast<ListViewItem>()
                                    .Where(x => x.Tag == null || (x.Tag is string && (string)x.Tag != "FILLER"))
                                    .Select(x => x.Text)
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Select(x => x.Trim())
                                    .ToList();

                                blackWhiteListManager.SaveBlackList(updatedBlackList);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("保存黑名单失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                };

                // ==================== 中下：查看最爱列表区域（完全按照历史记录风格）====================
                Panel favoritesListPanel = new Panel()
                {
                    Left = S(368),
                    Top = S(310),
                    Width = S(300),
                    Height = S(320),
                    BackColor = Color.Transparent,
                    BorderStyle = BorderStyle.None
                };

                // 启用双缓冲
                typeof(Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, favoritesListPanel, new object[] { true });

                configForm.Controls.Add(favoritesListPanel);

                // 绘制查看最爱面板背景（绘制背景图片和遮罩）
                favoritesListPanel.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    int cornerRadius = S(10); // 圆角半径
                    Rectangle panelRect = new Rectangle(0, 0, favoritesListPanel.Width, favoritesListPanel.Height);

                    using (GraphicsPath roundedPath = CreateRoundedRectanglePath(panelRect, cornerRadius))
                    {
                        if (configForm.BackgroundImage != null)
                        {
                            // 绘制背景图片的对应区域
                            Rectangle bgRect = configForm.RectangleToClient(
                                favoritesListPanel.RectangleToScreen(favoritesListPanel.ClientRectangle)
                            );

                            // 设置裁剪区域为圆角
                            g.SetClip(roundedPath);
                            g.DrawImage(configForm.BackgroundImage,
                                favoritesListPanel.ClientRectangle,
                                bgRect,
                                GraphicsUnit.Pixel);
                            g.ResetClip();
                        }

                        // 绘制半透明白色背景
                        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                            g.FillPath(bgBrush, roundedPath);
                    }
                };

                // 设置面板圆角区域（用于裁剪ListView）
                favoritesListPanel.HandleCreated += (s, e) =>
                {
                    // 不设置 Region，避免 Windows 绘制系统边框
                    // ListView 会通过 OwnerDraw 自己绘制圆角背景
                };

                // 为 ListView 设置圆角
                ListView? favListView = null;


                // 创建 ListView 用于显示收藏列表（历史记录风格）
                favListView = new ListView
                {
                    Left = S(0),  // 填满整个面板
                    Top = S(0),
                    Width = favoritesListPanel.Width,
                    Height = favoritesListPanel.Height,
                    BackColor = Color.FromArgb(240, 240, 240), // 使用浅灰色背景，接近半透明白色的视觉效果
                    ForeColor = Color.Black,
                    BorderStyle = BorderStyle.None, // 去掉边框
                    View = View.Details,
                    OwnerDraw = true,
                    FullRowSelect = true,
                    HeaderStyle = ColumnHeaderStyle.None,
                    MultiSelect = false,
                    Scrollable = true,
                    AllowDrop = true // 启用拖放功能
                };
                favoritesListPanel.Controls.Add(favListView);
                
                // 启用双缓冲（温和方式，不干扰OwnerDraw）
                typeof(ListView).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, favListView, new object[] { true });

                // 为 ListView 设置圆角 Region
                favListView.HandleCreated += (s, e) =>
                {
                    int cornerRadius = S(10);
                    using (GraphicsPath roundedPath = CreateRoundedRectanglePath(
                        new Rectangle(0, 0, favListView.Width, favListView.Height),
                        cornerRadius))
                    {
                        favListView.Region = new Region(roundedPath);
                    }
                };

                // 创建刷新收藏列表的方法（使用局部变量捕获）
                Action refreshFavoritesList = () =>
                {
                    favListView.BeginUpdate(); // 开始批量更新，提高性能
                    try
                    {
                        favListView.Items.Clear();

                        // 重新加载收藏列表，只筛选视频文件
                        List<string> allFavorites = favoritesManager.GetAllFavorites();
                        string[] videoExtensions = { ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".flv", ".rmvb" };
                        List<string> videoFavorites = allFavorites
                            .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLower()))
                            .ToList();

                        // 计算卡片高度
                        int itemHeight = S(70);

                        // 添加收藏项
                        foreach (string favorite in videoFavorites)
                        {
                            ListViewItem item = new ListViewItem("");
                            item.Tag = favorite;
                            favListView.Items.Add(item);
                        }

                        // 添加填充Item填满ListView高度
                        int listViewHeight = favListView.ClientSize.Height;
                        // 根据屏幕分辨率动态调整填充倍数：4K屏需要更多填充项
                        int fillMultiplier = GetListViewFillMultiplier();
                        int fillerCount = (int)Math.Ceiling((double)listViewHeight / itemHeight) * fillMultiplier;

                        for (int i = 0; i < fillerCount; i++)
                        {
                            var fillerItem = new ListViewItem("");
                            fillerItem.Tag = "FILLER";
                            favListView.Items.Add(fillerItem);
                        }
                    }
                    finally
                    {
                        favListView.EndUpdate(); // 结束批量更新
                    }
                };

                // 添加一个占满宽度的列
                favListView.Columns.Add("", favListView.ClientSize.Width);

                // 启用双缓冲
                typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(favListView, new object[] {
                        ControlStyles.OptimizedDoubleBuffer |
                        ControlStyles.AllPaintingInWmPaint,
                        true
                    });

                // 隐藏列头
                favListView.DrawColumnHeader += (s, e) => { };

                // 【关键】隐藏滚动条（参考历史记录窗口的实现）
                favListView.HandleCreated += (s, e) =>
                {
                    ShowScrollBar(favListView.Handle, 0, false);
                    ShowScrollBar(favListView.Handle, SB_VERT, false);
                };

                // 防止Resize递归的标志
                bool isResizingFavList = false;

                // 在多个事件中强制隐藏滚动条，并动态调整列宽度
                favListView.Resize += (s, e) =>
                {
                    if (isResizingFavList) return;

                    isResizingFavList = true;
                    try
                    {
                        if (favListView.Columns.Count > 0 && favListView.ClientSize.Width > 0)
                        {
                            favListView.Columns[0].Width = favListView.ClientSize.Width;
                        }

                        if (favListView.IsHandleCreated)
                        {
                            ShowScrollBar(favListView.Handle, 0, false);
                            ShowScrollBar(favListView.Handle, SB_VERT, false);
                        }
                    }
                    finally
                    {
                        isResizingFavList = false;
                    }
                };

                favListView.VisibleChanged += (s, e) =>
                {
                    if (favListView.IsHandleCreated)
                    {
                        ShowScrollBar(favListView.Handle, 0, false);
                        ShowScrollBar(favListView.Handle, SB_VERT, false);
                    }
                };

                favListView.ClientSizeChanged += (s, e) =>
                {
                    if (favListView.IsHandleCreated)
                    {
                        ShowScrollBar(favListView.Handle, 0, false);
                        ShowScrollBar(favListView.Handle, SB_VERT, false);
                    }
                };

                // 【关键】添加鼠标滚轮事件，每次滚动3个Item
                favListView.MouseWheel += (s, e) =>
                {
                    if (favListView.Items.Count == 0) return;

                    // 计算滚动方向和距离
                    int delta = e.Delta / 120; // 每次滚动的单位
                    int scrollAmount = delta * 3; // 每次滚动3个Item

                    if (favListView.TopItem != null)
                    {
                        int currentIndex = favListView.TopItem.Index;
                        int newIndex = currentIndex - scrollAmount; // 向上滚是正数，向下滚是负数

                        // 获取实际的收藏项数量（排除FILLER项）
                        int actualItemCount = favListView.Items.Cast<ListViewItem>()
                            .Count(item => item.Tag is string tag && tag != "FILLER");

                        // 计算可见区域能显示的item数量
                        int itemHeight = S(70);
                        int visibleItemCount = favListView.ClientSize.Height / itemHeight;

                        // 计算最大可滚动的索引（确保至少有一个item可见）
                        int maxScrollIndex = Math.Max(0, actualItemCount - 1);

                        // 限制滚动范围
                        newIndex = Math.Max(0, Math.Min(maxScrollIndex, newIndex));

                        // 【强制回滚逻辑】如果滚动到最底部（最后一个实际item），自动回滚一个item
                        if (newIndex >= actualItemCount - 1 && delta < 0) // delta < 0 表示向下滚动
                        {
                            newIndex = Math.Max(0, actualItemCount - 2); // 回滚到倒数第二个item
                        }

                        if (newIndex != currentIndex && newIndex < favListView.Items.Count)
                        {
                            favListView.TopItem = favListView.Items[newIndex];
                        }
                    }

                    // 滚动后强制隐藏滚动条
                    if (favListView.IsHandleCreated)
                    {
                        ShowScrollBar(favListView.Handle, 0, false);
                        ShowScrollBar(favListView.Handle, SB_VERT, false);
                    }
                };

                // 初始加载收藏列表
                refreshFavoritesList();

                // 添加拖放事件到favListView（替代原来的btnDragDropFavorites）
                favListView.DragEnter += (s, ee) =>
                {
                    if (ee.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                    {
                        ee.Effect = DragDropEffects.Copy;
                    }
                };

                favListView.DragDrop += (s, ee) =>
                {
                    try
                    {
                        string[]? files = (string[]?)ee.Data?.GetData(DataFormats.FileDrop);
                        if (files == null || files.Length == 0) return;

                        string[] validExtensions = { ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".flv", ".rmvb" };
                        List<string> validVideos = new List<string>();
                        foreach (string file in files)
                        {
                            string ext = Path.GetExtension(file).ToLower();
                            if (validExtensions.Contains(ext))
                            {
                                validVideos.Add(file);
                            }
                        }

                        int addedCount = 0;
                        foreach (string video in validVideos)
                        {
                            if (!favoritesManager.IsFavorite(video))
                            {
                                favoritesManager.AddFavorite(video);
                                addedCount++;
                            }
                        }

                        if (addedCount > 0)
                        {
                            favoritesManager.SaveToConfig();

                            // 实时刷新收藏列表显示
                            refreshFavoritesList();

                            MessageBox.Show($"已添加 {addedCount} 个视频到收藏", "添加成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else if (validVideos.Count > 0)
                        {
                            MessageBox.Show("所选视频已在收藏中", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("未找到有效的视频文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"添加收藏失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                // 设置行高 - 移除256的限制，允许更大的行高
                int itemHeight = S(70);
                ImageList imgList = new ImageList();
                imgList.ImageSize = new Size(1, itemHeight);
                favListView.SmallImageList = imgList;

                // 绘制每个收藏项（历史记录卡片风格）
                favListView.DrawItem += (s, e) =>
                {
                    Graphics g = e.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // 先绘制背景图片（无论是否有卡片都绘制）
                    if (configForm.BackgroundImage != null)
                    {
                        Rectangle listRect = configForm.RectangleToClient(favListView.RectangleToScreen(e.Bounds));
                        g.DrawImage(configForm.BackgroundImage,
                            e.Bounds,
                            listRect,
                            GraphicsUnit.Pixel);

                        // 绘制白色半透明遮罩（最爱视频下面的遮罩）
                        using (SolidBrush mask = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                            g.FillRectangle(mask, e.Bounds);
                    }

                    // 【填充Item】只绘制背景，不绘制内容
                    if (e.Item != null && e.Item.Tag is string && (string)e.Item.Tag == "FILLER")
                    {
                        return; // 只绘制背景，不绘制其他内容
                    }

                    // 如果没有 Item 或 Item 无效，只绘制背景，不绘制卡片
                    if (e.Item == null || e.Item.Index < 0 || e.Item.Index >= favListView.Items.Count) return;

                    ListViewItem item = favListView.Items[e.Item.Index];
                    string? favPath = item.Tag as string;
                    if (string.IsNullOrEmpty(favPath)) return;

                    string? dirPath = Path.GetDirectoryName(favPath);
                    string folderName = GetSmartFolderName(dirPath);
                    string fileName = Path.GetFileName(favPath);

                    // 绘制圆角卡片
                    int horizontalMargin = S(15);
                    Rectangle cardRect = new Rectangle(
                        e.Bounds.X + horizontalMargin - 1,  // 往左移动1像素
                        e.Bounds.Y + S(5),
                        e.Bounds.Width - (horizontalMargin * 2),
                        e.Bounds.Height - S(10)
                    );

                    using (GraphicsPath path = new GraphicsPath())
                    {
                        int d = S(30);
                        path.AddArc(cardRect.X, cardRect.Y, d, d, 180, 90);
                        path.AddArc(cardRect.Right - d, cardRect.Y, d, d, 270, 90);
                        path.AddArc(cardRect.Right - d, cardRect.Bottom - d, d, d, 0, 90);
                        path.AddArc(cardRect.X, cardRect.Bottom - d, d, d, 90, 90);
                        path.CloseFigure();

                        // 半透明黑色背景
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                            g.FillPath(sb, path);

                        // 金色边框（收藏专用颜色）
                        using (Pen p = new Pen(Color.FromArgb(180, Color.Gold), 1))
                            g.DrawPath(p, path);
                    }

                    // 绘制收藏按钮（右侧）
                    int btnSize = S(30);
                    int btnMargin = S(10);
                    Rectangle favButtonRect = new Rectangle(
                        cardRect.Right - btnSize - btnMargin,
                        cardRect.Y + (cardRect.Height - btnSize) / 2,
                        btnSize,
                        btnSize
                    );

                    // 存储按钮位置到 SubItems 中，用于点击检测
                    e.Item.SubItems.Clear();
                    e.Item.SubItems.Add(favButtonRect.ToString());

                    // 绘制收藏图标（加载 config/fav.png）
                    Image? favIcon = null;
                    string favIconPath = "config/fav.png";
                    if (File.Exists(favIconPath))
                    {
                        try
                        {
                            favIcon = Image.FromFile(favIconPath);
                        }
                        catch { }
                    }

                    if (favIcon != null)
                    {
                        g.DrawImage(favIcon, favButtonRect);
                        favIcon.Dispose();
                    }
                    else
                    {
                        // 默认金色星星
                        float fontScale = 1.0f + (GetScale() - 1.0f) * 0.10f;
                        using (Font starFont = new Font("Segoe UI Symbol", 16 * fontScale, FontStyle.Regular))
                        {
                            TextRenderer.DrawText(g, "★", starFont, favButtonRect, Color.Gold,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        }
                    }

                    // 绘制文字（调整右边距以避免与按钮重叠）
                    float fSc = 1.0f + (GetScale() - 1.0f) * 0.10f;
                    using (Font cardFont = new Font("微软雅黑", 11 * fSc, FontStyle.Bold))
                    {
                        int leftMargin = S(25);
                        int rightMargin = btnSize + btnMargin * 2;

                        // 第一行：文件夹名（粉色）
                        Rectangle rect1 = new Rectangle(
                            cardRect.X + leftMargin,
                            cardRect.Y + S(12),
                            cardRect.Width - leftMargin - rightMargin,
                            cardRect.Height / 2 - S(2)
                        );
                        TextRenderer.DrawText(g, folderName, cardFont, rect1, Color.HotPink,
                            TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                        // 第二行：文件名（白色）- 增加底部边距以确保小写字母完整显示
                        Rectangle rect2 = new Rectangle(
                            cardRect.X + leftMargin,
                            cardRect.Y + cardRect.Height / 2,
                            cardRect.Width - leftMargin - rightMargin,
                            cardRect.Height / 2 - S(8)  // 增加底部边距
                        );
                        TextRenderer.DrawText(g, fileName, cardFont, rect2, Color.White,
                            TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    }
                };

                // 点击事件处理
                favListView.MouseClick += (s, e) =>
                {
                    // 只处理左键单击
                    if (e.Button != MouseButtons.Left) return;

                    ListViewHitTestInfo hit = favListView.HitTest(e.Location);
                    if (hit.Item == null) return;

                    string? favPath = hit.Item.Tag as string;
                    if (string.IsNullOrEmpty(favPath)) return;

                    // 检查是否点击了收藏按钮
                    if (hit.Item.SubItems.Count > 1)
                    {
                        string rectStr = hit.Item.SubItems[1].Text;
                        if (!string.IsNullOrEmpty(rectStr))
                        {
                            try
                            {
                                string[] parts = rectStr.Replace("{X=", "").Replace("Y=", "").Replace("Width=", "")
                                    .Replace("Height=", "").Replace("}", "").Split(',');
                                if (parts.Length == 4)
                                {
                                    int btnX = int.Parse(parts[0]);
                                    int btnY = int.Parse(parts[1]);
                                    int btnW = int.Parse(parts[2]);
                                    int btnH = int.Parse(parts[3]);
                                    Rectangle btnRect = new Rectangle(btnX, btnY, btnW, btnH);

                                    if (btnRect.Contains(e.Location))
                                    {
                                        // 点击收藏按钮 = 取消收藏
                                        favoritesManager.RemoveFavorite(favPath);
                                        favoritesManager.SaveToConfig();
                                        refreshFavoritesList();
                                        return;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    // 点击卡片其他区域：播放视频并记录重温行为
                    if (File.Exists(favPath))
                    {
                        // 获取文件夹名和文件名
                        string? dirPath = Path.GetDirectoryName(favPath);
                        string folderName = GetSmartFolderName(dirPath);
                        string fileName = Path.GetFileName(favPath)!;

                        // 更新主界面显示
                        this.currentVideoPath = favPath;
                        this.displayFolderName = folderName;
                        this.displayFileName = fileName;
                        this.UpdateLabelLayout();
                        this.lblFolderName.Invalidate();
                        this.lblFileName.Invalidate();

                        // 播放视频
                        Process.Start(new ProcessStartInfo(favPath) { UseShellExecute = true });

                        // 记录重温行为（type = 2）
                        RecordHistory(folderName, fileName, 2);
                    }
                    else
                    {
                        // 文件不存在时的提示
                        string? dirPath = Path.GetDirectoryName(favPath);
                        string folderName = GetSmartFolderName(dirPath);
                        string fileName = Path.GetFileName(favPath)!;
                        MessageBox.Show($"陛下，此佳人已不知所踪！\n\n【{folderName}】\n{fileName}\n\n或许已移居他处，或已香消玉殒...",
                            "佳人失踪", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };

                // 鼠标移动时改变光标并显示完整文件信息
                ListViewItem? lastHoverFavItem = null;
                favListView.MouseMove += (s, e) =>
                {
                    var hitTest = favListView.HitTest(e.Location);

                    if (hitTest.Item != lastHoverFavItem)
                    {
                        lastHoverFavItem = hitTest.Item;

                        if (hitTest.Item != null)
                        {
                            // 如果是填充Item，显示默认光标
                            if (hitTest.Item.Tag is string && (string)hitTest.Item.Tag == "FILLER")
                            {
                                favListView.Cursor = Cursors.Default;
                                videoFavToolTip.Hide(favListView);
                            }
                            else
                            {
                                favListView.Cursor = Cursors.Hand;
                                
                                // 显示完整的文件信息
                                string? favPath = hitTest.Item.Tag as string;
                                if (!string.IsNullOrEmpty(favPath))
                                {
                                    string? dirPath = Path.GetDirectoryName(favPath);
                                    string folderName = GetSmartFolderName(dirPath);
                                    string fileName = Path.GetFileName(favPath);
                                    string fullInfo = $"{folderName}\n{fileName}";
                                    videoFavToolTip.Show(fullInfo, favListView, e.X + 10, e.Y - 30);
                                }
                            }
                        }
                        else
                        {
                            favListView.Cursor = Cursors.Default;
                            videoFavToolTip.Hide(favListView);
                        }
                    }
                };

                // 鼠标进入视频收藏区域时隐藏图片收藏的ToolTip
                favListView.MouseEnter += (s, e) =>
                {
                    imageFavToolTip.Hide(configForm); // 隐藏图片收藏的ToolTip
                };

                // 鼠标离开时隐藏ToolTip
                favListView.MouseLeave += (s, e) =>
                {
                    videoFavToolTip.Hide(favListView);
                    lastHoverFavItem = null;
                };

                // ==================== 中上：白名单区域 ====================
                Panel whiteListPanel = new Panel()
                {
                    Left = S(368),
                    Top = S(15),
                    Width = S(300),
                    Height = S(280),
                    BackColor = Color.Transparent,
                    BorderStyle = BorderStyle.None
                };

                // 启用双缓冲
                typeof(Panel).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, whiteListPanel, new object[] { true });

                configForm.Controls.Add(whiteListPanel);

                // 绘制白名单面板背景
                whiteListPanel.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    int cornerRadius = S(10); // 圆角半径
                    Rectangle panelRect = new Rectangle(0, 0, whiteListPanel.Width, whiteListPanel.Height);

                    using (GraphicsPath roundedPath = CreateRoundedRectanglePath(panelRect, cornerRadius))
                    {
                        if (configForm.BackgroundImage != null)
                        {
                            // 如果有背景图片，先绘制背景图片的对应区域
                            Rectangle bgRect = configForm.RectangleToClient(
                                whiteListPanel.RectangleToScreen(whiteListPanel.ClientRectangle)
                            );

                            // 设置裁剪区域为圆角矩形
                            g.SetClip(roundedPath);
                            g.DrawImage(configForm.BackgroundImage,
                                whiteListPanel.ClientRectangle,
                                bgRect,
                                GraphicsUnit.Pixel);
                            g.ResetClip();
                        }

                        // 绘制半透明白色背景（无论是否有背景图片都绘制）
                        using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                            g.FillPath(bgBrush, roundedPath);
                    }
                };

                Label lblWhiteList = new Label()
                {
                    Text = "白名单（只影响名字显示）",
                    Left = S(10),
                    Top = S(10),
                    Width = S(280),
                    Height = S(25),
                    Font = new Font("微软雅黑", 10, FontStyle.Bold),
                    BackColor = Color.Transparent
                };
                whiteListPanel.Controls.Add(lblWhiteList);

                // 使用ListView替代TextBox实现透明效果（参考最爱视频的实现）
                ListView txtWhiteList = new ListView()
                {
                    Left = S(10),
                    Top = S(40),
                    Width = S(280),
                    Height = S(190),
                    BackColor = Color.FromArgb(240, 240, 240), // 使用浅灰色背景作为后备
                    ForeColor = Color.Black,
                    BorderStyle = BorderStyle.FixedSingle, // 保留边框
                    View = View.Details,
                    OwnerDraw = true,
                    FullRowSelect = true,
                    HeaderStyle = ColumnHeaderStyle.None,
                    MultiSelect = false,
                    Scrollable = true,
                    LabelEdit = true // 允许编辑
                };
                whiteListPanel.Controls.Add(txtWhiteList);

                // 添加一个占满宽度的列
                txtWhiteList.Columns.Add("", txtWhiteList.ClientSize.Width);

                // 启用双缓冲（温和方式，不干扰OwnerDraw）
                typeof(ListView).InvokeMember("DoubleBuffered",
                    System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, txtWhiteList, new object[] { true });

                // 隐藏列头
                txtWhiteList.DrawColumnHeader += (s, e) => { };

                // 防止Resize递归的标志
                bool isResizingWhiteList = false;

                // 动态调整列宽度以填满整个ListView
                txtWhiteList.Resize += (s, e) =>
                {
                    if (isResizingWhiteList) return;

                    isResizingWhiteList = true;
                    try
                    {
                        if (txtWhiteList.Columns.Count > 0 && txtWhiteList.ClientSize.Width > 0)
                        {
                            txtWhiteList.Columns[0].Width = txtWhiteList.ClientSize.Width;
                        }

                        if (txtWhiteList.IsHandleCreated)
                        {
                            ShowScrollBar(txtWhiteList.Handle, 0, false);
                            ShowScrollBar(txtWhiteList.Handle, SB_VERT, false);
                        }
                    }
                    finally
                    {
                        isResizingWhiteList = false;
                    }
                };

                // 添加白名单项并记录最后一个真实Item的索引
                int lastWhiteItemIndex = -1;
                foreach (string item in blackWhiteListManager.StopWords)
                {
                    ListViewItem lvi = new ListViewItem(item);
                    txtWhiteList.Items.Add(lvi);
                    lastWhiteItemIndex = txtWhiteList.Items.Count - 1;
                }

                // 【关键】添加填充Item填满ListView高度（增加额外的填充项以应对滚动）
                int whiteListHeight = txtWhiteList.ClientSize.Height;
                int whiteListItemHeight = txtWhiteList.Font.Height + 4;
                // 根据屏幕分辨率动态调整填充倍数：4K屏需要更多填充项
                int whiteListFillMultiplier = GetListViewFillMultiplier();
                int whiteListFillerCount = (int)Math.Ceiling((double)whiteListHeight / whiteListItemHeight) * whiteListFillMultiplier;

                for (int i = 0; i < whiteListFillerCount; i++)
                {
                    var fillerItem = new ListViewItem("");
                    fillerItem.Tag = "FILLER";
                    txtWhiteList.Items.Add(fillerItem);
                }

                // 绘制每个项（实现透明效果 + 高亮选中项）
                txtWhiteList.DrawItem += (s, e) =>
                {
                    if (e.Item == null || e.Item.Index < 0 || e.Item.Index >= txtWhiteList.Items.Count) return;
                    
                    Graphics g = e.Graphics;

                    // 先绘制背景图片或默认背景
                    if (configForm.BackgroundImage != null)
                    {
                        try
                        {
                            Rectangle listRect = configForm.RectangleToClient(txtWhiteList.RectangleToScreen(e.Bounds));
                            g.DrawImage(configForm.BackgroundImage,
                                e.Bounds,
                                listRect,
                                GraphicsUnit.Pixel);
                        }
                        catch
                        {
                            using (SolidBrush defaultBg = new SolidBrush(Color.FromArgb(240, 240, 240)))
                                g.FillRectangle(defaultBg, e.Bounds);
                        }
                    }
                    else
                    {
                        using (SolidBrush defaultBg = new SolidBrush(Color.FromArgb(240, 240, 240)))
                            g.FillRectangle(defaultBg, e.Bounds);
                    }

                    // 绘制白色半透明遮罩
                    using (SolidBrush mask = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                        g.FillRectangle(mask, e.Bounds);

                    // 【填充Item】只绘制背景，不绘制内容
                    if (e.Item != null && e.Item.Tag is string && (string)e.Item.Tag == "FILLER")
                    {
                        return; // 只绘制背景，不绘制其他内容
                    }

                    // 如果是选中项，绘制高亮背景
                    if (e.Item != null && e.Item.Selected)
                    {
                        using (SolidBrush highlightBrush = new SolidBrush(Color.FromArgb(100, 135, 206, 250))) // 浅蓝色半透明
                            g.FillRectangle(highlightBrush, e.Bounds);
                    }

                    // 绘制文字
                    if (e.Item != null && e.Item.Index >= 0 && !string.IsNullOrEmpty(e.Item.Text))
                    {
                        TextRenderer.DrawText(g, e.Item.Text, txtWhiteList.Font, e.Bounds, Color.Black,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    }
                };

                // 添加双击编辑功能
                txtWhiteList.MouseDoubleClick += (s, e) =>
                {
                    if (txtWhiteList.SelectedItems.Count > 0)
                    {
                        var selectedItem = txtWhiteList.SelectedItems[0];
                        // 不编辑FILLER项
                        if (selectedItem.Tag == null || (selectedItem.Tag is string && (string)selectedItem.Tag != "FILLER"))
                        {
                            selectedItem.BeginEdit();
                        }
                    }
                };

                // 添加编辑完成后的保存逻辑
                txtWhiteList.AfterLabelEdit += (s, e) =>
                {
                    if (e.Label != null && !string.IsNullOrWhiteSpace(e.Label))
                    {
                        // 更新项的文本（通过索引访问）
                        txtWhiteList.Items[e.Item].Text = e.Label.Trim();

                        // 立即保存到文件
                        try
                        {
                            List<string> updatedStopWords = txtWhiteList.Items.Cast<ListViewItem>()
                                .Where(x => x.Tag == null || (x.Tag is string && (string)x.Tag != "FILLER"))
                                .Select(x => x.Text)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(x => x.Trim())
                                .ToList();

                            blackWhiteListManager.SaveStopWords(updatedStopWords);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("保存白名单失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else if (e.Label != null && string.IsNullOrWhiteSpace(e.Label))
                    {
                        // 如果编辑后为空，取消编辑
                        e.CancelEdit = true;
                    }
                };

                // 隐藏白名单输入框的滚动条
                txtWhiteList.HandleCreated += (s, e) =>
                {
                    ShowScrollBar(txtWhiteList.Handle, 0, false);
                    ShowScrollBar(txtWhiteList.Handle, SB_VERT, false);
                };

                txtWhiteList.VisibleChanged += (s, e) =>
                {
                    if (txtWhiteList.IsHandleCreated)
                    {
                        ShowScrollBar(txtWhiteList.Handle, 0, false);
                        ShowScrollBar(txtWhiteList.Handle, SB_VERT, false);
                    }
                };

                txtWhiteList.VisibleChanged += (s, e) =>
                {
                    if (txtWhiteList.IsHandleCreated)
                    {
                        ShowScrollBar(txtWhiteList.Handle, 0, false);
                        ShowScrollBar(txtWhiteList.Handle, SB_VERT, false);
                    }
                };

                txtWhiteList.ClientSizeChanged += (s, e) =>
                {
                    if (txtWhiteList.IsHandleCreated)
                    {
                        ShowScrollBar(txtWhiteList.Handle, 0, false);
                        ShowScrollBar(txtWhiteList.Handle, SB_VERT, false);
                    }
                };

                // 保留滚轮滚动功能
                txtWhiteList.MouseWheel += (s, e) =>
                {
                    if (txtWhiteList.Items.Count == 0) return;

                    int delta = e.Delta / 120;
                    int scrollAmount = delta * 1; // 每次滚动1个Item

                    if (txtWhiteList.TopItem != null)
                    {
                        int currentIndex = txtWhiteList.TopItem.Index;
                        int newIndex = currentIndex - scrollAmount;

                        // 如果有真实Item，限制滚动范围
                        if (lastWhiteItemIndex >= 0)
                        {
                            // 计算最大可滚动的索引
                            int maxScrollIndex = Math.Max(0, lastWhiteItemIndex);
                            newIndex = Math.Max(0, Math.Min(maxScrollIndex, newIndex));

                            // 【限制滚动】如果滚动到最底部，回滚一个item
                            if (newIndex >= lastWhiteItemIndex && delta < 0) // delta < 0 表示向下滚动
                            {
                                newIndex = Math.Max(0, lastWhiteItemIndex - 1);
                            }
                        }
                        else
                        {
                            newIndex = Math.Max(0, Math.Min(txtWhiteList.Items.Count - 1, newIndex));
                        }

                        if (newIndex != currentIndex && newIndex >= 0 && newIndex < txtWhiteList.Items.Count)
                        {
                            txtWhiteList.TopItem = txtWhiteList.Items[newIndex];
                        }
                    }

                    if (txtWhiteList.IsHandleCreated)
                    {
                        ShowScrollBar(txtWhiteList.Handle, 0, false);
                        ShowScrollBar(txtWhiteList.Handle, SB_VERT, false);
                    }
                };

                // 添加"新增"按钮
                Button btnAddWhite = new Button()
                {
                    Text = "新增",
                    Left = S(10),
                    Top = S(240),
                    Width = S(60),
                    Height = S(25),
                    BackColor = Color.FromArgb(200, 255, 255, 255),
                    ForeColor = Color.Black,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnAddWhite.FlatAppearance.BorderSize = 0;
                // 添加圆角
                using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btnAddWhite.Width, btnAddWhite.Height), 5))
                {
                    btnAddWhite.Region = new Region(path);
                }
                // 绘制圆角描边
                btnAddWhite.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btnAddWhite.Width - 1, btnAddWhite.Height - 1), 5))
                    {
                        using (Pen pen = new Pen(Color.Gray, 1))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                };
                whiteListPanel.Controls.Add(btnAddWhite);

                btnAddWhite.Click += (s, e) =>
                {
                    // 弹出输入框
                    string input = Microsoft.VisualBasic.Interaction.InputBox("请输入要添加的白名单关键词：", "添加白名单", "");
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        // 移除所有FILLER项
                        for (int i = txtWhiteList.Items.Count - 1; i >= 0; i--)
                        {
                            if (txtWhiteList.Items[i].Tag is string && (string)txtWhiteList.Items[i].Tag! == "FILLER")
                            {
                                txtWhiteList.Items.RemoveAt(i);
                            }
                        }

                        // 添加新项
                        ListViewItem newItem = new ListViewItem(input.Trim());
                        txtWhiteList.Items.Add(newItem);

                        // 重新添加FILLER项
                        int whiteListHeight = txtWhiteList.ClientSize.Height;
                        int whiteListItemHeight = txtWhiteList.Font.Height + 4;
                        int whiteListFillerCount = (int)Math.Ceiling((double)whiteListHeight / whiteListItemHeight);

                        for (int i = 0; i < whiteListFillerCount; i++)
                        {
                            var fillerItem = new ListViewItem("");
                            fillerItem.Tag = "FILLER";
                            txtWhiteList.Items.Add(fillerItem);
                        }

                        txtWhiteList.Refresh();

                        // 立即保存到文件
                        try
                        {
                            List<string> updatedStopWords = txtWhiteList.Items.Cast<ListViewItem>()
                                .Where(x => x.Tag == null || (x.Tag is string && (string)x.Tag != "FILLER"))
                                .Select(x => x.Text)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(x => x.Trim())
                                .ToList();

                            blackWhiteListManager.SaveStopWords(updatedStopWords);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("保存白名单失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                };

                // 添加"删除"按钮
                Button btnDelWhite = new Button()
                {
                    Text = "删除",
                    Left = S(75),
                    Top = S(240),
                    Width = S(60),
                    Height = S(25),
                    BackColor = Color.FromArgb(200, 255, 255, 255),
                    ForeColor = Color.Black,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                btnDelWhite.FlatAppearance.BorderSize = 0;
                // 添加圆角
                using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btnDelWhite.Width, btnDelWhite.Height), 5))
                {
                    btnDelWhite.Region = new Region(path);
                }
                // 绘制圆角描边
                btnDelWhite.Paint += (s, pe) =>
                {
                    Graphics g = pe.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(0, 0, btnDelWhite.Width - 1, btnDelWhite.Height - 1), 5))
                    {
                        using (Pen pen = new Pen(Color.Gray, 1))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                };
                whiteListPanel.Controls.Add(btnDelWhite);

                btnDelWhite.Click += (s, e) =>
                {
                    if (txtWhiteList.SelectedItems.Count > 0)
                    {
                        var selectedItem = txtWhiteList.SelectedItems[0];
                        // 不删除FILLER项
                        if (selectedItem.Tag == null || (selectedItem.Tag is string && (string)selectedItem.Tag != "FILLER"))
                        {
                            txtWhiteList.Items.Remove(selectedItem);
                            txtWhiteList.Refresh();

                            // 立即保存到文件并更新内存中的stopWords变量
                            try
                            {
                                List<string> updatedStopWords = txtWhiteList.Items.Cast<ListViewItem>()
                                    .Where(x => x.Tag == null || (x.Tag is string && (string)x.Tag != "FILLER"))
                                    .Select(x => x.Text)
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Select(x => x.Trim())
                                    .ToList();

                                blackWhiteListManager.SaveStopWords(updatedStopWords);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("保存白名单失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                };

                // ==================== Load Current Configuration ====================
                // Load path slots from PathManager
                PathSlot[] allPaths = pathManager.GetAllPaths();
                for (int i = 0; i < 6; i++)
                {
                    pathCheckBoxes[i].Checked = allPaths[i].IsEnabled;
                    pathRadioLabels[i].Tag = allPaths[i].IsEnabled;
                    pathRadioLabels[i].Invalidate();
                    pathTextBoxes[i].Text = allPaths[i].DirectoryPath;
                }

                // 清除第一个文本框的自动选中状态
                if (pathTextBoxes.Length > 0 && pathTextBoxes[0] != null)
                {
                    pathTextBoxes[0].SelectionLength = 0;
                    pathTextBoxes[0].SelectionStart = 0;
                    // 将焦点移到配置窗口本身，避免任何文本框获得焦点
                    configForm.Focus();
                }

                // Load active mode from ModeManager
                PrimaryMode primaryMode = modeManager.GetPrimaryMode();
                FilterOptions activeFilters = modeManager.GetActiveFilters();

                // 用于跟踪当前选中的主模式按钮（单选）
                Button? selectedPrimaryModeButton = null;

                // 用于跟踪当前选中的筛选按钮（多选）
                Dictionary<Button, FilterOptions> filterButtonStates = new Dictionary<Button, FilterOptions>();

                Color selectedColor = Color.Transparent;
                Color normalColor = Color.Transparent;

                // 设置主模式按钮选中状态的辅助方法（单选）
                Action<Button, PrimaryMode> selectPrimaryMode = (btn, mode) =>
                {
                    if (selectedPrimaryModeButton != null)
                    {
                        selectedPrimaryModeButton.BackColor = normalColor;
                        selectedPrimaryModeButton.Tag = null; // 标记为未选中
                        selectedPrimaryModeButton.Invalidate();
                    }
                    selectedPrimaryModeButton = btn;
                    btn.BackColor = selectedColor;
                    btn.Tag = "selected"; // 标记为选中
                    btn.Invalidate();

                    // Update mode manager
                    modeManager.SetPrimaryMode(mode);
                };

                // 设置筛选按钮选中状态的辅助方法（多选切换）
                Action<Button, FilterOptions> toggleFilter = (btn, filter) =>
                {
                    // Toggle filter in mode manager
                    modeManager.ToggleFilter(filter);

                    // Update button visual state
                    bool isEnabled = modeManager.IsFilterEnabled(filter);
                    btn.BackColor = isEnabled ? selectedColor : normalColor;
                    btn.Tag = isEnabled ? "selected" : null; // 标记选中状态
                    btn.Invalidate();

                    // Update tracking dictionary
                    filterButtonStates[btn] = filter;
                };

                // 根据当前模式设置初始选中状态
                // Primary Mode (single select)
                if (primaryMode == PrimaryMode.VideoMode)
                {
                    selectedPrimaryModeButton = btnVideoMode;
                    btnVideoMode.BackColor = selectedColor;
                    btnVideoMode.Tag = "selected";
                }
                else
                {
                    selectedPrimaryModeButton = btnFolderMode;
                    btnFolderMode.BackColor = selectedColor;
                    btnFolderMode.Tag = "selected";
                }

                // Filter Options (multi-select)
                filterButtonStates[btnNewestToOldest] = FilterOptions.NewestToOldest;
                filterButtonStates[btnOldestToNewest] = FilterOptions.OldestToNewest;
                filterButtonStates[btnNeverWatched] = FilterOptions.NeverWatched;
                filterButtonStates[btnFavoritesOnly] = FilterOptions.FavoritesOnly;
                filterButtonStates[btnSuperResolution] = FilterOptions.SuperResolution;
                filterButtonStates[btnUncensored] = FilterOptions.Uncensored;

                // Set initial filter button states
                if (modeManager.IsFilterEnabled(FilterOptions.NewestToOldest))
                {
                    btnNewestToOldest.BackColor = selectedColor;
                    btnNewestToOldest.Tag = "selected";
                }
                if (modeManager.IsFilterEnabled(FilterOptions.OldestToNewest))
                {
                    btnOldestToNewest.BackColor = selectedColor;
                    btnOldestToNewest.Tag = "selected";
                }
                if (modeManager.IsFilterEnabled(FilterOptions.NeverWatched))
                {
                    btnNeverWatched.BackColor = selectedColor;
                    btnNeverWatched.Tag = "selected";
                }
                if (modeManager.IsFilterEnabled(FilterOptions.FavoritesOnly))
                {
                    btnFavoritesOnly.BackColor = selectedColor;
                    btnFavoritesOnly.Tag = "selected";
                }
                if (modeManager.IsFilterEnabled(FilterOptions.SuperResolution))
                {
                    btnSuperResolution.BackColor = selectedColor;
                    btnSuperResolution.Tag = "selected";
                }
                if (modeManager.IsFilterEnabled(FilterOptions.Uncensored))
                {
                    btnUncensored.BackColor = selectedColor;
                    btnUncensored.Tag = "selected";
                }

                // 绑定按钮点击事件
                // Primary Mode buttons (single select)
                btnVideoMode.Click += (s, ee) => selectPrimaryMode(btnVideoMode, PrimaryMode.VideoMode);
                btnFolderMode.Click += (s, ee) => selectPrimaryMode(btnFolderMode, PrimaryMode.FolderMode);

                // Filter Option buttons (multi-select toggle)
                btnNewestToOldest.Click += (s, ee) => toggleFilter(btnNewestToOldest, FilterOptions.NewestToOldest);
                btnOldestToNewest.Click += (s, ee) => toggleFilter(btnOldestToNewest, FilterOptions.OldestToNewest);
                btnNeverWatched.Click += (s, ee) => toggleFilter(btnNeverWatched, FilterOptions.NeverWatched);
                btnFavoritesOnly.Click += (s, ee) => toggleFilter(btnFavoritesOnly, FilterOptions.FavoritesOnly);
                btnSuperResolution.Click += (s, ee) => toggleFilter(btnSuperResolution, FilterOptions.SuperResolution);
                btnUncensored.Click += (s, ee) => toggleFilter(btnUncensored, FilterOptions.Uncensored);

                // Load exclusion settings (now unified)
                ExclusionSettings exclusionSettings = modeManager.GetExclusionSettings();
                numExcludeCount1.Value = exclusionSettings.ExcludeRecentCount;
                numExcludeDays1.Value = exclusionSettings.ExcludeRecentDays;
                numMinVideos.Value = exclusionSettings.MinVideosForExclusion;

                // Load custom keywords - already loaded into ListView above

                // ==================== 窗口关闭时自动保存所有配置 ====================
                configForm.FormClosing += (s, ee) =>
                {
                    try
                    {
                        // ==================== Save Path Configuration ====================
                        for (int i = 0; i < 6; i++)
                        {
                            bool isEnabled = (bool)(pathRadioLabels[i].Tag ?? false);
                            pathManager.SetPath(i, pathTextBoxes[i].Text.Trim(), isEnabled);
                        }
                        pathManager.SaveToConfig();

                        // ==================== Save Mode Configuration ====================
                        // Primary mode is already saved via button click handlers
                        // Filter options are already saved via button click handlers
                        // Just need to ensure final save

                        // ==================== Save Exclusion Settings ====================
                        ExclusionSettings settings = new ExclusionSettings
                        {
                            ExcludeRecentDays = (int)numExcludeDays1.Value,
                            ExcludeRecentCount = (int)numExcludeCount1.Value,
                            MinVideosForExclusion = (int)numMinVideos.Value
                        };
                        modeManager.SetExclusionSettings(settings);

                        // ==================== Save Custom Keywords ====================
                        // 从ListView中收集所有关键词
                        List<string> keywords = lstCustomKeywords.Items.Cast<ListViewItem>()
                            .Where(x => x.Tag == null || (x.Tag is string && (string)x.Tag != "FILLER"))
                            .Select(x => x.Text)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .ToList();

                        string keywordsStr = string.Join(",", keywords);
                        modeManager.SetCustomKeywords(keywordsStr);

                        if (!string.IsNullOrEmpty(keywordsStr))
                        {
                            modeManager.EnableFilter(FilterOptions.CustomKeywords);
                        }
                        else
                        {
                            modeManager.DisableFilter(FilterOptions.CustomKeywords);
                        }

                        modeManager.SaveToConfig();

                        // ==================== Backward Compatibility ====================
                        PathSlot[] paths = pathManager.GetAllPaths();
                        PathSlot? firstEnabledPath = paths.FirstOrDefault(p => p.IsEnabled && !string.IsNullOrEmpty(p.DirectoryPath));
                        if (firstEnabledPath != null)
                        {
                            workPath = firstEnabledPath.DirectoryPath;
                        }

                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"保存配置失败：{ex.Message}\n\n堆栈跟踪：\n{ex.StackTrace}", 
                            "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

            configForm.FormClosed += (s, e) =>
            {
                configFormInstance = null;
                imageFavToolTip.Dispose();
                videoFavToolTip.Dispose();
            };

            // 恢复布局并显示窗口
            configForm.ResumeLayout(false);
            configForm.PerformLayout();
            
            // 在窗口显示后强制刷新所有ListView，确保DrawItem正确触发
            configForm.Shown += (s, e) =>
            {
                txtBlackList?.Invalidate();
                lstCustomKeywords?.Invalidate();
                imageFavListView?.Invalidate();
                favListView?.Invalidate();
            };
            
            configForm.Show(this);

            // 执行完逻辑后，把焦点给到主背景
            this.ActiveControl = null;
        }




        private List<string> SafeGetFiles(string path, string[] extensions)
        {
            List<string> files = new List<string>();
            try
            {
                var currentFiles = Directory.GetFiles(path, "*.*")
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()));
                files.AddRange(currentFiles);

                foreach (var dir in Directory.GetDirectories(path))
                {
                    files.AddRange(SafeGetFiles(dir, extensions));
                }
            }
            catch { }
            return files;
        }

        // --- 核心抽取逻辑 ---
        // --- 核心抽取逻辑 ---
        private void button1_Click(object? sender, EventArgs e)
        {
            // ==================== Path Validation ====================
            // Check if using new multi-path system or legacy single path
            List<string> enabledPaths = pathManager.GetEnabledPaths();

            if (enabledPaths.Count == 0)
            {
                ShowUserFriendlyError(
                    "未配置视频路径",
                    "请先在配置中启用至少一个视频路径。\n\n建议操作：\n1. 点击「选择路径」按钮\n2. 启用至少一个路径\n3. 保存设置后重试"
                );
                return;
            }

            try
            {
                // ==================== Use VideoSelector for Video Selection ====================
                // Create VideoSelector with all managers
                VideoSelector videoSelector = new VideoSelector(
                    pathManager,
                    modeManager,
                    historyManager,
                    favoritesManager,
                    blackWhiteListManager
                );

                // Select video using the new system
                string? selectedVideo = videoSelector.SelectVideo();

                // ==================== Handle Selection Result ====================
                if (string.IsNullOrEmpty(selectedVideo))
                {
                    // VideoSelector already logged the specific error
                    // Show a generic user-friendly message
                    ShowUserFriendlyError(
                        "无法选择视频",
                        "未能找到符合当前模式和过滤条件的视频。\n\n建议操作：\n1. 检查路径配置\n2. 调整模式设置\n3. 减少排除规则\n4. 启用更多路径"
                    );
                    return;
                }

                // ==================== Validate Selected Video ====================
                if (!File.Exists(selectedVideo))
                {
                    ShowUserFriendlyError(
                        "视频文件不存在",
                        "选中的视频文件不存在或已被删除。\n\n将尝试重新选择..."
                    );
                    // Retry once
                    button1_Click(sender, e);
                    return;
                }

                // ==================== Update UI with Selected Video ====================
                currentVideoPath = selectedVideo;
                displayFolderName = GetSmartFolderName(Path.GetDirectoryName(selectedVideo));
                displayFileName = Path.GetFileName(selectedVideo);

                UpdateLabelLayout();
                lblFolderName.Invalidate();
                lblFileName.Invalidate();

                // ==================== Play Video and Record History ====================
                try
                {
                    Process.Start(new ProcessStartInfo(selectedVideo) { UseShellExecute = true });

                    // Record to history using HistoryManager
                    historyManager.RecordPlay(displayFolderName, displayFileName, 0, selectedVideo);
                    RecordSelectedRootPathUsage(selectedVideo);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    ShowUserFriendlyError(
                        "无法播放视频",
                        $"无法打开视频文件。\n\n文件：{displayFileName}\n\n建议操作：\n1. 检查是否安装了视频播放器\n2. 检查文件关联设置\n3. 尝试手动打开文件"
                    );
                }
                catch (UnauthorizedAccessException)
                {
                    ShowUserFriendlyError(
                        "访问被拒绝",
                        $"没有权限访问视频文件。\n\n文件：{displayFileName}\n\n建议操作：\n1. 检查文件权限\n2. 以管理员身份运行程序"
                    );
                }
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error in video selection: {ex.Message}\n{ex.StackTrace}");
                ShowUserFriendlyError(
                    "发生错误",
                    $"视频选择过程中发生错误。\n\n错误信息：{ex.Message}\n\n建议操作：\n1. 检查配置设置\n2. 重启应用程序\n3. 查看日志文件获取详细信息"
                );
            }
        }

        /// <summary>
        /// Shows a user-friendly error message with title and detailed message.
        /// </summary>
        private void ShowUserFriendlyError(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Logs error messages to console and file for debugging without blocking the user.
        /// </summary>
        private void LogError(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logMessage = $"[{timestamp}] ERROR: {message}";
                Console.WriteLine(logMessage);

                // Write to log file
                string logDir = Path.Combine(Application.StartupPath, "config");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                string logFile = Path.Combine(logDir, "error.log");
                File.AppendAllText(logFile, logMessage + Environment.NewLine);
            }
            catch
            {
                // Silently fail - don't let logging errors crash the app
            }
        }



        private string GetSmartFolderName(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "未知领域";
            DirectoryInfo dir = new DirectoryInfo(path);

            // 处理磁盘根目录的情况（如 "F:\"）
            if (dir.Parent == null)
            {
                // 返回磁盘盘符，如 "F:"
                return dir.Root.Name.TrimEnd('\\');
            }

            string finalName = dir.Name ?? "未知";

            // 向上溯源
            while (dir != null && !dir.FullName.TrimEnd('\\').Equals(workPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                string name = dir.Name ?? "未知";
                string nameUpper = name.ToUpper();

                // 1. 【核心修正】严格全字匹配白名单
                // 只有当文件夹名完全是 "FC2" 时才停止，像 "FC2PPV-123" 这种不完全相等的会被视为噪点继续往上找
                if (stopWords.Any(sw => nameUpper.Equals(sw.ToUpper())))
                {
                    finalName = name;
                    break;
                }

                // 2. 噪点识别：如果是那种长长的编号文件夹，继续往上找
                // 规则：包含 PPV/VOL 等关键字，或者名字很长且数字占比很高（超过50%）
                bool isNoise = false;
                string[] noiseKeywords = { "PPV", "EPISODE", "VOL", "PART", "S0", "S1", "DISK", "新建文件夹", "PLUS", "REMASTER", "4K", "1080", "修正", "增强", "pt" };
                
                // 检查是否包含噪点关键字
                if (noiseKeywords.Any(k => nameUpper.Contains(k)))
                {
                    isNoise = true;
                }
                // 检查是否是"字母+分隔符+数字"的编号格式（如 MIDD-807, MIDD_807, MIDD 807, IPZ-083-C）
                // 或者是"字母+数字"的无分隔符编号格式（如 IPZ00083, MIDD807）
                // 分隔符可以是: 连字符(-), 下划线(_), 空格( )
                // 支持多段格式: IPZ-083-C, SSIS-123-A, FC2-PPV-1234567
                // 支持无分隔符: IPZ00083, MIDD807, SNIS123
                else if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Z]{2,10}([-_\s]\d+[-_\s]?[A-Z0-9]*|\d{3,})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    isNoise = true;
                }
                // 检查是否是纯数字或数字占比过高的编号文件夹
                // 例如：FC2PPV-1234567（数字占比高），但不包括正常的中文文件夹名
                else if (name.Length > 8)
                {
                    int digitCount = name.Count(char.IsDigit);
                    int letterCount = name.Count(char.IsLetter);
                    // 如果数字超过6个，且数字占比超过50%，认为是编号文件夹
                    if (digitCount > 6 && digitCount > letterCount)
                    {
                        isNoise = true;
                    }
                }

                if (isNoise)
                {
                    var parent = dir.Parent!;
                    if (parent?.Name != null)
                    {
                        dir = parent;
                        finalName = parent.Name;
                    }
                }
                else
                {
                    // 如果既不是白名单也不是噪点，暂时记录，但为了找“erika”，我们尝试再往上爬一级看看
                    finalName = name;

                    // 关键：如果上一级就是白名单，这一级就不该停
                    if (dir.Parent != null)
                    {
                        string pName = (dir.Parent.Name ?? "未知").ToUpper();
                        if (stopWords.Any(sw => pName.Equals(sw.ToUpper())))
                        {
                            dir = dir.Parent;
                            finalName = dir.Name ?? "未知";
                            break;
                        }
                    }
                    break;
                }
            }

            return finalName;
        }

        private void Form1_Load(object? sender, EventArgs e)
        {
            // 路径配置现在由 PathManager 管理

            // 确保静音按钮位置正确（考虑DPI缩放）
            UpdateMuteButtonPosition();
        }

        private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // 保存所有配置
            try
            {
                pathManager.SaveToConfig();
                modeManager.SaveToConfig();
                favoritesManager.SaveToConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving configuration on exit: {ex.Message}");
            }
            
            // 保存静音设置
            File.WriteAllText(muteFilePath, isMuted.ToString());

            if (mediaPlayer != null)
            {
                mediaPlayer.Stop();
                mediaPlayer.Close();
            }
        }

        // 创建静音切换按钮
        private PictureBox muteButton = null!;

        // 【可配置】静音按钮的大小和位置
        private readonly int MUTE_BUTTON_WIDTH = 24;   // 按钮宽度
        private readonly int MUTE_BUTTON_HEIGHT = 24;  // 按钮高度
        private readonly int MUTE_BUTTON_X = 152;      // 按钮X坐标
        private readonly int MUTE_BUTTON_Y = 6;        // 按钮Y坐标

        private void CreateMuteButton()
        {
            muteButton = new PictureBox();
            UpdateMuteButtonPosition(); // 使用新的位置更新方法
            muteButton.BackColor = Color.Transparent;
            muteButton.Cursor = Cursors.Hand;
            muteButton.SizeMode = PictureBoxSizeMode.Zoom;

            // 根据静音状态设置图标
            UpdateMuteButtonIcon();

            // 点击事件
            muteButton.Click += (s, e) =>
            {
                // 播放静音按钮音效
                PlayButtonSound("mute");

                isMuted = !isMuted;

                // 更新MediaPlayer音量
                if (mediaPlayer != null)
                {
                    mediaPlayer.Volume = isMuted ? 0 : 0.8;
                }

                // 更新按钮图标
                UpdateMuteButtonIcon();

                // 保存设置
                File.WriteAllText(muteFilePath, isMuted.ToString());
            };

            this.Controls.Add(muteButton);
            muteButton.BringToFront();
        }

        // 新增：更新静音按钮位置的方法，考虑DPI缩放
        private void UpdateMuteButtonPosition()
        {
            if (muteButton != null)
            {
                muteButton.Size = new Size(S(MUTE_BUTTON_WIDTH), S(MUTE_BUTTON_HEIGHT));
                muteButton.Location = new Point(S(MUTE_BUTTON_X), S(MUTE_BUTTON_Y));
            }
        }

        // 重写DPI改变事件，确保静音按钮位置正确
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);

            // DPI改变时更新静音按钮位置
            UpdateMuteButtonPosition();
        }

        private void UpdateMuteButtonIcon()
        {
            try
            {
                // 从 audio 文件夹加载自定义图片
                string iconPath = isMuted
                    ? Path.Combine(Application.StartupPath, "audio", "mute.png")      // 静音图标
                    : Path.Combine(Application.StartupPath, "audio", "unmute.png");   // 有声图标

                if (File.Exists(iconPath))
                {
                    // 释放旧图片
                    if (muteButton.Image != null)
                    {
                        var oldImage = muteButton.Image;
                        muteButton.Image = null;
                        oldImage.Dispose();
                    }

                    // 加载新图片
                    using (FileStream fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read))
                    {
                        muteButton.Image = Image.FromStream(fs);
                    }
                }
                else
                {
                    // 如果图片不存在，使用代码绘制的默认图标
                    DrawDefaultMuteIcon();
                }
            }
            catch
            {
                // 出错时使用默认图标
                DrawDefaultMuteIcon();
            }
        }

        private void DrawDefaultMuteIcon()
        {
            // 创建一个简单的图标：静音显示??，有声显示??
            Bitmap icon = new Bitmap(24, 24);
            using (Graphics g = Graphics.FromImage(icon))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                if (isMuted)
                {
                    // 静音图标：喇叭 + X
                    // 喇叭
                    using (SolidBrush brush = new SolidBrush(Color.White))
                    {
                        Point[] speaker = new Point[]
                        {
                            new Point(4, 8),
                            new Point(8, 8),
                            new Point(12, 4),
                            new Point(12, 20),
                            new Point(8, 16),
                            new Point(4, 16)
                        };
                        g.FillPolygon(brush, speaker);
                    }
                    // X标记
                    using (Pen pen = new Pen(Color.Red, 2))
                    {
                        g.DrawLine(pen, 14, 8, 20, 14);
                        g.DrawLine(pen, 20, 8, 14, 14);
                    }
                }
                else
                {
                    // 有声图标：喇叭 + 声波
                    // 喇叭
                    using (SolidBrush brush = new SolidBrush(Color.White))
                    {
                        Point[] speaker = new Point[]
                        {
                            new Point(4, 8),
                            new Point(8, 8),
                            new Point(12, 4),
                            new Point(12, 20),
                            new Point(8, 16),
                            new Point(4, 16)
                        };
                        g.FillPolygon(brush, speaker);
                    }
                    // 声波
                    using (Pen pen = new Pen(Color.White, 2))
                    {
                        g.DrawArc(pen, 14, 6, 6, 12, -45, 90);
                        g.DrawArc(pen, 16, 4, 8, 16, -45, 90);
                    }
                }
            }

            // 释放旧图片
            if (muteButton.Image != null)
            {
                var oldImage = muteButton.Image;
                muteButton.Image = null;
                oldImage.Dispose();
            }

            muteButton.Image = icon;
        }

        // --- 按钮音效播放功能 ---
        /// <summary>
        /// 播放指定按钮的音效文件（支持多个重名文件随机播放）
        /// 文件命名规则：
        /// - btn1.wav, btn1.mp3（基础文件）
        /// - btn1_1.wav, btn1_2.mp3（下划线编号）
        /// - btn1(2).wav, btn1(3).mp3（Windows复制格式）
        /// - btn1-a.wav, btn1-cool.mp3（任意分隔符）
        /// </summary>
        /// <param name="soundFileBaseName">音效文件基础名（如 "btn1"，不含扩展名）</param>
        private void PlayButtonSound(string soundFileBaseName)
        {
            try
            {
                // 音效文件夹路径
                string audioDir = Path.Combine(Application.StartupPath, "audio");

                // 如果文件夹不存在，自动创建
                if (!Directory.Exists(audioDir))
                {
                    Directory.CreateDirectory(audioDir);
                    return; // 刚创建的文件夹肯定是空的，直接返回
                }

                // 查找所有匹配的音效文件（支持 .wav 和 .mp3）
                List<string> matchedFiles = new List<string>();
                string[] extensions = { ".wav", ".mp3" };

                // 获取 audio 文件夹中的所有音频文件
                foreach (var ext in extensions)
                {
                    var allFiles = Directory.GetFiles(audioDir, "*" + ext);
                    foreach (var file in allFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);

                        // 匹配规则：文件名以 soundFileBaseName 开头
                        // 支持：btn1.wav, btn1_1.wav, btn1(2).wav, btn1-a.wav 等
                        if (fileName.Equals(soundFileBaseName, StringComparison.OrdinalIgnoreCase) ||
                            fileName.StartsWith(soundFileBaseName + "_", StringComparison.OrdinalIgnoreCase) ||
                            fileName.StartsWith(soundFileBaseName + "(", StringComparison.OrdinalIgnoreCase) ||
                            fileName.StartsWith(soundFileBaseName + "-", StringComparison.OrdinalIgnoreCase) ||
                            fileName.StartsWith(soundFileBaseName + " ", StringComparison.OrdinalIgnoreCase))
                        {
                            matchedFiles.Add(file);
                        }
                    }
                }

                // 如果没有找到任何音效文件，直接返回
                if (matchedFiles.Count == 0) return;

                // 随机选择一个音效文件
                Random rnd = new Random(Guid.NewGuid().GetHashCode());
                string selectedSound = matchedFiles[rnd.Next(matchedFiles.Count)];

                // 停止当前播放的音效
                soundPlayer.Stop();
                soundPlayer.Close();

                // 加载并播放新音效
                // 按钮音效独立于视频背景音，固定音量
                soundPlayer.Open(new Uri(selectedSound, UriKind.Absolute));
                soundPlayer.Volume = 1.0; // 固定音量
                soundPlayer.Play();
            }
            catch { }
        }

        /// <summary>
        /// 为5个按钮绑定音效
        /// </summary>
        private void BindButtonSounds()
        {
            // pictureBox1 - 配置按钮 -> btn1.wav 或 btn1.mp3
            pictureBox1.Click -= btnSelectPath_Click;
            pictureBox1.Click += (s, e) =>
            {
                PlayButtonSound("btn1");
                btnSelectPath_Click(s, e);
            };

            // pictureBox2 - 随机翻牌按钮 -> btn2.wav 或 btn2.mp3
            pictureBox2.Click -= button1_Click;
            pictureBox2.Click += (s, e) =>
            {
                PlayButtonSound("btn2");
                button1_Click(s, e);
            };

            // pictureBox5 - 随机女士按钮（随机打开图片）-> btn3.wav 或 btn3.mp3
            pictureBox5.Click -= BtnRandomLady_Click;
            pictureBox5.Click += (s, e) =>
            {
                PlayButtonSound("btn3");
                BtnRandomLady_Click(s, e);
            };

            // pictureBox3 - 历史记录按钮 -> btn4.wav 或 btn4.mp3
            pictureBox3.Click -= btnHistory_Click;
            pictureBox3.Click += (s, e) =>
            {
                PlayButtonSound("btn4");
                btnHistory_Click(s, e);
            };

            // pictureBox4 - 年度报告按钮 -> btn5.wav 或 btn5.mp3
            pictureBox4.Click -= btnAnnualReport_Click;
            pictureBox4.Click += (s, e) =>
            {
                PlayButtonSound("btn5");
                btnAnnualReport_Click(s, e);
            };
        }

        // --- 文件夹名字显示文件名字显示 ---
        private void Label_Paint(object? sender, PaintEventArgs e)
        {
            Label lbl = (Label)sender!;
            float sc = GetScale();
            // 修复4K下字体过大问题：在高分辨率下适当缩小字体
            // 1080p (sc=1.0): fSc=1.0
            // 2K (sc=1.5): fSc=0.925
            // 4K (sc=2.0): fSc=0.85
            float fSc = sc <= 1.0f ? 1.0f : (1.0f - (sc - 1.0f) * 0.15f);
            string textToDraw = (lbl.Name == "lblFolderName") ? displayFolderName : displayFileName;
            if (string.IsNullOrEmpty(textToDraw)) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // --- A. 自适应圆角路径 ---
            // 增加圆角随分辨率的缩放，1080p下12像素，4K下自动扩大
            int radius = S(25);
            using (GraphicsPath path = new GraphicsPath())
            {
                // 留出 1 像素的边空，防止描边被裁剪
                float borderThickness = 1.5f * sc;
                RectangleF rect = new RectangleF(borderThickness / 2, borderThickness / 2, lbl.Width - borderThickness, lbl.Height - borderThickness);

                path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
                path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
                path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
                path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
                path.CloseAllFigures();

                // --- B. 填充与描边（降低透明度，减少视觉冲击）---
                using (SolidBrush backBrush = new SolidBrush(Color.FromArgb(120, 255, 255, 255))) // 从140降到120
                    g.FillPath(backBrush, path);

                using (Pen borderPen = new Pen(Color.FromArgb(120, 0, 0, 0), borderThickness)) // 从200降到120，边框更淡
                    g.DrawPath(borderPen, path);
            }
            // --- C. 文字绘制 (提供更大的“内边距”让字不挤) ---
            using (GraphicsPath textPath = new GraphicsPath())
            {
                float baseFontSize = (lbl.Name == "lblFolderName") ? 23f : 15f;

                using (Font adaptiveFont = new Font("微软雅黑", baseFontSize * fSc, FontStyle.Bold))
                {
                    float currentEmSize = g.DpiY * adaptiveFont.Size / 72;

                    // --- 核心修复：针对短文本，暴力撑开 textRect ---
                    // 如果字符 <= 12，我们给它一个巨大的宽度 (lbl.Width * 2)，
                    // 确保 GDI+ 认为空间绝对充足，从而放弃任何换行尝试。
                    Rectangle textRect;
                    bool isFolderName = (lbl.Name == "lblFolderName");
                    bool isShortText = (!isFolderName && textToDraw.Length <= 18);

                    if (isShortText)
                    {
                        // 给予超大宽度，水平居中对齐会处理剩下的事
                        textRect = new Rectangle(-lbl.Width, S(10), lbl.Width * 3, lbl.Height - S(20));
                    }
                    else
                    {
                        // 对于文件夹名字或长文本，使用正常宽度以支持换行
                        textRect = new Rectangle(S(8), S(10), lbl.Width - S(16), lbl.Height - S(20));
                    }

                    using (StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter
                    })
                    {
                        if (isShortText)
                        {
                            sf.FormatFlags |= StringFormatFlags.NoWrap;
                        }

                        textPath.AddString(textToDraw, adaptiveFont.FontFamily, (int)adaptiveFont.Style, currentEmSize, textRect, sf);
                    }
                }

                // --- D. 视觉渲染 ---
                using (Pen outlinePen = new Pen(Color.FromArgb(180, 0, 0, 0), 2.0f * sc)) // 从200降到180，轮廓更柔和
                {
                    outlinePen.LineJoin = LineJoin.Round;
                    g.DrawPath(outlinePen, textPath);
                }

                using (SolidBrush textBrush = new SolidBrush(ColorTranslator.FromHtml("#FF1493")))
                {
                    g.FillPath(textBrush, textPath);
                }
            }
        }

        private void LblFolderName_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(displayFolderName)) return;

            // --- 【去重核心逻辑】 ---
            // 如果档案里最后一条记录和现在这一条的时间、内容完全一致，只是类型不同
            // 咱们就得把最后一行删了，再写入“宠幸”记录
            try
            {
                if (File.Exists(historyFilePath))
                {
                    var allLines = File.ReadAllLines(historyFilePath).ToList();
                    if (allLines.Count > 0)
                    {
                        string lastLine = allLines.Last();
                        // 检查最后一行是不是刚才那条“翻牌”记录 (Type=0)
                        if (lastLine.Contains(displayFolderName) && lastLine.EndsWith("|0"))
                        {
                            allLines.RemoveAt(allLines.Count - 1); // 删掉那条没用的翻牌记录
                            File.WriteAllLines(historyFilePath, allLines); // 保存回去
                        }
                    }
                }
            }
            catch { }

            // 现在记录真正的“宠幸” (Type = 1)
            RecordHistory(displayFolderName, displayFileName, 1);

            // 视觉反馈
            lblFolderName.ForeColor = Color.Gold;
            MessageBox.Show($"今日宠幸【{displayFolderName}】！");
        }

        // 修改您的记录方法，多存一个字段（绝对路径）
        private void RecordHistory(string folderName, string fileName, int type)
        {
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string actualFolderPath = string.IsNullOrEmpty(currentVideoPath)
                ? workPath
                : Path.GetDirectoryName(currentVideoPath) ?? workPath;

            // 确保 config 目录存在
            string? configDir = Path.GetDirectoryName(historyFilePath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            if (File.Exists(historyFilePath))
            {
                var lines = File.ReadAllLines(historyFilePath).ToList();
                // 查找同一分钟、同一视频的最后一条记录
                var lastEntry = lines.Where(l => l.Contains($"|{fileName}|")).LastOrDefault();

                int finalType = type;
                if (lastEntry != null)
                {
                    string[] p = lastEntry.Split('|');
                    int lastType = p.Length > 3 ? int.Parse(p[3]) : 0;

                    // 核心覆盖逻辑：
                    // 1. 如果当前是“宠幸”(1)，且上一条是“冷宫”(0)，则覆盖。
                    // 2. 如果当前是“宠幸”(1)，且上一条是“重温”(2)，则升级为“回味”(3)。
                    if (type == 1 && lastType == 0) { lines.Remove(lastEntry); finalType = 1; }
                    else if (type == 1 && lastType == 2) { lines.Remove(lastEntry); finalType = 3; }
                    else if (type == 2) { /* 重温不覆盖任何东西，直接添加 */ }
                }

                lines.Add($"{time}|{folderName}|{fileName}|{finalType}|{actualFolderPath}");
                File.WriteAllLines(historyFilePath, lines);
            }
            else
            {
                File.WriteAllText(historyFilePath, $"{time}|{folderName}|{fileName}|{type}|{actualFolderPath}" + Environment.NewLine);
            }

            RecordSelectedRootPathUsage(currentVideoPath);
            
            // 如果历史记录窗口是打开的，刷新显示
            if (historyFormInstance != null && !historyFormInstance.IsDisposed && historyUpdateDataAction != null)
            {
                try
                {
                    historyUpdateDataAction.Invoke();
                }
                catch { }
            }
            // 如果热力图窗口是打开的，刷新显示
            if (heatmapFormInstance != null && !heatmapFormInstance.IsDisposed && heatmapUpdateDataAction != null)
            {
                try
                {
                    heatmapUpdateDataAction.Invoke();
                }
                catch { }
            }
            // 如果月度统计窗口是打开的，刷新显示
            if (monthlyStatsFormInstance != null && !monthlyStatsFormInstance.IsDisposed && monthlyStatsUpdateDataAction != null)
            {
                try
                {
                    monthlyStatsUpdateDataAction.Invoke();
                }
                catch { }
            }
            // 如果不活跃天数窗口是打开的，刷新显示
            if (inactiveStreakFormInstance != null && !inactiveStreakFormInstance.IsDisposed && inactiveStreakUpdateDataAction != null)
            {
                try
                {
                    inactiveStreakUpdateDataAction.Invoke();
                }
                catch { }
            }
        }

        // 文件名字框文字大小
        private void UpdateLabelLayout()
        {
            float sc = GetScale();
            // 与Label_Paint保持一致的缩放逻辑
            float fSc = sc <= 1.0f ? 1.0f : (1.0f - (sc - 1.0f) * 0.15f);

            using (Graphics g = this.CreateGraphics())
            {
                // 创建实际使用的字体对象
                using (Font folderFont = new Font("微软雅黑", 23f * fSc, FontStyle.Bold))
                using (Font fileFont = new Font("微软雅黑", 15f * fSc, FontStyle.Bold))
                {
                    // 计算文件夹框大小 - 支持最多2行换行
                    int maxWidth = (int)(this.ClientSize.Width * 0.92);
                    SizeF sizeFolder = g.MeasureString(displayFolderName, folderFont, maxWidth - S(25));
                    
                    // 计算单行和两行的高度
                    float singleLineHeight = g.MeasureString("测试", folderFont).Height;
                    float maxTwoLinesHeight = singleLineHeight * 2.0f;
                    
                    // 限制最多2行：如果计算出的高度超过2行，就限制为2行并用省略号
                    int newHeight = (int)Math.Min(sizeFolder.Height, maxTwoLinesHeight) + S(10);
                    
                    lblFolderName.Width = Math.Min((int)sizeFolder.Width + S(20), maxWidth);
                    lblFolderName.Height = newHeight;

                    // 文件名框 - 使用新的16f字体大小
                    SizeF sizeFile;

                    if (displayFileName.Length <= 18)
                    {
                        sizeFile = g.MeasureString(displayFileName, fileFont);
                        lblFileName.Width = (int)sizeFile.Width + S(15);
                        lblFileName.Height = (int)sizeFile.Height + S(15);
                    }
                    else
                    {
                        sizeFile = g.MeasureString(displayFileName, fileFont, maxWidth - S(25));
                        lblFileName.Width = Math.Min((int)sizeFile.Width + S(30), maxWidth);

                        SizeF twoLinesHeight = g.MeasureString("一行\n二行", fileFont, maxWidth - S(18));
                        lblFileName.Height = (int)Math.Min(sizeFile.Height, twoLinesHeight.Height) + S(20);
                    }
                    
                    // 重新定位：文件名label保持在固定位置，文件夹label根据高度向上调整
                    int fileNameBaseTop = S(700); // 文件名label的基准位置
                    lblFileName.Top = fileNameBaseTop;
                    
                    // 文件夹label在文件名label上方，间距为S(10)
                    lblFolderName.Top = lblFileName.Top - lblFolderName.Height - S(10);
                }
            }

            // 居中对齐
            lblFolderName.Left = (this.ClientSize.Width - lblFolderName.Width) / 2;
            lblFileName.Left = (this.ClientSize.Width - lblFileName.Width) / 2;

            // 触发重绘
            lblFolderName.Invalidate();
            lblFileName.Invalidate();
        }

        private void 赛博选妃_Load(object sender, EventArgs e) { }

        private void btnHistory_Click(object? sender, EventArgs e)
        {
            if (historyFormInstance != null && !historyFormInstance.IsDisposed) { historyFormInstance.Activate(); return; }
            // --- 第一步：在此处统一声明所有关键变量 (解决报错) ---
            float sc = GetScale();
            // 当 sc=1.0 (1080p) 时，vSc=1.0
            // 当 sc=3.0 (4K) 时，vSc ≈ 1.8 (而不是 3.0)，这样字就不会傻大
            float vSc = 1.0f + (sc - 1.0f) * 0.1f;
            // 1. 先声明日历网格，方便 Paint 事件引用。padding是圆圈左右间距
            TableLayoutPanel calGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 7,
                BackColor = Color.Transparent,
                AutoSize = true,
                Padding = new Padding(S(35), S(5), S(35), S(5))
            };
            for (int i = 0; i < 7; i++) calGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.28f));

            // 2. 提前声明日期变量 [cite: 35]
            DateTime viewDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            DateTime selectedDate = DateTime.Now;

            // 3. 预加载历史记录日期到内存 (解决卡顿) 
            List<string> rds = File.Exists(historyFilePath)
                ? File.ReadAllLines(historyFilePath).Select(l => l.Split('|')[0].Substring(0, 10)).Distinct().ToList()
                : new List<string>();

            // 4. 声明委托（先设为 null，后面赋值） [cite: 35]
            Action? refreshCalendar = null;
            Action? updateData = null;
            // --- 1. 窗口初始化 ---
            historyFormInstance = new HistoryTransparentForm
            {
                Text = "宠幸起居注",
                // --- 关键补丁：直接引用主窗体的图标 ---
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                //ClientSize = new Size(340, 610),
                BackgroundImageLayout = ImageLayout.None,
                BackColor = Color.Black
            };
            
            // 暂停布局，减少初始化时的闪烁
            historyFormInstance.SuspendLayout();
            
            ApplyRescaling(historyFormInstance, 450, 820);
            // --- 2. 随机背景 (从 img/History 独立池子抽取，支持视频和静态图片) ---
            System.Windows.Media.MediaPlayer? historyMediaPlayer = null; // 历史窗口专用播放器
            System.Windows.Forms.Timer? videoTimer = null; // 视频渲染定时器
            string? histImgPath = GetRandomImageFromSubDir("History");
            if (!string.IsNullOrEmpty(histImgPath) && File.Exists(histImgPath))
            {
                try
                {
                    string ext = Path.GetExtension(histImgPath).ToLower();
                    bool isVideo = ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".wmv" || ext == ".flv";

                    if (isVideo)
                    {
                        // === 视频背景 ===
                        historyMediaPlayer = new System.Windows.Media.MediaPlayer();
                        historyMediaPlayer.Open(new Uri(histImgPath, UriKind.Absolute));
                        historyMediaPlayer.Volume = isMuted ? 0 : 1.0; // 根据设置决定音量
                        historyMediaPlayer.MediaEnded += (s, e) =>
                        {
                            historyMediaPlayer.Position = TimeSpan.Zero;
                            historyMediaPlayer.Play();
                        };

                        // 使用ScrubbingEnabled提高性能
                        historyMediaPlayer.ScrubbingEnabled = true;
                        historyMediaPlayer.Play(); // 恢复自动播放，通过焦点控制渲染

                        // 预先计算缩放参数，避免每帧重复计算
                        int targetWidth = historyFormInstance.ClientSize.Width;
                        int targetHeight = historyFormInstance.ClientSize.Height;

                        // 【性能优化】防止重复渲染的标志
                        bool isRendering = false;

                        // 【性能优化3】缓存上一帧的位置信息，避免重复计算
                        int cachedScaledWidth = 0;
                        int cachedScaledHeight = 0;
                        int cachedPosX = 0;
                        int cachedPosY = 0;
                        int cachedVideoWidth = 0;
                        int cachedVideoHeight = 0;

                        // 【性能优化12】使用定时器代替CompositionTarget.Rendering，降低CPU占用
                        videoTimer = new System.Windows.Forms.Timer();
                        videoTimer.Interval = 100; // 每100ms更新一次（10fps）
                        videoTimer.Tick += (s, e) =>
                        {
                            try
                            {
                                // 【性能优化5】防止并发渲染
                                if (isRendering || historyMediaPlayer == null ||
                                    historyMediaPlayer.NaturalVideoWidth <= 0 ||
                                    historyMediaPlayer.NaturalVideoHeight <= 0)
                                    return;

                                // 【焦点优化】只有在历史窗口有焦点时才渲染视频帧
                                if (!historyFormInstance.ContainsFocus)
                                    return;

                                // 【性能优化6】检查窗口是否可见，不可见时跳过渲染
                                if (historyFormInstance == null || !historyFormInstance.IsHandleCreated ||
                                    historyFormInstance.IsDisposed || !historyFormInstance.Visible)
                                    return;

                                isRendering = true;

                                int videoWidth = historyMediaPlayer.NaturalVideoWidth;
                                int videoHeight = historyMediaPlayer.NaturalVideoHeight;

                                // 【性能优化7】只在视频尺寸变化时重新计算缩放参数
                                if (cachedVideoWidth != videoWidth || cachedVideoHeight != videoHeight)
                                {
                                    float ratioX = (float)targetWidth / videoWidth;
                                    float ratioY = (float)targetHeight / videoHeight;
                                    float ratio = Math.Max(ratioX, ratioY);

                                    cachedScaledWidth = (int)(videoWidth * ratio);
                                    cachedScaledHeight = (int)(videoHeight * ratio);
                                    cachedPosX = (targetWidth - cachedScaledWidth) / 2;
                                    cachedPosY = (targetHeight - cachedScaledHeight) / 2;
                                    cachedVideoWidth = videoWidth;
                                    cachedVideoHeight = videoHeight;
                                }

                                // 创建视频帧
                                var drawingVisual = new System.Windows.Media.DrawingVisual();
                                using (var drawingContext = drawingVisual.RenderOpen())
                                {
                                    drawingContext.DrawVideo(historyMediaPlayer, new System.Windows.Rect(0, 0, videoWidth, videoHeight));
                                }

                                // 【性能优化8】更激进的分辨率缩减 - 最大宽度降到640（背景视频不需要太高清）
                                int renderWidth = Math.Min(videoWidth, 640);
                                int renderHeight = (int)(videoHeight * ((float)renderWidth / videoWidth));

                                var renderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                                    renderWidth,
                                    renderHeight,
                                    96, 96,
                                    System.Windows.Media.PixelFormats.Pbgra32);
                                renderBitmap.Render(drawingVisual);

                                // 【性能优化9】降低JPEG质量到50，大幅减少编码时间
                                using (var stream = new MemoryStream())
                                {
                                    var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                                    encoder.QualityLevel = 50; // 背景视频可以接受较低质量
                                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderBitmap));
                                    encoder.Save(stream);
                                    stream.Position = 0;

                                    using (var originalBitmap = new Bitmap(stream))
                                    {
                                        // 创建缩放后的图片
                                        var scaledBitmap = new Bitmap(targetWidth, targetHeight);
                                        using (Graphics g = Graphics.FromImage(scaledBitmap))
                                        {
                                            // 【性能优化10】使用最快的插值模式
                                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                                            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                                            g.DrawImage(originalBitmap, cachedPosX, cachedPosY, cachedScaledWidth, cachedScaledHeight);
                                        }

                                        // 使用BeginInvoke异步更新UI，避免阻塞渲染线程
                                        if (historyFormInstance != null && historyFormInstance.IsHandleCreated && !historyFormInstance.IsDisposed)
                                        {
                                            historyFormInstance.BeginInvoke(new Action(() =>
                                            {
                                                try
                                                {
                                                    if (historyFormInstance.BackgroundImage != null)
                                                    {
                                                        var oldImage = historyFormInstance.BackgroundImage;
                                                        historyFormInstance.BackgroundImage = null;
                                                        oldImage.Dispose();
                                                    }
                                                    historyFormInstance.BackgroundImage = scaledBitmap;
                                                    historyFormInstance.BackgroundImageLayout = ImageLayout.None;
                                                }
                                                catch { }
                                                finally
                                                {
                                                    isRendering = false;
                                                }
                                            }));
                                        }
                                        else
                                        {
                                            isRendering = false;
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                isRendering = false;
                            }
                        };

                        videoTimer.Start();

                        // 【性能优化11】窗口最小化时暂停视频播放和定时器
                        historyFormInstance.Resize += (s, e) =>
                        {
                            if (historyFormInstance.WindowState == FormWindowState.Minimized)
                            {
                                historyMediaPlayer?.Pause();
                                videoTimer.Stop();
                            }
                            else if (historyFormInstance.WindowState == FormWindowState.Normal)
                            {
                                historyMediaPlayer?.Play();
                                videoTimer.Start();
                            }
                        };
                    }
                    else
                    {
                        // === 静态图片背景 ===
                        using (FileStream fs = new FileStream(histImgPath, FileMode.Open, FileAccess.Read))
                        {
                            using (Image original = Image.FromStream(fs))
                            {
                                // 这里直接按窗口当前 ClientSize 裁切好
                                Bitmap readyBg = new Bitmap(historyFormInstance.ClientSize.Width, historyFormInstance.ClientSize.Height);
                                using (Graphics g = Graphics.FromImage(readyBg))
                                {
                                    // 调用你原来的 DrawAspectFillBackground
                                    DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                                }
                                historyFormInstance.BackgroundImage = readyBg;
                                // 静态图既然已经裁切好了，布局设为 Tile 或 Center 即可
                                historyFormInstance.BackgroundImageLayout = ImageLayout.Center;
                            }
                        }
                    }
                }
                catch { }
            }

            // 窗口关闭时清理视频播放器和定时器
            historyFormInstance.FormClosing += (s, e) =>
            {
                if (videoTimer != null)
                {
                    videoTimer.Stop();
                    videoTimer.Dispose();
                    videoTimer = null!;
                }

                if (historyMediaPlayer != null)
                {
                    historyMediaPlayer.Stop();
                    historyMediaPlayer.Close();
                    historyMediaPlayer = null!;
                }
                
                // 清除updateData引用
                historyUpdateDataAction = null;
            };

            // 【焦点控制】历史记录窗口焦点事件 - 控制视频播放和渲染
            historyFormInstance.Activated += (s, e) =>
            {
                // 历史记录窗口获得焦点时，开始播放视频和渲染
                historyMediaPlayer?.Play();
                videoTimer?.Start();
            };

            historyFormInstance.Deactivate += (s, e) =>
            {
                // 历史记录窗口失去焦点时，暂停视频播放和渲染
                historyMediaPlayer?.Pause();
                videoTimer?.Stop();
            };
            // --- 关键：解决双缓冲和自适应绘图 ---
            // 使用反射开启双缓冲（因为 historyFormInstance 是外部实例，无法直接设置属性）
            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, historyFormInstance, new object[] { true });
            // --- 在你原有的反射开启双缓冲代码下方紧接着添加 ---
            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                // 关键组合：所有绘制在内存完成 + 禁止背景擦除
                setStyleMethod.Invoke(historyFormInstance, new object[] {
        ControlStyles.AllPaintingInWmPaint |
        ControlStyles.UserPaint |
        ControlStyles.OptimizedDoubleBuffer, true
    });
            }
            // --- 定义究极版评价函数 ---
            Func<List<dynamic>, string> getRoyalDecree = (dayLogs) =>
            {
                int total = dayLogs.Count;
                int fav = dayLogs.Count(i => i.Type == 1);
                int rev = dayLogs.Count(i => i.Type == 2) + dayLogs.Count(i => i.Type == 3);
                int cold = dayLogs.Count(i => i.Type == 0);

                Random rng = new Random(Guid.NewGuid().GetHashCode());

                // --- 【优先级1：零记录 - 圣体为重】 ---
                if (total == 0)
                {
                    string[] emptySayings = { 
    "陛下修身养性，后宫春心暗许",
    "今日无宠，满园春色待君临",
    "后宫三千佳丽，个个如花似玉，陛下可要临幸？",
    "满园春色关不住，娘娘们都在等陛下宠幸",
    "玉体横陈，香肌如雪，陛下何不怜香惜玉？",
    "红袖添香夜读书，不如红袖伴君眠",
    "春宫图上美人多，不如美人在眼前",
    "温香软玉满怀抱，陛下今夜宠谁家？",
    "娇花照水，弱柳扶风，陛下可要采撷？",
    "云鬓花颜金步摇，芙蓉帐暖度春宵",
    "美人如玉剑如虹，陛下的剑何时出鞘？",
    "春心荡漾无人知，陛下可要解相思？",
    "粉面含春威不露，丹唇未启笑先闻",
    "梨花一枝春带雨，陛下可要怜花惜玉？",
    "娇羞花解语，温柔玉有香",
    "金屋藏娇娇欲语，玉楼春暖暖如酥"
                    };
                    return emptySayings[rng.Next(emptySayings.Length)];
                }

                // --- 【优先级2：特殊触发 - 根据数值进不同的高级池子】 ---

                // 1. 冷宫怨念（冷宫 > 10）
                if (cold > 10)
                {
                    string[] coldSayings = { 
    "冷宫深深深几许，娘娘们望眼欲穿",
    "宁缺毋滥，陛下要的是绝世尤物",
    "凡脂俗粉入不了陛下法眼，当寻天香国色",
    "陛下品味高雅，这些胭脂俗粉确实配不上",
    "陛下慧眼识珠，岂会被庸姿迷了眼？",
    "陛下金屋藏娇，自然要藏绝世佳人",
    "娇躯摆弄姿态万千，可惜君心不在此",
"千般媚态万种风情，终究配不上君王品味",
"骚货们使尽浑身解数，君王依然无动于衷",
"娇喘连连媚眼如丝，奈何君心如铁石",
"千娇百媚尽显风骚，终是入不了法眼",
"千般诱惑万种风情，无奈君王品味独到",
"娇喘媚笑春光无限，奈何君王不屑一顾",

                    };
                    return coldSayings[rng.Next(coldSayings.Length)];
                }
                //均衡
                if (cold <= 10 && fav >= 2 && rev >= 2 && total > 6 && Math.Abs(cold - fav) <= 3)
                {
                    string[] balanceSayings = {
        "陛下的公平，连包青天都要点赞",
        "陛下的智慧，比那诸葛亮还要高深",
        "陛下雨露均沾，三千佳丽个个得宠，真乃情圣也！",
    "环肥燕瘦各有千秋，陛下博爱众生，当真是万花丛中过",
    "陛下左拥右抱，前呼后拥，这才是帝王风范！",
    "春兰秋菊各擅胜场，陛下样样都爱，真是贪心呢",
    "千娇百媚尽收眼底，陛下这是要阅尽天下美人？",
    "春花秋月何时了，陛下的风流韵事知多少？",
    "各色佳人尽入怀中，陛下这是要坐拥天下美人？",
    "陛下进退有度，张弛有道，深得御女之术精髓",
    "温香软玉满怀抱，陛下左右开弓，当真了得",
    };
                    return balanceSayings[rng.Next(balanceSayings.Length)];
                }

                // 2. 战神降临（总数 > 20）
                if (total > 20)
                {
                    string[] warSayings = { 
    "佳人们个个香汗淋漓，娇喘连连不能自已！",
    "连天上的嫦娥都要偷偷下凡品尝龙根滋味！",
    "三千佳丽尽在胯下承欢！",
    "一夜征战千女，个个酥软如泥！",
    "美人们魂飞魄散，欲仙欲死求饶不得！",
    "翻云覆雨间让佳人们欲罢不能，春潮泛滥！",
    "让佳人们见识什么叫真正的男人！",
    "横扫花丛，让佳人们尝尽销魂滋味！",
    "三千粉黛尽在掌股之间！",
    "美人们个个魂牵梦萦，欲罢不能！",};
                    return warSayings[rng.Next(warSayings.Length)];
                }

                // 3. 念旧情深（重温+回味 > 5）
                if (rev > 5)
                {
                    string[] revSayings = {
                        "衣不如新，人不如旧，陛下深谙此道",
                        "这份深情，连嫦娥都要羡慕呢",
                        "万千佳丽如过眼云烟，唯她们如烙印般深刻",
                        "看遍天下佳丽，终觉旧爱最是销魂蚀骨",
                        "老片重温瞬间梆硬，这反应骗不了自己",
                        "万花丛中过片叶不沾，唯独伊人让人沦陷",
                        "千娇百媚见过不少，不如旧爱温香软玉",
                        "旧梦再现立马来劲，熟悉身影血脉贲张",
                        "经典重看精神抖擞，故人归来雄风再起",
                    };
                    return revSayings[rng.Next(revSayings.Length)];
                }

                //夜猫子皇帝（晚上9 点后-2点）-古风版本
                if (DateTime.Now.Hour >= 21 || DateTime.Now.Hour < 2 && rng.Next(100) < 25)
                {
                    string[] nightSayings = {
        "夜静春山空，陛下独品香茗",
    "月色如纱轻抚玉体，朦胧灯火映照香肌",
    "银烛秋光透过帘帐，若隐若现销魂身段",
    "烛火摇曳帘影婆娑，温香软玉若隐若现",
    "夜雾迷蒙灯火阑珊，纱窗半掩春光乍泄",
"银汉迢迢夜未央，花穴琼浆正当流",
"三更时分夜色浓，花洞甘泉最解渴",
"更深月色半人家，花缝甘露润唇舌",
"银烛秋光夜朦胧，桃源洞府水如酒",
"夜色如水月如钩，花洞琼浆润心田"
    };
                    return nightSayings[rng.Next(nightSayings.Length)];
                }

                // 白天摸鱼皇帝（上午5点-下午6点，30%概率触发）
                if (DateTime.Now.Hour > 5 && DateTime.Now.Hour <= 17 && rng.Next(100) < 30)
                {
                    string[] daySayings = {
        "光天化日之下，陛下好大胆",
        "日理万机之余，还要日立万姬",
        "艳阳高照热难耐，双峰甘泉最解渴",
        "饥渴难耐思甘露，玉峰双峙待君嘬",
        "口干舌燥思甘露，酥胸半露待君品",
        "烈日炎炎口舌干，最思玉乳解饥渴",
        "日头正毒人焦渴，粉嫩樱桃解君忧",
        "烈日当空口渴甚，玉峰之上有清泉",
        "骄阳炙烤汗如珠，酥胸玉液最甘甜"
    };
                    return daySayings[rng.Next(daySayings.Length)];
                }

                // 凌晨修仙者（凌晨2点-早上5点，25%概率触发）
                if (DateTime.Now.Hour >= 2 && DateTime.Now.Hour <= 5 && rng.Next(100) < 25)
                {
                    string[] dawnSayings = {
    "晨雾迷蒙若隐现，朦胧中见美人如花开",
    "鸡鸣犬吠天色明，晨光乍现春心荡漾时",
    "朝霞满天映玉颜，晨露滴滴润如酥肌肤",
    "天色微明鸟初啼，晨风轻抚温香软玉怀",
    "朝阳东升照大地，晨光透纱帐春色无边",
    "早起鸟儿有嫩逼草，晨露盈盈待君品尝",
    "日出东方红似火，晨光销魂照温柔",
"早起三光身体好，销魂一刻胜千秋"

       };
                    return dawnSayings[rng.Next(dawnSayings.Length)];
                }

                // 黄昏时光（下午7点-9点，20%概率触发）
                if (DateTime.Now.Hour > 17 && DateTime.Now.Hour < 21 && rng.Next(100) < 25)
                {
                    string[] eveningSayings = {
        "夕阳西下，陛下开始夜生活了",
        "黄昏时分，正是温柔时光",
        "夜幕降临，陛下兴致来了",
        "华灯初上，陛下准备入窗帘了",
    };
                    return eveningSayings[rng.Next(eveningSayings.Length)];
                }

                // --- 【优先级3：普通随机 - 每天的日常翻牌】 ---
                string[] normals = {
        "春宵苦短日高起，从此君王不早朝",
        "金屋藏娇，今日不知又是哪位佳人得幸？",
"后宫佳丽三千，请君慢慢品鉴",
"三千粉黛无颜色，回眸一笑百媚生",
"梨花一枝春带雨，娇羞花解语温柔",
"云鬓花颜金步摇，芙蓉帐暖度春宵",
"粉面含春威不露，丹唇未启笑先闻",
"春心荡漾无人知，正好解君相思苦",
"回眸一笑百媚生，六宫粉黛尽失色",
"娇羞花解语温柔，玉有香气袭人来",
"红袖添香夜读书，不如红袖伴君眠"

    };

                return normals[rng.Next(normals.Length)];
            };

            // --- 【性能优化】简化Paint事件，但保留日历绘制 ---
            historyFormInstance.Paint += (s, pe) =>
            {
                if (historyFormInstance.BackgroundImage == null) return;
                Graphics g = pe.Graphics;
                Image img = historyFormInstance.BackgroundImage;

                // 绘制背景 (静态图片)
                if (historyFormInstance.BackgroundImageLayout == ImageLayout.None)
                    DrawAspectFillBackground(g, img, historyFormInstance.ClientRectangle);
                else
                    g.DrawImage(img, 0, 0);

                // 绘制半透明黑色滤镜 (调整透明度为120，保持视觉统一)
                using (SolidBrush mask = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                    g.FillRectangle(mask, historyFormInstance.ClientRectangle);

                // 绘制日历圆圈和数字（始终显示）
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float sc = GetScale();

                // 【性能优化】缓存Font对象
                using (Font dayFont = new Font("Impact", 14 * vSc))
                {
                    foreach (Control ctrl in calGrid.Controls)
                    {
                        if (ctrl is Label lbl && lbl.Tag is DateTime cDate)
                        {
                            Rectangle screenRect = lbl.RectangleToScreen(lbl.ClientRectangle);
                            Rectangle clientRect = historyFormInstance.RectangleToClient(screenRect);
                            int diameter = (int)(Math.Min(clientRect.Width, clientRect.Height) * 0.9f);
                            int x = clientRect.X + (clientRect.Width - diameter) / 2;
                            int y = clientRect.Y + (clientRect.Height - diameter) / 2;
                            Rectangle rct = new Rectangle(x, y, diameter, diameter);

                            bool hasRecord = rds.Contains(cDate.ToString("yyyy-MM-dd"));
                            Color bgC = hasRecord ? Color.FromArgb(140, 255, 20, 147) : Color.FromArgb(130, 0, 0, 0);

                            using (SolidBrush sb = new SolidBrush(bgC)) g.FillEllipse(sb, rct);

                            if (cDate.Date == selectedDate.Date)
                            {
                                using (Pen pGold = new Pen(Color.Gold, 2 * sc)) g.DrawEllipse(pGold, rct);
                            }

                            // 使用Graphics.DrawString并手动调整位置以获得真正的居中
                            using (StringFormat sf = new StringFormat())
                            {
                                sf.Alignment = StringAlignment.Center;
                                sf.LineAlignment = StringAlignment.Center;
                                // 向下偏移一点以补偿字体基线
                                float offsetY = rct.Height * 0.05f;
                                g.DrawString(cDate.Day.ToString(), dayFont, Brushes.White, 
                                    new RectangleF(rct.X, rct.Y + offsetY, rct.Width, rct.Height), sf);
                            }
                        }
                    }
                }
            };


            // --- 3. UI 组件初始化 ---
            Panel navBar = new Panel { Dock = DockStyle.Top, Height = S(45), BackColor = Color.Transparent };
            Label lblMonth = new Label { Text = viewDate.ToString("yyyy - MM"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Impact", 15), Cursor = Cursors.Hand };
            NoFocusButton btnPrev = new NoFocusButton { Text = "<", Dock = DockStyle.Left, Width = S(40), FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
            NoFocusButton btnNext = new NoFocusButton { Text = ">", Dock = DockStyle.Right, Width = S(40), FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
            btnPrev.FlatAppearance.BorderSize = btnNext.FlatAppearance.BorderSize = 0;
            navBar.Controls.AddRange(new Control[] { lblMonth, btnPrev, btnNext });

            //TableLayoutPanel calGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 7, BackColor = Color.Transparent, AutoSize = true, Padding = new Padding(5) };
            for (int i = 0; i < 7; i++) calGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14.28f));

            // 使用 S() 函数自动缩放，确保 4K 下高度翻倍
            float fSc = 1.0f + (sc - 1.0f) * 0.10f;
            Label lblReport = new Label
            {
                Dock = DockStyle.Bottom,
                Height = S(80),
                BackColor = Color.FromArgb(120, 0, 0, 0),
            };
            lblReport.Paint += (s, e) =>
            {
                if (lblReport.Tag == null) return;
                string content = lblReport.Tag.ToString()!;
                //随机评价字体缩放
                float fSc = 1.0f + (sc - 1.0f) * 0.05f;
                using (Font reportFont = new Font("微软雅黑", 11 * fSc, FontStyle.Bold))
                {
                    string[] lines = content.Split('\n');
                    if (lines.Length < 2) return;

                    int rowOffset = (int)(15 * sc);

                    // 绘制第一行
                    Rectangle rect1 = new Rectangle(0, -rowOffset, lblReport.Width, lblReport.Height);
                    TextRenderer.DrawText(e.Graphics, lines[0], reportFont, rect1, Color.HotPink,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);

                    // 绘制第二行
                    Rectangle rect2 = new Rectangle(0, rowOffset, lblReport.Width, lblReport.Height);
                    TextRenderer.DrawText(e.Graphics, lines[1].Trim(), reportFont, rect2, Color.HotPink,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                }
            };
            // 【先创建背景Panel】
            Panel bgPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackgroundImageLayout = ImageLayout.None
            };

            // 绘制背景Panel
            bgPanel.Paint += (s, pe) =>
            {
                if (historyFormInstance.BackgroundImage != null)
                {
                    Rectangle panelRect = historyFormInstance.RectangleToClient(
                        bgPanel.RectangleToScreen(bgPanel.ClientRectangle)
                    );

                    pe.Graphics.DrawImage(historyFormInstance.BackgroundImage,
                        bgPanel.ClientRectangle,
                        panelRect,
                        GraphicsUnit.Pixel);

                    // 绘制半透明遮罩 (统一调整透明度为120)
                    using (SolidBrush mask = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                        pe.Graphics.FillRectangle(mask, bgPanel.ClientRectangle);
                }
            };

            // 【ListView 替换 FlowLayoutPanel - 彻底解决画面撕裂】
            ListView listView = new ListView
            {
                Dock = DockStyle.Fill, // 改回Dock.Fill，自动填满
                BackColor = Color.Black,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                View = View.Details,
                OwnerDraw = true,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.None,
                MultiSelect = false,
                Activation = ItemActivation.TwoClick, // 改为双击，避免自动显示小手
                Scrollable = true
            };

            // 把ListView放到bgPanel上
            bgPanel.Controls.Add(listView);

            // 添加一个占满宽度的列
            listView.Columns.Add("", listView.ClientSize.Width);

            // 启用双缓冲
            typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(listView, new object[] {
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint,
                    true
                });

            // 【关键】绘制底部空白区域
            listView.Paint += (s, pe) =>
            {
                if (historyFormInstance.BackgroundImage == null) return;
                if (listView.Items.Count == 0) return;

                // 计算最后一个Item的底部
                var lastItem = listView.Items[listView.Items.Count - 1];
                int itemsBottom = lastItem.Bounds.Bottom;

                // 如果有空白区域
                if (itemsBottom < listView.ClientRectangle.Bottom)
                {
                    Rectangle emptyRect = new Rectangle(
                        0,
                        itemsBottom,
                        listView.ClientRectangle.Width,
                        listView.ClientRectangle.Bottom - itemsBottom
                    );

                    Rectangle listRect = historyFormInstance.RectangleToClient(
                        listView.RectangleToScreen(emptyRect)
                    );

                    pe.Graphics.DrawImage(historyFormInstance.BackgroundImage,
                        emptyRect,
                        listRect,
                        GraphicsUnit.Pixel);

                    using (SolidBrush mask = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                        pe.Graphics.FillRectangle(mask, emptyRect);
                }
            };

            // 【关键】DrawColumnHeader 事件 - 不绘制列头
            listView.DrawColumnHeader += (s, e) =>
            {
                // 不绘制列头，保持透明
            };

            // 【滚动条处理】
            bool isScrollEnabled = true; // 标记是否允许滚动
            int lastRealItemIndex = -1; // 最后一个真实Item的索引

            // 【关键】添加MouseWheel事件，确保滚轮可以滚动
            listView.MouseWheel += (s, e) =>
            {
                if (!isScrollEnabled || listView.Items.Count == 0) return;

                // 计算滚动方向和距离
                int delta = e.Delta / 120; // 每次滚动的单位
                int scrollAmount = delta * 3; // 每次滚动3个Item

                if (listView.TopItem != null)
                {
                    int currentIndex = listView.TopItem.Index;
                    int newIndex = currentIndex - scrollAmount; // 向上滚是正数，向下滚是负数

                    // 如果有真实Item，限制滚动范围
                    if (lastRealItemIndex >= 0)
                    {
                        // 计算最大可滚动的索引
                        int maxScrollIndex = Math.Max(0, lastRealItemIndex);
                        newIndex = Math.Max(0, Math.Min(maxScrollIndex, newIndex));

                        // 【限制滚动】如果滚动到最底部，回滚一个item
                        if (newIndex >= lastRealItemIndex && delta < 0) // delta < 0 表示向下滚动
                        {
                            newIndex = Math.Max(0, lastRealItemIndex - 1);
                        }
                    }
                    else
                    {
                        newIndex = Math.Max(0, Math.Min(listView.Items.Count - 1, newIndex));
                    }

                    if (newIndex != currentIndex && newIndex < listView.Items.Count)
                    {
                        listView.TopItem = listView.Items[newIndex];
                    }
                }
            };

            // 【关键】添加持续监控Timer，强制隐藏滚动条
            System.Windows.Forms.Timer hideScrollBarTimer = new System.Windows.Forms.Timer { Interval = 200 };
            hideScrollBarTimer.Tick += (s, e) =>
            {
                if (listView.IsHandleCreated && !listView.IsDisposed)
                {
                    ShowScrollBar(listView.Handle, 0, false);
                    ShowScrollBar(listView.Handle, SB_VERT, false);
                }
            };
            hideScrollBarTimer.Start();

            listView.HandleCreated += (s, e) =>
            {
                ShowScrollBar(listView.Handle, 0, false); // 永久隐藏横向
                ShowScrollBar(listView.Handle, SB_VERT, false); // 隐藏垂直（用按钮替代）
            };

            // 确保横向和垂直滚动条始终隐藏
            listView.Resize += (s, e) =>
            {
                if (listView.IsHandleCreated)
                {
                    ShowScrollBar(listView.Handle, 0, false);
                    ShowScrollBar(listView.Handle, SB_VERT, false);
                }
            };

            historyFormInstance.Shown += (s, e) =>
            {
                // 窗口显示时强制隐藏滚动条
                if (listView.IsHandleCreated)
                {
                    ShowScrollBar(listView.Handle, 0, false);
                    ShowScrollBar(listView.Handle, SB_VERT, false);
                }
            };

            // --- 4. 核心逻辑 ---
            updateData = () =>
            {
                listView.BeginUpdate(); // 批量更新开始
                listView.Items.Clear();

                if (!File.Exists(historyFilePath))
                {
                    // 【关键】文件不存在时：禁用滚动，显示"今日未曾临幸"
                    listView.Scrollable = false;
                    isScrollEnabled = false;

                    // 先添加"今日未曾临幸"在顶部
                    var emptyItem = new ListViewItem("—— 今日未曾临幸 ——");
                    emptyItem.Tag = null;
                    emptyItem.ImageIndex = 0;
                    listView.Items.Add(emptyItem);

                    // 计算需要多少填充Item填满剩余空间
                    int listViewHeight = listView.ClientSize.Height;
                    // 根据屏幕分辨率动态调整填充倍数：4K屏需要更多填充项
                    int fillMultiplier = GetListViewFillMultiplier();
                    int fillerCount = (int)Math.Ceiling((double)listViewHeight / (90 * (1.0f + (sc - 1.0f) * 1.2f))) * fillMultiplier;

                    // 在底部添加填充Item
                    for (int i = 0; i < fillerCount; i++)
                    {
                        var fillerItem = new ListViewItem("");
                        fillerItem.Tag = "FILLER";
                        fillerItem.ImageIndex = 0;
                        listView.Items.Add(fillerItem);
                    }

                    lastRealItemIndex = -1;
                    listView.EndUpdate();
                    return;
                }

                string target = selectedDate.ToString("yyyy-MM-dd");
                
                // 读取统一的历史记录文件（包含视频和图片）
                var allLogs = new List<dynamic>();
                if (File.Exists(historyFilePath))
                {
                    allLogs = File.ReadAllLines(historyFilePath)
                        .Select((line, index) => new { Line = line, Index = index })
                        .Where(x => x.Line.StartsWith(target))
                        .Select(x =>
                        {
                            string[] p = x.Line.Split('|');
                            string fileName = p.Length > 2 ? p[2] : "";
                            // 根据文件扩展名判断是图片还是视频
                            string ext = Path.GetExtension(fileName).ToLower();
                            bool isImage = ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".bmp" || ext == ".webp";
                            
                            return new
                            {
                                Time = p[0],
                                Folder = p[1],
                                File = fileName,
                                Type = p.Length > 3 ? int.Parse(p[3]) : 0,
                                Path = p.Length > 4 ? p[4] : workPath,
                                Index = x.Index,
                                IsImage = isImage
                            };
                        })
                        .Cast<dynamic>()
                        .ToList();
                }
                
                // 按时间降序，时间相同时按行号降序（行号越大越新）
                var logs = allLogs
                    .OrderByDescending(x => {
                        DateTime dt;
                        return DateTime.TryParse((string)x.Time, out dt) ? dt : DateTime.MinValue;
                    })
                    .ThenByDescending(x => (int)x.Index)
                    .Cast<dynamic>()
                    .ToList();

                // 调用评价函数
                string currentSaying = getRoyalDecree(logs.Cast<dynamic>().ToList());

                int fav = logs.Count(i => i.Type == 1);
                int rev = logs.Count(i => i.Type == 2) + logs.Count(i => i.Type == 3);
                int cold = logs.Count(i => i.Type == 0);

                // 更新底部报告文本
                string reportContent = $"冷宫:{cold} | 宠幸:{fav} | 重温:{logs.Count(i => i.Type == 2)} | 回味:{logs.Count(i => i.Type == 3)} | 总计:{logs.Count}\n{currentSaying}";
                lblReport.Tag = reportContent;
                lblReport.Invalidate();

                // 计算卡片高度
                float itemVSc = 1.0f + (sc - 1.0f) * 1.2f;
                int itemHeight = (int)(90 * itemVSc); // 增加高度以容纳间距

                // 设置行高
                ImageList imgList = new ImageList();
                imgList.ImageSize = new Size(1, Math.Min(itemHeight, 256));
                listView.SmallImageList = imgList;

                // 添加列表项
                if (!logs.Any())
                {
                    // 【关键】空记录时：禁用滚动
                    listView.Scrollable = false;
                    isScrollEnabled = false; // 禁用滚动标记

                    // 先添加"今日未曾临幸"在顶部
                    var emptyItem = new ListViewItem("—— 今日未曾临幸 ——");
                    emptyItem.Tag = null;
                    emptyItem.ImageIndex = 0;
                    listView.Items.Add(emptyItem);

                    // 计算需要多少填充Item填满剩余空间（增加额外的填充项以应对滚动）
                    int listViewHeight = listView.ClientSize.Height;
                    // 根据屏幕分辨率动态调整填充倍数：4K屏需要更多填充项
                    int fillMultiplier = GetListViewFillMultiplier();
                    int fillerCount = (int)Math.Ceiling((double)listViewHeight / itemHeight) * fillMultiplier;

                    // 在底部添加填充Item
                    for (int i = 0; i < fillerCount; i++)
                    {
                        var fillerItem = new ListViewItem("");
                        fillerItem.Tag = "FILLER";
                        fillerItem.ImageIndex = 0;
                        listView.Items.Add(fillerItem);
                    }
                }
                else
                {
                    // 有记录时启用滚动
                    listView.Scrollable = true;
                    isScrollEnabled = true; // 启用滚动标记

                    foreach (var log in logs)
                    {
                        var item = new ListViewItem();
                        item.Tag = log;
                        item.ImageIndex = 0;
                        listView.Items.Add(item);
                    }

                    // 【关键】添加填充Item填满ListView高度（增加额外的填充项以应对滚动）
                    int listViewHeight = listView.ClientSize.Height;
                    // 根据屏幕分辨率动态调整填充倍数：4K屏需要更多填充项
                    int fillMultiplier = GetListViewFillMultiplier();
                    int fillerCount = (int)Math.Ceiling((double)listViewHeight / itemHeight) * fillMultiplier;

                    for (int i = 0; i < fillerCount; i++)
                    {
                        var fillerItem = new ListViewItem("");
                        fillerItem.Tag = "FILLER";
                        fillerItem.ImageIndex = 0;
                        listView.Items.Add(fillerItem);
                    }
                }

                listView.EndUpdate();

                // 【关键】记录最后一个真实Item的索引，用于滚动限制
                if (isScrollEnabled && logs.Any())
                {
                    lastRealItemIndex = logs.Count() - 1;
                }
                else
                {
                    lastRealItemIndex = -1;
                }

                // 确保横向和垂直滚动条隐藏
                if (listView.IsHandleCreated)
                {
                    ShowScrollBar(listView.Handle, 0, false);
                    ShowScrollBar(listView.Handle, SB_VERT, false);
                }
            };

            // ListView 自定义绘制事件
            listView.DrawItem += (s, e) =>
            {
                float fSc = 1.0f + (sc - 1.0f) * 0.10f;
                float itemVSc = 1.0f + (sc - 1.0f) * 1.2f;
                int itemHeight = (int)(90 * itemVSc);

                // 【关键】所有Item都先绘制背景
                if (historyFormInstance.BackgroundImage != null)
                {
                    Rectangle listRect = historyFormInstance.RectangleToClient(listView.RectangleToScreen(e.Bounds));
                    e.Graphics.DrawImage(historyFormInstance.BackgroundImage,
                        e.Bounds,
                        listRect,
                        GraphicsUnit.Pixel);

                    // 绘制半透明遮罩 (统一调整透明度为120)
                    using (SolidBrush mask = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                        e.Graphics.FillRectangle(mask, e.Bounds);
                }

                // 【填充Item】只绘制背景，不绘制内容
                if (e.Item.Tag is string && (string)e.Item.Tag == "FILLER")
                {
                    return; // 只绘制背景，不绘制其他内容
                }

                // 空记录：绘制文字
                if (e.Item.Tag == null)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Font emptyFont = new Font("微软雅黑", 13 * fSc, FontStyle.Bold);
                    TextRenderer.DrawText(e.Graphics, "—— 今日未曾临幸 ——", emptyFont, e.Bounds, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    emptyFont.Dispose();
                    return;
                }

                // 获取数据
                dynamic log = e.Item.Tag;
                string typeStr = log.Type switch { 1 => "[宠幸]", 2 => "[重温]", 3 => "[回味]", _ => "[冷宫]" };
                Color tc = log.Type switch { 1 => Color.HotPink, 2 => Color.DeepSkyBlue, 3 => Color.MediumPurple, _ => Color.White };

                // 绘制圆角卡片
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                // 使用固定的左右边距，确保视觉统一
                int horizontalMargin = S(15);
                Rectangle cardRect = new Rectangle(e.Bounds.X + horizontalMargin, e.Bounds.Y + S(5), e.Bounds.Width - (horizontalMargin * 2), e.Bounds.Height - S(10));

                using (var path = new GraphicsPath())
                {
                    int d = S(30);
                    path.AddArc(cardRect.X, cardRect.Y, d, d, 180, 90);
                    path.AddArc(cardRect.Right - d, cardRect.Y, d, d, 270, 90);
                    path.AddArc(cardRect.Right - d, cardRect.Bottom - d, d, d, 0, 90);
                    path.AddArc(cardRect.X, cardRect.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();

                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) // 统一调整透明度为120
                        e.Graphics.FillPath(sb, path);
                    using (Pen p = new Pen(Color.FromArgb(180, tc), 1))
                        e.Graphics.DrawPath(p, path);
                }

                // 绘制收藏按钮（右侧）
                int favButtonSize = S(30);
                int favButtonMargin = S(10);
                Rectangle favButtonRect = new Rectangle(
                    cardRect.Right - favButtonSize - favButtonMargin,
                    cardRect.Y + (cardRect.Height - favButtonSize) / 2,
                    favButtonSize,
                    favButtonSize
                );

                // 存储按钮位置到 Item.Tag 中，用于点击检测
                e.Item.SubItems.Clear();
                e.Item.SubItems.Add(favButtonRect.ToString());

                // 获取视频完整路径
                string vp = log.Path;
                if (!vp.EndsWith(log.File, StringComparison.OrdinalIgnoreCase))
                    vp = Path.Combine(log.Path, log.File);

                // 检查是否已收藏
                bool isFavorite = favoritesManager.IsFavorite(vp);

                // 尝试加载自定义图标，如果失败则使用默认图标
                Image? favIcon = null;
                string favIconPath = isFavorite ? "config/fav.png" : "config/unfav.png";
                if (File.Exists(favIconPath))
                {
                    try
                    {
                        favIcon = Image.FromFile(favIconPath);
                    }
                    catch { }
                }

                if (favIcon != null)
                {
                    // 绘制自定义图标
                    e.Graphics.DrawImage(favIcon, favButtonRect);
                    favIcon.Dispose();
                }
                else
                {
                    // 绘制默认图标（星星）
                    Font starFont = new Font("Segoe UI Symbol", 16 * fSc, FontStyle.Regular);
                    string starText = isFavorite ? "★" : "☆";
                    Color starColor = isFavorite ? Color.Gold : Color.Gray;
                    TextRenderer.DrawText(e.Graphics, starText, starFont, favButtonRect, starColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    starFont.Dispose();
                }

                // 绘制文字（调整右边距以避免与收藏按钮重叠）
                Font cardFont = new Font("微软雅黑", 11 * fSc, FontStyle.Bold);

                // 根据是否是图片决定显示顺序
                string line1, line2;
                if (log.IsImage)
                {
                    // 图片模式：第一行显示文件名，第二行显示文件夹名
                    line1 = $"{log.Time.Substring(11, 5)} {typeStr} {log.File}";
                    line2 = log.Folder;
                }
                else
                {
                    // 视频模式：第一行显示文件夹名，第二行显示文件名
                    line1 = $"{log.Time.Substring(11, 5)} {typeStr} {log.Folder}";
                    line2 = log.File;
                }

                int leftMargin = S(25);
                int rightMargin = favButtonSize + favButtonMargin * 2; // 为收藏按钮留出空间

                Rectangle rect1 = new Rectangle(
                    cardRect.X + leftMargin,
                    cardRect.Y + S(15),
                    cardRect.Width - leftMargin - rightMargin,
                    cardRect.Height / 2
                );
                TextRenderer.DrawText(e.Graphics, line1, cardFont, rect1, tc,
                    TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                Rectangle rect2 = new Rectangle(
                    cardRect.X + leftMargin,
                    cardRect.Y + cardRect.Height / 2 + S(5),
                    cardRect.Width - leftMargin - rightMargin,
                    cardRect.Height / 2 - S(15)
                );
                TextRenderer.DrawText(e.Graphics, line2, cardFont, rect2, tc,
                    TextFormatFlags.Top | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

                cardFont.Dispose();
            };

            // 【关键】使用 ImageList 设置行高
            // DrawColumnHeader 已在上面定义，不需要重复

            // 在窗口创建后立即设置 ImageList
            float itemVSc = 1.0f + (sc - 1.0f) * 1.2f;
            int itemHeight = (int)(90 * itemVSc);
            ImageList imgList = new ImageList();
            imgList.ImageSize = new Size(1, itemHeight);
            listView.SmallImageList = imgList;

            // ListView 点击事件 - 使用MouseClick代替ItemActivate，避免需要双击
            listView.MouseClick += (s, e) =>
            {
                // 只处理左键单击
                if (e.Button != MouseButtons.Left) return;

                var hitTest = listView.HitTest(e.Location);
                if (hitTest.Item == null || hitTest.Item.Tag == null) return;

                // 【关键】忽略填充Item的点击
                if (hitTest.Item.Tag is string && (string)hitTest.Item.Tag == "FILLER")
                    return;

                dynamic log = hitTest.Item.Tag;
                string vp = log.Path;
                if (!vp.EndsWith(log.File, StringComparison.OrdinalIgnoreCase))
                    vp = Path.Combine(log.Path, log.File);

                // 检查是否点击了收藏按钮
                if (hitTest.Item.SubItems.Count > 1)
                {
                    string rectStr = hitTest.Item.SubItems[1].Text;
                    if (!string.IsNullOrEmpty(rectStr))
                    {
                        try
                        {
                            // 解析按钮矩形
                            string[] parts = rectStr.Replace("{X=", "").Replace("Y=", "").Replace("Width=", "")
                                .Replace("Height=", "").Replace("}", "").Split(',');
                            if (parts.Length == 4)
                            {
                                int btnX = int.Parse(parts[0]);
                                int btnY = int.Parse(parts[1]);
                                int btnW = int.Parse(parts[2]);
                                int btnH = int.Parse(parts[3]);
                                Rectangle favButtonRect = new Rectangle(btnX, btnY, btnW, btnH);

                                // 转换点击位置到ListView坐标
                                Point clickPos = e.Location;

                                // 检查是否点击在收藏按钮区域
                                if (favButtonRect.Contains(clickPos))
                                {
                                    // 切换收藏状态
                                    favoritesManager.ToggleFavorite(vp);
                                    favoritesManager.SaveToConfig();

                                    // 刷新显示
                                    listView.Invalidate(hitTest.Item.Bounds);
                                    return; // 不播放视频
                                }
                            }
                        }
                        catch { }
                    }
                }

                // 点击卡片其他区域：播放视频或打开图片
                if (File.Exists(vp))
                {
                    this.currentVideoPath = vp;
                    this.displayFolderName = log.Folder;
                    this.displayFileName = log.File;
                    this.UpdateLabelLayout();
                    this.lblFolderName.Invalidate();
                    this.lblFileName.Invalidate();
                    Process.Start(new ProcessStartInfo(vp) { UseShellExecute = true });
                    
                    // 根据是图片还是视频记录到不同的历史文件
                    if (log.IsImage)
                    {
                        imageHistoryManager.RecordView(log.Folder, log.File, 2, vp);
                        RecordSelectedRootPathUsage(vp);
                    }
                    else
                    {
                        RecordHistory(log.Folder, log.File, 2);
                    }
                    
                    updateData();
                }
                else
                {
                    // 文件不存在时的提示
                    MessageBox.Show($"陛下，此佳人已不知所踪！\n\n【{log.Folder}】\n{log.File}\n\n或许已移居他处，或已香消玉殒...",
                        "佳人失踪", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            // 【关键】鼠标移动时，根据Item类型改变光标（防止闪烁）
            ListViewItem? lastHoverItem = null;
            listView.MouseMove += (s, e) =>
            {
                var hitTest = listView.HitTest(e.Location);

                // 只在Item改变时更新光标，避免频繁切换
                if (hitTest.Item != lastHoverItem)
                {
                    lastHoverItem = hitTest.Item;

                    if (hitTest.Item != null)
                    {
                        // 如果是填充Item或空记录，显示默认光标
                        if ((hitTest.Item.Tag is string && (string)hitTest.Item.Tag == "FILLER") ||
                            hitTest.Item.Tag == null)
                        {
                            listView.Cursor = Cursors.Default;
                        }
                        else
                        {
                            listView.Cursor = Cursors.Hand;
                        }
                    }
                    else
                    {
                        listView.Cursor = Cursors.Default;
                    }
                }
            };

            // 动态设置列宽，确保横向滚动条不出现
            listView.Resize += (s, e) =>
            {
                if (listView.Columns.Count > 0)
                    listView.Columns[0].Width = listView.ClientSize.Width;

                // 强制隐藏横向滚动条
                if (listView.IsHandleCreated)
                    ShowScrollBar(listView.Handle, 0, false);
            };

            refreshCalendar = () =>
            {
                calGrid.SuspendLayout();
                calGrid.Controls.Clear();
                lblMonth.Text = viewDate.ToString("yyyy - MM");

                // 1. 更新内存数据（rds），确保点击翻页时数据也是最新的 
                rds = File.Exists(historyFilePath)
                    ? File.ReadAllLines(historyFilePath).Select(l => l.Split('|')[0].Substring(0, 10)).Distinct().ToList()
                    : new List<string>();

                int offset = ((int)new DateTime(viewDate.Year, viewDate.Month, 1).DayOfWeek + 6) % 7;
                for (int i = 0; i < 42; i++)
                {
                    int dNum = i - offset + 1;
                    if (dNum > 0 && dNum <= DateTime.DaysInMonth(viewDate.Year, viewDate.Month))
                    {
                        DateTime cDate = new DateTime(viewDate.Year, viewDate.Month, dNum);

                        // 2. 【关键】：创建一个全透明的 Label，不写 Text，不写 Paint 事件 [cite: 95, 96]
                        // 它现在只负责：1. 撑开格子；2. 接收点击；3. 给主窗体 Paint 提供位置参考
                        Label lbl = new Label
                        {
                            Text = "", // 必须为空，数字由主窗体画 
                            Size = new Size(S(45), S(45)),
                            BackColor = Color.Transparent,
                            Cursor = Cursors.Hand,
                            Tag = cDate // 存入日期，供主窗体 Paint 读取 
                        };

                        // 3. 点击事件保持逻辑 
                        lbl.Click += (ss, eee) =>
                        {
                            selectedDate = cDate;
                            refreshCalendar?.Invoke();
                            updateData?.Invoke();
                        };

                        calGrid.Controls.Add(lbl, i % 7, i / 7);
                    }
                }
                calGrid.ResumeLayout();

                // 4. 【核心】：强制通知主窗体重绘日历区域
                historyFormInstance.Invalidate();
                calGrid.Height = (calGrid.RowCount) * S(45); // 设置合适的行高
            };

            // --- 5. 组装 (注意 Add 顺序决定了谁覆盖谁) ---
            historyFormInstance.Controls.Add(bgPanel);   // 先加背景Panel（包含listView）
            historyFormInstance.Controls.Add(calGrid);     // 再加 Top 的日历网格
            historyFormInstance.Controls.Add(navBar);      // 再加 Top 的导航栏
            historyFormInstance.Controls.Add(lblReport);   // 最后加 Bottom 的

            // 年份选择
            lblMonth.Click += (s, ee) =>
            {
                Form fYear = new HistoryTransparentForm { Text = "选年", Size = new Size(S(220), S(130)), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedToolWindow };
                NumericUpDown num = new NumericUpDown { Left = S(20), Top = S(12), Width = S(150), Height = S(32), AutoSize = false, Font = new Font("微软雅黑", 12), Minimum = 1900, Maximum = 2100, Value = viewDate.Year };
                num.Click += (ss, ee) => { }; // 防止点选年份时触发背景双击
                NoFocusButton b = new NoFocusButton { Text = "确定", Left = S(60), Top = S(58), Width = S(90), Height = S(32), Font = new Font("微软雅黑", 10, FontStyle.Bold), DialogResult = DialogResult.OK };
                NoFocusButton btnCancel = new NoFocusButton { Text = "取消", Left = S(60), Top = S(94), Width = S(90), Height = S(28), Font = new Font("微软雅黑", 9), DialogResult = DialogResult.Cancel };
                fYear.AcceptButton = b;
                fYear.CancelButton = btnCancel;
                fYear.Shown += (ss, eee) => { num.Focus(); num.Select(0, num.Text.Length); };
                fYear.Controls.AddRange(new Control[] { num, b, btnCancel });
                if (fYear.ShowDialog() == DialogResult.OK) { viewDate = new DateTime((int)num.Value, viewDate.Month, 1); refreshCalendar(); }
            };

            btnPrev.Click += (s, ee) => { viewDate = viewDate.AddMonths(-1); refreshCalendar(); };
            btnNext.Click += (s, ee) => { viewDate = viewDate.AddMonths(1); refreshCalendar(); };


            refreshCalendar(); updateData();
            
            // 保存updateData引用，以便主界面可以刷新历史窗口
            historyUpdateDataAction = updateData;
            
            // 恢复布局并显示窗口
            historyFormInstance.ResumeLayout(false);
            historyFormInstance.PerformLayout();
            
            historyFormInstance.Show();
        }


        // --- 这一个是万能的，把它当成您的“按钮模板” ---
        public class NoFocusButton : System.Windows.Forms.Button
        {
            public NoFocusButton()
            {
                this.TabStop = false; // 严禁抢夺焦点
                this.FlatStyle = FlatStyle.Flat; // 强制扁平化
            }
            // 关键：直接掐断虚框绘制的神经
            protected override bool ShowFocusCues => false;
        }

        private void BtnRandomLady_Click(object? sender, EventArgs e)
        {
            // ==================== Path Validation ====================
            // Check if using new multi-path system or legacy single path
            List<string> enabledPaths = pathManager.GetEnabledPaths();

            if (enabledPaths.Count == 0)
            {
                ShowUserFriendlyError(
                    "未配置图片路径",
                    "请先在配置中启用至少一个路径。\n\n建议操作：\n1. 点击「选择路径」按钮\n2. 启用至少一个路径\n3. 保存设置后重试"
                );
                return;
            }

            try
            {
                // Create ImageSelector with all managers
                ImageSelector imageSelector = new ImageSelector(
                    pathManager,
                    modeManager,
                    imageHistoryManager,
                    favoritesManager,
                    blackWhiteListManager
                );

                // Select image using the new system
                string? selectedImage = imageSelector.SelectImage();

                // ==================== Handle Selection Result ====================
                if (string.IsNullOrEmpty(selectedImage))
                {
                    ShowUserFriendlyError(
                        "无法选择图片",
                        "未能找到符合当前模式和过滤条件的图片。\n\n建议操作：\n1. 检查路径配置\n2. 调整模式设置\n3. 减少排除规则\n4. 启用更多路径"
                    );
                    return;
                }

                // ==================== Validate Selected Image ====================
                if (!File.Exists(selectedImage))
                {
                    ShowUserFriendlyError(
                        "图片文件不存在",
                        "选中的图片文件不存在或已被删除。\n\n将尝试重新选择..."
                    );
                    // Retry once
                    BtnRandomLady_Click(sender, e);
                    return;
                }

                // ==================== Update UI with Selected Image ====================
                currentVideoPath = selectedImage;
                displayFolderName = GetSmartFolderName(Path.GetDirectoryName(selectedImage));
                displayFileName = Path.GetFileName(selectedImage);

                UpdateLabelLayout();
                lblFolderName.Invalidate();
                lblFileName.Invalidate();

                // ==================== Open Image and Record History ====================
                try
                {
                    Process.Start(new ProcessStartInfo(selectedImage) { UseShellExecute = true });

                    // Record to history using ImageHistoryManager
                    imageHistoryManager.RecordView(displayFolderName, displayFileName, 0, selectedImage);
                    RecordSelectedRootPathUsage(selectedImage);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    ShowUserFriendlyError(
                        "无法打开图片",
                        $"无法打开图片文件。\n\n文件：{displayFileName}\n\n建议操作：\n1. 检查是否安装了图片查看器\n2. 检查文件关联设置\n3. 尝试手动打开文件"
                    );
                }
                catch (UnauthorizedAccessException)
                {
                    ShowUserFriendlyError(
                        "访问被拒绝",
                        $"没有权限访问图片文件。\n\n文件：{displayFileName}\n\n建议操作：\n1. 检查文件权限\n2. 以管理员身份运行程序"
                    );
                }
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error in image selection: {ex.Message}\n{ex.StackTrace}");
                ShowUserFriendlyError(
                    "发生错误",
                    $"图片选择过程中发生错误。\n\n错误信息：{ex.Message}\n\n建议操作：\n1. 检查配置设置\n2. 重启应用程序\n3. 查看日志文件获取详细信息"
                );
            }
        }


        private void btnAnnualReport_Click(object? sender, EventArgs e)
                {
                    if (annualReportFormInstance != null && !annualReportFormInstance.IsDisposed)
                    {
                        RequestAnnualReportRefresh(true);
                        annualReportFormInstance.Activate();
                        return;
                    }

                    if (!Directory.Exists(imgDir)) Directory.CreateDirectory(imgDir);

                    // 重置随机索引，确保每次打开窗口都会重新随机
                    peakHourRandomIndex = -1;

                    annualReportFormInstance = new HistoryTransparentForm
                    {
                        Text = "年度报告",
                        Icon = this.Icon,
                        FormBorderStyle = FormBorderStyle.FixedSingle,
                        MaximizeBox = false,
                        MinimizeBox = false,
                        StartPosition = FormStartPosition.CenterParent,
                        BackColor = Color.Black,
                        AutoScroll = false
                    };

                    // 暂停布局，减少初始化时的闪烁
                    annualReportFormInstance.SuspendLayout();

                    // 使用ApplyRescaling设置窗口尺寸（支持DPI缩放）
                    ApplyRescaling(annualReportFormInstance, 1400, 900);

                    // --- 背景逻辑（年度报告主窗口支持视频背景，绑定窗口保持静态）---
                    System.Windows.Media.MediaPlayer? annualReportMediaPlayer = null;
                    System.Windows.Forms.Timer? annualReportVideoTimer = null;
                    string? reportImgPath = GetRandomImageFromSubDir("Report");
                    if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
                    {
                        try
                        {
                            string ext = Path.GetExtension(reportImgPath).ToLower();
                            bool isVideo = ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".wmv" || ext == ".flv";

                            if (isVideo)
                            {
                                annualReportMediaPlayer = new System.Windows.Media.MediaPlayer();
                                annualReportMediaPlayer.Open(new Uri(reportImgPath, UriKind.Absolute));
                                annualReportMediaPlayer.Volume = isMuted ? 0 : 1.0;
                                annualReportMediaPlayer.MediaEnded += (s, e) =>
                                {
                                    if (annualReportMediaPlayer == null) return;
                                    annualReportMediaPlayer.Position = TimeSpan.Zero;
                                    annualReportMediaPlayer.Play();
                                };
                                annualReportMediaPlayer.ScrubbingEnabled = true;
                                annualReportMediaPlayer.Play();

                                int targetWidth = annualReportFormInstance.ClientSize.Width;
                                int targetHeight = annualReportFormInstance.ClientSize.Height;
                                bool isRendering = false;

                                annualReportVideoTimer = new System.Windows.Forms.Timer();
                                annualReportVideoTimer.Interval = 100;
                                annualReportVideoTimer.Tick += (s, e) =>
                                {
                                    try
                                    {
                                        if (isRendering || annualReportMediaPlayer == null ||
                                            annualReportMediaPlayer.NaturalVideoWidth <= 0 ||
                                            annualReportMediaPlayer.NaturalVideoHeight <= 0)
                                            return;

                                        if (annualReportFormInstance == null || !annualReportFormInstance.IsHandleCreated ||
                                            annualReportFormInstance.IsDisposed || !annualReportFormInstance.Visible)
                                            return;

                                        isRendering = true;

                                        int videoWidth = annualReportMediaPlayer.NaturalVideoWidth;
                                        int videoHeight = annualReportMediaPlayer.NaturalVideoHeight;

                                        var drawingVisual = new System.Windows.Media.DrawingVisual();
                                        using (var drawingContext = drawingVisual.RenderOpen())
                                        {
                                            drawingContext.DrawVideo(annualReportMediaPlayer, new System.Windows.Rect(0, 0, videoWidth, videoHeight));
                                        }

                                        var renderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                                            videoWidth,
                                            videoHeight,
                                            96, 96,
                                            System.Windows.Media.PixelFormats.Pbgra32);
                                        renderBitmap.Render(drawingVisual);

                                        using (var stream = new MemoryStream())
                                        {
                                            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                                            encoder.QualityLevel = 75;
                                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(renderBitmap));
                                            encoder.Save(stream);
                                            stream.Position = 0;

                                            using (var renderedFrame = new Bitmap(stream))
                                            {
                                                float ratio = Math.Max((float)targetWidth / renderedFrame.Width,
                                                                      (float)targetHeight / renderedFrame.Height);
                                                int newWidth = (int)(renderedFrame.Width * ratio);
                                                int newHeight = (int)(renderedFrame.Height * ratio);
                                                int posX = (targetWidth - newWidth) / 2;
                                                int posY = (targetHeight - newHeight) / 2;

                                                var scaledBitmap = new Bitmap(targetWidth, targetHeight);
                                                using (Graphics g = Graphics.FromImage(scaledBitmap))
                                                {
                                                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                                                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                                                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                                                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;
                                                    g.DrawImage(renderedFrame, posX, posY, newWidth, newHeight);
                                                }

                                                if (annualReportFormInstance != null && annualReportFormInstance.IsHandleCreated && !annualReportFormInstance.IsDisposed)
                                                {
                                                    annualReportFormInstance.BeginInvoke(new Action(() =>
                                                    {
                                                        try
                                                        {
                                                            if (annualReportFormInstance.BackgroundImage != null)
                                                            {
                                                                var oldImage = annualReportFormInstance.BackgroundImage;
                                                                annualReportFormInstance.BackgroundImage = null;
                                                                oldImage.Dispose();
                                                            }
                                                            annualReportFormInstance.BackgroundImage = scaledBitmap;
                                                            annualReportFormInstance.BackgroundImageLayout = ImageLayout.None;
                                                        }
                                                        catch { }
                                                        finally
                                                        {
                                                            isRendering = false;
                                                        }
                                                    }));
                                                }
                                                else
                                                {
                                                    scaledBitmap.Dispose();
                                                    isRendering = false;
                                                }
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        isRendering = false;
                                    }
                                };
                                annualReportVideoTimer.Start();
                            }
                            else
                            {
                                using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                                {
                                    using (Image original = Image.FromStream(fs))
                                    {
                                        Bitmap readyBg = new Bitmap(annualReportFormInstance.ClientSize.Width, annualReportFormInstance.ClientSize.Height);
                                        using (Graphics g = Graphics.FromImage(readyBg))
                                        {
                                            DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                                        }
                                        annualReportFormInstance.BackgroundImage = readyBg;
                                        annualReportFormInstance.BackgroundImageLayout = ImageLayout.None;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // 窗口关闭时清理资源
                    annualReportFormInstance.FormClosing += (s, e) =>
                    {
                        if (annualReportVideoTimer != null)
                        {
                            annualReportVideoTimer.Stop();
                            annualReportVideoTimer.Dispose();
                            annualReportVideoTimer = null;
                        }
                        if (annualReportMediaPlayer != null)
                        {
                            annualReportMediaPlayer.Stop();
                            annualReportMediaPlayer.Close();
                            annualReportMediaPlayer = null;
                        }
                        if (annualReportFormInstance.BackgroundImage != null)
                        {
                            var img = annualReportFormInstance.BackgroundImage;
                            annualReportFormInstance.BackgroundImage = null;
                            img.Dispose();
                        }
                        UnregisterAnnualReportRefresh(annualReportFormInstance);
                    };

                    annualReportFormInstance.Resize += (s, e) =>
                    {
                        if (annualReportVideoTimer == null || annualReportMediaPlayer == null) return;

                        if (annualReportFormInstance.WindowState == FormWindowState.Minimized)
                        {
                            annualReportMediaPlayer.Pause();
                            annualReportVideoTimer.Stop();
                        }
                        else
                        {
                            annualReportMediaPlayer.Play();
                            annualReportVideoTimer.Start();
                        }
                    };

                    annualReportFormInstance.Activated += (s, e) =>
                    {
                        annualReportMediaPlayer?.Play();
                        annualReportVideoTimer?.Start();
                    };

                    annualReportFormInstance.Deactivate += (s, e) =>
                    {
                        annualReportMediaPlayer?.Pause();
                        annualReportVideoTimer?.Stop();
                    };

                    // 启用双缓冲
                    typeof(Form).InvokeMember("DoubleBuffered",
                        System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                        null, annualReportFormInstance, new object[] { true });

                    var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (setStyleMethod != null)
                    {
                        setStyleMethod.Invoke(annualReportFormInstance, new object[] {
                            ControlStyles.AllPaintingInWmPaint |
                            ControlStyles.UserPaint |
                            ControlStyles.OptimizedDoubleBuffer, true
                        });
                    }

                    // 绘制背景和半透明遮罩
                    annualReportFormInstance.Paint += (s, pe) =>
                    {
                        Graphics g = pe.Graphics;

                        if (annualReportFormInstance.BackgroundImage != null)
                        {
                            // 直接绘制（已经在渲染时处理好了缩放）
                            g.DrawImage(annualReportFormInstance.BackgroundImage, 0, 0);
                        }

                        // 绘制半透明黑色滤镜（降低透明度到60，让背景更明显）
                        using (SolidBrush mask = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                            g.FillRectangle(mask, annualReportFormInstance.ClientRectangle);
                    };

                    // ==================== 创建6个相同大小的半透明白色panel ====================
                    // 动态计算panel尺寸和位置，适配窗口大小
                    int clientWidth = annualReportFormInstance.ClientSize.Width;
                    int clientHeight = annualReportFormInstance.ClientSize.Height;
                    
                    // 计算间距和panel尺寸
                    int margin = S(15);  // 窗口边距
                    int panelSpacingX = S(15);  // panel之间的水平间距
                    int panelSpacingY = S(15);  // panel之间的垂直间距
                    
                    // 3列2行布局，计算每个panel的宽度和高度
                    int panelWidth = (clientWidth - 2 * margin - 2 * panelSpacingX) / 3;
                    int panelHeight = (clientHeight - 2 * margin - panelSpacingY) / 2;
                    
                    // 计算实际位置
                    int[] xPositions = new int[3];
                    int[] yPositions = new int[2];
                    
                    for (int col = 0; col < 3; col++)
                    {
                        xPositions[col] = margin + col * (panelWidth + panelSpacingX);
                    }
                    
                    for (int row = 0; row < 2; row++)
                    {
                        yPositions[row] = margin + row * (panelHeight + panelSpacingY);
                    }

                    Panel[] reportPanels = new Panel[6];

                    for (int row = 0; row < 2; row++)
                    {
                        for (int col = 0; col < 3; col++)
                        {
                            int index = row * 3 + col;

                            reportPanels[index] = new Panel()
                            {
                                Left = xPositions[col],
                                Top = yPositions[row],
                                Width = panelWidth,
                                Height = panelHeight,
                                BackColor = Color.Transparent,
                                BorderStyle = BorderStyle.None
                            };

                            // 启用双缓冲
                            typeof(Panel).InvokeMember("DoubleBuffered",
                                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                                null, reportPanels[index], new object[] { true });

                            annualReportFormInstance.Controls.Add(reportPanels[index]);

                            // 绘制半透明白色背景（完全参考配置中心pathPanel的实现）
                            reportPanels[index].Paint += (s, pe) =>
                            {
                                Graphics g = pe.Graphics;
                                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                                Panel panel = (Panel)s!;
                                int cornerRadius = S(10);
                                Rectangle panelRect = new Rectangle(0, 0, panel.Width, panel.Height);

                                using (GraphicsPath roundedPath = CreateRoundedRectanglePath(panelRect, cornerRadius))
                                {
                                    if (annualReportFormInstance.BackgroundImage != null)
                                    {
                                        // 如果有背景图片，先绘制背景图片的对应区域
                                        Rectangle bgRect = annualReportFormInstance.RectangleToClient(
                                            panel.RectangleToScreen(panel.ClientRectangle)
                                        );

                                        // 设置裁剪区域为圆角矩形
                                        g.SetClip(roundedPath);
                                        g.DrawImage(annualReportFormInstance.BackgroundImage,
                                            panel.ClientRectangle,
                                            bgRect,
                                            GraphicsUnit.Pixel);
                                        g.ResetClip();
                                    }

                                    // 绘制半透明白色背景（无论是否有背景图片都绘制）
                                    using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                                        g.FillPath(bgBrush, roundedPath);
                                }
                            };

                            // 第一个panel使用自定义绘制，不添加标题
                            if (index == 0)
                            {
                                reportPanels[0].Cursor = Cursors.Hand;
                                // 移除panel的点击事件，改为处理卡片点击
                                reportPanels[0].MouseClick += (s, e) =>
                                {
                                    HandleCardClick(e.X, e.Y, reportPanels[0].Width, reportPanels[0].Height, 0);
                                };
                                
                                // 添加鼠标移动事件来改变光标
                                reportPanels[0].MouseMove += (s, e) =>
                                {
                                    var cursor = GetCursorForPosition(e.X, e.Y, reportPanels[0].Width, reportPanels[0].Height, 0);
                                    reportPanels[0].Cursor = cursor;
                                };
                                
                                // 使用Paint事件绘制内容
                                reportPanels[0].Paint += (s, pe) =>
                                {
                                    DrawPanel1Content(pe.Graphics, reportPanels[0].Width, reportPanels[0].Height);
                                };
                            }
                            // 第二个panel（中上）绘制第二和第三个卡片
                            else if (index == 1)
                            {
                                reportPanels[1].Cursor = Cursors.Hand;
                                reportPanels[1].MouseClick += (s, e) =>
                                {
                                    HandleCardClick(e.X, e.Y, reportPanels[1].Width, reportPanels[1].Height, 1);
                                };
                                
                                reportPanels[1].MouseMove += (s, e) =>
                                {
                                    var cursor = GetCursorForPosition(e.X, e.Y, reportPanels[1].Width, reportPanels[1].Height, 1);
                                    reportPanels[1].Cursor = cursor;
                                };
                                
                                reportPanels[1].Paint += (s, pe) =>
                                {
                                    DrawPanel2Content(pe.Graphics, reportPanels[1].Width, reportPanels[1].Height);
                                };
                            }
                            // 第三个panel（中下）绘制视频相关卡片
                            else if (index == 2)
                            {
                                reportPanels[2].Cursor = Cursors.Hand;
                                reportPanels[2].MouseClick += (s, e) =>
                                {
                                    HandleCardClick(e.X, e.Y, reportPanels[2].Width, reportPanels[2].Height, 2);
                                };
                                
                                reportPanels[2].MouseMove += (s, e) =>
                                {
                                    var cursor = GetCursorForPosition(e.X, e.Y, reportPanels[2].Width, reportPanels[2].Height, 2);
                                    reportPanels[2].Cursor = cursor;
                                };
                                
                                reportPanels[2].Paint += (s, pe) =>
                                {
                                    DrawPanel3Content(pe.Graphics, reportPanels[2].Width, reportPanels[2].Height);
                                };
                            }
                            // 第四个panel（左下）绘制收藏卡片
                            else if (index == 3)
                            {
                                reportPanels[3].Cursor = Cursors.Default;
                                reportPanels[3].Paint += (s, pe) =>
                                {
                                    DrawPanel4Content(pe.Graphics, reportPanels[3].Width, reportPanels[3].Height);
                                };
                            }
                            // 第五个panel（中下）绘制图包收藏卡片
                            else if (index == 4)
                            {
                                reportPanels[4].Cursor = Cursors.Default;
                                reportPanels[4].Paint += (s, pe) =>
                                {
                                    DrawPanel5Content(pe.Graphics, reportPanels[4].Width, reportPanels[4].Height);
                                };
                            }
                            // 第六个panel（右下）绘制番号相关卡片
                            else if (index == 5)
                            {
                                reportPanels[5].Cursor = Cursors.Hand;
                                reportPanels[5].MouseClick += (s, e) =>
                                {
                                    HandleCardClick(e.X, e.Y, reportPanels[5].Width, reportPanels[5].Height, 5);
                                };

                                reportPanels[5].MouseMove += (s, e) =>
                                {
                                    var cursor = GetCursorForPosition(e.X, e.Y, reportPanels[5].Width, reportPanels[5].Height, 5);
                                    reportPanels[5].Cursor = cursor;
                                };

                                reportPanels[5].Paint += (s, pe) =>
                                {
                                    DrawPanel6Content(pe.Graphics, reportPanels[5].Width, reportPanels[5].Height);
                                };
                            }
                        }
                    }

                    // 恢复布局并显示窗口
                    annualReportFormInstance.ResumeLayout(false);
                    annualReportFormInstance.PerformLayout();
                    RegisterAnnualReportRefresh(annualReportFormInstance, RefreshAnnualReportWindowCore);
                    RequestAnnualReportRefresh(true);

                    annualReportFormInstance.Show();
                }

        // 年度统计数据结构
        private class YearlyStatistics
        {
            public bool HasHistoryData { get; set; }
            public int ActiveDays { get; set; }
            public DateTime PeakDate { get; set; }
            public int PeakDayClicks { get; set; }
            public int PeakMonth { get; set; }
            public int PeakMonthClicks { get; set; }
            public int SpecialDateClicks { get; set; }
            public string SpecialDateName { get; set; } = "";
            public int MaxInactiveStreak { get; set; }
            public int EarlyMorningClicks { get; set; }
            public DateTime EarliestTime { get; set; }
            public double LateNightPercentage { get; set; }
            public int PeakHour { get; set; } // 最活跃的小时
            public int PeakHourClicks { get; set; } // 最活跃小时的点击数
            public string MostUsedPath { get; set; } = ""; // 最常使用的路径
            public int MostUsedPathCount { get; set; } // 最常使用路径的次数
            public string MostUsedBackground { get; set; } = ""; // 最常使用的背景
            public int MostUsedBackgroundCount { get; set; } // 最常使用背景的次数
            public string MostWatchedVideo { get; set; } = ""; // 最常观看的视频
            public int MostWatchedVideoCount { get; set; } // 最常观看视频的次数
            public string FavoriteTeacher { get; set; } = ""; // 最爱老师（文件夹名）
            public int FavoriteTeacherCount { get; set; } // 最爱老师观看次数
            public int UnwatchedVideoCount { get; set; } // 从未被选中过的视频数量
            public int AddedVideoCount { get; set; } // 新增视频数量
            public int DeletedVideoCount { get; set; } // 删除视频数量
            public int TotalFavoriteVideoCount { get; set; } // 当前收藏总数
            public int AddedFavoriteVideoCount { get; set; } // 今年新增收藏数
            public int FavoriteRewatchCount { get; set; } // 收藏重温次数
            public int FavoriteNeverRewatchedCount { get; set; } // 收藏后从未重温数量
            public string MostRewatchedFavorite { get; set; } = ""; // 最爱收藏
            public int MostRewatchedFavoriteCount { get; set; } // 最爱收藏的重温/回味次数
            public int TotalFavoriteImagePackCount { get; set; } // 当前图包收藏总数
            public int AddedFavoriteImagePackCount { get; set; } // 今年新增图包收藏数
            public int FavoriteImagePackRewatchCount { get; set; } // 图包收藏重温次数
            public int FavoriteImagePackNeverRewatchedCount { get; set; } // 图包收藏后从未重温数量
            public string MostRewatchedFavoriteImagePack { get; set; } = ""; // 最爱的图包收藏
            public int MostRewatchedFavoriteImagePackCount { get; set; } // 最爱的图包收藏重温/回味次数
            public string FavoriteCodePrefix { get; set; } = ""; // 最爱的番号前缀
            public int FavoriteCodePrefixCount { get; set; } // 最爱的番号前缀观看次数
            public string FavoriteTeacherCollection { get; set; } = ""; // 片库里数量最多的老师
            public int FavoriteTeacherCollectionCount { get; set; } // 片库里数量最多的老师的视频数
        }

        private class InventorySnapshot
        {
            public DateTime Timestamp { get; set; }
            public List<string> EnabledPaths { get; set; } = new List<string>();
            public List<string> Videos { get; set; } = new List<string>();
        }

        private class InventorySnapshotFile
        {
            public int Year { get; set; }
            public List<InventorySnapshot> Snapshots { get; set; } = new List<InventorySnapshot>();
        }

        private class FavoriteYearRecord
        {
            public int Year { get; set; }
            public int AddedCount { get; set; }
        }

        private class AnnualCountState
        {
            public int Year { get; set; }
            public int BaselineCount { get; set; }
        }

        private class VideoCodecCacheEntry
        {
            public string Path { get; set; } = "";
            public long FileSize { get; set; }
            public long LastWriteTimeUtcTicks { get; set; }
            public string CodecName { get; set; } = "";
            public DateTime UpdatedAt { get; set; }
        }

        private class VideoCodecCacheFile
        {
            public List<VideoCodecCacheEntry> Entries { get; set; } = new List<VideoCodecCacheEntry>();
        }

        private class VideoCodecUsageSummary
        {
            public bool HasData { get; set; }
            public string CodecDisplayName { get; set; } = "";
            public int CodecCount { get; set; }
            public int TotalCount { get; set; }
            public double Percentage { get; set; }
            public bool IsOriginalContainer { get; set; }
        }

        private class UnwatchedFolderCacheEntry
        {
            public string Folder { get; set; } = "";
            public int Count { get; set; }
        }

        private class UnwatchedVideoCacheFile
        {
            public int Version { get; set; }
            public int Year { get; set; }
            public string EnabledPathsSignature { get; set; } = "";
            public long HistoryTicks { get; set; }
            public int Count { get; set; } = -1;
            public List<UnwatchedFolderCacheEntry> FolderDistribution { get; set; } = new List<UnwatchedFolderCacheEntry>();
            public DateTime UpdatedAt { get; set; }
        }

        private class TeacherCollectionCacheEntry
        {
            public string Teacher { get; set; } = "";
            public int Count { get; set; }
        }

        private class TeacherCollectionCacheFile
        {
            public int Version { get; set; }
            public string EnabledPathsSignature { get; set; } = "";
            public int FavoriteCount { get; set; } = -1;
            public string FavoriteTeacher { get; set; } = "";
            public List<TeacherCollectionCacheEntry> Distribution { get; set; } = new List<TeacherCollectionCacheEntry>();
            public DateTime UpdatedAt { get; set; }
        }

        private class VideoQualitySnapshotEntry
        {
            public string GroupKey { get; set; } = "";
            public string Teacher { get; set; } = "";
            public string SampleName { get; set; } = "";
            public bool Has4K { get; set; }
            public bool HasUncensored { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        private class VideoQualitySnapshotFile
        {
            public int Version { get; set; }
            public List<VideoQualitySnapshotEntry> Entries { get; set; } = new List<VideoQualitySnapshotEntry>();
            public DateTime UpdatedAt { get; set; }
        }

        private class VideoQualitySummary
        {
            public bool HasData { get; set; }
            public int TotalGroups { get; set; }
            public int FourKGroups { get; set; }
            public int UncensoredGroups { get; set; }
            public double FourKPercentage { get; set; }
            public double UncensoredPercentage { get; set; }
        }

        private static readonly HashSet<string> SupportedVideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".wmv", ".flv", ".mov", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp", ".rmvb"
        };

        private string GetCanonicalVideoSourceKey(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            string baseName = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return string.Empty;
            }

            string normalized = baseName.ToUpperInvariant().Trim();
            normalized = normalized.Replace("[", " ").Replace("]", " ")
                .Replace("(", " ").Replace(")", " ")
                .Replace("{", " ").Replace("}", " ")
                .Replace("【", " ").Replace("】", " ")
                .Replace("_", " ").Replace(".", " ");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();

            var fc2Match = System.Text.RegularExpressions.Regex.Match(
                normalized,
                @"\b(FC2)\s*(?:PPV)?\s*(\d{3,})\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fc2Match.Success)
            {
                return $"{fc2Match.Groups[1].Value.ToUpperInvariant()}-{fc2Match.Groups[2].Value}";
            }

            var codeMatch = System.Text.RegularExpressions.Regex.Match(
                normalized,
                @"\b([A-Z]{2,10})(?:\s+|00|-)?(\d{2,6})\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (codeMatch.Success)
            {
                return $"{codeMatch.Groups[1].Value.ToUpperInvariant()}-{codeMatch.Groups[2].Value}";
            }

            string cleaned = normalized;
            string[] removableTokens =
            {
                "4K", "2160P", "UHD", "SUPERRES", "RESTORED", "UNCENSORED", "CENSORED",
                "无码", "無碼", "破解", "破解版", "超分", "修复", "修復", "IRIS", "PROB"
            };

            foreach (string token in removableTokens)
            {
                cleaned = System.Text.RegularExpressions.Regex.Replace(
                    cleaned,
                    $@"(?<![A-Z0-9]){System.Text.RegularExpressions.Regex.Escape(token)}(?![A-Z0-9])",
                    " ",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            string previous;
            do
            {
                previous = cleaned;
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?:\s|[-_])?VRV\d+$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?:\s|[-_])?(?:UHQF|HQF|LQF|MQF|F)\d+$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?:\s|[-_])(?:PART|PT|CD|DISC|DISK|VOL|EPISODE|EP)\s*\d{1,3}$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?:\s|[-_])[A-Z]$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?:\s|[-_])\d{1,3}$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
            }
            while (!string.Equals(previous, cleaned, StringComparison.Ordinal));

            return cleaned;
        }

        private string GetVideoTeacherKeyFromPath(string? videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
            {
                return string.Empty;
            }

            string? folderPath = Path.GetDirectoryName(videoPath);
            string teacherName = GetSmartFolderName(folderPath);
            if (string.IsNullOrWhiteSpace(teacherName))
            {
                teacherName = Path.GetFileName(folderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(teacherName) ? string.Empty : teacherName.Trim();
        }

        private string GetCanonicalVideoIdentityKey(string? fileName, string? videoPath = null)
        {
            string sourceKey = GetCanonicalVideoSourceKey(fileName);
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                if (!string.IsNullOrWhiteSpace(videoPath))
                {
                    return NormalizeInventoryPath(videoPath);
                }

                return string.Empty;
            }

            string teacherKey = GetVideoTeacherKeyFromPath(videoPath);
            return string.IsNullOrWhiteSpace(teacherKey) ? sourceKey : $"{teacherKey}|{sourceKey}";
        }

        private Dictionary<string, int> GetCurrentYearVideoWatchCountsFromHistoryLog()
        {
            var videoCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");
            int currentYear = DateTime.Now.Year;

            if (!File.Exists(historyPath))
            {
                return videoCounts;
            }

            foreach (var line in File.ReadAllLines(historyPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('|');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], out DateTime timestamp) || timestamp.Year != currentYear) continue;

                string fileNameField = parts[2];
                string fullPath = parts[4];
                string extension = Path.GetExtension(fileNameField);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = Path.GetExtension(fullPath);
                }
                if (!SupportedVideoExtensions.Contains(extension)) continue;

                string rawVideoName = fileNameField;
                if (string.IsNullOrWhiteSpace(rawVideoName))
                {
                    rawVideoName = Path.GetFileName(fullPath);
                }
                else
                {
                    rawVideoName = Path.GetFileName(rawVideoName);
                }

                string videoName = GetCanonicalVideoSourceKey(rawVideoName);
                if (string.IsNullOrWhiteSpace(videoName))
                {
                    continue;
                }

                if (videoCounts.ContainsKey(videoName))
                    videoCounts[videoName]++;
                else
                    videoCounts[videoName] = 1;
            }

            return videoCounts;
        }

        private Dictionary<string, int> GetCurrentYearTeacherWatchCountsFromHistoryLog()
        {
            var teacherCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");
            int currentYear = DateTime.Now.Year;

            if (!File.Exists(historyPath))
            {
                return teacherCounts;
            }

            foreach (var line in File.ReadAllLines(historyPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('|');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], out DateTime timestamp) || timestamp.Year != currentYear) continue;

                string fileNameField = parts[2];
                string fullPath = parts[4];
                string extension = Path.GetExtension(fileNameField);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = Path.GetExtension(fullPath);
                }
                if (!SupportedVideoExtensions.Contains(extension)) continue;

                string folderPath = fullPath;
                if (!string.IsNullOrWhiteSpace(fullPath) && !Directory.Exists(fullPath))
                {
                    string? dirName = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(dirName))
                    {
                        folderPath = dirName;
                    }
                }

                string teacherName = GetSmartFolderName(folderPath);
                if (string.IsNullOrWhiteSpace(teacherName))
                {
                    teacherName = parts[1];
                }
                if (string.IsNullOrWhiteSpace(teacherName)) continue;

                if (teacherCounts.ContainsKey(teacherName))
                    teacherCounts[teacherName]++;
                else
                    teacherCounts[teacherName] = 1;
            }

            return teacherCounts;
        }

        private List<(string teacher, int count)> GetTeacherCollectionDistribution()
        {
            var teacherCounts = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string videoPath in GetEnabledVideoInventory())
            {
                string? folderPath = Path.GetDirectoryName(videoPath);
                string teacherName = GetSmartFolderName(folderPath);
                if (string.IsNullOrWhiteSpace(teacherName))
                {
                    teacherName = Path.GetFileName(folderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(teacherName))
                {
                    teacherName = "未知老师";
                }

                string sourceKey = GetCanonicalVideoSourceKey(videoPath);
                if (string.IsNullOrWhiteSpace(sourceKey))
                {
                    sourceKey = NormalizeInventoryPath(videoPath);
                }

                if (!teacherCounts.TryGetValue(teacherName, out HashSet<string>? sourceSet))
                {
                    sourceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    teacherCounts[teacherName] = sourceSet;
                }

                sourceSet.Add(sourceKey);
            }

            return teacherCounts
                .Select(kvp => (teacher: kvp.Key, count: kvp.Value.Count))
                .OrderByDescending(item => item.count)
                .ThenBy(item => item.teacher, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string GetTeacherCollectionCacheFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "teacher_collection_cache.json");
        }

        private TeacherCollectionCacheFile LoadTeacherCollectionCacheFile()
        {
            string filePath = GetTeacherCollectionCacheFilePath();
            if (!File.Exists(filePath))
            {
                return new TeacherCollectionCacheFile();
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var cacheFile = JsonSerializer.Deserialize<TeacherCollectionCacheFile>(json) ?? new TeacherCollectionCacheFile();
                return cacheFile.Version == TeacherCollectionCacheVersion ? cacheFile : new TeacherCollectionCacheFile();
            }
            catch
            {
                return new TeacherCollectionCacheFile();
            }
        }

        private void SaveTeacherCollectionCacheFile(TeacherCollectionCacheFile cacheFile)
        {
            string filePath = GetTeacherCollectionCacheFilePath();
            string configDir = Path.GetDirectoryName(filePath) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            cacheFile.Version = TeacherCollectionCacheVersion;
            var options = new JsonSerializerOptions { WriteIndented = false };
            File.WriteAllText(filePath, JsonSerializer.Serialize(cacheFile, options));
        }

        private void EnsureTeacherCollectionCacheLoaded()
        {
            lock (teacherCollectionScanLock)
            {
                if (cachedFavoriteTeacherCollectionCount >= 0 || isTeacherCollectionScanInProgress)
                {
                    return;
                }

                var cacheFile = LoadTeacherCollectionCacheFile();
                if (cacheFile.FavoriteCount >= 0)
                {
                    cachedFavoriteTeacherCollection = cacheFile.FavoriteTeacher ?? "";
                    cachedFavoriteTeacherCollectionCount = cacheFile.FavoriteCount;
                    cachedTeacherCollectionDistribution = cacheFile.Distribution
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.Teacher))
                        .Select(entry => (entry.Teacher, entry.Count))
                        .ToList();
                    teacherCollectionScanStatusMessage = "已加载上次老师库存统计结果，点击可按当前路径重算。";
                }
            }
        }

        private void StartTeacherCollectionScanForAnnualReport()
        {
            lock (teacherCollectionScanLock)
            {
                if (isTeacherCollectionScanInProgress)
                {
                    return;
                }

                isTeacherCollectionScanInProgress = true;
                teacherCollectionScanStatusMessage = "正在统计当前启用路径里的老师库存...";
            }

            Task.Run(() =>
            {
                try
                {
                    var distribution = GetTeacherCollectionDistribution();
                    string enabledPathsSignature = string.Join("|", GetEnabledPathSnapshot());
                    var favorite = distribution.FirstOrDefault();

                    SaveTeacherCollectionCacheFile(new TeacherCollectionCacheFile
                    {
                        EnabledPathsSignature = enabledPathsSignature,
                        FavoriteTeacher = favorite.teacher ?? "",
                        FavoriteCount = distribution.Count > 0 ? favorite.count : 0,
                        Distribution = distribution.Select(item => new TeacherCollectionCacheEntry
                        {
                            Teacher = item.teacher,
                            Count = item.count
                        }).ToList(),
                        UpdatedAt = DateTime.Now
                    });

                    lock (teacherCollectionScanLock)
                    {
                        cachedTeacherCollectionDistribution = distribution;
                        cachedFavoriteTeacherCollection = distribution.Count > 0 ? favorite.teacher ?? string.Empty : string.Empty;
                        cachedFavoriteTeacherCollectionCount = distribution.Count > 0 ? favorite.count : 0;
                        isTeacherCollectionScanInProgress = false;
                        teacherCollectionScanStatusMessage = distribution.Count > 0
                            ? "老师库存统计已更新。"
                            : "当前启用路径里没有可统计的视频。";
                    }
                }
                catch
                {
                    lock (teacherCollectionScanLock)
                    {
                        isTeacherCollectionScanInProgress = false;
                        teacherCollectionScanStatusMessage = "老师库存统计失败，请稍后再试。";
                    }
                }

                if (annualReportFormInstance != null && !annualReportFormInstance.IsDisposed)
                {
                    try
                    {
                        annualReportFormInstance.BeginInvoke(new Action(() => RequestAnnualReportRefresh(true)));
                    }
                    catch { }
                }
            });
        }

        private string GetVideoQualitySnapshotFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "video_quality_snapshot.json");
        }

        private VideoQualitySnapshotFile LoadVideoQualitySnapshotFile()
        {
            string filePath = GetVideoQualitySnapshotFilePath();
            if (!File.Exists(filePath))
            {
                return new VideoQualitySnapshotFile();
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var snapshotFile = JsonSerializer.Deserialize<VideoQualitySnapshotFile>(json) ?? new VideoQualitySnapshotFile();
                return snapshotFile.Version == VideoQualitySnapshotVersion ? snapshotFile : new VideoQualitySnapshotFile();
            }
            catch
            {
                return new VideoQualitySnapshotFile();
            }
        }

        private void SaveVideoQualitySnapshotFile(VideoQualitySnapshotFile snapshotFile)
        {
            string filePath = GetVideoQualitySnapshotFilePath();
            string configDir = Path.GetDirectoryName(filePath) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            snapshotFile.Version = VideoQualitySnapshotVersion;
            snapshotFile.UpdatedAt = DateTime.Now;
            var options = new JsonSerializerOptions { WriteIndented = false };
            File.WriteAllText(filePath, JsonSerializer.Serialize(snapshotFile, options));
        }

        private VideoQualitySummary GetVideoQualitySummaryFromSnapshot()
        {
            var snapshotFile = LoadVideoQualitySnapshotFile();
            var validEntries = snapshotFile.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.GroupKey))
                .ToList();

            if (validEntries.Count == 0)
            {
                return new VideoQualitySummary();
            }

            int totalGroups = validEntries.Count;
            int fourKGroups = validEntries.Count(entry => entry.Has4K);
            int uncensoredGroups = validEntries.Count(entry => entry.HasUncensored);

            return new VideoQualitySummary
            {
                HasData = true,
                TotalGroups = totalGroups,
                FourKGroups = fourKGroups,
                UncensoredGroups = uncensoredGroups,
                FourKPercentage = totalGroups > 0 ? (double)fourKGroups / totalGroups * 100 : 0,
                UncensoredPercentage = totalGroups > 0 ? (double)uncensoredGroups / totalGroups * 100 : 0
            };
        }

        private void StartVideoQualitySnapshotScanForAnnualReport()
        {
            lock (qualitySnapshotScanLock)
            {
                if (isQualitySnapshotScanInProgress)
                {
                    return;
                }

                isQualitySnapshotScanInProgress = true;
                qualitySnapshotScanStatusMessage = "正在统计当前启用路径里的 4K / 无码占比...";
            }

            Task.Run(() =>
            {
                try
                {
                    var currentGroups = BuildVideoQualityGroupsFromEnabledInventory();
                    var snapshotFile = LoadVideoQualitySnapshotFile();
                    var mergedEntries = snapshotFile.Entries
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.GroupKey))
                        .ToDictionary(entry => entry.GroupKey, entry => entry, StringComparer.OrdinalIgnoreCase);

                    foreach (var entry in currentGroups)
                    {
                        mergedEntries[entry.GroupKey] = entry;
                    }

                    snapshotFile.Entries = mergedEntries.Values
                        .OrderBy(entry => entry.Teacher, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(entry => entry.GroupKey, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    SaveVideoQualitySnapshotFile(snapshotFile);

                    lock (qualitySnapshotScanLock)
                    {
                        isQualitySnapshotScanInProgress = false;
                        qualitySnapshotScanStatusMessage = currentGroups.Count > 0
                            ? "4K / 无码占比统计已更新。"
                            : "当前启用路径里没有可统计的视频。";
                    }
                }
                catch
                {
                    lock (qualitySnapshotScanLock)
                    {
                        isQualitySnapshotScanInProgress = false;
                        qualitySnapshotScanStatusMessage = "4K / 无码占比统计失败，请稍后再试。";
                    }
                }

                if (annualReportFormInstance != null && !annualReportFormInstance.IsDisposed)
                {
                    try
                    {
                        annualReportFormInstance.BeginInvoke(new Action(() => RequestAnnualReportRefresh(true)));
                    }
                    catch { }
                }
            });
        }

        private List<VideoQualitySnapshotEntry> BuildVideoQualityGroupsFromEnabledInventory()
        {
            var groups = new Dictionary<string, VideoQualitySnapshotEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (string videoPath in GetEnabledVideoInventory())
            {
                string fileName = Path.GetFileName(videoPath);
                string? folderPath = Path.GetDirectoryName(videoPath);
                string teacherName = GetSmartFolderName(folderPath);
                if (string.IsNullOrWhiteSpace(teacherName))
                {
                    teacherName = Path.GetFileName(folderPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
                }
                if (string.IsNullOrWhiteSpace(teacherName))
                {
                    teacherName = "未知老师";
                }

                string groupKey = BuildVideoQualityGroupKey(fileName, teacherName);
                if (string.IsNullOrWhiteSpace(groupKey))
                {
                    continue;
                }

                bool is4K = LooksLike4KVideo(fileName);
                bool isUncensored = LooksLikeUncensoredVideo(fileName);

                if (!groups.TryGetValue(groupKey, out VideoQualitySnapshotEntry? entry))
                {
                    entry = new VideoQualitySnapshotEntry
                    {
                        GroupKey = groupKey,
                        Teacher = teacherName,
                        SampleName = fileName,
                        Has4K = is4K,
                        HasUncensored = isUncensored,
                        UpdatedAt = DateTime.Now
                    };
                    groups[groupKey] = entry;
                }
                else
                {
                    entry.Has4K |= is4K;
                    entry.HasUncensored |= isUncensored;
                    entry.UpdatedAt = DateTime.Now;
                    if (string.IsNullOrWhiteSpace(entry.SampleName))
                    {
                        entry.SampleName = fileName;
                    }
                }
            }

            return groups.Values.ToList();
        }

        private string BuildVideoQualityGroupKey(string fileName, string teacherName)
        {
            string canonicalKey = GetCanonicalVideoSourceKey(fileName);
            if (string.IsNullOrWhiteSpace(canonicalKey))
            {
                return string.Empty;
            }

            return $"{teacherName}|{canonicalKey}";
        }

        private bool LooksLike4KVideo(string fileName)
        {
            string upper = (Path.GetFileNameWithoutExtension(fileName) ?? "").ToUpperInvariant();
            return upper.Contains("4K") ||
                   upper.Contains("2160P") ||
                   upper.Contains("UHD") ||
                   upper.Contains("IRIS") ||
                   upper.Contains("PROB");
        }

        private bool LooksLikeUncensoredVideo(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName) ?? "";
            string upper = name.ToUpperInvariant();
            return name.Contains("无码") ||
                   name.Contains("無碼") ||
                   upper.Contains("UNCENSORED") ||
                   name.Contains("破解");
        }

        private Dictionary<string, int> GetCurrentYearCodePrefixWatchCountsFromHistoryLog()
        {
            var codeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");
            int currentYear = DateTime.Now.Year;

            if (!File.Exists(historyPath))
            {
                return codeCounts;
            }

            foreach (var line in File.ReadAllLines(historyPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('|');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], out DateTime timestamp) || timestamp.Year != currentYear) continue;

                string fileNameField = parts[2];
                string fullPath = parts[4];
                string extension = Path.GetExtension(fileNameField);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = Path.GetExtension(fullPath);
                }
                if (!SupportedVideoExtensions.Contains(extension)) continue;

                string rawFileName = fileNameField;
                if (string.IsNullOrWhiteSpace(rawFileName) && !string.IsNullOrWhiteSpace(fullPath))
                {
                    rawFileName = Path.GetFileName(fullPath);
                }

                string codePrefix = ExtractVideoCodePrefix(rawFileName);
                if (string.IsNullOrWhiteSpace(codePrefix))
                {
                    continue;
                }

                if (codeCounts.ContainsKey(codePrefix))
                    codeCounts[codePrefix]++;
                else
                    codeCounts[codePrefix] = 1;
            }

            return codeCounts;
        }

        private string ExtractVideoCodePrefix(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            string name = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            name = name.Trim();

            var fc2Match = System.Text.RegularExpressions.Regex.Match(
                name,
                @"(?i)\b(FC2)[-_ ]*(?:PPV)?[-_ ]*\d{3,}\b");
            if (fc2Match.Success)
            {
                return fc2Match.Groups[1].Value.ToUpperInvariant();
            }

            var standardMatch = System.Text.RegularExpressions.Regex.Match(
                name,
                @"(?i)\b([A-Z]{2,10})(?:[-_ ]|00)?\d{2,6}\b");
            if (standardMatch.Success)
            {
                return standardMatch.Groups[1].Value.ToUpperInvariant();
            }

            return string.Empty;
        }

        private List<string> GetEnabledVideoInventory()
        {
            List<string> enabledPaths = pathManager.GetEnabledPaths()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string signature = string.Join("|", enabledPaths);
            if (cachedEnabledVideos != null &&
                string.Equals(cachedEnabledPathsSignature, signature, StringComparison.OrdinalIgnoreCase) &&
                (DateTime.Now - cachedEnabledVideosAt).TotalSeconds < 10)
            {
                return new List<string>(cachedEnabledVideos);
            }

            var selector = new VideoSelector(
                pathManager,
                modeManager,
                historyManager,
                favoritesManager,
                blackWhiteListManager);

            cachedEnabledVideos = selector.ScanEnabledPaths();
            cachedEnabledPathsSignature = signature;
            cachedEnabledVideosAt = DateTime.Now;
            return new List<string>(cachedEnabledVideos);
        }

        private List<string> GetEnabledPathSnapshot()
        {
            return pathManager.GetEnabledPaths()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeInventoryPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string NormalizeInventoryPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private bool IsVideoUnderEnabledPaths(string videoPath, HashSet<string> enabledPaths)
        {
            if (enabledPaths.Count == 0 || string.IsNullOrWhiteSpace(videoPath))
            {
                return false;
            }

            string normalizedVideoPath = NormalizeInventoryPath(videoPath);
            foreach (string enabledPath in enabledPaths)
            {
                if (normalizedVideoPath.Equals(enabledPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string prefix = enabledPath + Path.DirectorySeparatorChar;
                string altPrefix = enabledPath + Path.AltDirectorySeparatorChar;
                if (normalizedVideoPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    normalizedVideoPath.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string ResolveHistoryEntryPath(string? fileNameField, string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            string normalizedFullPath = NormalizeInventoryPath(fullPath);
            string rawFileName = string.IsNullOrWhiteSpace(fileNameField) ? string.Empty : Path.GetFileName(fileNameField);
            if (string.IsNullOrWhiteSpace(rawFileName))
            {
                return normalizedFullPath;
            }

            string fullPathExtension = Path.GetExtension(normalizedFullPath);
            string fileNameExtension = Path.GetExtension(rawFileName);
            bool fullPathLooksLikeFile = !string.IsNullOrWhiteSpace(fullPathExtension);
            bool fileNameLooksLikeFile = !string.IsNullOrWhiteSpace(fileNameExtension);

            if (fullPathLooksLikeFile || !fileNameLooksLikeFile)
            {
                return normalizedFullPath;
            }

            try
            {
                return NormalizeInventoryPath(Path.Combine(normalizedFullPath, rawFileName));
            }
            catch
            {
                return normalizedFullPath;
            }
        }

        private string GetSelectedRootPathForHistoryEntry(string? fileNameField, string? fullPath, IReadOnlyList<string>? enabledPaths = null)
        {
            IReadOnlyList<string> candidateRoots = enabledPaths ?? GetEnabledPathSnapshot();
            if (candidateRoots.Count == 0)
            {
                return string.Empty;
            }

            string resolvedPath = ResolveHistoryEntryPath(fileNameField, fullPath);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return string.Empty;
            }

            string? bestMatch = null;
            foreach (string enabledPath in candidateRoots)
            {
                if (string.IsNullOrWhiteSpace(enabledPath))
                {
                    continue;
                }

                if (resolvedPath.Equals(enabledPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (bestMatch == null || enabledPath.Length > bestMatch.Length)
                    {
                        bestMatch = enabledPath;
                    }

                    continue;
                }

                string prefix = enabledPath + Path.DirectorySeparatorChar;
                string altPrefix = enabledPath + Path.AltDirectorySeparatorChar;
                if (resolvedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    resolvedPath.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (bestMatch == null || enabledPath.Length > bestMatch.Length)
                    {
                        bestMatch = enabledPath;
                    }
                }
            }

            return bestMatch ?? string.Empty;
        }

        private string GetVideoCodecCacheFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "video_codec_cache.json");
        }

        private string GetPathUsageLogFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "path_usage.log");
        }

        private void RecordSelectedRootPathUsage(string? filePath)
        {
            string selectedRootPath = GetSelectedRootPathForHistoryEntry(null, filePath);
            if (string.IsNullOrWhiteSpace(selectedRootPath))
            {
                return;
            }

            string logPath = GetPathUsageLogFilePath();
            string? configDir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm}|{selectedRootPath}";
            File.AppendAllLines(logPath, new[] { entry });
        }

        private List<(string path, int count)> GetCurrentYearSelectedPathUsageData()
        {
            var pathCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string logPath = GetPathUsageLogFilePath();
            int currentYear = DateTime.Now.Year;

            if (!File.Exists(logPath))
            {
                return new List<(string path, int count)>();
            }

            foreach (string line in File.ReadAllLines(logPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split('|');
                if (parts.Length < 2)
                {
                    continue;
                }

                if (!DateTime.TryParse(parts[0], out DateTime timestamp) || timestamp.Year != currentYear)
                {
                    continue;
                }

                string normalizedPath = NormalizeInventoryPath(parts[1]);
                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                if (pathCounts.ContainsKey(normalizedPath))
                {
                    pathCounts[normalizedPath]++;
                }
                else
                {
                    pathCounts[normalizedPath] = 1;
                }
            }

            return pathCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        private string GetUnwatchedVideoCacheFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "unwatched_video_cache.json");
        }

        private UnwatchedVideoCacheFile LoadUnwatchedVideoCacheFile()
        {
            string filePath = GetUnwatchedVideoCacheFilePath();
            if (!File.Exists(filePath))
            {
                return new UnwatchedVideoCacheFile();
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var cacheFile = JsonSerializer.Deserialize<UnwatchedVideoCacheFile>(json) ?? new UnwatchedVideoCacheFile();
                return cacheFile.Version == UnwatchedVideoCacheVersion ? cacheFile : new UnwatchedVideoCacheFile();
            }
            catch
            {
                return new UnwatchedVideoCacheFile();
            }
        }

        private void SaveUnwatchedVideoCacheFile(UnwatchedVideoCacheFile cacheFile)
        {
            string filePath = GetUnwatchedVideoCacheFilePath();
            string configDir = Path.GetDirectoryName(filePath) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            cacheFile.Version = UnwatchedVideoCacheVersion;
            cacheFile.Year = DateTime.Now.Year;
            var options = new JsonSerializerOptions { WriteIndented = false };
            File.WriteAllText(filePath, JsonSerializer.Serialize(cacheFile, options));
        }

        private void EnsureUnwatchedCacheLoaded()
        {
            lock (unwatchedScanLock)
            {
                if (cachedUnwatchedVideoCount >= 0 || isUnwatchedScanInProgress)
                {
                    return;
                }

                EnsureCurrentYearUnwatchedCacheFile();
                var cacheFile = LoadUnwatchedVideoCacheFile();

                if (cacheFile.Count >= 0 &&
                    cacheFile.Year == DateTime.Now.Year)
                {
                    cachedUnwatchedVideoCount = cacheFile.Count;
                    cachedUnwatchedFolderDistribution = cacheFile.FolderDistribution
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.Folder))
                        .Select(entry => (entry.Folder, entry.Count))
                        .ToList();
                    unwatchedScanStatusMessage = "已加载上次未看过视频统计结果，点击可按当前路径重算。";
                }
            }
        }

        private VideoCodecCacheFile LoadVideoCodecCacheFile()
        {
            string filePath = GetVideoCodecCacheFilePath();
            if (!File.Exists(filePath))
            {
                return new VideoCodecCacheFile();
            }

            try
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<VideoCodecCacheFile>(json) ?? new VideoCodecCacheFile();
            }
            catch
            {
                return new VideoCodecCacheFile();
            }
        }

        private void SaveVideoCodecCacheFile(VideoCodecCacheFile cacheFile)
        {
            string filePath = GetVideoCodecCacheFilePath();
            string configDir = Path.GetDirectoryName(filePath) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var options = new JsonSerializerOptions { WriteIndented = false };
            File.WriteAllText(filePath, JsonSerializer.Serialize(cacheFile, options));
        }

        private VideoCodecUsageSummary GetCachedVideoCodecUsageSummary()
        {
            var currentVideos = GetEnabledVideoInventory()
                .Where(video => !string.IsNullOrWhiteSpace(video))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (currentVideos.Count == 0)
            {
                return new VideoCodecUsageSummary();
            }

            var cacheFile = LoadVideoCodecCacheFile();
            var validEntries = cacheFile.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
                .GroupBy(entry => NormalizeInventoryPath(entry.Path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(entry => entry.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);

            var codecCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var originalContainerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int detectedCount = 0;

            foreach (string videoPath in currentVideos)
            {
                string extension = Path.GetExtension(videoPath).ToLowerInvariant();
                if (extension == ".mkv" || extension == ".iso")
                {
                    string containerName = extension.TrimStart('.').ToUpperInvariant();
                    if (originalContainerCounts.ContainsKey(containerName))
                        originalContainerCounts[containerName]++;
                    else
                        originalContainerCounts[containerName] = 1;
                }

                string normalizedPath = NormalizeInventoryPath(videoPath);
                if (!validEntries.TryGetValue(normalizedPath, out VideoCodecCacheEntry? entry))
                {
                    continue;
                }

                if (!File.Exists(videoPath))
                {
                    continue;
                }

                FileInfo fileInfo = new FileInfo(videoPath);
                if (entry.FileSize != fileInfo.Length || entry.LastWriteTimeUtcTicks != fileInfo.LastWriteTimeUtc.Ticks)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.CodecName))
                {
                    continue;
                }

                string displayCodec = NormalizeCodecDisplayName(entry.CodecName);
                if (string.IsNullOrWhiteSpace(displayCodec))
                {
                    continue;
                }

                detectedCount++;
                if (codecCounts.ContainsKey(displayCodec))
                    codecCounts[displayCodec]++;
                else
                    codecCounts[displayCodec] = 1;
            }

            if (codecCounts.Count == 0 || detectedCount == 0)
            {
                if (originalContainerCounts.Count > 0)
                {
                    var topOriginalOnly = originalContainerCounts
                        .OrderByDescending(kvp => kvp.Value)
                        .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                        .First();

                    return new VideoCodecUsageSummary
                    {
                        HasData = true,
                        CodecDisplayName = topOriginalOnly.Key,
                        CodecCount = topOriginalOnly.Value,
                        TotalCount = currentVideos.Count,
                        Percentage = currentVideos.Count > 0 ? (double)topOriginalOnly.Value / currentVideos.Count * 100 : 0,
                        IsOriginalContainer = true
                    };
                }

                return new VideoCodecUsageSummary();
            }

            var topCodec = codecCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .First();

            if (originalContainerCounts.Count > 0)
            {
                var topOriginal = originalContainerCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .First();

                if (topOriginal.Value >= topCodec.Value)
                {
                    return new VideoCodecUsageSummary
                    {
                        HasData = true,
                        CodecDisplayName = topOriginal.Key,
                        CodecCount = topOriginal.Value,
                        TotalCount = currentVideos.Count,
                        Percentage = currentVideos.Count > 0 ? (double)topOriginal.Value / currentVideos.Count * 100 : 0,
                        IsOriginalContainer = true
                    };
                }
            }

            return new VideoCodecUsageSummary
            {
                HasData = true,
                CodecDisplayName = topCodec.Key,
                CodecCount = topCodec.Value,
                TotalCount = detectedCount,
                Percentage = (double)topCodec.Value / detectedCount * 100,
                IsOriginalContainer = false
            };
        }

        private void StartVideoCodecScanForAnnualReport()
        {
            lock (videoCodecScanLock)
            {
                if (isVideoCodecScanInProgress)
                {
                    return;
                }

                isVideoCodecScanInProgress = true;
                videoCodecScanStatusMessage = "正在统计当前启用路径的视频编码...";
            }

            Task.Run(() =>
            {
                string statusMessage = RefreshVideoCodecCacheForEnabledVideos();

                lock (videoCodecScanLock)
                {
                    isVideoCodecScanInProgress = false;
                    videoCodecScanStatusMessage = statusMessage;
                }

                if (annualReportFormInstance != null && !annualReportFormInstance.IsDisposed)
                {
                    try
                    {
                        annualReportFormInstance.BeginInvoke(new Action(() => RequestAnnualReportRefresh(true)));
                    }
                    catch { }
                }
            });
        }

        private void StartInventorySnapshotScanForAnnualReport()
        {
            lock (inventoryScanStatusLock)
            {
                if (isInventorySnapshotScanInProgress)
                {
                    return;
                }

                isInventorySnapshotScanInProgress = true;
                inventorySnapshotScanStatusMessage = "正在统计当前启用路径的视频增删变化...";
            }

            Task.Run(() =>
            {
                string statusMessage = string.Empty;
                try
                {
                    EnsureCurrentYearInventorySnapshot(force: true);
                }
                catch
                {
                    statusMessage = "库存快照统计失败，请稍后再试。";
                }

                lock (inventoryScanStatusLock)
                {
                    isInventorySnapshotScanInProgress = false;
                    inventorySnapshotScanStatusMessage = statusMessage;
                }

                if (annualReportFormInstance != null && !annualReportFormInstance.IsDisposed)
                {
                    try
                    {
                        annualReportFormInstance.BeginInvoke(new Action(() => RequestAnnualReportRefresh(true)));
                    }
                    catch { }
                }
            });
        }

        private string RefreshVideoCodecCacheForEnabledVideos()
        {
            string? ffprobePath = FindFfprobeExecutablePath();
            if (string.IsNullOrWhiteSpace(ffprobePath))
            {
                return "未找到 ffprobe，暂时无法读取视频编码。";
            }

            var currentVideos = GetEnabledVideoInventory()
                .Where(video => !string.IsNullOrWhiteSpace(video) && File.Exists(video))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (currentVideos.Count == 0)
            {
                return "当前没有可统计编码的视频。";
            }

            var cacheFile = LoadVideoCodecCacheFile();
            var cacheEntries = cacheFile.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
                .GroupBy(entry => NormalizeInventoryPath(entry.Path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(entry => entry.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);

            bool updated = false;
            int scannedCount = 0;

            foreach (string videoPath in currentVideos)
            {
                FileInfo fileInfo = new FileInfo(videoPath);
                string normalizedPath = NormalizeInventoryPath(videoPath);

                if (cacheEntries.TryGetValue(normalizedPath, out VideoCodecCacheEntry? existingEntry) &&
                    existingEntry.FileSize == fileInfo.Length &&
                    existingEntry.LastWriteTimeUtcTicks == fileInfo.LastWriteTimeUtc.Ticks &&
                    !string.IsNullOrWhiteSpace(existingEntry.CodecName))
                {
                    continue;
                }

                string codecName = GetVideoCodecNameWithFfprobe(ffprobePath, videoPath);
                cacheEntries[normalizedPath] = new VideoCodecCacheEntry
                {
                    Path = normalizedPath,
                    FileSize = fileInfo.Length,
                    LastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                    CodecName = codecName,
                    UpdatedAt = DateTime.Now
                };
                scannedCount++;
                updated = true;
            }

            if (updated)
            {
                cacheFile.Entries = cacheEntries.Values
                    .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                SaveVideoCodecCacheFile(cacheFile);
            }

            if (scannedCount > 0)
            {
                return $"已更新 {scannedCount} 个视频的编码信息。";
            }

            return "编码缓存已经是最新状态。";
        }

        private string? FindFfprobeExecutablePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "ffprobe.exe"),
                Path.Combine(baseDir, "ffmpeg", "bin", "ffprobe.exe"),
                Path.Combine(Application.StartupPath, "ffprobe.exe"),
                Path.Combine(Application.StartupPath, "ffmpeg", "bin", "ffprobe.exe"),
                "ffprobe"
            };

            foreach (string candidate in candidates)
            {
                if (string.Equals(candidate, "ffprobe", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using Process process = new Process();
                        process.StartInfo = new ProcessStartInfo
                        {
                            FileName = candidate,
                            Arguments = "-version",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        process.Start();
                        if (!process.WaitForExit(1500))
                        {
                            try { process.Kill(); } catch { }
                            continue;
                        }
                        if (process.ExitCode == 0)
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }
                else if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private string GetVideoCodecNameWithFfprobe(string ffprobePath, string videoPath)
        {
            try
            {
                using Process process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -select_streams v:0 -show_entries stream=codec_name -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd().Trim();
                string error = process.StandardError.ReadToEnd().Trim();

                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    return string.Empty;
                }

                if (process.ExitCode != 0)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(output))
                {
                    string firstLine = output
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault() ?? string.Empty;
                    return firstLine.Trim();
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string NormalizeCodecDisplayName(string codecName)
        {
            if (string.IsNullOrWhiteSpace(codecName))
            {
                return string.Empty;
            }

            return codecName.Trim().ToLowerInvariant() switch
            {
                "hevc" or "h265" => "HEVC",
                "h264" or "avc1" => "H.264",
                "av1" => "AV1",
                "vp9" => "VP9",
                "mpeg4" => "MPEG-4",
                "vc1" => "VC-1",
                "wmv3" => "WMV3",
                _ => codecName.Trim().ToUpperInvariant()
            };
        }

        private List<string> GetUnwatchedVideos()
        {
            HashSet<string> watchedVideos = GetWatchedVideoSetFromHistoryLog();
            var unwatchedVideos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string video in GetEnabledVideoInventory())
            {
                string canonicalKey = GetCanonicalVideoIdentityKey(video, video);
                if (string.IsNullOrWhiteSpace(canonicalKey) || watchedVideos.Contains(canonicalKey))
                {
                    continue;
                }

                if (!unwatchedVideos.ContainsKey(canonicalKey))
                {
                    unwatchedVideos[canonicalKey] = video;
                }
            }

            return unwatchedVideos.Values.ToList();
        }

        private HashSet<string> GetWatchedVideoSetFromHistoryLog()
        {
            var watchedVideos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");

            if (!File.Exists(historyPath))
            {
                return watchedVideos;
            }

            foreach (var line in File.ReadAllLines(historyPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split('|');
                if (parts.Length < 5) continue;

                string fileNameField = parts[2];
                string fullPath = parts[4];
                string extension = Path.GetExtension(fileNameField);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = Path.GetExtension(fullPath);
                }
                if (!SupportedVideoExtensions.Contains(extension)) continue;

                string rawVideoName = fileNameField;
                if (string.IsNullOrWhiteSpace(rawVideoName) && !string.IsNullOrWhiteSpace(fullPath))
                {
                    rawVideoName = Path.GetFileName(fullPath);
                }
                else if (!string.IsNullOrWhiteSpace(rawVideoName))
                {
                    rawVideoName = Path.GetFileName(rawVideoName);
                }

                string historyVideoPath = fullPath;
                if (!string.IsNullOrWhiteSpace(fullPath) &&
                    Directory.Exists(fullPath) &&
                    !string.IsNullOrWhiteSpace(rawVideoName))
                {
                    historyVideoPath = Path.Combine(fullPath, rawVideoName);
                }

                string canonicalKey = GetCanonicalVideoIdentityKey(rawVideoName, historyVideoPath);
                if (!string.IsNullOrWhiteSpace(canonicalKey))
                {
                    watchedVideos.Add(canonicalKey);
                }
            }

            return watchedVideos;
        }

        private List<(string folder, int count)> GetUnwatchedFolderDistribution()
        {
            var folderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (string videoPath in GetUnwatchedVideos())
            {
                string folderName = GetSmartFolderName(Path.GetDirectoryName(videoPath));
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    folderName = Path.GetFileName(Path.GetDirectoryName(videoPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = "未知文件夹";

                if (folderCounts.ContainsKey(folderName))
                    folderCounts[folderName]++;
                else
                    folderCounts[folderName] = 1;
            }

            return folderCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        private void StartUnwatchedVideoScanForAnnualReport()
        {
            lock (unwatchedScanLock)
            {
                if (isUnwatchedScanInProgress)
                {
                    return;
                }

                isUnwatchedScanInProgress = true;
                unwatchedScanStatusMessage = "正在统计当前启用路径里从未被选中过的视频...";
            }

            Task.Run(() =>
            {
                string statusMessage;
                try
                {
                    var unwatchedVideos = GetUnwatchedVideos();
                    var folderDistribution = GetUnwatchedFolderDistribution();
                    string enabledPathsSignature = string.Join("|", GetEnabledPathSnapshot());
                    string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");
                    long historyTicks = File.Exists(historyPath) ? File.GetLastWriteTimeUtc(historyPath).Ticks : 0;

                    SaveUnwatchedVideoCacheFile(new UnwatchedVideoCacheFile
                    {
                        EnabledPathsSignature = enabledPathsSignature,
                        HistoryTicks = historyTicks,
                        Count = unwatchedVideos.Count,
                        FolderDistribution = folderDistribution
                            .Select(item => new UnwatchedFolderCacheEntry { Folder = item.folder, Count = item.count })
                            .ToList(),
                        UpdatedAt = DateTime.Now
                    });

                    lock (unwatchedScanLock)
                    {
                        cachedUnwatchedVideoCount = unwatchedVideos.Count;
                        cachedUnwatchedFolderDistribution = folderDistribution;
                        statusMessage = folderDistribution.Count > 0 || unwatchedVideos.Count > 0
                            ? "未看过视频统计已更新。"
                            : "当前没有未看过视频，或者启用路径里没有可统计内容。";
                        isUnwatchedScanInProgress = false;
                        unwatchedScanStatusMessage = statusMessage;
                    }
                }
                catch
                {
                    lock (unwatchedScanLock)
                    {
                        cachedUnwatchedVideoCount = -1;
                        cachedUnwatchedFolderDistribution = new List<(string folder, int count)>();
                        isUnwatchedScanInProgress = false;
                        unwatchedScanStatusMessage = "未看过视频统计失败，请稍后再试。";
                    }
                }

                if (annualReportFormInstance != null && !annualReportFormInstance.IsDisposed)
                {
                    try
                    {
                        annualReportFormInstance.BeginInvoke(new Action(() => RequestAnnualReportRefresh(true)));
                    }
                    catch { }
                }
            });
        }

        private string GetInventorySnapshotFilePath(int year)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", $"video_inventory_snapshots_{year}.json");
        }

        private InventorySnapshotFile LoadInventorySnapshotFile(int year)
        {
            string filePath = GetInventorySnapshotFilePath(year);
            if (!File.Exists(filePath))
            {
                return new InventorySnapshotFile { Year = year };
            }

            try
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<InventorySnapshotFile>(json) ?? new InventorySnapshotFile { Year = year };
            }
            catch
            {
                return new InventorySnapshotFile { Year = year };
            }
        }

        private void SaveInventorySnapshotFile(InventorySnapshotFile snapshotFile)
        {
            string filePath = GetInventorySnapshotFilePath(snapshotFile.Year);
            string configDir = Path.GetDirectoryName(filePath) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var options = new JsonSerializerOptions { WriteIndented = false };
            File.WriteAllText(filePath, JsonSerializer.Serialize(snapshotFile, options));
        }

        private void EnsureCurrentYearInventorySnapshot(bool force = false)
        {
            lock (inventorySnapshotLock)
            {
                int currentYear = DateTime.Now.Year;
                var snapshotFile = LoadInventorySnapshotFile(currentYear);
                var latestSnapshot = snapshotFile.Snapshots
                    .OrderByDescending(snapshot => snapshot.Timestamp)
                    .FirstOrDefault();

                var enabledPaths = GetEnabledPathSnapshot();
                var currentVideos = GetEnabledVideoInventory()
                    .OrderBy(video => video, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                bool sameEnabledPaths = latestSnapshot != null &&
                    latestSnapshot.EnabledPaths.Count == enabledPaths.Count &&
                    latestSnapshot.EnabledPaths.SequenceEqual(enabledPaths, StringComparer.OrdinalIgnoreCase);
                bool sameContent = latestSnapshot != null &&
                    latestSnapshot.Videos.Count == currentVideos.Count &&
                    latestSnapshot.Videos.SequenceEqual(currentVideos, StringComparer.OrdinalIgnoreCase);

                if (latestSnapshot == null || !sameEnabledPaths || !sameContent)
                {
                    snapshotFile.Snapshots.Add(new InventorySnapshot
                    {
                        Timestamp = DateTime.Now,
                        EnabledPaths = enabledPaths,
                        Videos = currentVideos
                    });

                    snapshotFile.Snapshots = snapshotFile.Snapshots
                        .OrderBy(snapshot => snapshot.Timestamp)
                        .ToList();

                    SaveInventorySnapshotFile(snapshotFile);
                }
            }
        }

        private (int addedCount, int deletedCount) GetYearlyInventoryChangeCounts(int year)
        {
            var snapshotFile = LoadInventorySnapshotFile(year);
            if (snapshotFile.Snapshots.Count < 2)
            {
                return (0, 0);
            }

            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var snapshots = snapshotFile.Snapshots.OrderBy(snapshot => snapshot.Timestamp).ToList();

            for (int i = 1; i < snapshots.Count; i++)
            {
                var previousSnapshot = snapshots[i - 1];
                var currentSnapshot = snapshots[i];

                var previousEnabledPaths = new HashSet<string>(
                    previousSnapshot.EnabledPaths
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(NormalizeInventoryPath),
                    StringComparer.OrdinalIgnoreCase);
                var currentEnabledPaths = new HashSet<string>(
                    currentSnapshot.EnabledPaths
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(NormalizeInventoryPath),
                    StringComparer.OrdinalIgnoreCase);

                if (previousEnabledPaths.Count == 0 || currentEnabledPaths.Count == 0)
                {
                    continue;
                }

                var commonEnabledPaths = new HashSet<string>(previousEnabledPaths, StringComparer.OrdinalIgnoreCase);
                commonEnabledPaths.IntersectWith(currentEnabledPaths);

                if (commonEnabledPaths.Count == 0)
                {
                    continue;
                }

                var previous = new HashSet<string>(
                    previousSnapshot.Videos
                        .Where(video => IsVideoUnderEnabledPaths(video, commonEnabledPaths)),
                    StringComparer.OrdinalIgnoreCase);
                var current = new HashSet<string>(
                    currentSnapshot.Videos
                        .Where(video => IsVideoUnderEnabledPaths(video, commonEnabledPaths)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (string video in current.Except(previous, StringComparer.OrdinalIgnoreCase))
                {
                    added.Add(video);
                }

                foreach (string video in previous.Except(current, StringComparer.OrdinalIgnoreCase))
                {
                    deleted.Add(video);
                }
            }

            // 同一个文件如果年内只是临时删掉又还原，或新增后又删掉，就不算年度净变化。
            var transientChanges = added.Intersect(deleted, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string video in transientChanges)
            {
                added.Remove(video);
                deleted.Remove(video);
            }

            return (added.Count, deleted.Count);
        }

        private string GetFavoriteYearStatsFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "favorite_year_stats.txt");
        }

        private string GetFavoriteImagePackYearStatsFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "favorite_image_pack_year_stats.txt");
        }

        private string BuildYearArchivedFilePath(string filePath, int year)
        {
            string directory = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            string candidatePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{year}{extension}");
            int index = 1;

            while (File.Exists(candidatePath))
            {
                candidatePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{year}_{index}{extension}");
                index++;
            }

            return candidatePath;
        }

        private void ArchiveFileWithYearSuffix(string filePath, int year)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            string archivedPath = BuildYearArchivedFilePath(filePath, year);
            File.Move(filePath, archivedPath);
        }

        private bool TryLoadAnnualCountState(string filePath, out AnnualCountState state)
        {
            state = new AnnualCountState();
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                string line = File.ReadLines(filePath)
                    .FirstOrDefault(content => !string.IsNullOrWhiteSpace(content))?
                    .Trim() ?? string.Empty;

                var match = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"^YEAR=(\d+)\s+BASELINE=(\d+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (!match.Success)
                {
                    return false;
                }

                state.Year = int.Parse(match.Groups[1].Value);
                state.BaselineCount = int.Parse(match.Groups[2].Value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SaveAnnualCountState(string filePath, AnnualCountState state)
        {
            string? configDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            File.WriteAllText(filePath, $"YEAR={state.Year} BASELINE={state.BaselineCount}");
        }

        private void EnsureCurrentYearAnnualCountFile(string filePath, int currentYear, int currentTotalCount)
        {
            if (TryLoadAnnualCountState(filePath, out AnnualCountState state))
            {
                if (state.Year == currentYear)
                {
                    return;
                }

                ArchiveFileWithYearSuffix(filePath, state.Year);
                SaveAnnualCountState(filePath, new AnnualCountState
                {
                    Year = currentYear,
                    BaselineCount = currentTotalCount
                });
                return;
            }

            if (File.Exists(filePath))
            {
                var legacyRecords = LoadFavoriteYearRecords(filePath);
                if (legacyRecords.Count > 0)
                {
                    int latestYear = legacyRecords.Max(record => record.Year);
                    int currentYearAdded = legacyRecords
                        .Where(record => record.Year == currentYear)
                        .Select(record => record.AddedCount)
                        .DefaultIfEmpty(0)
                        .First();

                    ArchiveFileWithYearSuffix(filePath, latestYear);
                    SaveAnnualCountState(filePath, new AnnualCountState
                    {
                        Year = currentYear,
                        BaselineCount = currentYearAdded > 0
                            ? Math.Max(0, currentTotalCount - currentYearAdded)
                            : latestYear < currentYear ? currentTotalCount : 0
                    });
                    return;
                }

                ArchiveFileWithYearSuffix(filePath, Math.Max(0, currentYear - 1));
                SaveAnnualCountState(filePath, new AnnualCountState
                {
                    Year = currentYear,
                    BaselineCount = currentTotalCount
                });
                return;
            }

            string directory = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            bool hasArchivedFiles = Directory.Exists(directory) &&
                Directory.GetFiles(directory, $"{fileNameWithoutExtension}_*{extension}").Length > 0;

            SaveAnnualCountState(filePath, new AnnualCountState
            {
                Year = currentYear,
                BaselineCount = hasArchivedFiles ? currentTotalCount : 0
            });
        }

        private void EnsureCurrentYearUnwatchedCacheFile()
        {
            string filePath = GetUnwatchedVideoCacheFilePath();
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var cacheFile = JsonSerializer.Deserialize<UnwatchedVideoCacheFile>(json);
                int cacheYear = cacheFile?.Year > 0
                    ? cacheFile.Year
                    : cacheFile?.UpdatedAt.Year > 0
                        ? cacheFile.UpdatedAt.Year
                        : File.GetLastWriteTime(filePath).Year;

                if (cacheYear > 0 && cacheYear != DateTime.Now.Year)
                {
                    ArchiveFileWithYearSuffix(filePath, cacheYear);
                }
            }
            catch
            {
                int fallbackYear = File.GetLastWriteTime(filePath).Year;
                if (fallbackYear > 0 && fallbackYear != DateTime.Now.Year)
                {
                    ArchiveFileWithYearSuffix(filePath, fallbackYear);
                }
            }
        }

        private List<FavoriteYearRecord> LoadFavoriteYearRecords(string filePath)
        {
            var records = new List<FavoriteYearRecord>();

            if (!File.Exists(filePath))
            {
                return records;
            }

            foreach (string line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                if (int.TryParse(parts[0], out int year) && int.TryParse(parts[1], out int addedCount))
                {
                    records.Add(new FavoriteYearRecord
                    {
                        Year = year,
                        AddedCount = addedCount
                    });
                }
            }

            return records.OrderBy(record => record.Year).ToList();
        }

        private void SaveFavoriteYearRecords(string filePath, List<FavoriteYearRecord> records)
        {
            string? configDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var lines = records
                .OrderBy(record => record.Year)
                .Select(record => $"{record.Year} {record.AddedCount}")
                .ToList();

            File.WriteAllLines(filePath, lines);
        }

        private (int totalCount, int addedThisYear) GetOrUpdateFavoriteYearStats(string filePath, int year, int currentFavoriteCount)
        {
            EnsureCurrentYearAnnualCountFile(filePath, year, currentFavoriteCount);
            if (!TryLoadAnnualCountState(filePath, out AnnualCountState state))
            {
                state = new AnnualCountState { Year = year, BaselineCount = 0 };
                SaveAnnualCountState(filePath, state);
            }

            int addedThisYear = Math.Max(0, currentFavoriteCount - state.BaselineCount);
            return (currentFavoriteCount, addedThisYear);
        }

        private int GetFavoriteImagePackCount()
        {
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
            return favoritesManager.GetAllFavorites()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => imageExtensions.Contains(Path.GetExtension(path).ToLower()))
                .GroupBy(path => Path.GetDirectoryName(path), StringComparer.OrdinalIgnoreCase)
                .Count(group => !string.IsNullOrWhiteSpace(group.Key));
        }

        private int GetFavoriteImagePackRewatchCount(int year)
        {
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
            var favoriteImageFolders = favoritesManager.GetAllFavorites()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => imageExtensions.Contains(Path.GetExtension(path).ToLower()))
                .Select(path => Path.GetDirectoryName(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (favoriteImageFolders.Count == 0)
            {
                return 0;
            }

            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");
            if (!File.Exists(historyPath))
            {
                return 0;
            }

            int count = 0;
            foreach (string line in File.ReadAllLines(historyPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Split('|');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], out DateTime timestamp) || timestamp.Year != year) continue;
                if (!int.TryParse(parts[3], out int type) || type != 2) continue;

                string fileName = parts[2];
                string fullPath = parts[4];
                string extension = Path.GetExtension(fileName);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = Path.GetExtension(fullPath);
                }
                if (!imageExtensions.Contains(extension.ToLower())) continue;

                string? folderPath = null;
                if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                {
                    folderPath = Path.GetDirectoryName(fullPath);
                }
                else if (!string.IsNullOrWhiteSpace(fullPath) && Directory.Exists(fullPath))
                {
                    folderPath = fullPath;
                }

                if (!string.IsNullOrWhiteSpace(folderPath) && favoriteImageFolders.Contains(folderPath))
                {
                    count++;
                }
            }

            return count;
        }

        private int GetFavoriteImagePackNeverRewatchedCount(int year)
        {
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
            var favoriteImageFolders = favoritesManager.GetAllFavorites()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => imageExtensions.Contains(Path.GetExtension(path).ToLower()))
                .Select(path => Path.GetDirectoryName(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (favoriteImageFolders.Count == 0)
            {
                return 0;
            }

            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");
            if (!File.Exists(historyPath))
            {
                return favoriteImageFolders.Count;
            }

            var rewatchedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(historyPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Split('|');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], out DateTime timestamp) || timestamp.Year != year) continue;
                if (!int.TryParse(parts[3], out int type)) continue;
                if (type != 2 && type != 3) continue;

                string fileName = parts[2];
                string fullPath = parts[4];
                string extension = Path.GetExtension(fileName);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = Path.GetExtension(fullPath);
                }
                if (!imageExtensions.Contains(extension.ToLower())) continue;

                string? folderPath = null;
                if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                {
                    folderPath = Path.GetDirectoryName(fullPath);
                }
                else if (!string.IsNullOrWhiteSpace(fullPath) && Directory.Exists(fullPath))
                {
                    folderPath = fullPath;
                }

                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    rewatchedFolders.Add(folderPath);
                }
            }

            return favoriteImageFolders.Count(folder => !string.IsNullOrWhiteSpace(folder) && !rewatchedFolders.Contains(folder));
        }

        private (string favoriteName, int count) GetMostRewatchedFavoriteImagePack(int year)
        {
            string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
            var favoriteImageFolders = favoritesManager.GetAllFavorites()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => imageExtensions.Contains(Path.GetExtension(path).ToLower()))
                .Select(path => Path.GetDirectoryName(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (favoriteImageFolders.Count == 0)
            {
                return ("", 0);
            }

            var favoriteFolderSet = new HashSet<string>(favoriteImageFolders.Cast<string>(), StringComparer.OrdinalIgnoreCase);
            var folderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");

            if (!File.Exists(historyPath))
            {
                return ("", 0);
            }

            foreach (string line in File.ReadAllLines(historyPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Split('|');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], out DateTime timestamp) || timestamp.Year != year) continue;
                if (!int.TryParse(parts[3], out int type)) continue;
                if (type != 2 && type != 3) continue;

                string fileName = parts[2];
                string fullPath = parts[4];
                string extension = Path.GetExtension(fileName);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    extension = Path.GetExtension(fullPath);
                }
                if (!imageExtensions.Contains(extension.ToLower())) continue;

                string? folderPath = null;
                if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                {
                    folderPath = Path.GetDirectoryName(fullPath);
                }
                else if (!string.IsNullOrWhiteSpace(fullPath) && Directory.Exists(fullPath))
                {
                    folderPath = fullPath;
                }

                if (string.IsNullOrWhiteSpace(folderPath) || !favoriteFolderSet.Contains(folderPath))
                {
                    continue;
                }

                folderCounts[folderPath] = folderCounts.GetValueOrDefault(folderPath) + 1;
            }

            if (folderCounts.Count == 0)
            {
                return ("", 0);
            }

            var topFolder = folderCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .First();

            return (GetSmartFolderName(topFolder.Key), topFolder.Value);
        }

        private int GetFavoriteNeverRewatchedCount(int year)
        {
            var currentFavorites = favoritesManager.GetAllFavorites()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (currentFavorites.Count == 0)
            {
                return 0;
            }

            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");
            if (!File.Exists(historyPath))
            {
                return currentFavorites.Count;
            }

            var rewatchedFavorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(historyPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Split('|');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], out DateTime timestamp) || timestamp.Year != year) continue;
                if (!int.TryParse(parts[3], out int type)) continue;
                if (type != 2 && type != 3) continue;

                string fullPath = parts[4];
                string fileName = parts[2];

                if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                {
                    rewatchedFavorites.Add(fullPath);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(fullPath) &&
                    Directory.Exists(fullPath) &&
                    !string.IsNullOrWhiteSpace(fileName))
                {
                    rewatchedFavorites.Add(Path.Combine(fullPath, fileName));
                }
            }

            return currentFavorites.Count(path => !rewatchedFavorites.Contains(path));
        }

        private (string favoriteName, int count) GetMostRewatchedFavorite(int year)
        {
            var currentFavorites = favoritesManager.GetAllFavorites()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (currentFavorites.Count == 0)
            {
                return ("", 0);
            }

            var favoriteSet = new HashSet<string>(currentFavorites, StringComparer.OrdinalIgnoreCase);
            var favoriteCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");

            if (!File.Exists(historyPath))
            {
                return ("", 0);
            }

            foreach (string line in File.ReadAllLines(historyPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Split('|');
                if (parts.Length < 5) continue;
                if (!DateTime.TryParse(parts[0], out DateTime timestamp) || timestamp.Year != year) continue;
                if (!int.TryParse(parts[3], out int type)) continue;
                if (type != 2 && type != 3) continue;

                string fullPath = parts[4];
                string fileName = parts[2];
                string normalizedPath = "";

                if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                {
                    normalizedPath = fullPath;
                }
                else if (!string.IsNullOrWhiteSpace(fullPath) &&
                    Directory.Exists(fullPath) &&
                    !string.IsNullOrWhiteSpace(fileName))
                {
                    normalizedPath = Path.Combine(fullPath, fileName);
                }

                if (string.IsNullOrWhiteSpace(normalizedPath) || !favoriteSet.Contains(normalizedPath))
                {
                    continue;
                }

                if (favoriteCounts.ContainsKey(normalizedPath))
                    favoriteCounts[normalizedPath]++;
                else
                    favoriteCounts[normalizedPath] = 1;
            }

            if (favoriteCounts.Count == 0)
            {
                return ("", 0);
            }

            var favorite = favoriteCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .First();

            return (Path.GetFileName(favorite.Key), favorite.Value);
        }
        
        // 获取年度统计数据
        private YearlyStatistics GetYearlyStatistics()
        {
            string historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "History.log");
            string backgroundLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "BackgroundUsage.log");
            string snapshotPath = GetInventorySnapshotFilePath(DateTime.Now.Year);
            EnsureUnwatchedCacheLoaded();
            string enabledPathsSignature = string.Join("|", pathManager.GetEnabledPaths()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            long historyTicks = File.Exists(historyPath) ? File.GetLastWriteTimeUtc(historyPath).Ticks : 0;
            long backgroundTicks = File.Exists(backgroundLogPath) ? File.GetLastWriteTimeUtc(backgroundLogPath).Ticks : 0;
            long snapshotTicks = File.Exists(snapshotPath) ? File.GetLastWriteTimeUtc(snapshotPath).Ticks : 0;
            string cacheKey = $"{DateTime.Now.Year}|{historyTicks}|{backgroundTicks}|{snapshotTicks}|{enabledPathsSignature}|{cachedEnabledPathsSignature}|{cachedEnabledVideosAt.Ticks}";

            if (cachedYearlyStatistics != null && string.Equals(cachedYearlyStatisticsKey, cacheKey, StringComparison.Ordinal))
            {
                return cachedYearlyStatistics;
            }

            var stats = new YearlyStatistics();
            var currentYear = DateTime.Now.Year;
            
            try
            {
                // 获取所有历史记录（视频和图片）
                var allEntries = new List<(DateTime timestamp, string fileName, string fullPath, int type)>();
                
                // 读取历史文件 - 使用正确的路径
                if (File.Exists(historyPath))
                {
                    var lines = File.ReadAllLines(historyPath);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        var parts = line.Split('|');
                        if (parts.Length >= 5)
                        {
                            if (DateTime.TryParse(parts[0], out DateTime timestamp) && 
                                int.TryParse(parts[3], out int entryType) &&
                                timestamp.Year == currentYear)
                            {
                                allEntries.Add((timestamp, parts[2], parts[4], entryType));
                            }
                        }
                    }
                }
                
                if (allEntries.Count == 0)
                {
                    // 如果没有数据，返回默认值
                    stats.HasHistoryData = false;
                    stats.ActiveDays = 0;
                    stats.PeakDate = DateTime.Now;
                    stats.PeakDayClicks = 0;
                    stats.PeakMonth = DateTime.Now.Month;
                    stats.PeakMonthClicks = 0;
                    return stats;
                }
                
                stats.HasHistoryData = true;

                // 计算活跃天数
                var activeDatesSet = new HashSet<DateTime>();
                foreach (var entry in allEntries)
                {
                    activeDatesSet.Add(entry.timestamp.Date);
                }
                stats.ActiveDays = activeDatesSet.Count;
                
                // 计算每日点击量，找出峰值日
                var dailyClicks = new Dictionary<DateTime, int>();
                foreach (var entry in allEntries)
                {
                    var date = entry.timestamp.Date;
                    if (dailyClicks.ContainsKey(date))
                        dailyClicks[date]++;
                    else
                        dailyClicks[date] = 1;
                }
                
                DateTime peakDate = DateTime.Now;
                int maxDailyClicks = 0;
                foreach (var kvp in dailyClicks)
                {
                    if (kvp.Value > maxDailyClicks)
                    {
                        maxDailyClicks = kvp.Value;
                        peakDate = kvp.Key;
                    }
                }
                stats.PeakDate = peakDate;
                stats.PeakDayClicks = maxDailyClicks;
                
                // 计算每月点击量，找出峰值月
                var monthlyClicks = new Dictionary<int, int>();
                foreach (var entry in allEntries)
                {
                    int month = entry.timestamp.Month;
                    if (monthlyClicks.ContainsKey(month))
                        monthlyClicks[month]++;
                    else
                        monthlyClicks[month] = 1;
                }
                
                int peakMonth = DateTime.Now.Month;
                int maxMonthlyClicks = 0;
                foreach (var kvp in monthlyClicks)
                {
                    if (kvp.Value > maxMonthlyClicks)
                    {
                        maxMonthlyClicks = kvp.Value;
                        peakMonth = kvp.Key;
                    }
                }
                stats.PeakMonth = peakMonth;
                stats.PeakMonthClicks = maxMonthlyClicks;
                
                // 检查特殊日期（情人节2/14，七夕农历7/7，这里简化为8/22左右）
                var valentinesDay = new DateTime(currentYear, 2, 14);
                var qixiDay = new DateTime(currentYear, 8, 22); // 简化处理
                
                if (dailyClicks.ContainsKey(valentinesDay))
                {
                    stats.SpecialDateClicks = dailyClicks[valentinesDay];
                    stats.SpecialDateName = "情人节";
                }
                else if (dailyClicks.ContainsKey(qixiDay))
                {
                    stats.SpecialDateClicks = dailyClicks[qixiDay];
                    stats.SpecialDateName = "七夕";
                }
                
                // 计算最长不活跃天数
                if (activeDatesSet.Count > 1)
                {
                    var activeDatesList = new List<DateTime>(activeDatesSet);
                    activeDatesList.Sort();
                    
                    int maxStreak = 0;
                    for (int i = 1; i < activeDatesList.Count; i++)
                    {
                        int daysDiff = (activeDatesList[i] - activeDatesList[i-1]).Days - 1;
                        if (daysDiff > maxStreak)
                        {
                            maxStreak = daysDiff;
                        }
                    }
                    stats.MaxInactiveStreak = maxStreak;
                }
                
                // 计算早晨点击（6:00-9:00）
                int morningCount = 0;
                DateTime earliestTime = DateTime.MaxValue;
                foreach (var entry in allEntries)
                {
                    if (entry.timestamp.Hour >= 6 && entry.timestamp.Hour < 9)
                    {
                        morningCount++;
                        if (entry.timestamp.TimeOfDay < earliestTime.TimeOfDay)
                        {
                            earliestTime = entry.timestamp;
                        }
                    }
                }
                stats.EarlyMorningClicks = morningCount;
                if (morningCount > 0)
                {
                    stats.EarliestTime = earliestTime;
                }
                
                // 计算深夜点击比例（1:00-3:00）
                int lateNightCount = 0;
                foreach (var entry in allEntries)
                {
                    if (entry.timestamp.Hour >= 1 && entry.timestamp.Hour < 3)
                    {
                        lateNightCount++;
                    }
                }
                stats.LateNightPercentage = allEntries.Count > 0 ? (double)lateNightCount / allEntries.Count * 100 : 0;
                
                // 计算最活跃的小时
                var hourlyClicks = new Dictionary<int, int>();
                for (int i = 0; i < 24; i++)
                {
                    hourlyClicks[i] = 0;
                }
                foreach (var entry in allEntries)
                {
                    hourlyClicks[entry.timestamp.Hour]++;
                }
                var peakHourEntry = hourlyClicks.OrderByDescending(kvp => kvp.Value).First();
                stats.PeakHour = peakHourEntry.Key;
                stats.PeakHourClicks = peakHourEntry.Value;

                // 计算最常使用的路径（只统计已记录的根路径使用日志）
                var pathUsageData = GetCurrentYearSelectedPathUsageData();
                if (pathUsageData.Count > 0)
                {
                    var mostUsed = pathUsageData[0];
                    stats.MostUsedPath = mostUsed.path;
                    stats.MostUsedPathCount = mostUsed.count;
                }

                // 计算最常使用的背景（读取背景使用日志）
                var backgroundCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                
                if (File.Exists(backgroundLogPath))
                {
                    var bgLines = File.ReadAllLines(backgroundLogPath);
                    foreach (var line in bgLines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var parts = line.Split('|');
                        if (parts.Length >= 2)
                        {
                            if (DateTime.TryParse(parts[0], out DateTime bgTimestamp) && bgTimestamp.Year == currentYear)
                            {
                                string bgKey = NormalizeBackgroundUsageKey(parts[1]);
                                if (string.IsNullOrWhiteSpace(bgKey))
                                    continue;

                                if (backgroundCounts.ContainsKey(bgKey))
                                    backgroundCounts[bgKey]++;
                                else
                                    backgroundCounts[bgKey] = 1;
                            }
                        }
                    }
                }

                if (backgroundCounts.Count > 0)
                {
                    var mostUsedBg = backgroundCounts.OrderByDescending(kvp => kvp.Value).First();
                    stats.MostUsedBackground = mostUsedBg.Key;
                    stats.MostUsedBackgroundCount = mostUsedBg.Value;
                }

                // 计算最常观看的视频（按当年 History.log 的完整视频记录统计）
                var videoCounts = GetCurrentYearVideoWatchCountsFromHistoryLog();

                if (videoCounts.Count > 0)
                {
                    var mostWatched = videoCounts.OrderByDescending(kvp => kvp.Value).First();
                    stats.MostWatchedVideo = mostWatched.Key;
                    stats.MostWatchedVideoCount = mostWatched.Value;
                }

                var teacherCounts = GetCurrentYearTeacherWatchCountsFromHistoryLog();
                if (teacherCounts.Count > 0)
                {
                    var favoriteTeacher = teacherCounts.OrderByDescending(kvp => kvp.Value).First();
                    stats.FavoriteTeacher = favoriteTeacher.Key;
                    stats.FavoriteTeacherCount = favoriteTeacher.Value;
                }

                stats.FavoriteRewatchCount = allEntries.Count(entry => entry.type == 2);

                EnsureUnwatchedCacheLoaded();
                lock (unwatchedScanLock)
                {
                    stats.UnwatchedVideoCount = Math.Max(0, cachedUnwatchedVideoCount);
                }
                var inventoryChanges = GetYearlyInventoryChangeCounts(currentYear);
                stats.AddedVideoCount = inventoryChanges.addedCount;
                stats.DeletedVideoCount = inventoryChanges.deletedCount;

                int totalFavorites = favoritesManager.GetAllFavorites().Count;
                var favoriteYearStats = GetOrUpdateFavoriteYearStats(GetFavoriteYearStatsFilePath(), currentYear, totalFavorites);
                stats.TotalFavoriteVideoCount = favoriteYearStats.totalCount;
                stats.AddedFavoriteVideoCount = favoriteYearStats.addedThisYear;
                stats.FavoriteNeverRewatchedCount = GetFavoriteNeverRewatchedCount(currentYear);
                var mostRewatchedFavorite = GetMostRewatchedFavorite(currentYear);
                stats.MostRewatchedFavorite = mostRewatchedFavorite.favoriteName;
                stats.MostRewatchedFavoriteCount = mostRewatchedFavorite.count;

                int totalImagePackFavorites = GetFavoriteImagePackCount();
                var favoriteImagePackYearStats = GetOrUpdateFavoriteYearStats(GetFavoriteImagePackYearStatsFilePath(), currentYear, totalImagePackFavorites);
                stats.TotalFavoriteImagePackCount = favoriteImagePackYearStats.totalCount;
                stats.AddedFavoriteImagePackCount = favoriteImagePackYearStats.addedThisYear;
                stats.FavoriteImagePackRewatchCount = GetFavoriteImagePackRewatchCount(currentYear);
                stats.FavoriteImagePackNeverRewatchedCount = GetFavoriteImagePackNeverRewatchedCount(currentYear);
                var mostRewatchedFavoriteImagePack = GetMostRewatchedFavoriteImagePack(currentYear);
                stats.MostRewatchedFavoriteImagePack = mostRewatchedFavoriteImagePack.favoriteName;
                stats.MostRewatchedFavoriteImagePackCount = mostRewatchedFavoriteImagePack.count;

                var codePrefixCounts = GetCurrentYearCodePrefixWatchCountsFromHistoryLog();
                if (codePrefixCounts.Count > 0)
                {
                    var favoriteCodePrefix = codePrefixCounts
                        .OrderByDescending(kvp => kvp.Value)
                        .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                        .First();
                    stats.FavoriteCodePrefix = favoriteCodePrefix.Key;
                    stats.FavoriteCodePrefixCount = favoriteCodePrefix.Value;
                }

                EnsureTeacherCollectionCacheLoaded();
                lock (teacherCollectionScanLock)
                {
                    stats.FavoriteTeacherCollection = cachedFavoriteTeacherCollection;
                    stats.FavoriteTeacherCollectionCount = Math.Max(0, cachedFavoriteTeacherCollectionCount);
                }
            }
            catch (Exception ex)
            {
                // 出错时返回默认值
                Console.WriteLine($"Error calculating yearly statistics: {ex.Message}");
                stats.HasHistoryData = false;
                stats.ActiveDays = 0;
                stats.PeakDate = DateTime.Now;
                stats.PeakDayClicks = 0;
                stats.PeakMonth = DateTime.Now.Month;
                stats.PeakMonthClicks = 0;
            }

            cachedYearlyStatistics = stats;
            cachedYearlyStatisticsKey = cacheKey;
            return stats;
        }

        // 绘制Panel 1的时间主题内容
        private void DrawPanel1Content(Graphics g, int panelWidth, int panelHeight)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            
            // 清空panel1的卡片区域列表
            panel1CardAreas.Clear();
            currentCardAreas = panel1CardAreas; // 设置当前使用的列表
            
            int yPos = S(15);  // 从顶部开始，不留标题空间
            int leftMargin = S(15);
            int maxWidth = panelWidth - S(30);
            
            // 获取真实统计数据
            var yearStats = GetYearlyStatistics();
            bool hasHistoryData = yearStats.HasHistoryData;
            
            // 固定触发的三个卡片
            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetPoZhenMuLeiTitle(yearStats.ActiveDays), 
                $"全年 {yearStats.ActiveDays} 天，您都懂得取悦自己",
                hasHistoryData ? GetPoZhenMuLeiSubText(yearStats.ActiveDays) : string.Empty, true, hasHistoryData);
            
            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetInactiveStreakTitle(yearStats.MaxInactiveStreak),
                $"最高连续 {yearStats.MaxInactiveStreak} 天没点开任何文件夹",
                hasHistoryData ? GetInactiveStreakSubText(yearStats.MaxInactiveStreak) : string.Empty, true, hasHistoryData);
            
            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetPeakHourTitle(yearStats.PeakHour),
                GetPeakHourMainText(yearStats.PeakHour),
                hasHistoryData ? GetPeakHourSubText(yearStats.PeakHour) : string.Empty, true, hasHistoryData);
                
            // 条件触发内容（收集所有满足条件的成就，然后随机选择一个显示）
            var conditionalAchievements = new List<(string title, string mainText, string subText)>();
            
            if (yearStats.SpecialDateClicks > 0)
            {
                var specialDateOptions = new[]
                {
                    ("孤狼意志", 
                     $"{yearStats.SpecialDateName}当天，您狂点 {yearStats.SpecialDateClicks} 次",
                     "别人在外面送花，您在被窝里刷图？月老看了都想给您递纸巾"),
                    ("单身贵族",
                     $"{yearStats.SpecialDateName}这天，点击了 {yearStats.SpecialDateClicks} 次",
                     "别人秀恩爱，您秀技术，各有各的精彩"),
                    ("独行侠",
                     $"{yearStats.SpecialDateName}当天的 {yearStats.SpecialDateClicks} 次点击",
                     "别人在约会，您在冲浪，您这是把孤独活成了艺术"),
                    ("反向过节",
                     $"{yearStats.SpecialDateName}，{yearStats.SpecialDateClicks} 次的狂欢",
                     "别人过情人节，您过\"情人劫\"，这波操作我给满分")
                };
                int specialDateIndex = GetNonRepeatingRandomIndex(specialDateOptions.Length, ref lastSpecialDateOptionIndex);
                var selected = specialDateOptions[specialDateIndex];
                conditionalAchievements.Add(selected);
            }
            
            if (yearStats.EarlyMorningClicks > 0)
            {
                var earlyMorningOptions = new[]
                {
                    ("精力旺盛",
                     $"曾在清晨 {yearStats.EarliestTime:HH:mm} 留下记录",
                     "那绝对不是起得早，那是压根就没打算给太阳面子"),
                    ("不眠之夜",
                     $"清晨 {yearStats.EarliestTime:HH:mm} 的活跃记录",
                     "这个点还在冲，您这是通宵了还是失眠了"),
                    ("夜猫子",
                     $"{yearStats.EarliestTime:HH:mm} 的时间戳说明了一切",
                     "您这作息表是反着来的吧，建议去看看中医"),
                    ("晨间勇士",
                     $"凌晨 {yearStats.EarliestTime:HH:mm} 依然在线",
                     "这个点不睡觉，您这是在挑战人体极限吗")
                };
                int earlyMorningIndex = GetNonRepeatingRandomIndex(earlyMorningOptions.Length, ref lastEarlyMorningOptionIndex);
                var selected = earlyMorningOptions[earlyMorningIndex];
                conditionalAchievements.Add(selected);
            }
            
            // 如果有满足条件的成就，随机选择一个显示（不可点击）
            if (conditionalAchievements.Count > 0)
            {
                int conditionalChoiceIndex = GetNonRepeatingRandomIndex(conditionalAchievements.Count, ref lastPanel1ConditionalChoiceIndex);
                var selectedAchievement = conditionalAchievements[conditionalChoiceIndex];
                DrawAchievement(g, ref yPos, leftMargin, maxWidth, selectedAchievement.title, selectedAchievement.mainText, selectedAchievement.subText, false);
            }
            else
            {
                DrawAchievement(g, ref yPos, leftMargin, maxWidth, "静候佳音",
                    "暂无额外年度记录。",
                    string.Empty, false, false);
            }
        }

        // 绘制Panel 2的内容（第二和第三个卡片）
        private void DrawPanel2Content(Graphics g, int panelWidth, int panelHeight)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            
            // 清空panel2的卡片区域列表
            panel2CardAreas.Clear();
            currentCardAreas = panel2CardAreas; // 设置当前使用的列表
            
            int yPos = S(15);
            int leftMargin = S(15);
            int maxWidth = panelWidth - S(30);
            
            // 获取真实统计数据
            var yearStats = GetYearlyStatistics();
            bool hasHistoryData = yearStats.HasHistoryData;
            
            // 绘制第1个卡片
            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetYuHuoFenShenTitle(yearStats.PeakDayClicks),
                $"{yearStats.PeakDate:M月d日} 点击量最高，达到 {yearStats.PeakDayClicks} 次",
                hasHistoryData ? GetYuHuoFenShenSubText(yearStats.PeakDayClicks) : string.Empty, true, hasHistoryData);
                
            // 绘制第2个卡片
            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetYueDuBaZhuTitle(yearStats.PeakMonthClicks),
                $"您在{yearStats.PeakMonth}月最猛，共起飞{yearStats.PeakMonthClicks} 次",
                hasHistoryData ? GetYueDuBaZhuSubText(yearStats.PeakMonthClicks) : string.Empty, true, hasHistoryData);

            // 绘制第3个卡片 - 最常使用路径
            string shortPath = ShortenPath(yearStats.MostUsedPath, 20);
            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetPathTitle(yearStats.MostUsedPathCount),
                $"最常光顾 {shortPath}，共 {yearStats.MostUsedPathCount} 次",
                hasHistoryData ? GetPathSubText(yearStats.MostUsedPathCount) : string.Empty, true, hasHistoryData);

            // 绘制第4个卡片 - 最常使用背景
            string backgroundMainText;
            if (yearStats.MostUsedBackgroundCount > 0)
            {
                string bgFileName = Path.GetFileName(yearStats.MostUsedBackground);
                string shortBgName = bgFileName.Length > 20 ? bgFileName.Substring(0, 17) + "..." : bgFileName;
                backgroundMainText = $"最爱背景：{shortBgName}，用了 {yearStats.MostUsedBackgroundCount} 次";
            }
            else
            {
                backgroundMainText = "最爱背景：暂无数据，点击统计。";
            }

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetBackgroundTitle(yearStats.MostUsedBackgroundCount),
                backgroundMainText,
                yearStats.MostUsedBackgroundCount > 0 ? GetBackgroundSubText(yearStats.MostUsedBackgroundCount) : string.Empty, true, yearStats.MostUsedBackgroundCount > 0);
        }

        // 绘制Panel 3的内容（视频相关卡片）
        private void DrawPanel3Content(Graphics g, int panelWidth, int panelHeight)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            
            // 清空panel3的卡片区域列表
            panel3CardAreas.Clear();
            currentCardAreas = panel3CardAreas; // 设置当前使用的列表
            
            int yPos = S(15);
            int leftMargin = S(15);
            int maxWidth = panelWidth - S(30);
            
            // 获取真实统计数据
            var yearStats = GetYearlyStatistics();
            bool hasHistoryData = yearStats.HasHistoryData;
            
            // 绘制第一个卡片 - 观看最多次数的视频
            string mostWatchedVideoMainText;
            if (yearStats.MostWatchedVideoCount > 0)
            {
                string videoFileName = yearStats.MostWatchedVideo;
                string shortVideoName = videoFileName.Length > 25 ? videoFileName.Substring(0, 22) + "..." : videoFileName;
                mostWatchedVideoMainText = $"观看最多次数的视频：{shortVideoName}";
            }
            else
            {
                mostWatchedVideoMainText = "观看最多次数的视频：暂无数据。";
            }

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetVideoTitle(yearStats.MostWatchedVideoCount),
                mostWatchedVideoMainText,
                yearStats.MostWatchedVideoCount > 0 ? GetVideoSubText(yearStats.MostWatchedVideoCount) : string.Empty, true, yearStats.MostWatchedVideoCount > 0);

            // 绘制第二个卡片 - 最爱老师
            string favoriteTeacherMainText;
            if (yearStats.FavoriteTeacherCount > 0)
            {
                string teacherName = yearStats.FavoriteTeacher;
                string shortTeacherName = teacherName.Length > 25 ? teacherName.Substring(0, 22) + "..." : teacherName;
                favoriteTeacherMainText = $"最爱老师：{shortTeacherName}，一共 {yearStats.FavoriteTeacherCount} 次。";
            }
            else
            {
                favoriteTeacherMainText = "最爱老师：暂无数据。";
            }

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetTeacherTitle(yearStats.FavoriteTeacherCount),
                favoriteTeacherMainText,
                yearStats.FavoriteTeacherCount > 0 ? GetTeacherSubText(yearStats.FavoriteTeacherCount) : string.Empty, true, yearStats.FavoriteTeacherCount > 0);

            bool unwatchedScanInProgress;
            string unwatchedStatusMessage;
            int unwatchedCount;
            lock (unwatchedScanLock)
            {
                unwatchedScanInProgress = isUnwatchedScanInProgress;
                unwatchedStatusMessage = unwatchedScanStatusMessage;
                unwatchedCount = cachedUnwatchedVideoCount;
            }

            string unwatchedMainText;
            if (unwatchedCount >= 0)
            {
                unwatchedMainText = $"共有 {unwatchedCount} 个视频从未被选中过。";
            }
            else
            {
                unwatchedMainText = "从未被选中过的视频：点击统计。";
            }

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetUnwatchedVideoTitle(unwatchedCount),
                unwatchedMainText,
                unwatchedCount >= 0 ? GetUnwatchedVideoSubText(unwatchedCount, false, unwatchedStatusMessage) : string.Empty, true, unwatchedCount >= 0);

            bool inventoryScanInProgress;
            string inventoryStatusMessage;
            lock (inventoryScanStatusLock)
            {
                inventoryScanInProgress = isInventorySnapshotScanInProgress;
                inventoryStatusMessage = inventorySnapshotScanStatusMessage;
            }

            string inventoryMainText = $"今年新增 {yearStats.AddedVideoCount} 个视频，删除 {yearStats.DeletedVideoCount} 个视频。";

            DrawAchievement(g, ref yPos, leftMargin, maxWidth,
                GetInventoryChangeTitle(yearStats.AddedVideoCount, yearStats.DeletedVideoCount),
                inventoryMainText,
                hasHistoryData ? GetInventoryChangeSubText(yearStats.AddedVideoCount, yearStats.DeletedVideoCount, false, inventoryStatusMessage) : string.Empty,
                true, hasHistoryData);
        }

        private void DrawPanel4Content(Graphics g, int panelWidth, int panelHeight)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            int yPos = S(15);
            int leftMargin = S(15);
            int maxWidth = panelWidth - S(30);

            var yearStats = GetYearlyStatistics();
            bool hasHistoryData = yearStats.HasHistoryData;

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetFavoriteCollectionTitle(yearStats.TotalFavoriteVideoCount, yearStats.AddedFavoriteVideoCount),
                $"视频收藏总数 {yearStats.TotalFavoriteVideoCount} 个，今年新增 {yearStats.AddedFavoriteVideoCount} 个。",
                hasHistoryData ? GetFavoriteCollectionSubText(yearStats.TotalFavoriteVideoCount, yearStats.AddedFavoriteVideoCount) : string.Empty, false, hasHistoryData);

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetFavoriteRewatchTitle(yearStats.FavoriteRewatchCount),
                $"重温视频收藏 {yearStats.FavoriteRewatchCount} 次。",
                hasHistoryData ? GetFavoriteRewatchSubText(yearStats.FavoriteRewatchCount) : string.Empty, false, hasHistoryData);

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetFavoriteNeverRewatchedTitle(yearStats.FavoriteNeverRewatchedCount),
                $"有{yearStats.FavoriteNeverRewatchedCount} 个视频收藏后从未看过。",
                hasHistoryData ? GetFavoriteNeverRewatchedSubText(yearStats.FavoriteNeverRewatchedCount) : string.Empty, false, hasHistoryData);

            string mostRewatchedFavoriteMainText;
            if (yearStats.MostRewatchedFavoriteCount > 0)
            {
                string shortFavoriteName = yearStats.MostRewatchedFavorite.Length > 25
                    ? yearStats.MostRewatchedFavorite.Substring(0, 22) + "..."
                    : yearStats.MostRewatchedFavorite;
                mostRewatchedFavoriteMainText = $"最爱的视频收藏是 {shortFavoriteName}。";
            }
            else
            {
                mostRewatchedFavoriteMainText = "最爱的视频收藏：暂无数据。";
            }

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetMostRewatchedFavoriteTitle(yearStats.MostRewatchedFavoriteCount),
                mostRewatchedFavoriteMainText,
                yearStats.MostRewatchedFavoriteCount > 0 ? GetMostRewatchedFavoriteSubText(yearStats.MostRewatchedFavoriteCount) : string.Empty, false, yearStats.MostRewatchedFavoriteCount > 0);
        }

        private void DrawPanel5Content(Graphics g, int panelWidth, int panelHeight)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            int yPos = S(15);
            int leftMargin = S(15);
            int maxWidth = panelWidth - S(30);

            var yearStats = GetYearlyStatistics();
            bool hasHistoryData = yearStats.HasHistoryData;

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetFavoriteImagePackTitle(yearStats.TotalFavoriteImagePackCount, yearStats.AddedFavoriteImagePackCount),
                $"图包收藏总数 {yearStats.TotalFavoriteImagePackCount} 个，今年新增 {yearStats.AddedFavoriteImagePackCount} 个。",
                hasHistoryData ? GetFavoriteImagePackSubText(yearStats.TotalFavoriteImagePackCount, yearStats.AddedFavoriteImagePackCount) : string.Empty, false, hasHistoryData);

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetFavoriteImagePackRewatchTitle(yearStats.FavoriteImagePackRewatchCount),
                $"重温图包收藏 {yearStats.FavoriteImagePackRewatchCount} 次。",
                hasHistoryData ? GetFavoriteImagePackRewatchSubText(yearStats.FavoriteImagePackRewatchCount) : string.Empty, false, hasHistoryData);

            DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetFavoriteImagePackNeverRewatchedTitle(yearStats.FavoriteImagePackNeverRewatchedCount),
                $"有 {yearStats.FavoriteImagePackNeverRewatchedCount} 个图包收藏后从未看过。",
                hasHistoryData ? GetFavoriteImagePackNeverRewatchedSubText(yearStats.FavoriteImagePackNeverRewatchedCount) : string.Empty, false, hasHistoryData);

            using (Font mainFont = new Font("微软雅黑", 12))
            {
                string favoriteImagePackMainText;
                if (yearStats.MostRewatchedFavoriteImagePackCount > 0)
                {
                    string mainTextPrefix = "最爱的图包收藏是 ";
                    string mainTextSuffix = "。";
                    int availableNameWidth = Math.Max(S(80), maxWidth - S(20) - (int)Math.Ceiling(g.MeasureString(mainTextPrefix + mainTextSuffix, mainFont).Width));
                    string shortFavoriteImagePackName = TruncateTextWithEllipsis(g, yearStats.MostRewatchedFavoriteImagePack, mainFont, availableNameWidth);
                    favoriteImagePackMainText = $"{mainTextPrefix}{shortFavoriteImagePackName}。";
                }
                else
                {
                    favoriteImagePackMainText = "最爱的图包收藏：暂无数据。";
                }

                DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetMostRewatchedFavoriteImagePackTitle(yearStats.MostRewatchedFavoriteImagePackCount),
                    favoriteImagePackMainText,
                    yearStats.MostRewatchedFavoriteImagePackCount > 0 ? GetMostRewatchedFavoriteImagePackSubText(yearStats.MostRewatchedFavoriteImagePackCount) : string.Empty, false, yearStats.MostRewatchedFavoriteImagePackCount > 0);
            }
        }

        private void DrawPanel6Content(Graphics g, int panelWidth, int panelHeight)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            panel6CardAreas.Clear();
            currentCardAreas = panel6CardAreas;

            int yPos = S(15);
            int leftMargin = S(15);
            int maxWidth = panelWidth - S(30);

            var yearStats = GetYearlyStatistics();
            var codecSummary = GetCachedVideoCodecUsageSummary();
            bool hasHistoryData = yearStats.HasHistoryData;

            using (Font mainFont = new Font("微软雅黑", 12))
            {
                string codePrefixMainText;
                if (yearStats.FavoriteCodePrefixCount > 0)
                {
                    string mainTextPrefix = "最爱的番号：";
                    string mainTextSuffix = "。";
                    int availablePrefixWidth = Math.Max(S(80), maxWidth - S(20) - (int)Math.Ceiling(g.MeasureString(mainTextPrefix + mainTextSuffix, mainFont).Width));
                    string shortCodePrefix = TruncateTextWithEllipsis(g, yearStats.FavoriteCodePrefix, mainFont, availablePrefixWidth);
                    codePrefixMainText = $"{mainTextPrefix}{shortCodePrefix}。";
                }
                else
                {
                    codePrefixMainText = "最爱的番号：暂无数据。";
                }

                DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetCodePrefixTitle(yearStats.FavoriteCodePrefixCount),
                    codePrefixMainText,
                    yearStats.FavoriteCodePrefixCount > 0 ? GetCodePrefixSubText(yearStats.FavoriteCodePrefixCount) : string.Empty, true, yearStats.FavoriteCodePrefixCount > 0);
            }

            bool codecScanInProgress;
            string codecStatusMessage;
            lock (videoCodecScanLock)
            {
                codecScanInProgress = isVideoCodecScanInProgress;
                codecStatusMessage = videoCodecScanStatusMessage;
            }

            string codecMainText;
            if (codecSummary.HasData)
            {
                codecMainText = codecSummary.IsOriginalContainer
                    ? $"原档王者 {codecSummary.CodecDisplayName}，占比 {codecSummary.Percentage:0.#}%。"
                    : $"最常用的编码是 {codecSummary.CodecDisplayName}，占比 {codecSummary.Percentage:0.#}%。";
            }
            else
            {
                codecMainText = "最常用的编码：点击统计。";
            }

            DrawAchievement(g, ref yPos, leftMargin, maxWidth,
                GetVideoCodecTitle(codecSummary.HasData, codecSummary.CodecDisplayName, false),
                codecMainText,
                codecSummary.HasData ? GetVideoCodecSubText(codecSummary.HasData, codecSummary.CodecDisplayName, false, codecStatusMessage) : string.Empty,
                true, codecSummary.HasData);

            bool teacherCollectionScanInProgress;
            string teacherCollectionStatusMessage;
            string favoriteTeacherCollectionName;
            int favoriteTeacherCollectionCount;
            lock (teacherCollectionScanLock)
            {
                teacherCollectionScanInProgress = isTeacherCollectionScanInProgress;
                teacherCollectionStatusMessage = teacherCollectionScanStatusMessage;
                favoriteTeacherCollectionName = cachedFavoriteTeacherCollection;
                favoriteTeacherCollectionCount = cachedFavoriteTeacherCollectionCount;
            }

            using (Font mainFont = new Font("微软雅黑", 12))
            {
                string mainTextPrefix = "库存最多的老师：";
                string mainTextSuffix = "。";
                string mainText;

                if (favoriteTeacherCollectionCount >= 0 && !string.IsNullOrWhiteSpace(favoriteTeacherCollectionName))
                {
                    int availableNameWidth = Math.Max(S(80), maxWidth - S(20) - (int)Math.Ceiling(g.MeasureString(mainTextPrefix + mainTextSuffix, mainFont).Width));
                    string shortTeacherName = TruncateTextWithEllipsis(g, favoriteTeacherCollectionName, mainFont, availableNameWidth);
                    mainText = $"{mainTextPrefix}{shortTeacherName}。";
                }
                else
                {
                    mainText = "库存最多的老师：点击统计。";
                }

                DrawAchievement(g, ref yPos, leftMargin, maxWidth, GetTeacherCollectionTitle(favoriteTeacherCollectionCount, false),
                    mainText,
                    favoriteTeacherCollectionCount >= 0 && !string.IsNullOrWhiteSpace(favoriteTeacherCollectionName) ? GetTeacherCollectionSubText(favoriteTeacherCollectionCount, false, teacherCollectionStatusMessage) : string.Empty,
                    true, favoriteTeacherCollectionCount >= 0 && !string.IsNullOrWhiteSpace(favoriteTeacherCollectionName));
            }

            bool qualityScanInProgress;
            string qualityStatusMessage;
            lock (qualitySnapshotScanLock)
            {
                qualityScanInProgress = isQualitySnapshotScanInProgress;
                qualityStatusMessage = qualitySnapshotScanStatusMessage;
            }

            var qualitySummary = GetVideoQualitySummaryFromSnapshot();
            string qualityMainText;
            if (qualitySummary.HasData)
            {
                qualityMainText = $"4K占比 {qualitySummary.FourKPercentage:0.#}%，无码占比 {qualitySummary.UncensoredPercentage:0.#}%。";
            }
            else
            {
                qualityMainText = "4K占比 / 无码占比：点击统计。";
            }

            DrawAchievement(g, ref yPos, leftMargin, maxWidth,
                GetVideoQualityTitle(qualitySummary, false),
                qualityMainText,
                qualitySummary.HasData ? GetVideoQualitySubText(qualitySummary, false, qualityStatusMessage) : string.Empty,
                true, qualitySummary.HasData);
        }
        
        private void DrawAchievement(Graphics g, ref int yPos, int leftMargin, int maxWidth, string title, string mainText, string subText, bool isClickable = false, bool showTitleInSubText = true)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            
            // 计算文本高度来确定卡片高度
            using (Font titleFont = new Font("微软雅黑", 12, FontStyle.Bold))
            using (Font mainFont = new Font("微软雅黑", 12))
            using (Font subFont = new Font("微软雅黑", 10))
            {
                // 固定卡片高度为91像素
                int cardHeight = S(91);
                
                // 合并标题和吐槽语
                string combinedSubText = showTitleInSubText ? $"【{title}】{subText}" : subText;
                bool hasSubText = !string.IsNullOrWhiteSpace(combinedSubText);
                
                // 计算各部分文本的高度用于定位
                int mainTextHeight = CalculateTextHeight(g, mainText, mainFont, maxWidth - S(20)) + S(5);
                int combinedSubTextHeight = hasSubText
                    ? CalculateTextHeight(g, $"  ○ {combinedSubText}", subFont, maxWidth - S(20)) + S(5)
                    : 0;
                
                // 计算总内容高度
                int totalContentHeight = mainTextHeight + combinedSubTextHeight;
                
                // 计算上内边距，使内容垂直居中
                int topPadding = (cardHeight - totalContentHeight) / 2;
                
                // 绘制圆角卡片背景
                Rectangle cardRect = new Rectangle(leftMargin, yPos, maxWidth, cardHeight);
                using (GraphicsPath cardPath = CreateRoundedRectanglePath(cardRect, S(10)))
                {
                    // 半透明黑色背景
                    using (SolidBrush cardBrush = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                        g.FillPath(cardBrush, cardPath);
                    
                    // 淡色边框，可点击的卡片用不同颜色
                    Color borderColor = isClickable ? Color.FromArgb(180, 100, 150, 255) : Color.FromArgb(150, 200, 200, 200);
                    using (Pen cardPen = new Pen(borderColor, 1))
                        g.DrawPath(cardPen, cardPath);
                }
                
                // 记录卡片区域
                currentCardAreas.Add(new CardArea(cardRect, title, isClickable));
                
                // 在卡片内绘制文本（垂直居中）
                int textY = yPos + topPadding;
                
                // 主文案（需要高亮数字）
                DrawTextWithHighlightedNumbers(g, mainText, mainFont, Brushes.White, Brushes.Yellow, leftMargin + S(10), ref textY, maxWidth - S(20));
                if (hasSubText)
                {
                    textY += S(5);
                }
                
                // 标题和吐槽文案合并（需要高亮数字）
                if (hasSubText)
                {
                    DrawTextWithHighlightedNumbers(g, $"  ○ {combinedSubText}", subFont, Brushes.LightGray, Brushes.Cyan, leftMargin + S(10), ref textY, maxWidth - S(20));
                }
                
                // 更新Y位置到下一个卡片
                yPos += cardHeight + S(5); // 减少卡片间距到5px
            }
        }
        
        // 计算文本在指定宽度内的高度
        private int CalculateTextHeight(Graphics g, string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            
            SizeF textSize = g.MeasureString(text, font, maxWidth);
            return (int)Math.Ceiling(textSize.Height);
        }
        
        // 在卡片内绘制自动换行文本
        private void DrawWrappedTextInCard(Graphics g, string text, Font font, Brush brush, int x, ref int yPos, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            string currentLine = "";
            int lineHeight = (int)font.GetHeight(g);
            
            for (int i = 0; i < text.Length; i++)
            {
                string testLine = currentLine + text[i];
                SizeF size = g.MeasureString(testLine, font);
                
                if (size.Width > maxWidth && currentLine.Length > 0)
                {
                    g.DrawString(currentLine, font, brush, x, yPos);
                    yPos += lineHeight + 2;
                    currentLine = text[i].ToString();
                }
                else
                {
                    currentLine = testLine;
                }
            }
            
            if (currentLine.Length > 0)
            {
                g.DrawString(currentLine, font, brush, x, yPos);
                yPos += lineHeight + 2;
            }
        }
        
        // 绘制带高亮数字的文本
        private void DrawTextWithHighlightedNumbers(Graphics g, string text, Font font, Brush normalBrush, Brush numberBrush, int x, ref int yPos, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return;
            
            int lineHeight = (int)font.GetHeight(g);
            int currentX = x;
            string currentLine = "";
            
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                string testLine = currentLine + c;
                SizeF testSize = g.MeasureString(testLine, font);
                
                // 如果超出宽度且当前行不为空，换行
                if (testSize.Width > maxWidth && currentLine.Length > 0)
                {
                    // 绘制当前行
                    DrawLineWithHighlightedNumbers(g, currentLine, font, normalBrush, numberBrush, x, yPos);
                    yPos += lineHeight;  // 减小行间距，去掉+2
                    currentLine = c.ToString();
                }
                else
                {
                    currentLine = testLine;
                }
            }
            
            // 绘制最后一行
            if (currentLine.Length > 0)
            {
                DrawLineWithHighlightedNumbers(g, currentLine, font, normalBrush, numberBrush, x, yPos);
                yPos += lineHeight;  // 减小行间距，去掉+2
            }
        }
        
        // 绘制单行带高亮数字的文本
        private void DrawLineWithHighlightedNumbers(Graphics g, string text, Font font, Brush normalBrush, Brush numberBrush, int x, int y)
        {
            int currentX = x;

            // 先检测需要整体高亮的动态内容，比如文件名、路径名、老师名
            var highlightPattern = new System.Text.RegularExpressions.Regex(
                        @"(?<=最爱背景：).*?(?=，)|(?<=最爱老师：).*?(?=，一共)|(?<=最常光顾 ).*?(?=，共)|(?<=观看最多次数的视频：).*|(?<=年度最爱视频：).*|(?<=最爱的视频收藏是 ).*?(?=。)|(?<=最爱的图包收藏是 ).*?(?=。)|(?<=最爱的番号：).*?(?=。)|(?<=最常用的编码是 ).*?(?=，占比)|(?<=库存最多的老师：).*?(?=。)|\b[\w\-\[\]]+\.(jpg|jpeg|png|gif|mp4|webp|mkv|avi|mov)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var matches = highlightPattern.Matches(text);

            if (matches.Count > 0)
            {
                // 如果有需要高亮的文本，按段落处理
                int lastIndex = 0;
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    // 绘制文件名之前的部分
                    if (match.Index > lastIndex)
                    {
                        string beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                        DrawLineWithHighlightedNumbersSimple(g, beforeText, font, normalBrush, numberBrush, ref currentX, y);
                    }

                    // 绘制命中的动态文本（用高亮色）
                    string highlightedText = match.Value;
                    SizeF highlightedTextSize = g.MeasureString(highlightedText, font);
                    g.DrawString(highlightedText, font, numberBrush, currentX, y);
                    currentX += (int)highlightedTextSize.Width;
                    
                    lastIndex = match.Index + match.Length;
                }
                
                // 绘制剩余部分
                if (lastIndex < text.Length)
                {
                    string remainingText = text.Substring(lastIndex);
                    DrawLineWithHighlightedNumbersSimple(g, remainingText, font, normalBrush, numberBrush, ref currentX, y);
                }
            }
            else
            {
                // 没有文件名，使用原来的逻辑
                DrawLineWithHighlightedNumbersSimple(g, text, font, normalBrush, numberBrush, ref currentX, y);
            }
        }
        
        // 简化版的数字高亮绘制（不处理文件名）
        private void DrawLineWithHighlightedNumbersSimple(Graphics g, string text, Font font, Brush normalBrush, Brush numberBrush, ref int currentX, int y)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var tokenPattern = new System.Text.RegularExpressions.Regex(
                @"\d+\s*[KkPp](?![A-Za-z0-9])|[\d.%:]+|[^\d]+",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

            foreach (System.Text.RegularExpressions.Match match in tokenPattern.Matches(text))
            {
                string token = match.Value;
                bool isHighlightedNumber = System.Text.RegularExpressions.Regex.IsMatch(
                    token,
                    @"^[\d.%:]+$",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);

                Brush brush = isHighlightedNumber ? numberBrush : normalBrush;
                SizeF tokenSize = g.MeasureString(token, font);
                g.DrawString(token, font, brush, currentX, y);
                currentX += (int)tokenSize.Width;
            }
        }
        
        // 判断字符是否为数字相关字符或文件名字符
        private bool IsNumberChar(char c)
        {
            return char.IsDigit(c) || c == '.' || c == '%' || c == ':';
        }

        // 判断字符串是否看起来像文件名（包含扩展名）
        private bool LooksLikeFileName(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            // 检查是否包含常见的图片/视频扩展名
            string lower = word.ToLower();
            return lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".png") || 
                   lower.EndsWith(".gif") || lower.EndsWith(".mp4") || lower.EndsWith(".webp") ||
                   lower.Contains(".jpg,") || lower.Contains(".jpeg,") || lower.Contains(".png,");
        }
        
        // 处理卡片点击
        private void HandleCardClick(int x, int y, int panelWidth, int panelHeight, int panelIndex)
        {
            // 根据panel索引选择对应的卡片区域列表
            var cardAreas = panelIndex switch
            {
                0 => panel1CardAreas,
                1 => panel2CardAreas,
                2 => panel3CardAreas,
                5 => panel6CardAreas,
                _ => currentCardAreas
            };
            
            foreach (var cardArea in cardAreas)
            {
                if (cardArea.Bounds.Contains(x, y) && cardArea.IsClickable)
                {
                    // 根据卡片标题执行不同操作（使用包含匹配，因为标题是动态的）
                    string title = cardArea.Title;
                    
                    // 第一个卡片：炮火华尔兹/全勤皇帝/不灭圣火/钢铁战士/半程选手/常驻会员/业余选手/周末战士/偶尔放纵/佛系青年/隐形人/清修道长 -> 历史记录
                    if (title == "炮火华尔兹" || title == "全勤皇帝" || title == "不灭圣火" || title == "钢铁战士" ||
                        title == "半程选手" || title == "常驻会员" || title == "业余选手" || title == "周末战士" ||
                        title == "偶尔放纵" || title == "佛系青年" || title == "隐形人" || title == "清修道长")
                    {
                        btnHistory_Click(null, EventArgs.Empty);
                    }
                    // 不活跃天数卡片 -> 不活跃天数统计
                    else if (title == "全勤战神" || title == "无休勇士" || title == "铁人三项" || title == "不眠战士" ||
                             title == "速战速决" || title == "闪电回归" || title == "转眼即逝" || title == "快进快出" ||
                             title == "短期戒断" || title == "阶段性清醒" || title == "小型闭关" || title == "中途休整" ||
                             title == "闭关修炼" || title == "出家未遂" || title == "长期潜伏" || title == "重返江湖")
                    {
                        ShowInactiveStreakWindow();
                    }
                    // 深夜孤影等时间相关卡片 -> 24小时活跃度
                    else if (title == "深夜孤影" || title == "不眠战士" || title == "暗夜行者" || title == "夜猫本猫" ||
                             title == "晨练达人" || title == "清晨第一发" || title == "早起的鸟儿" || title == "晨间仪式" ||
                             title == "上班摸鱼" || title == "工作间隙" || title == "午前时光" || title == "摸鱼专家" ||
                             title == "午休项目" || title == "饭后消遣" || title == "午间放松" || title == "午睡替代" ||
                             title == "下午茶时间" || title == "午后时光" || title == "摸鱼高手" || title == "提神醒脑" ||
                             title == "下班放松" || title == "黄昏时刻" || title == "晚餐前奏" || title == "归家仪式" ||
                             title == "夜生活" || title == "睡前仪式" || title == "夜间娱乐" || title == "深夜放纵")
                    {
                        ShowHourlyActivityWindow();
                    }
                    // 第二个卡片：单日最高点击量 -> 热力图
                    else if (title == "选妃狂魔" || title == "时间管理大师" || title == "体力惊人" || title == "停不下来" ||
                             title == "口味挑剔" || title == "认真筛选" || title == "稳定输出" || title == "适度放纵" ||
                             title == "极简主义" || title == "蜻蜓点水" || title == "意思意思" || title == "克制有度")
                    {
                        ShowHeatmapWindow();
                    }
                    // 第三个卡片：月度最高点击量 -> 月度统计
                    else if (title == "硬盘疗伤" || title == "疯狂月份" || title == "巅峰之月" || title == "爆发期" ||
                             title == "月度冠军" || title == "火力全开" || title == "高光时刻" || title == "月度MVP" ||
                             title == "平平无奇" || title == "养生月份" || title == "意思意思" || title == "低调做人")
                    {
                        ShowMonthlyStatsWindow();
                    }
                    // 第四个卡片：最常使用路径 -> 路径热力图
                    else if (title == "心头好" || title == "常驻地" || title == "钦定宝地" || title == "根据地")
                    {
                        ShowPathHeatmapWindow();
                    }
                    // 第五个卡片：最爱背景 -> 背景热力图
                    else if (title == "门面担当" || title == "长期聘用" || title == "久处不厌" || title == "看板娘")
                    {
                        ShowBackgroundHeatmapWindow();
                    }
                    // 第六个卡片：暗香浮动/脂玉流光/肉感盛宴/绝对领域 -> 视频热力图
                    else if (title == "暗香浮动" || title == "脂玉流光" || title == "肉感盛宴" || title == "绝对领域")
                    {
                        ShowVideoHeatmapWindow();
                    }
                    // 第七个卡片：入骨缠绵/指尖余温/终极缪斯/禁忌私语 -> 最爱老师排行榜
                    else if (title == "入骨缠绵" || title == "指尖余温" || title == "终极缪斯" || title == "禁忌私语")
                    {
                        ShowTeacherRankingWindow();
                    }
                    else if (title == "库存普查" || title == "待您点名" || title == "深宫遗珠" || title == "无人问津" || title == "冷宫常住" || title == "长夜未央")
                    {
                        StartUnwatchedVideoScanForAnnualReport();
                        ShowUnwatchedFolderRankingWindow();
                    }
                    else if (title == "库存盘点" || title == "广纳新欢" || title == "持续扩编" || title == "喜添新人" || title == "后宫扩招" || title == "断舍离中" || title == "裁撤后宫" || title == "清理门户" || title == "挥泪削籍")
                    {
                        StartInventorySnapshotScanForAnnualReport();
                    }
                    else if (title == "暗号成瘾" || title == "门牌熟客" || title == "字母通灵" || title == "前缀有灵")
                    {
                        ShowFavoriteCodeRankingWindow();
                    }
                    else if (title == "编码侦查" || title == "待揭面纱" ||
                             title == "原档主义" || title == "老牌原味" || title == "祖传口粮" || title == "不改原味" ||
                             title == "原盘在手" || title == "原味至上" || title == "完整版控" || title == "留全才稳" ||
                             title == "修行未满" || title == "收过一手" || title == "压过再留" || title == "盘算周全" ||
                             title == "新码试炼" || title == "画质偏执" || title == "尝鲜过头" || title == "空间强迫" ||
                             title == "编码偏门")
                    {
                        StartVideoCodecScanForAnnualReport();
                    }
                    else if (title == "待查藏量" || title == "细腰锁人" || title == "蜜腿生祸" || title == "雪肤生香" || title == "肉感留痕")
                    {
                        StartTeacherCollectionScanForAnnualReport();
                        ShowFavoriteTeacherCollectionRankingWindow();
                    }
                    else if (title == "全裸素颜" || title == "生物解剖" || title == "降维打击" || title == "坦诚相见" ||
                             title == "像素崇拜" || title == "算力冗余" || title == "华丽囚笼" || title == "参数霸权" ||
                             title == "原始本能" || title == "底层透视" || title == "算力博弈" || title == "硬核纯度" ||
                             title == "待查片质")
                    {
                        StartVideoQualitySnapshotScanForAnnualReport();
                    }
                    else
                    {
                        MessageBox.Show($"点击了卡片: {cardArea.Title}\n\n这里将显示详细的统计数据");
                    }
                    break;
                }
            }
        }

        // 显示选妃日榜窗口
        private void ShowHeatmapWindow()
        {
            if (heatmapFormInstance != null && !heatmapFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                heatmapFormInstance.Activate();
                return;
            }

            // 创建窗口
            heatmapFormInstance = new HistoryTransparentForm
            {
                Text = "选妃日榜",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            heatmapFormInstance.SuspendLayout();

            // 设置窗口尺寸（比年度报告窗口小一些）
            ApplyRescaling(heatmapFormInstance, 900, 700);

            // 背景逻辑（使用Report文件夹）
            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(heatmapFormInstance.ClientSize.Width, heatmapFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            heatmapFormInstance.BackgroundImage = readyBg;
                            heatmapFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            // 窗口关闭时清理资源
            heatmapFormInstance.FormClosing += (s, e) =>
            {
                if (heatmapFormInstance.BackgroundImage != null)
                {
                    var img = heatmapFormInstance.BackgroundImage;
                    heatmapFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                // 清除刷新委托引用
                heatmapUpdateDataAction = null;
                UnregisterAnnualReportRefresh(heatmapFormInstance);
            };

            // 启用双缓冲
            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, heatmapFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(heatmapFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            // 获取热力图数据（按点击次数排序，取前15天）
            var heatmapData = GetHeatmapData(15);

            // 创建刷新数据的委托
            Action updateData = () =>
            {
                heatmapData = GetHeatmapData(15);
                if (heatmapFormInstance != null && !heatmapFormInstance.IsDisposed)
                {
                    heatmapFormInstance.Invalidate(); // 触发重绘
                }
            };

            // 保存刷新委托引用
            heatmapUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(heatmapFormInstance, updateData);

            // 绘制热力图
            heatmapFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (heatmapFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(heatmapFormInstance.BackgroundImage, 0, 0);
                }

                // 绘制半透明黑色滤镜（提高透明度，让背景更暗）
                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, heatmapFormInstance.ClientRectangle);

                // 绘制热力图内容
                DrawHeatmapContent(g, heatmapFormInstance.ClientSize.Width, heatmapFormInstance.ClientSize.Height, heatmapData);
            };

            heatmapFormInstance.ResumeLayout();
            heatmapFormInstance.Show();
        }

        // 获取热力图数据
        private List<(DateTime date, int clicks)> GetHeatmapData(int topN)
        {
            var dailyClicks = new Dictionary<DateTime, int>();

            if (File.Exists(historyFilePath))
            {
                var lines = File.ReadAllLines(historyFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 1 && DateTime.TryParse(parts[0], out DateTime timestamp))
                    {
                        DateTime dateOnly = timestamp.Date;
                        if (dailyClicks.ContainsKey(dateOnly))
                            dailyClicks[dateOnly]++;
                        else
                            dailyClicks[dateOnly] = 1;
                    }
                }
            }

            // 按点击次数降序排序，取前topN天
            return dailyClicks
                .OrderByDescending(kvp => kvp.Value)
                .Take(topN)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        // 绘制热力图内容
        private void DrawHeatmapContent(Graphics g, int width, int height, List<(DateTime date, int clicks)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(30);
            int barSpacing = S(10);
            int barHeight = S(35);
            int startY = S(80);

            // 标题
            using (Font titleFont = new Font("微软雅黑", 20, FontStyle.Bold))
            {
                string title = "选妃日榜 TOP 15";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(30));
            }

            // 找出最大点击次数用于归一化
            int maxClicks = data.Max(d => d.clicks);

            using (Font labelFont = new Font("微软雅黑", 13))
            using (Font valueFont = new Font("微软雅黑", 13, FontStyle.Bold))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var (date, clicks) = data[i];
                    int y = startY + i * (barHeight + barSpacing);

                    // 计算条形宽度（归一化到可用宽度）
                    int maxBarWidth = width - margin * 2 - S(200);
                    int barWidth = (int)(maxBarWidth * ((float)clicks / maxClicks));

                    // 绘制条形（渐变色，从蓝到红）
                    float ratio = (float)clicks / maxClicks;
                    Color barColor = GetHeatColor(ratio);
                    Rectangle barRect = new Rectangle(margin + S(150), y, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(180, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    // 绘制日期
                    string dateStr = date.ToString("yyyy-MM-dd");
                    g.DrawString(dateStr, labelFont, Brushes.White, margin, y + S(8));

                    // 绘制点击次数
                    string clicksStr = $"{clicks} 次";
                    SizeF clicksSize = g.MeasureString(clicksStr, valueFont);
                    g.DrawString(clicksStr, valueFont, Brushes.Yellow, margin + S(150) + barWidth + S(10), y + S(8));
                }
            }
        }

        // 根据比例获取热力颜色（蓝->绿->黄->红）
        private Color GetHeatColor(float ratio)
        {
            if (ratio < 0.25f)
                return Color.FromArgb(100, 150, 255); // 蓝色
            else if (ratio < 0.5f)
                return Color.FromArgb(100, 255, 150); // 绿色
            else if (ratio < 0.75f)
                return Color.FromArgb(255, 200, 100); // 黄色
            else
                return Color.FromArgb(255, 100, 100); // 红色
        }

        // 显示月度统计窗口
        private void ShowMonthlyStatsWindow()
        {
            if (monthlyStatsFormInstance != null && !monthlyStatsFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                monthlyStatsFormInstance.Activate();
                return;
            }

            // 创建窗口
            monthlyStatsFormInstance = new HistoryTransparentForm
            {
                Text = "选妃月榜",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            monthlyStatsFormInstance.SuspendLayout();

            // 设置窗口尺寸（比年度报告窗口小一些）
            ApplyRescaling(monthlyStatsFormInstance, 1000, 700);

            // 背景逻辑（使用Report文件夹）
            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(monthlyStatsFormInstance.ClientSize.Width, monthlyStatsFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            monthlyStatsFormInstance.BackgroundImage = readyBg;
                            monthlyStatsFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            // 窗口关闭时清理资源
            monthlyStatsFormInstance.FormClosing += (s, e) =>
            {
                if (monthlyStatsFormInstance.BackgroundImage != null)
                {
                    var img = monthlyStatsFormInstance.BackgroundImage;
                    monthlyStatsFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                // 清除刷新委托引用
                monthlyStatsUpdateDataAction = null;
                UnregisterAnnualReportRefresh(monthlyStatsFormInstance);
            };

            // 启用双缓冲
            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, monthlyStatsFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(monthlyStatsFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            // 获取月度统计数据
            var monthlyData = GetMonthlyStatsData();

            // 创建刷新数据的委托
            Action updateData = () =>
            {
                monthlyData = GetMonthlyStatsData();
                if (monthlyStatsFormInstance != null && !monthlyStatsFormInstance.IsDisposed)
                {
                    monthlyStatsFormInstance.Invalidate(); // 触发重绘
                }
            };

            // 保存刷新委托引用
            monthlyStatsUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(monthlyStatsFormInstance, updateData);

            // 绘制月度统计
            monthlyStatsFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (monthlyStatsFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(monthlyStatsFormInstance.BackgroundImage, 0, 0);
                }

                // 绘制半透明黑色滤镜
                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, monthlyStatsFormInstance.ClientRectangle);

                // 绘制月度统计内容
                DrawMonthlyStatsContent(g, monthlyStatsFormInstance.ClientSize.Width, monthlyStatsFormInstance.ClientSize.Height, monthlyData);
            };

            monthlyStatsFormInstance.ResumeLayout();
            monthlyStatsFormInstance.Show();
        }

        // 获取月度统计数据
        private List<(int month, int clicks)> GetMonthlyStatsData()
        {
            var monthlyClicks = new Dictionary<int, int>();

            if (File.Exists(historyFilePath))
            {
                var lines = File.ReadAllLines(historyFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 1 && DateTime.TryParse(parts[0], out DateTime timestamp))
                    {
                        int month = timestamp.Month;
                        if (monthlyClicks.ContainsKey(month))
                            monthlyClicks[month]++;
                        else
                            monthlyClicks[month] = 1;
                    }
                }
            }

            // 按月份顺序排序（1-12月）
            return Enumerable.Range(1, 12)
                .Select(month => (month, monthlyClicks.ContainsKey(month) ? monthlyClicks[month] : 0))
                .ToList();
        }

        // 绘制月度统计内容
        private void DrawMonthlyStatsContent(Graphics g, int width, int height, List<(int month, int clicks)> data)
        {
            int margin = S(40);
            int startY = S(100);
            int chartHeight = height - startY - S(80);

            // 标题
            using (Font titleFont = new Font("微软雅黑", 20, FontStyle.Bold))
            {
                string title = "选妃月榜";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(30));
            }

            // 找出最大点击次数用于归一化
            int maxClicks = data.Max(d => d.clicks);
            if (maxClicks == 0) maxClicks = 1; // 避免除以0

            // 计算每个柱子的宽度和间距
            int barCount = 12;
            int totalWidth = width - 2 * margin;
            int barSpacing = S(10);
            int barWidth = (totalWidth - (barCount - 1) * barSpacing) / barCount;

            using (Font labelFont = new Font("微软雅黑", 11))
            using (Font valueFont = new Font("微软雅黑", 10, FontStyle.Bold))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var (month, clicks) = data[i];
                    int x = margin + i * (barWidth + barSpacing);

                    // 计算柱子高度（归一化）
                    int barHeight = clicks > 0 ? (int)(chartHeight * ((float)clicks / maxClicks)) : S(2); // 没数据时显示2像素的底线
                    int barY = startY + chartHeight - barHeight;

                    // 绘制柱子（渐变色）
                    float ratio = maxClicks > 0 && clicks > 0 ? (float)clicks / maxClicks : 0;
                    Color barColor = clicks > 0 ? GetHeatColor(ratio) : Color.FromArgb(80, 80, 80); // 没数据时用灰色
                    Rectangle barRect = new Rectangle(x, barY, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(180, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    // 绘制边框
                    using (GraphicsPath borderPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (Pen borderPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1))
                    {
                        g.DrawPath(borderPen, borderPath);
                    }

                    // 绘制月份标签
                    string monthStr = $"{month}月";
                    SizeF monthSize = g.MeasureString(monthStr, labelFont);
                    g.DrawString(monthStr, labelFont, Brushes.White, x + (barWidth - monthSize.Width) / 2, startY + chartHeight + S(10));

                    // 绘制点击次数（在柱子上方，包括0）
                    string clicksStr = clicks.ToString();
                    SizeF clicksSize = g.MeasureString(clicksStr, valueFont);
                    Color textColor = clicks > 0 ? Color.Yellow : Color.Gray; // 0用灰色显示
                    using (SolidBrush textBrush = new SolidBrush(textColor))
                    {
                        g.DrawString(clicksStr, valueFont, textBrush, x + (barWidth - clicksSize.Width) / 2, barY - S(20));
                    }
                }
            }
        }

        // 显示不活跃天数统计窗口
        private void ShowInactiveStreakWindow()
        {
            if (inactiveStreakFormInstance != null && !inactiveStreakFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                inactiveStreakFormInstance.Activate();
                return;
            }

            // 创建窗口
            inactiveStreakFormInstance = new HistoryTransparentForm
            {
                Text = "贤者记录",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            inactiveStreakFormInstance.SuspendLayout();

            // 设置窗口尺寸
            ApplyRescaling(inactiveStreakFormInstance, 1000, 700);

            // 背景逻辑（使用Report文件夹）
            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(inactiveStreakFormInstance.ClientSize.Width, inactiveStreakFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            inactiveStreakFormInstance.BackgroundImage = readyBg;
                            inactiveStreakFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            // 窗口关闭时清理资源
            inactiveStreakFormInstance.FormClosing += (s, e) =>
            {
                if (inactiveStreakFormInstance.BackgroundImage != null)
                {
                    var img = inactiveStreakFormInstance.BackgroundImage;
                    inactiveStreakFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                inactiveStreakUpdateDataAction = null;
                UnregisterAnnualReportRefresh(inactiveStreakFormInstance);
            };

            // 启用双缓冲
            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, inactiveStreakFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(inactiveStreakFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            // 获取不活跃天数数据
            var inactiveStreakData = GetInactiveStreakData();

            // 创建刷新数据的委托
            Action updateData = () =>
            {
                inactiveStreakData = GetInactiveStreakData();
                if (inactiveStreakFormInstance != null && !inactiveStreakFormInstance.IsDisposed)
                {
                    inactiveStreakFormInstance.Invalidate();
                }
            };

            inactiveStreakUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(inactiveStreakFormInstance, updateData);

            // 绘制不活跃天数统计
            inactiveStreakFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (inactiveStreakFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(inactiveStreakFormInstance.BackgroundImage, 0, 0);
                }

                // 绘制半透明黑色滤镜
                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, inactiveStreakFormInstance.ClientRectangle);

                // 绘制不活跃天数内容
                DrawInactiveStreakContent(g, inactiveStreakFormInstance.ClientSize.Width, inactiveStreakFormInstance.ClientSize.Height, inactiveStreakData);
            };

            inactiveStreakFormInstance.ResumeLayout();
            inactiveStreakFormInstance.Show();
        }

        // 获取不活跃天数数据
        private List<(DateTime startDate, DateTime endDate, int days)> GetInactiveStreakData()
        {
            var streaks = new List<(DateTime startDate, DateTime endDate, int days)>();

            if (File.Exists(historyFilePath))
            {
                var activeDates = new HashSet<DateTime>();
                var lines = File.ReadAllLines(historyFilePath);
                
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 1 && DateTime.TryParse(parts[0], out DateTime timestamp))
                    {
                        activeDates.Add(timestamp.Date);
                    }
                }

                if (activeDates.Count > 1)
                {
                    var sortedDates = activeDates.OrderBy(d => d).ToList();
                    
                    for (int i = 1; i < sortedDates.Count; i++)
                    {
                        int daysDiff = (sortedDates[i] - sortedDates[i - 1]).Days - 1;
                        if (daysDiff > 0)
                        {
                            streaks.Add((sortedDates[i - 1].AddDays(1), sortedDates[i].AddDays(-1), daysDiff));
                        }
                    }
                }
            }

            // 按天数降序排序（天数越多排在上面）
            return streaks.OrderByDescending(s => s.days).ToList();
        }

        // 绘制不活跃天数内容
        private void DrawInactiveStreakContent(Graphics g, int width, int height, List<(DateTime startDate, DateTime endDate, int days)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(30);
            int barSpacing = S(10);
            int barHeight = S(35);
            int startY = S(80);

            // 标题
            using (Font titleFont = new Font("微软雅黑", 20, FontStyle.Bold))
            {
                string title = "贤者记录";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(30));
            }

            // 找出最大天数用于归一化
            int maxDays = data.Max(d => d.days);

            using (Font labelFont = new Font("微软雅黑", 10))
            using (Font valueFont = new Font("微软雅黑", 10, FontStyle.Bold))
            {
                for (int i = 0; i < Math.Min(data.Count, 15); i++) // 最多显示15条
                {
                    var (startDate, endDate, days) = data[i];
                    int y = startY + i * (barHeight + barSpacing);

                    // 计算条形宽度
                    int maxBarWidth = width - margin * 2 - S(250);
                    int barWidth = (int)(maxBarWidth * ((float)days / maxDays));

                    // 绘制条形
                    float ratio = (float)days / maxDays;
                    Color barColor = GetHeatColor(ratio);
                    Rectangle barRect = new Rectangle(margin + S(200), y, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(180, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    // 绘制日期范围
                    string dateStr = $"{startDate:MM/dd} - {endDate:MM/dd}";
                    g.DrawString(dateStr, labelFont, Brushes.White, margin, y + S(10));

                    // 绘制天数
                    string daysStr = $"{days} 天";
                    g.DrawString(daysStr, valueFont, Brushes.Yellow, margin + S(200) + barWidth + S(10), y + S(10));
                }
            }
        }
        
        // 显示24小时活跃度窗口
        private void ShowHourlyActivityWindow()
        {
            if (hourlyActivityFormInstance != null && !hourlyActivityFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                hourlyActivityFormInstance.Activate();
                return;
            }

            // 创建窗口
            hourlyActivityFormInstance = new HistoryTransparentForm
            {
                Text = "时光刻度",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            hourlyActivityFormInstance.SuspendLayout();

            // 设置窗口尺寸
            ApplyRescaling(hourlyActivityFormInstance, 1000, 700);

            // 背景逻辑（使用Report文件夹）
            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(hourlyActivityFormInstance.ClientSize.Width, hourlyActivityFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            hourlyActivityFormInstance.BackgroundImage = readyBg;
                            hourlyActivityFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            // 窗口关闭时清理资源
            hourlyActivityFormInstance.FormClosing += (s, e) =>
            {
                if (hourlyActivityFormInstance.BackgroundImage != null)
                {
                    var img = hourlyActivityFormInstance.BackgroundImage;
                    hourlyActivityFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                hourlyActivityUpdateDataAction = null;
                UnregisterAnnualReportRefresh(hourlyActivityFormInstance);
            };

            // 启用双缓冲
            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, hourlyActivityFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(hourlyActivityFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            // 获取24小时活跃度数据
            var hourlyData = GetHourlyActivityData();

            // 创建刷新数据的委托
            Action updateData = () =>
            {
                hourlyData = GetHourlyActivityData();
                if (hourlyActivityFormInstance != null && !hourlyActivityFormInstance.IsDisposed)
                {
                    hourlyActivityFormInstance.Invalidate();
                }
            };

            hourlyActivityUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(hourlyActivityFormInstance, updateData);

            // 绘制24小时活跃度
            hourlyActivityFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (hourlyActivityFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(hourlyActivityFormInstance.BackgroundImage, 0, 0);
                }

                // 绘制半透明黑色滤镜
                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, hourlyActivityFormInstance.ClientRectangle);

                // 绘制24小时活跃度内容
                DrawHourlyActivityContent(g, hourlyActivityFormInstance.ClientSize.Width, hourlyActivityFormInstance.ClientSize.Height, hourlyData);
            };

            hourlyActivityFormInstance.ResumeLayout();
            hourlyActivityFormInstance.Show();
        }

        // 获取24小时活跃度数据
        private List<(int hour, int clicks)> GetHourlyActivityData()
        {
            var hourlyClicks = new Dictionary<int, int>();
            
            // 初始化0-23小时
            for (int i = 0; i < 24; i++)
            {
                hourlyClicks[i] = 0;
            }

            if (File.Exists(historyFilePath))
            {
                var lines = File.ReadAllLines(historyFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 1 && DateTime.TryParse(parts[0], out DateTime timestamp))
                    {
                        int hour = timestamp.Hour;
                        hourlyClicks[hour]++;
                    }
                }
            }

            // 按小时顺序返回（0-23）
            return hourlyClicks.OrderBy(kvp => kvp.Key).Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }

        // 绘制24小时活跃度内容
        private void DrawHourlyActivityContent(Graphics g, int width, int height, List<(int hour, int clicks)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(40);
            int startY = S(120);
            int chartHeight = height - startY - S(80);
            int barWidth = S(30);
            int barSpacing = S(5);

            // 标题
            using (Font titleFont = new Font("微软雅黑", 20, FontStyle.Bold))
            {
                string title = "时光刻度";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(30));
            }

            // 副标题
            using (Font subTitleFont = new Font("微软雅黑", 12))
            {
                string subTitle = "24小时活跃度分布";
                SizeF subTitleSize = g.MeasureString(subTitle, subTitleFont);
                g.DrawString(subTitle, subTitleFont, Brushes.LightGray, (width - subTitleSize.Width) / 2, S(65));
            }

            // 找出最大点击数用于归一化
            int maxClicks = data.Max(d => d.clicks);
            if (maxClicks == 0) maxClicks = 1; // 避免除以0

            using (Font labelFont = new Font("微软雅黑", 10))
            using (Font valueFont = new Font("微软雅黑", 9, FontStyle.Bold))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var (hour, clicks) = data[i];
                    int x = margin + i * (barWidth + barSpacing);

                    // 计算柱子高度（归一化）
                    int barHeight = clicks > 0 ? (int)(chartHeight * ((float)clicks / maxClicks)) : S(2);
                    int barY = startY + chartHeight - barHeight;

                    // 绘制柱子（渐变色）
                    float ratio = maxClicks > 0 && clicks > 0 ? (float)clicks / maxClicks : 0;
                    Color barColor = clicks > 0 ? GetHeatColor(ratio) : Color.FromArgb(80, 80, 80);
                    Rectangle barRect = new Rectangle(x, barY, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(180, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    // 绘制边框
                    using (GraphicsPath borderPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (Pen borderPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1))
                    {
                        g.DrawPath(borderPen, borderPath);
                    }

                    // 绘制小时标签（显示所有小时）
                    string hourStr = $"{hour:D2}";
                    SizeF hourSize = g.MeasureString(hourStr, labelFont);
                    g.DrawString(hourStr, labelFont, Brushes.White, x + (barWidth - hourSize.Width) / 2, startY + chartHeight + S(10));

                    // 绘制点击次数（在柱子上方，只显示有数据的）
                    if (clicks > 0)
                    {
                        string clicksStr = clicks.ToString();
                        SizeF clicksSize = g.MeasureString(clicksStr, valueFont);
                        using (SolidBrush textBrush = new SolidBrush(Color.Yellow))
                        {
                            g.DrawString(clicksStr, valueFont, textBrush, x + (barWidth - clicksSize.Width) / 2, barY - S(18));
                        }
                    }
                }
            }
        }

        // 显示路径热力图窗口
        private void ShowPathHeatmapWindow()
        {
            if (pathHeatmapFormInstance != null && !pathHeatmapFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                pathHeatmapFormInstance.Activate();
                return;
            }

            pathHeatmapFormInstance = new HistoryTransparentForm
            {
                Text = "路径热力图",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            pathHeatmapFormInstance.SuspendLayout();
            ApplyRescaling(pathHeatmapFormInstance, 1000, 700);

            // 背景逻辑（使用Report文件夹）
            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(pathHeatmapFormInstance.ClientSize.Width, pathHeatmapFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            pathHeatmapFormInstance.BackgroundImage = readyBg;
                            pathHeatmapFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            // 窗口关闭时清理资源
            pathHeatmapFormInstance.FormClosing += (s, e) =>
            {
                if (pathHeatmapFormInstance.BackgroundImage != null)
                {
                    var img = pathHeatmapFormInstance.BackgroundImage;
                    pathHeatmapFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                pathHeatmapUpdateDataAction = null;
                UnregisterAnnualReportRefresh(pathHeatmapFormInstance);
            };

            // 启用双缓冲
            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, pathHeatmapFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(pathHeatmapFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            // 获取路径使用数据
            var pathData = GetPathUsageData();

            // 创建刷新数据的委托
            Action updateData = () =>
            {
                pathData = GetPathUsageData();
                if (pathHeatmapFormInstance != null && !pathHeatmapFormInstance.IsDisposed)
                {
                    pathHeatmapFormInstance.Invalidate();
                }
            };

            pathHeatmapUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(pathHeatmapFormInstance, updateData);

            // 绘制路径热力图
            pathHeatmapFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (pathHeatmapFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(pathHeatmapFormInstance.BackgroundImage, 0, 0);
                }

                // 绘制半透明黑色滤镜
                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, pathHeatmapFormInstance.ClientRectangle);

                // 绘制路径热力图内容
                DrawPathHeatmapContent(g, pathHeatmapFormInstance.ClientSize.Width, pathHeatmapFormInstance.ClientSize.Height, pathData);
            };

            pathHeatmapFormInstance.ResumeLayout();
            pathHeatmapFormInstance.Show();
        }

        // 获取路径使用数据
        private List<(string path, int count)> GetPathUsageData()
        {
            return GetCurrentYearSelectedPathUsageData().Take(15).ToList();
        }

        // 绘制路径热力图内容
        private void DrawPathHeatmapContent(Graphics g, int width, int height, List<(string path, int count)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(30);
            int startY = S(80);
            int pathTextHeight = S(20); // 路径文本行高度
            int barHeight = S(25); // 柱状图高度
            int itemSpacing = S(15); // 每个项目之间的间距
            int itemTotalHeight = pathTextHeight + barHeight + S(5); // 每个项目的总高度（路径+柱状图+间距）
            int maxBarWidth = width - margin * 2 - S(80); // 留出右侧数字空间

            // 绘制标题
            using (Font titleFont = new Font("微软雅黑", 18, FontStyle.Bold))
            {
                string title = "路径使用排行榜";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(25));
            }

            int maxCount = data.Max(d => d.count);

            using (Font pathFont = new Font("微软雅黑", 10))
            using (Font countFont = new Font("微软雅黑", 10, FontStyle.Bold))
            {
                // 减少显示数量到10个，避免过于拥挤
                int displayCount = Math.Min(data.Count, 10);
                
                for (int i = 0; i < displayCount; i++)
                {
                    var (path, count) = data[i];
                    int y = startY + i * (itemTotalHeight + itemSpacing);

                    // 第一行：绘制路径文本
                    string shortPath = ShortenPath(path, 80);
                    g.DrawString(shortPath, pathFont, Brushes.White, margin, y);

                    // 第二行：绘制柱状图
                    int barY = y + pathTextHeight + S(5);
                    
                    // 计算条形宽度
                    int barWidth = maxCount > 0 ? (int)((double)count / maxCount * maxBarWidth) : 0;
                    barWidth = Math.Max(barWidth, S(5)); // 最小宽度

                    // 根据排名选择颜色
                    Color barColor = i switch
                    {
                        0 => Color.FromArgb(255, 215, 0),   // 金色
                        1 => Color.FromArgb(192, 192, 192), // 银色
                        2 => Color.FromArgb(205, 127, 50),  // 铜色
                        _ => Color.FromArgb(100, 149, 237)  // 其他用蓝色
                    };

                    // 绘制条形背景
                    Rectangle barRect = new Rectangle(margin, barY, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(200, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    // 绘制次数（在条形右侧）
                    string countStr = count.ToString();
                    SizeF countSize = g.MeasureString(countStr, countFont);
                    g.DrawString(countStr, countFont, Brushes.Yellow, margin + barWidth + S(10), barY + (barHeight - countFont.Height) / 2);
                }
            }
        }

        // 根据鼠标位置获取光标类型
        private Cursor GetCursorForPosition(int x, int y, int panelWidth, int panelHeight, int panelIndex)
        {
            // 根据panel索引选择对应的卡片区域列表
            var cardAreas = panelIndex switch
            {
                0 => panel1CardAreas,
                1 => panel2CardAreas,
                2 => panel3CardAreas,
                5 => panel6CardAreas,
                _ => currentCardAreas
            };
            
            foreach (var cardArea in cardAreas)
            {
                if (cardArea.Bounds.Contains(x, y) && cardArea.IsClickable)
                {
                    return Cursors.Hand;
                }
            }
            return Cursors.Default;
        }
        
        // 卡片区域信息
        private class CardArea
        {
            public Rectangle Bounds { get; set; }
            public string Title { get; set; }
            public bool IsClickable { get; set; }
            
            public CardArea(Rectangle bounds, string title, bool isClickable)
            {
                Bounds = bounds;
                Title = title;
                IsClickable = isClickable;
            }
        }
        
        // 存储当前panel中的卡片区域（为每个panel维护独立的列表）
        private List<CardArea> panel1CardAreas = new List<CardArea>();
        private List<CardArea> panel2CardAreas = new List<CardArea>();
        private List<CardArea> panel3CardAreas = new List<CardArea>();
        private List<CardArea> panel6CardAreas = new List<CardArea>();
        private List<CardArea> currentCardAreas = new List<CardArea>(); // 保留用于兼容性
        
        private void DrawWrappedText(Graphics g, string text, Font font, Brush brush, int x, ref int yPos, int maxWidth)
        {
            string currentLine = "";
            int lineHeight = (int)font.GetHeight(g);
            
            for (int i = 0; i < text.Length; i++)
            {
                string testLine = currentLine + text[i];
                SizeF size = g.MeasureString(testLine, font);
                
                if (size.Width > maxWidth && currentLine.Length > 0)
                {
                    g.DrawString(currentLine, font, brush, x, yPos);
                    yPos += lineHeight + 2;
                    currentLine = text[i].ToString();
                }
                else
                {
                    currentLine = testLine;
                }
            }
            
            if (currentLine.Length > 0)
            {
                g.DrawString(currentLine, font, brush, x, yPos);
                yPos += lineHeight + 2;
            }
        }
        
        private string GetPoZhenMuLeiSubText(int days)
        {
            if (days >= 200)
            {
                var texts = new[]
                {
                    "用三百万发小蝌蚪奏响了一曲炮火华尔兹。天哪，我都开始同情您的腰子了",
                    $"{days}天？您这出勤率高得离谱，建议给硬盘发个全勤奖，它比您还累",
                    $"一年{days}天在线，您这是嫌肾太闲了？中医看了都想给您开两副药",
                    $"{days}天的战绩，您已经把\"适可而止\"这个成语从字典里删了吧"
                };
                return texts[lastPoZhenMuLeiIndex];
            }
            else if (days >= 100)
            {
                var texts = new[]
                {
                    $"一年{days}天，您成功证明了\"节制\"这个词在您这儿只是偶尔想起来的事",
                    $"{days}天的记录，您这使用频率让我怀疑您是不是把这软件当成了日常必需品",
                    $"一年{days}天的活跃度，您这是把\"偶尔放纵\"当成了人生信条？挺会把握分寸啊",
                    $"{days}天的记录，您这使用习惯让我猜测您是不是只在周末才想起来有这么个软件"
                };
                return texts[lastPoZhenMuLeiIndex];
            }
            else
            {
                var texts = new[]
                {
                    $"{days}天？您这使用频率低得我都怀疑您是不是装错软件了，还是说您在别的地方有小号",
                    $"{days}天的记录，您把\"清心寡欲\"活成了行为艺术，我都开始怀疑您是不是出家了",
                    $"全年{days}天？您这存在感低得我都怀疑您是不是只是来测试一下软件能不能打开",
                    $"{days}天的使用记录，您这节制程度让我怀疑您是不是在修炼什么\"禁欲系\"武功秘籍"
                };
                return texts[lastPoZhenMuLeiIndex];
            }
        }

        private string GetPoZhenMuLeiTitle(int days)
        {
            if (days >= 200)
            {
                var titles = new[] { "炮火华尔兹", "全勤皇帝", "不灭圣火", "钢铁战士" };
                int index = GetNonRepeatingRandomIndex(titles.Length, ref lastPoZhenMuLeiIndex);
                return titles[index];
            }
            else if (days > 100) // 100 < days < 200
            {
                var titles = new[] { "半程选手", "常驻会员", "业余选手", "周末战士" };
                int index = GetNonRepeatingRandomIndex(titles.Length, ref lastPoZhenMuLeiIndex);
                return titles[index];
            }
            else
            {
                var titles = new[] { "偶尔放纵", "佛系青年", "隐形人", "清修道长" };
                int index = GetNonRepeatingRandomIndex(titles.Length, ref lastPoZhenMuLeiIndex);
                return titles[index];
            }
        }

        private string GetInactiveStreakTitle(int days)
        {
            string[] titles;
            if (days <= 0)
            {
                titles = new[] { "全勤战神", "无休勇士", "铁人三项", "不眠战士" };
            }
            else if (days <= 3)
            {
                titles = new[] { "速战速决", "闪电回归", "转眼即逝", "快进快出" };
            }
            else if (days <= 14)
            {
                titles = new[] { "短期戒断", "阶段性清醒", "小型闭关", "中途休整" };
            }
            else
            {
                titles = new[] { "闭关修炼", "出家未遂", "长期潜伏", "重返江湖" };
            }

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastInactiveStreakIndex);
            return titles[index];
        }

        private string GetInactiveStreakSubText(int days)
        {
            string[] texts;
            if (days <= 0)
            {
                texts = new[]
                {
                    "一天都没缺席，您这身体真的扛得住吗？建议去查查肾功能",
                    "全年无休，您这是在挑战人体极限？腰子表示压力很大",
                    "一天都没断过，我开始担心您的身体了，适度放纵才是王道",
                    "全年在线，您这是嫌自己精力太旺盛了？中医看了都想劝您两句"
                };
            }
            else if (days <= 3)
            {
                texts = new[]
                {
                    $"{days}天就回来了，您这是去洗了个澡还是睡了一觉",
                    $"{days}天的空窗期，您这是刚想起\"节制\"两个字怎么写就忘了",
                    $"{days}天的冷静期，您这是去思考人生了还是只是网断了",
                    $"{days}天就回归了，您这速度让我怀疑您是不是只是重启了一下"
                };
            }
            else if (days <= 14)
            {
                texts = new[]
                {
                    $"{days}天没碰，您这是突然想通了还是硬盘坏了需要修",
                    $"{days}天的禁欲生活，看来您还是有点自制力的，虽然最后还是没忍住",
                    $"{days}天的空窗期，您这是在养精蓄锐还是在反思人生",
                    $"{days}天没动静，您这是在给身体放假还是在酝酿更大的爆发"
                };
            }
            else
            {
                texts = new[]
                {
                    $"憋了{days}天，我还以为您要成仙了，结果还是回来了",
                    $"{days}天的禁欲生活，差点就成功了，可惜功亏一篑",
                    $"{days}天没动静，您这是在憋大招还是真的想戒了，结果还是破功了",
                    $"消失{days}天，您这是去闭关了还是被关禁闭了"
                };
            }

            return texts[lastInactiveStreakIndex];
        }
        
        private string GetPeakHourTitle(int hour)
        {
            if (hour >= 0 && hour <= 5) // 凌晨档
            {
                var titles = new[] { "深夜孤影", "不眠战士", "暗夜行者", "夜猫本猫" };
                int index = GetPeakHourRandomIndex(hour);
                return titles[index % titles.Length];
            }
            else if (hour >= 6 && hour <= 8) // 清晨档
            {
                var titles = new[] { "晨练达人", "清晨第一发", "早起的鸟儿", "晨间仪式" };
                int index = GetPeakHourRandomIndex(hour);
                return titles[index % titles.Length];
            }
            else if (hour >= 9 && hour <= 11) // 上午档
            {
                var titles = new[] { "上班摸鱼", "工作间隙", "午前时光", "摸鱼专家" };
                int index = GetPeakHourRandomIndex(hour);
                return titles[index % titles.Length];
            }
            else if (hour >= 12 && hour <= 13) // 中午档
            {
                var titles = new[] { "午休项目", "饭后消遣", "午间放松", "午睡替代" };
                int index = GetPeakHourRandomIndex(hour);
                return titles[index % titles.Length];
            }
            else if (hour >= 14 && hour <= 17) // 下午档
            {
                var titles = new[] { "下午茶时间", "午后时光", "摸鱼高手", "提神醒脑" };
                int index = GetPeakHourRandomIndex(hour);
                return titles[index % titles.Length];
            }
            else if (hour >= 18 && hour <= 20) // 傍晚档
            {
                var titles = new[] { "下班放松", "黄昏时刻", "晚餐前奏", "归家仪式" };
                int index = GetPeakHourRandomIndex(hour);
                return titles[index % titles.Length];
            }
            else // 21-23点 夜晚档
            {
                var titles = new[] { "夜生活", "睡前仪式", "夜间娱乐", "深夜放纵" };
                int index = GetPeakHourRandomIndex(hour);
                return titles[index % titles.Length];
            }
        }
        
        // 获取固定的随机索引，确保标题、主文案、副文案对应
        private int peakHourRandomIndex = -1;
        
        // 记录上次使用的索引，确保下次不重复
        private int lastPoZhenMuLeiIndex = -1;
        private int lastInactiveStreakIndex = -1;
        private int lastYuHuoFenShenIndex = -1;
        private int lastYueDuBaZhuIndex = -1;
        private int lastPathIndex = -1;
        private int lastBackgroundIndex = -1;
        private int lastVideoIndex = -1;
        private int lastTeacherIndex = -1;
        private int lastUnwatchedVideoIndex = -1;
        private int lastInventoryChangeIndex = -1;
        private int lastFavoriteCollectionIndex = -1;
        private int lastFavoriteRewatchIndex = -1;
        private int lastFavoriteNeverRewatchedIndex = -1;
        private int lastMostRewatchedFavoriteIndex = -1;
        private int lastFavoriteImagePackIndex = -1;
        private int lastFavoriteImagePackRewatchIndex = -1;
        private int lastFavoriteImagePackNeverRewatchedIndex = -1;
        private int lastMostRewatchedFavoriteImagePackIndex = -1;
        private int lastCodePrefixIndex = -1;
        private int lastTeacherCollectionIndex = -1;
        private int lastVideoQualityIndex = -1;
        private int lastVideoCodecIndex = -1;
        private int lastPeakHourIndex = -1;
        private int lastSpecialDateOptionIndex = -1;
        private int lastEarlyMorningOptionIndex = -1;
        private int lastPanel1ConditionalChoiceIndex = -1;
        
        // 获取不重复的随机索引
        private int GetNonRepeatingRandomIndex(int maxValue, ref int lastIndex)
        {
            if (freezeAnnualReportCardRandoms && lastIndex >= 0 && lastIndex < maxValue)
            {
                return lastIndex;
            }

            var random = new Random(Guid.NewGuid().GetHashCode());
            int newIndex;
            do
            {
                newIndex = random.Next(maxValue);
            } while (newIndex == lastIndex && maxValue > 1);
            lastIndex = newIndex;
            return newIndex;
        }
        
        private int GetPeakHourRandomIndex(int hour)
        {
            if (peakHourRandomIndex == -1)
            {
                peakHourRandomIndex = GetNonRepeatingRandomIndex(4, ref lastPeakHourIndex);
            }
            return peakHourRandomIndex;
        }
        
        private string GetPeakHourMainText(int hour)
        {
            if (hour >= 0 && hour <= 5) // 凌晨档
            {
                var texts = new[]
                {
                    $"您在凌晨{hour}点最活跃",
                    $"凌晨{hour}点，别人在做梦，您在冲",
                    $"{hour}点的活跃记录说明了一切",
                    $"凌晨{hour}点依然精神抖擞"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 6 && hour <= 8) // 清晨档
            {
                var texts = new[]
                {
                    $"早上{hour}点就开始了一天的\"锻炼\"",
                    $"{hour}点，一睁眼就想着这事",
                    $"早上{hour}点的高峰期",
                    $"{hour}点准时报到"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 9 && hour <= 11) // 上午档
            {
                var texts = new[]
                {
                    $"{hour}点最活跃，这不是上班时间吗",
                    $"上午{hour}点的小秘密",
                    $"{hour}点的活跃记录",
                    $"{hour}点，老板以为您在工作"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 12 && hour <= 13) // 中午档
            {
                var texts = new[]
                {
                    $"中午{hour}点的午休方式",
                    $"{hour}点，刚吃完饭就开始了",
                    $"中午{hour}点的高峰期",
                    $"{hour}点，不睡觉在干嘛"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 14 && hour <= 17) // 下午档
            {
                var texts = new[]
                {
                    $"下午{hour}点的特殊茶点",
                    $"{hour}点对抗困意的方式",
                    $"下午{hour}点还在摸鱼",
                    $"{hour}点的提神方式"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 18 && hour <= 20) // 傍晚档
            {
                var texts = new[]
                {
                    $"傍晚{hour}点，下班第一件事",
                    $"{hour}点，迎接夜晚的方式",
                    $"傍晚{hour}点的开胃菜",
                    $"{hour}点，到家第一件事"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else // 21-23点 夜晚档
            {
                var texts = new[]
                {
                    $"晚上{hour}点的夜生活",
                    $"{hour}点的助眠项目",
                    $"晚上{hour}点的娱乐活动",
                    $"{hour}点，一天的高光时刻"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
        }
        
        private string GetPeakHourSubText(int hour)
        {
            if (hour >= 0 && hour <= 5) // 凌晨档
            {
                var texts = new[]
                {
                    "深夜里的每一个Byte，都承载着你不为人知的孤独",
                    "这个点还不睡，您是失眠还是根本没打算给床面子",
                    "您这作息表是反着来的吧，太阳都替您担心",
                    "您这生物钟是不是跟地球不在一个频道上"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 6 && hour <= 8) // 清晨档
            {
                var texts = new[]
                {
                    "您这把它当成晨练项目了，挺会养生啊",
                    "您这执行力，建议去当CEO",
                    "早起的鸟儿有虫吃，早起的您有片看",
                    "您这是把它当成起床必修课了"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 9 && hour <= 11) // 上午档
            {
                var texts = new[]
                {
                    "您这摸鱼技术，HR看了想哭",
                    "趁着工作间隙放松一下，您这时间管理大师啊",
                    "您这是把上午当成了私人娱乐时间",
                    "您这摸鱼技术炉火纯青，建议出教程"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 12 && hour <= 13) // 中午档
            {
                var texts = new[]
                {
                    "别人在睡觉，您在冲，这午休质量堪忧啊",
                    "您这是饭后百步走的升级版吗",
                    "您这午休方式挺特别，建议申请专利",
                    "看来您不需要午睡，只需要这个"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 14 && hour <= 17) // 下午档
            {
                var texts = new[]
                {
                    "您这下午茶喝得挺刺激啊",
                    "咖啡提神太普通，您这招更管用",
                    "您这工作效率让我开始担心您的绩效了",
                    "别人喝咖啡，您看片，各有各的活法"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else if (hour >= 18 && hour <= 20) // 傍晚档
            {
                var texts = new[]
                {
                    "您这是把它当成了下班打卡项目",
                    "别人看夕阳，您看片，都是欣赏美好事物",
                    "您这是饭前开胃还是饭后消食，我有点分不清了",
                    "您这回家仪式感拉满了"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
            else // 21-23点 夜晚档
            {
                var texts = new[]
                {
                    "您这夜生活过得挺充实，就是有点费纸巾",
                    "您这是把它当成了安眠药的替代品",
                    "您这是在为睡眠做准备还是在挑战睡眠",
                    "您这是憋了一天就等这一刻吧"
                };
                int index = GetPeakHourRandomIndex(hour);
                return texts[index % texts.Length];
            }
        }
        
        private string GetYuHuoFenShenSubText(int clicks)
        {
            string[] texts;
            if (clicks >= 21)
            {
                texts = new[]
                {
                    $"单日{clicks}次，您这是把后宫翻了个底朝天",
                    $"一天{clicks}次？您这效率让我怀疑您是不是有分身术",
                    $"{clicks}次的记录，建议去参加奥运会，绝对能拿金牌",
                    $"单日{clicks}次，您这是上瘾了还是中邪了"
                };
            }
            else if (clicks >= 11)
            {
                texts = new[]
                {
                    $"单日{clicks}次，您这是在精挑细选还是在货比三家",
                    $"一天{clicks}次，看来您对质量还是有要求的",
                    $"{clicks}次的单日记录，您这频率控制得刚刚好",
                    $"单日{clicks}次，您这是在享受过程而不是追求数量"
                };
            }
            else
            {
                texts = new[]
                {
                    $"{clicks}次就够了，您这是在践行\"少即是多\"的哲学",
                    $"单日{clicks}次，您这是来打个卡就走的节奏",
                    $"一天{clicks}次，您这是象征性地表示一下到此一游",
                    $"{clicks}次的自律，您这定力让人刮目相看"
                };
            }

            return texts[lastYuHuoFenShenIndex];
        }

        private string GetYuHuoFenShenTitle(int clicks)
        {
            string[] titles;
            if (clicks >= 21)
            {
                titles = new[] { "选妃狂魔", "时间管理大师", "体力惊人", "停不下来" };
            }
            else if (clicks >= 11)
            {
                titles = new[] { "口味挑剔", "认真筛选", "稳定输出", "适度放纵" };
            }
            else
            {
                titles = new[] { "极简主义", "蜻蜓点水", "意思意思", "克制有度" };
            }

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastYuHuoFenShenIndex);
            return titles[index];
        }
        
        private string GetYueDuBaZhuSubText(int clicks)
        {
            string[] texts;
            if (clicks >= 210)
            {
                texts = new[]
                {
                    $"那个月{clicks}次，您这是失恋了还是换了新硬盘",
                    $"单月{clicks}次，您这是把一年的量都压缩到一个月了",
                    $"{clicks}次的月度记录，您这是遇到什么刺激了",
                    $"一个月{clicks}次，您这是在冲业绩还是在发泄情绪"
                };
            }
            else if (clicks >= 110)
            {
                texts = new[]
                {
                    $"单月{clicks}次，您这是吃了什么大补药",
                    $"{clicks}次？这个月您是不是把日历当成了任务清单",
                    $"一个月{clicks}次，您这精力管理堪称教科书",
                    $"{clicks}次的战绩，您这是在冲KPI还是在冲排行榜"
                };
            }
            else
            {
                texts = new[]
                {
                    $"巅峰月才{clicks}次，您这是在装低调还是真低调",
                    $"单月{clicks}次，您这节制程度让我怀疑您是不是道士",
                    $"{clicks}次就是巅峰了？您这是来打个卡就走的节奏",
                    $"一个月{clicks}次，您这存在感低得像是在隐身"
                };
            }

            return texts[lastYueDuBaZhuIndex];
        }

        private string GetYueDuBaZhuTitle(int clicks)
        {
            string[] titles;
            if (clicks >= 210)
            {
                titles = new[] { "硬盘疗伤", "疯狂月份", "巅峰之月", "爆发期" };
            }
            else if (clicks >= 110)
            {
                titles = new[] { "月度冠军", "火力全开", "高光时刻", "月度MVP" };
            }
            else
            {
                titles = new[] { "平平无奇", "养生月份", "意思意思", "低调做人" };
            }

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastYueDuBaZhuIndex);
            return titles[index];
        }

        // 显示背景热力图窗口
        private void ShowBackgroundHeatmapWindow()
        {
            if (backgroundHeatmapFormInstance != null && !backgroundHeatmapFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                backgroundHeatmapFormInstance.Activate();
                return;
            }

            backgroundHeatmapFormInstance = new HistoryTransparentForm
            {
                Text = "背景使用排行榜",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            backgroundHeatmapFormInstance.SuspendLayout();
            ApplyRescaling(backgroundHeatmapFormInstance, 1000, 700);

            // 背景逻辑（使用Report文件夹）
            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(backgroundHeatmapFormInstance.ClientSize.Width, backgroundHeatmapFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            backgroundHeatmapFormInstance.BackgroundImage = readyBg;
                            backgroundHeatmapFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            // 窗口关闭时清理资源
            backgroundHeatmapFormInstance.FormClosing += (s, e) =>
            {
                if (backgroundHeatmapFormInstance.BackgroundImage != null)
                {
                    var img = backgroundHeatmapFormInstance.BackgroundImage;
                    backgroundHeatmapFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                backgroundHeatmapUpdateDataAction = null;
                UnregisterAnnualReportRefresh(backgroundHeatmapFormInstance);
            };

            // 启用双缓冲
            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, backgroundHeatmapFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(backgroundHeatmapFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            // 获取背景使用数据
            var backgroundData = GetBackgroundUsageData();

            // 创建刷新数据的委托
            Action updateData = () =>
            {
                backgroundData = GetBackgroundUsageData();
                if (backgroundHeatmapFormInstance != null && !backgroundHeatmapFormInstance.IsDisposed)
                {
                    backgroundHeatmapFormInstance.Invalidate();
                }
            };

            backgroundHeatmapUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(backgroundHeatmapFormInstance, updateData);

            // 绘制背景热力图
            backgroundHeatmapFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (backgroundHeatmapFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(backgroundHeatmapFormInstance.BackgroundImage, 0, 0);
                }

                // 绘制半透明黑色滤镜
                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, backgroundHeatmapFormInstance.ClientRectangle);

                // 绘制背景热力图内容
                DrawBackgroundHeatmapContent(g, backgroundHeatmapFormInstance.ClientSize.Width, backgroundHeatmapFormInstance.ClientSize.Height, backgroundData);
            };

            backgroundHeatmapFormInstance.ResumeLayout();
            backgroundHeatmapFormInstance.Show();
        }

        // 获取背景使用数据
        private List<(string background, int count)> GetBackgroundUsageData()
        {
            var backgroundCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string backgroundLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "BackgroundUsage.log");

            if (File.Exists(backgroundLogPath))
            {
                var lines = File.ReadAllLines(backgroundLogPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('|');
                    if (parts.Length >= 2)
                    {
                        string bgKey = NormalizeBackgroundUsageKey(parts[1]);
                        if (string.IsNullOrWhiteSpace(bgKey))
                            continue;

                        if (backgroundCounts.ContainsKey(bgKey))
                            backgroundCounts[bgKey]++;
                        else
                            backgroundCounts[bgKey] = 1;
                    }
                }
            }

            // 按使用次数降序排列，取前10个
            return backgroundCounts.OrderByDescending(kvp => kvp.Value).Take(10).Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }

        // 绘制背景热力图内容
        private void DrawBackgroundHeatmapContent(Graphics g, int width, int height, List<(string background, int count)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(30);
            int startY = S(80);
            int bgTextHeight = S(20); // 背景名称行高度
            int barHeight = S(25); // 柱状图高度
            int itemSpacing = S(15); // 每个项目之间的间距
            int itemTotalHeight = bgTextHeight + barHeight + S(5); // 每个项目的总高度
            int maxBarWidth = width - margin * 2 - S(80); // 留出右侧数字空间

            // 绘制标题
            using (Font titleFont = new Font("微软雅黑", 18, FontStyle.Bold))
            {
                string title = "背景使用排行榜";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(25));
            }

            int maxCount = data.Max(d => d.count);

            using (Font bgFont = new Font("微软雅黑", 10))
            using (Font countFont = new Font("微软雅黑", 10, FontStyle.Bold))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var (background, count) = data[i];
                    int y = startY + i * (itemTotalHeight + itemSpacing);

                    // 第一行：绘制背景文件名
                    string bgFileName = Path.GetFileName(background);
                    string shortBgName = bgFileName.Length > 60 ? bgFileName.Substring(0, 57) + "..." : bgFileName;
                    g.DrawString(shortBgName, bgFont, Brushes.White, margin, y);

                    // 第二行：绘制柱状图
                    int barY = y + bgTextHeight + S(5);
                    
                    // 计算条形宽度
                    int barWidth = maxCount > 0 ? (int)((double)count / maxCount * maxBarWidth) : 0;
                    barWidth = Math.Max(barWidth, S(5)); // 最小宽度

                    // 根据排名选择颜色
                    Color barColor = i switch
                    {
                        0 => Color.FromArgb(255, 215, 0),   // 金色
                        1 => Color.FromArgb(192, 192, 192), // 银色
                        2 => Color.FromArgb(205, 127, 50),  // 铜色
                        _ => Color.FromArgb(100, 149, 237)  // 其他用蓝色
                    };

                    // 绘制条形背景
                    Rectangle barRect = new Rectangle(margin, barY, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(200, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    // 绘制次数（在条形右侧）
                    string countStr = count.ToString();
                    SizeF countSize = g.MeasureString(countStr, countFont);
                    g.DrawString(countStr, countFont, Brushes.Yellow, margin + barWidth + S(10), barY + (barHeight - countFont.Height) / 2);
                }
            }
        }

        // 路径省略函数：只显示盘符和最后的文件夹
        private string ShortenPath(string path, int maxLength)
        {
            if (string.IsNullOrEmpty(path)) return "未知路径";
            if (path.Length <= maxLength) return path;

            try
            {
                string root = Path.GetPathRoot(path) ?? "";
                string lastFolder = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                if (string.IsNullOrEmpty(lastFolder))
                    lastFolder = path;

                string shortened = $"{root}...\\{lastFolder}";
                return shortened.Length <= maxLength ? shortened : $"{root}...\\{lastFolder.Substring(0, Math.Min(15, lastFolder.Length))}...";
            }
            catch
            {
                return path.Length > maxLength ? path.Substring(0, maxLength - 3) + "..." : path;
            }
        }

        private string GetPathTitle(int count)
        {
            var titles = new[] { "心头好", "常驻地", "钦定宝地", "根据地" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastPathIndex);
            return titles[index];
        }

        private string GetPathSubText(int count)
        {
            var texts = new[]
            {
                $"{count}次的记录，这个路径里的货色让人欲罢不能",
                $"余味绕梁的{count}次，这个路径里的质量让人回味无穷",
                $"深陷其中的{count}次光顾，这里的妃子让人如痴如醉",
                $"心心念念，{count}次都奔向这个路径，这里肯定藏着好东西"
            };
            return texts[lastPathIndex];
        }

        private string GetBackgroundTitle(int count)
        {
            string[] titles =
            {
                "门面担当",
                "长期聘用",
                "久处不厌",
                "看板娘"
            };

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastBackgroundIndex);
            return titles[index];
        }

        private string GetBackgroundSubText(int count)
        {
            string[] texts =
            {
                "看来这张图和您之间已经不是背景关系了，多少有点常驻首页的意思",
                "这张图在您这里的待遇，已经接近编制内员工了",
                "一张图能陪您这么久，多少说明它早就不只是顺眼那么简单了",
                "这张图待得太久，久到首页看不见它，反而会觉得少了点什么"
            };

            return texts[lastBackgroundIndex >= 0 && lastBackgroundIndex < texts.Length ? lastBackgroundIndex : 0];
        }

        private string GetVideoTitle(int count)
        {
            string[] titles = { "暗香浮动", "脂玉流光", "肉感盛宴", "绝对领域" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastVideoIndex);
            return titles[index];
        }

        private string GetVideoSubText(int count)
        {
            string[] texts =
            {
                "那一抹深邃的墨色顺着曲线一寸寸勒进骨髓，在紧致与丰盈的边缘反复撕扯，每处起伏都藏着粘稠的张力",
                "如羊脂玉般通透的色泽，透出底下若隐若现的红晕，在光影下泛起一层禁忌的微光",
                "那是原始且野蛮的生命力在肆意战栗，每一处起伏都带着沉甸甸的引力",
                "视线在几寸玲珑间彻底凝固，视线顺着边缘贪婪地往深处钻，这种求而不得的焦灼，才是最蚀骨的慢性毒药"
            };

            return texts[lastVideoIndex >= 0 && lastVideoIndex < texts.Length ? lastVideoIndex : 0];
        }

        private string GetTeacherTitle(int count)
        {
            string[] titles = { "入骨缠绵", "指尖余温", "终极缪斯", "禁忌私语" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastTeacherIndex);
            return titles[index];
        }

        private string GetTeacherSubText(int count)
        {
            string[] texts =
            {
                "每一帧像素都在细密地摩挲着感官边缘，从屏幕里溢了出来，顺着视神经一寸寸地啃食着那点残存的清醒",
                "进度条被反复蹂躏，只为捕捉那一瞬转瞬即逝的灵魂出窍。这种贪婪的凝视，连算法都在为这份狂热而共振",
                "任凭佳丽千千万，视线终究会在这场盛大的诱惑中彻底缴械。这是写进本能里的、永不褪色的赛博初恋",
                "每一寸起伏都带着滚烫的引力，在视线交汇的瞬间，连空气都变得粘稠，贴着耳际发出无法拒绝的沉沦邀请"
            };

            return texts[lastTeacherIndex >= 0 && lastTeacherIndex < texts.Length ? lastTeacherIndex : 0];
        }

        private string GetUnwatchedVideoTitle(int count, bool isScanning = false)
        {
            if (count < 0)
                return "待您点名";
            
            string[] titles = { "深宫遗珠", "无人问津", "冷宫常住", "长夜未央" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastUnwatchedVideoIndex);
            return titles[index];
        }

        private string GetUnwatchedVideoSubText(int count, bool isScanning = false, string statusMessage = "")
        {
            if (count < 0)
            {
                if (!string.IsNullOrWhiteSpace(statusMessage) && statusMessage.Contains("失败"))
                    return statusMessage;
                return "点一下这张卡片，就会开始统计当前启用路径里还有多少视频从未被抽中过。";
            }
            
            string[] texts =
            {
                "它们这一年始终没轮到上场，像是明明在册，却一直没等到您的旨意",
                "片库里还有这么一批视频安安静静躺着，看起来您今年确实没空翻到它们",
                "这一年它们连一次被点到的机会都没有，待遇多少有点过于边缘了",
                "它们在片库里沉默了一整年，像是始终没等到属于自己的那次召见"
            };

            return texts[lastUnwatchedVideoIndex >= 0 && lastUnwatchedVideoIndex < texts.Length ? lastUnwatchedVideoIndex : 0];
        }

        private string GetInventoryChangeTitle(int addedCount, int deletedCount, bool isScanning = false)
        {
            string[] titles = addedCount >= deletedCount
                ? new[] { "广纳新欢", "持续扩编", "喜添新人", "后宫扩招" }
                : new[] { "断舍离中", "裁撤后宫", "清理门户", "挥泪削籍" };

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastInventoryChangeIndex);
            return titles[index];
        }

        private string GetInventoryChangeSubText(int addedCount, int deletedCount, bool isScanning = false, string statusMessage = "")
        {
            if (!string.IsNullOrWhiteSpace(statusMessage) && statusMessage.Contains("失败"))
                return statusMessage;

            string[] texts = addedCount >= deletedCount
                ? new[]
                {
                    "这一年添进来的比送走的更多，看得出后宫编制还在稳步扩张",
                    "新面孔一批批入册，片库这边显然还远没到收手的时候",
                    "进的人比走的人多，今年这本名册，明显又厚了几页",
                    "新增压过删除，说明您这一年主要还是在往里收，不太舍得往外清"
                }
                : new[]
                {
                    "这一年送走的比收进来的更多，片库这边看起来是认真清过一轮了",
                    "删得比加得多，说明今年这场整顿，不只是嘴上说说",
                    "旧人退场的速度快过新人进宫，您这一年显然动过真格",
                    "送走的人更多，看来今年这本名册，您是亲手薄下来的"
                };

            return texts[lastInventoryChangeIndex >= 0 && lastInventoryChangeIndex < texts.Length ? lastInventoryChangeIndex : 0];
        }

        private string GetFavoriteCollectionTitle(int totalCount, int addedCount)
        {
            string[] titles = addedCount < 15
                ? new[] { "小心珍藏", "慢慢入柜", "精挑细选", "收而不滥" }
                : new[] { "持续进货", "越收越多", "收藏上头", "柜门渐满" };

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastFavoriteCollectionIndex);
            return titles[index];
        }

        private string GetFavoriteCollectionSubText(int totalCount, int addedCount)
        {
            string[] texts = addedCount < 15
                ? new[]
                {
                    "这一年收进来的不算多，看得出您下手比以前谨慎，留在收藏里的多少都过了眼缘",
                    "新增不多，像是挑着收、慢慢留，能进这一栏的都不算随手之选",
                    "今年这份收藏增长得很克制，留下来的，多半都是您真觉得值得放进柜里的",
                    "数量涨得不快，说明您今年收收藏这件事，多少带了点门槛"
                }
                : new[]
                {
                    "这一年收藏栏肉眼可见地扩编了，能让您点下收藏的，显然不止三两个",
                    "新增一路往上走，说明您今年对“先收起来再说”这件事，执行得很彻底",
                    "这一年能加进来这么多，说明您今年的收藏键，按得相当顺手",
                    "这一年新收进来的明显多了不少，收藏栏这边看着是越摆越满了"
                };

            return texts[lastFavoriteCollectionIndex >= 0 && lastFavoriteCollectionIndex < texts.Length ? lastFavoriteCollectionIndex : 0];
        }

        private string GetFavoriteRewatchTitle(int count)
        {
            string[] titles = count < 50
                ? new[] { "旧情回温", "还惦记着", "偶尔想她", "余温未散" }
                : new[] { "念念不休", "反复温存", "旧欢难忘", "缱绻未尽" };

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastFavoriteRewatchIndex);
            return titles[index];
        }

        private string GetFavoriteRewatchSubText(int count)
        {
            string[] texts = count < 50
                ? new[]
                {
                    "这一年偶尔也会回头看看，像是旧人还在心上，只是不至于夜夜想起",
                    "次数不算太多，却足够说明这些收藏您并没有真的放下",
                    "不是天天回看，但隔一阵子还是会点开，像是心里总留着一点念想",
                    "虽然只是偶尔重温几次，可那点熟悉感显然一直都没散干净"
                }
                : new[]
                {
                    "这一年能回去看这么多次，说明这点旧情在您这里根本就没断过",
                    "重温到这个次数，已经不像随手回顾，更像舍不得把那点感觉放凉",
                    "这些收藏被您反复点开这么多次，显然不只是留着纪念，而是一直挂在心上",
                    "回味到这个份上，说明有些旧收藏在您这里，从来就没真正翻篇"
                };

            return texts[lastFavoriteRewatchIndex >= 0 && lastFavoriteRewatchIndex < texts.Length ? lastFavoriteRewatchIndex : 0];
        }

        private string GetFavoriteNeverRewatchedTitle(int count)
        {
            string[] titles = { "收了再说", "只藏不看", "留作念想", "有名无实" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastFavoriteNeverRewatchedIndex);
            return titles[index];
        }

        private string GetFavoriteNeverRewatchedSubText(int count)
        {
            string[] texts =
            {
                "收藏的时候情真意切，收完之后倒是一直没顾上回来见它们",
                "这些收藏像是先被您安顿进柜子里，之后就一直安安静静地等着",
                "当时舍不得放走，后来却也一直没回头，多少有点留着当牵挂的意思",
                "进了收藏名单，却迟迟没等到后续，这段关系目前还停在名义阶段"
            };

            return texts[lastFavoriteNeverRewatchedIndex >= 0 && lastFavoriteNeverRewatchedIndex < texts.Length ? lastFavoriteNeverRewatchedIndex : 0];
        }

        private string GetMostRewatchedFavoriteTitle(int count)
        {
            string[] titles = { "独占心尖", "偏爱难掩", "余温最盛", "旧欢未冷" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastMostRewatchedFavoriteIndex);
            return titles[index];
        }

        private string GetMostRewatchedFavoriteSubText(int count)
        {
            string[] texts =
            {
                "重温次数一拉出来就很难装作随手看看了，它在您这里显然早就占了心尖位置",
                "收藏可以很多，回头最多的却只有一个，您这一点私心，榜单已经替您认了",
                "别的收藏也许只是留着，它却像一直在您手边，随时等着被您再碰一下",
                "这一年里最常被您重新点开的，还是它，看来有些旧欢确实很难凉下去"
            };

            return texts[lastMostRewatchedFavoriteIndex >= 0 && lastMostRewatchedFavoriteIndex < texts.Length ? lastMostRewatchedFavoriteIndex : 0];
        }

        private string GetFavoriteImagePackTitle(int totalCount, int addedCount)
        {
            string[] titles = addedCount < 15
                ? new[] { "口味微露", "另开小灶", "留册试探", "收得含蓄" }
                : new[] { "偏好渐显", "图包见长", "图包渐丰", "收漫成册" };

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastFavoriteImagePackIndex);
            return titles[index];
        }

        private string GetFavoriteImagePackSubText(int totalCount, int addedCount)
        {
            string[] texts = addedCount < 15
                ? new[]
                {
                    "今年这边收得不算多，怎么，视频先放着不点，难道这些是传说中的黄色漫画？",
                    "新增还不算多，但方向已经有点意思了，您这是最近不太想看视频，开始对这些东西上心了？",
                    "这边收得还算克制，所以您这是偶尔换换口味，还是说这里头真有点不方便点开视频时才看的东西？",
                    "这一年新收得不算多，所以您这边到底是偏图包，还是偏漫画，暂时还不太好下结论"
                }
                : new[]
                {
                    "图包收进来这么多以后就很难装路过了，难道这里头真有您一直惦记的那种黄色漫画？",
                    "新增一多，味道就出来了，怎么，最近不怎么点视频，专门开始收这一路的图包了？",
                    "这一年图包越收越多，看来您最近对视频那种一口气看完的路子，多少没那么上心了",
                    "所以您最近是嫌视频太省事，开始更吃这种慢慢铺开的劲儿了？"
                };

            return texts[lastFavoriteImagePackIndex >= 0 && lastFavoriteImagePackIndex < texts.Length ? lastFavoriteImagePackIndex : 0];
        }

        private string GetFavoriteImagePackRewatchTitle(int count)
        {
            string[] titles = { "反复翻她", "旧册生温", "余温还在", "翻册成瘾" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastFavoriteImagePackRewatchIndex);
            return titles[index];
        }

        private string GetFavoriteImagePackRewatchSubText(int count)
        {
            string[] texts =
            {
                "这一年图包被您翻了这么多回，想必连她们脸上的光影、身上的线条，甚至每一处细节都快熟得不能再熟了",
                "能把一册图包翻回来看这么多次，说明您对里面那些神情、角度和气氛，早就不是看过就算了",
                "能反复翻回来这么多次，说明这些图包在您这儿一直没凉，怕是连发丝、眼神和每一寸暧昧都看熟了",
                "这一页页翻下来翻上去，看到这个次数，估计连哪张露得最妙、哪张最该停一停，您都早有章法了"
            };

            return texts[lastFavoriteImagePackRewatchIndex >= 0 && lastFavoriteImagePackRewatchIndex < texts.Length ? lastFavoriteImagePackRewatchIndex : 0];
        }

        private string GetFavoriteImagePackNeverRewatchedTitle(int count)
        {
            string[] titles = { "收了未翻", "图包吃灰", "翻她未遂", "冷在柜中" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastFavoriteImagePackNeverRewatchedIndex);
            return titles[index];
        }

        private string GetFavoriteImagePackNeverRewatchedSubText(int count)
        {
            string[] texts =
            {
                "收的时候像怕她跑了，收完以后倒是一直没舍得翻第一页",
                "这一批图包在收藏栏里躺得相当安静，安静到像是已经默认自己短期内等不到您了",
                "明明都收进来了，结果这一年愣是没往回翻，像是念头起过，手却一直没伸过去",
                "它们被您收进柜里以后就没什么动静了，像是从入册那天起就直接进了静置期"
            };

            return texts[lastFavoriteImagePackNeverRewatchedIndex >= 0 && lastFavoriteImagePackNeverRewatchedIndex < texts.Length ? lastFavoriteImagePackNeverRewatchedIndex : 0];
        }

        private string GetMostRewatchedFavoriteImagePackTitle(int count)
        {
            string[] titles = { "独占心册", "她最顺手", "偏爱太明", "旧图最熟" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastMostRewatchedFavoriteImagePackIndex);
            return titles[index];
        }

        private string GetMostRewatchedFavoriteImagePackSubText(int count)
        {
            string[] texts =
            {
                "这么多图包里，偏偏她最常被您想起，看来有些册子进了柜，也还是待在心上",
                "别的图包收着也就收着，她却总像最顺手的那一册，手一伸过去就容易落到她身上",
                "一年下来最常落在她头上，您对这册子的私心，已经明显到很难装没这回事了",
                "能在这么多图包里一直排到前面，想来她身上那些该看的地方，您早就熟门熟路了"
            };

            return texts[lastMostRewatchedFavoriteImagePackIndex >= 0 && lastMostRewatchedFavoriteImagePackIndex < texts.Length ? lastMostRewatchedFavoriteImagePackIndex : 0];
        }

        private string GetCodePrefixTitle(int count)
        {
            string[] titles = { "暗号成瘾", "门牌熟客", "字母通灵", "前缀有灵" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastCodePrefixIndex);
            return titles[index];
        }

        private string GetCodePrefixSubText(int count)
        {
            string[] texts =
            {
                "这些字母组合对普通人是乱码，对您来说却是开启天堂的钥匙",
                "别人看是无意义字符，您看一眼就知道这串东西大概通往哪一层幻境",
                "这串前缀在别人眼里像密码，在您眼里却已经接近导航坐标",
                "看似只是几个冰冷字母，落到您这里，却像某种一眼就会心跳加速的召唤"
            };

            return texts[lastCodePrefixIndex >= 0 && lastCodePrefixIndex < texts.Length ? lastCodePrefixIndex : 0];
        }

        private string GetTeacherCollectionTitle(int count, bool isScanning = false)
        {
            if (count < 0)
                return "待查藏量";

            string[] titles = { "细腰锁人", "蜜腿生祸", "雪肤生香", "肉感留痕" };
            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastTeacherCollectionIndex);
            return titles[index];
        }

        private string GetTeacherCollectionSubText(int count, bool isScanning = false, string statusMessage = "")
        {
            if (count < 0)
            {
                if (!string.IsNullOrWhiteSpace(statusMessage) && statusMessage.Contains("失败"))
                    return statusMessage;
                return "点一下这张卡片，就会开始统计当前启用路径里哪位老师在片库里最有排面。";
            }

            string[] texts =
            {
                "能把库存一路攒上去，想来她那截腰身是真有点本事，细得勾眼，软得留人",
                "她能在您这儿囤到这个规模，八成跟那双腿脱不了干系，修长白净，摆着就够让人起念头",
                "库存能热成这样，想来她那身皮肉是真白得有点犯规，隔着屏幕都像带着股软香气",
                "她能在库存里压住别人，多半靠的就是那点白软丰润，眼睛一沾上去就不太舍得挪开"
            };

            return texts[lastTeacherCollectionIndex >= 0 && lastTeacherCollectionIndex < texts.Length ? lastTeacherCollectionIndex : 0];
        }

        private string GetVideoQualityTitle(VideoQualitySummary summary, bool isScanning)
        {
            if (!summary.HasData)
                return "待查片质";

            string[] titles;
            if (summary.FourKPercentage >= 40 && summary.UncensoredPercentage >= 40)
                titles = new[] { "全裸素颜", "生物解剖", "降维打击", "坦诚相见" };
            else if (summary.FourKPercentage > summary.UncensoredPercentage)
                titles = new[] { "像素崇拜", "算力冗余", "华丽囚笼", "参数霸权" };
            else
                titles = new[] { "原始本能", "底层透视", "算力博弈", "硬核纯度" };

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastVideoQualityIndex);
            return titles[index];
        }

        private string GetVideoQualitySubText(VideoQualitySummary summary, bool isScanning, string statusMessage)
        {
            if (!summary.HasData)
            {
                if (!string.IsNullOrWhiteSpace(statusMessage) && statusMessage.Contains("失败"))
                    return statusMessage;
                return "点一下这张卡片，就会统计您历史上扫描过的片库里 4K 和无码的占比。";
            }

            string[] texts;
            if (summary.FourKPercentage >= 40 && summary.UncensoredPercentage >= 40)
                texts = new[] { "这就是所谓的纤毫毕现吧？没了一层马赛克的朦胧美，4K 的每一根汗毛都在精准致敬",
                    "高清用于捕捉细节，无码用于还原真相。这不再是单纯的视觉消费，而是严谨的生物形态研究",
                    "马赛克是工业时代的残次品，低清是上个世纪的眼泪。这才是赛博时代的文明标杆",
                    "在这种画质面前，所有的遮掩都显得多余，这才是真正的沉浸式体验" };
            else if (summary.FourKPercentage > summary.UncensoredPercentage)
                texts = new[] { "4K 的细腻感已经溢出屏幕。在这种极致的解析力面前，连马赛克的边缘都被打磨出了晶莹剔透的工业美感",
                    "即便遮挡依然存在，这种超越肉眼极限的清晰度，也是对视觉神经最奢侈的霸凌",
                    "超高质量的底层码率，包裹着尚未破除的赛博枷锁。这是一场关于“高清遮掩”的暴力美学实验",
                    "分辨率的绝对优势，足以压制一切纯度上的遗憾，连马赛克都显得像是一种刻意为之的装饰艺术" };
            else
                texts = new[] { "放弃了 4K 的精雕细琢，选择了全方位的视觉诚实。这是对马赛克文明最彻底的背叛",
                    "像素的多寡不再是衡量标准，算法的穿透力才是核心指标。任何人为的遮掩都已在底层协议中失效",
                    "即便画质未达极致，但在数据层面上，真实感已通过算力完成了强行着陆",
                    "画质虽在及格边缘试探，但纯度已在巅峰俯瞰。这是一场只为还原生物本原的视觉苦修" };

            return texts[lastVideoQualityIndex >= 0 && lastVideoQualityIndex < texts.Length ? lastVideoQualityIndex : 0];
        }

        private string GetVideoCodecTitle(bool hasData, string codecDisplayName, bool isScanning)
        {
            if (!hasData)
                return "待揭面纱";

            string normalizedCodec = NormalizeCodecDisplayName(codecDisplayName);
            string[] titles = normalizedCodec switch
            {
                "MKV" or "ISO" => new[] { "原盘在手", "原味至上", "完整版控", "留全才稳" },
                "H.264" => new[] { "原档主义", "老牌原味", "祖传口粮", "不改原味" },
                "HEVC" => new[] { "修行未满", "收过一手", "压过再留", "盘算周全" },
                "AV1" => new[] { "新码试炼", "画质偏执", "尝鲜过头", "空间强迫" },
                _ => new[] { "编码偏门" }
            };

            int index = GetNonRepeatingRandomIndex(titles.Length, ref lastVideoCodecIndex);
            return titles[index];
        }

        private string GetVideoCodecSubText(bool hasData, string codecDisplayName, bool isScanning, string statusMessage)
        {
            if (!hasData)
            {
                if (!string.IsNullOrWhiteSpace(statusMessage) && statusMessage.Contains("失败"))
                    return statusMessage;
                return "点一下这张卡片，就会开始统计当前启用路径里最常见的视频编码。";
            }

            string normalizedCodec = NormalizeCodecDisplayName(codecDisplayName);
            string[] texts = normalizedCodec switch
            {
                "MKV" or "ISO" => new[]
                {
                    "原档这两个字到您这儿，多少已经有点信仰味了",
                    "看得出您对压制版那套始终差点意思，原味才算真到位",
                    "压过一层、削过一点，总归差着口气；您这边显然更吃这种完整版本",
                    "东西一旦不全，味道就像跟着少了一截，您这边显然不太肯吃这种亏"
                },
                "H.264" => new[]
                {
                    "说明您这边更爱那口原汁原味，能不动刀就不动刀",
                    "别人在后面折腾压制，您这边倒像更愿意把原档留着，讲究一个味道别变",
                    "说明您这边主打一个原档耐看，老口粮到今天依旧能打",
                    "看得出您对那种没怎么折腾过的版本，始终还是更有感情"
                },
                "HEVC" => new[]
                {
                    "HEVC 都顶到前面了，难不成不是您偏爱这一口，而是高质量源太难找，先收个能看的再说？",
                    "比起原档直收，您显然更偏好处理过的，难道还专挑了带字幕的那版？",
                    "您到底是偏爱压过一轮的版本，还是高质量源暂时还没摸到？",
                    "说明您多半不只看内容，连体积和保存压力都算进去了"
                },
                "AV1" => new[]
                {
                    "说明您这边对新东西是真愿意上手，不只是听听风声",
                    "看得出您对清晰度这件事不是一般在意，多少有点往死里抠细节的意思",
                    "别人还在观望，您这边已经把 AV1 收成习惯了，技术口味多少有点走在前面",
                    "说明您这边对保存压力相当敏感，体积大一点都像是在跟您作对"
                },
                _ => new[] { "主流三家都没拿第一，说明您这边的路子多少有点不按常理出牌" }
            };

            return texts[lastVideoCodecIndex >= 0 && lastVideoCodecIndex < texts.Length ? lastVideoCodecIndex : 0];
        }

        private string TruncateTextWithEllipsis(Graphics g, string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (maxWidth <= 0)
            {
                return "...";
            }

            if (g.MeasureString(text, font).Width <= maxWidth)
            {
                return text;
            }

            const string ellipsis = "...";
            if (g.MeasureString(ellipsis, font).Width > maxWidth)
            {
                return ellipsis;
            }

            int left = 0;
            int right = text.Length;
            while (left < right)
            {
                int mid = (left + right + 1) / 2;
                string candidate = text.Substring(0, mid) + ellipsis;
                if (g.MeasureString(candidate, font).Width <= maxWidth)
                {
                    left = mid;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return text.Substring(0, left) + ellipsis;
        }

        // 显示最爱老师排行榜窗口
        private void ShowTeacherRankingWindow()
        {
            if (teacherRankingFormInstance != null && !teacherRankingFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                teacherRankingFormInstance.Activate();
                return;
            }

            teacherRankingFormInstance = new HistoryTransparentForm
            {
                Text = "观看最爱老师排行榜",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            teacherRankingFormInstance.SuspendLayout();
            ApplyRescaling(teacherRankingFormInstance, 1000, 700);

            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(teacherRankingFormInstance.ClientSize.Width, teacherRankingFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            teacherRankingFormInstance.BackgroundImage = readyBg;
                            teacherRankingFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            teacherRankingFormInstance.FormClosing += (s, e) =>
            {
                if (teacherRankingFormInstance.BackgroundImage != null)
                {
                    var img = teacherRankingFormInstance.BackgroundImage;
                    teacherRankingFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                teacherRankingUpdateDataAction = null;
                UnregisterAnnualReportRefresh(teacherRankingFormInstance);
            };

            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, teacherRankingFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(teacherRankingFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            var teacherData = GetTeacherUsageData();

            Action updateData = () =>
            {
                teacherData = GetTeacherUsageData();
                if (teacherRankingFormInstance != null && !teacherRankingFormInstance.IsDisposed)
                {
                    teacherRankingFormInstance.Invalidate();
                }
            };

            teacherRankingUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(teacherRankingFormInstance, updateData);

            teacherRankingFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (teacherRankingFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(teacherRankingFormInstance.BackgroundImage, 0, 0);
                }

                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, teacherRankingFormInstance.ClientRectangle);

                DrawTeacherRankingContent(g, teacherRankingFormInstance.ClientSize.Width, teacherRankingFormInstance.ClientSize.Height, teacherData);
            };

            teacherRankingFormInstance.ResumeLayout();
            teacherRankingFormInstance.Show();
        }

        private List<(string teacher, int count)> GetTeacherUsageData()
        {
            return GetCurrentYearTeacherWatchCountsFromHistoryLog()
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        private List<(string teacher, int count)> GetTeacherCollectionUsageData()
        {
            EnsureTeacherCollectionCacheLoaded();
            lock (teacherCollectionScanLock)
            {
                return cachedTeacherCollectionDistribution
                    .Take(10)
                    .ToList();
            }
        }

        private void DrawTeacherRankingContent(Graphics g, int width, int height, List<(string teacher, int count)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(30);
            int startY = S(80);
            int teacherTextHeight = S(20);
            int barHeight = S(25);
            int itemSpacing = S(15);
            int itemTotalHeight = teacherTextHeight + barHeight + S(5);
            int maxBarWidth = width - margin * 2 - S(80);

            using (Font titleFont = new Font("微软雅黑", 18, FontStyle.Bold))
            {
                string title = "观看最爱老师排行榜";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(25));
            }

            int maxCount = data.Max(d => d.count);

            using (Font teacherFont = new Font("微软雅黑", 10))
            using (Font countFont = new Font("微软雅黑", 10, FontStyle.Bold))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var (teacher, count) = data[i];
                    int y = startY + i * (itemTotalHeight + itemSpacing);

                    string shortTeacherName = teacher.Length > 60 ? teacher.Substring(0, 57) + "..." : teacher;
                    g.DrawString(shortTeacherName, teacherFont, Brushes.White, margin, y);

                    int barY = y + teacherTextHeight + S(5);
                    int barWidth = maxCount > 0 ? (int)((double)count / maxCount * maxBarWidth) : 0;
                    barWidth = Math.Max(barWidth, S(5));

                    Color barColor = i switch
                    {
                        0 => Color.FromArgb(255, 215, 0),
                        1 => Color.FromArgb(192, 192, 192),
                        2 => Color.FromArgb(205, 127, 50),
                        _ => Color.FromArgb(100, 149, 237)
                    };

                    Rectangle barRect = new Rectangle(margin, barY, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(200, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    string countStr = count.ToString();
                    g.DrawString(countStr, countFont, Brushes.Yellow, margin + barWidth + S(10), barY + (barHeight - countFont.Height) / 2);
                }
            }
        }

        private void ShowFavoriteTeacherCollectionRankingWindow()
        {
            if (favoriteTeacherCollectionRankingFormInstance != null && !favoriteTeacherCollectionRankingFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                favoriteTeacherCollectionRankingFormInstance.Activate();
                return;
            }

            favoriteTeacherCollectionRankingFormInstance = new HistoryTransparentForm
            {
                Text = "老师库存排行榜",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            favoriteTeacherCollectionRankingFormInstance.SuspendLayout();
            ApplyRescaling(favoriteTeacherCollectionRankingFormInstance, 1000, 700);

            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(favoriteTeacherCollectionRankingFormInstance.ClientSize.Width, favoriteTeacherCollectionRankingFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            favoriteTeacherCollectionRankingFormInstance.BackgroundImage = readyBg;
                            favoriteTeacherCollectionRankingFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            favoriteTeacherCollectionRankingFormInstance.FormClosing += (s, e) =>
            {
                if (favoriteTeacherCollectionRankingFormInstance.BackgroundImage != null)
                {
                    var img = favoriteTeacherCollectionRankingFormInstance.BackgroundImage;
                    favoriteTeacherCollectionRankingFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                favoriteTeacherCollectionRankingUpdateDataAction = null;
                UnregisterAnnualReportRefresh(favoriteTeacherCollectionRankingFormInstance);
            };

            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, favoriteTeacherCollectionRankingFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(favoriteTeacherCollectionRankingFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            var teacherCollectionData = GetTeacherCollectionUsageData();

            Action updateData = () =>
            {
                teacherCollectionData = GetTeacherCollectionUsageData();
                if (favoriteTeacherCollectionRankingFormInstance != null && !favoriteTeacherCollectionRankingFormInstance.IsDisposed)
                {
                    favoriteTeacherCollectionRankingFormInstance.Invalidate();
                }
            };

            favoriteTeacherCollectionRankingUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(favoriteTeacherCollectionRankingFormInstance, updateData);

            favoriteTeacherCollectionRankingFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (favoriteTeacherCollectionRankingFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(favoriteTeacherCollectionRankingFormInstance.BackgroundImage, 0, 0);
                }

                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, favoriteTeacherCollectionRankingFormInstance.ClientRectangle);

                DrawTeacherCollectionRankingContent(g, favoriteTeacherCollectionRankingFormInstance.ClientSize.Width, favoriteTeacherCollectionRankingFormInstance.ClientSize.Height, teacherCollectionData);
            };

            favoriteTeacherCollectionRankingFormInstance.ResumeLayout();
            favoriteTeacherCollectionRankingFormInstance.Show();
        }

        private void DrawTeacherCollectionRankingContent(Graphics g, int width, int height, List<(string teacher, int count)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(30);
            int startY = S(80);
            int teacherTextHeight = S(20);
            int barHeight = S(25);
            int itemSpacing = S(15);
            int itemTotalHeight = teacherTextHeight + barHeight + S(5);
            int maxBarWidth = width - margin * 2 - S(80);

            using (Font titleFont = new Font("微软雅黑", 18, FontStyle.Bold))
            {
                string title = "老师库存排行榜";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(25));
            }

            int maxCount = data.Max(d => d.count);

            using (Font teacherFont = new Font("微软雅黑", 10))
            using (Font countFont = new Font("微软雅黑", 10, FontStyle.Bold))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var (teacher, count) = data[i];
                    int y = startY + i * (itemTotalHeight + itemSpacing);

                    string shortTeacherName = teacher.Length > 60 ? teacher.Substring(0, 57) + "..." : teacher;
                    g.DrawString(shortTeacherName, teacherFont, Brushes.White, margin, y);

                    int barY = y + teacherTextHeight + S(5);
                    int barWidth = maxCount > 0 ? (int)((double)count / maxCount * maxBarWidth) : 0;
                    barWidth = Math.Max(barWidth, S(5));

                    Color barColor = i switch
                    {
                        0 => Color.FromArgb(255, 215, 0),
                        1 => Color.FromArgb(192, 192, 192),
                        2 => Color.FromArgb(205, 127, 50),
                        _ => Color.FromArgb(100, 149, 237)
                    };

                    Rectangle barRect = new Rectangle(margin, barY, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(200, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    string countStr = count.ToString();
                    g.DrawString(countStr, countFont, Brushes.Yellow, margin + barWidth + S(10), barY + (barHeight - countFont.Height) / 2);
                }
            }
        }

        private void ShowFavoriteCodeRankingWindow()
        {
            if (favoriteCodeRankingFormInstance != null && !favoriteCodeRankingFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                favoriteCodeRankingFormInstance.Activate();
                return;
            }

            favoriteCodeRankingFormInstance = new HistoryTransparentForm
            {
                Text = "最爱番号排行榜",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            favoriteCodeRankingFormInstance.SuspendLayout();
            ApplyRescaling(favoriteCodeRankingFormInstance, 1000, 700);

            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(favoriteCodeRankingFormInstance.ClientSize.Width, favoriteCodeRankingFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            favoriteCodeRankingFormInstance.BackgroundImage = readyBg;
                            favoriteCodeRankingFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            favoriteCodeRankingFormInstance.FormClosing += (s, e) =>
            {
                if (favoriteCodeRankingFormInstance.BackgroundImage != null)
                {
                    var img = favoriteCodeRankingFormInstance.BackgroundImage;
                    favoriteCodeRankingFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                favoriteCodeRankingUpdateDataAction = null;
                UnregisterAnnualReportRefresh(favoriteCodeRankingFormInstance);
            };

            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, favoriteCodeRankingFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(favoriteCodeRankingFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            var codeData = GetFavoriteCodeUsageData();

            Action updateData = () =>
            {
                codeData = GetFavoriteCodeUsageData();
                if (favoriteCodeRankingFormInstance != null && !favoriteCodeRankingFormInstance.IsDisposed)
                {
                    favoriteCodeRankingFormInstance.Invalidate();
                }
            };

            favoriteCodeRankingUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(favoriteCodeRankingFormInstance, updateData);

            favoriteCodeRankingFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (favoriteCodeRankingFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(favoriteCodeRankingFormInstance.BackgroundImage, 0, 0);
                }

                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, favoriteCodeRankingFormInstance.ClientRectangle);

                DrawFavoriteCodeRankingContent(g, favoriteCodeRankingFormInstance.ClientSize.Width, favoriteCodeRankingFormInstance.ClientSize.Height, codeData);
            };

            favoriteCodeRankingFormInstance.ResumeLayout();
            favoriteCodeRankingFormInstance.Show();
        }

        private List<(string prefix, int count)> GetFavoriteCodeUsageData()
        {
            return GetCurrentYearCodePrefixWatchCountsFromHistoryLog()
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        private void DrawFavoriteCodeRankingContent(Graphics g, int width, int height, List<(string prefix, int count)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(30);
            int startY = S(80);
            int prefixTextHeight = S(20);
            int barHeight = S(25);
            int itemSpacing = S(15);
            int itemTotalHeight = prefixTextHeight + barHeight + S(5);
            int maxBarWidth = width - margin * 2 - S(80);

            using (Font titleFont = new Font("微软雅黑", 18, FontStyle.Bold))
            {
                string title = "最爱番号排行榜";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(25));
            }

            int maxCount = data.Max(d => d.count);

            using (Font prefixFont = new Font("微软雅黑", 10))
            using (Font countFont = new Font("微软雅黑", 10, FontStyle.Bold))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var (prefix, count) = data[i];
                    int y = startY + i * (itemTotalHeight + itemSpacing);

                    string shortPrefix = prefix.Length > 60 ? prefix.Substring(0, 57) + "..." : prefix;
                    g.DrawString(shortPrefix, prefixFont, Brushes.White, margin, y);

                    int barY = y + prefixTextHeight + S(5);
                    int barWidth = maxCount > 0 ? (int)((double)count / maxCount * maxBarWidth) : 0;
                    barWidth = Math.Max(barWidth, S(5));

                    Color barColor = i switch
                    {
                        0 => Color.FromArgb(255, 215, 0),
                        1 => Color.FromArgb(192, 192, 192),
                        2 => Color.FromArgb(205, 127, 50),
                        _ => Color.FromArgb(100, 149, 237)
                    };

                    Rectangle barRect = new Rectangle(margin, barY, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(200, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    string countStr = count.ToString();
                    g.DrawString(countStr, countFont, Brushes.Yellow, margin + barWidth + S(10), barY + (barHeight - countFont.Height) / 2);
                }
            }
        }

        private void ShowUnwatchedFolderRankingWindow()
        {
            if (unwatchedFolderRankingFormInstance != null && !unwatchedFolderRankingFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                unwatchedFolderRankingFormInstance.Activate();
                return;
            }

            unwatchedFolderRankingFormInstance = new HistoryTransparentForm
            {
                Text = "从未看过视频的文件夹分布排行榜",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            unwatchedFolderRankingFormInstance.SuspendLayout();
            ApplyRescaling(unwatchedFolderRankingFormInstance, 1000, 700);

            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(unwatchedFolderRankingFormInstance.ClientSize.Width, unwatchedFolderRankingFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            unwatchedFolderRankingFormInstance.BackgroundImage = readyBg;
                            unwatchedFolderRankingFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            unwatchedFolderRankingFormInstance.FormClosing += (s, e) =>
            {
                if (unwatchedFolderRankingFormInstance.BackgroundImage != null)
                {
                    var img = unwatchedFolderRankingFormInstance.BackgroundImage;
                    unwatchedFolderRankingFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                unwatchedFolderRankingUpdateDataAction = null;
                UnregisterAnnualReportRefresh(unwatchedFolderRankingFormInstance);
            };

            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, unwatchedFolderRankingFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(unwatchedFolderRankingFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            var unwatchedFolderData = GetUnwatchedFolderUsageData();

            Action updateData = () =>
            {
                unwatchedFolderData = GetUnwatchedFolderUsageData();
                if (unwatchedFolderRankingFormInstance != null && !unwatchedFolderRankingFormInstance.IsDisposed)
                {
                    unwatchedFolderRankingFormInstance.Invalidate();
                }
            };

            unwatchedFolderRankingUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(unwatchedFolderRankingFormInstance, updateData);

            unwatchedFolderRankingFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (unwatchedFolderRankingFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(unwatchedFolderRankingFormInstance.BackgroundImage, 0, 0);
                }

                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, unwatchedFolderRankingFormInstance.ClientRectangle);

                DrawUnwatchedFolderRankingContent(g, unwatchedFolderRankingFormInstance.ClientSize.Width, unwatchedFolderRankingFormInstance.ClientSize.Height, unwatchedFolderData);
            };

            unwatchedFolderRankingFormInstance.ResumeLayout();
            unwatchedFolderRankingFormInstance.Show();
        }

        private List<(string folder, int count)> GetUnwatchedFolderUsageData()
        {
            EnsureUnwatchedCacheLoaded();
            lock (unwatchedScanLock)
            {
                return cachedUnwatchedFolderDistribution
                    .Take(10)
                    .ToList();
            }
        }

        private void DrawUnwatchedFolderRankingContent(Graphics g, int width, int height, List<(string folder, int count)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(30);
            int startY = S(80);
            int folderTextHeight = S(20);
            int barHeight = S(25);
            int itemSpacing = S(15);
            int itemTotalHeight = folderTextHeight + barHeight + S(5);
            int maxBarWidth = width - margin * 2 - S(80);

            using (Font titleFont = new Font("微软雅黑", 18, FontStyle.Bold))
            {
                string title = "从未看过视频的文件夹分布排行榜";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(25));
            }

            int maxCount = data.Max(d => d.count);

            using (Font folderFont = new Font("微软雅黑", 10))
            using (Font countFont = new Font("微软雅黑", 10, FontStyle.Bold))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var (folder, count) = data[i];
                    int y = startY + i * (itemTotalHeight + itemSpacing);

                    string shortFolderName = folder.Length > 60 ? folder.Substring(0, 57) + "..." : folder;
                    g.DrawString(shortFolderName, folderFont, Brushes.White, margin, y);

                    int barY = y + folderTextHeight + S(5);
                    int barWidth = maxCount > 0 ? (int)((double)count / maxCount * maxBarWidth) : 0;
                    barWidth = Math.Max(barWidth, S(5));

                    Color barColor = i switch
                    {
                        0 => Color.FromArgb(255, 215, 0),
                        1 => Color.FromArgb(192, 192, 192),
                        2 => Color.FromArgb(205, 127, 50),
                        _ => Color.FromArgb(100, 149, 237)
                    };

                    Rectangle barRect = new Rectangle(margin, barY, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(200, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    string countStr = count.ToString();
                    g.DrawString(countStr, countFont, Brushes.Yellow, margin + barWidth + S(10), barY + (barHeight - countFont.Height) / 2);
                }
            }
        }

        // 显示视频热力图窗口
        private void ShowVideoHeatmapWindow()
        {
            if (videoHeatmapFormInstance != null && !videoHeatmapFormInstance.IsDisposed)
            {
                RequestAnnualReportRefresh(true);
                videoHeatmapFormInstance.Activate();
                return;
            }

            videoHeatmapFormInstance = new HistoryTransparentForm
            {
                Text = "观看最多次数的视频",
                Icon = this.Icon,
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black,
                AutoScroll = false
            };

            videoHeatmapFormInstance.SuspendLayout();
            ApplyRescaling(videoHeatmapFormInstance, 1000, 700);

            // 背景逻辑（使用Report文件夹）
            string? reportImgPath = GetRandomStaticImageFromSubDir("Report");
            if (!string.IsNullOrEmpty(reportImgPath) && File.Exists(reportImgPath))
            {
                try
                {
                    using (FileStream fs = new FileStream(reportImgPath, FileMode.Open, FileAccess.Read))
                    {
                        using (Image original = Image.FromStream(fs))
                        {
                            Bitmap readyBg = new Bitmap(videoHeatmapFormInstance.ClientSize.Width, videoHeatmapFormInstance.ClientSize.Height);
                            using (Graphics g = Graphics.FromImage(readyBg))
                            {
                                DrawAspectFillBackground(g, original, new Rectangle(0, 0, readyBg.Width, readyBg.Height));
                            }
                            videoHeatmapFormInstance.BackgroundImage = readyBg;
                            videoHeatmapFormInstance.BackgroundImageLayout = ImageLayout.None;
                        }
                    }
                }
                catch { }
            }

            // 窗口关闭时清理资源
            videoHeatmapFormInstance.FormClosing += (s, e) =>
            {
                if (videoHeatmapFormInstance.BackgroundImage != null)
                {
                    var img = videoHeatmapFormInstance.BackgroundImage;
                    videoHeatmapFormInstance.BackgroundImage = null;
                    img.Dispose();
                }
                videoHeatmapUpdateDataAction = null;
                UnregisterAnnualReportRefresh(videoHeatmapFormInstance);
            };

            // 启用双缓冲
            typeof(Form).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, videoHeatmapFormInstance, new object[] { true });

            var setStyleMethod = typeof(Control).GetMethod("SetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setStyleMethod != null)
            {
                setStyleMethod.Invoke(videoHeatmapFormInstance, new object[] {
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer, true
                });
            }

            // 获取视频使用数据
            var videoData = GetVideoUsageData();

            // 创建刷新数据的委托
            Action updateData = () =>
            {
                videoData = GetVideoUsageData();
                if (videoHeatmapFormInstance != null && !videoHeatmapFormInstance.IsDisposed)
                {
                    videoHeatmapFormInstance.Invalidate();
                }
            };

            videoHeatmapUpdateDataAction = updateData;
            RegisterAnnualReportRefresh(videoHeatmapFormInstance, updateData);

            // 绘制视频热力图
            videoHeatmapFormInstance.Paint += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                if (videoHeatmapFormInstance.BackgroundImage != null)
                {
                    g.DrawImage(videoHeatmapFormInstance.BackgroundImage, 0, 0);
                }

                // 绘制半透明黑色滤镜
                using (SolidBrush mask = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                    g.FillRectangle(mask, videoHeatmapFormInstance.ClientRectangle);

                // 绘制视频热力图内容
                DrawVideoHeatmapContent(g, videoHeatmapFormInstance.ClientSize.Width, videoHeatmapFormInstance.ClientSize.Height, videoData);
            };

            videoHeatmapFormInstance.ResumeLayout();
            videoHeatmapFormInstance.Show();
        }

        // 获取视频使用数据（年度报告用，统计当年 History.log 中的全部视频记录）
        private List<(string video, int count)> GetVideoUsageData()
        {
            var videoCounts = GetCurrentYearVideoWatchCountsFromHistoryLog();

            // 按使用次数降序排列，取前10个
            return videoCounts.OrderByDescending(kvp => kvp.Value).Take(10).Select(kvp => (kvp.Key, kvp.Value)).ToList();
        }

        // 绘制视频热力图内容
        private void DrawVideoHeatmapContent(Graphics g, int width, int height, List<(string video, int count)> data)
        {
            if (data.Count == 0)
            {
                using (Font font = new Font("微软雅黑", 16))
                {
                    string text = "暂无数据";
                    SizeF textSize = g.MeasureString(text, font);
                    g.DrawString(text, font, Brushes.White, (width - textSize.Width) / 2, (height - textSize.Height) / 2);
                }
                return;
            }

            int margin = S(30);
            int startY = S(80);
            int videoTextHeight = S(20); // 视频名称行高度
            int barHeight = S(25); // 柱状图高度
            int itemSpacing = S(15); // 每个项目之间的间距
            int itemTotalHeight = videoTextHeight + barHeight + S(5); // 每个项目的总高度
            int maxBarWidth = width - margin * 2 - S(80); // 留出右侧数字空间

            // 绘制标题
            using (Font titleFont = new Font("微软雅黑", 18, FontStyle.Bold))
            {
                string title = "观看最多次数的视频";
                SizeF titleSize = g.MeasureString(title, titleFont);
                g.DrawString(title, titleFont, Brushes.White, (width - titleSize.Width) / 2, S(25));
            }

            int maxCount = data.Max(d => d.count);

            using (Font videoFont = new Font("微软雅黑", 10))
            using (Font countFont = new Font("微软雅黑", 10, FontStyle.Bold))
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var (video, count) = data[i];
                    int y = startY + i * (itemTotalHeight + itemSpacing);

                    // 第一行：绘制视频文件名
                    string shortVideoName = video.Length > 60 ? video.Substring(0, 57) + "..." : video;
                    g.DrawString(shortVideoName, videoFont, Brushes.White, margin, y);

                    // 第二行：绘制柱状图
                    int barY = y + videoTextHeight + S(5);
                    
                    // 计算条形宽度
                    int barWidth = maxCount > 0 ? (int)((double)count / maxCount * maxBarWidth) : 0;
                    barWidth = Math.Max(barWidth, S(5)); // 最小宽度

                    // 根据排名选择颜色
                    Color barColor = i switch
                    {
                        0 => Color.FromArgb(255, 215, 0),   // 金色
                        1 => Color.FromArgb(192, 192, 192), // 银色
                        2 => Color.FromArgb(205, 127, 50),  // 铜色
                        _ => Color.FromArgb(100, 149, 237)  // 其他用蓝色
                    };

                    // 绘制圆角条形
                    Rectangle barRect = new Rectangle(margin, barY, barWidth, barHeight);
                    using (GraphicsPath barPath = CreateRoundedRectanglePath(barRect, S(5)))
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(200, barColor)))
                    {
                        g.FillPath(barBrush, barPath);
                    }

                    // 绘制次数（在条形右侧）
                    string countStr = count.ToString();
                    SizeF countSize = g.MeasureString(countStr, countFont);
                    g.DrawString(countStr, countFont, Brushes.Yellow, margin + barWidth + S(10), barY + (barHeight - countFont.Height) / 2);
                }
            }
        }

        private void DrawAspectFillBackground(Graphics g, System.Drawing.Image img, Rectangle clientRect)
        {
            if (img == null) return;

            // --- 核心：如果是 GIF，激活当前帧 ---
            if (ImageAnimator.CanAnimate(img))
            {
                ImageAnimator.UpdateFrames(img);
            }

            // 原有的缩放逻辑保持不变
            float ratio = Math.Max((float)clientRect.Width / img.Width, (float)clientRect.Height / img.Height);
            int newWidth = (int)(img.Width * ratio);
            int newHeight = (int)(img.Height * ratio);
            int posX = (clientRect.Width - newWidth) / 2;
            int posY = (clientRect.Height - newHeight) / 2;

            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, posX, posY, newWidth, newHeight);
        }

        // 【新增】自定义绘制Label，替代控件（在Form的Paint事件中调用）
        private void DrawCustomLabel(Graphics g, System.Windows.Forms.Label lbl, string text, bool isFolderName)
        {
            if (string.IsNullOrEmpty(text)) return;

            float sc = GetScale();
            float fSc = 1.0f + (sc - 1.0f) * 0.1f;

            var state = g.Save();
            g.TranslateTransform(lbl.Left, lbl.Top);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int radius = S(25);
            using (GraphicsPath path = new GraphicsPath())
            {
                float borderThickness = 1.5f * sc;
                RectangleF rect = new RectangleF(borderThickness / 2, borderThickness / 2,
                    lbl.Width - borderThickness, lbl.Height - borderThickness);

                path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
                path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
                path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
                path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
                path.CloseAllFigures();

                // 半透明白色背景
                using (SolidBrush backBrush = new SolidBrush(Color.FromArgb(140, 255, 255, 255)))
                    g.FillPath(backBrush, path);

                // 黑色边框
                using (Pen borderPen = new Pen(Color.FromArgb(200, 0, 0, 0), borderThickness))
                    g.DrawPath(borderPen, path);
            }

            using (GraphicsPath textPath = new GraphicsPath())
            {
                float baseFontSize = isFolderName ? 23f : 13f;

                using (Font adaptiveFont = new Font("微软雅黑", baseFontSize * fSc, FontStyle.Bold))
                {
                    float currentEmSize = g.DpiY * adaptiveFont.Size / 72;
                    Rectangle textRect;
                    bool isShortText = (isFolderName || text.Length <= 18);

                    if (isShortText)
                    {
                        textRect = new Rectangle(-lbl.Width, S(10), lbl.Width * 3, lbl.Height - S(20));
                    }
                    else
                    {
                        textRect = new Rectangle(S(8), S(10), lbl.Width - S(16), lbl.Height - S(20));
                    }

                    using (StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter
                    })
                    {
                        if (isShortText)
                        {
                            sf.FormatFlags |= StringFormatFlags.NoWrap;
                        }

                        textPath.AddString(text, adaptiveFont.FontFamily, (int)adaptiveFont.Style,
                            currentEmSize, textRect, sf);
                    }
                }

                // 黑色描边
                using (Pen outlinePen = new Pen(Color.FromArgb(200, 0, 0, 0), 2.5f * sc))
                {
                    outlinePen.LineJoin = LineJoin.Round;
                    g.DrawPath(outlinePen, textPath);
                }

                // 粉色填充
                using (SolidBrush textBrush = new SolidBrush(ColorTranslator.FromHtml("#FF1493")))
                {
                    g.FillPath(textBrush, textPath);
                }
            }

            g.Restore(state);
        }
    }

}
