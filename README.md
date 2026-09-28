# AliVirtualMic
application that feeds live mic feed from applications to your mic
# 🎙️ Ali Virtual Mic

**Ali Virtual Mic** is a Windows audio mixer that combines your **physical microphone** with audio from selected applications and sends the combined audio through a virtual audio cable.

This allows you to send your **voice + Spotify + game audio + other application audio** into applications such as **TikTok Live Studio, Discord, OBS, and other programs that accept microphone input**.

## ✨ Features

- 🎤 Mixes your physical microphone with application audio
- 🎵 Select individual applications such as Spotify
- 🎮 Capture game audio
- 💬 Send Discord/application audio through the mix
- 🔊 Adjustable microphone volume
- 🔄 Refresh the available applications and audio devices
- ⚡ Uses Windows per-application audio loopback
- 🪟 Designed for Windows 10/11
- 🆓 Open-source project

## ⚠️ Required: VB-CABLE

**Ali Virtual Mic requires VB-CABLE to work as a virtual microphone.**

Ali Virtual Mic itself mixes the audio, but Windows does not allow a normal application to create a new microphone device by itself. VB-CABLE provides the virtual audio device that carries the mixed audio into other applications.

### Download VB-CABLE

Download VB-CABLE from the official VB-Audio website:

https://vb-audio.com/Cable/

Download:

**VBCABLE_Driver_Pack45.zip**

### Installing VB-CABLE

1. Download the VB-CABLE ZIP file from the official website.
2. Extract the ZIP file.
3. Open the extracted folder.
4. For 64-bit Windows, run the 64-bit installation program.
5. Right-click the installer and select **Run as administrator**.
6. Click **Install Driver**.
7. Restart your computer after installation.

After restarting, Windows should have two new audio devices:

- **CABLE Input (VB-Audio Virtual Cable)**
- **CABLE Output (VB-Audio Virtual Cable)**

> ⚠️ The names can initially seem backwards. This is normal for VB-CABLE.

## 🔧 Setting Up Ali Virtual Mic

### 1. Open Ali Virtual Mic

Run:

`AliVirtualMic.exe`

### 2. Select your microphone

Under **Microphone**, select your physical microphone.

For example:

`Razer Nari Essential`

### 3. Select the virtual cable

Under:

**Output device / virtual cable**

select:

`CABLE Input (VB-Audio Virtual Cable)`

### 4. Select your applications

Under:

**Applications to send through the virtual mic**

select the applications whose audio you want to send through the virtual microphone.

For example:

- ☑ Spotify
- ☑ Your game
- ☑ Chrome
- ☐ Discord

Only select the applications you want included.

If an application does not appear, open the application and start playing audio, then press **Refresh**.

### 5. Start the mixer

Click:

**START MIXER**

The status should change to something similar to:

`Running — 1 app(s) + mic`

## 🎤 Setting Up TikTok / Discord / OBS

In the application where you want to use the mixed audio, select:

**CABLE Output (VB-Audio Virtual Cable)**

as your microphone/input device.

### The complete audio path

```text
                    ┌──────────────────┐
🎤 Physical Mic ───►│                  │
                    │  Ali Virtual Mic │──► CABLE Input
🎵 Spotify ─────────►│      MIXER       │
                    │                  │
🎮 Game ────────────►│                  │
                    └──────────────────┘
                              │
                              ▼
                    VB-CABLE Virtual Device
                              │
                              ▼
                       CABLE Output
                              │
               ┌──────────────┼──────────────┐
               ▼              ▼              ▼
             TikTok         Discord          OBS
```

## 🎵 Example: Spotify + Microphone

If you want your TikTok stream to hear both your voice and Spotify:

### Ali Virtual Mic

**Microphone:**

`Razer Nari Essential`

**Output:**

`CABLE Input (VB-Audio Virtual Cable)`

**Applications:**

`☑ Spotify`

Then click:

**START MIXER**

### TikTok

Set your microphone/input device to:

`CABLE Output (VB-Audio Virtual Cable)`

Now the audio sent to TikTok will contain both your microphone and Spotify.

## 🎮 Example: Game + Microphone

For game audio:

1. Start your game.
2. Start playing audio.
3. Click **Refresh** in Ali Virtual Mic.
4. Find the game in the application list.
5. Tick the game.
6. Make sure your physical microphone is selected.
7. Select **CABLE Input** as the output.
8. Click **START MIXER**.
9. Select **CABLE Output** as the microphone in TikTok/Discord/OBS.

## 🔇 Troubleshooting

### I can hear my microphone but not Spotify

Check that:

- Spotify is playing audio.
- Spotify is selected in Ali Virtual Mic.
- **CABLE Input** is selected as Ali Virtual Mic's output.
- **CABLE Output** is selected as the microphone in the destination application.
- You pressed **Refresh** after opening Spotify.

### Spotify appears multiple times

Spotify can run multiple processes. Select the Spotify process associated with the active Spotify application rather than background/helper processes.

### TikTok/Discord cannot hear anything

Make sure the application's microphone is set to:

`CABLE Output (VB-Audio Virtual Cable)`

Do **not** select CABLE Input as the microphone.

### I don't see CABLE Input or CABLE Output

VB-CABLE has probably not been installed correctly.

Re-run the VB-CABLE installer as administrator and restart Windows.

### I hear an echo

Make sure you aren't monitoring the same audio through multiple paths.

For example, avoid simultaneously sending the mixed audio to your speakers/headphones and then capturing those speakers again.

## 🖥️ Requirements

- Windows 10 version 2004 or newer
- Windows 11
- 64-bit Windows
- .NET 9 runtime is **not required** when using the self-contained release
- VB-CABLE
- A working microphone

## 📦 Building From Source

Clone the repository and open a terminal in the `src` directory.

Build a release:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

The executable will be created in:

```text
bin\Release\net9.0-windows10.0.19041.0\win-x64\publish\
```

The main executable is:

```text
AliVirtualMic.exe
```

## 🛠️ Technology

Ali Virtual Mic is built using:

- C#
- .NET 9
- Windows Forms
- NAudio
- Windows WASAPI
- Windows per-process loopback capture
- VB-Audio VB-CABLE

## ⚠️ Important Dependency Notice

**VB-CABLE is not included with Ali Virtual Mic.**

It is a separate virtual audio driver developed by **VB-Audio**.

You must install VB-CABLE separately before using Ali Virtual Mic as a virtual microphone.

Official VB-CABLE website:

https://vb-audio.com/Cable/

Please follow the licensing and distribution terms provided by VB-Audio.

## 📄 License

Add your preferred open-source license to this repository, such as MIT, before distributing the project.

---

### Ali Virtual Mic

**Mix your voice. Mix your apps. One virtual microphone.** 🎤🎵🎮
