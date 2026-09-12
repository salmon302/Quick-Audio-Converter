// SPDX-License-Identifier: MIT
namespace QuickAudioConverter;

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuickAudioConverter.Engine;

/// <summary>
/// Application entry point. In GUI mode it hosts the main window. With --headless/--selftest it
/// runs the converter without a window (the basis for File Explorer context-menu invocation).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--headless" || args[0] == "--selftest" || args[0] == "--gentestwav"))
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
        if (args[0] == "--gentestwav")
        {
            string path = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "qac_selftest", "sample.wav");
            int seconds = args.Length > 2 && int.TryParse(args[2], out int s) ? s : 1;
            GenerateTestWav(path, seconds);
            Console.WriteLine("Wrote " + path);
            return 0;
        }

        if (args[0] == "--selftest")
        {
            return RunSelfTest() ? 0 : 1;
        }

        var inputs = new List<string>();
        string? format = null;
        string? outDir = null;
        int channels = 2;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input": inputs.Add(args[++i]); break;
                case "--format": format = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                case "--mono": channels = 1; break;
                case "--stereo": channels = 2; break;
            }
        }

        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("No --input specified.");
            return 2;
        }

        format ??= "mp3";
        var settings = new ConversionSettings { OutputFormat = format, Channels = channels, BitrateKbps = 224, SampleRate = 44100 };
        var routing = new OutputRouting { SaveToSource = outDir == null, SaveToFolder = outDir, CopySourceStructure = false };
        var service = new AudioConversionService(new WmfAudioEngine());
        var report = Task.Run(() => service.ConvertAsync(inputs, settings, routing)).GetAwaiter().GetResult();
        Console.WriteLine($"Succeeded={report.Succeeded} Failed={report.Failed}");
        foreach (var e in report.Errors) Console.Error.WriteLine("ERR " + e);
        return report.Failed == 0 ? 0 : 1;
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

            bool ok = wavOk && mp3Ok && m4aWavOk && m4aMp3Ok;
            string result = $"SELFTEST {(ok ? "PASS" : "FAIL")} | WAV(manual)={wavOk} MP3(LAME)={mp3Ok} M4A->WAV={m4aWavOk} M4A->MP3={m4aMp3Ok}";
            Console.WriteLine(result);
            if (!wavOk) Console.Error.WriteLine("WAV ERR: " + wavErr);
            if (!mp3Ok) Console.Error.WriteLine("MP3 ERR: " + mp3Err);
            if (!m4aWavOk) Console.Error.WriteLine("M4A->WAV ERR: " + m4aErr);
            if (!m4aMp3Ok) Console.Error.WriteLine("M4A->MP3 ERR: " + m4aErr);
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

    /// <summary>Validates the 44-byte RIFF/WAVE(PCM) header written by <see cref="WavWriter"/>.</summary>
    private static bool IsValidWav(string path)
    {
        var hdr = new byte[44];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        if (fs.Length < 44) return false;
        if (fs.Read(hdr, 0, 44) != 44) return false;
        // "RIFF", "WAVE", "fmt ", "data" in little-endian.
        bool riff = hdr[0] == 'R' && hdr[1] == 'I' && hdr[2] == 'F' && hdr[3] == 'F';
        bool wave = hdr[8] == 'W' && hdr[9] == 'A' && hdr[10] == 'V' && hdr[11] == 'E';
        bool fmt = hdr[12] == 'f' && hdr[13] == 'm' && hdr[14] == 't' && hdr[15] == ' ';
        bool data = hdr[36] == 'd' && hdr[37] == 'a' && hdr[38] == 't' && hdr[39] == 'a';
        int audioFormat = hdr[20] | (hdr[21] << 8);
        int bits = hdr[34] | (hdr[35] << 8);
        int dataSize = hdr[40] | (hdr[41] << 8) | (hdr[42] << 16) | (hdr[43] << 24);
        return riff && wave && fmt && data && audioFormat == 1 && bits == 16 && dataSize == (int)(fs.Length - 44);
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
