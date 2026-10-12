using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("澄桌")]
[assembly: AssemblyProduct("澄桌（ClearDesk）")]
[assembly: AssemblyVersion("0.1.0.0")]

namespace ClearDesk.Packaging
{
    // Small .NET Framework bootstrapper; Windows 11 includes its runtime.
    // Offline embeds the full runtime; ONLINE downloads a private runtime and embeds only the app.
    internal static class PortableLauncher
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClearDesk");
                var forwarded = new List<string>();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--package-root" && i + 1 < args.Length) root = Path.GetFullPath(args[++i]);
                    else forwarded.Add(args[i]);
                }
                string hash;
                using (var payload = OpenPayload())
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", "").ToLowerInvariant();
                string appRoot = Path.Combine(root, "packages", hash);
                string ready = Path.Combine(appRoot, ".ready");
                string app = Path.Combine(appRoot, "ClearDesk.GlassWindow.exe");
                string sid = WindowsIdentity.GetCurrent().User.Value;
                using (var gate = new Mutex(false, @"Local\ClearDesk.Package-" + sid + "-" + hash))
                {
                    bool acquired;
                    try { acquired = gate.WaitOne(TimeSpan.FromMinutes(2)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("澄桌首次准备尚未完成，请稍后重试。");
                    try
                    {
                        if (!File.Exists(ready) || !File.Exists(app))
                        {
                            Extract(appRoot);
                            if (!File.Exists(app)) throw new IOException("发布包缺少澄桌程序。");
                            File.WriteAllText(ready, hash);
                        }
                    }
                    finally { gate.ReleaseMutex(); }
                }
#if ONLINE
                string runtimeRoot;
                while (true)
                {
                    try { runtimeRoot = RuntimeBootstrap.Ensure(root); break; }
                    catch (OperationCanceledException) { return 0; }
                    catch (Exception exception)
                    {
                        if (MessageBox.Show("运行环境准备失败：" + exception.Message + "\n\n请检查网络后重试。已保存的个人设置不会受影响。",
                            "澄桌", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry) return 1;
                    }
                }
#endif
                if (!forwarded.Contains("--state-root"))
                {
                    forwarded.Add("--state-root"); forwarded.Add(Path.Combine(root, "data"));
                }
                var start = new ProcessStartInfo(app)
                {
                    WorkingDirectory = appRoot, UseShellExecute = false,
                    Arguments = BuildArguments(forwarded)
                };
                // Runtime is app-local; developer SDK paths must not be required to run the package.
                start.EnvironmentVariables.Remove("DOTNET_ROOT");
                start.EnvironmentVariables.Remove("DOTNET_ROOT_X64");
#if ONLINE
                start.EnvironmentVariables["DOTNET_ROOT"] = runtimeRoot;
                start.EnvironmentVariables["DOTNET_ROOT_X64"] = runtimeRoot;
#endif
                using (var process = Process.Start(start))
                {
                    if (forwarded.Contains("--smoke-test") || forwarded.Contains("--instance-probe"))
                    { process.WaitForExit(); return process.ExitCode; }
                }
                return 0;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "澄桌启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static Stream OpenPayload()
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClearDesk.Payload.zip");
            if (stream == null) throw new IOException("无法读取澄桌发布包。");
            return stream;
        }

        private static void Extract(string destination)
        {
            Directory.CreateDirectory(destination);
            string prefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var payload = OpenPayload())
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("发布包包含无效路径。");
                    if (String.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var source = entry.Open())
                    using (var output = File.Create(target)) source.CopyTo(output);
                }
            }
        }

        private static string BuildArguments(IEnumerable<string> arguments)
        {
            var result = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (result.Length != 0) result.Append(' ');
                result.Append('"');
                int slashes = 0;
                foreach (char character in argument)
                {
                    if (character == '\\') { slashes++; continue; }
                    if (character == '"') result.Append('\\', slashes * 2 + 1);
                    else result.Append('\\', slashes);
                    slashes = 0; result.Append(character);
                }
                result.Append('\\', slashes * 2); result.Append('"');
            }
            return result.ToString();
        }
    }
}
