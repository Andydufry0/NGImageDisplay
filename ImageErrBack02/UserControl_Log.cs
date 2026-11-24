using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImageErrBack02
{
    public partial class UserControl_Log : UserControl
    {
        public UserControl_Log()
        {
            _logPath = "./Log.log";
            InitializeComponent();
            this.Dock = DockStyle.Fill;

        }

        private string _logPath;

        [Description("日志文件保存路径"), Category("自定义")]
        public string LogPath
        {
            get => _logPath;
            set
            {
                // 可选：添加路径验证逻辑
                if (!string.IsNullOrEmpty(value))
                {
                    _logPath = value;
                }
            }
        }

        /// <summary>
        /// 输出日志
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="color"></param>
        public void LogMsg(string msg, System.Drawing.Color color = default)
        {
            if (richTextBox1.InvokeRequired)
            {
                richTextBox1.Invoke(new Action(() => LogMsg(msg, color)));
                return;
            }

            // 删除最早行（当超过限制时）
            if (this.richTextBox1.Lines.Length >= 100)
            {
                int firstLineEnd = this.richTextBox1.GetFirstCharIndexFromLine(1);
                if (firstLineEnd >= 0) // 确保找到第二行
                {
                    this.richTextBox1.Select(0, firstLineEnd);
                    this.richTextBox1.SelectedText = "";
                }
            }

            // 添加新日志（保留格式）
            this.richTextBox1.SelectionStart = this.richTextBox1.TextLength;
            this.richTextBox1.SelectionColor = color == default ? System.Drawing.Color.Black : color;
            string logmsg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ▶ {msg}{Environment.NewLine}";
            this.richTextBox1.AppendText(logmsg);
            this.richTextBox1.ScrollToCaret();

            Task.Run(() => SaveLogToLocal(logmsg));
        }
        /// <summary>
        /// 保存日志
        /// </summary>
        /// <param name="logmsg"></param>
        private void SaveLogToLocal(string logmsg)
        {
            // 确保日志目录存在
            string dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir); // 如果目录不存在则创建，存在则不做任何操作
            }

            using (var sw = new StreamWriter(_logPath, true, Encoding.UTF8))
            {
                sw.Write(logmsg);
            }

        }
        /// <summary>
        /// 清空log
        /// </summary>
        public void ClearLog()
        {
            this.richTextBox1.Clear();
        }

    }
}
