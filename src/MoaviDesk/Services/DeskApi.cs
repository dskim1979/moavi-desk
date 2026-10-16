/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 */

using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoaviDesk.Services;

/// <summary>A desktop the signed-in user holds (GET /api/vlab/me/desktops).</summary>
internal sealed class DeskInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Pool { get; set; } = "";
    public string State { get; set; } = "";
    public string? Os { get; set; }
    public DeskSpec? Spec { get; set; }
    public List<string> Protocols { get; set; } = [];
}

internal sealed class DeskSpec
{
    public int? Cpu { get; set; }
    [JsonPropertyName("memory_gb")] public double? MemoryGb { get; set; }
    public string? Gpu { get; set; }
}

/// <summary>POST /api/vlab/desktops/&lt;id&gt;/connect: vnc → ws_path + password, spice → a .vv file.</summary>
internal sealed class ConnectInfo
{
    public string Type { get; set; } = "";
    [JsonPropertyName("ws_path")] public string? WsPath { get; set; }
    public string? Password { get; set; }
    public string? Vv { get; set; }
    public string? Filename { get; set; }
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
}

/// <summary>GET /api/vlab/client/config: what the engine asks of its Windows clients.</summary>
internal sealed class ClientConfig
{
    [JsonPropertyName("product")] public string Product { get; set; } = "MOAVI Desk";
    [JsonPropertyName("latest_version")] public string? LatestVersion { get; set; }
    [JsonPropertyName("download_url")] public string? DownloadUrl { get; set; }
    [JsonPropertyName("min_version")] public string? MinVersion { get; set; }
    [JsonPropertyName("protocol")] public string Protocol { get; set; } = "spice";
    [JsonPropertyName("support_url")] public string? SupportUrl { get; set; }
}

/// <summary>The engine's answer to an enrolling PC (enroll and enroll/status).</summary>
internal sealed class EnrollAnswer
{
    public string? Token { get; set; }
    public string State { get; set; } = "";
    public string? Key { get; set; }
    public EnrollRef? Lab { get; set; }
    public EnrollRef? Terminal { get; set; }
    [JsonPropertyName("poll_seconds")] public int PollSeconds { get; set; } = 30;
}

internal sealed class EnrollRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class DeskApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

internal enum LoginResult { Ok, SecondFactor, Invalid, Locked, Network, Other }

/// <summary>
/// The MOAVI Desk engine as a Windows client sees it. The session lives in the engine's
/// session_id cookie; X-Requested-With satisfies the engine's CSRF guard on /api/*.
/// </summary>
internal sealed class DeskApi : IDisposable
{
    public const string TerminalKeyHeader = "X-Terminal-Key";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _http;

    public Uri BaseUri { get; }
    public bool IgnoreCertificateErrors { get; }
    public string Username { get; private set; } = "";
    public string LastError { get; private set; } = "";

    public DeskApi(string serverUrl, bool ignoreCertificateErrors)
    {
        BaseUri = NormalizeServer(serverUrl);
        IgnoreCertificateErrors = ignoreCertificateErrors;
        var handler = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
        if (ignoreCertificateErrors)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        _http = new HttpClient(handler) { BaseAddress = BaseUri, Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"MOAVI-Desk-Client/{AppVersion}");
    }

    public static string AppVersion
        => typeof(DeskApi).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>"desk.example.com" → https://desk.example.com/ ; keeps an explicit scheme or port.</summary>
    public static Uri NormalizeServer(string serverUrl)
    {
        var s = (serverUrl ?? "").Trim().TrimEnd('/');
        if (!s.Contains("://", StringComparison.Ordinal)) { s = "https://" + s; }
        return new Uri(s + "/");
    }

    /// <summary>Address, port and scheme of a stored server URL (port 443/80 when not given).</summary>
    public static (string Host, int Port, bool Https) ParseServer(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) { return ("", 443, true); }
        try
        {
            var u = NormalizeServer(serverUrl);
            return (u.Host, u.Port, u.Scheme == Uri.UriSchemeHttps);
        }
        catch { return (serverUrl.Trim(), 443, true); }
    }

    /// <summary>https://host[:port] from the settings fields; the default port is left out.</summary>
    public static string ComposeServer(string host, int port, bool https)
    {
        host = host.Trim().TrimEnd('/');
        if (host.Contains("://", StringComparison.Ordinal)) { host = new Uri(host).Host; }
        if (host.Contains(':') && !host.StartsWith('[')) { host = $"[{host}]"; }   // IPv6
        var scheme = https ? "https" : "http";
        return (https && port == 443) || (!https && port == 80) ? $"{scheme}://{host}" : $"{scheme}://{host}:{port}";
    }

    /// <summary>
    /// Is a MOAVI Desk engine answering at <paramref name="serverUrl"/>? Asks the public
    /// /api/auth/check; returns the round trip, or why not.
    /// </summary>
    public static async Task<(bool Ok, long Ms, string Error)> ProbeAsync(string serverUrl, bool ignoreCertificateErrors,
                                                                         int timeoutSeconds = 6)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) { return (false, 0, ""); }
        var handler = new HttpClientHandler();
        if (ignoreCertificateErrors)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var r = await http.GetAsync(new Uri(NormalizeServer(serverUrl), "api/auth/check"));
            sw.Stop();
            var d = await ReadJsonAsync(r);
            return r.IsSuccessStatusCode && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("authenticated", out _)
                    ? (true, sw.ElapsedMilliseconds, "")
                    : (false, sw.ElapsedMilliseconds, L("MoaviTestNotEngine"));
        }
        catch (TaskCanceledException) { return (false, 0, L("MoaviTestTimeout")); }
        catch (HttpRequestException ex) { return (false, 0, ex.InnerException?.Message ?? ex.Message); }
        catch (Exception ex) { return (false, 0, ex.Message); }
    }

    /// <summary>The cookie header the engine set, for the console websocket upgrade.</summary>
    public string CookieHeader => _cookies.GetCookieHeader(BaseUri);

    public async Task<LoginResult> LoginAsync(string username, string password, string? totp)
    {
        HttpResponseMessage r;
        try
        {
            var body = new Dictionary<string, object?> { ["username"] = username, ["password"] = password, ["remember"] = false };
            if (!string.IsNullOrWhiteSpace(totp)) { body["totp_code"] = totp.Trim(); }
            r = await _http.PostAsync("api/auth/login", Body(body));
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return LoginResult.Network;
        }

        var d = await ReadJsonAsync(r);
        if (r.StatusCode == HttpStatusCode.OK && Bool(d, "requires_2fa")) { return LoginResult.SecondFactor; }
        if (r.StatusCode == HttpStatusCode.OK && Bool(d, "success"))
        {
            Username = username;
            return LoginResult.Ok;
        }
        LastError = Str(d, "error") ?? $"HTTP {(int)r.StatusCode}";
        if (Bool(d, "locked")) { return LoginResult.Locked; }
        return r.StatusCode == HttpStatusCode.Unauthorized ? LoginResult.Invalid : LoginResult.Other;
    }

    public async Task LogoutAsync()
    {
        try { await _http.PostAsync("api/auth/logout", null); } catch { /* already gone */ }
    }

    public async Task<List<DeskInfo>> DesktopsAsync()
    {
        var d = await SendAsync(HttpMethod.Get, "api/vlab/me/desktops");
        return d.TryGetProperty("desktops", out var list)
                ? list.Deserialize<List<DeskInfo>>(Json) ?? []
                : [];
    }

    public async Task<ConnectInfo> ConnectAsync(string desktopId, string protocol)
    {
        var d = await SendAsync(HttpMethod.Post, $"api/vlab/desktops/{Uri.EscapeDataString(desktopId)}/connect",
                                new { protocol });
        return d.Deserialize<ConnectInfo>(Json)!;
    }

    public Task PowerAsync(string desktopId, string action)
        => SendAsync(HttpMethod.Post, $"api/vlab/desktops/{Uri.EscapeDataString(desktopId)}/power", new { action });

    public async Task EndSessionAsync(string sessionId)
    {
        try { await SendAsync(HttpMethod.Post, $"api/vlab/sessions/{Uri.EscapeDataString(sessionId)}/end"); }
        catch { /* best effort */ }
    }

    /// <summary>The engine's wishes for this client; null when the engine predates the endpoint.</summary>
    public async Task<ClientConfig?> ClientConfigAsync()
    {
        try
        {
            var d = await SendAsync(HttpMethod.Get, $"api/vlab/client/config?version={Uri.EscapeDataString(AppVersion)}");
            return d.Deserialize<ClientConfig>(Json);
        }
        catch { return null; }
    }

    /// <summary>
    /// A room's PC asks to be known by its MAC (POST /api/vlab/terminals/register); an admin
    /// approves it under 단말 관리. Returns the terminal state, or null when it could not register.
    /// </summary>
    public async Task<string?> RegisterTerminalAsync()
    {
        var mac = PrimaryMac();
        if (mac is null) { return null; }
        try
        {
            var d = await SendAsync(HttpMethod.Post, "api/vlab/terminals/register",
                                    new { mac, name = Environment.MachineName });
            return d.TryGetProperty("terminal", out var t) ? Str(t, "state") : Str(d, "state");
        }
        catch { return null; }
    }

    /// <summary>
    /// A room's PC enrols with its installer's code, no one signed in (POST /api/vlab/terminals/enroll):
    /// it gets a poll token and its state (pending, or approved when the code approves at once).
    /// </summary>
    public async Task<EnrollAnswer> EnrollAsync(string mac, string name, string code)
        => (await SendAsync(HttpMethod.Post, "api/vlab/terminals/enroll", new { mac, name, code })).Deserialize<EnrollAnswer>(Json)!;

    /// <summary>
    /// The enrolled PC asks again (POST /api/vlab/terminals/enroll/status): pending, or approved with the
    /// kiosk key, which the engine hands out this once.
    /// </summary>
    public async Task<EnrollAnswer> EnrollStatusAsync(string token)
        => (await SendAsync(HttpMethod.Post, "api/vlab/terminals/enroll/status", new { token })).Deserialize<EnrollAnswer>(Json)!;

    /// <summary>LAB kiosk: the session running in this terminal's room and a desktop of it for the user.</summary>
    public async Task<(string DesktopId, string DesktopName, string Lab)> KioskStartAsync(string terminalKey)
    {
        var d = await SendAsync(HttpMethod.Post, "api/vlab/kiosk/start", new { },
                                new() { [TerminalKeyHeader] = terminalKey });
        var desk = d.GetProperty("desktop");
        var lab = d.TryGetProperty("lab", out var l) ? Str(l, "name") ?? "" : "";
        return (Str(desk, "id")!, Str(desk, "name") ?? "", lab);
    }

    /// <summary>The MAC of the first up, non-loopback network adapter, as aa:bb:cc:dd:ee:ff.</summary>
    public static string? PrimaryMac()
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces()
                                  .Where(n => n.OperationalStatus == OperationalStatus.Up
                                              && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                                                                         and not NetworkInterfaceType.Tunnel)
                                  .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
                                  .FirstOrDefault(n => n.GetPhysicalAddress().GetAddressBytes().Length == 6);
        var bytes = nic?.GetPhysicalAddress().GetAddressBytes();
        return bytes is null ? null : string.Join(":", bytes.Select(b => b.ToString("x2")));
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null,
                                              Dictionary<string, string>? headers = null)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null) { req.Content = Body(body); }
        foreach (var (k, v) in headers ?? []) { req.Headers.TryAddWithoutValidation(k, v); }

        HttpResponseMessage r;
        try { r = await _http.SendAsync(req); }
        catch (Exception) { throw new DeskApiException(0, "network", L("MoaviServerOffline")); }

        var d = await ReadJsonAsync(r);
        if (!r.IsSuccessStatusCode)
        {
            var code = Str(d, "code") ?? Str(d, "error") ?? $"HTTP {(int)r.StatusCode}";
            throw new DeskApiException((int)r.StatusCode, code, Str(d, "error") ?? code);
        }
        return d;
    }

    // a fixed-length body: JsonContent streams chunked, which some proxies and WSGI servers drop
    private static StringContent Body(object body)
        => new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text)) { return default; }
        try { return JsonDocument.Parse(text).RootElement.Clone(); }
        catch { return default; }
    }

    private static bool Bool(JsonElement d, string name)
        => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Str(JsonElement d, string name)
        => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

    public void Dispose() => _http.Dispose();
}
