using System.Diagnostics;
using System.Runtime.InteropServices;
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

    readonly WaveFormat MixFormat =
        WaveFormat.CreateIeeeFloatWaveFormat(
            SampleRate,
            Channels);

    // =========================================================
    // MAIN UI
    // =========================================================

    readonly ComboBox mic =
        new() { DropDownStyle = ComboBoxStyle.DropDownList };

    readonly ComboBox output =
        new() { DropDownStyle = ComboBoxStyle.DropDownList };

    readonly CheckedListBox apps = new();

    readonly Button refresh =
        new() { Text = "Refresh" };

    readonly Button start =
        new() { Text = "START MIXER", Height = 44 };

    readonly TrackBar micVolume =
        new()
        {
            Minimum = 0,
            Maximum = 200,
            Value = 100,
            TickFrequency = 25
        };

    readonly Label micValue =
        new()
        {
            Text = "100%",
            AutoSize = true
        };

    readonly Label status =
        new()
        {
            Text = "Stopped"
        };

    // =========================================================
    // SOUNDBOARD UI
    // =========================================================

    readonly ListView soundList = new();

    readonly Button addSound =
        new()
        {
            Text = "ADD AUDIO"
        };

    readonly Button playSound =
        new()
        {
            Text = "PLAY"
        };

    readonly Button stopSound =
        new()
        {
            Text = "STOP"
        };

    readonly Button removeSound =
        new()
        {
            Text = "REMOVE"
        };

    readonly Button bindKey =
        new()
        {
            Text = "BIND KEY"
        };

    readonly Label hotkeyInfo =
        new()
        {
            Text = "Select a sound, then click BIND KEY.",
            AutoSize = true
        };

    readonly TrackBar soundVolume =
        new()
        {
            Minimum = 0,
            Maximum = 100,
            Value = 100,
            TickFrequency = 10
        };

    readonly Label soundVolumeValue =
        new()
        {
            Text = "100%",
            AutoSize = true
        };

    readonly List<SoundItem> sounds = [];

    readonly string soundboardFolder =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments),
            "Ali Virtual Mic",
            "Soundboard");

    // =========================================================
    // MIXER
    // =========================================================

    WasapiRecorder? micRecorder;
    WasapiPlayer? player;
    MixingSampleProvider? mixer;

    readonly List<WasapiRecorder> appRecorders = [];

    readonly List<BufferedWaveProvider> sourceBuffers = [];

    // ---------------------------------------------------------
    // IMPORTANT:
    // The soundboard gets ONE permanent input into the mixer.
    // Sounds are decoded into this buffer instead of dynamically
    // adding/removing providers while WASAPI is playing.
    // ---------------------------------------------------------

    BufferedWaveProvider? soundboardBuffer;

    bool running;

    // =========================================================
    // HOTKEYS
    // =========================================================

    const int HotkeyBaseId = 5000;

    int nextHotkeyId = HotkeyBaseId;

    readonly Dictionary<int, SoundItem> registeredHotkeys = [];

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(
        IntPtr hWnd,
        int id);

    const uint MOD_NONE = 0;

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    public MainForm()
    {
        Text = "Ali Virtual Mic";

        Width = 900;
        Height = 850;

        MinimumSize =
            new Size(
                900,
                850);

        StartPosition =
            FormStartPosition.CenterScreen;

        BackColor =
            Color.FromArgb(
                24,
                24,
                28);

        ForeColor =
            Color.White;

        BuildInterface();

        Shown += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(
                    soundboardFolder);

                LoadSoundboard();
                RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.ToString(),
                    "Ali Virtual Mic");
            }
        };

        FormClosing += (_, _) =>
        {
            StopMixer();

            UnregisterAllHotkeys();

            foreach (var sound in sounds)
                sound.Dispose();
        };
    }

    // =========================================================
    // UI
    // =========================================================

    void BuildInterface()
    {
        var title =
            new Label
            {
                Text = "ALI VIRTUAL MIC",

                Font =
                    new Font(
                        "Segoe UI",
                        21,
                        FontStyle.Bold),

                AutoSize = true,

                Location =
                    new Point(
                        25,
                        15)
            };

        Controls.Add(title);

        var sub =
            new Label
            {
                Text =
                    "Microphone + applications + soundboard → virtual microphone",

                AutoSize = true,

                Location =
                    new Point(
                        28,
                        50),

                ForeColor =
                    Color.Silver
            };

        Controls.Add(sub);

        // =====================================================
        // DEVICES
        // =====================================================

        AddLabel(
            "Microphone",
            28,
            82);

        mic.SetBounds(
            28,
            106,
            820,
            32);

        Controls.Add(mic);

        AddLabel(
            "Output device / virtual cable",
            28,
            150);

        output.SetBounds(
            28,
            174,
            820,
            32);

        Controls.Add(output);

        // =====================================================
        // APPLICATIONS
        // =====================================================

        AddLabel(
            "Open applications to send through the virtual mic",
            28,
            220);

        apps.SetBounds(
            28,
            245,
            820,
            145);

        apps.CheckOnClick = true;

        Controls.Add(apps);

        // =====================================================
        // MIC VOLUME
        // =====================================================

        AddLabel(
            "Microphone volume",
            28,
            405);

        micVolume.SetBounds(
            160,
            397,
            550,
            40);

        micVolume.Scroll += (_, _) =>
        {
            micValue.Text =
                $"{micVolume.Value}%";
        };

        Controls.Add(micVolume);

        micValue.Location =
            new Point(
                725,
                409);

        Controls.Add(micValue);

        // =====================================================
        // SOUNDBOARD
        // =====================================================

        var soundTitle =
            new Label
            {
                Text = "SOUNDBOARD",

                Font =
                    new Font(
                        "Segoe UI",
                        15,
                        FontStyle.Bold),

                AutoSize = true,

                Location =
                    new Point(
                        28,
                        455)
            };

        Controls.Add(soundTitle);

        var soundSub =
            new Label
            {
                Text =
                    "Import sounds and trigger them with buttons or global keyboard shortcuts.",

                AutoSize = true,

                Location =
                    new Point(
                        30,
                        483),

                ForeColor =
                    Color.Silver
            };

        Controls.Add(soundSub);

        soundList.SetBounds(
            28,
            512,
            820,
            120);

        soundList.View =
            View.Details;

        soundList.FullRowSelect = true;
        soundList.GridLines = true;
        soundList.MultiSelect = false;

        soundList.Columns.Add(
            "Sound",
            300);

        soundList.Columns.Add(
            "Hotkey",
            120);

        soundList.Columns.Add(
            "Volume",
            100);

        soundList.Columns.Add(
            "File",
            280);

        soundList.SelectedIndexChanged +=
            (_, _) =>
                SoundSelectionChanged();

        soundList.DoubleClick +=
            (_, _) =>
                PlaySelectedSound();

        Controls.Add(soundList);

        addSound.SetBounds(
            28,
            645,
            120,
            35);

        addSound.Click +=
            (_, _) =>
                AddAudio();

        Controls.Add(addSound);

        playSound.SetBounds(
            158,
            645,
            90,
            35);

        playSound.Click +=
            (_, _) =>
                PlaySelectedSound();

        Controls.Add(playSound);

        stopSound.SetBounds(
            258,
            645,
            90,
            35);

        stopSound.Click +=
            (_, _) =>
                StopSelectedSound();

        Controls.Add(stopSound);

        removeSound.SetBounds(
            358,
            645,
            100,
            35);

        removeSound.Click +=
            (_, _) =>
                RemoveSelectedSound();

        Controls.Add(removeSound);

        bindKey.SetBounds(
            468,
            645,
            110,
            35);

        bindKey.Click +=
            (_, _) =>
                BindSelectedSound();

        Controls.Add(bindKey);

        hotkeyInfo.Location =
            new Point(
                595,
                654);

        hotkeyInfo.ForeColor =
            Color.Silver;

        Controls.Add(hotkeyInfo);

        AddLabel(
            "Sound volume",
            28,
            695);

        soundVolume.SetBounds(
            145,
            687,
            500,
            40);

        soundVolume.Scroll +=
            (_, _) =>
            {
                soundVolumeValue.Text =
                    $"{soundVolume.Value}%";

                UpdateSelectedSoundVolume();
            };

        Controls.Add(soundVolume);

        soundVolumeValue.Location =
            new Point(
                660,
                699);

        Controls.Add(soundVolumeValue);

        // =====================================================
        // CONTROL BUTTONS
        // =====================================================

        refresh.SetBounds(
            28,
            750,
            120,
            38);

        refresh.Click +=
            (_, _) =>
                RefreshAll();

        Controls.Add(refresh);

        start.SetBounds(
            165,
            747,
            250,
            44);

        start.Click +=
            (_, _) =>
                Toggle();

        Controls.Add(start);

        status.SetBounds(
            435,
            758,
            400,
            25);

        Controls.Add(status);

        var help =
            new Label
            {
                Text =
                    "Use CABLE Input as the mixer output, then select CABLE Output as your microphone in Discord, OBS or TikTok.",

                AutoSize = false,

                Width = 820,

                Height = 40,

                Location =
                    new Point(
                        28,
                        795),

                ForeColor =
                    Color.Silver
            };

        Controls.Add(help);
    }

    void AddLabel(
        string text,
        int x,
        int y)
    {
        Controls.Add(
            new Label
            {
                Text = text,

                AutoSize = true,

                Location =
                    new Point(
                        x,
                        y),

                ForeColor =
                    Color.Gainsboro
            });
    }

    // =========================================================
    // DEVICE REFRESH
    // =========================================================

    void RefreshAll()
    {
        try
        {
            using var e =
                new MMDeviceEnumerator();

            mic.Items.Clear();
            output.Items.Clear();

            foreach (var d in
                e.EnumerateAudioEndPoints(
                    DataFlow.Capture,
                    DeviceState.Active))
            {
                mic.Items.Add(
                    new DeviceItem(d));
            }

            foreach (var d in
                e.EnumerateAudioEndPoints(
                    DataFlow.Render,
                    DeviceState.Active))
            {
                output.Items.Add(
                    new DeviceItem(d));
            }

            if (mic.Items.Count > 0)
                mic.SelectedIndex = 0;

            int cable = -1;

            for (int i = 0;
                 i < output.Items.Count;
                 i++)
            {
                if (output.Items[i]
                    .ToString()!
                    .Contains(
                        "CABLE Input",
                        StringComparison.OrdinalIgnoreCase))
                {
                    cable = i;
                    break;
                }
            }

            output.SelectedIndex =
                cable >= 0
                    ? cable
                    : output.Items.Count > 0
                        ? 0
                        : -1;

            RefreshApps();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Ali Virtual Mic");
        }
    }

    void RefreshApps()
    {
        var old =
            apps.CheckedItems
                .Cast<AppItem>()
                .Select(x => x.Pid)
                .ToHashSet();

        apps.Items.Clear();

        var list =
            Process.GetProcesses()
                .Where(p =>
                {
                    try
                    {
                        return
                            p.MainWindowHandle !=
                                IntPtr.Zero
                            &&
                            !string.IsNullOrWhiteSpace(
                                p.MainWindowTitle);
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
                .OrderBy(
                    x => x.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    x => x.Pid)
                .ToList();

        foreach (var item in list)
        {
            apps.Items.Add(
                item,
                old.Contains(item.Pid));
        }
    }

    // =========================================================
    // MIXER
    // =========================================================

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

        var selectedApps =
            apps.CheckedItems
                .Cast<AppItem>()
                .ToList();

        try
        {
            mixer =
                new MixingSampleProvider(
                    MixFormat)
                {
                    ReadFully = true
                };

            // =================================================
            // MICROPHONE
            // =================================================

            micRecorder =
                new WasapiRecorderBuilder()
                    .WithDevice(
                        micDevice.Device)
                    .WithSharedMode()
                    .WithFormat(
                        MixFormat)
                    .WithBufferLength(30)
                    .WithMmcssThreadPriority(
                        "Pro Audio")
                    .Build();

            var micBuffer =
                NewBuffer();

            micRecorder.DataAvailable +=
                (buffer, _, _, _) =>
                {
                    try
                    {
                        micBuffer.AddSamples(
                            buffer.ToArray(),
                            0,
                            buffer.Length);
                    }
                    catch
                    {
                    }
                };

            mixer.AddMixerInput(
                new VolumeSampleProvider(
                    micBuffer.ToSampleProvider())
                {
                    Volume =
                        micVolume.Value /
                        100f
                });

            micRecorder.StartRecording();

            // =================================================
            // APPLICATION AUDIO
            // =================================================

            foreach (var app in selectedApps)
            {
                try
                {
                    var recorder =
                        new WasapiRecorderBuilder()
                            .WithProcessLoopback(
                                (uint)app.Pid,
                                ProcessLoopbackMode
                                    .IncludeTargetProcessTree)
                            .WithFormat(
                                MixFormat)
                            .WithBufferLength(30)
                            .WithMmcssThreadPriority(
                                "Pro Audio")
                            .BuildAsync()
                            .GetAwaiter()
                            .GetResult();

                    var buffer =
                        NewBuffer();

                    recorder.DataAvailable +=
                        (data, _, _, _) =>
                        {
                            try
                            {
                                buffer.AddSamples(
                                    data.ToArray(),
                                    0,
                                    data.Length);
                            }
                            catch
                            {
                            }
                        };

                    mixer.AddMixerInput(
                        new VolumeSampleProvider(
                            buffer.ToSampleProvider())
                        {
                            Volume = 1f
                        });

                    recorder.StartRecording();

                    appRecorders.Add(
                        recorder);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Could not capture " +
                        $"{app.Name} ({app.Pid}): " +
                        ex.Message);
                }
            }

            // =================================================
            // PERMANENT SOUNDBOARD INPUT
            // =================================================

            soundboardBuffer =
                new BufferedWaveProvider(
                    MixFormat)
                {
                    DiscardOnBufferOverflow = true,

                    BufferDuration =
                        TimeSpan.FromSeconds(30)
                };

            mixer.AddMixerInput(
                soundboardBuffer.ToSampleProvider());

            // =================================================
            // OUTPUT
            // =================================================

            player =
                new WasapiPlayerBuilder()
                    .WithDevice(
                        outputDevice.Device)
                    .WithSharedMode()
                    .WithLatency(50)
                    .WithMmcssThreadPriority(
                        "Pro Audio")
                    .Build();

            player.Init(
                new SampleToWaveProvider16(
                    mixer));

            player.Play();

            running = true;

            start.Text =
                "STOP MIXER";

            status.Text =
                $"Running — {appRecorders.Count} app(s) + mic + soundboard";

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
        var buffer =
            new BufferedWaveProvider(
                MixFormat)
            {
                DiscardOnBufferOverflow =
                    true
            };

        sourceBuffers.Add(
            buffer);

        return buffer;
    }

    void StopMixer()
    {
        running = false;

        // Stop all currently playing soundboard files.
        foreach (var sound in sounds)
        {
            try
            {
                sound.Stop();
            }
            catch
            {
            }
        }

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
        {
            try
            {
                r.Dispose();
            }
            catch
            {
            }
        }

        appRecorders.Clear();

        try
        {
            player?.Dispose();
        }
        catch
        {
        }

        player = null;

        sourceBuffers.Clear();

        soundboardBuffer = null;

        mixer = null;

        start.Text =
            "START MIXER";

        status.Text =
            "Stopped";

        refresh.Enabled = true;
        apps.Enabled = true;
        mic.Enabled = true;
        output.Enabled = true;
    }

    // =========================================================
    // SOUNDBOARD
    // =========================================================

    void AddAudio()
    {
        Directory.CreateDirectory(
            soundboardFolder);

        using var dialog =
            new OpenFileDialog
            {
                Title =
                    "Add sound to Ali Virtual Mic",

                Multiselect = true,

                Filter =
                    "Audio Files|*.mp3;*.wav;*.aiff;*.aif;*.flac;*.m4a|" +
                    "MP3 Files|*.mp3|" +
                    "WAV Files|*.wav|" +
                    "AIFF Files|*.aiff;*.aif|" +
                    "FLAC Files|*.flac|" +
                    "M4A Files|*.m4a|" +
                    "All Files|*.*"
            };

        if (dialog.ShowDialog() !=
            DialogResult.OK)
        {
            return;
        }

        foreach (var sourceFile in dialog.FileNames)
        {
            try
            {
                string ext =
                    Path.GetExtension(
                        sourceFile);

                string name =
                    Path.GetFileNameWithoutExtension(
                        sourceFile);

                string destination =
                    Path.Combine(
                        soundboardFolder,
                        Path.GetFileName(
                            sourceFile));

                int n = 1;

                while (File.Exists(destination))
                {
                    destination =
                        Path.Combine(
                            soundboardFolder,
                            $"{name} ({n}){ext}");

                    n++;
                }

                File.Copy(
                    sourceFile,
                    destination);

                using (var test =
                    new AudioFileReader(
                        destination))
                {
                    _ = test.WaveFormat;
                }

                var sound =
                    new SoundItem(
                        Path.GetFileNameWithoutExtension(
                            destination),
                        destination);

                sounds.Add(sound);

                AddSoundToList(sound);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not add:\n" +
                    $"{sourceFile}\n\n" +
                    $"{ex.Message}",
                    "Soundboard");
            }
        }
    }

    void LoadSoundboard()
    {
        sounds.Clear();

        soundList.Items.Clear();

        if (!Directory.Exists(
                soundboardFolder))
        {
            return;
        }

        foreach (var file in
            Directory.EnumerateFiles(
                soundboardFolder))
        {
            try
            {
                using var test =
                    new AudioFileReader(file);

                var sound =
                    new SoundItem(
                        Path.GetFileNameWithoutExtension(
                            file),
                        file);

                sounds.Add(sound);

                AddSoundToList(sound);
            }
            catch
            {
                // Ignore unsupported files.
            }
        }
    }

    void AddSoundToList(
        SoundItem sound)
    {
        var item =
            new ListViewItem(
                sound.Name);

        item.SubItems.Add(
            sound.HotkeyName ??
            "None");

        item.SubItems.Add(
            $"{sound.Volume}%");

        item.SubItems.Add(
            sound.FilePath);

        item.Tag = sound;

        soundList.Items.Add(item);
    }

    SoundItem? GetSelectedSound()
    {
        if (soundList.SelectedItems.Count == 0)
            return null;

        return soundList
            .SelectedItems[0]
            .Tag as SoundItem;
    }

    void SoundSelectionChanged()
    {
        var sound =
            GetSelectedSound();

        if (sound == null)
            return;

        soundVolume.Value =
            Math.Clamp(
                sound.Volume,
                0,
                100);

        soundVolumeValue.Text =
            $"{sound.Volume}%";

        hotkeyInfo.Text =
            sound.HotkeyName == null
                ? "No hotkey assigned."
                : $"Hotkey: {sound.HotkeyName}";
    }

    void UpdateSelectedSoundVolume()
    {
        var sound =
            GetSelectedSound();

        if (sound == null)
            return;

        sound.Volume =
            soundVolume.Value;

        sound.UpdateVolume();

        if (soundList.SelectedItems.Count > 0)
        {
            soundList
                .SelectedItems[0]
                .SubItems[2]
                .Text =
                $"{sound.Volume}%";
        }
    }

    void PlaySelectedSound()
    {
        var sound =
            GetSelectedSound();

        if (sound == null)
        {
            MessageBox.Show(
                "Select a sound first.",
                "Soundboard");

            return;
        }

        PlaySound(sound);
    }

    void StopSelectedSound()
    {
        var sound =
            GetSelectedSound();

        if (sound == null)
            return;

        sound.Stop();

        ClearSoundboardBuffer();
    }

    void PlaySound(
        SoundItem sound)
    {
        if (!running ||
            mixer == null ||
            soundboardBuffer == null)
        {
            MessageBox.Show(
                "Start the mixer first.",
                "Soundboard");

            return;
        }

        try
        {
            // Stop this sound if it is already playing.
            sound.Stop();

            sound.Play(
                MixFormat,
                soundboardBuffer);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not play {sound.Name}:\n\n" +
                $"{ex.Message}",
                "Soundboard");
        }
    }

    void ClearSoundboardBuffer()
    {
        try
        {
            soundboardBuffer?.ClearBuffer();
        }
        catch
        {
        }
    }

    void RemoveSelectedSound()
    {
        var sound =
            GetSelectedSound();

        if (sound == null)
            return;

        UnregisterHotkey(sound);

        sound.Stop();

        sounds.Remove(sound);

        if (soundList.SelectedItems.Count > 0)
        {
            soundList.Items.Remove(
                soundList.SelectedItems[0]);
        }

        sound.Dispose();

        try
        {
            if (File.Exists(
                    sound.FilePath) &&
                sound.FilePath.StartsWith(
                    soundboardFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(
                    sound.FilePath);
            }
        }
        catch
        {
        }
    }

    // =========================================================
    // HOTKEY BINDING
    // =========================================================

    void BindSelectedSound()
    {
        var sound =
            GetSelectedSound();

        if (sound == null)
        {
            MessageBox.Show(
                "Select a sound first.",
                "Soundboard");

            return;
        }

        using var dialog =
            new HotkeyForm(
                sound.HotkeyName);

        if (dialog.ShowDialog(this) !=
            DialogResult.OK)
        {
            return;
        }

        UnregisterHotkey(sound);

        if (dialog.SelectedKey == Keys.None)
        {
            sound.Hotkey =
                Keys.None;

            sound.HotkeyName =
                null;
        }
        else
        {
            int id =
                nextHotkeyId++;

            if (!RegisterHotKey(
                    Handle,
                    id,
                    MOD_NONE,
                    (uint)dialog.SelectedKey))
            {
                MessageBox.Show(
                    "Windows could not register that key.\n\n" +
                    "Try another key.",
                    "Soundboard");

                return;
            }

            registeredHotkeys[id] =
                sound;

            sound.Hotkey =
                dialog.SelectedKey;

            sound.HotkeyName =
                dialog.SelectedKey.ToString();
        }

        UpdateSoundListRow(sound);

        hotkeyInfo.Text =
            sound.HotkeyName == null
                ? "No hotkey assigned."
                : $"Hotkey: {sound.HotkeyName}";
    }

    void UpdateSoundListRow(
        SoundItem sound)
    {
        foreach (ListViewItem item in
            soundList.Items)
        {
            if (ReferenceEquals(
                    item.Tag,
                    sound))
            {
                item.SubItems[1].Text =
                    sound.HotkeyName ??
                    "None";

                break;
            }
        }
    }

    void UnregisterHotkey(
        SoundItem sound)
    {
        var pair =
            registeredHotkeys
                .FirstOrDefault(
                    x =>
                        ReferenceEquals(
                            x.Value,
                            sound));

        if (pair.Key != 0)
        {
            UnregisterHotKey(
                Handle,
                pair.Key);

            registeredHotkeys.Remove(
                pair.Key);
        }
    }

    void UnregisterAllHotkeys()
    {
        foreach (var id in
            registeredHotkeys.Keys.ToList())
        {
            UnregisterHotKey(
                Handle,
                id);
        }

        registeredHotkeys.Clear();
    }

    protected override void WndProc(
        ref Message m)
    {
        const int WM_HOTKEY =
            0x0312;

        if (m.Msg == WM_HOTKEY)
        {
            int id =
                m.WParam.ToInt32();

            if (registeredHotkeys.TryGetValue(
                    id,
                    out var sound))
            {
                PlaySound(sound);
            }
        }

        base.WndProc(ref m);
    }

    // =========================================================
    // DEVICE / APP CLASSES
    // =========================================================

    sealed class DeviceItem(
        MMDevice device)
    {
        public MMDevice Device { get; } =
            device;

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
            if (string.IsNullOrWhiteSpace(
                    WindowTitle))
            {
                return
                    $"{Name} (PID {Pid})";
            }

            return
                $"{Name} — {WindowTitle} (PID {Pid})";
        }
    }
}

// =============================================================
// SOUND ITEM
// =============================================================

public sealed class SoundItem : IDisposable
{
    public string Name { get; }

    public string FilePath { get; }

    public int Volume { get; set; } = 100;

    public Keys Hotkey { get; set; } =
        Keys.None;

    public string? HotkeyName { get; set; }

    AudioFileReader? reader;

    VolumeSampleProvider? volumeProvider;

    CancellationTokenSource? playbackCancellation;

    Task? playbackTask;

    readonly object playbackLock = new();

    public SoundItem(
        string name,
        string filePath)
    {
        Name = name;
        FilePath = filePath;
    }

    // =========================================================
    // PLAY
    // =========================================================

    public void Play(
        WaveFormat mixFormat,
        BufferedWaveProvider targetBuffer)
    {
        if (!File.Exists(FilePath))
        {
            throw new FileNotFoundException(
                "The sound file could not be found.",
                FilePath);
        }

        lock (playbackLock)
        {
            StopInternal();

            reader =
                new AudioFileReader(
                    FilePath);

            ISampleProvider converted =
                reader.ToSampleProvider();

            // -------------------------------------------------
            // CHANNEL CONVERSION
            // -------------------------------------------------

            if (converted.WaveFormat.Channels == 1 &&
                mixFormat.Channels == 2)
            {
                converted =
                    new MonoToStereoSampleProvider(
                        converted);
            }
            else if (converted.WaveFormat.Channels == 2 &&
                     mixFormat.Channels == 1)
            {
                converted =
                    new StereoToMonoSampleProvider(
                        converted);
            }
            else if (converted.WaveFormat.Channels !=
                     mixFormat.Channels)
            {
                throw new InvalidOperationException(
                    $"Unsupported channel count: " +
                    $"{converted.WaveFormat.Channels}.");
            }

            // -------------------------------------------------
            // SAMPLE RATE CONVERSION
            // -------------------------------------------------

            if (converted.WaveFormat.SampleRate !=
                mixFormat.SampleRate)
            {
                converted =
                    new WdlResamplingSampleProvider(
                        converted,
                        mixFormat.SampleRate);
            }

            // -------------------------------------------------
            // VOLUME
            // -------------------------------------------------

            volumeProvider =
                new VolumeSampleProvider(
                    converted)
                {
                    Volume =
                        Math.Clamp(
                            Volume,
                            0,
                            100) / 100f
                };

            playbackCancellation =
                new CancellationTokenSource();

            CancellationToken token =
                playbackCancellation.Token;

            // -------------------------------------------------
            // AUDIO COPY TASK
            // -------------------------------------------------

            playbackTask =
                Task.Run(
                    () =>
                        CopyAudioToMixer(
                            mixFormat,
                            targetBuffer,
                            token),
                    token);
        }
    }

    // =========================================================
    // COPY AUDIO INTO PERMANENT MIXER BUFFER
    // =========================================================

    void CopyAudioToMixer(
        WaveFormat mixFormat,
        BufferedWaveProvider targetBuffer,
        CancellationToken token)
    {
        try
        {
            if (volumeProvider == null)
                return;

            var waveProvider =
                new SampleToWaveProvider(
                    volumeProvider);

            byte[] buffer =
                new byte[
                    mixFormat.AverageBytesPerSecond /
                    10];

            while (!token.IsCancellationRequested)
            {
                int bytesRead =
                    waveProvider.Read(
                        buffer,
                        0,
                        buffer.Length);

                if (bytesRead <= 0)
                    break;

                targetBuffer.AddSamples(
                    buffer,
                    0,
                    bytesRead);

                // Small wait prevents the decoder from
                // filling the entire 30-second buffer
                // instantly.
                Thread.Sleep(5);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"Soundboard playback error: {ex}");
        }
        finally
        {
            lock (playbackLock)
            {
                if (reader != null)
                {
                    try
                    {
                        reader.Dispose();
                    }
                    catch
                    {
                    }

                    reader = null;
                }

                volumeProvider = null;

                playbackCancellation?.Dispose();
                playbackCancellation = null;

                playbackTask = null;
            }
        }
    }

    // =========================================================
    // VOLUME
    // =========================================================

    public void UpdateVolume()
    {
        lock (playbackLock)
        {
            if (volumeProvider != null)
            {
                volumeProvider.Volume =
                    Math.Clamp(
                        Volume,
                        0,
                        100) / 100f;
            }
        }
    }

    // =========================================================
    // STOP
    // =========================================================

    public void Stop()
    {
        lock (playbackLock)
        {
            StopInternal();
        }
    }

    void StopInternal()
    {
        try
        {
            playbackCancellation?.Cancel();
        }
        catch
        {
        }

        try
        {
            reader?.Dispose();
        }
        catch
        {
        }

        reader = null;

        volumeProvider = null;

        playbackCancellation?.Dispose();

        playbackCancellation = null;

        playbackTask = null;
    }

    // =========================================================
    // DISPOSE
    // =========================================================

    public void Dispose()
    {
        Stop();
    }
}

// =============================================================
// HOTKEY WINDOW
// =============================================================

public sealed class HotkeyForm : Form
{
    readonly Label label =
        new()
        {
            Text =
                "Press the key you want to bind.",

            AutoSize = true,

            Location =
                new Point(
                    20,
                    20)
        };

    public Keys SelectedKey { get; private set; } =
        Keys.None;

    public HotkeyForm(
        string? currentKey)
    {
        Text =
            "Bind Soundboard Key";

        Width = 400;
        Height = 180;

        StartPosition =
            FormStartPosition.CenterParent;

        BackColor =
            Color.FromArgb(
                24,
                24,
                28);

        ForeColor =
            Color.White;

        Controls.Add(label);

        Controls.Add(
            new Label
            {
                Text =
                    string.IsNullOrWhiteSpace(
                        currentKey)
                        ? "Current: None"
                        : $"Current: {currentKey}",

                AutoSize = true,

                Location =
                    new Point(
                        20,
                        50),

                ForeColor =
                    Color.Silver
            });

        var clear =
            new Button
            {
                Text = "CLEAR",

                Location =
                    new Point(
                        20,
                        90),

                Width = 100,
                Height = 32
            };

        clear.Click +=
            (_, _) =>
            {
                SelectedKey =
                    Keys.None;

                DialogResult =
                    DialogResult.OK;

                Close();
            };

        Controls.Add(clear);

        var cancel =
            new Button
            {
                Text = "CANCEL",

                Location =
                    new Point(
                        130,
                        90),

                Width = 100,
                Height = 32
            };

        cancel.Click +=
            (_, _) =>
            {
                DialogResult =
                    DialogResult.Cancel;

                Close();
            };

        Controls.Add(cancel);

        Controls.Add(
            new Label
            {
                Text =
                    "Allowed: F1–F12 or NumPad 0–9",

                AutoSize = true,

                Location =
                    new Point(
                        20,
                        135),

                ForeColor =
                    Color.Silver
            });

        KeyPreview = true;

        KeyDown +=
            (_, e) =>
            {
                if (e.KeyCode ==
                    Keys.Escape)
                {
                    DialogResult =
                        DialogResult.Cancel;

                    Close();

                    return;
                }

                if (IsAllowedKey(
                        e.KeyCode))
                {
                    SelectedKey =
                        e.KeyCode;

                    DialogResult =
                        DialogResult.OK;

                    Close();
                }
            };

        Shown +=
            (_, _) =>
                Activate();
    }

    protected override bool ProcessCmdKey(
        ref Message msg,
        Keys keyData)
    {
        Keys key =
            keyData & Keys.KeyCode;

        if (IsAllowedKey(key))
        {
            SelectedKey =
                key;

            DialogResult =
                DialogResult.OK;

            Close();

            return true;
        }

        return base.ProcessCmdKey(
            ref msg,
            keyData);
    }

    static bool IsAllowedKey(
        Keys key)
    {
        return
            (key >= Keys.F1 &&
             key <= Keys.F12)
            ||
            (key >= Keys.NumPad0 &&
             key <= Keys.NumPad9);
    }
}