using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public sealed class VpnCleanupException : Exception
    { public VpnCleanupException() : base("VPN startup failed and automatic disconnect failed. Open the WireGuard app to deactivate the eero tunnel.") { } }
    internal static class VpnTunnelLifecycle
    {
        public static void Start(Action install, Func<bool> running, Func<bool> handshake, Action stop, Action confirmed, int attempts, Action wait)
        {
            bool installed = false;
            try
            {
                install(); installed = true;
                for (int i = 0; i < attempts; i++) { wait(); if (handshake()) { confirmed(); return; } }
                throw new InvalidOperationException("Guardian did not complete a VPN handshake. The tunnel was removed to restore your connection.");
            }
            catch
            {
                if (installed || running()) { try { stop(); } catch (Exception) { throw new VpnCleanupException(); } }
                throw;
            }
        }
    }
    public static class WindowsVpn
    {
        public static string UserDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EeroDesktop", "VPN"); } }
        private static string MachineDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EeroDesktop-VPN"); } }
        public static string Tunnel(string scope) { if (!Regex.IsMatch(scope ?? "", "^[a-f0-9]{24}$")) throw new ArgumentException("Invalid VPN profile."); return "Eero-" + scope; }
        private static string Binary(string name)
        {
            string root = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string path = Path.Combine(root, "WireGuard", name);
            if (!File.Exists(path)) throw new FileNotFoundException("Install the Windows VPN component first using the button on the VPN page.");
            VendorSignature.Verify(path); return path;
        }
        public static bool Installed { get { try { Binary("wireguard.exe"); Binary("wg.exe"); return true; } catch (Exception) { return false; } } }
        public static string Status(string scope)
        {
            try { using (var service = new ServiceController("WireGuardTunnel$" + Tunnel(scope))) return service.Status == ServiceControllerStatus.Running ? "Windows tunnel is running" : "Windows tunnel: " + service.Status; }
            catch (InvalidOperationException) { return "Disconnected"; }
        }
        public static bool Running(string scope) { return Status(scope) != "Disconnected"; }
        public static string[] OwnActiveScopes()
        {
            if (!Directory.Exists(UserDirectory)) return new string[0];
            return System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(System.Linq.Enumerable.Select(Directory.GetFiles(UserDirectory, "*.dat"), Path.GetFileNameWithoutExtension), scope => Regex.IsMatch(scope, "^[a-f0-9]{24}$") && Running(scope)));
        }
        public static bool AnotherTunnel(string scope)
        {
            foreach (var service in ServiceController.GetServices()) using (service)
                if (service.ServiceName.StartsWith("WireGuardTunnel$Eero-", StringComparison.Ordinal) && service.ServiceName != "WireGuardTunnel$" + Tunnel(scope) && service.Status != ServiceControllerStatus.Stopped) return true;
            return false;
        }
        public static string[] KeyPair()
        {
            string wg = Binary("wg.exe");
            string privateKey = VpnContracts.Key(Command(wg, "genkey", null));
            string publicKey = VpnContracts.Key(Command(wg, "pubkey", privateKey + "\n"));
            return new[] { privateKey, publicKey };
        }
        private static string Command(string exe, string arguments, string input)
        {
            using (var process = new Process { StartInfo = new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden } })
            {
                process.Start(); if (input != null) process.StandardInput.Write(input); process.StandardInput.Close();
                Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(12000)) { process.Kill(); throw new System.TimeoutException("The Windows VPN component did not respond."); }
                Task.WaitAll(output, error);
                if (process.ExitCode != 0) throw new InvalidOperationException("The Windows VPN component returned error " + process.ExitCode + ".");
                if (output.Result.Length > 8192) throw new InvalidDataException("The Windows VPN component returned an unexpected response.");
                return output.Result.Trim();
            }
        }
        public static async Task InstallComponent()
        {
            PrivateUserDirectory();
            string installer = Path.Combine(UserDirectory, "wireguard-installer-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                using (var client = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }))
                {
                    client.Timeout = TimeSpan.FromMinutes(2);
                    using (var response = await client.GetAsync("https://download.wireguard.com/windows-client/wireguard-installer.exe", System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        using (var source = await response.Content.ReadAsStreamAsync()) using (var target = File.Create(installer))
                        {
                            byte[] buffer = new byte[32768]; int count, total = 0;
                            while ((count = await source.ReadAsync(buffer, 0, buffer.Length)) > 0) { total += count; if (total > 16 * 1024 * 1024) throw new InvalidDataException("The installer exceeded the download limit."); await target.WriteAsync(buffer, 0, count); }
                        }
                    }
                }
                VendorSignature.Verify(installer);
                using (var process = Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true, Verb = "runas" })) await Task.Run(delegate { process.WaitForExit(); });
                if (!Installed) throw new InvalidOperationException("WireGuard installation was not completed. Install it before connecting.");
            }
            finally { if (File.Exists(installer)) File.Delete(installer); }
        }
        private static void PrivateUserDirectory()
        {
            Directory.CreateDirectory(UserDirectory);
            if ((File.GetAttributes(UserDirectory) & FileAttributes.ReparsePoint) != 0) throw new IOException("The VPN data directory cannot be a link.");
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(UserDirectory, security);
        }
        public static async Task<object> Control(string action, string scope, object profile = null)
        {
            if (action != "connect" && action != "disconnect") throw new ArgumentException("Unsupported VPN action.");
            Tunnel(scope); if (profile != null) VpnContracts.Configuration(profile);
            Binary("wireguard.exe"); PrivateUserDirectory();
            string id = Guid.NewGuid().ToString("N"), file = Path.Combine(UserDirectory, id + ".request");
            var store = new SessionStore(file);
            store.Save(J.Stringify(J.Obj("action", action, "scope", scope, "created", DateTime.UtcNow.ToString("o"), "profile", profile)));
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                using (var process = Process.Start(new ProcessStartInfo(exe, "--vpn-control " + id) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })) await Task.Run(delegate { process.WaitForExit(); });
                string resultFile = Path.Combine(UserDirectory, id + ".result");
                if (!File.Exists(resultFile)) throw new InvalidOperationException("Windows VPN permission was cancelled or the helper did not complete.");
                object result = J.Parse(new SessionStore(resultFile).Load()); File.Delete(resultFile);
                if (!J.B(result, "ok")) throw new InvalidOperationException(J.S(result, "message"));
                return result;
            }
            finally { if (File.Exists(file)) File.Delete(file); }
        }
        // Runs only after the user presses Connect/Disconnect and approves Windows UAC.
        public static int ElevatedControl(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$")) return 2;
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
            string resultFile = Path.Combine(UserDirectory, id + ".result");
            try
            {
                string requestFile = Path.Combine(UserDirectory, id + ".request");
                if (new FileInfo(requestFile).Length > 32768) throw new InvalidDataException("Invalid VPN request size.");
                object request = J.Parse(new SessionStore(requestFile).Load());
                DateTime created;
                if (!DateTime.TryParse(J.S(request, "created"), null, System.Globalization.DateTimeStyles.RoundtripKind, out created) || Math.Abs((DateTime.UtcNow - created.ToUniversalTime()).TotalMinutes) > 10) throw new InvalidOperationException("The VPN request expired. Try again.");
                string scope = J.S(request, "scope"), name = Tunnel(scope), action = J.S(request, "action");
                if (action != "connect" && action != "disconnect") throw new InvalidDataException("Unsupported VPN action.");
                using (var mutex = new Mutex(false, "Global\\EeroDesktop-VPN-control"))
                {
                    bool acquired = false;
                    try
                    {
                        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                        if (!acquired) throw new InvalidOperationException("Another VPN action is in progress.");
                        string exe = Binary("wireguard.exe");
                        if (action == "disconnect")
                        {
                            if (Running(scope)) Command(exe, "/uninstalltunnelservice " + name, null);
                            string old = Path.Combine(MachineDirectory, name + ".conf.dpapi");
                            if (Directory.Exists(MachineDirectory)) { SecureMachineDirectory(); if (File.Exists(old)) File.Delete(old); }
                            new SessionStore(resultFile).Save(J.Stringify(J.Obj("ok", true, "message", "VPN disconnected."))); return 0;
                        }
                        if (Running(scope)) throw new InvalidOperationException("Disconnect the existing Windows tunnel before reconnecting or changing region.");
                        if (AnotherTunnel(scope)) throw new InvalidOperationException("Another eero Windows VPN is active. Disconnect it before connecting this network's VPN.");
                        SecureMachineDirectory();
                        string configuration = VpnContracts.Configuration(J.Get(request, "profile"));
                        string path = Path.Combine(MachineDirectory, name + ".conf.dpapi");
                        byte[] clear = Encoding.UTF8.GetBytes(configuration);
                        try { File.WriteAllBytes(path, ProtectMachine(clear, name)); }
                        finally { Array.Clear(clear, 0, clear.Length); }
                        try
                        {
                            VpnTunnelLifecycle.Start(delegate { Command(exe, "/installtunnelservice \"" + path + "\"", null); }, () => Running(scope), delegate
                            {
                                string handshakes;
                                try { handshakes = Command(Binary("wg.exe"), "show " + name + " latest-handshakes", null); }
                                catch (InvalidOperationException) { return false; } // The service may still be creating its interface.
                                foreach (string line in handshakes.Split('\n'))
                                {
                                    string[] parts = line.Trim().Split('\t'); long epoch;
                                    if (parts.Length == 2 && Int64.TryParse(parts[1], out epoch) && epoch > 0 && Math.Abs((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds - epoch) < 180) return true;
                                }
                                return false;
                            }, delegate { Command(exe, "/uninstalltunnelservice " + name, null); }, delegate
                            { new SessionStore(resultFile).Save(J.Stringify(J.Obj("ok", true, "message", "Connected to Guardian. WireGuard handshake verified.", "verified_utc", DateTime.UtcNow.ToString("o")))); }, 15, delegate { Thread.Sleep(1000); });
                            return 0;
                        }
                        catch (VpnCleanupException) { throw; }
                        catch
                        {
                            if (File.Exists(path)) File.Delete(path); throw;
                        }
                    }
                    finally { if (acquired) mutex.ReleaseMutex(); }
                }
            }
            catch (Exception e)
            {
                try { new SessionStore(resultFile).Save(J.Stringify(J.Obj("ok", false, "message", MainWindow.Message(e)))); } catch (Exception) { }
                return 1;
            }
        }
        private static void SecureMachineDirectory()
        {
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var security = new DirectorySecurity(); security.SetOwner(admins); security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { admins, system }) security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            if (Directory.Exists(MachineDirectory))
            {
                if ((File.GetAttributes(MachineDirectory) & FileAttributes.ReparsePoint) != 0) throw new IOException("The system VPN directory cannot be a link.");
                var owner = Directory.GetAccessControl(MachineDirectory).GetOwner(typeof(SecurityIdentifier));
                if (!owner.Equals(admins) && !owner.Equals(system)) throw new IOException("The system VPN directory has an unexpected owner.");
                Directory.SetAccessControl(MachineDirectory, security);
            }
            else Directory.CreateDirectory(MachineDirectory, security);
        }
        [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
        [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr value);
        internal static byte[] ProtectMachine(byte[] data, string name)
        {
            var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) }; Blob output = new Blob();
            try
            {
                Marshal.Copy(data, 0, input.Data, data.Length);
                if (!CryptProtectData(ref input, name, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 5, out output)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
            }
            finally { Marshal.Copy(new byte[data.Length], 0, input.Data, data.Length); Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); }
        }
    }
    public static class VendorSignature
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileData { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File, Subject; }
        [StructLayout(LayoutKind.Sequential)] private struct TrustData { public uint Size; public IntPtr Policy, Client; public uint UI, Revocation, Choice; public IntPtr File; public uint Action; public IntPtr State; public IntPtr Url; public uint Flags, Context; }
        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
        public static void Verify(string path)
        {
            var file = new FileData { Size = (uint)Marshal.SizeOf(typeof(FileData)), Path = path };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileData)));
            try
            {
                Marshal.StructureToPtr(file, pointer, false);
                var data = new TrustData { Size = (uint)Marshal.SizeOf(typeof(TrustData)), UI = 2, Choice = 1, File = pointer, Action = 1, Flags = 0x1000 };
                var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
                int result;
                try { result = WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
                finally { data.Action = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
                if (result != 0) throw new InvalidDataException("Windows could not verify the VPN component's digital signature.");
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    if (certificate.GetNameInfo(X509NameType.SimpleName, false) != "WireGuard LLC") throw new InvalidDataException("The VPN component is not signed by WireGuard LLC.");
            }
            finally { Marshal.DestroyStructure(pointer, typeof(FileData)); Marshal.FreeHGlobal(pointer); }
        }
    }
}
