using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    // The service streams concatenated JSON objects, not Server-Sent Events.
    public sealed class JsonObjectStream
    {
        private readonly StringBuilder buffer = new StringBuilder();
        private int depth;
        private bool quoted, escaped;
        public void Feed(char[] chars, int count, Action<object> receive)
        {
            for (int i = 0; i < count; i++)
            {
                char c = chars[i];
                if (buffer.Length == 0 && Char.IsWhiteSpace(c)) continue;
                if (buffer.Length == 0 && c != '{') throw new InvalidDataException("The chat service returned an unsupported stream.");
                buffer.Append(c);
                if (buffer.Length > 1024 * 1024) throw new InvalidDataException("A chat response exceeded the size limit.");
                if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; }
                else if (c == '"') quoted = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                if (depth == 0) { object value = J.Parse(buffer.ToString()); buffer.Clear(); receive(value); }
            }
        }
        public void Finish() { if (buffer.Length != 0) throw new EndOfStreamException("The chat response was interrupted. Reload history before sending again."); }
    }
    public static class ConversationContracts
    {
        public static object Request(string networkId, string conversationId, string message, string mcpToken = null, object permission = null, object uiEvent = null)
        {
            if (String.IsNullOrWhiteSpace(networkId)) throw new ArgumentException("Select a network before opening chat.");
            if (message != null && (String.IsNullOrWhiteSpace(message) || message.Length > 8000)) throw new ArgumentException("Enter a message of 1–8,000 characters.");
            var body = J.Obj("context", J.Obj("focused_network_id", networkId, "client_capabilities", J.Obj("opt_in_widgets", new string[0])));
            if (!String.IsNullOrEmpty(conversationId)) body["conversation_id"] = conversationId;
            if (message != null) body["message"] = message;
            if (!String.IsNullOrEmpty(mcpToken)) body["mcp_token"] = mcpToken;
            if (permission != null) body["permission_grant"] = permission;
            if (uiEvent != null) body["ui_event"] = uiEvent;
            return body;
        }
        public static string Error(Exception error)
        {
            var api = error as ApiException;
            if (api != null && api.ApiError == "error.conversations.not_available") return "eero has not enabled chat for this account or network.";
            if (api != null && api.ApiError == "error.conversations.user_blocked") return "eero has restricted chat access for this account.";
            return MainWindow.Message(error);
        }
    }
    public sealed partial class EeroApi
    {
        public async Task StreamConversation(object body, Action<object> receive, CancellationToken cancellation)
        {
            HttpResponseMessage response = null;
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (!SignedIn) throw new ApiException(401, "session.missing", true);
                // Retry only an explicit session-refresh rejection before a stream starts.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    bool refresh = false;
                    using (var request = new HttpRequestMessage(HttpMethod.Put, Resource("/2.2/conversations/messages")))
                    {
                        request.Headers.Add("Cookie", "s=" + ValidToken(token));
                        request.Content = new StringContent(J.Stringify(body), Encoding.UTF8, "application/json");
                        response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                object envelope = null;
                                try { envelope = J.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)); } catch (ArgumentException) { }
                                var failure = new ApiException((int)response.StatusCode, J.S(envelope, "meta", "error"), true);
                                refresh = attempt == 0 && failure.Code == 401 && failure.ApiError == "error.session.refresh";
                                if (!refresh) throw failure;
                            }
                            else
                            {
                                IEnumerable<string> cookies;
                                if (response.Headers.TryGetValues("Set-Cookie", out cookies)) foreach (string value in cookies)
                                {
                                    var match = System.Text.RegularExpressions.Regex.Match(value, @"(?:^|,\s*)s=([^; ,]+)");
                                    if (match.Success) Commit(match.Groups[1].Value);
                                }
                                break;
                            }
                        }
                    }
                    if (refresh)
                    {
                        response.Dispose(); response = null;
                        Reply fresh = await Raw("POST", "login/refresh", null, token, false, true).ConfigureAwait(false);
                        Commit(fresh.Cookie ?? J.S(fresh.Data, "user_token").NullIfEmpty() ?? token);
                    }
                }
            }
            catch { if (response != null) response.Dispose(); throw; }
            finally { gate.Release(); }
            // Keep normal refresh, switching and logout available while reading a long reply.
            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (cancellation.Register(delegate { stream.Dispose(); }))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
            {
                var parser = new JsonObjectStream(); var chars = new char[2048]; int count, total = 0;
                while ((count = await reader.ReadAsync(chars, 0, chars.Length).ConfigureAwait(false)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested(); total += count;
                    if (total > 8 * 1024 * 1024) throw new InvalidDataException("The chat response exceeded the size limit.");
                    parser.Feed(chars, count, value => { cancellation.ThrowIfCancellationRequested(); receive(value); });
                }
                cancellation.ThrowIfCancellationRequested(); parser.Finish();
            }
        }
    }
}
