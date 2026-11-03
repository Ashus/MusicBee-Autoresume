namespace MusicBeePlugin
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static void ConfigurationSurvivesDisable()
        {
            var plugin = new Plugin();
            using (var panel = new System.Windows.Forms.Panel())
            {
                plugin.Configure(panel.Handle);
                Check(panel.Controls.Count == 1, "Configuration must create its controls.");
                var original = panel.Controls[0];
                var size = panel.Size;
                panel.Enabled = false;
                Check(!original.Visible, "Disabled configuration controls must be hidden.");
                panel.Enabled = true;
                Check(original.Visible && panel.Size == size,
                    "Enabling configuration must restore controls without resizing the host slot.");
                plugin.Close(Plugin.PluginCloseReason.UserDisabled);
                Check(!original.IsDisposed && panel.Controls.Count == 1,
                    "Disabling must preserve the host-owned configuration panel.");
                plugin.Configure(panel.Handle);
                Check(original.IsDisposed && panel.Controls.Count == 1,
                    "Reconfiguration after disabling must replace controls without duplicates.");
            }
        }

        [System.STAThread]
        private static int Main()
        {
            try
            {
                AutostartRunsOnce();
                PlayingAndLoadingAreSafe();
                ManualStopAndDifferentItemCancel();
                NetworkAndDeadline();
                SleepRequiresPreviousPlayback();
                SleepSnapshotSurvivesNotifications();
                ConnectionRecoveryWrapsAndBacksOff();
                ConnectionRecoveryHonorsChanges();
                RadioErrorMatchingIsSpecific();
                ModalDialogGuards();
                UiLocalization();
                AdditionalNetworkMessages();
                AsynchronousErrorHistory();
                PersistenceAndRecovery();
                ConfigurationSurvivesDisable();
                System.Console.WriteLine("Conditions passed: " + _passed + "; failures: " + _failed + ".");
                return _failed == 0 ? 0 : 1;
            }
            catch (System.Exception exception)
            {
                System.Console.Error.WriteLine("Test failed: " + exception.Message);
                return 1;
            }
        }

        private static PlaybackSnapshot Snapshot(PlaybackStatus state, string item = "https://example.invalid/radio")
        {
            return new PlaybackSnapshot(state, item);
        }

        private static void Check(bool condition, string description)
        {
            if (condition)
                _passed++;
            else
            {
                _failed++;
                System.Console.Error.WriteLine("Condition failed: " + description);
            }
        }

        private static void AutostartRunsOnce()
        {
            var coordinator = new ResumeCoordinator();
            var stopped = Snapshot(PlaybackStatus.Stopped);
            coordinator.Schedule(0, stopped, false);
            Check(!coordinator.TakeAttempt(1999, stopped, true), "Autoplay must wait for the host.");
            Check(coordinator.TakeAttempt(2000, stopped, true), "Autoplay must start the available item.");
            Check(!coordinator.TakeAttempt(3000, stopped, true), "Even a failed attempt must not repeat.");
            coordinator.Schedule(0, Snapshot(PlaybackStatus.Stopped, ""), false);
            Check(!coordinator.TakeAttempt(3000, Snapshot(PlaybackStatus.Stopped, ""), true), "An empty queue must not trigger Play.");
            var paused = Snapshot(PlaybackStatus.Paused);
            coordinator.Schedule(0, paused, false);
            Check(coordinator.TakeAttempt(2000, paused, true), "Autoplay on launch must not depend on the previous paused state.");
        }

        private static void PlayingAndLoadingAreSafe()
        {
            var coordinator = new ResumeCoordinator();
            var stopped = Snapshot(PlaybackStatus.Stopped);
            coordinator.Schedule(0, stopped, false);
            Check(!coordinator.TakeAttempt(2000, Snapshot(PlaybackStatus.Playing), true), "A playing player must not receive Play/Pause.");
            Check(!coordinator.TakeAttempt(3000, stopped, true), "Existing playback must cancel recovery.");
            coordinator.Schedule(0, stopped, false);
            Check(!coordinator.TakeAttempt(2000, Snapshot(PlaybackStatus.Loading), true), "Loading must not trigger a toggle.");
            Check(coordinator.TakeAttempt(4000, stopped, true), "Recovery can continue after loading.");
        }

        private static void ManualStopAndDifferentItemCancel()
        {
            var coordinator = new ResumeCoordinator();
            var stopped = Snapshot(PlaybackStatus.Stopped);
            coordinator.Schedule(0, stopped, false);
            coordinator.Observe(stopped, true);
            Check(!coordinator.TakeAttempt(2000, stopped, true), "Manual Stop must cancel pending recovery.");
            coordinator.Schedule(0, stopped, false);
            Check(!coordinator.TakeAttempt(2000, Snapshot(PlaybackStatus.Stopped, "https://example.invalid/other"), true),
                "A station change must cancel the original recovery.");
            coordinator.Schedule(0, stopped, false);
            coordinator.Cancel();
            Check(!coordinator.TakeAttempt(2000, stopped, true), "Disabling an option must cancel recovery.");
        }

        private static void NetworkAndDeadline()
        {
            var coordinator = new ResumeCoordinator();
            var stopped = Snapshot(PlaybackStatus.Stopped);
            coordinator.Schedule(0, stopped, false);
            Check(!coordinator.TakeAttempt(2000, stopped, false), "A stream must wait for an available network interface.");
            Check(coordinator.TakeAttempt(2500, stopped, true), "Network availability must release the first attempt.");
            coordinator.Schedule(0, stopped, false);
            Check(!coordinator.TakeAttempt(30000, stopped, true), "Waiting must expire.");
            coordinator.Schedule(0, Snapshot(PlaybackStatus.Stopped, @"C:\Music\track.mp3"), false);
            Check(coordinator.TakeAttempt(2000, Snapshot(PlaybackStatus.Stopped, @"C:\Music\track.mp3"), false),
                "A local file does not require a network.");
        }

        private static void SleepRequiresPreviousPlayback()
        {
            var coordinator = new ResumeCoordinator();
            var stopped = Snapshot(PlaybackStatus.Stopped);
            coordinator.Schedule(0, Snapshot(PlaybackStatus.Paused), true);
            Check(!coordinator.TakeAttempt(3000, stopped, true), "A previously paused player must not resume after sleep.");
            coordinator.Schedule(0, stopped, true);
            Check(!coordinator.TakeAttempt(3000, stopped, true), "A previously stopped player must not resume after sleep.");
            coordinator.Schedule(0, Snapshot(PlaybackStatus.Playing), true);
            Check(!coordinator.TakeAttempt(2999, stopped, true), "Recovery must wait for devices to resume.");
            Check(coordinator.TakeAttempt(3000, stopped, true), "Previously active playback must resume.");
        }

        private static void SleepSnapshotSurvivesNotifications()
        {
            var tracker = new SleepTracker();
            tracker.Update(Snapshot(PlaybackStatus.Playing));
            tracker.Suspend();
            tracker.Update(Snapshot(PlaybackStatus.Stopped));
            tracker.Suspend();
            var saved = tracker.Resume();
            Check(saved != null && saved.Status == PlaybackStatus.Playing, "Duplicate suspend and a later Stop must not overwrite the saved state.");
            Check(tracker.Resume() == null, "Duplicate resume must not schedule another recovery.");
            tracker.Update(Snapshot(PlaybackStatus.Paused));
            tracker.Suspend();
            Check(tracker.Resume()?.Status == PlaybackStatus.Paused, "The next sleep cycle must use the new state.");
        }

        private static void ConnectionRecoveryWrapsAndBacksOff()
        {
            var queue = new[] { "https://example.invalid/first", "https://example.invalid/second" };
            var recovery = new ConnectionRecovery();
            var first = Snapshot(PlaybackStatus.Stopped, queue[0]);
            var second = Snapshot(PlaybackStatus.Stopped, queue[1]);
            recovery.ReportFailure(0, first, queue, 0);
            Check(recovery.TakeAttempt(2999, first, true, queue) == null, "Recovery must delay after an error.");
            Check(recovery.TakeAttempt(3000, first, true, queue) == queue[1], "The next station must be tried after an error.");
            Check(recovery.TakeAttempt(4000, first, true, queue) == null, "An attempt must not repeat without another confirmed error.");
            recovery.ReportFailure(4000, second, queue, 1);
            Check(recovery.TakeAttempt(7000, second, true, queue) == queue[0], "Recovery must wrap at queue end regardless of Repeat.");
            recovery.ReportFailure(8000, first, queue, 0);
            Check(recovery.TakeAttempt(11000, first, true, queue) == null, "Delay must increase after an unsuccessful round.");
            Check(recovery.TakeAttempt(14000, first, true, queue) == queue[1], "The next round must become available after its delay.");
            recovery.Cancel();
            recovery.ReportFailure(0, first, new[] { queue[0] }, 0);
            Check(recovery.TakeAttempt(3000, first, true, new[] { queue[0] }) == queue[0], "A single station may be retried.");
            recovery.Cancel();
            recovery.ReportFailure(0, second, queue, -1);
            Check(recovery.TakeAttempt(3000, second, true, queue) == queue[0], "Queue end must use a matching item even when its index is missing.");
        }

        private static void ConnectionRecoveryHonorsChanges()
        {
            var queue = new[] { "https://example.invalid/first", "https://example.invalid/second" };
            var current = Snapshot(PlaybackStatus.Stopped, queue[0]);
            var recovery = new ConnectionRecovery();
            recovery.ReportFailure(0, current, queue, 0);
            Check(recovery.TakeAttempt(3000, current, false, queue) == null, "The next web stream must not start without a network.");
            Check(recovery.TakeAttempt(4000, current, true, queue) == queue[1], "Network recovery must release the pending attempt.");
            recovery.ReportFailure(0, current, queue, 0);
            Check(recovery.TakeAttempt(3000, Snapshot(PlaybackStatus.Playing, queue[0]), true, queue) == null,
                "Host-restored playback must not be interrupted.");
            recovery.ReportFailure(0, current, queue, 0);
            Check(recovery.TakeAttempt(3000, Snapshot(PlaybackStatus.Stopped, queue[1]), true, queue) == null,
                "A station change must cancel the old attempt.");
            recovery.ReportFailure(0, current, queue, 0);
            Check(recovery.TakeAttempt(3000, current, true, new[] { queue[0] }) == null,
                "A queue change must cancel the original plan.");
            recovery.ReportFailure(0, current, queue, 0);
            recovery.Cancel();
            Check(recovery.TakeAttempt(3000, current, true, queue) == null, "Manual Stop or disabling the option must cancel connection recovery.");
            recovery.ReportFailure(0, current, System.Array.Empty<string>(), -1);
            Check(recovery.TakeAttempt(3000, current, true, queue) == null, "An empty queue must not cause an index error.");
        }

        private const string CzechRadioTemplate = "Rozhlasová stanice '{0}' nemůže být otevřena";

        private static void RadioErrorMatchingIsSpecific()
        {
            foreach (var template in new[] { RadioMessageTemplate.EnglishDefault, CzechRadioTemplate, "Station '{0}' indisponible.", "ラジオ「{0}」を開けません" })
            {
                var matcher = new RadioMessageTemplate(template);
                var message = string.Format(template, "Station A");
                Check(matcher.IsValid && matcher.Matches(message), "The host template must match in any language.");
                Check(matcher.Matches(string.Format(template, "Another 'station' {name}")), "Only the station name may vary.");
                Check(!matcher.Matches(message + " unrelated text"), "Additional message text must not be ignored.");
                Check(!matcher.Matches(string.Format(template, "")), "An empty station name must not match.");
                CheckModal(message, false, true, true, true, true, template);
            }

            foreach (var invalid in new[] { null, "", "{0}", "Radio {0} {0}", "Radio {1}", "Radio {0" })
                Check(!new RadioMessageTemplate(invalid).IsValid, "Unsupported host formatting must disable matching.");
            using (var form = new AmbiguousMessageForm("first", "second"))
                Check(!HiddenDialogMessage.TryRead(form, out _, out _) && form.Unchanged,
                    "Multiple string fields must be rejected without modification.");
            using (var form = new TestHostForm())
                Check(!HiddenDialogMessage.TryRead(form, out _, out _), "A missing message field must be rejected.");
            CheckModal("An unrelated error", false, true, false, true, true);
        }

        private sealed class TestHostForm : System.Windows.Forms.Form
        {
        }

        private sealed class HiddenMessageForm : System.Windows.Forms.Form
        {
            internal HiddenMessageForm(string message)
            {
                StoredMessage = message;
            }

            internal string StoredMessage { get; }
        }

        private sealed class AmbiguousMessageForm : System.Windows.Forms.Form
        {
            private readonly string _first;
            private readonly string _second;

            internal AmbiguousMessageForm(string first, string second)
            {
                _first = first;
                _second = second;
            }

            internal bool Unchanged => _first == "first" && _second == "second";
        }

        private static void UiLocalization()
        {
            var english = UiText.ForHostLanguage("English");
            var czech = UiText.ForHostLanguage("Czech");
            Check(english.Get("Autoplay") == "Autoplay on MusicBee launch", "English is the default and preserves MusicBee capitalization.");
            Check(czech.Get("Autoplay") == "Přehrát při spuštění MusicBee", "A Czech host must use the Czech UI.");
            Check(UiText.ForHostLanguage(null).Get("AfterSleep") == english.Get("AfterSleep"),
                "A missing host language must use English.");
            Check(UiText.ForHostLanguage("German").Get("Autoplay") == english.Get("Autoplay"),
                "Other host languages must fall back to English.");
            Check(czech.Warning("Settings were restored from a backup.") == "Nastavení bylo obnoveno ze zálohy.",
                "Normal configuration warnings must be localized.");
            Check(czech.Warning("Power notifications are not available (TestException).").Contains("TestException") &&
                  czech.Warning("Power notifications are not available (TestException).").Contains("napájení"),
                "Localized warnings must retain the technical error type.");
            foreach (var key in new[] { "Description", "Autoplay", "AfterSleep", "AfterConnection", "RecoveryHelp", "SaveFailure", "InitFailure", "RuntimeFailure", "PowerWarning", "LoadFailure", "BackupRestored", "AllSettingsFailed" })
                Check(english.Get(key) != key && czech.Get(key) != key, "Both embedded UI resources must contain " + key + ".");
            var filename = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,
                "temporary-host-language-" + System.Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                System.IO.File.WriteAllText(filename, "<Settings>\n<Other>unescaped & content</Other>\n<SystemLanguage>Czech</SystemLanguage>\n</Settings>");
                Check(UiText.FromSettingsFile(filename).Get("Autoplay") == czech.Get("Autoplay"),
                    "The UI must follow the host language without copying any host translation.");
                System.IO.File.WriteAllText(filename, "<SystemLanguage>English</SystemLanguage>");
                Check(UiText.FromSettingsFile(filename).Get("Autoplay") == english.Get("Autoplay"),
                    "An English host must override the Windows language.");
            }
            finally
            {
                if (System.IO.File.Exists(filename))
                    System.IO.File.Delete(filename);
            }
            Check(UiText.FromSettingsFile(filename).Get("Autoplay") == english.Get("Autoplay"),
                "Missing host settings must use English.");
        }

        private static void AdditionalNetworkMessages()
        {
            var messages = new[] { "The internet connection timed out", "The requested stream is not responding, try later.", "The radio station is not accessible at this point in time" };
            foreach (var message in messages)
            {
                var matcher = new RadioMessageTemplate(message, false);
                Check(matcher.IsValid && matcher.Matches(message), "Additional network messages must match exactly.");
                Check(!matcher.Matches(message + " extra"), "Additional text must not turn another error into a network message.");
                CheckModal(message, false, true, true, true, true, CzechRadioTemplate, messages);
            }

            CheckModal("Unable to initialise the preferred output device", false, true, false, true, true, CzechRadioTemplate, messages);
            CheckModal("Unable to play this file - it has an unsupported format or is corrupted", false, true, false, true, true, CzechRadioTemplate, messages);
            Check(!new RadioMessageTemplate("Unexpected {0}", false).IsValid, "Fixed-message definitions must reject placeholders.");
        }

        private static void AsynchronousErrorHistory()
        {
            var queue = new[] { "https://example.invalid/first", "https://example.invalid/second" };
            var recovery = new ConnectionRecovery();
            recovery.ReportFailure(0, Snapshot(PlaybackStatus.Stopped, queue[0]), queue, 0);
            recovery.CancelPending();
            Check(recovery.TakeAttempt(3000, Snapshot(PlaybackStatus.Stopped, queue[0]), true, queue) == null,
                "A Stop notification must still cancel the scheduled action.");
            recovery.ReportFailure(4000, Snapshot(PlaybackStatus.Stopped, queue[1]), queue, 1);
            recovery.CancelPending();
            recovery.ReportFailure(8000, Snapshot(PlaybackStatus.Stopped, queue[0]), queue, 0);
            Check(recovery.RetryDelaySeconds == 6, "Late host notifications must not erase failed-round backoff.");
            recovery.Cancel();
            recovery.ReportFailure(10000, Snapshot(PlaybackStatus.Stopped, queue[0]), queue, 0);
            Check(recovery.RetryDelaySeconds == 3, "Successful playback or an explicit session reset must clear failure history.");
        }

        private static void ModalDialogGuards()
        {
            CheckModal("Rozhlasová stanice 'Zero Day' nemůže být otevřena", false, true, true);
            CheckModal("Confirm file deletion?", false, true, false);
            CheckModal("Rozhlasová stanice 'Zero Day' nemůže být otevřena", true, true, false);
            CheckModal("Rozhlasová stanice 'Zero Day' nemůže být otevřena", false, false, false);
            CheckModal("Rozhlasová stanice 'Zero Day' nemůže být otevřena", false, true, true, true);
            CheckModal("An unrelated MusicBee error", false, true, false, true);
            CheckModal("Rozhlasová stanice 'Zero Day' nemůže být otevřena", true, true, false, true);
        }

        private static void CheckModal(string message, bool extraButton, bool defaultButton, bool expected, bool skinned = false, bool hidden = false, string template = CzechRadioTemplate, string[] additional = null)
        {
            using (var main = hidden ? new TestHostForm() : new System.Windows.Forms.Form())
            using (var dialog = hidden ? new HiddenMessageForm(message) : new System.Windows.Forms.Form())
            using (var timer = new System.Windows.Forms.Timer())
            {
                main.Opacity = 0;
                main.ShowInTaskbar = false;
                dialog.Opacity = 0;
                dialog.ShowInTaskbar = false;
                timer.Interval = 50;
                RunModal(main, dialog, timer, message, extraButton, defaultButton, expected, skinned, template, additional);
                if (dialog is HiddenMessageForm messageForm)
                    Check(messageForm.StoredMessage == message, "The compatibility reader must never alter the host message.");
            }
        }

        private static void RunModal(System.Windows.Forms.Form main, System.Windows.Forms.Form dialog,
            System.Windows.Forms.Timer timer, string message, bool extraButton, bool defaultButton, bool expected, bool skinned, string template, string[] additional)
        {
            var ok = skinned
                ? (System.Windows.Forms.IButtonControl)new SkinnedTestButton { Text = "Proceed", DialogResult = System.Windows.Forms.DialogResult.Yes }
                : new System.Windows.Forms.Button { Text = "OK", DialogResult = System.Windows.Forms.DialogResult.OK };
            dialog.Controls.Add((System.Windows.Forms.Control)ok);
            if (skinned)
                dialog.CancelButton = ok;
            if (!(dialog is HiddenMessageForm))
                dialog.Controls.Add(new System.Windows.Forms.Label { Text = message, AutoSize = true });
            if (extraButton)
                dialog.Controls.Add(new System.Windows.Forms.Button { Text = "Cancel" });
            if (defaultButton)
                dialog.AcceptButton = ok;
            var recognized = false;
            System.Exception error = null;
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                try
                {
                    var handler = new RadioErrorDialog(template);
                    handler.SetAdditionalTemplates(additional);
                    var confirmation = handler.FindConfirmation(main);
                    recognized = confirmation == ok;
                    confirmation?.PerformClick();
                }
                catch (System.Exception exception)
                {
                    error = exception;
                }
                finally
                {
                    dialog.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                }
            };
            main.Show();
            timer.Start();
            dialog.ShowDialog(main);
            Check(error == null && recognized == expected,
                "The modal guard must accept only the exact error with a single default OK button.");
        }

        // Reproduces the observed host contract without inheriting the standard Button class.
        private sealed class SkinnedTestButton : System.Windows.Forms.Control, System.Windows.Forms.IButtonControl
        {
            public System.Windows.Forms.DialogResult DialogResult { get; set; }

            public void NotifyDefault(bool value)
            {
            }

            public void PerformClick()
            {
                var form = FindForm();
                if (form != null)
                    form.DialogResult = DialogResult;
            }
        }


        private static void PersistenceAndRecovery()
        {
            var directory = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory,
                "temporary-settings-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new SettingsStore(directory);
                var initial = store.Load(out var warning);
                Check(initial.Autostart && !initial.AfterSleep && initial.AfterConnectionError && warning == null,
                    "Autoplay and reconnect default on; sleep recovery defaults off.");
                var saved = new AutoresumeSettings { Autostart = true, AfterSleep = true, AfterConnectionError = true };
                store.Save(saved);
                var loaded = store.Load(out warning);
                Check(loaded.Autostart && loaded.AfterSleep && loaded.AfterConnectionError && warning == null,
                    "All options must survive reloading.");
                store.Save(new AutoresumeSettings { Autostart = false, AfterSleep = false, AfterConnectionError = false });
                loaded = store.Load(out warning);
                Check(!loaded.Autostart && !loaded.AfterSleep && !loaded.AfterConnectionError,
                    "Explicitly disabled options must stay disabled after an update.");
                var filename = System.IO.Path.Combine(directory, "settings.xml");
                System.IO.File.WriteAllText(filename, "<broken");
                loaded = store.Load(out warning);
                Check(loaded.Autostart && loaded.AfterSleep && warning != null, "A backup must replace corrupt settings.");
                System.IO.File.Delete(filename + ".bak");
                loaded = store.Load(out warning);
                Check(loaded.Autostart && !loaded.AfterSleep && warning != null, "Without a backup the new defaults must apply.");
                System.IO.File.WriteAllText(filename, "<Autoresume><AutostartOnLaunch>invalid</AutostartOnLaunch><FutureOption>true</FutureOption></Autoresume>");
                loaded = store.Load(out warning);
                Check(!loaded.Autostart && !loaded.AfterSleep && warning == null, "Invalid values must be disabled; missing sleep recovery must default off.");
                System.IO.File.WriteAllText(filename, "<!DOCTYPE Autoresume [<!ENTITY test 'true'>]><Autoresume><AutostartOnLaunch>&test;</AutostartOnLaunch></Autoresume>");
                loaded = store.Load(out warning);
                Check(loaded.Autostart && warning != null, "Settings must reject XML entities and use default values.");
                System.IO.File.Delete(filename);
                System.IO.Directory.CreateDirectory(filename);
                var failed = false;
                try
                {
                    store.Save(saved);
                }
                catch (System.IO.IOException)
                {
                    failed = true;
                }
                catch (System.UnauthorizedAccessException)
                {
                    failed = true;
                }

                Check(failed && !System.IO.File.Exists(filename + ".new"), "An unwritable target must not leave an incomplete write.");
                System.IO.Directory.Delete(filename);
            }
            finally
            {
                // Delete only files created by this test; never delete recursively.
                foreach (var name in new[] { "settings.xml", "settings.xml.bak", "settings.xml.new" })
                {
                    var path = System.IO.Path.Combine(directory, name);
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }

                if (System.IO.Directory.Exists(directory))
                    System.IO.Directory.Delete(directory);
            }
        }
    }
}