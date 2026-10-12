using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

// Integration verifier loads the shipped EXE, so it exercises its embedded download manifest and code.
internal static class RuntimeBootstrapVerifier
{
    private static Type runtime;
    private static readonly List<string> checks = new List<string>();
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            runtime = Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("ClearDesk.Packaging.RuntimeBootstrap", true);
            string cache = Path.GetFullPath(args[1]);
            string fixtures = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(fixtures);
            Call("ReadManifest");
            string version = (string)runtime.GetField("Version", Static).GetValue(null);
            string target = Path.Combine(cache, "runtimes", version + "-win-x64");
            Check((bool)Call("IsReady", target), "Actual first-download cache is complete");
            WebRequest.DefaultWebProxy = new WebProxy("http://127.0.0.1:1");
            Check((string)Call("Ensure", cache) == target, "Cached runtime works with an unreachable network proxy");
            WebRequest.DefaultWebProxy = null;

            string corrupt = Path.Combine(fixtures, "corrupt.zip");
            File.WriteAllText(corrupt, "incomplete download");
            Expect<InvalidDataException>(() => Call("Verify", corrupt, new string('0', 128)), "Corrupted archive fails SHA-512 verification");
            string invalid = Path.Combine(fixtures, "traversal.zip");
            using (var zip = ZipFile.Open(invalid, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("../escaped.txt").Open())) writer.Write("invalid path");
            Expect<InvalidDataException>(() => Call("Extract", invalid, Path.Combine(fixtures, "extract"), CancellationToken.None), "Archive cannot escape its runtime directory");
            Check(!File.Exists(Path.Combine(fixtures, "escaped.txt")), "Traversal did not create an external file");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Expect<OperationCanceledException>(() => Call("Extract", invalid, Path.Combine(fixtures, "cancelled"), cancellation.Token), "Cancelled extraction stops immediately");
            }

            Type update = runtime.GetNestedType("Update", BindingFlags.NonPublic);
            object progress = Activator.CreateInstance(typeof(Progress<>).MakeGenericType(update));
            Array downloads = (Array)runtime.GetField("Downloads", Static).GetValue(null);
            object download = downloads.GetValue(0);
            download.GetType().GetField("Url", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(download, "https://127.0.0.1:1/unreachable.zip");
            string retryTarget = Path.Combine(fixtures, "retry-runtime");
            Expect<WebException>(() => Call("Prepare", retryTarget, progress, CancellationToken.None), "Failed network connection is reported");
            Check(!File.Exists(Path.Combine(retryTarget, ".ready")), "Failed preparation has no ready marker");
            Check(Directory.GetFiles(retryTarget, "*.download").Length == 0, "Failed download cleans incomplete archive");
            Call("ReadManifest");
            Call("Prepare", retryTarget, progress, CancellationToken.None);
            Check((bool)Call("IsReady", retryTarget), "Retry after network failure downloads and completes successfully");
            Check(Directory.GetFiles(retryTarget, "*.download").Length == 0, "Completed preparation cleans downloaded ZIPs");

            Application.EnableVisualStyles();
            Type preparation = runtime.GetNestedType("PreparationWindow", BindingFlags.NonPublic);
            using (var form = (Form)Activator.CreateInstance(preparation, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { Path.Combine(fixtures, "cancel-ui") }, null))
            using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                timer.Tick += delegate
                {
                    timer.Stop();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(fixtures, "download-dialog.png")); }
                    foreach (Control control in form.Controls) if (control is Button) ((Button)control).PerformClick();
                };
                form.Shown += delegate { timer.Start(); };
                Application.Run(form);
                object failure = preparation.GetField("Failure", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Check(failure is OperationCanceledException, "Download dialog Cancel interrupts preparation and closes");
                Check(!File.Exists(Path.Combine(fixtures, "cancel-ui", ".ready")), "User cancellation has no ready marker");
                Check(Directory.GetFiles(Path.Combine(fixtures, "cancel-ui"), "*.download").Length == 0, "User cancellation removes incomplete downloads");
            }
            File.WriteAllLines(Path.Combine(fixtures, "bootstrap-checks.txt"), checks);
            Console.WriteLine("Bootstrap checks passed: " + checks.Count);
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static object Call(string name, params object[] args)
    {
        try { return runtime.GetMethod(name, Static).Invoke(null, args); }
        catch (TargetInvocationException exception) { throw exception.InnerException; }
    }
    private static void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException(name);
        checks.Add(name); Console.WriteLine("PASS: " + name);
    }
    private static void Expect<T>(Action action, string name) where T : Exception
    {
        try { action(); } catch (T) { Check(true, name); return; }
        throw new InvalidOperationException(name);
    }
}
