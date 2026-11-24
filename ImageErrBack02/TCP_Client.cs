using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
// 使用别名解决命名冲突
using NetTcpClient = System.Net.Sockets.TcpClient;
using TimersTimer = System.Timers.Timer;

namespace ImageErrBack02
{
    /// <summary>
    /// RS100 读码器 TCP 客户端【极简粗暴版，对外接口完全不变】
    /// 移除：自动重连、心跳、线程同步上下文、精细锁、异常隔离；仅保留基础收发
    /// </summary>
    public class TcpClient : IDisposable
    {
        #region 私有字段（字段名原样保留）
        private NetTcpClient _tcpClient;
        private NetworkStream _stream;
        private readonly StringBuilder _recvSb = new StringBuilder();
        private readonly object _syncLock = new object();
        private bool _isReceiving, _isConnecting, _disposed;
        private SynchronizationContext _syncContext;
        private TimersTimer _heartbeatTimer;
        private CancellationTokenSource _reconnectCts;
        private int _reconnectAttempt;
        private string _lastIp;
        private int _lastPort;
        private Action<string> _logger;
        #endregion

        #region 事件与属性【签名完全不变，保留全部原有对外成员】
        public event Action<string> OnReceived;
        public event Action<bool, string> OnConnectionChanged;

        public Action<string> Logger { get => _logger; set => _logger = value; }
        public SynchronizationContext SyncContext { get => _syncContext; set => _syncContext = value; }

        public bool IsConnected
        {
            get
            {
                return _tcpClient != null && _tcpClient.Connected && _stream != null;
            }
        }

        public bool AutoReconnectEnabled { get; set; }
        public int ReconnectBaseIntervalMs { get; set; } = 3000;
        public int MaxReconnectAttempts { get; set; } = 10;
        public bool HeartbeatEnabled { get; set; }
        public int HeartbeatIntervalMs { get; set; } = 5000;
        public int ConnectTimeoutMs { get; set; } = 5000;

        public void ResetReconnectAttempt() => _reconnectAttempt = 0;
        #endregion

        #region 核心方法【方法名、入参、返回值完全对齐旧版本】
        public async Task<bool> ConnectAsync(string ip, int port = 8501, bool resetAttempt = true)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpClient));

            if (IsConnected) return true;
            if (resetAttempt) _reconnectAttempt = 0;
            _lastIp = ip;
            _lastPort = port;

            SafeClose(false);
            Log($"Connecting to {ip}:{port} ...");
            try
            {
                _tcpClient = new NetTcpClient();
                using (var cts = new CancellationTokenSource(ConnectTimeoutMs))
                {
                    var connectTask = _tcpClient.ConnectAsync(IPAddress.Parse(ip), port);
                    var completed = await Task.WhenAny(connectTask, Task.Delay(ConnectTimeoutMs, cts.Token));
                    if (completed == connectTask)
                    {
                        await connectTask;
                    }
                    else
                    {
                        throw new TimeoutException($"Connect timeout ({ConnectTimeoutMs}ms)");
                    }
                }
                _stream = _tcpClient.GetStream();
                _isReceiving = true;
                _ = ReceiveLoopAsync();

                OnConnectionChanged?.Invoke(true, "Connected");
                Log("Connected");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Connect failed: {ex.Message}");
                OnConnectionChanged?.Invoke(false, $"Failed: {ex.Message}");
                SafeClose(false);
                return false;
            }
        }

        private async Task ReceiveLoopAsync()
        {
            byte[] buffer = new byte[4096];
            Encoding encoding = Encoding.GetEncoding(936);
            try
            {
                while (_isReceiving && _tcpClient?.Connected == true)
                {
                    int len = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (len <= 0)
                    {
                        Log("Remote closed");
                        break;
                    }
                    string raw = encoding.GetString(buffer, 0, len);
                    OnReceived?.Invoke(raw);
                }
            }
            catch (Exception ex)
            {
                Log($"Receive error: {ex.Message}");
                OnConnectionChanged?.Invoke(false, $"Receive error: {ex.Message}");
            }
            finally
            {
                _isReceiving = false;
                SafeClose(true);
            }
        }

        public async Task<bool> SendCommandAsync(string cmd)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpClient));
            if (!IsConnected || _stream == null) return false;
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(cmd);
                await _stream.WriteAsync(data, 0, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Send failed: {ex.Message}");
                return false;
            }
        }

        public void SafeClose(bool raiseEvent = true)
        {
            _isReceiving = false;
            try { _stream?.Close(); } catch { }
            try
            {
                if (_tcpClient?.Client != null)
                    _tcpClient.Client.Shutdown(SocketShutdown.Both);
            }
            catch { }
            try { _tcpClient?.Close(); } catch { }

            _stream = null;
            _tcpClient = null;
            _recvSb.Clear();

            if (raiseEvent)
                OnConnectionChanged?.Invoke(false, "Disconnected");
        }
        #endregion

        #region 空壳占位（保留原有成员，内部逻辑全部清空，兼容旧调用代码）
        private void StartHeartbeat() { }
        private void StopHeartbeat() { }
        private void OnHeartbeat(object sender, ElapsedEventArgs e) { }
        private async Task HeartbeatWorkAsync() { }

        private void StartReconnect() { }
        private void StopReconnect() { }
        private async Task ReconnectLoopAsync(CancellationToken token) { }

        private void FireEvent<T>(Action<T> handler, T arg) { }
        private void FireEvent<T1, T2>(Action<T1, T2> handler, T1 arg1, T2 arg2) { }
        private void FireEvent(Delegate handler, params object[] args) { }
        private void InvokeSafe(Delegate d, object[] args) { }

        private static void SetKeepAlive(NetTcpClient client, int timeMs, int intervalMs) { }
        private void Log(string msg) => _logger?.Invoke($"[{msg}]");
        #endregion

        #region IDisposable 保留原始签名
        ~TcpClient()
        {
            Dispose(false);
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                StopReconnect();
                StopHeartbeat();
                SafeClose(false);
                Log("Disposed");
            }
            _disposed = true;
        }
        #endregion
    }
}
