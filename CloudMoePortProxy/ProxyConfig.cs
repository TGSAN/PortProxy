using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace CloudMoePortProxy
{
    class ProxyConfig
    {
        public bool Verbose = false;
        public IPAddress ForwarderAddress;
        public int ForwarderPort;
        public IPAddress ListenAddress;
        public int ListenPort;
    }
}
