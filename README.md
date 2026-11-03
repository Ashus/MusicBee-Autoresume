# Autoresume

A MusicBee plugin that starts playback on launch and reconnects internet radio after connection errors. It can also help resume playback when your computer wakes from sleep or hibernation.

## Installation

1. Close MusicBee.
2. Copy `MB_Autoresume.dll` into the `Plugins` folder inside your MusicBee installation. When updating, replace the existing DLL.
3. Start MusicBee and find **Autoresume** in Preferences under Plugins. Adjust its options and save your changes.

Only the DLL is needed. The plugin requires Windows and .NET Framework 4.8 and supports 32-bit MusicBee. It has been tested by the user with **MusicBee 3.7.9686 P**.

## Settings

| Option | Default | Behavior |
| --- | --- | --- |
| Autoplay on MusicBee launch | On | Starts the current item restored by MusicBee after launch. |
| Resume playing after sleep | Off | Attempts to resume after sleep or hibernation if playback was active before sleep. |
| Resume playing after connection error | On | Dismisses recognized radio connection errors and tries the next item in the playing queue. |

Your choices are saved across restarts and updates. Updates do not overwrite existing settings. The settings panel is available in English and Czech, following your MusicBee language; other languages use English.

### Autoplay on launch

After a short delay, Autoresume starts the item restored by MusicBee, even if playback was paused or stopped before you closed the player. If MusicBee is already playing, playback continues uninterrupted. If there is no current item, nothing is added to the queue.

This option does not launch MusicBee when you sign in to Windows. MusicBee handles restoring the queue and track position.

### Radio connection recovery

MusicBee may handle short interruptions itself. If it displays a recognized error for an unavailable station, a connection timeout, or an unresponsive stream, Autoresume dismisses the message and tries the next item in the current playing queue after a delay. At the end of the queue, it returns to the first item. With a single radio station in the queue, it retries that same station.

Recovery works **whether Repeat is on or off**. You can use it with one favorite station or a list of alternative streams, such as an M3U playlist. After each unsuccessful pass through the queue, the retry delay increases from 3 seconds up to 60 seconds. Retries continue without a fixed attempt limit, but playback can only resume when a source becomes available.

Pressing Stop or Pause, selecting another item, changing the queue, or disabling the option cancels a pending attempt. Autoresume does not clear or rewrite your queue. In a queue containing both streams and local files, recovery may start a local file. If the same URL appears more than once, MusicBee may select its first occurrence.

Only recognized network errors are dismissed. Other errors, including audio output device and unsupported format errors, remain for you to review. Autoresume does not switch output devices when a Bluetooth speaker disconnects. Recovery after reconnecting a Bluetooth speaker has not been verified.

### Resume after sleep

MusicBee resumed playback by itself in the tested scenario, so this option is off by default. Enable it as a fallback if MusicBee remains stopped after waking.

When enabled, it only attempts to restore playback that was active before sleep. It waits at least 3 seconds after waking and allows up to 60 seconds for loading or network availability. Playback that has already resumed is left uninterrupted.

This option does not disable MusicBee's own resume behavior. It therefore cannot prevent MusicBee itself from starting playback unexpectedly. Whether it helps when MusicBee retries a stream before the network is ready has not yet been established.

## Settings and troubleshooting logs

Settings and logs are stored in the `MusicBee-Autoresume` folder within MusicBee's data directory. For a portable installation, this is usually:

```text
<MusicBee>\AppData\MusicBee-Autoresume\
```

`settings.xml` stores your choices. If you need to report a problem, include `autoresume.log` and, if present, `autoresume.log.1`, along with your MusicBee version and a description of what happened.

Logs are in English. At most two log files are kept, each approximately 256 KiB. They do not contain stream URLs, station names, or error message text.

If an error dialog remains open, it may be an unrecognized message. Dismiss it manually and restart playback if needed.

## Disabling or uninstalling

You can turn off each feature independently in the plugin settings, or disable or uninstall the entire plugin from MusicBee's plugin list.

Saved settings are kept for reinstallation. To remove all plugin data, close MusicBee and delete the `MusicBee-Autoresume` folder from its data directory.
