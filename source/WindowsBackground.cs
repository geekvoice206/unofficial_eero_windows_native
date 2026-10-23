using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace EeroDesktop
{
    public interface IStartupStore { string Read(); void Write(string command); }
    public sealed class WindowsStartupStore : IStartupStore
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public string Read() { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key == null ? null : key.GetValue("EeroDesktop") as string; }
        public void Write(string command)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Key)) { if (command == null) key.DeleteValue("EeroDesktop", false); else key.SetValue("EeroDesktop", command, RegistryValueKind.String); }
        }
    }
    public static class StartupRegistration
    {
        public static string Command(string executable)
        {
            if (String.IsNullOrWhiteSpace(executable) || executable.Any(c => c == '"' || Char.IsControl(c)) || !Path.IsPathRooted(executable) || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an absolute Windows executable path for startup.");
            return "\"" + Path.GetFullPath(executable) + "\" --background";
        }
        public static void Apply(IStartupStore store, string executable, bool enabled, Action savePreferences)
        {
            string desired = enabled ? Command(executable) : null, previous = store.Read();
            store.Write(desired);
            try { savePreferences(); }
            catch { store.Write(previous); throw; }
        }
    }
    public sealed class AppInstance : IDisposable
    {
        private readonly Mutex mutex; private readonly EventWaitHandle wake;
        private RegisteredWaitHandle listener;
        public readonly bool IsOwner;
        public static string UserScope
        {
            get { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User.Value))).Replace("-", ""); }
        }
        public AppInstance(string scope)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(scope ?? "", "^[A-Za-z0-9_-]{1,100}$")) throw new ArgumentException("Invalid application instance scope.");
            var user = WindowsIdentity.GetCurrent().User; bool created;
            var mutexAccess = new MutexSecurity(); mutexAccess.AddAccessRule(new MutexAccessRule(user, MutexRights.FullControl, AccessControlType.Allow));
            var eventAccess = new EventWaitHandleSecurity(); eventAccess.AddAccessRule(new EventWaitHandleAccessRule(user, EventWaitHandleRights.FullControl, AccessControlType.Allow));
            string name = @"Local\EeroDesktop-" + scope;
            mutex = new Mutex(false, name + "-instance", out created, mutexAccess);
            try { IsOwner = mutex.WaitOne(0); } catch (AbandonedMutexException) { IsOwner = true; }
            wake = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-show", out created, eventAccess);
        }
        public void Listen(Action show) { if (!IsOwner) throw new InvalidOperationException("This process does not own the application window."); listener = ThreadPool.RegisterWaitForSingleObject(wake, delegate { show(); }, null, Timeout.Infinite, false); }
        public void ShowOwner() { wake.Set(); }
        public void Dispose() { if (listener != null) listener.Unregister(null); wake.Dispose(); if (IsOwner) mutex.ReleaseMutex(); mutex.Dispose(); }
    }
    public sealed class NotificationJournal
    {
        private readonly string path;
        private readonly Dictionary<string, List<string>> seen = new Dictionary<string, List<string>>();
        private static readonly byte[] entropy = Encoding.UTF8.GetBytes("EeroDesktop.NotificationJournal.v1");
        public NotificationJournal(string file)
        {
            path = file;
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 16 * 1024 * 1024) return;
                byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(file), entropy, DataProtectionScope.CurrentUser);
                try
                {
                    var data = J.Parse(Encoding.UTF8.GetString(clear)) as IDictionary<string, object>;
                    if (data != null) foreach (var item in data.Take(64)) if (item.Key.Length == 64) seen[item.Key] = J.List(item.Value).Select(x => J.S(x)).Where(ValidId).Distinct().Take(256).ToList();
                }
                finally { Array.Clear(clear, 0, clear.Length); }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (CryptographicException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
        }
        private static bool ValidId(string id) { return id.Length > 0 && id.Length <= 256 && !id.Any(Char.IsControl); }
        private static string Scope(string account, string network)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(account + "\n" + EeroApi.Resource(network).AbsoluteUri))).Replace("-", "");
        }
        public List<object> Observe(string account, string network, IEnumerable<object> newestFirst)
        {
            string scope = Scope(account, network); List<string> ids; bool seeded = seen.TryGetValue(scope, out ids);
            if (!seeded) { ids = new List<string>(); seen[scope] = ids; }
            var result = new List<object>();
            foreach (object item in newestFirst.Take(50).Reverse())
            {
                string id = J.First(item, "uuid", "id"); if (!ValidId(id) || ids.Contains(id)) continue;
                ids.Add(id); if (seeded) result.Add(item);
            }
            if (ids.Count > 256) ids.RemoveRange(0, ids.Count - 256);
            while (seen.Count > 64) seen.Remove(seen.Keys.First(k => k != scope));
            return result;
        }
        public void Save()
        {
            byte[] clear = Encoding.UTF8.GetBytes(J.Stringify(seen)), encrypted;
            try { encrypted = ProtectedData.Protect(clear, entropy, DataProtectionScope.CurrentUser); }
            finally { Array.Clear(clear, 0, clear.Length); }
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, encrypted); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
    public sealed partial class MainWindow
    {
        private NotificationJournal notificationJournal;
        private bool backgroundLaunch;
        internal void RestoreWindow()
        {
            backgroundLaunch = false;
            ShowInTaskbar = true; Show(); WindowState = WindowState.Normal; Activate();
        }
        internal void PrepareForShutdown() { exiting = true; if (notificationTimer != null) notificationTimer.Stop(); }
        private void SignInAttention()
        {
            if (tray == null) return;
            tray.BalloonTipTitle = "eero Desktop needs sign-in"; tray.BalloonTipText = "Open eero Desktop from the notification area to restore network alerts."; tray.ShowBalloonTip(5000);
        }
        private void WindowsNotificationsPage()
        {
            Sub("Windows notifications");
            ObjectCards(J.Obj("windows_alerts", UiPreferences.Notifications, "keep_running_when_closed", UiPreferences.Background, "start_with_windows", UiPreferences.StartWithWindows, "monitor_all_networks", UiPreferences.AllNetworks));
            page.Children.Add(Text("Alerts check eero's notification history every minute while this app runs. Start with Windows can resume the app in the notification area after you sign in to Windows. Remember your eero session to restore it automatically.", 14, "#64738A"));
            page.Children.Add(LocalButton("Configure Windows notifications", delegate
            {
                Edit("Windows notifications", new[] { new Field("enabled", "Show Windows network alerts", UiPreferences.Notifications, "bool"), new Field("background", "Keep running in the notification area when the window closes", UiPreferences.Background, "bool"), new Field("startup", "Start in the notification area when I sign in to Windows", UiPreferences.StartWithWindows, "bool"), new Field("all", "Monitor all networks in this account", UiPreferences.AllNetworks, "bool") }, async v =>
                {
                    if (preview) throw new InvalidOperationException("Preview mode does not change Windows startup or your preferences.");
                    bool oldEnabled = UiPreferences.Notifications, oldBackground = UiPreferences.Background, oldStartup = UiPreferences.StartWithWindows, oldAll = UiPreferences.AllNetworks;
                    try
                    {
                        StartupRegistration.Apply(new WindowsStartupStore(), System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName, Flag(v, "startup"), delegate
                        {
                            UiPreferences.Notifications = Flag(v, "enabled"); UiPreferences.StartWithWindows = Flag(v, "startup"); UiPreferences.Background = Flag(v, "background") || UiPreferences.StartWithWindows; UiPreferences.AllNetworks = Flag(v, "all"); UiPreferences.Save();
                        });
                    }
                    catch { UiPreferences.Notifications = oldEnabled; UiPreferences.Background = oldBackground; UiPreferences.StartWithWindows = oldStartup; UiPreferences.AllNetworks = oldAll; throw; }
                    ConfigureDesktopNotifications(); SetNotice("Windows notification and startup preferences saved."); await Task.FromResult(0);
                });
            }));
            page.Children.Add(Text("Startup is optional and starts disabled. Keep the app in its current folder after enabling startup. Exit on the tray menu stops alerts until the next launch. Closing Windows or signing out stops monitoring.", 13, "#64738A"));
        }
    }
}
