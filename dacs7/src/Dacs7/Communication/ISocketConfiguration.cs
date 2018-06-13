﻿

namespace Dacs7.Communication
{
    public interface ISocketConfiguration
    {
        string Hostname { get; set; }
        int ServiceName { get; set; }
        int AutoconnectTime { get; set; }
        int ReceiveBufferSize { get; set; }
        string NetworkAdapter { get; set; }
        bool KeepAlive { get; set; }
    }
}