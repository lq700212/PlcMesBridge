// =========================================================================
// MelsecPlcClient：三菱 MC 协议实现（IPlcClient 真机版）
//
// 干什么：包一层 HslCommunication 的 MelsecMcNet（二进制）/ MelsecMcAsciiNet
//   （ASCII，老项目 1/2/5 号机用），用哪个由构造参数定（A版硬编码分支消除）。
// 引用的 Hsl 是子模块 extern/kaleidoscope/Kaleidoscope/libs/HslCommunication.dll
//   （公司已购 12.6.0），版本与子模块统一，不从 NuGet 另引。
//
// 自愈范式（照搬 kaleidoscope HslPlcClient，朴素策略）：
// ① 读写失败一律标记断线（IsConnected=false），下次读写先重连（节流 5s，
//    避免失败风暴里每次读写都阻塞一个连接超时）；
// ② 连接用 ConnectServer + ConnectTimeOut（Hsl 内部 BeginConnect 式超时，
//    不冻 UI——但本类仍只在后台线程被调用，双保险）；
// ③ 授权：构造/连接前调 HslAuthorization.EnsureRegistered()（幂等，
//    无 hsl.dat 时只记 Warn 不抛，见子模块 Utils/HslAuthorization.cs）。
//
// Hsl 的 ReadString 返回可能含 '\0'，原样上交（去 '\0' 是业务层的事）。
// =========================================================================

using HslCommunication;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.Net;
using HslCommunication.Profinet.Melsec;
using Kaleidoscope.Utils;

namespace PlcMesBridge.Core.Comms;

public sealed class MelsecPlcClient : IPlcClient
{
    // 双字段（照搬子模块 HslPlcClient）：_conn 管连接（ConnectServer/Close/超时），
    // _rw 管读写（同一实例的 IReadWriteNet 视图，一套接口吃掉读写）。
    private DeviceTcpNet? _conn;
    private IReadWriteNet? _rw;
    private readonly bool _useAscii;
    private readonly object _lock = new();
    private DateTime _lastReconnectAt = DateTime.MinValue;
    private bool _disposed;

    public bool IsConnected { get; private set; }
    public string IpAddress { get; private set; } = "";
    public int Port { get; private set; }

    /// <param name="useAscii">true=MelsecMcAsciiNet（老项目 1/2/5 号机），false=MelsecMcNet。</param>
    public MelsecPlcClient(bool useAscii = false)
    {
        _useAscii = useAscii;
    }

    public bool Connect(string ip, int port, int timeoutMs = 3000)
    {
        lock (_lock)
        {
            DisconnectLocked();
            // 授权幂等注册（无授权不抛，见类注释）。
            HslAuthorization.EnsureRegistered();

            DeviceTcpNet dev = _useAscii
                ? new MelsecMcAsciiNet(ip, port)
                : new MelsecMcNet(ip, port);
            dev.ConnectTimeOut = timeoutMs;
            OperateResult conn = dev.ConnectServer();
            if (!conn.IsSuccess)
            {
                dev.ConnectClose();
                IsConnected = false;
                return false;
            }
            _conn = dev;
            _rw = dev;
            IpAddress = ip;
            Port = port;
            IsConnected = true;
            _lastReconnectAt = DateTime.Now;
            return true;
        }
    }

    public void Disconnect()
    {
        lock (_lock)
            DisconnectLocked();
    }

    private void DisconnectLocked()
    {
        try { _conn?.ConnectClose(); } catch { /* 关闭期异常吞掉 */ }
        _conn = null;
        _rw = null;
        IsConnected = false;
    }

    /// <summary>
    /// 断线节流重连：距上次重连不足 5s 直接返回 false（防读写失败风暴里
    /// 每次调用都阻塞一个完整连接超时，把轮询周期拖垮）。
    /// </summary>
    private bool EnsureConnected()
    {
        if (IsConnected && _conn != null && _rw != null)
            return true;
        if ((DateTime.Now - _lastReconnectAt).TotalSeconds < 5)
            return false;
        return Connect(IpAddress, Port);
    }

    private void MarkBroken()
    {
        // 朴素策略：协议报错也视为断线（代价一次重连，换逻辑简单）。
        IsConnected = false;
    }

    public (bool Ok, short Value) ReadInt16(string address)
    {
        lock (_lock)
        {
            if (!EnsureConnected() || _rw == null)
                return (false, 0);
            var r = _rw.ReadInt16(address);
            if (!r.IsSuccess) { MarkBroken(); return (false, 0); }
            return (true, r.Content);
        }
    }

    public (bool Ok, short[]? Values) ReadInt16(string address, ushort length)
    {
        lock (_lock)
        {
            if (!EnsureConnected() || _rw == null)
                return (false, null);
            var r = _rw.ReadInt16(address, length);
            if (!r.IsSuccess) { MarkBroken(); return (false, null); }
            return (true, r.Content);
        }
    }

    public (bool Ok, int[]? Values) ReadInt32(string address, ushort length)
    {
        lock (_lock)
        {
            if (!EnsureConnected() || _rw == null)
                return (false, null);
            var r = _rw.ReadInt32(address, length);
            if (!r.IsSuccess) { MarkBroken(); return (false, null); }
            return (true, r.Content);
        }
    }

    public (bool Ok, string? Value) ReadString(string address, ushort length)
    {
        lock (_lock)
        {
            if (!EnsureConnected() || _rw == null)
                return (false, null);
            var r = _rw.ReadString(address, length);
            if (!r.IsSuccess) { MarkBroken(); return (false, null); }
            return (true, r.Content);
        }
    }

    public bool WriteInt16(string address, short value)
    {
        lock (_lock)
        {
            if (!EnsureConnected() || _rw == null)
                return false;
            var r = _rw.Write(address, value);
            if (!r.IsSuccess) { MarkBroken(); return false; }
            return true;
        }
    }

    public bool WriteInt16(string address, short[] values)
    {
        lock (_lock)
        {
            if (!EnsureConnected() || _rw == null)
                return false;
            var r = _rw.Write(address, values);
            if (!r.IsSuccess) { MarkBroken(); return false; }
            return true;
        }
    }

    public bool WriteInt32(string address, int[] values)
    {
        lock (_lock)
        {
            if (!EnsureConnected() || _rw == null)
                return false;
            var r = _rw.Write(address, values);
            if (!r.IsSuccess) { MarkBroken(); return false; }
            return true;
        }
    }

    public bool WriteString(string address, string value, int length)
    {
        lock (_lock)
        {
            if (!EnsureConnected() || _rw == null)
                return false;
            var r = _rw.Write(address, value, length);
            if (!r.IsSuccess) { MarkBroken(); return false; }
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Disconnect();
    }
}
