using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClearDesk.Packaging
{
    internal static class RuntimeBootstrap
    {
        internal sealed class Download
        {
            internal string Url, Hash;
        }
        private static string Version, Identity;
        private static Download[] Downloads;

        internal static string Ensure(string root)
        {
            ReadManifest();
            string target = Path.Combine(root, "runtimes", Version + "-win-x64");
            if (IsReady(target)) return target;
            Application.EnableVisualStyles();
            using (var window = new PreparationWindow(target))
            {
                Application.Run(window);
                if (window.Failure != null) throw window.Failure;
            }
            return target;
        }

        private static void ReadManifest()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClearDesk.Runtime.txt"))
            using (var reader = new StreamReader(stream))
            {
                string text = reader.ReadToEnd();
                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length != 3 || !System.Text.RegularExpressions.Regex.IsMatch(lines[0], @"^\d+\.\d+\.\d+$"))
                    throw new InvalidDataException("运行环境信息无效。");
                Version = lines[0];
                Downloads = new Download[2];
                for (int i = 0; i < Downloads.Length; i++)
                {
                    string[] parts = lines[i + 1].Split('|');
                    Uri uri;
                    if (parts.Length != 2 || !Uri.TryCreate(parts[0], UriKind.Absolute, out uri) ||
                        uri.Scheme != "https" || uri.Host != "builds.dotnet.microsoft.com" ||
                        !System.Text.RegularExpressions.Regex.IsMatch(parts[1], "^[a-f0-9]{128}$"))
                        throw new InvalidDataException("运行环境下载信息无效。");
                    Downloads[i] = new Download { Url = parts[0], Hash = parts[1] };
                }
                using (var sha = SHA256.Create())
                    Identity = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "");
            }
        }

        private static bool IsReady(string target)
        {
            string marker = Path.Combine(target, ".ready");
            return File.Exists(marker) && File.ReadAllText(marker) == Identity &&
                File.Exists(Path.Combine(target, "dotnet.exe")) &&
                File.Exists(Path.Combine(target, "host", "fxr", Version, "hostfxr.dll")) &&
                File.Exists(Path.Combine(target, "shared", "Microsoft.NETCore.App", Version, "coreclr.dll")) &&
                File.Exists(Path.Combine(target, "shared", "Microsoft.WindowsDesktop.App", Version, "PresentationFramework.dll"));
        }

        private static void Prepare(string target, IProgress<Update> progress, CancellationToken cancel)
        {
            string sid = WindowsIdentity.GetCurrent().User.Value;
            using (var gate = new Mutex(false, @"Local\ClearDesk.Runtime-" + sid + "-" + Version))
            {
                bool acquired = false;
                try
                {
                    progress.Report(new Update("正在准备运行环境…", -1));
                    while (!acquired)
                    {
                        cancel.ThrowIfCancellationRequested();
                        try { acquired = gate.WaitOne(250); }
                        catch (AbandonedMutexException) { acquired = true; }
                    }
                    if (IsReady(target)) return;
                    Directory.CreateDirectory(target);
                    string marker = Path.Combine(target, ".ready");
                    if (File.Exists(marker)) File.Delete(marker);
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    for (int i = 0; i < Downloads.Length; i++)
                    {
                        string name = i == 0 ? ".NET 核心运行时" : ".NET 桌面运行时";
                        string archive = Path.Combine(target, "runtime-" + i + ".download");
                        try
                        {
                            Fetch(Downloads[i], archive, name, progress, cancel);
                            progress.Report(new Update("正在校验" + name + "…", -1));
                            Verify(archive, Downloads[i].Hash);
                            cancel.ThrowIfCancellationRequested();
                            progress.Report(new Update("正在展开" + name + "…", -1));
                            Extract(archive, target, cancel);
                        }
                        finally { if (File.Exists(archive)) File.Delete(archive); }
                    }
                    cancel.ThrowIfCancellationRequested();
                    File.WriteAllText(marker, Identity);
                    if (!IsReady(target))
                    {
                        File.Delete(marker);
                        throw new InvalidDataException("运行环境文件不完整，请重试。");
                    }
                }
                finally { if (acquired) gate.ReleaseMutex(); }
            }
        }

        private static void Fetch(Download download, string destination, string name, IProgress<Update> progress, CancellationToken cancel)
        {
            var request = (HttpWebRequest)WebRequest.Create(download.Url);
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.UserAgent = "ClearDesk/0.1";
            progress.Report(new Update("正在连接微软下载服务…", -1));
            using (cancel.Register(request.Abort))
            {
                try
                {
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.ResponseUri.Scheme != "https") throw new IOException("下载连接必须使用 HTTPS。");
                        using (var source = response.GetResponseStream())
                        using (var output = File.Create(destination))
                        {
                            var buffer = new byte[81920];
                            long total = 0;
                            int count;
                            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cancel.ThrowIfCancellationRequested();
                                total += count;
                                if (total > 256L * 1024 * 1024) throw new IOException("下载文件大小异常。");
                                output.Write(buffer, 0, count);
                                int percent = response.ContentLength > 0 ? (int)(total * 100 / response.ContentLength) : -1;
                                progress.Report(new Update("正在下载" + name + "：" + (total / 1048576.0).ToString("F1") + " MB", percent));
                            }
                            if (response.ContentLength >= 0 && total != response.ContentLength) throw new IOException("下载未完成，请重试。");
                        }
                    }
                }
                catch (WebException) { cancel.ThrowIfCancellationRequested(); throw; }
            }
        }

        internal static void Verify(string archive, string expected)
        {
            using (var input = File.OpenRead(archive))
            using (var sha = SHA512.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                if (!String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("下载文件校验失败，请重试。");
            }
        }

        internal static void Extract(string archive, string target, CancellationToken cancel)
        {
            string prefix = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var zip = ZipFile.OpenRead(archive))
            {
                foreach (var entry in zip.Entries)
                {
                    cancel.ThrowIfCancellationRequested();
                    string path = Path.GetFullPath(Path.Combine(target, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("运行环境包含无效路径。");
                    if (String.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open())
                    using (var output = File.Create(path)) input.CopyTo(output);
                }
            }
        }

        private sealed class Update
        {
            internal readonly string Text;
            internal readonly int Percent;
            internal Update(string text, int percent) { Text = text; Percent = percent; }
        }

        private sealed class PreparationWindow : Form
        {
            private readonly string target;
            private readonly CancellationTokenSource cancel = new CancellationTokenSource();
            private readonly Label status;
            private readonly ProgressBar bar;
            private readonly Button cancelButton;
            private bool finished;
            internal Exception Failure;

            internal PreparationWindow(string target)
            {
                this.target = target;
                Text = "澄桌 · 首次启动";
                AutoScaleMode = AutoScaleMode.Dpi;
                Font = new Font("Microsoft YaHei UI", 9F);
                ClientSize = new Size(440, 180);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false; MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
                Controls.Add(new Label { Text = "正在准备澄桌", Location = new Point(20, 18), AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
                Controls.Add(new Label { Text = "首次需要联网下载；完成后可离线启动。", Location = new Point(20, 48), AutoSize = true });
                status = new Label { Location = new Point(20, 78), Size = new Size(400, 25), Text = "正在准备…" };
                bar = new ProgressBar { Location = new Point(20, 108), Size = new Size(400, 16), Style = ProgressBarStyle.Marquee };
                cancelButton = new Button { Text = "取消", Location = new Point(340, 139), Size = new Size(80, 28) };
                cancelButton.Click += delegate { CancelPreparation(); };
                Controls.Add(status); Controls.Add(bar); Controls.Add(cancelButton);
                Shown += RunPreparation;
                FormClosing += delegate(object sender, FormClosingEventArgs e)
                {
                    if (!finished) { e.Cancel = true; CancelPreparation(); }
                };
            }

            private void CancelPreparation()
            {
                cancelButton.Enabled = false;
                status.Text = "正在取消…";
                cancel.Cancel();
            }

            private async void RunPreparation(object sender, EventArgs e)
            {
                var progress = new Progress<Update>(update =>
                {
                    if (finished || cancel.IsCancellationRequested) return;
                    status.Text = update.Text;
                    bar.Style = update.Percent < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                    if (update.Percent >= 0) bar.Value = Math.Min(100, update.Percent);
                });
                try { await Task.Run(() => Prepare(target, progress, cancel.Token)); }
                catch (Exception exception) { Failure = exception; }
                finally { finished = true; Close(); }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) cancel.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
