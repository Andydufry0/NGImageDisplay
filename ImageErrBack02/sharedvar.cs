using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;


namespace ImageErrBack02
{
    public static class sharedvar
    {

        public static void SaveParams(string name, string value, string filePath = "Data\\config.ini")
        {
            try
            {
                // 1. 确保目录存在
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // 2. 读取现有配置（如果文件存在）
                List<string> lines = new List<string>();
                if (File.Exists(filePath))
                    lines.AddRange(File.ReadAllLines(filePath, Encoding.UTF8));

                // 3. 查找是否已存在该键（忽略大小写），更新或添加
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    // 匹配格式：[key=value]
                    if (lines[i].StartsWith($"[{name}=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"[{name}={value}]";
                        found = true;
                        break;
                    }
                }
                if (!found)
                    lines.Add($"[{name}={value}]");

                // 4. 写回文件（覆盖）
                File.WriteAllLines(filePath, lines, Encoding.UTF8);
            }
            catch
            {
                // 静默失败（可根据需要改为记录日志或抛出）
                // 例如：Log.Error($"保存配置失败: {ex.Message}");
            }
        }

        public static Dictionary<string, string> LoadParams(string filePath = "Data\\config.ini")
        {
            var result = new Dictionary<string, string>();

            // 如果文件不存在，直接返回空字典（调用方自己处理默认值）
            if (!File.Exists(filePath))
                return result;

            try
            {
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                foreach (string line in lines)
                {
                    // 匹配 [key=value] 格式，忽略前后空格
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        // 去掉首尾的方括号
                        string content = trimmed.Substring(1, trimmed.Length - 2);
                        int eqIndex = content.IndexOf('=');
                        if (eqIndex > 0) // 确保有 '=' 且 key 不为空
                        {
                            string key = content.Substring(0, eqIndex).Trim();
                            string value = content.Substring(eqIndex + 1).Trim();
                            if (!string.IsNullOrEmpty(key))
                                result[key] = value;
                        }
                    }
                }
            }
            catch
            {
                // 读取失败返回空字典（或可记录日志）
            }

            return result;
        }
    }
}
