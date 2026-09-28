using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AliVirtualMic;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    const int SampleRate = 48000;
    const int Channels = 2;
    readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
    readonly ComboBox mic = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox output = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckedListBox apps = new();
    readonly Button refresh = new() { Text = "Refresh" };
    readonly Button start = new() { Text = "START MIXER", Height = 44 };
    readonly TrackBar micVolume = new() { Minimum = 0, Maximum = 200, Value = 100, TickFrequency = 25 };
    readonly Label micValue = new() { Text = "100%", AutoSize = true };
    readonly Label status = new() { Text = "Stopped" };

    WasapiRecorder? micRecorder;
    WasapiPlayer? player;
    MixingSampleProvider? mixer;
    readonly List<WasapiRecorder> appRecorders = [];
    readonly List<BufferedWaveProvider> sourceBuffers = [];
    bool running;

    public MainForm()
    {
        Text = "Ali Virtual Mic";
        Width = 760;
        Height = 690;
        MinimumSize = new Size(760, 690);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.White;

        var title = new Label
        {
            Text = "ALI VIRTUAL MIC",
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(25, 18)
        };

        var sub = new Label
        {
            Text = "Mix your microphone with selected application audio",
            AutoSize = true,
            Location = new Point(28, 53),
            ForeColor = Color.Silver
        };

        Controls.Add(title);
        Controls.Add(sub);

        AddLabel("Microphone", 28, 88);
        mic.SetBounds(28, 112, 680, 34);
        Controls.Add(mic);

        AddLabel("Output device / virtual cable", 28, 163);
        output.SetBounds(28, 187, 680, 34);
        Controls.Add(output);

        AddLabel("Applications to send through the virtual mic", 28, 238);
        apps.SetBounds(28, 263, 680, 205);
        apps.CheckOnClick = true;
        Controls.Add(apps);

        AddLabel("Microphone volume", 28, 482);
        micVolume.SetBounds(145, 474, 430, 45);
        micVolume.Scroll += (_, _) => micValue.Text = $"{micVolume.Value}%";
        Controls.Add(micVolume);

        micValue.Location = new Point(590, 486);
        Controls.Add(micValue);

        refresh.SetBounds(28, 535, 120, 38);
        refresh.Click += (_, _) => RefreshAll();
        Controls.Add(refresh);

        start.SetBounds(165, 532, 250, 44);
        start.Click += (_, _) => Toggle();
        Controls.Add(start);

        status.SetBounds(435, 543, 280, 25);
        Controls.Add(status);

        var help = new Label
        {
            Text = "Tip: select CABLE Input (VB-Audio Virtual Cable) above, then choose CABLE Output as the microphone in Discord/OBS/TikTok/etc.",
            AutoSize = false,
            Width = 680,
            Height = 55,
            Location = new Point(28, 595),
            ForeColor = Color.Silver
        };

        Controls.Add(help);

        Shown += (_, _) => RefreshAll();
        FormClosing += (_, _) => StopMixer();
    }

    void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Location = new Point(x, y),
            ForeColor = Color.Gainsboro
        });
    }

    void RefreshAll()
    {
        try
        {
            using var e = new MMDeviceEnumerator();

            mic.Items.Clear();
            output.Items.Clear();

            foreach (var d in e.EnumerateAudioEndPoints(
                DataFlow.Capture,
                DeviceState.Active))
            {
                mic.Items.Add(new DeviceItem(d));
            }

            foreach (var d in e.EnumerateAudioEndPoints(
                DataFlow.Render,
                DeviceState.Active))
            {
                output.Items.Add(new DeviceItem(d));
            }

            if (mic.Items.Count > 0)
                mic.SelectedIndex = 0;

            int cable = -1;

            for (int i = 0; i < output.Items.Count; i++)
            {
                if (output.Items[i].ToString()!
                    .Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
                {
                    cable = i;
                    break;
                }
            }

            output.SelectedIndex =
                cable >= 0
                    ? cable
                    : (output.Items.Count > 0 ? 0 : -1);

            RefreshApps();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ali Virtual Mic");
        }
    }

    void RefreshApps()
    {
    var old = apps.CheckedItems
        .Cast<AppItem>()
        .Select(x => x.Pid)
        .ToHashSet();

    apps.Items.Clear();

    var list = Process.GetProcesses()
        .Where(p =>
        {
            try
            {
                // Only show processes with a visible main window.
                return p.MainWindowHandle != IntPtr.Zero &&
                       !string.IsNullOrWhiteSpace(p.MainWindowTitle);
            }
            catch
            {
                return false;
            }
        })
        .Select(p =>
        {
            try
            {
                return new AppItem(
                    p.Id,
                    p.ProcessName,
                    p.MainWindowTitle);
            }
            catch
            {
                return null;
            }
        })
        .Where(x => x != null)
        .Cast<AppItem>()
        .Where(x =>
            !string.Equals(
                x.Name,
                "AliVirtualMic",
                StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Pid)
        .ToList();

    foreach (var item in list)
    {
        apps.Items.Add(item, old.Contains(item.Pid));
    }
}

    void Toggle()
    {
        if (running)
            StopMixer();
        else
            StartMixer();
    }

    void StartMixer()
    {
        if (mic.SelectedItem is not DeviceItem micDevice ||
            output.SelectedItem is not DeviceItem outputDevice)
        {
            MessageBox.Show(
                "Choose a microphone and output device first.");
            return;
        }

        var selectedApps = apps.CheckedItems
            .Cast<AppItem>()
            .ToList();

        if (selectedApps.Count == 0)
        {
            var answer = MessageBox.Show(
                "No applications are selected. Start with microphone only?",
                "Ali Virtual Mic",
                MessageBoxButtons.YesNo);

            if (answer != DialogResult.Yes)
                return;
        }

        try
        {
            mixer = new MixingSampleProvider(MixFormat)
            {
                ReadFully = true
            };

            // Physical microphone
            micRecorder = new WasapiRecorderBuilder()
                .WithDevice(micDevice.Device)
                .WithSharedMode()
                .WithFormat(MixFormat)
                .WithBufferLength(30)
                .WithMmcssThreadPriority("Pro Audio")
                .Build();

            var micBuffer = NewBuffer();

            micRecorder.DataAvailable +=
                (buffer, _, _, _) =>
                    micBuffer.AddSamples(
                        buffer.ToArray(),
                        0,
                        buffer.Length);

            mixer.AddMixerInput(
                new VolumeSampleProvider(
                    micBuffer.ToSampleProvider())
                {
                    Volume = micVolume.Value / 100f
                });

            micRecorder.StartRecording();

            // Per-process Windows loopback capture
            foreach (var app in selectedApps)
            {
                try
                {
                    var recorder = new WasapiRecorderBuilder()
                        .WithProcessLoopback(
                            (uint)app.Pid,
                            ProcessLoopbackMode.IncludeTargetProcessTree)
                        .WithFormat(MixFormat)
                        .WithBufferLength(30)
                        .WithMmcssThreadPriority("Pro Audio")
                        .BuildAsync()
                        .GetAwaiter()
                        .GetResult();

                    var buffer = NewBuffer();

                    recorder.DataAvailable +=
                        (data, _, _, _) =>
                            buffer.AddSamples(
                                data.ToArray(),
                                0,
                                data.Length);

                    mixer.AddMixerInput(
                        new VolumeSampleProvider(
                            buffer.ToSampleProvider())
                        {
                            Volume = 1f
                        });

                    recorder.StartRecording();
                    appRecorders.Add(recorder);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Could not capture {app.Name} ({app.Pid}): {ex.Message}");
                }
            }

            player = new WasapiPlayerBuilder()
                .WithDevice(outputDevice.Device)
                .WithSharedMode()
                .WithLatency(50)
                .WithMmcssThreadPriority("Pro Audio")
                .Build();

            player.Init(new SampleToWaveProvider16(mixer));
            player.Play();

            running = true;
            start.Text = "STOP MIXER";
            status.Text = $"Running — {appRecorders.Count} app(s) + mic";

            refresh.Enabled = false;
            apps.Enabled = false;
            mic.Enabled = false;
            output.Enabled = false;
        }
        catch (Exception ex)
        {
            StopMixer();

            MessageBox.Show(
                ex.ToString(),
                "Could not start mixer");
        }
    }

    BufferedWaveProvider NewBuffer()
    {
        var b = new BufferedWaveProvider(MixFormat)
        {
            DiscardOnBufferOverflow = true
        };

        sourceBuffers.Add(b);
        return b;
    }

    void StopMixer()
    {
        running = false;

        try
        {
            micRecorder?.StopRecording();
        }
        catch
        {
        }

        try
        {
            foreach (var r in appRecorders)
                r.StopRecording();
        }
        catch
        {
        }

        try
        {
            player?.Stop();
        }
        catch
        {
        }

        micRecorder?.Dispose();
        micRecorder = null;

        foreach (var r in appRecorders)
            r.Dispose();

        appRecorders.Clear();

        player?.Dispose();
        player = null;

        sourceBuffers.Clear();
        mixer = null;

        start.Text = "START MIXER";
        status.Text = "Stopped";

        refresh.Enabled = true;
        apps.Enabled = true;
        mic.Enabled = true;
        output.Enabled = true;
    }

    sealed class DeviceItem(MMDevice device)
    {
        public MMDevice Device { get; } = device;

        public override string ToString()
        {
            return Device.FriendlyName;
        }
    }

    sealed record AppItem(
        int Pid,
        string Name,
        string WindowTitle)
    {
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(WindowTitle)
                ? $"{Name}  (PID {Pid})"
                : $"{Name} — {WindowTitle}  (PID {Pid})";
        }
    }
}