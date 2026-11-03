namespace MusicBeePlugin
{
    internal sealed class MusicBeeAdapter
    {
        private readonly Plugin.MusicBeeApiInterface _api;

        internal MusicBeeAdapter(System.IntPtr pointer)
        {
            if (System.IntPtr.Size != 4 || pointer == System.IntPtr.Zero)
                throw new System.NotSupportedException("This plugin requires 32-bit MusicBee.");
            // Validate the header before marshaling the delegate table.
            if (System.Runtime.InteropServices.Marshal.ReadInt16(pointer) < Plugin.MinInterfaceVersion ||
                System.Runtime.InteropServices.Marshal.ReadInt16(pointer, 2) < Plugin.MinApiRevision)
                throw new System.NotSupportedException("This MusicBee API version is not supported.");
            var api = new Plugin.MusicBeeApiInterface();
            api.Initialise(pointer);
            _api = api;
            if (_api.Player_GetPlayState == null || _api.Player_PlayPause == null ||
                _api.NowPlaying_GetFileUrl == null || _api.Setting_GetPersistentStoragePath == null ||
                _api.MB_GetWindowHandle == null)
                throw new System.NotSupportedException("Required MusicBee API functions are not available.");
        }

        internal string StorageDirectory()
        {
            var path = _api.Setting_GetPersistentStoragePath();
            return string.IsNullOrWhiteSpace(path)
                ? throw new System.InvalidOperationException("MusicBee did not provide a settings folder.")
                : System.IO.Path.Combine(path, "MusicBee-Autoresume");
        }

        internal System.Windows.Forms.Control MainControl()
        {
            return System.Windows.Forms.Control.FromHandle(_api.MB_GetWindowHandle());
        }

        internal string RadioErrorTemplate()
        {
            return LocalizeMessage("Main.msg.Rscnbo", RadioMessageTemplate.EnglishDefault);
        }

        internal string[] AdditionalRadioErrorTemplates()
        {
            return new[]
            {
                LocalizeMessage("Main.msg.Ticto", "The internet connection timed out"),
                LocalizeMessage("Main.msg.Utpts", "The requested stream is not responding, try later."),
                LocalizeMessage("Main.msg.Trsinaatpi", "The radio station is not accessible at this point in time")
            };
        }

        private string LocalizeMessage(string key, string defaultText)
        {
            try
            {
                return _api.MB_GetLocalisation == null ? defaultText : _api.MB_GetLocalisation(key, defaultText);
            }
            catch (System.Exception)
            {
                // A failed localization lookup must not authorize confirmation or disable other features.
                return null;
            }
        }

        internal UiText ReadUiText()
        {
            try
            {
                return UiText.FromSettingsFile(System.IO.Path.Combine(_api.Setting_GetPersistentStoragePath(), "MusicBee3Settings.ini"));
            }
            catch (System.Exception)
            {
                return UiText.English;
            }
        }

        internal PlaybackSnapshot Read()
        {
            var state = _api.Player_GetPlayState();
            var item = _api.NowPlaying_GetFileUrl();
            if (string.IsNullOrEmpty(item) && _api.NowPlayingList_GetCurrentIndex != null &&
                _api.NowPlayingList_GetListFileUrl != null)
            {
                var index = _api.NowPlayingList_GetCurrentIndex();
                if (index >= 0)
                    item = _api.NowPlayingList_GetListFileUrl(index);
            }

            PlaybackStatus status;
            switch (state)
            {
                case Plugin.PlayState.Playing: status = PlaybackStatus.Playing; break;
                case Plugin.PlayState.Paused: status = PlaybackStatus.Paused; break;
                case Plugin.PlayState.Stopped: status = PlaybackStatus.Stopped; break;
                case Plugin.PlayState.Loading: status = PlaybackStatus.Loading; break;
                case Plugin.PlayState.Undefined:
                default: status = PlaybackStatus.Unknown; break;
            }

            return new PlaybackSnapshot(status, item);
        }

        internal bool PlayIfUnchanged(PlaybackSnapshot expected)
        {
            var current = Read();
            if ((current.Status != PlaybackStatus.Paused && current.Status != PlaybackStatus.Stopped) ||
                !string.Equals(current.Item, expected.Item, System.StringComparison.Ordinal) ||
                current.Item.Length == 0)
                return false;
            return _api.Player_PlayPause();
        }

        internal string[] ReadQueue(out int index)
        {
            index = -1;
            if (_api.NowPlayingList_QueryFilesEx == null || _api.NowPlayingList_GetCurrentIndex == null ||
                !_api.NowPlayingList_QueryFilesEx(null, out var queue) || queue == null)
                return null;
            index = _api.NowPlayingList_GetCurrentIndex();
            return queue;
        }

        internal bool PlayCandidate(PlaybackSnapshot expected, string candidate, string[] expectedQueue, out bool accepted)
        {
            accepted = false;
            if (_api.NowPlayingList_PlayNow == null || string.IsNullOrWhiteSpace(candidate))
                return false;
            var current = Read();
            if ((current.Status != PlaybackStatus.Stopped && current.Status != PlaybackStatus.Paused) ||
                !string.Equals(current.Item, expected.Item, System.StringComparison.Ordinal) ||
                !ConnectionRecovery.SameQueue(expectedQueue, ReadQueue(out _)) ||
                System.Array.IndexOf(expectedQueue, candidate) < 0)
                return false;
            // The public API selects an existing item by URL; never clear or replace the queue.
            accepted = _api.NowPlayingList_PlayNow(candidate);
            return true;
        }
    }
}