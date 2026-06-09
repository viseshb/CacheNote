using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using CacheNote.Core.Data;
using CacheNote.Core.Models;

namespace CacheNote.Core.Cloud;

/// <summary>
/// Two-way Google Calendar sync (primary calendar) over the Calendar v3 REST API.
///
/// Sign-in: OAuth 2.0 Desktop flow — PKCE + loopback redirect (system browser; the
/// client id/secret come from .env). The refresh token is stored DPAPI-encrypted in
/// the settings table; nothing secret is ever logged.
///
/// Sync model:
///  - PUSH: local events without a google_id are inserted remotely; local events whose
///    updated_utc moved past the recorded google_updated_utc baseline are patched;
///    deletes of linked events are tombstoned (google_deletes) and deleted remotely.
///  - PULL: incremental via syncToken (full window on first run / 410 Gone), upserting
///    by google_id; "cancelled" items delete the local copy. Conflicts: newer side wins.
///
/// Local edits call <see cref="RequestSync"/> (debounced) so changes flow to Google
/// automatically; the app also syncs at startup and on a periodic timer.
/// </summary>
public sealed class GoogleCalendarSyncService
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string ApiBase = "https://www.googleapis.com/calendar/v3/calendars/primary/events";
    private const string Scope = "https://www.googleapis.com/auth/calendar.events";

    private const string RefreshTokenKey = "google_refresh_token";   // DPAPI-protected, base64
    private const string SyncTokenKey = "google_sync_token";
    private const string ConnectedKey = "google_connected";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly CloudConfig _cfg;
    private readonly IEventRepository _events;
    private readonly IReminderRepository _reminders;
    private readonly Services.ISettingsService _settings;
    private readonly ILogger<GoogleCalendarSyncService> _log;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTime _accessTokenExpiresUtc;
    private Timer? _debounce;

    public GoogleCalendarSyncService(
        CloudConfig cfg,
        IEventRepository events,
        IReminderRepository reminders,
        Services.ISettingsService settings,
        ILogger<GoogleCalendarSyncService> log)
    {
        _cfg = cfg;
        _events = events;
        _reminders = reminders;
        _settings = settings;
        _log = log;
    }

    /// <summary>OAuth client present in .env (prerequisite for everything else).</summary>
    public bool IsConfigured => _cfg.GoogleSyncConfigured && !string.IsNullOrWhiteSpace(_cfg.GoogleClientSecret);

    /// <summary>Signed in (a refresh token is stored).</summary>
    public bool IsConnected => _settings.GetBool(ConnectedKey) && LoadRefreshToken() is not null;

    /// <summary>Human-readable result of the last sync ("12 pulled, 3 pushed" / error text).</summary>
    public string LastSyncStatus { get; private set; } = "";

    /// <summary>Raised after a sync finishes so open calendar UI can refresh.</summary>
    public event Action? SyncCompleted;

    // ------------------------------------------------------------------ sign-in

    /// <summary>
    /// Interactive sign-in: opens the system browser, catches the loopback redirect,
    /// exchanges the code (PKCE), stores the refresh token (DPAPI), then runs a first sync.
    /// </summary>
    public async Task<bool> SignInAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            LastSyncStatus = "Add GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET to .env first.";
            return false;
        }

        // PKCE verifier/challenge.
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(verifierBytes);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var port = FreeTcpPort();
        var redirect = $"http://127.0.0.1:{port}/";

        var authUrl = AuthEndpoint
            + "?client_id=" + Uri.EscapeDataString(_cfg.GoogleClientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirect)
            + "&response_type=code"
            + "&scope=" + Uri.EscapeDataString(Scope)
            + "&code_challenge=" + challenge
            + "&code_challenge_method=S256"
            + "&access_type=offline"
            + "&prompt=consent";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();

        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        // Wait for the browser redirect (3 min timeout).
        var ctxTask = listener.GetContextAsync();
        var done = await Task.WhenAny(ctxTask, Task.Delay(TimeSpan.FromMinutes(3), ct));
        if (done != ctxTask)
        {
            LastSyncStatus = "Sign-in timed out — no browser response.";
            return false;
        }

        var ctx = ctxTask.Result;
        var code = ctx.Request.QueryString["code"];
        var error = ctx.Request.QueryString["error"];
        var html = code is not null
            ? "<html><body style='font-family:Segoe UI'><h3>CacheNote is connected to Google Calendar.</h3>You can close this tab.</body></html>"
            : $"<html><body style='font-family:Segoe UI'><h3>Sign-in failed.</h3>{WebUtility.HtmlEncode(error ?? "No code returned.")}</body></html>";
        var buf = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf, ct);
        ctx.Response.Close();

        if (code is null)
        {
            LastSyncStatus = "Sign-in was cancelled or denied.";
            return false;
        }

        // Exchange code → tokens.
        using var res = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _cfg.GoogleClientId,
            ["client_secret"] = _cfg.GoogleClientSecret,
            ["redirect_uri"] = redirect,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
        }), ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Google token exchange failed: {Status}", res.StatusCode);
            LastSyncStatus = $"Token exchange failed ({(int)res.StatusCode}). Check the OAuth client in .env.";
            return false;
        }

        using var doc = JsonDocument.Parse(body);
        var refresh = doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        _accessToken = doc.RootElement.GetProperty("access_token").GetString();
        _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(
            doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() - 60 : 3000);

        if (string.IsNullOrEmpty(refresh))
        {
            LastSyncStatus = "Google returned no refresh token — remove the app's access at myaccount.google.com/permissions and sign in again.";
            return false;
        }

        StoreRefreshToken(refresh);
        _settings.SetBool(ConnectedKey, true);
        _settings.Set(SyncTokenKey, null);   // force a full first pull
        _log.LogInformation("Google Calendar connected.");

        await SyncAsync(ct);
        return true;
    }

    /// <summary>Forget tokens and stop syncing (remote data untouched).</summary>
    public void Disconnect()
    {
        _settings.Set(RefreshTokenKey, null);
        _settings.Set(SyncTokenKey, null);
        _settings.SetBool(ConnectedKey, false);
        _accessToken = null;
        LastSyncStatus = "Disconnected.";
    }

    // ------------------------------------------------------------------ sync

    /// <summary>Debounced background sync — call after any local event change.</summary>
    public void RequestSync()
    {
        if (!IsConnected)
            return;
        _debounce?.Dispose();
        _debounce = new Timer(_ => _ = SyncSafeAsync(), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
    }

    private async Task SyncSafeAsync()
    {
        try { await SyncAsync(CancellationToken.None); }
        catch (Exception ex) { _log.LogWarning(ex, "Background Google sync failed."); }
    }

    /// <summary>Full two-way sync. Serialized — concurrent calls queue up.</summary>
    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            LastSyncStatus = "Not connected.";
            return false;
        }

        await _gate.WaitAsync(ct);
        try
        {
            var token = await GetAccessTokenAsync(ct);
            if (token is null)
            {
                LastSyncStatus = "Google sign-in expired — connect again in Settings.";
                return false;
            }

            var pushed = await PushAsync(token, ct);
            var (pulled, ok) = await PullAsync(token, ct);
            if (!ok)
                return false;

            LastSyncStatus = $"Synced {DateTime.Now:h:mm tt} — {pushed} pushed, {pulled} pulled.";
            _log.LogInformation("Google sync done: {Pushed} pushed, {Pulled} pulled.", pushed, pulled);
            SyncCompleted?.Invoke();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> PushAsync(string token, CancellationToken ct)
    {
        var pushed = 0;

        // 1. Remote deletes for locally-deleted linked events.
        foreach (var gid in _events.GetPendingGoogleDeletes())
        {
            using var req = Authed(HttpMethod.Delete, $"{ApiBase}/{Uri.EscapeDataString(gid)}", token);
            using var res = await Http.SendAsync(req, ct);
            // Success, already gone, or no longer accessible — all mean "done".
            if (res.IsSuccessStatusCode
                || res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.Forbidden)
            {
                _events.ClearPendingGoogleDelete(gid);
                pushed++;
            }
        }

        // 2. Inserts + updates.
        foreach (var e in _events.GetAll())
        {
            ct.ThrowIfCancellationRequested();
            if (e.GoogleId is null)
            {
                using var req = Authed(HttpMethod.Post, ApiBase, token);
                req.Content = JsonContent.Create(ToGoogle(e));
                using var res = await Http.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                    var gid = doc.RootElement.GetProperty("id").GetString()!;
                    var updated = ParseStamp(doc.RootElement, "updated") ?? DateTime.UtcNow;
                    _events.LinkGoogle(e.Id, gid, updated);
                    pushed++;
                }
                else
                {
                    _log.LogWarning("Google insert failed for event {Id}: {Status}", e.Id, res.StatusCode);
                }
            }
            else if (e.GoogleUpdatedUtc is DateTime baseline && e.UpdatedUtc > baseline.AddSeconds(2))
            {
                using var req = Authed(HttpMethod.Patch, $"{ApiBase}/{Uri.EscapeDataString(e.GoogleId)}", token);
                req.Content = JsonContent.Create(ToGoogle(e));
                using var res = await Http.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                    var updated = ParseStamp(doc.RootElement, "updated") ?? DateTime.UtcNow;
                    _events.LinkGoogle(e.Id, e.GoogleId, updated);
                    pushed++;
                }
                else if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // Deleted on Google; the pull pass (cancelled item) will remove it locally.
                }
                else
                {
                    _log.LogWarning("Google update failed for event {Id}: {Status}", e.Id, res.StatusCode);
                }
            }
        }
        return pushed;
    }

    private async Task<(int pulled, bool ok)> PullAsync(string token, CancellationToken ct)
    {
        var pulled = 0;
        var syncToken = _settings.Get(SyncTokenKey);
        string? pageToken = null;
        string? nextSyncToken = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var url = ApiBase + "?maxResults=250&showDeleted=true";
            if (pageToken is not null)
                url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            else if (!string.IsNullOrEmpty(syncToken))
                url += "&syncToken=" + Uri.EscapeDataString(syncToken);
            else
                // First pull: a 1-year-back window keeps the initial import sane.
                url += "&timeMin=" + Uri.EscapeDataString(DateTime.UtcNow.AddYears(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

            using var req = Authed(HttpMethod.Get, url, token);
            using var res = await Http.SendAsync(req, ct);

            if (res.StatusCode == HttpStatusCode.Gone)
            {
                // syncToken expired — clear and do a full pull next time around.
                _settings.Set(SyncTokenKey, null);
                syncToken = null;
                pageToken = null;
                continue;
            }
            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("Google pull failed: {Status}", res.StatusCode);
                LastSyncStatus = $"Google sync failed ({(int)res.StatusCode}).";
                return (pulled, false);
            }

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            if (root.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (ApplyRemote(item))
                        pulled++;
                }
            }

            if (root.TryGetProperty("nextPageToken", out var npt))
            {
                pageToken = npt.GetString();
                continue;
            }
            nextSyncToken = root.TryGetProperty("nextSyncToken", out var nst) ? nst.GetString() : null;
            break;
        }

        if (nextSyncToken is not null)
            _settings.Set(SyncTokenKey, nextSyncToken);
        return (pulled, true);
    }

    /// <summary>Upsert/delete one remote item locally. Returns true when something changed.</summary>
    private bool ApplyRemote(JsonElement item)
    {
        var gid = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (gid is null)
            return false;

        var status = item.TryGetProperty("status", out var st) ? st.GetString() : "confirmed";
        var local = _events.GetByGoogleId(gid);

        if (status == "cancelled")
        {
            if (local is null)
                return false;
            if (local.ReminderId is long rid)
                _reminders.Delete(rid);
            _events.Delete(local.Id);
            return true;
        }

        // Skip exception instances of recurring events (they carry recurringEventId);
        // the local model has no per-occurrence overrides — the master event covers it.
        if (item.TryGetProperty("recurringEventId", out _))
            return false;

        var updated = ParseStamp(item, "updated") ?? DateTime.UtcNow;
        if (local is not null)
        {
            var baseline = local.GoogleUpdatedUtc ?? DateTime.MinValue;
            var remoteChanged = updated > baseline.AddSeconds(2);
            var localChanged = local.UpdatedUtc > baseline.AddSeconds(2);
            if (!remoteChanged)
                return false;
            if (localChanged && local.UpdatedUtc >= updated)
                return false;   // local is newer — the push pass sends it; don't clobber

            MapGoogleInto(item, local);
            local.GoogleUpdatedUtc = updated;
            local.UpdatedUtc = DateTime.UtcNow;
            _events.Update(local);
            return true;
        }

        var e = new CalendarEvent { GoogleId = gid, GoogleUpdatedUtc = updated };
        MapGoogleInto(item, e);
        e.CreatedUtc = DateTime.UtcNow;
        e.UpdatedUtc = DateTime.UtcNow;
        _events.Insert(e);
        return true;
    }

    // ------------------------------------------------------------------ mapping

    private static void MapGoogleInto(JsonElement item, CalendarEvent e)
    {
        e.Title = item.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
        e.Location = item.TryGetProperty("location", out var l) ? l.GetString() : null;
        e.Notes = item.TryGetProperty("description", out var d) ? d.GetString() : null;

        var (startUtc, allDayStart) = ParseGoogleTime(item, "start");
        var (endUtc, _) = ParseGoogleTime(item, "end");
        e.AllDay = allDayStart;
        e.StartUtc = startUtc ?? DateTime.UtcNow;
        if (endUtc is DateTime end)
        {
            // Google all-day "end" is EXCLUSIVE (next day's date) — convert to inclusive.
            if (allDayStart)
                end = end.AddDays(-1);
            e.EndUtc = end > e.StartUtc ? end : null;
        }
        else
        {
            e.EndUtc = null;
        }

        e.Recurrence = EventRecurrence.None;
        if (item.TryGetProperty("recurrence", out var rec) && rec.ValueKind == JsonValueKind.Array)
        {
            foreach (var rule in rec.EnumerateArray())
            {
                var r = rule.GetString() ?? "";
                if (!r.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (r.Contains("FREQ=DAILY", StringComparison.OrdinalIgnoreCase)) e.Recurrence = EventRecurrence.Daily;
                else if (r.Contains("FREQ=WEEKLY", StringComparison.OrdinalIgnoreCase)) e.Recurrence = EventRecurrence.Weekly;
                else if (r.Contains("FREQ=MONTHLY", StringComparison.OrdinalIgnoreCase)) e.Recurrence = EventRecurrence.Monthly;
                else if (r.Contains("FREQ=YEARLY", StringComparison.OrdinalIgnoreCase)) e.Recurrence = EventRecurrence.Yearly;
            }
        }

        // Meeting link: explicit hangoutLink, else a conference entry point.
        e.MeetingUrl = item.TryGetProperty("hangoutLink", out var hl) ? hl.GetString() : e.MeetingUrl;
        if (string.IsNullOrEmpty(e.MeetingUrl)
            && item.TryGetProperty("conferenceData", out var cd)
            && cd.TryGetProperty("entryPoints", out var eps)
            && eps.ValueKind == JsonValueKind.Array)
        {
            foreach (var ep in eps.EnumerateArray())
            {
                if (ep.TryGetProperty("entryPointType", out var t) && t.GetString() == "video"
                    && ep.TryGetProperty("uri", out var uri))
                {
                    e.MeetingUrl = uri.GetString();
                    break;
                }
            }
        }

        // Alert: first popup override.
        e.AlertMinutes = null;
        if (item.TryGetProperty("reminders", out var rem)
            && rem.TryGetProperty("overrides", out var ovr)
            && ovr.ValueKind == JsonValueKind.Array)
        {
            foreach (var o in ovr.EnumerateArray())
            {
                if (o.TryGetProperty("method", out var m) && m.GetString() == "popup"
                    && o.TryGetProperty("minutes", out var min))
                {
                    e.AlertMinutes = min.GetInt32();
                    break;
                }
            }
        }
    }

    private object ToGoogle(CalendarEvent e)
    {
        var localStart = DateTime.SpecifyKind(e.StartUtc, DateTimeKind.Utc).ToLocalTime();
        // Local EndUtc is inclusive; Google's all-day end is exclusive. Timed events with no
        // end get a 30-minute default (Google requires an end).
        var endUtc = e.EndUtc ?? (e.AllDay ? e.StartUtc : e.StartUtc.AddMinutes(30));
        var localEnd = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc).ToLocalTime();

        object start, end;
        if (e.AllDay)
        {
            start = new { date = localStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            end = new { date = localEnd.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }
        else
        {
            if (localEnd <= localStart)
                localEnd = localStart.AddMinutes(30);
            // RFC3339 with the local UTC offset, plus the IANA zone — Google REQUIRES timeZone
            // on recurring events, and it must be IANA ("America/Chicago"), not the Windows id.
            TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana);
            start = new
            {
                dateTime = localStart.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                timeZone = iana ?? "UTC",
            };
            end = new
            {
                dateTime = localEnd.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
                timeZone = iana ?? "UTC",
            };
        }

        string[]? recurrence = e.Recurrence switch
        {
            EventRecurrence.Daily => ["RRULE:FREQ=DAILY"],
            EventRecurrence.Weekly => ["RRULE:FREQ=WEEKLY"],
            EventRecurrence.Monthly => ["RRULE:FREQ=MONTHLY"],
            EventRecurrence.Yearly => ["RRULE:FREQ=YEARLY"],
            _ => null,
        };

        return new
        {
            summary = string.IsNullOrWhiteSpace(e.Title) ? "(no title)" : e.Title,
            location = e.Location,
            description = e.Notes,
            start,
            end,
            recurrence,
            reminders = e.AlertMinutes is int min
                ? (object)new { useDefault = false, overrides = new[] { new { method = "popup", minutes = min } } }
                : new { useDefault = true },
        };
    }

    private static (DateTime? utc, bool allDay) ParseGoogleTime(JsonElement item, string prop)
    {
        if (!item.TryGetProperty(prop, out var t))
            return (null, false);
        if (t.TryGetProperty("dateTime", out var dt) && dt.GetString() is string sdt)
            return (DateTime.Parse(sdt, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), false);
        if (t.TryGetProperty("date", out var d) && d.GetString() is string sd)
        {
            // All-day date = local midnight of that date.
            var local = DateTime.ParseExact(sd, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return (DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(), true);
        }
        return (null, false);
    }

    private static DateTime? ParseStamp(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var p) && p.GetString() is string s
            ? DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
            : null;

    // ------------------------------------------------------------------ tokens

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpiresUtc)
            return _accessToken;

        var refresh = LoadRefreshToken();
        if (refresh is null)
            return null;

        using var res = await Http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["refresh_token"] = refresh,
            ["client_id"] = _cfg.GoogleClientId,
            ["client_secret"] = _cfg.GoogleClientSecret,
            ["grant_type"] = "refresh_token",
        }), ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Google token refresh failed: {Status}", res.StatusCode);
            if (res.StatusCode == HttpStatusCode.BadRequest)
            {
                // Refresh token revoked — force a fresh sign-in.
                _settings.SetBool(ConnectedKey, false);
            }
            return null;
        }

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        _accessToken = doc.RootElement.GetProperty("access_token").GetString();
        _accessTokenExpiresUtc = DateTime.UtcNow.AddSeconds(
            doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() - 60 : 3000);
        return _accessToken;
    }

    private void StoreRefreshToken(string refresh)
    {
        // CacheNote only runs on Windows (WinUI shell) — DPAPI is always available.
#pragma warning disable CA1416
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(refresh), null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
        _settings.Set(RefreshTokenKey, Convert.ToBase64String(cipher));
    }

    private string? LoadRefreshToken()
    {
        var b64 = _settings.Get(RefreshTokenKey);
        if (string.IsNullOrEmpty(b64))
            return null;
        try
        {
#pragma warning disable CA1416
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;   // different user/machine — token unrecoverable
        }
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
