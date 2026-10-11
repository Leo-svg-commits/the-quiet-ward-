using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public sealed class QuietWardServer : IDisposable
{
    private sealed class AccountRecord
    {
        public string Salt;
        public string PasswordHash;
    }

    private sealed class PlayerConnection
    {
        public string Id;
        public string Name;
        public string RoomId;
        public string CoatColor = "field";
        public WebSocket Socket;
        public readonly SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
        public double X;
        public double Z;
        public double Yaw;
        public int Floor;
        public long LastChatTicks;
        public long LastPvpAttackTicks;
        public bool PvpEnabled;
        public bool Downed;
        public int Hearts = 3;
        public int ReviveHearts = 5;
    }

    private readonly string root;
    private readonly int port;
    private readonly bool allowLan;
    private readonly string accountPath;
    private HttpListener listener = new HttpListener();
    private readonly ConcurrentDictionary<string, PlayerConnection> players =
        new ConcurrentDictionary<string, PlayerConnection>();
    private readonly ConcurrentDictionary<string, string> sessionTokens =
        new ConcurrentDictionary<string, string>();
    private readonly object accountsLock = new object();
    private Dictionary<string, AccountRecord> accounts = new Dictionary<string, AccountRecord>(StringComparer.OrdinalIgnoreCase);
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 32768 };
    private CancellationTokenSource cancellation;
    private bool lanEnabled;

    public QuietWardServer(string projectRoot, int serverPort)
        : this(projectRoot, serverPort, true)
    {
    }

    public QuietWardServer(string projectRoot, int serverPort, bool enableLan)
    {
        root = Path.GetFullPath(projectRoot);
        if (serverPort < 1 || serverPort > 65535)
            throw new ArgumentOutOfRangeException("serverPort");
        port = serverPort;
        allowLan = enableLan;
        accountPath = Path.Combine(root, "accounts.json");
        LoadAccounts();
    }

    public string Address
    {
        get { return "http://127.0.0.1:" + port; }
    }

    public void Start()
    {
        if (!allowLan)
        {
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();
            lanEnabled = false;
        }
        else
        {
            try
            {
                listener.Prefixes.Add("http://+:" + port + "/");
                listener.Start();
                lanEnabled = true;
            }
            catch (HttpListenerException)
            {
                StartLocalOnly();
            }
            catch (UnauthorizedAccessException)
            {
                StartLocalOnly();
            }
        }

        cancellation = new CancellationTokenSource();
        Task.Run((Func<Task>)ListenLoop);
        Console.WriteLine("The Quiet Ward server is running at " + Address);
        List<string> addresses = GetHostAddresses();
        if (lanEnabled && addresses.Count > 0)
        {
            Console.WriteLine("Share a local-network address with players in your room:");
            foreach (string address in addresses)
                Console.WriteLine("  " + address);
        }
        else if (!allowLan)
        {
            Console.WriteLine("Local-only hosting was requested; other devices cannot join this server.");
        }
        else
        {
            Console.WriteLine("Local-network hosting is unavailable. Run PowerShell as administrator and allow TCP port " +
                port + " through Windows Defender Firewall.");
        }
        Console.WriteLine("Press Ctrl+C to stop the local server.");
    }

    private void StartLocalOnly()
    {
        listener.Close();
        listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        listener.Start();
        lanEnabled = false;
    }

    private async Task ListenLoop()
    {
        while (!cancellation.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                if (!cancellation.IsCancellationRequested)
                    Console.Error.WriteLine("The game server stopped accepting connections.");
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            StartRequestHandler(context);
        }
    }

    private void StartRequestHandler(HttpListenerContext context)
    {
        Task handler = context.Request.IsWebSocketRequest
            ? Task.Run(() => HandleWebSocket(context))
            : Task.Run(() => HandleHttp(context));
        handler.ContinueWith(task =>
            Console.Error.WriteLine("A request handler failed: " + task.Exception.GetBaseException().Message),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task HandleHttp(HttpListenerContext context)
    {
        try
        {
            string route = context.Request.Url.AbsolutePath.Trim('/');
            if (route == "auth/signup" || route == "auth/login")
            {
                HandleAuthentication(context, route == "auth/signup");
                return;
            }
            string file = route == "" || route == "index.html" ? "index.html" :
                route == "README.md" ? "README.md" : null;

            if (route == "health")
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                context.Response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
                if (context.Request.HttpMethod == "OPTIONS")
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }
                byte[] health = Encoding.UTF8.GetBytes(json.Serialize(new Dictionary<string, object>
                {
                    { "ok", true },
                    { "players", players.Count },
                    { "port", port },
                    { "lanEnabled", lanEnabled },
                    { "addresses", GetHostAddresses() }
                }));
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = health.Length;
                if (context.Request.HttpMethod != "HEAD")
                    await context.Response.OutputStream.WriteAsync(health, 0, health.Length).ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            if (context.Request.Url.AbsolutePath == "/multiplayer")
            {
                context.Response.StatusCode = 426;
                context.Response.Close();
                return;
            }

            if (file == null)
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }

            byte[] content = File.ReadAllBytes(Path.Combine(root, file));
            context.Response.ContentType = file == "index.html"
                ? "text/html; charset=utf-8"
                : "text/markdown; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.ContentLength64 = content.Length;
            if (context.Request.HttpMethod != "HEAD")
                await context.Response.OutputStream.WriteAsync(content, 0, content.Length).ConfigureAwait(false);
            context.Response.Close();
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine("Could not serve a game file: " + ex.Message);
            CloseResponse(context, 500);
        }
        catch (HttpListenerException)
        {
            CloseResponse(context, 500);
        }
        catch (ObjectDisposedException)
        {
            CloseResponse(context, 500);
        }
    }

    private void HandleAuthentication(HttpListenerContext context, bool createAccount)
    {
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        if (context.Request.HttpMethod == "OPTIONS")
        {
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
        }
        if (context.Request.HttpMethod != "POST")
        {
            WriteJsonResponse(context, 405, new Dictionary<string, object> { { "error", "Use POST to sign in." } });
            return;
        }

        Dictionary<string, object> body;
        try
        {
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                body = json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not read an account request: " + ex.Message);
            WriteJsonResponse(context, 400, new Dictionary<string, object> { { "error", "The sign-in request was invalid." } });
            return;
        }

        object rawUsername;
        object rawPassword;
        string username = body != null && body.TryGetValue("username", out rawUsername) ? rawUsername as string : null;
        string password = body != null && body.TryGetValue("password", out rawPassword) ? rawPassword as string : null;
        if (String.IsNullOrWhiteSpace(username) || !Regex.IsMatch(username, "^[A-Za-z0-9_-]{3,18}$"))
        {
            WriteJsonResponse(context, 400, new Dictionary<string, object> { { "error", "Username must be 3-18 letters, numbers, underscores, or hyphens." } });
            return;
        }
        if (String.IsNullOrEmpty(password) || password.Length < 8 || password.Length > 128)
        {
            WriteJsonResponse(context, 400, new Dictionary<string, object> { { "error", "Password must be 8-128 characters." } });
            return;
        }

        username = username.Trim();
        string canonical = username.ToLowerInvariant();
        lock (accountsLock)
        {
            AccountRecord record;
            if (createAccount)
            {
                if (accounts.ContainsKey(canonical))
                {
                    WriteJsonResponse(context, 409, new Dictionary<string, object> { { "error", "That username is already registered on this game server." } });
                    return;
                }
                byte[] salt = new byte[16];
                using (var random = RandomNumberGenerator.Create())
                    random.GetBytes(salt);
                record = new AccountRecord
                {
                    Salt = Convert.ToBase64String(salt),
                    PasswordHash = HashPassword(password, salt)
                };
                accounts[canonical] = record;
                try
                {
                    SaveAccounts();
                }
                catch (IOException ex)
                {
                    accounts.Remove(canonical);
                    Console.Error.WriteLine("Could not save a game account: " + ex.Message);
                    WriteJsonResponse(context, 500, new Dictionary<string, object> { { "error", "Could not save the account on the game server." } });
                    return;
                }
            }
            else if (!accounts.TryGetValue(canonical, out record) ||
                !VerifyPassword(password, record))
            {
                WriteJsonResponse(context, 401, new Dictionary<string, object> { { "error", "Username or password is incorrect." } });
                return;
            }
        }

        string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        sessionTokens[token] = username;
        WriteJsonResponse(context, 200, new Dictionary<string, object>
        {
            { "token", token },
            { "username", username }
        });
    }

    private static string HashPassword(string password, byte[] salt)
    {
        using (var derive = new Rfc2898DeriveBytes(password, salt, 150000))
            return Convert.ToBase64String(derive.GetBytes(32));
    }

    private static bool VerifyPassword(string password, AccountRecord record)
    {
        try
        {
            byte[] expected = Convert.FromBase64String(record.PasswordHash);
            byte[] actual = Convert.FromBase64String(HashPassword(password, Convert.FromBase64String(record.Salt)));
            int difference = expected.Length ^ actual.Length;
            for (int i = 0; i < Math.Min(expected.Length, actual.Length); i++)
                difference |= expected[i] ^ actual[i];
            return difference == 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void LoadAccounts()
    {
        if (!File.Exists(accountPath))
            return;
        try
        {
            Dictionary<string, AccountRecord> loaded =
                json.Deserialize<Dictionary<string, AccountRecord>>(File.ReadAllText(accountPath));
            if (loaded != null)
                accounts = new Dictionary<string, AccountRecord>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not load game accounts from " + accountPath + ".", ex);
        }
    }

    private void SaveAccounts()
    {
        string temporaryPath = accountPath + ".tmp";
        File.WriteAllText(temporaryPath, json.Serialize(accounts));
        if (File.Exists(accountPath))
            File.Replace(temporaryPath, accountPath, null);
        else
            File.Move(temporaryPath, accountPath);
    }

    private void WriteJsonResponse(HttpListenerContext context, int statusCode, Dictionary<string, object> payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json.Serialize(payload));
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    private async Task HandleWebSocket(HttpListenerContext context)
    {
        WebSocket socket = null;
        PlayerConnection connection = null;
        try
        {
            if (context.Request.Url.AbsolutePath != "/multiplayer")
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
            }
            string roomId = NormalizeRoom(context.Request.QueryString["room"]);
            if (players.Values.Count(player => player.RoomId == roomId) >= 12)
            {
                context.Response.StatusCode = 503;
                context.Response.Close();
                return;
            }

            socket = (await context.AcceptWebSocketAsync(null).ConfigureAwait(false)).WebSocket;
            string accountToken = context.Request.QueryString["token"];
            string accountName = null;
            bool authenticated = !String.IsNullOrEmpty(accountToken) && sessionTokens.TryGetValue(accountToken, out accountName);
            connection = new PlayerConnection
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = authenticated ? accountName : "Wanderer-" + Guid.NewGuid().ToString("N").Substring(0, 4),
                RoomId = roomId,
                Socket = socket,
                Hearts = 3,
                ReviveHearts = 5
            };
            object[] existing = players.Values.Where(player => player.RoomId == roomId)
                .Select(PeerDescriptor).ToArray();
            if (!players.TryAdd(connection.Id, connection))
                return;

            await SendJson(connection, new Dictionary<string, object>
            {
                { "type", "welcome" },
                { "id", connection.Id },
                { "name", connection.Name },
                { "peers", existing }
            }).ConfigureAwait(false);

            Broadcast(new Dictionary<string, object>
            {
                { "type", "peer-joined" },
                { "peer", PeerDescriptor(connection) }
            }, connection.Id, connection.RoomId);

            byte[] buffer = new byte[4096];
            while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
            {
                using (MemoryStream message = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token)
                            .ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                            return;
                        if (result.MessageType != WebSocketMessageType.Text ||
                            message.Length + result.Count > 8192)
                            throw new InvalidDataException("A multiplayer message exceeded the allowed size.");
                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    Dictionary<string, object> data =
                        json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(message.ToArray()));
                    if (data != null)
                        HandleMessage(connection, data);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine("Disconnected an invalid multiplayer message: " + ex.Message);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("A multiplayer connection ended: " + ex.Message);
        }
        finally
        {
            if (connection != null)
            {
                PlayerConnection removed;
                players.TryRemove(connection.Id, out removed);
                Broadcast(new Dictionary<string, object>
                {
                    { "type", "peer-left" },
                    { "id", connection.Id }
                }, connection.Id, connection.RoomId);
            }
            if (socket != null)
            {
                try { socket.Abort(); socket.Dispose(); }
                catch (ObjectDisposedException) { }
            }
            else
            {
                CloseResponse(context, 500);
            }
        }
    }

    private void HandleMessage(PlayerConnection sender, Dictionary<string, object> data)
    {
        object rawType;
        if (!data.TryGetValue("type", out rawType))
            return;
        string type = rawType as string;
        if (type == "pose")
        {
            double x, z, yaw;
            int floor;
            if (!TryNumber(data, "x", out x) || !TryNumber(data, "z", out z) ||
                !TryNumber(data, "yaw", out yaw) || !TryInteger(data, "floor", out floor) ||
                Math.Abs(x) > 18 || Math.Abs(z) > 34 || floor < 0 || floor > 2)
                return;

            lock (sender)
            {
                sender.X = x;
                sender.Z = z;
                sender.Yaw = yaw;
                sender.Floor = floor;
            }
            var pose = new Dictionary<string, object>
            {
                { "type", "pose" }, { "from", sender.Id }, { "name", sender.Name },
                { "x", x }, { "z", z }, { "yaw", yaw }, { "floor", floor },
                { "moving", BooleanValue(data, "moving") }
            };
            Broadcast(pose, sender.Id, sender.RoomId);
            return;
        }

        if (type == "pvp-state")
        {
            sender.PvpEnabled = BooleanValue(data, "enabled");
            if (!sender.PvpEnabled)
            {
                sender.Downed = false;
                sender.Hearts = 3;
            }
            Broadcast(new Dictionary<string, object>
            {
                { "type", "pvp-state" }, { "from", sender.Id },
                { "enabled", sender.PvpEnabled }, { "downed", sender.Downed },
                { "hearts", sender.Hearts }, { "reviveHearts", sender.ReviveHearts }
            }, sender.Id, sender.RoomId);
            return;
        }

        if (type == "pvp-hit")
        {
            object rawTarget;
            string targetId = data.TryGetValue("target", out rawTarget) ? rawTarget as string : null;
            PlayerConnection target;
            if (!sender.PvpEnabled || sender.Downed || String.IsNullOrEmpty(targetId) ||
                !players.TryGetValue(targetId, out target) || target.RoomId != sender.RoomId ||
                !target.PvpEnabled || target.Downed)
                return;

            long now = DateTime.UtcNow.Ticks;
            if (now - Interlocked.Read(ref sender.LastPvpAttackTicks) < TimeSpan.FromMilliseconds(650).Ticks)
                return;
            double dx = target.X - sender.X;
            double dz = target.Z - sender.Z;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            double facing = Math.Atan2(dx, -dz);
            double angle = Math.Atan2(Math.Sin(facing - sender.Yaw), Math.Cos(facing - sender.Yaw));
            if (sender.Floor != target.Floor || distance > 10 || Math.Abs(angle) > 1.15)
                return;
            Interlocked.Exchange(ref sender.LastPvpAttackTicks, now);

            lock (target)
            {
                target.Hearts = Math.Max(0, target.Hearts - 1);
                target.Downed = target.Hearts == 0;
            }
            var damage = new Dictionary<string, object>
            {
                { "type", "pvp-health" }, { "from", sender.Id }, { "target", target.Id },
                { "hearts", target.Hearts }, { "attacker", sender.Name }
            };
            Broadcast(damage, null, sender.RoomId);
            if (target.Downed)
            {
                Broadcast(new Dictionary<string, object>
                {
                    { "type", "pvp-down" }, { "from", target.Id }, { "name", target.Name }
                }, null, sender.RoomId);
            }
            return;
        }

        if (type == "pvp-rescue")
        {
            object rawTarget, rawAccept;
            string targetId = data.TryGetValue("target", out rawTarget) ? rawTarget as string : null;
            bool accept = data.TryGetValue("accept", out rawAccept) && rawAccept is bool && (bool)rawAccept;
            PlayerConnection target;
            if (!sender.PvpEnabled || sender.Downed || String.IsNullOrEmpty(targetId) ||
                !players.TryGetValue(targetId, out target) || target.RoomId != sender.RoomId ||
                !target.PvpEnabled || !target.Downed)
                return;

            if (!accept)
            {
                Broadcast(new Dictionary<string, object>
                {
                    { "type", "pvp-rescue-declined" }, { "from", sender.Id },
                    { "target", target.Id }, { "name", sender.Name }
                }, null, sender.RoomId);
                return;
            }

            lock (sender)
            {
                if (sender.ReviveHearts < 2)
                    return;
                sender.ReviveHearts -= 2;
            }
            lock (target)
            {
                target.Downed = false;
                target.Hearts = 1;
            }
            Broadcast(new Dictionary<string, object>
            {
                { "type", "pvp-rescued" }, { "from", sender.Id }, { "target", target.Id },
                { "hearts", target.Hearts }, { "reviveHearts", sender.ReviveHearts },
                { "name", sender.Name }
            }, null, sender.RoomId);
            return;
        }

        if (type == "pvp-respawn")
        {
            int hearts;
            if (!sender.PvpEnabled || !TryInteger(data, "hearts", out hearts))
                return;
            lock (sender)
            {
                sender.Hearts = Math.Max(1, Math.Min(3, hearts));
                sender.Downed = false;
            }
            Broadcast(new Dictionary<string, object>
            {
                { "type", "pvp-state" }, { "from", sender.Id }, { "enabled", true },
                { "downed", false }, { "hearts", sender.Hearts }, { "reviveHearts", sender.ReviveHearts }
            }, sender.Id, sender.RoomId);
            return;
        }

        if (type == "rename")
        {
            object rawName;
            string name = data.TryGetValue("name", out rawName) ? rawName as string : null;
            if (String.IsNullOrWhiteSpace(name))
                return;
            name = new String(name.Trim().Where(c => !Char.IsControl(c)).Take(18).ToArray());
            if (name.Length == 0)
                return;
            sender.Name = name;
            Broadcast(new Dictionary<string, object>
            {
                { "type", "peer-renamed" }, { "id", sender.Id }, { "name", name }
            }, sender.Id, sender.RoomId);
            return;
        }

        if (type == "profile")
        {
            object rawColor;
            string color = data.TryGetValue("coatColor", out rawColor) ? rawColor as string : null;
            if (color != "field" && color != "charcoal" && color != "brown" && color != "blue")
                return;
            lock (sender)
                sender.CoatColor = color;
            Broadcast(new Dictionary<string, object>
            {
                { "type", "peer-profile" }, { "id", sender.Id }, { "coatColor", color }
            }, sender.Id, sender.RoomId);
            return;
        }

        if (type == "chat")
        {
            object rawText;
            string text = data.TryGetValue("text", out rawText) ? rawText as string : null;
            if (String.IsNullOrWhiteSpace(text))
                return;
            long now = DateTime.UtcNow.Ticks;
            if (now - Interlocked.Read(ref sender.LastChatTicks) < TimeSpan.FromMilliseconds(500).Ticks)
                return;
            Interlocked.Exchange(ref sender.LastChatTicks, now);
            text = new String(text.Where(c => !Char.IsControl(c) || c == '\n' || c == '\t').Take(240).ToArray()).Trim();
            if (text.Length > 0)
            {
                Broadcast(new Dictionary<string, object>
                {
                    { "type", "chat" }, { "from", sender.Id }, { "name", sender.Name }, { "text", text }
                }, null, sender.RoomId);
            }
            return;
        }

        if (type == "voice-state")
        {
            Broadcast(new Dictionary<string, object>
            {
                { "type", "voice-state" },
                { "from", sender.Id },
                { "name", sender.Name },
                { "enabled", BooleanValue(data, "enabled") }
            }, sender.Id, sender.RoomId);
            return;
        }

        if (type == "signal")
        {
            object rawTarget, rawSignal;
            string target = data.TryGetValue("target", out rawTarget) ? rawTarget as string : null;
            if (String.IsNullOrEmpty(target) || target == sender.Id ||
                !data.TryGetValue("signal", out rawSignal))
                return;
            PlayerConnection recipient;
            if (!players.TryGetValue(target, out recipient) || recipient.RoomId != sender.RoomId)
                return;
            var relay = new Dictionary<string, object>
            {
                { "type", "signal" },
                { "from", sender.Id },
                { "name", sender.Name },
                { "signal", rawSignal }
            };
            SendJsonAsync(recipient, relay);
        }
    }

    private static string NormalizeRoom(string room)
    {
        if (String.IsNullOrWhiteSpace(room))
            return "ward";
        string normalized = new String(room.Trim().ToLowerInvariant()
            .Where(c => Char.IsLetterOrDigit(c) || c == '-').Take(24).ToArray());
        return normalized.Length == 0 ? "ward" : normalized;
    }

    private static bool TryNumber(Dictionary<string, object> data, string key, out double value)
    {
        value = 0;
        object raw;
        if (!data.TryGetValue(key, out raw) || raw == null)
            return false;
        try
        {
            value = Convert.ToDouble(raw);
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
        catch (FormatException) { return false; }
        catch (InvalidCastException) { return false; }
    }

    private static bool TryInteger(Dictionary<string, object> data, string key, out int value)
    {
        value = 0;
        object raw;
        if (!data.TryGetValue(key, out raw) || raw == null)
            return false;
        try { value = Convert.ToInt32(raw); return true; }
        catch (FormatException) { return false; }
        catch (InvalidCastException) { return false; }
        catch (OverflowException) { return false; }
    }

    private static bool BooleanValue(Dictionary<string, object> data, string key)
    {
        object raw;
        return data.TryGetValue(key, out raw) && raw is bool && (bool)raw;
    }

    private object PeerDescriptor(PlayerConnection peer)
    {
        lock (peer)
        {
            return new Dictionary<string, object>
            {
                { "id", peer.Id }, { "name", peer.Name }, { "x", peer.X },
                { "z", peer.Z }, { "yaw", peer.Yaw }, { "floor", peer.Floor },
                { "coatColor", peer.CoatColor },
                { "pvpEnabled", peer.PvpEnabled }, { "downed", peer.Downed },
                { "hearts", peer.Hearts }, { "reviveHearts", peer.ReviveHearts }
            };
        }
    }

    private void Broadcast(Dictionary<string, object> message, string exceptId, string roomId)
    {
        string serialized;
        try { serialized = json.Serialize(message); }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine("Could not encode a multiplayer message: " + ex.Message);
            return;
        }

        byte[] payload = Encoding.UTF8.GetBytes(serialized);
        foreach (PlayerConnection player in players.Values)
        {
            if ((exceptId != null && player.Id == exceptId) ||
                (roomId != null && player.RoomId != roomId))
                continue;
            SendBytesAsync(player, payload);
        }

    }

    private List<string> GetHostAddresses()
    {
        if (!lanEnabled)
            return new List<string>();

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address))
            .Distinct()
            .Select(address => "http://" + address + ":" + port)
            .ToList();
    }

    private void SendJsonAsync(PlayerConnection player, Dictionary<string, object> message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json.Serialize(message));
        SendBytesAsync(player, payload);
    }

    private async void SendBytesAsync(PlayerConnection player, byte[] payload)
    {
        bool locked = false;
        try
        {
            locked = await player.SendLock.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (!locked || player.Socket.State != WebSocketState.Open)
                return;
            await player.Socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true,
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            Console.Error.WriteLine("A multiplayer message could not be delivered: " + ex.Message);
        }
        finally
        {
            if (locked)
                player.SendLock.Release();
        }
    }

    private async Task SendJson(PlayerConnection player, Dictionary<string, object> message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json.Serialize(message));
        await player.SendLock.WaitAsync(cancellation.Token).ConfigureAwait(false);
        try
        {
            await player.Socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true,
                cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            player.SendLock.Release();
        }
    }

    private static void CloseResponse(HttpListenerContext context, int status)
    {
        try
        {
            context.Response.StatusCode = status;
            context.Response.Close();
        }
        catch (ObjectDisposedException) { }
        catch (HttpListenerException) { }
    }

    public void Stop()
    {
        if (cancellation != null)
        {
            cancellation.Cancel();
            if (listener.IsListening)
                listener.Stop();
            foreach (PlayerConnection player in players.Values)
            {
                try { player.Socket.Abort(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    public void Dispose()
    {
        Stop();
        listener.Close();
        if (cancellation != null)
            cancellation.Dispose();
    }
}
