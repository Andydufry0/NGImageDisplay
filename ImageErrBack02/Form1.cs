using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace ImageErrBack02
{
    public partial class Form1 : Form
    {
        private TcpClient tcpClient;
        private ImageGridPanel _imageGrid;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            InitParams();
            InitTcpClient();
            InitImageGrid();
        }

        private void InitImageGrid()
        {
            // 移除 Designer 中预设的 picBox1
            splitContainer1.Panel1.Controls.Clear();

            // 容器 Panel 包裹 ImageGridPanel + 折叠按钮
            var container = new Panel { Dock = DockStyle.Fill };

            _imageGrid = new ImageGridPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45)
            };
            _imageGrid.OnRightClickImage += ShowPreviewWindow;
            container.Controls.Add(_imageGrid);

            // 标准折叠按钮，Dock.Right 固定在右侧边缘
            _toggleBtn = new Button
            {
                Text = "\u25C0",
                Width = 18,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand
            };
            _toggleBtn.FlatAppearance.BorderSize = 0;
            _toggleBtn.Click += ToggleRightPanel;
            container.Controls.Add(_toggleBtn);

            splitContainer1.Panel1.Controls.Add(container);
        }

        private bool _isRightPanelCollapsed = false;
        private int _savedSplitterDistance = 0;
        private Button _toggleBtn;

        private void ToggleRightPanel(object sender, EventArgs e)
        {
            if (_isRightPanelCollapsed)
            {
                splitContainer1.Panel2Collapsed = false;
                splitContainer1.SplitterDistance = _savedSplitterDistance;
                _toggleBtn.Text = "\u25C0"; // ◀
                _isRightPanelCollapsed = false;
            }
            else
            {
                _savedSplitterDistance = splitContainer1.SplitterDistance;
                splitContainer1.Panel2Collapsed = true;
                _isRightPanelCollapsed = true;
                _toggleBtn.Text = "\u25B6"; // ▶
            }
        }

        private Form _previewForm;
        private PicBox _previewPicBox;

        private void ShowPreviewWindow(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return;

            // 如果窗口已存在，复用
            if (_previewForm != null && !_previewForm.IsDisposed)
            {
                _previewPicBox.ImageFilePath = imagePath;
                LoadPreviewImage(imagePath);
                _previewForm.Activate();
                return;
            }

            // 创建新窗口
            _previewForm = new Form
            {
                Text = "图片预览",
                FormBorderStyle = FormBorderStyle.Sizable,
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(1000, 750),
                MinimumSize = new Size(400, 300)
            };

            _previewPicBox = new PicBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                AutoFitOnSetImage = true,
                AutoFitOnResize = true,
                EnableDragClamp = false,
                MinZoom = 0.1f,
                MaxZoom = 20f
            };

            _previewForm.Controls.Add(_previewPicBox);
            _previewForm.FormClosing += (s, e) =>
            {
                _previewPicBox.Image?.Dispose();
                _previewPicBox.Image = null;
                _previewPicBox = null;
                _previewForm = null;
            };

            LoadPreviewImage(imagePath);
            _previewForm.Show();
        }

        private async void LoadPreviewImage(string imagePath)
        {
            if (_previewPicBox == null) return;

            try
            {
                var bmp = await Task.Run(() =>
                {
                    using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var tmp = Image.FromStream(fs))
                        return new Bitmap(tmp);
                });

                if (_previewPicBox != null && !_previewPicBox.IsDisposed)
                {
                    _previewPicBox.Image?.Dispose();
                    _previewPicBox.Image = bmp;
                }
                else
                {
                    bmp.Dispose();
                }
            }
            catch (Exception ex)
            {
                userControl_Log1.LogMsg($"预览图加载失败: {ex.Message}");
            }
        }

        private void InitParams()
        {
            try
            {
                var config = sharedvar.LoadParams();
                if (config == null) return; // 如果返回 null，直接退出

                // 逐个安全获取，不存在或空字符串则不赋值
                if (config.TryGetValue("IP", out string ip) && !string.IsNullOrEmpty(ip))
                    richTextBox1.Text = ip;

                if (config.TryGetValue("Port", out string port) && !string.IsNullOrEmpty(port))
                    richTextBox2.Text = port;

                // 图片根目录：收到条码后在此目录下找同名文件夹
                _imageRootPath = GetConfigValue(config, "ImagePath");
                UpdatePathMenuText();
            }
            catch (Exception e)
            {
                userControl_Log1.LogMsg($"加载配置异常: {e.Message}");
            }
        }

        /// <summary>
        /// 图片根目录：收到条码后在此目录下找同名文件夹
        /// </summary>
        private string _imageRootPath = "";

        /// <summary>
        /// 刷新"设置->图片路径"的菜单文字，过长时截断显示，完整路径放 ToolTip
        /// </summary>
        private void UpdatePathMenuText()
        {
            if (string.IsNullOrEmpty(_imageRootPath))
            {
                imagePathItem.Text = "图片路径（未设置）";
                imagePathItem.ToolTipText = "点击选择条码图片所在的根目录";
                return;
            }

            string show = _imageRootPath.Length > 48
                ? "…" + _imageRootPath.Substring(_imageRootPath.Length - 45)
                : _imageRootPath;

            imagePathItem.Text = "图片路径：" + show;
            imagePathItem.ToolTipText = _imageRootPath;
        }

        private void imagePathItem_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "请选择条码图片所在的根目录";
                if (!string.IsNullOrEmpty(_imageRootPath) && Directory.Exists(_imageRootPath))
                    folderDialog.SelectedPath = _imageRootPath;

                if (folderDialog.ShowDialog() != DialogResult.OK)
                    return;

                _imageRootPath = folderDialog.SelectedPath;
                UpdatePathMenuText();
                sharedvar.SaveParams("ImagePath", _imageRootPath);
                userControl_Log1.LogMsg($"图片路径已设置：{_imageRootPath}");
            }
        }

        /// <summary>
        /// 读取配置项，不存在或空白返回空串
        /// </summary>
        private static string GetConfigValue(Dictionary<string, string> config, string key)
        {
            string v;
            return config.TryGetValue(key, out v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : "";
        }

        private void InitTcpClient()
        {
            richTextBox3.Clear();

            // 2. 实例化TCP客户端
            tcpClient = new TcpClient
            {
                AutoReconnectEnabled = true,
                MaxReconnectAttempts = 0,      // 0 = 无限重连
                HeartbeatEnabled = false,       // RS100不需要心跳
                ConnectTimeoutMs = 5000,
                ReconnectBaseIntervalMs = 3000,

                // UI同步上下文，事件直接UI线程，无需Invoke
                SyncContext = SynchronizationContext.Current,

                // 日志回调，清除字符串内部换行
                Logger = msg =>
                {
                    string cleanMsg = msg.Replace("\r", "").Replace("\n", " ");
                    userControl_Log1.LogMsg(cleanMsg);
                }
            };

            // 3. 绑定事件
            tcpClient.OnReceived += TcpClient_OnReceived;
            tcpClient.OnConnectionChanged += TcpClient_OnConnectionChanged;

            userControl_Log1.LogMsg($"TCP客户端初始化完成");
        }

        private async void button3_Click(object sender, EventArgs e)
        {
            if (tcpClient == null) return;

            var _ip = richTextBox1.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(_ip))
            {
                userControl_Log1.LogMsg("请输入读码器IP");
                return;
            }

            if (!int.TryParse(richTextBox2.Text, out int port))
            {
                userControl_Log1.LogMsg("请输入有效的端口号");
                return;
            }

            try
            {
                bool ok = await tcpClient.ConnectAsync(_ip, port);
                if (ok)
                {
                    sharedvar.SaveParams("IP", richTextBox1.Text);
                    sharedvar.SaveParams("Port", richTextBox2.Text);
                    tcpClient.AutoReconnectEnabled = true;
                }
                else
                {
                    userControl_Log1.LogMsg("连接失败，请检查网络或读码器状态");
                }
            }
            catch (Exception ex)
            {
                userControl_Log1.LogMsg($"连接异常：{ex.Message}");
            }
        }
        private void button4_Click(object sender, EventArgs e)
        {
            if (tcpClient == null)
            {
                userControl_Log1.LogMsg("TCP-Client NULL");
                return;
            }

            if (!tcpClient.IsConnected)
            {
                userControl_Log1.LogMsg("当前已经是断开状态");
                return;
            }
            tcpClient.AutoReconnectEnabled = false;
            // 执行断开，会触发OnConnectionChanged断开事件
            tcpClient.SafeClose(true);
            userControl_Log1.LogMsg("手动执行断开连接");
        }

        private void TcpClient_OnReceived(string barcode)
        {
            // 读码器常在条码尾部附加 \r\n，必须先清理，否则目录名永远拼不上
            string code = (barcode ?? "").Trim();
            richTextBox3.Text = code;

            if (code.Length == 0)
            {
                userControl_Log1.LogMsg("收到空条码，已忽略");
                return;
            }

            userControl_Log1.LogMsg($"收到条码：{code}");

            // 1. 在设置的路径下按条码找文件夹并读取图片
            string folder;
            List<string> imagePaths = FindImagesByBarcode(code, out folder);

            if (imagePaths.Count == 0)
            {
                userControl_Log1.LogMsg($"未找到条码对应的图片：{code}");
                // 同一个失败条码不重复弹窗，避免读码器连续触发时刷屏
                if (_lastFailBarcode != code)
                {
                    _lastFailBarcode = code;
                    MessageBox.Show(
                        $"未在设置的图片路径下找到条码对应的图片。\r\n\r\n条码：{code}\r\n\r\n请确认 设置->图片路径 正确，且该路径下存在以条码命名的文件夹（文件夹内需有图片）。",
                        "未找到图片", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            _lastFailBarcode = null;
            userControl_Log1.LogMsg($"匹配到文件夹：{folder}，共 {imagePaths.Count} 张图片");
            _ = CreatPicBox(imagePaths);
        }

        private string _lastFailBarcode = null;

        // 文件夹命名时不能使用的字符
        private static readonly char[] InvalidNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>
        /// 按条码在设置的三个根路径下查找图片文件夹并返回其中的图片
        /// 命名兼容两种：条码原文、条码中的非法文件名字符替换为 _ 后的名字
        /// 找不到返回空列表，matchedFolder 为命中目录（未命中为 null）
        /// </summary>
        private List<string> FindImagesByBarcode(string barcode, out string matchedFolder)
        {
            matchedFolder = null;
            var result = new List<string>();

            // 兜底清理：读码器可能在条码后附带 \r\n 或空格
            barcode = (barcode ?? "").Trim();
            if (barcode.Length == 0)
                return result;

            string root = GetRootPath();
            if (root == null)
            {
                userControl_Log1.LogMsg("图片路径未设置或不存在，请用 设置->图片路径 重新选择");
                return result;
            }

            // 候选文件夹名：原文 + 安全名（条码含 URL 字符时 Windows 不认原文）
            string safeName = ToSafeFolderName(barcode);
            foreach (string name in new[] { barcode, safeName })
            {
                if (string.IsNullOrEmpty(name)) continue;

                string full;
                try
                {
                    full = Path.Combine(root, name);
                }
                catch
                {
                    continue;   // 路径过长或含非法字符，跳过该候选
                }

                if (!Directory.Exists(full))
                    continue;

                var images = LoadImage(full);
                if (images.Count > 0)
                {
                    matchedFolder = full;
                    return images;
                }

                userControl_Log1.LogMsg($"文件夹存在但无图片：{full}");
            }

            return result;
        }

        /// <summary>
        /// 取设置中的图片根目录，未设置或目录不存在返回 null
        /// </summary>
        private string GetRootPath()
        {
            if (string.IsNullOrWhiteSpace(_imageRootPath))
                return null;

            string p = _imageRootPath.Trim().Trim('"');
            return Directory.Exists(p) ? p : null;
        }

        /// <summary>
        /// 把条码转成合法的文件夹名：非法字符替换为 _，去掉尾部点和空格
        /// </summary>
        private static string ToSafeFolderName(string barcode)
        {
            if (string.IsNullOrEmpty(barcode)) return null;

            var sb = new StringBuilder(barcode.Length);
            foreach (char c in barcode)
                sb.Append(c < 32 || Array.IndexOf(InvalidNameChars, c) >= 0 ? '_' : c);

            string name = sb.ToString().Trim().TrimEnd('.', ' ');
            return name.Length == 0 ? null : name;
        }
        private void TcpClient_OnConnectionChanged(bool isConnected, string info)
        {
            if (isConnected)
            {
                label3.Text = "✅  已连接";
                label3.BackColor = Color.Green;
                userControl_Log1.LogMsg("已连接");
            }
            else
            {
                label3.Text = "❌  已断开";
                label3.BackColor = Color.Gray;
                userControl_Log1.LogMsg("已断开");
                richTextBox3.Text = "";
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveParams();

            // 清理预览窗口
            if (_previewForm != null && !_previewForm.IsDisposed)
            {
                _previewPicBox?.Image?.Dispose();
                _previewForm.Close();
            }

            if (tcpClient != null)
            {
                // 解绑事件，防止内存泄漏
                tcpClient.OnReceived -= TcpClient_OnReceived;
                tcpClient.OnConnectionChanged -= TcpClient_OnConnectionChanged;
                tcpClient.Dispose();
                tcpClient = null;
            }
            base.OnFormClosing(e);
        }

        private void SaveParams()
        {
            sharedvar.SaveParams("IP", richTextBox1.Text);
            sharedvar.SaveParams("Port", richTextBox2.Text);
            sharedvar.SaveParams("ImagePath", _imageRootPath);
        }
        public static List<string> LoadImage(string folderPath,string[] searchPatterns = null)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return new List<string>();

        if (searchPatterns == null || searchPatterns.Length == 0)
        {
            searchPatterns = new string[]
            {
            "*.jpg", "*.jpeg", "*.png", "*.bmp",
            "*.gif", "*.tiff", "*.ico", "*.webp"
            };
        }

        var imagePaths = new List<string>();
        try
        {
            foreach (string pattern in searchPatterns)
            {
                string[] files = Directory.GetFiles(folderPath, pattern, SearchOption.TopDirectoryOnly);
                imagePaths.AddRange(files);
            }
            // 去重并排序
            imagePaths = imagePaths.Distinct().OrderBy(p => Path.GetFileName(p)).ToList();
        }
        catch
        {
            return new List<string>();
        }
        return imagePaths;
    }


    private async Task CreatPicBox(List<string> imagePaths, CancellationToken cancellationToken = default)
    {
        if (imagePaths == null || imagePaths.Count == 0)
            return;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 设置图片路径到网格面板
        _imageGrid.SetImagePaths(imagePaths);

        // 异步加载缩略图
        await _imageGrid.LoadThumbnailsAsync(cancellationToken);

        sw.Stop();
        userControl_Log1.LogMsg($"图片加载完成，共 {imagePaths.Count} 张，耗时 {sw.ElapsedMilliseconds} ms");
    }

    private void button1_Click(object sender, EventArgs e)
        {


        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "请选择包含图片的文件夹";
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string folder = folderDialog.SelectedPath;

                    // 1. 读取图片路径列表
                    List<string> imagePaths = LoadImage(folder);

                    if (imagePaths.Count == 0)
                    {
                        MessageBox.Show("该文件夹中没有支持的图片文件");
                        return;
                    }

                    // 2. 根据图片数量创建 PictureBox
                    _ = CreatPicBox(imagePaths);

                    // 可选：显示加载数量
                    userControl_Log1.LogMsg($"已加载 {imagePaths.Count} 张图片");
                }
            }

        }

    }
}
