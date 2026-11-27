using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace CastMPV.DLNA
{
    public sealed class CastMediaRequest
    {
        public string Uri { get; set; }
        public string Title { get; set; }
        public string UserAgent { get; set; }
        public string Referer { get; set; }
    }

    public sealed class PlaybackSnapshot
    {
        public string TransportState { get; set; } = "STOPPED"; // STOPPED, PLAYING, PAUSED_PLAYBACK, TRANSITIONING
        public string CurrentUri { get; set; } = string.Empty;
        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public int Volume { get; set; } = 85;
        public bool Muted { get; set; }
    }

    /// <summary>
    /// 100% pure managed C# UPnP/DLNA MediaRenderer using System.Net.HttpListener
    /// (with automatic non-admin System.Net.Sockets.TcpListener fallback) and System.Net.Sockets.UdpClient SSDP.
    /// Zero third-party UPnP or OpenSSL dependencies.
    /// </summary>
    public sealed class DlnaServer : IDisposable
    {
        private const string SsdpMulticastIp = "239.255.255.250";
        private const int SsdpPort = 1900;

        private string _deviceName;
        private readonly string _deviceUuid;
        private readonly int _preferredPort;
        private int _activePort;
        private volatile bool _running;

        private HttpListener _httpListener;
        private TcpListener _socketFallbackListener;
        private Thread _httpAcceptThread;
        private UdpClient _ssdpListener;
        private Thread _ssdpReceiveThread;
        private Thread _ssdpAnnounceThread;
        private readonly ConcurrentDictionary<string, string> _subscriptions = new ConcurrentDictionary<string, string>();

        private string _currentUri = string.Empty;
        private string _currentMetadata = string.Empty;
        private string _nextUri = string.Empty;

        public event Action<CastMediaRequest> CastMediaRequested;
        public event Action PlayRequested;
        public event Action PauseRequested;
        public event Action StopRequested;
        public event Action<double> SeekRequested;
        public event Action<int> VolumeRequested;
        public event Action<bool> MuteRequested;
        public event Action<string> StatusChanged;

        public Func<PlaybackSnapshot> GetPlaybackSnapshot { get; set; }

        public bool IsRunning => _running;
        public int ActivePort => _activePort;
        public string DeviceName => _deviceName;

        public DlnaServer(string deviceName, string deviceUuid, int port = 8200)
        {
            _deviceName = string.IsNullOrWhiteSpace(deviceName) ? "CastMPV Receiver" : deviceName.Trim();
            _deviceUuid = string.IsNullOrWhiteSpace(deviceUuid) ? Guid.NewGuid().ToString("D") : deviceUuid.Trim();
            _preferredPort = port;
        }

        public void UpdateDeviceName(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            _deviceName = newName.Trim();
            if (_running)
            {
                SendSsdpNotify("ssdp:byebye");
                SendSsdpNotify("ssdp:alive");
            }
        }

        public string GetPrimaryLanIp()
        {
            return GetLocalIPv4Addresses().FirstOrDefault()?.ToString() ?? "127.0.0.1";
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            StartHttpRendererListener();
            StartSsdpResponder();

            StatusChanged?.Invoke($"Running ({_deviceName} @ {GetPrimaryLanIp()}:{_activePort})");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            try { SendSsdpNotify("ssdp:byebye"); } catch { }

            try { _httpListener?.Stop(); _httpListener?.Close(); } catch { }
            try { _socketFallbackListener?.Stop(); } catch { }
            try { _ssdpListener?.Close(); } catch { }

            StatusChanged?.Invoke("Stopped");
        }

        private void StartHttpRendererListener()
        {
            // 1. Try native System.Net.HttpListener first on the configured port (+ fallback ports)
            if (HttpListener.IsSupported)
            {
                for (int p = _preferredPort; p <= _preferredPort + 10; p++)
                {
                    var hl = new HttpListener();
                    try
                    {
                        hl.Prefixes.Add($"http://+:{p}/");
                        hl.Start();
                        _httpListener = hl;
                        _activePort = p;

                        _httpAcceptThread = new Thread(HttpListenerAcceptLoop)
                        {
                            IsBackground = true,
                            Name = "CastMPV-HttpListener"
                        };
                        _httpAcceptThread.Start();
                        return;
                    }
                    catch
                    {
                        try { hl.Close(); } catch { }
                    }
                }
            }

            // 2. Non-admin Windows fallback: pure managed System.Net.Sockets.TcpListener
            // (HttpListener wildcard prefixes require netsh urlacl/admin; TcpListener works for any standard user on LAN)
            Exception lastEx = null;
            for (int p = _preferredPort; p <= _preferredPort + 15; p++)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Any, p);
                    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    listener.Start();
                    _socketFallbackListener = listener;
                    _activePort = p;
                    break;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (_socketFallbackListener == null)
                throw new InvalidOperationException("Failed to bind DLNA HTTP listener port.", lastEx);

            _httpAcceptThread = new Thread(SocketAcceptLoop)
            {
                IsBackground = true,
                Name = "CastMPV-DlnaSocketHttp"
            };
            _httpAcceptThread.Start();
        }

        private void HttpListenerAcceptLoop()
        {
            while (_running && _httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    HttpListenerContext ctx = _httpListener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleHttpListenerContext(ctx));
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void HandleHttpListenerContext(HttpListenerContext ctx)
        {
            try
            {
                HttpListenerRequest req = ctx.Request;
                HttpListenerResponse resp = ctx.Response;

                string method = req.HttpMethod?.ToUpperInvariant() ?? "GET";
                string path = req.Url?.AbsolutePath ?? "/";

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in req.Headers.AllKeys)
                {
                    if (key != null) headers[key] = req.Headers[key];
                }

                string body = string.Empty;
                if (req.HasEntityBody)
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                    body = reader.ReadToEnd();
                }

                string hostHeader = !string.IsNullOrWhiteSpace(req.UserHostName)
                    ? req.UserHostName
                    : $"{GetLocalIpForRemote(req.RemoteEndPoint)}:{_activePort}";

                DlnaHttpResponse result = RouteRequest(method, path, headers, body, hostHeader);

                resp.StatusCode = result.StatusCode;
                resp.StatusDescription = result.StatusReason;
                resp.ContentType = result.ContentType;
                if (result.ExtraHeaders != null)
                {
                    foreach (var kv in result.ExtraHeaders)
                        resp.Headers[kv.Key] = kv.Value;
                }

                byte[] payload = Encoding.UTF8.GetBytes(result.Body ?? string.Empty);
                resp.ContentLength64 = payload.Length;
                if (method != "HEAD" && payload.Length > 0)
                {
                    resp.OutputStream.Write(payload, 0, payload.Length);
                }
                resp.OutputStream.Close();
            }
            catch
            {
                // Ignore client disconnect
            }
        }

        private void SocketAcceptLoop()
        {
            while (_running && _socketFallbackListener != null)
            {
                try
                {
                    TcpClient client = _socketFallbackListener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleSocketHttpClient(client));
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void HandleSocketHttpClient(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                try
                {
                    using NetworkStream stream = client.GetStream();
                    if (!TryReadSocketHttpRequest(stream, out string method, out string path, out Dictionary<string, string> headers, out string body))
                        return;

                    string hostHeader = headers.TryGetValue("host", out string h) && !string.IsNullOrWhiteSpace(h)
                        ? h
                        : $"{GetLocalIpForRemote(client.Client.RemoteEndPoint)}:{_activePort}";

                    DlnaHttpResponse result = RouteRequest(method, path, headers, body, hostHeader);
                    SendSocketHttpResponse(stream, method, result);
                }
                catch
                {
                    // Ignore transient client disconnects
                }
            }
        }

        private static bool TryReadSocketHttpRequest(
            NetworkStream stream,
            out string method,
            out string path,
            out Dictionary<string, string> headers,
            out string body)
        {
            method = null;
            path = null;
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            body = string.Empty;

            var headerBytes = new MemoryStream();
            byte[] single = new byte[1];
            int matchState = 0;

            while (headerBytes.Length < 65536)
            {
                int read = stream.Read(single, 0, 1);
                if (read <= 0) return false;
                byte b = single[0];
                headerBytes.WriteByte(b);

                if (b == '\r' && (matchState == 0 || matchState == 2)) matchState++;
                else if (b == '\n' && (matchState == 1 || matchState == 3)) matchState++;
                else matchState = 0;

                if (matchState == 4) break;
            }

            string headerText = Encoding.UTF8.GetString(headerBytes.ToArray());
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return false;

            string[] requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2) return false;

            method = requestLine[0].Trim().ToUpperInvariant();
            path = requestLine[1].Trim();
            int qIdx = path.IndexOf('?');
            if (qIdx >= 0) path = path.Substring(0, qIdx);

            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    string name = lines[i].Substring(0, colon).Trim();
                    string val = lines[i].Substring(colon + 1).Trim();
                    headers[name] = val;
                }
            }

            if (headers.TryGetValue("content-length", out string clStr) && int.TryParse(clStr, out int contentLength) && contentLength > 0)
            {
                if (contentLength > 2 * 1024 * 1024) return false;
                byte[] buf = new byte[contentLength];
                int offset = 0;
                while (offset < contentLength)
                {
                    int n = stream.Read(buf, offset, contentLength - offset);
                    if (n <= 0) break;
                    offset += n;
                }
                body = Encoding.UTF8.GetString(buf, 0, offset);
            }

            return true;
        }

        private static void SendSocketHttpResponse(NetworkStream stream, string method, DlnaHttpResponse result)
        {
            byte[] payload = Encoding.UTF8.GetBytes(result.Body ?? string.Empty);
            var sb = new StringBuilder();
            sb.Append($"HTTP/1.1 {result.StatusCode} {result.StatusReason}\r\n");
            sb.Append($"Content-Type: {result.ContentType}\r\n");
            sb.Append($"Content-Length: {payload.Length}\r\n");
            sb.Append("Server: Windows/10.0 UPnP/1.1 CastMPV/1.0\r\n");
            sb.Append("Connection: close\r\n");
            if (result.ExtraHeaders != null)
            {
                foreach (var kv in result.ExtraHeaders)
                    sb.Append($"{kv.Key}: {kv.Value}\r\n");
            }
            sb.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (method != "HEAD" && payload.Length > 0)
                stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private DlnaHttpResponse RouteRequest(
            string method,
            string path,
            Dictionary<string, string> headers,
            string body,
            string hostHeader)
        {
            if (method == "GET" || method == "HEAD")
            {
                string xml = null;
                switch (path)
                {
                    case "/":
                    case "/description.xml":
                        xml = BuildDeviceDescriptionXml(hostHeader);
                        break;
                    case "/AVTransport/scpd.xml":
                        xml = BuildAvTransportScpdXml();
                        break;
                    case "/RenderingControl/scpd.xml":
                        xml = BuildRenderingControlScpdXml();
                        break;
                    case "/ConnectionManager/scpd.xml":
                        xml = BuildConnectionManagerScpdXml();
                        break;
                }

                if (xml != null)
                    return new DlnaHttpResponse(200, "OK", "text/xml; charset=\"utf-8\"", xml);

                return new DlnaHttpResponse(404, "Not Found", "text/plain", "Not Found");
            }

            if (method == "POST")
            {
                string soapAction = headers.TryGetValue("soapaction", out string sa) ? sa.Trim('"', ' ') : string.Empty;
                string actionName = soapAction.Contains("#") ? soapAction.Substring(soapAction.IndexOf('#') + 1) : string.Empty;

                XDocument doc;
                try
                {
                    doc = XDocument.Parse(body);
                }
                catch
                {
                    return SoapFaultResponse(402, "Invalid XML");
                }

                if (string.IsNullOrEmpty(actionName))
                {
                    XElement bodyEl = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Body");
                    actionName = bodyEl?.Elements().FirstOrDefault()?.Name.LocalName ?? string.Empty;
                }

                string responseXml = null;
                if (path.Equals("/AVTransport/control", StringComparison.OrdinalIgnoreCase))
                    responseXml = ProcessAvTransportAction(actionName, doc);
                else if (path.Equals("/RenderingControl/control", StringComparison.OrdinalIgnoreCase))
                    responseXml = ProcessRenderingControlAction(actionName, doc);
                else if (path.Equals("/ConnectionManager/control", StringComparison.OrdinalIgnoreCase))
                    responseXml = ProcessConnectionManagerAction(actionName);

                if (responseXml != null)
                    return new DlnaHttpResponse(200, "OK", "text/xml; charset=\"utf-8\"", responseXml);

                return SoapFaultResponse(401, "Invalid Action");
            }

            if (method == "SUBSCRIBE")
            {
                string sid = headers.TryGetValue("sid", out string existingSid) && !string.IsNullOrWhiteSpace(existingSid)
                    ? existingSid
                    : $"uuid:{Guid.NewGuid():D}";

                if (headers.TryGetValue("callback", out string cb))
                    _subscriptions[sid] = cb.Trim('<', '>');

                var extraHeaders = new Dictionary<string, string>
                {
                    ["SID"] = sid,
                    ["TIMEOUT"] = "Second-1800"
                };
                return new DlnaHttpResponse(200, "OK", "text/plain", string.Empty, extraHeaders);
            }

            if (method == "UNSUBSCRIBE")
            {
                return new DlnaHttpResponse(200, "OK", "text/plain", string.Empty);
            }

            return new DlnaHttpResponse(405, "Method Not Allowed", "text/plain", "Method Not Allowed");
        }

        private string ProcessAvTransportAction(string action, XDocument doc)
        {
            const string ns = "urn:schemas-upnp-org:service:AVTransport:1";
            PlaybackSnapshot snap = GetPlaybackSnapshot?.Invoke() ?? new PlaybackSnapshot();

            switch (action)
            {
                case "SetAVTransportURI":
                {
                    string rawUri = GetXmlElementValue(doc, "CurrentURI");
                    string metadata = GetXmlElementValue(doc, "CurrentURIMetaData");
                    _currentUri = rawUri ?? string.Empty;
                    _currentMetadata = metadata ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(_currentUri))
                    {
                        CastMediaRequest req = ParseCastRequest(_currentUri, _currentMetadata);
                        CastMediaRequested?.Invoke(req);
                    }
                    return WrapSoapResponse(ns, action, string.Empty);
                }

                case "SetNextAVTransportURI":
                {
                    _nextUri = GetXmlElementValue(doc, "NextURI") ?? string.Empty;
                    return WrapSoapResponse(ns, action, string.Empty);
                }

                case "Play":
                {
                    if (!string.IsNullOrWhiteSpace(_currentUri) && snap.TransportState == "STOPPED")
                        CastMediaRequested?.Invoke(ParseCastRequest(_currentUri, _currentMetadata));
                    else
                        PlayRequested?.Invoke();
                    return WrapSoapResponse(ns, action, string.Empty);
                }

                case "Pause":
                    PauseRequested?.Invoke();
                    return WrapSoapResponse(ns, action, string.Empty);

                case "Stop":
                    StopRequested?.Invoke();
                    return WrapSoapResponse(ns, action, string.Empty);

                case "Seek":
                {
                    string target = GetXmlElementValue(doc, "Target");
                    if (TryParseUpnpTime(target, out double sec))
                        SeekRequested?.Invoke(sec);
                    return WrapSoapResponse(ns, action, string.Empty);
                }

                case "GetTransportInfo":
                {
                    string inner =
                        $"<CurrentTransportState>{EscapeXml(snap.TransportState)}</CurrentTransportState>" +
                        "<CurrentTransportStatus>OK</CurrentTransportStatus>" +
                        "<CurrentSpeed>1</CurrentSpeed>";
                    return WrapSoapResponse(ns, action, inner);
                }

                case "GetPositionInfo":
                {
                    string dur = FormatUpnpTime(snap.DurationSeconds);
                    string pos = FormatUpnpTime(snap.PositionSeconds);
                    string uri = string.IsNullOrWhiteSpace(snap.CurrentUri) ? _currentUri : snap.CurrentUri;
                    string inner =
                        "<Track>1</Track>" +
                        $"<TrackDuration>{dur}</TrackDuration>" +
                        $"<TrackMetaData>{EscapeXml(_currentMetadata)}</TrackMetaData>" +
                        $"<TrackURI>{EscapeXml(uri)}</TrackURI>" +
                        $"<RelTime>{pos}</RelTime>" +
                        $"<AbsTime>{pos}</AbsTime>" +
                        "<RelCount>2147483647</RelCount>" +
                        "<AbsCount>2147483647</AbsCount>";
                    return WrapSoapResponse(ns, action, inner);
                }

                case "GetMediaInfo":
                {
                    string dur = FormatUpnpTime(snap.DurationSeconds);
                    string uri = string.IsNullOrWhiteSpace(snap.CurrentUri) ? _currentUri : snap.CurrentUri;
                    string inner =
                        "<NrTracks>1</NrTracks>" +
                        $"<MediaDuration>{dur}</MediaDuration>" +
                        $"<CurrentURI>{EscapeXml(uri)}</CurrentURI>" +
                        $"<CurrentURIMetaData>{EscapeXml(_currentMetadata)}</CurrentURIMetaData>" +
                        $"<NextURI>{EscapeXml(_nextUri)}</NextURI>" +
                        "<NextURIMetaData></NextURIMetaData>" +
                        "<PlayMedium>NETWORK</PlayMedium>" +
                        "<RecordMedium>NOT_IMPLEMENTED</RecordMedium>" +
                        "<WriteStatus>NOT_IMPLEMENTED</WriteStatus>";
                    return WrapSoapResponse(ns, action, inner);
                }

                case "GetDeviceCapabilities":
                    return WrapSoapResponse(ns, action, "<PlayMedia>NETWORK</PlayMedia><RecMedia>NOT_IMPLEMENTED</RecMedia><RecQualityModes>NOT_IMPLEMENTED</RecQualityModes>");

                case "GetTransportSettings":
                    return WrapSoapResponse(ns, action, "<PlayMode>NORMAL</PlayMode><RecQualityMode>NOT_IMPLEMENTED</RecQualityMode>");

                case "GetCurrentTransportActions":
                    return WrapSoapResponse(ns, action, "<Actions>Play,Pause,Stop,Seek</Actions>");

                default:
                    return null;
            }
        }

        private string ProcessRenderingControlAction(string action, XDocument doc)
        {
            const string ns = "urn:schemas-upnp-org:service:RenderingControl:1";
            PlaybackSnapshot snap = GetPlaybackSnapshot?.Invoke() ?? new PlaybackSnapshot();

            switch (action)
            {
                case "GetVolume":
                    return WrapSoapResponse(ns, action, $"<CurrentVolume>{snap.Volume}</CurrentVolume>");

                case "SetVolume":
                    if (int.TryParse(GetXmlElementValue(doc, "DesiredVolume"), out int vol))
                        VolumeRequested?.Invoke(Math.Max(0, Math.Min(100, vol)));
                    return WrapSoapResponse(ns, action, string.Empty);

                case "GetMute":
                    return WrapSoapResponse(ns, action, $"<CurrentMute>{(snap.Muted ? "1" : "0")}</CurrentMute>");

                case "SetMute":
                    string m = GetXmlElementValue(doc, "DesiredMute");
                    bool mute = m == "1" || string.Equals(m, "true", StringComparison.OrdinalIgnoreCase);
                    MuteRequested?.Invoke(mute);
                    return WrapSoapResponse(ns, action, string.Empty);

                default:
                    return null;
            }
        }

        private static string ProcessConnectionManagerAction(string action)
        {
            const string ns = "urn:schemas-upnp-org:service:ConnectionManager:1";

            switch (action)
            {
                case "GetProtocolInfo":
                {
                    const string sink =
                        "http-get:*:video/mp4:*," +
                        "http-get:*:video/x-matroska:*," +
                        "http-get:*:video/webm:*," +
                        "http-get:*:video/x-msvideo:*," +
                        "http-get:*:video/mp2t:*," +
                        "http-get:*:application/x-mpegURL:*," +
                        "http-get:*:application/vnd.apple.mpegurl:*," +
                        "http-get:*:application/dash+xml:*," +
                        "http-get:*:audio/mpeg:*," +
                        "http-get:*:audio/flac:*," +
                        "http-get:*:audio/mp4:*," +
                        "http-get:*:*:*";
                    return WrapSoapResponse(ns, action, $"<Source></Source><Sink>{EscapeXml(sink)}</Sink>");
                }

                case "GetCurrentConnectionIDs":
                    return WrapSoapResponse(ns, action, "<ConnectionIDs>0</ConnectionIDs>");

                case "GetCurrentConnectionInfo":
                    return WrapSoapResponse(ns, action,
                        "<RcsID>0</RcsID><AVTransportID>0</AVTransportID>" +
                        "<ProtocolInfo></ProtocolInfo><PeerConnectionManager></PeerConnectionManager>" +
                        "<PeerConnectionID>-1</PeerConnectionID><Direction>Input</Direction><Status>OK</Status>");

                default:
                    return null;
            }
        }

        private static CastMediaRequest ParseCastRequest(string rawUri, string metadata)
        {
            string uri = WebUtility.HtmlDecode(rawUri).Trim();
            string userAgent = null;
            string referer = null;

            int pipeIdx = uri.IndexOf('|');
            if (pipeIdx > 0)
            {
                string paramPart = uri.Substring(pipeIdx + 1);
                uri = uri.Substring(0, pipeIdx);
                foreach (string pair in paramPart.Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = pair.Substring(0, eq).Trim();
                    string val = Uri.UnescapeDataString(pair.Substring(eq + 1).Trim());
                    if (key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) userAgent = val;
                    else if (key.Equals("Referer", StringComparison.OrdinalIgnoreCase)) referer = val;
                }
            }

            string title = null;
            if (!string.IsNullOrWhiteSpace(metadata))
            {
                try
                {
                    string decodedMeta = metadata.TrimStart().StartsWith("&lt;") ? WebUtility.HtmlDecode(metadata) : metadata;
                    var didl = XDocument.Parse(decodedMeta);
                    title = didl.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value;
                }
                catch { }
            }

            return new CastMediaRequest
            {
                Uri = uri,
                Title = title,
                UserAgent = userAgent,
                Referer = referer
            };
        }

        private void StartSsdpResponder()
        {
            _ssdpListener = new UdpClient(AddressFamily.InterNetwork);
            _ssdpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _ssdpListener.Client.Bind(new IPEndPoint(IPAddress.Any, SsdpPort));

            IPAddress multicastAddr = IPAddress.Parse(SsdpMulticastIp);
            foreach (IPAddress localIp in GetLocalIPv4Addresses())
            {
                try
                {
                    _ssdpListener.JoinMulticastGroup(multicastAddr, localIp);
                }
                catch { }
            }

            _ssdpReceiveThread = new Thread(SsdpReceiveLoop)
            {
                IsBackground = true,
                Name = "CastMPV-SsdpRx"
            };
            _ssdpReceiveThread.Start();

            _ssdpAnnounceThread = new Thread(SsdpAnnounceLoop)
            {
                IsBackground = true,
                Name = "CastMPV-SsdpAnnounce"
            };
            _ssdpAnnounceThread.Start();
        }

        private void SsdpReceiveLoop()
        {
            var remoteEp = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _ssdpListener.Receive(ref remoteEp);
                    string msg = Encoding.UTF8.GetString(data);
                    if (msg.StartsWith("M-SEARCH", StringComparison.OrdinalIgnoreCase))
                        HandleMSearch(msg, remoteEp);
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void HandleMSearch(string message, IPEndPoint remoteEp)
        {
            string st = null;
            foreach (string line in message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("ST:", StringComparison.OrdinalIgnoreCase))
                {
                    st = line.Substring(3).Trim();
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(st)) return;

            string[] supportedTargets =
            {
                "ssdp:all",
                "upnp:rootdevice",
                $"uuid:{_deviceUuid}",
                "urn:schemas-upnp-org:device:MediaRenderer:1",
                "urn:schemas-upnp-org:service:AVTransport:1",
                "urn:schemas-upnp-org:service:RenderingControl:1",
                "urn:schemas-upnp-org:service:ConnectionManager:1"
            };

            string localIp = GetBestLocalIpForTarget(remoteEp.Address);
            string location = $"http://{localIp}:{_activePort}/description.xml";

            if (st.Equals("ssdp:all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string target in supportedTargets.Skip(1))
                    SendMSearchResponse(remoteEp, target, location);
            }
            else if (supportedTargets.Any(t => t.Equals(st, StringComparison.OrdinalIgnoreCase)))
            {
                SendMSearchResponse(remoteEp, st, location);
            }
        }

        private void SendMSearchResponse(IPEndPoint remoteEp, string st, string location)
        {
            string usn = st.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase)
                ? $"uuid:{_deviceUuid}"
                : $"uuid:{_deviceUuid}::{st}";

            string resp =
                "HTTP/1.1 200 OK\r\n" +
                "CACHE-CONTROL: max-age=1800\r\n" +
                $"DATE: {DateTime.UtcNow:R}\r\n" +
                "EXT:\r\n" +
                $"LOCATION: {location}\r\n" +
                "SERVER: Windows/10.0 UPnP/1.1 CastMPV/1.0\r\n" +
                $"ST: {st}\r\n" +
                $"USN: {usn}\r\n" +
                "BOOTID.UPNP.ORG: 1\r\n" +
                "CONFIGID.UPNP.ORG: 1\r\n\r\n";

            byte[] bytes = Encoding.UTF8.GetBytes(resp);
            try { _ssdpListener?.Send(bytes, bytes.Length, remoteEp); } catch { }
        }

        private void SsdpAnnounceLoop()
        {
            SendSsdpNotify("ssdp:alive");
            int elapsedMs = 0;
            while (_running)
            {
                Thread.Sleep(500);
                elapsedMs += 500;
                if (elapsedMs >= 45000)
                {
                    elapsedMs = 0;
                    SendSsdpNotify("ssdp:alive");
                }
            }
        }

        private void SendSsdpNotify(string nts)
        {
            string[] notificationTypes =
            {
                "upnp:rootdevice",
                $"uuid:{_deviceUuid}",
                "urn:schemas-upnp-org:device:MediaRenderer:1",
                "urn:schemas-upnp-org:service:AVTransport:1",
                "urn:schemas-upnp-org:service:RenderingControl:1",
                "urn:schemas-upnp-org:service:ConnectionManager:1"
            };

            var multicastEp = new IPEndPoint(IPAddress.Parse(SsdpMulticastIp), SsdpPort);
            foreach (IPAddress localIp in GetLocalIPv4Addresses())
            {
                try
                {
                    string location = $"http://{localIp}:{_activePort}/description.xml";
                    using var sender = new UdpClient(new IPEndPoint(localIp, 0));
                    foreach (string nt in notificationTypes)
                    {
                        string usn = nt.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase)
                            ? $"uuid:{_deviceUuid}"
                            : $"uuid:{_deviceUuid}::{nt}";

                        string notify =
                            "NOTIFY * HTTP/1.1\r\n" +
                            $"HOST: {SsdpMulticastIp}:{SsdpPort}\r\n" +
                            "CACHE-CONTROL: max-age=1800\r\n" +
                            $"LOCATION: {location}\r\n" +
                            $"NT: {nt}\r\n" +
                            $"NTS: {nts}\r\n" +
                            "SERVER: Windows/10.0 UPnP/1.1 CastMPV/1.0\r\n" +
                            $"USN: {usn}\r\n\r\n";

                        byte[] bytes = Encoding.UTF8.GetBytes(notify);
                        sender.Send(bytes, bytes.Length, multicastEp);
                    }
                }
                catch { }
            }
        }

        private string BuildDeviceDescriptionXml(string hostHeader)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<root xmlns=""urn:schemas-upnp-org:device-1-0"" xmlns:dlna=""urn:schemas-dlna-org:device-1-0"">
  <specVersion>
    <major>1</major>
    <minor>0</minor>
  </specVersion>
  <URLBase>http://{EscapeXml(hostHeader)}/</URLBase>
  <device>
    <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
    <friendlyName>{EscapeXml(_deviceName)}</friendlyName>
    <manufacturer>CastMPV</manufacturer>
    <manufacturerURL>https://github.com/</manufacturerURL>
    <modelDescription>CastMPV Lightweight libmpv DLNA Media Renderer</modelDescription>
    <modelName>CastMPV Renderer</modelName>
    <modelNumber>1.0</modelNumber>
    <UDN>uuid:{EscapeXml(_deviceUuid)}</UDN>
    <dlna:X_DLNADOC xmlns:dlna=""urn:schemas-dlna-org:device-1-0"">DMR-1.50</dlna:X_DLNADOC>
    <serviceList>
      <service>
        <serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType>
        <serviceId>urn:upnp-org:serviceId:AVTransport</serviceId>
        <SCPDURL>/AVTransport/scpd.xml</SCPDURL>
        <controlURL>/AVTransport/control</controlURL>
        <eventSubURL>/AVTransport/event</eventSubURL>
      </service>
      <service>
        <serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType>
        <serviceId>urn:upnp-org:serviceId:RenderingControl</serviceId>
        <SCPDURL>/RenderingControl/scpd.xml</SCPDURL>
        <controlURL>/RenderingControl/control</controlURL>
        <eventSubURL>/RenderingControl/event</eventSubURL>
      </service>
      <service>
        <serviceType>urn:schemas-upnp-org:service:ConnectionManager:1</serviceType>
        <serviceId>urn:upnp-org:serviceId:ConnectionManager</serviceId>
        <SCPDURL>/ConnectionManager/scpd.xml</SCPDURL>
        <controlURL>/ConnectionManager/control</controlURL>
        <eventSubURL>/ConnectionManager/event</eventSubURL>
      </service>
    </serviceList>
  </device>
</root>";
        }

        private static string BuildAvTransportScpdXml() => @"<?xml version=""1.0"" encoding=""utf-8""?>
<scpd xmlns=""urn:schemas-upnp-org:service-1-0"">
  <specVersion><major>1</major><minor>0</minor></specVersion>
  <actionList>
    <action><name>SetAVTransportURI</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
      <argument><name>CurrentURI</name><direction>in</direction><relatedStateVariable>AVTransportURI</relatedStateVariable></argument>
      <argument><name>CurrentURIMetaData</name><direction>in</direction><relatedStateVariable>AVTransportURIMetaData</relatedStateVariable></argument>
    </argumentList></action>
    <action><name>Play</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
      <argument><name>Speed</name><direction>in</direction><relatedStateVariable>TransportPlaySpeed</relatedStateVariable></argument>
    </argumentList></action>
    <action><name>Pause</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
    </argumentList></action>
    <action><name>Stop</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
    </argumentList></action>
    <action><name>Seek</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
      <argument><name>Unit</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_SeekMode</relatedStateVariable></argument>
      <argument><name>Target</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_SeekTarget</relatedStateVariable></argument>
    </argumentList></action>
    <action><name>GetTransportInfo</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
      <argument><name>CurrentTransportState</name><direction>out</direction><relatedStateVariable>TransportState</relatedStateVariable></argument>
      <argument><name>CurrentTransportStatus</name><direction>out</direction><relatedStateVariable>TransportStatus</relatedStateVariable></argument>
      <argument><name>CurrentSpeed</name><direction>out</direction><relatedStateVariable>TransportPlaySpeed</relatedStateVariable></argument>
    </argumentList></action>
    <action><name>GetPositionInfo</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
      <argument><name>Track</name><direction>out</direction><relatedStateVariable>CurrentTrack</relatedStateVariable></argument>
      <argument><name>TrackDuration</name><direction>out</direction><relatedStateVariable>CurrentTrackDuration</relatedStateVariable></argument>
      <argument><name>TrackMetaData</name><direction>out</direction><relatedStateVariable>CurrentTrackMetaData</relatedStateVariable></argument>
      <argument><name>TrackURI</name><direction>out</direction><relatedStateVariable>CurrentTrackURI</relatedStateVariable></argument>
      <argument><name>RelTime</name><direction>out</direction><relatedStateVariable>RelativeTimePosition</relatedStateVariable></argument>
      <argument><name>AbsTime</name><direction>out</direction><relatedStateVariable>AbsoluteTimePosition</relatedStateVariable></argument>
      <argument><name>RelCount</name><direction>out</direction><relatedStateVariable>RelativeCounterPosition</relatedStateVariable></argument>
      <argument><name>AbsCount</name><direction>out</direction><relatedStateVariable>AbsoluteCounterPosition</relatedStateVariable></argument>
    </argumentList></action>
  </actionList>
  <serviceStateTable>
    <stateVariable sendEvents=""no""><name>A_ARG_TYPE_InstanceID</name><dataType>ui4</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>AVTransportURI</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>AVTransportURIMetaData</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>TransportState</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>TransportStatus</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>TransportPlaySpeed</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>A_ARG_TYPE_SeekMode</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>A_ARG_TYPE_SeekTarget</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>CurrentTrack</name><dataType>ui4</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>CurrentTrackDuration</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>CurrentTrackMetaData</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>CurrentTrackURI</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>RelativeTimePosition</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>AbsoluteTimePosition</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>RelativeCounterPosition</name><dataType>i4</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>AbsoluteCounterPosition</name><dataType>i4</dataType></stateVariable>
  </serviceStateTable>
</scpd>";

        private static string BuildRenderingControlScpdXml() => @"<?xml version=""1.0"" encoding=""utf-8""?>
<scpd xmlns=""urn:schemas-upnp-org:service-1-0"">
  <specVersion><major>1</major><minor>0</minor></specVersion>
  <actionList>
    <action><name>GetVolume</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
      <argument><name>Channel</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Channel</relatedStateVariable></argument>
      <argument><name>CurrentVolume</name><direction>out</direction><relatedStateVariable>Volume</relatedStateVariable></argument>
    </argumentList></action>
    <action><name>SetVolume</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
      <argument><name>Channel</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Channel</relatedStateVariable></argument>
      <argument><name>DesiredVolume</name><direction>in</direction><relatedStateVariable>Volume</relatedStateVariable></argument>
    </argumentList></action>
    <action><name>GetMute</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
      <argument><name>Channel</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Channel</relatedStateVariable></argument>
      <argument><name>CurrentMute</name><direction>out</direction><relatedStateVariable>Mute</relatedStateVariable></argument>
    </argumentList></action>
    <action><name>SetMute</name><argumentList>
      <argument><name>InstanceID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_InstanceID</relatedStateVariable></argument>
      <argument><name>Channel</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Channel</relatedStateVariable></argument>
      <argument><name>DesiredMute</name><direction>in</direction><relatedStateVariable>Mute</relatedStateVariable></argument>
    </argumentList></action>
  </actionList>
  <serviceStateTable>
    <stateVariable sendEvents=""no""><name>A_ARG_TYPE_InstanceID</name><dataType>ui4</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>A_ARG_TYPE_Channel</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>Volume</name><dataType>ui2</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>Mute</name><dataType>boolean</dataType></stateVariable>
  </serviceStateTable>
</scpd>";

        private static string BuildConnectionManagerScpdXml() => @"<?xml version=""1.0"" encoding=""utf-8""?>
<scpd xmlns=""urn:schemas-upnp-org:service-1-0"">
  <specVersion><major>1</major><minor>0</minor></specVersion>
  <actionList>
    <action><name>GetProtocolInfo</name><argumentList>
      <argument><name>Source</name><direction>out</direction><relatedStateVariable>SourceProtocolInfo</relatedStateVariable></argument>
      <argument><name>Sink</name><direction>out</direction><relatedStateVariable>SinkProtocolInfo</relatedStateVariable></argument>
    </argumentList></action>
  </actionList>
  <serviceStateTable>
    <stateVariable sendEvents=""no""><name>SourceProtocolInfo</name><dataType>string</dataType></stateVariable>
    <stateVariable sendEvents=""no""><name>SinkProtocolInfo</name><dataType>string</dataType></stateVariable>
  </serviceStateTable>
</scpd>";

        private static string WrapSoapResponse(string serviceNamespace, string actionName, string innerXml)
        {
            return
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body>" +
                $"<u:{actionName}Response xmlns:u=\"{serviceNamespace}\">{innerXml}</u:{actionName}Response>" +
                "</s:Body></s:Envelope>";
        }

        private static DlnaHttpResponse SoapFaultResponse(int code, string desc)
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                "<s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring>" +
                $"<detail><UPnPError xmlns=\"urn:schemas-upnp-org:control-1-0\"><errorCode>{code}</errorCode><errorDescription>{EscapeXml(desc)}</errorDescription></UPnPError></detail>" +
                "</s:Fault></s:Body></s:Envelope>";
            return new DlnaHttpResponse(500, "Internal Server Error", "text/xml; charset=\"utf-8\"", xml);
        }

        private static string GetXmlElementValue(XDocument doc, string localName)
        {
            return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
        }

        private static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string FormatUpnpTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                return "00:00:00";
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        }

        private static bool TryParseUpnpTime(string timeStr, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(timeStr)) return false;
            string[] parts = timeStr.Trim().Split(':');
            if (parts.Length == 3 &&
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double h) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double m) &&
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double s))
            {
                seconds = h * 3600 + m * 60 + s;
                return true;
            }
            return double.TryParse(timeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out seconds);
        }

        private static List<IPAddress> GetLocalIPv4Addresses()
        {
            var result = new List<IPAddress>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip.Address))
                        result.Add(ip.Address);
                }
            }
            return result;
        }

        private string GetLocalIpForRemote(EndPoint remoteEndPoint)
        {
            if (remoteEndPoint is IPEndPoint ep)
                return GetBestLocalIpForTarget(ep.Address);
            return GetPrimaryLanIp();
        }

        private string GetBestLocalIpForTarget(IPAddress targetIp)
        {
            byte[] targetBytes = targetIp.GetAddressBytes();
            foreach (IPAddress local in GetLocalIPv4Addresses())
            {
                byte[] b = local.GetAddressBytes();
                if (b[0] == targetBytes[0] && b[1] == targetBytes[1] && b[2] == targetBytes[2])
                    return local.ToString();
            }
            return GetPrimaryLanIp();
        }

        public void Dispose()
        {
            Stop();
        }

        private sealed class DlnaHttpResponse
        {
            public int StatusCode { get; }
            public string StatusReason { get; }
            public string ContentType { get; }
            public string Body { get; }
            public Dictionary<string, string> ExtraHeaders { get; }

            public DlnaHttpResponse(
                int statusCode,
                string statusReason,
                string contentType,
                string body,
                Dictionary<string, string> extraHeaders = null)
            {
                StatusCode = statusCode;
                StatusReason = statusReason;
                ContentType = contentType;
                Body = body;
                ExtraHeaders = extraHeaders;
            }
        }
    }
}
