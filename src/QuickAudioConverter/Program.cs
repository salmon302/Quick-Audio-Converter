// SPDX-License-Identifier: MIT
namespace QuickAudioConverter;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuickAudioConverter.Engine;
using QuickAudioConverter.Shell;

/// <summary>
/// Application entry point. In GUI mode it hosts the main window. With --headless/--selftest it
/// runs the converter without a window (the basis for File Explorer context-menu invocation).
/// --register / --unregister install or remove the per-user Explorer context-menu verbs.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            int? rc = DispatchShellVerbs(args);
            if (rc.HasValue) return rc.Value;
        }

        if (args.Length > 0 && (args[0] == "--headless" || args[0] == "--selftest" ||
                                args[0] == "--gentestwav" || args[0] == "--register" || args[0] == "--unregister"))
        {
            return HeadlessMain(args);
        }

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
    }

    private static int HeadlessMain(string[] args)
    {
        if (args[0] == "--register")
        {
            var s = AppSettings.Load();
            ShellIntegration.Register(s);
            Console.WriteLine("Registered Explorer context-menu verbs for " +
                string.Join(", ", AppSettings.DecodableExtensions));
            return 0;
        }
        if (args[0] == "--unregister")
        {
            var s = AppSettings.Load();
            ShellIntegration.Unregister(s);
            Console.WriteLine("Unregistered Explorer context-menu verbs.");
            return 0;
        }

        if (args[0] == "--gentestwav")
        {
            string path = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "qac_selftest", "sample.wav");
            int seconds = args.Length > 2 && int.TryParse(args[2], out int sec) ? sec : 1;
            GenerateTestWav(path, seconds);
            Console.WriteLine("Wrote " + path);
            return 0;
        }

        if (args[0] == "--selftest")
        {
            return RunSelfTest() ? 0 : 1;
        }

        // --headless: read the persistent "last-used" settings, apply CLI overrides, convert.
        var settings = AppSettings.Load();
        var inputs = new List<string>();
        string? format = null;
        string? outDir = null;
        int? channels = null;
        int? bitrate = null;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input": inputs.Add(args[++i]); break;
                case "--format": format = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                case "--mono": channels = 1; break;
                case "--stereo": channels = 2; break;
                case "--bitrate": bitrate = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
            }
        }

        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("No --input specified.");
            return 2;
        }

        var conv = new ConversionSettings
        {
            OutputFormat = format ?? settings.OutputFormat,
            Channels = channels ?? settings.Channels,
            BitrateKbps = bitrate ?? settings.BitrateKbps,
            SampleRate = settings.SampleRate
        };
        var routing = new OutputRouting
        {
            SaveToSource = outDir == null ? settings.SaveToSource : false,
            SaveToFolder = outDir ?? settings.DefaultSaveFolder,
            CopySourceStructure = settings.CopySourceStructure
        };

        var service = new AudioConversionService(new WmfAudioEngine());
        ConversionReport report;
        try
        {
            report = Task.Run(() => service.ConvertAsync(inputs, conv, routing)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ToastNotifier.Show("Quick Audio Converter", "Conversion failed: " + ex.Message);
            Console.Error.WriteLine("FATAL " + ex.Message);
            return 1;
        }

        Console.WriteLine($"Succeeded={report.Succeeded} Failed={report.Failed}");
        foreach (var e in report.Errors) Console.Error.WriteLine("ERR " + e);

        var fmt = (conv.OutputFormat ?? "mp3").ToUpperInvariant();
        if (report.Failed == 0)
            ToastNotifier.Show("Quick Audio Converter", $"Converted {report.Succeeded} file(s) to .{fmt}.");
        else
            ToastNotifier.Show("Quick Audio Converter",
                $"Converted {report.Succeeded}, failed {report.Failed} to .{fmt}.");

        return report.Failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Handles the IExplorerCommand / sparse-package installer verbs:
    /// --comserver (host the COM local server), --install/--uninstall (per-user or, with
    /// --all-users, elevated machine-wide + sparse package), and --register-modern-packaged
    /// (run under package identity to virtualize the registration).
    /// Returns null when the verb is not one of these (falls through to the existing dispatch).
    /// </summary>
    private static int? DispatchShellVerbs(string[] args)
    {
        switch (args[0])
        {
            case "--comserver":
                return ComServer.Run();

            case "--register-modern-packaged":
                ExplorerCommandRegistration.RegisterPackaged();
                return 0;

            case "--install":
            case "--uninstall":
            {
                bool allUsers = args.Length > 1 && args[1] == "--all-users";
                var scope = allUsers ? ExplorerCommandRegistration.Scope.AllUsers : ExplorerCommandRegistration.Scope.PerUser;

                // All-users install touches HKLM and the package catalog, so it must be elevated.
                if (allUsers && !Uac.IsAdministrator())
                {
                    return Uac.RelaunchElevated(string.Join(" ", args)) ? 0 : 1;
                }

                try
                {
                    if (args[0] == "--install")
                    {
                        ExplorerCommandRegistration.Register(scope);
                        if (allUsers)
                        {
                            ExplorerCommandRegistration.InstallSparsePackage();
                            ExplorerCommandRegistration.RegisterUnderPackageIdentity();
                        }
                        var settings = AppSettings.Load();
                        settings.ModernShellInstalled = true;
                        settings.ShellInstallScope = allUsers ? "AllUsers" : "PerUser";
                        settings.SparsePackageFullName = allUsers
                            ? ExplorerCommandRegistration.GetSparsePackageFullName()
                            : null;
                        settings.Save();
                        Console.WriteLine("Installed ExplorerCommand integration (" +
                            (allUsers ? "all users + sparse package" : "per user") + ").");
                    }
                    else
                    {
                        if (allUsers) ExplorerCommandRegistration.RemoveSparsePackage();
                        ExplorerCommandRegistration.Unregister(scope);
                        var settings = AppSettings.Load();
                        settings.ModernShellInstalled = false;
                        settings.ShellInstallScope = null;
                        settings.SparsePackageFullName = null;
                        settings.Save();
                        Console.WriteLine("Uninstalled ExplorerCommand integration.");
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("FATAL " + ex.Message);
                    return 1;
                }
            }

            default:
                return null;
        }
    }

    private static bool RunSelfTest()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "qac_selftest");
        Directory.CreateDirectory(tmp);
        string input = Path.Combine(tmp, "sample_in.wav");
        string wavOutDir = Path.Combine(tmp, "wav_out");
        string mp3OutDir = Path.Combine(tmp, "mp3_out");
        Directory.CreateDirectory(wavOutDir);
        Directory.CreateDirectory(mp3OutDir);
        string wavOut = Path.Combine(wavOutDir, "sample_in.wav");
        string mp3Out = Path.Combine(mp3OutDir, "sample_in.mp3");
        try
        {
            GenerateTestWav(input, 1);
            if (File.Exists(wavOut)) File.Delete(wavOut);
            if (File.Exists(mp3Out)) File.Delete(mp3Out);

            var service = new AudioConversionService(new WmfAudioEngine());
            bool wavOk = false, mp3Ok = false;
            string wavErr = "", mp3Err = "";

            // 1) WAV via manual 44-byte RIFF header (no Sink Writer).
            try
            {
                var wavSettings = new ConversionSettings { OutputFormat = "wav", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
                var wavRouting = new OutputRouting { SaveToSource = false, SaveToFolder = wavOutDir, CopySourceStructure = false };
                var wavReport = Task.Run(() => service.ConvertAsync(new[] { input }, wavSettings, wavRouting)).GetAwaiter().GetResult();
                wavOk = wavReport.Succeeded == 1 && File.Exists(wavOut) && IsValidWav(wavOut);
                wavErr = string.Join("; ", wavReport.Errors);
            }
            catch (Exception ex) { wavErr = ex.Message; }

            // 2) MP3 via libmp3lame (LAME).
            try
            {
                var mp3Settings = new ConversionSettings { OutputFormat = "mp3", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
                var mp3Routing = new OutputRouting { SaveToSource = false, SaveToFolder = mp3OutDir, CopySourceStructure = false };
                var mp3Report = Task.Run(() => service.ConvertAsync(new[] { input }, mp3Settings, mp3Routing)).GetAwaiter().GetResult();
                mp3Ok = mp3Report.Succeeded == 1 && File.Exists(mp3Out) && new FileInfo(mp3Out).Length > 1000;
                mp3Err = string.Join("; ", mp3Report.Errors);
            }
            catch (Exception ex) { mp3Err = ex.Message; }

            // 3) M4A decode -> WAV (manual) and -> MP3 (LAME) using the committed fixture.
            bool m4aWavOk = false, m4aMp3Ok = false;
            string m4aErr = "";
            string m4aFixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "1s_stereo_44k.m4a");
            if (File.Exists(m4aFixture))
            {
                try
                {
                    var m4aWavSettings = new ConversionSettings { OutputFormat = "wav", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
                    var m4aWavRouting = new OutputRouting { SaveToSource = false, SaveToFolder = Path.Combine(tmp, "m4a_wav_out"), CopySourceStructure = false };
                    var m4aWavReport = Task.Run(() => service.ConvertAsync(new[] { m4aFixture }, m4aWavSettings, m4aWavRouting)).GetAwaiter().GetResult();
                    string m4aWavOut = Path.Combine(tmp, "m4a_wav_out", "1s_stereo_44k.wav");
                    m4aWavOk = m4aWavReport.Succeeded == 1 && File.Exists(m4aWavOut) && IsValidWav(m4aWavOut);

                    var m4aMp3Settings = new ConversionSettings { OutputFormat = "mp3", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
                    var m4aMp3Routing = new OutputRouting { SaveToSource = false, SaveToFolder = Path.Combine(tmp, "m4a_mp3_out"), CopySourceStructure = false };
                    var m4aMp3Report = Task.Run(() => service.ConvertAsync(new[] { m4aFixture }, m4aMp3Settings, m4aMp3Routing)).GetAwaiter().GetResult();
                    string m4aMp3Out = Path.Combine(tmp, "m4a_mp3_out", "1s_stereo_44k.mp3");
                    m4aMp3Ok = m4aMp3Report.Succeeded == 1 && File.Exists(m4aMp3Out) && new FileInfo(m4aMp3Out).Length > 1000;

                    m4aErr = string.Join("; ", m4aWavReport.Errors) + " | " + string.Join("; ", m4aMp3Report.Errors);
                }
                catch (Exception ex) { m4aErr = ex.Message; }
            }
            else
            {
                m4aErr = "fixture not found: " + m4aFixture;
            }

            // 4) M4A output via FFmpeg (WAV->M4A and M4A->M4A). Skipped if ffmpeg is absent.
            bool wavM4aOk = false, m4aM4aOk = false;
            string ffmpegErr = "";
            string? ffmpeg = FfmpegEncoder.ResolveFfmpeg();
            bool ffmpegAvailable = ffmpeg is not null;
            if (ffmpegAvailable)
            {
                try
                {
                    var wavM4aSettings = new ConversionSettings { OutputFormat = "m4a", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
                    var wavM4aRouting = new OutputRouting { SaveToSource = false, SaveToFolder = Path.Combine(tmp, "wav_m4a_out"), CopySourceStructure = false };
                    var wavM4aReport = Task.Run(() => service.ConvertAsync(new[] { input }, wavM4aSettings, wavM4aRouting)).GetAwaiter().GetResult();
                    string wavM4aOut = Path.Combine(tmp, "wav_m4a_out", "sample_in.m4a");
                    wavM4aOk = wavM4aReport.Succeeded == 1 && File.Exists(wavM4aOut) && IsValidM4a(wavM4aOut);

                    var m4aM4aSettings = new ConversionSettings { OutputFormat = "m4a", Channels = 2, BitrateKbps = 224, SampleRate = 44100 };
                    var m4aM4aRouting = new OutputRouting { SaveToSource = false, SaveToFolder = Path.Combine(tmp, "m4a_m4a_out"), CopySourceStructure = false };
                    var m4aM4aReport = Task.Run(() => service.ConvertAsync(new[] { m4aFixture }, m4aM4aSettings, m4aM4aRouting)).GetAwaiter().GetResult();
                    string m4aM4aOut = Path.Combine(tmp, "m4a_m4a_out", "1s_stereo_44k.m4a");
                    m4aM4aOk = m4aM4aReport.Succeeded == 1 && File.Exists(m4aM4aOut) && IsValidM4a(m4aM4aOut);

                    ffmpegErr = string.Join("; ", wavM4aReport.Errors) + " | " + string.Join("; ", m4aM4aReport.Errors);
                }
                catch (Exception ex) { ffmpegErr = ex.Message; }
            }
            else
            {
                ffmpegErr = "ffmpeg not found on PATH/local; M4A output test skipped.";
            }

            bool ok = wavOk && mp3Ok && m4aWavOk && m4aMp3Ok && (!ffmpegAvailable || (wavM4aOk && m4aM4aOk));
            string ffmpegLeg = ffmpegAvailable ? $" WAV->M4A={wavM4aOk} M4A->M4A={m4aM4aOk}" : " WAV->M4A=SKIP M4A->M4A=SKIP(ffmpeg absent)";
            string result = $"SELFTEST {(ok ? "PASS" : "FAIL")} | WAV(manual)={wavOk} MP3(LAME)={mp3Ok} M4A->WAV={m4aWavOk} M4A->MP3={m4aMp3Ok}{ffmpegLeg}";
            Console.WriteLine(result);
            if (!wavOk) Console.Error.WriteLine("WAV ERR: " + wavErr);
            if (!mp3Ok) Console.Error.WriteLine("MP3 ERR: " + mp3Err);
            if (!m4aWavOk) Console.Error.WriteLine("M4A->WAV ERR: " + m4aErr);
            if (!m4aMp3Ok) Console.Error.WriteLine("M4A->MP3 ERR: " + m4aErr);
            if (ffmpegAvailable && !wavM4aOk) Console.Error.WriteLine("WAV->M4A ERR: " + ffmpegErr);
            if (ffmpegAvailable && !m4aM4aOk) Console.Error.WriteLine("M4A->M4A ERR: " + ffmpegErr);
            File.WriteAllText(Path.Combine(tmp, "result.txt"), result);
            return ok;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SELFTEST EXCEPTION: " + ex);
            File.WriteAllText(Path.Combine(tmp, "result.txt"), "SELFTEST EXCEPTION: " + ex.Message);
            return false;
        }
    }

    /// <summary>Validates the 44-byte RIFF/WAVE(PCM) header (delegates to <see cref="WavValidation"/>).</summary>
    private static bool IsValidWav(string path) => WavValidation.IsValidWav(path);

    /// <summary>Validates an MP4/M4A container by locating the 'ftyp' box near the start.</summary>
    private static bool IsValidM4a(string path)
    {
        var head = new byte[32];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        int n = fs.Read(head, 0, head.Length);
        if (n < 12) return false;
        for (int i = 0; i + 4 <= n; i++)
            if (head[i] == 'f' && head[i + 1] == 't' && head[i + 2] == 'y' && head[i + 3] == 'p') return true;
        return false;
    }

    /// <summary>Writes a 16-bit PCM WAV with a 440 Hz tone for conversion testing.</summary>
    private static void GenerateTestWav(string path, int seconds)
    {
        const int sampleRate = 44100, channels = 2, bytesPerSample = 2;
        int dataSize = sampleRate * seconds * channels * bytesPerSample;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * bytesPerSample);
        bw.Write((short)(channels * bytesPerSample));
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(dataSize);
        for (int i = 0; i < sampleRate * seconds; i++)
        {
            double t = (double)i / sampleRate;
            short s = (short)(Math.Sin(2 * Math.PI * 440 * t) * 30000);
            for (int c = 0; c < channels; c++) bw.Write(s);
        }
    }
}
