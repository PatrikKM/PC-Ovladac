using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PCOvladac
{
    public class CommandLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string SenderIp { get; set; }
        public string Command { get; set; }
        public bool Success { get; set; }
        public string ResultMessage { get; set; }
    }

    public class HttpServer : IDisposable
    {
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private int _port;

        private readonly Func<string, string, ActionExecutionResult> _commandDispatcher;
        private readonly Func<List<CommandMapping>> _commandsProvider;

        public bool IsRunning { get { return _running; } }
        public int Port { get { return _port; } }
        public string LastError { get; private set; }

        public HttpServer(
            Func<string, string, ActionExecutionResult> commandDispatcher,
            Func<List<CommandMapping>> commandsProvider)
        {
            _commandDispatcher = commandDispatcher;
            _commandsProvider = commandsProvider;
        }

        public bool Start(int port, out string errorMessage)
        {
            Stop();
            _port = port;
            errorMessage = null;
            LastError = null;

            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _running = true;

                _acceptThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "HttpServer-Accept"
                };
                _acceptThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                _running = false;
                LastError = ex.Message;
                errorMessage = ex.Message;
                return false;
            }
        }

        public void Stop()
        {
            _running = false;
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                _listener = null;
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;

                    string remoteIp = "neznámá";
                    var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
                    if (remoteEp != null)
                    {
                        remoteIp = remoteEp.Address.ToString();
                    }

                    using (NetworkStream stream = client.GetStream())
                    {
                        string method;
                        string rawPath;
                        string body;
                        if (!ReadHttpRequest(stream, out method, out rawPath, out body))
                        {
                            return;
                        }

                        if (method == "OPTIONS")
                        {
                            SendResponse(stream, 204, "No Content", "text/plain; charset=utf-8", "");
                            return;
                        }

                        string pathOnly = rawPath;
                        int qIdx = pathOnly.IndexOf('?');
                        if (qIdx >= 0) pathOnly = pathOnly.Substring(0, qIdx);

                        if (method == "GET" && (pathOnly == "/" || pathOnly == ""))
                        {
                            string html = BuildStatusHtml();
                            SendResponse(stream, 200, "OK", "text/html; charset=utf-8", html);
                            return;
                        }

                        if (method == "GET" && (pathOnly.Equals("/status", StringComparison.OrdinalIgnoreCase) ||
                                                pathOnly.Equals("/health", StringComparison.OrdinalIgnoreCase)))
                        {
                            string json = SerializeJson(new Dictionary<string, object>
                            {
                                { "status", "running" },
                                { "service", "PC-Ovladac" },
                                { "port", _port },
                                { "timestamp", DateTime.Now.ToString("o") }
                            });
                            SendResponse(stream, 200, "OK", "application/json; charset=utf-8", json);
                            return;
                        }

                        if (method == "GET" && pathOnly.Equals("/commands", StringComparison.OrdinalIgnoreCase))
                        {
                            var cmds = _commandsProvider != null ? _commandsProvider() : new List<CommandMapping>();
                            var list = new List<Dictionary<string, string>>();
                            foreach (var c in cmds)
                            {
                                bool isCustom = string.Equals(c.ActionType, "RunProgram", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(c.ActionType, "Shortcut", StringComparison.OrdinalIgnoreCase);
                                string displayName = isCustom ? c.Name : ActionExecutor.GetDisplayName(c.ActionType);
                                string iconBase64 = string.Equals(c.ActionType, "RunProgram", StringComparison.OrdinalIgnoreCase)
                                    ? IconHelper.ExtractProgramIconBase64(c.Parameter)
                                    : "";

                                list.Add(new Dictionary<string, string>
                                {
                                    { "name", c.Name },
                                    { "actionType", c.ActionType },
                                    { "displayName", displayName },
                                    { "iconBase64", iconBase64 }
                                });
                            }
                            string json = SerializeJson(new Dictionary<string, object>
                            {
                                { "status", "ok" },
                                { "commands", list }
                            });
                            SendResponse(stream, 200, "OK", "application/json; charset=utf-8", json);
                            return;
                        }

                        if (pathOnly.StartsWith("/cmd/", StringComparison.OrdinalIgnoreCase))
                        {
                            string cmdName = Uri.UnescapeDataString(pathOnly.Substring(5)).Trim('/');
                            HandleCommandRequest(stream, remoteIp, cmdName);
                            return;
                        }

                        if (method == "POST" && (pathOnly.Equals("/cmd", StringComparison.OrdinalIgnoreCase) || pathOnly == "/"))
                        {
                            string cmdName = ExtractCommandFromJson(body);
                            HandleCommandRequest(stream, remoteIp, cmdName);
                            return;
                        }

                        string notFound = SerializeJson(new Dictionary<string, object>
                        {
                            { "status", "error" },
                            { "message", "Neznámá cesta: " + pathOnly }
                        });
                        SendResponse(stream, 404, "Not Found", "application/json; charset=utf-8", notFound);
                    }
                }
                catch
                {
                    // Odpojený klient nebo timeout
                }
                finally
                {
                    try { client.Client.Shutdown(SocketShutdown.Send); } catch { }
                }
            }
        }

        private void HandleCommandRequest(NetworkStream stream, string remoteIp, string cmdName)
        {
            if (string.IsNullOrWhiteSpace(cmdName))
            {
                string errJson = SerializeJson(new Dictionary<string, object>
                {
                    { "status", "error" },
                    { "message", "Chybí název příkazu." }
                });
                SendResponse(stream, 400, "Bad Request", "application/json; charset=utf-8", errJson);
                return;
            }

            ActionExecutionResult result = _commandDispatcher != null
                ? _commandDispatcher(cmdName, remoteIp)
                : new ActionExecutionResult { Success = true, Message = "Příkaz přijat." };

            int code = result.Success ? 200 : 404;
            string codeText = result.Success ? "OK" : "Not Found";

            string json = SerializeJson(new Dictionary<string, object>
            {
                { "status", result.Success ? "ok" : "error" },
                { "command", cmdName },
                { "message", result.Message },
                { "senderIp", remoteIp },
                { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            });
            SendResponse(stream, code, codeText, "application/json; charset=utf-8", json);
        }

        private static bool ReadHttpRequest(NetworkStream stream, out string method, out string path, out string body)
        {
            method = "";
            path = "";
            body = "";

            byte[] buffer = new byte[32768];
            int totalRead = 0;
            int headerEnd = -1;

            while (totalRead < buffer.Length)
            {
                int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                if (read <= 0) break;
                totalRead += read;

                if (totalRead > 0 && buffer[0] == 0x16) return false;

                headerEnd = FindHeaderEnd(buffer, totalRead);
                if (headerEnd >= 0) break;
            }

            if (totalRead == 0 || headerEnd < 0) return false;

            string headerText = Encoding.UTF8.GetString(buffer, 0, headerEnd);
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return false;

            string[] requestParts = lines[0].Split(' ');
            if (requestParts.Length < 2) return false;

            method = requestParts[0].ToUpperInvariant();
            path = requestParts[1];

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    string key = lines[i].Substring(0, colon).Trim();
                    string val = lines[i].Substring(colon + 1).Trim();
                    if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(val, out contentLength);
                    }
                }
            }

            int bodyStart = headerEnd + 4;
            int alreadyInBuffer = totalRead - bodyStart;
            if (contentLength > 0 && contentLength <= 65536)
            {
                byte[] bodyBytes = new byte[contentLength];
                int copied = Math.Min(alreadyInBuffer, contentLength);
                if (copied > 0)
                {
                    Buffer.BlockCopy(buffer, bodyStart, bodyBytes, 0, copied);
                }
                while (copied < contentLength)
                {
                    int read = stream.Read(bodyBytes, copied, contentLength - copied);
                    if (read <= 0) break;
                    copied += read;
                }
                body = Encoding.UTF8.GetString(bodyBytes, 0, copied);
            }

            return true;
        }

        private static int FindHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 0; i <= length - 4; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' &&
                    buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                {
                    return i;
                }
            }
            return -1;
        }

        private static void SendResponse(NetworkStream stream, int statusCode, string statusText, string contentType, string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body ?? "");
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(statusText).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
            sb.Append("Connection: close\r\n");
            sb.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (bodyBytes.Length > 0)
            {
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }
            stream.Flush();
        }

        private static string ExtractCommandFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var serializer = new JavaScriptSerializer();
                var dict = serializer.Deserialize<Dictionary<string, object>>(json);
                if (dict == null) return null;

                object val;
                if (dict.TryGetValue("command", out val) && val != null) return val.ToString().Trim();
                if (dict.TryGetValue("cmd", out val) && val != null) return val.ToString().Trim();
            }
            catch { }
            return null;
        }

        private static string SerializeJson(object obj)
        {
            var serializer = new JavaScriptSerializer();
            return serializer.Serialize(obj);
        }

        private string BuildStatusHtml()
        {
            var cmds = _commandsProvider != null ? _commandsProvider() : new List<CommandMapping>();
            var btns = new StringBuilder();
            foreach (var c in cmds)
            {
                string safeName = WebUtility.HtmlEncode(c.Name);
                string urlName = Uri.EscapeDataString(c.Name);
                btns.Append("<button class=\"cmd\" onclick=\"sendCmd('")
                    .Append(urlName)
                    .Append("')\">")
                    .Append(safeName)
                    .Append("</button>");
            }

            return "<!DOCTYPE html><html lang=\"cs\"><head><meta charset=\"utf-8\">" +
                   "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                   "<title>PC Ovladač</title>" +
                   "<style>body{font-family:system-ui,sans-serif;max-width:520px;margin:1.5rem auto;padding:1rem;background:#f5f7fa;color:#1f2937}" +
                   ".card{background:#fff;border-radius:12px;padding:1.25rem;box-shadow:0 2px 8px rgba(0,0,0,.08)}" +
                   "h1{margin-top:0;font-size:1.3rem}.grid{display:grid;grid-template-columns:1fr 1fr;gap:.6rem;margin-top:1rem}" +
                   "button.cmd{padding:.75rem;border:none;border-radius:8px;background:#2563eb;color:#fff;font-size:.95rem;font-weight:600;cursor:pointer}" +
                   "#msg{margin-top:1rem;font-size:.9rem;color:#0f766e}</style></head>" +
                   "<body><div class=\"card\"><h1>PC-Ovladac (port " + _port + ")</h1>" +
                   "<div class=\"grid\">" + btns + "</div><div id=\"msg\"></div></div>" +
                   "<script>function sendCmd(n){fetch('/cmd/'+n).then(r=>r.json()).then(d=>{" +
                   "document.getElementById('msg').textContent=d.message||d.status;" +
                   "}).catch(e=>{document.getElementById('msg').textContent='Chyba: '+e;});}</script>" +
                   "</body></html>";
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
