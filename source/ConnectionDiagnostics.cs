using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public static class ConnectionDiagnostics
    {
        public static string Category(Exception error)
        {
            for (Exception e = error; e != null; e = e.InnerException)
            {
                var socket = e as SocketException;
                if (socket != null)
                {
                    if (socket.SocketErrorCode == SocketError.AccessDenied) return "network_access_blocked";
                    if (socket.SocketErrorCode == SocketError.HostNotFound || socket.SocketErrorCode == SocketError.TryAgain || socket.SocketErrorCode == SocketError.NoData) return "dns_failure";
                    if (socket.SocketErrorCode == SocketError.TimedOut) return "timeout";
                    if (socket.SocketErrorCode == SocketError.ConnectionRefused) return "connection_refused";
                    if (socket.SocketErrorCode == SocketError.NetworkUnreachable || socket.SocketErrorCode == SocketError.HostUnreachable || socket.SocketErrorCode == SocketError.NetworkDown) return "network_unreachable";
                }
                if (e is AuthenticationException) return "tls_failure";
                var web = e as WebException;
                if (web != null)
                {
                    if (web.Status == WebExceptionStatus.TrustFailure || web.Status == WebExceptionStatus.SecureChannelFailure) return "tls_failure";
                    if (web.Status == WebExceptionStatus.NameResolutionFailure || web.Status == WebExceptionStatus.ProxyNameResolutionFailure) return "dns_failure";
                    if (web.Status == WebExceptionStatus.Timeout) return "timeout";
                }
            }
            return error is TaskCanceledException ? "timeout" : "connection_failure";
        }
        public static int NativeCode(Exception error)
        {
            for (Exception e = error; e != null; e = e.InnerException)
            {
                var native = e as Win32Exception;
                if (native != null) return native.NativeErrorCode;
            }
            return 0;
        }
        public static string UserMessage(Exception error)
        {
            string category = Category(error);
            if (category == "network_access_blocked") return "Windows blocked this app's network access (10013). Close this window and open EeroDesktop.exe directly from File Explorer, then try again.";
            if (category == "dns_failure") return "Windows could not resolve api-user.e2ro.com. Check your DNS or VPN connection and try again.";
            if (category == "tls_failure") return "Windows could not establish a trusted TLS connection to eero. Check the PC's date, time, and certificate configuration.";
            if (category == "timeout") return "The connection to eero timed out. Try again in a moment.";
            if (category == "connection_refused") return "The connection to eero was refused. Check whether your proxy or network filter is blocking this app.";
            if (category == "network_unreachable") return "Windows reports that eero's network is unreachable. Check your internet or VPN connection.";
            int code = NativeCode(error);
            return "The connection to eero failed" + (code == 0 ? "" : " (Windows error " + code + ")") + ". Use Check connection for details, or restart the app directly from File Explorer.";
        }
        public static int Run(string output)
        {
            try
            {
                using (var api = new EeroApi(new SessionStore(Path.Combine(Path.GetTempPath(), "unused-eero-probe.dat"))))
                    api.CheckConnection().GetAwaiter().GetResult();
                File.WriteAllText(output, J.Stringify(J.Obj("ok", true, "endpoint", EeroApi.Origin, "result", "HTTPS connection succeeded; eero returned the expected unauthenticated response. No login or verification-code request was made.")));
                return 0;
            }
            catch (Exception e)
            {
                File.WriteAllText(output, J.Stringify(J.Obj("ok", false, "category", Category(e), "native_code", NativeCode(e), "exception_type", e.GetType().Name, "message", UserMessage(e))));
                return 1;
            }
        }
    }
}
