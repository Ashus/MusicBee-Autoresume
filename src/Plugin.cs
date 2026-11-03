namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private readonly ResumeCoordinator _coordinator = new ResumeCoordinator();
        private readonly ConnectionRecovery _connection = new ConnectionRecovery();
        private readonly RadioErrorDialog _radioError = new RadioErrorDialog();
        private readonly SleepTracker _sleep = new SleepTracker();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private MusicBeeAdapter _adapter;
        private SettingsStore _store;
        private DiagnosticLog _log;
        private AutoresumeSettings _settings = new AutoresumeSettings();
        private System.Windows.Forms.Control _dispatcher;
        private System.Windows.Forms.Timer _timer;
        private System.Windows.Forms.FlowLayoutPanel _options;
        private UiText _text = UiText.English;
        private System.Windows.Forms.CheckBox _autostart;
        private System.Windows.Forms.CheckBox _afterSleep;
        private System.Windows.Forms.CheckBox _afterConnectionError;
        private PlaybackStatus _lastLoggedState = PlaybackStatus.Unknown;
        private string _configurationWarning;
        private string _lastDialogObservation;
        private volatile bool _closed;
        private bool _started;
        private bool _powerSubscribed;
        private bool _issuingPlayback;
        private bool _handlingErrorDialog;
        private int _handledErrors;
        private volatile bool _faulted;

        public PluginInfo Initialise(System.IntPtr apiInterfacePtr)
        {
            // Plugin lifecycle. Requirements and unverified host behavior are documented in docs/.
            if (_closed)
                Cleanup();
            _closed = false;
            _started = false;
            _faulted = false;
            _configurationWarning = null;
            _lastLoggedState = PlaybackStatus.Unknown;
            _lastDialogObservation = null;
            _sleep.Resume();
            try
            {
                _adapter = new MusicBeeAdapter(apiInterfacePtr);
                _text = _adapter.ReadUiText();
            }
            catch (System.Exception exception)
            {
                _faulted = true;
                _configurationWarning = "Initialization failed (" + exception.GetType().Name + "). Automatic playback recovery is disabled.";
                _log?.Write(_configurationWarning);
            }

            return new PluginInfo
            {
                PluginInfoVersion = PluginInfoVersion,
                Name = "Autoresume",
                Description = _text.Get("Description"),
                Author = "Ashus",
                TargetApplication = string.Empty,
                Type = PluginType.General,
                VersionMajor = (short)typeof(Plugin).Assembly.GetName().Version.Major,
                VersionMinor = (short)typeof(Plugin).Assembly.GetName().Version.Minor,
                Revision = (short)typeof(Plugin).Assembly.GetName().Version.Build,
                MinInterfaceVersion = MinInterfaceVersion,
                MinApiRevision = MinApiRevision,
                ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents,
                ConfigurationPanelHeight = 135
            };
        }

        public bool Configure(System.IntPtr panelHandle)
        {
            Guard(() =>
            {
                var panel = System.Windows.Forms.Control.FromHandle(panelHandle);
                if (panel == null || panel.InvokeRequired)
                    return;

                _options?.Dispose();
                _text = _adapter?.ReadUiText() ?? UiText.English;
                _options = new System.Windows.Forms.FlowLayoutPanel
                {
                    FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
                    WrapContents = false,
                    // MusicBee reserves a fixed-height slot and lays out the following plugins.
                    // Do not let docking or AutoSize resize that slot during enable/disable.
                    Size = new System.Drawing.Size(panel.ClientSize.Width, 135),
                    Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left |
                             System.Windows.Forms.AnchorStyles.Right,
                    BackColor = System.Drawing.Color.Transparent,
                    ForeColor = panel.ForeColor
                };
                _autostart = Checkbox(_text.Get("Autoplay"), _settings.Autostart, !_faulted);
                _afterSleep = Checkbox(_text.Get("AfterSleep"), _settings.AfterSleep, !_faulted && _powerSubscribed);
                _afterConnectionError = Checkbox(_text.Get("AfterConnection"), _settings.AfterConnectionError, !_faulted);
                _options.Controls.Add(_autostart);
                _options.Controls.Add(_afterSleep);
                _options.Controls.Add(_afterConnectionError);
                _options.Controls.Add(new System.Windows.Forms.Label
                {
                    AutoSize = true,
                    MaximumSize = new System.Drawing.Size(System.Math.Max(220, panel.ClientSize.Width - 6), 0),
                    Text = _text.Get("RecoveryHelp")
                });
                if (_configurationWarning != null)
                    _options.Controls.Add(new System.Windows.Forms.Label
                    {
                        AutoSize = true,
                        MaximumSize = new System.Drawing.Size(460, 0),
                        Text = _text.Warning(_configurationWarning)
                    });
                _options.EnabledChanged += ConfigurationEnabledChanged;
                panel.Controls.Add(_options);
                _options.Visible = _options.Enabled;
            }, true, true);
            return false;
        }

        private static System.Windows.Forms.CheckBox Checkbox(string text, bool value, bool enabled)
        {
            return new System.Windows.Forms.CheckBox { Text = text, Checked = value, Enabled = enabled, AutoSize = true };
        }

        private static void ConfigurationEnabledChanged(object sender, System.EventArgs args)
        {
            // Match the other inline plugin settings: hide while the host disables the slot.
            var options = (System.Windows.Forms.Control)sender;
            options.Visible = options.Enabled;
        }

        public void SaveSettings()
        {
            RunOnUi(() =>
            {
                if (_store == null || _autostart == null || _autostart.IsDisposed)
                    return;
                var updated = new AutoresumeSettings
                {
                    Autostart = _autostart.Checked,
                    AfterSleep = _afterSleep.Checked,
                    AfterConnectionError = _afterConnectionError.Checked
                };
                try
                {
                    _store.Save(updated);
                    _settings = updated;
                    // Applying settings cancels any pending recovery.
                    _coordinator.Cancel();
                    _connection.Cancel();
                    _log?.Write("Settings saved.");
                }
                catch (System.Exception exception) when (SettingsStore.IsStorageError(exception))
                {
                    _log?.Write("Settings could not be saved (" + exception.GetType().Name + ").");
                    System.Windows.Forms.MessageBox.Show(_dispatcher,
                        _text.Get("SaveFailure"),
                        "Autoresume", System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                }
            });
        }

        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            if (_closed || _faulted)
                return;
            if (type == NotificationType.ShutdownStarted)
            {
                Close(PluginCloseReason.MusicBeeClosing);
                return;
            }

            ReceivePlayerNotification(type);
        }

        private void ReceivePlayerNotification(NotificationType type)
        {
            if (type == NotificationType.PluginStartup)
            {
                Guard(() =>
                {
                    _dispatcher = _adapter.MainControl();
                    if (_dispatcher == null)
                        throw new System.InvalidOperationException("The MusicBee UI thread is not available.");
                    RunOnUi(Start);
                });
                return;
            }

            if (IsPlaybackNotification(type))
                RunOnUi(() => Observe(type));
        }

        private static bool IsPlaybackNotification(NotificationType type)
        {
            return type == NotificationType.PlayStateChanged || type == NotificationType.TrackChanged ||
                   type == NotificationType.TrackChanging || type == NotificationType.PlayingTracksChanged ||
                   type == NotificationType.NowPlayingListEnded;
        }

        private void InitializeStorage()
        {
            // Resolve persistent storage after PluginStartup, not during Initialise.
            var storageDirectory = _adapter.StorageDirectory();
            _log = new DiagnosticLog(storageDirectory);
            _store = new SettingsStore(storageDirectory);
            _settings = _store.Load(out _configurationWarning);
            _log.Write("Plugin " + typeof(Plugin).Assembly.GetName().Version +
                " initialized; host-localized UI and radio error matching enabled.");
            if (_configurationWarning != null)
                _log.Write(_configurationWarning);
        }

        private void Start()
        {
            if (_started)
                return;
            _started = true;
            InitializeStorage();
            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged += PowerModeChanged;
                _powerSubscribed = true;
            }
            catch (System.Exception exception)
            {
                _configurationWarning = "Power notifications are not available (" + exception.GetType().Name + ").";
                _log?.Write(_configurationWarning);
            }

            var snapshot = _adapter.Read();
            _sleep.Update(snapshot);
            RecordState(snapshot);
            _timer = new System.Windows.Forms.Timer { Interval = 500 };
            _timer.Tick += TimerTick;
            _timer.Start();
            if (!_settings.Autostart)
                return;
            _coordinator.Schedule(_clock.ElapsedMilliseconds, snapshot, false);
            _log?.Write("Autoplay on launch scheduled.");
        }

        private void Observe(NotificationType type)
        {
            if (!_started || _sleep.IsSuspended)
                return;
            var snapshot = _adapter.Read();
            // Stop/Pause cancels pending work; it does not prove a connection failure.
            var stop = type == NotificationType.PlayStateChanged &&
                       (snapshot.Status == PlaybackStatus.Paused || snapshot.Status == PlaybackStatus.Stopped);
            _coordinator.Observe(snapshot, stop || type == NotificationType.TrackChanging ||
                                           type == NotificationType.PlayingTracksChanged ||
                                           type == NotificationType.NowPlayingListEnded);
            if (!_issuingPlayback && !_handlingErrorDialog &&
                (stop || type == NotificationType.TrackChanging || type == NotificationType.PlayingTracksChanged ||
                 type == NotificationType.NowPlayingListEnded))
                _connection.CancelPending();
            if (snapshot.Status == PlaybackStatus.Playing)
                _connection.Cancel();
            _sleep.Update(snapshot);
            RecordState(snapshot);
        }

        private void TimerTick(object sender, System.EventArgs args)
        {
            if (_closed || _faulted)
            {
                _timer?.Stop();
                return;
            }

            Guard(() =>
            {
                if (_sleep.IsSuspended)
                    return;
                var snapshot = _adapter.Read();
                _sleep.Update(snapshot);
                RecordState(snapshot);
                if (snapshot.Status == PlaybackStatus.Playing)
                    _connection.Cancel();
                // A tick can run inside a modal loop opened by our previous API call.
                if (_handlingErrorDialog)
                    return;
                if (_settings.AfterConnectionError && ResumeCoordinator.IsWebStream(snapshot.Item) &&
                    snapshot.Status != PlaybackStatus.Playing && TryHandleRadioError(snapshot))
                    return;
                if (_issuingPlayback)
                    return;
                var networkAvailable = !ResumeCoordinator.IsWebStream(snapshot.Item) ||
                                       System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
                if (_settings.AfterConnectionError && _connection.IsDue(_clock.ElapsedMilliseconds))
                {
                    TryConnectionRecovery(snapshot, networkAvailable);
                    return;
                }

                if (!_coordinator.TakeAttempt(_clock.ElapsedMilliseconds, snapshot, networkAvailable))
                    return;
                var reason = _coordinator.Reason;
                _issuingPlayback = true;
                try
                {
                    var accepted = _adapter.PlayIfUnchanged(snapshot);
                    _log?.Write(reason + (accepted ? ": the API accepted Play/Pause." : ": Play/Pause was skipped or the API reported failure."));
                }
                finally
                {
                    _issuingPlayback = false;
                }
            });
        }

        private bool TryHandleRadioError(PlaybackSnapshot snapshot)
        {
            _radioError.SetMessageTemplate(_adapter.RadioErrorTemplate());
            _radioError.SetAdditionalTemplates(_adapter.AdditionalRadioErrorTemplates());
            var button = _radioError.FindConfirmation(_dispatcher);
            if (_radioError.LastObservation != _lastDialogObservation)
            {
                _lastDialogObservation = _radioError.LastObservation;
                if (_lastDialogObservation != null)
                    _log?.Write(_lastDialogObservation);
            }

            if (button == null)
                return false;
            _handlingErrorDialog = true;
            try
            {
                _handledErrors++;
                _coordinator.Cancel();
                var queue = _adapter.ReadQueue(out var index);
                _connection.ReportFailure(_clock.ElapsedMilliseconds, snapshot, queue, index);
                _log?.Write("Recognized a radio connection error; confirming the default button. Retry delay=" + _connection.RetryDelaySeconds + " seconds.");
                button.PerformClick();
                return true;
            }
            finally
            {
                _handlingErrorDialog = false;
            }
        }

        private void TryConnectionRecovery(PlaybackSnapshot snapshot, bool networkAvailable)
        {
            var queue = _adapter.ReadQueue(out _);
            var candidate = _connection.TakeAttempt(_clock.ElapsedMilliseconds, snapshot, networkAvailable, queue);
            if (candidate == null)
                return;
            var handledBefore = _handledErrors;
            _issuingPlayback = true;
            try
            {
                var attempted = _adapter.PlayCandidate(snapshot, candidate, queue, out var accepted);
                _log?.Write("Connection recovery: " + (!attempted ? "attempt cancelled after rechecking the state/queue." :
                    accepted ? "the API accepted playback of the next entry." : "the API reported failure."));
                if (!attempted)
                    _connection.Cancel();
                else if (!accepted && handledBefore == _handledErrors)
                {
                    var current = _adapter.Read();
                    var currentQueue = _adapter.ReadQueue(out var index);
                    _connection.ReportFailure(_clock.ElapsedMilliseconds, current, currentQueue, index);
                }
            }
            finally
            {
                _issuingPlayback = false;
            }
        }

        private void PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs args)
        {
            if (_closed || _faulted)
                return;
            switch (args.Mode)
            {
                case Microsoft.Win32.PowerModes.Suspend:
                    // Capture the last known state before dispatching work to the UI thread.
                    _sleep.Suspend();
                    RunOnUi(() =>
                    {
                        _coordinator.Cancel();
                        _connection.Cancel();
                        _log?.Write("Windows is entering sleep or hibernation.");
                    });
                    return;
                case Microsoft.Win32.PowerModes.Resume:
                    var snapshot = _sleep.Resume();
                    if (snapshot == null)
                        return;
                    RunOnUi(() =>
                    {
                        _log?.Write("Windows resumed; state before sleep: " + StateName(snapshot.Status) + ".");
                        if (_settings.AfterSleep)
                            _coordinator.Schedule(_clock.ElapsedMilliseconds, snapshot, true);
                    });
                    return;
                case Microsoft.Win32.PowerModes.StatusChange:
                default:
                    return;
            }
        }

        private void RecordState(PlaybackSnapshot snapshot)
        {
            if (snapshot.Status == _lastLoggedState)
                return;
            _lastLoggedState = snapshot.Status;
            _log?.Write("Playback state: " + StateName(snapshot.Status) +
                        (ResumeCoordinator.IsWebStream(snapshot.Item) ? "; web stream." : "; other or empty source."));
        }

        private static string StateName(PlaybackStatus state)
        {
            switch (state)
            {
                case PlaybackStatus.Playing: return "playing";
                case PlaybackStatus.Paused: return "paused";
                case PlaybackStatus.Stopped: return "stopped";
                case PlaybackStatus.Loading: return "loading";
                case PlaybackStatus.Unknown:
                default: return "unknown";
            }
        }

        private void RunOnUi(System.Action action)
        {
            var control = _dispatcher;
            if (_closed || _faulted || control == null || control.IsDisposed || !control.IsHandleCreated)
                return;
            Guard(() =>
            {
                if (control.InvokeRequired)
                    control.BeginInvoke(new System.Action(() => Guard(action)));
                else
                    Guard(action);
            });
        }

        private void Guard(System.Action action, bool allowFaulted = false, bool allowClosed = false)
        {
            if ((_closed && !allowClosed) || (_faulted && !allowFaulted))
                return;
            try
            {
                action();
            }
            catch (System.Exception exception)
            {
                // Host boundary: callbacks must not propagate exceptions into MusicBee.
                _faulted = true;
                // Dispatch may have failed on another thread; the timer stops in its own UI callback.
                _configurationWarning = "Automatic playback recovery was disabled after an error (" + exception.GetType().Name + ").";
                _log?.Write(_configurationWarning);
            }
        }

        public void Close(PluginCloseReason reason)
        {
            _closed = true;
            if (_powerSubscribed)
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerModeChanged;
                _powerSubscribed = false;
            }

            var control = _dispatcher;
            try
            {
                if (control != null && !control.IsDisposed && control.IsHandleCreated && control.InvokeRequired)
                    control.BeginInvoke(new System.Action(() =>
                    {
                        if (_closed)
                            Cleanup();
                    }));
                else
                    Cleanup();
            }
            catch (System.InvalidOperationException)
            {
                // The MusicBee UI may already be disposed; the closed flag blocks delayed callbacks.
            }
        }

        private void Cleanup()
        {
            _coordinator.Cancel();
            _connection.Cancel();
            // MusicBee owns the settings container and can reuse it after enabling the plugin.
            // Dispose our controls only when Configure replaces them; the host disposes them
            // with its container when Preferences closes.
            if (_timer == null)
                return;
            _timer.Stop();
            _timer.Tick -= TimerTick;
            _timer.Dispose();
            _timer = null;
        }

        public void Uninstall()
        {
            // Keep settings for reinstallation; there are no other system resources to remove.
            Close(PluginCloseReason.UserDisabled);
        }
    }
}