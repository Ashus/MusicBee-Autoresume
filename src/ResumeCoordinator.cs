namespace MusicBeePlugin
{
    internal enum PlaybackStatus
    {
        Unknown,
        Loading,
        Playing,
        Paused,
        Stopped
    }

    internal sealed class PlaybackSnapshot
    {
        internal PlaybackSnapshot(PlaybackStatus status, string item)
        {
            Status = status;
            Item = item ?? string.Empty;
        }

        internal PlaybackStatus Status { get; }
        internal string Item { get; }
    }

    // Pure scheduling logic without MusicBee API calls or timers.
    internal sealed class ResumeCoordinator
    {
        private string _target;
        private long _due;
        private long _expires;
        private bool _pending;

        internal string Reason { get; private set; }

        internal void Schedule(long now, PlaybackSnapshot snapshot, bool afterSleep)
        {
            if (afterSleep && (snapshot.Status != PlaybackStatus.Playing || snapshot.Item.Length == 0))
                return;

            _target = snapshot.Item;
            _due = now + (afterSleep ? 3000 : 2000);
            _expires = now + (afterSleep ? 60000 : 30000);
            _pending = true;
            Reason = afterSleep ? "Recovery after sleep" : "Autoplay on launch";
        }

        internal void Cancel()
        {
            _pending = false;
        }

        internal void Observe(PlaybackSnapshot snapshot, bool explicitStop)
        {
            if (!_pending)
                return;
            if (explicitStop || snapshot.Status == PlaybackStatus.Playing ||
                (_target.Length != 0 && snapshot.Item.Length != 0 &&
                 !string.Equals(_target, snapshot.Item, System.StringComparison.Ordinal)))
                Cancel();
        }

        internal bool TakeAttempt(long now, PlaybackSnapshot snapshot, bool networkAvailable)
        {
            if (!_pending)
                return false;
            if (now >= _expires)
            {
                Cancel();
                return false;
            }

            Observe(snapshot, false);
            if (!_pending || now < _due || snapshot.Item.Length == 0)
                return false;
            if (snapshot.Status != PlaybackStatus.Stopped && snapshot.Status != PlaybackStatus.Paused)
                return false;
            if (IsWebStream(snapshot.Item) && !networkAvailable)
                return false;

            // Consume before calling the API: even failure may open a modal dialog.
            Cancel();
            return true;
        }

        internal static bool IsWebStream(string item)
        {
            return System.Uri.TryCreate(item, System.UriKind.Absolute, out var uri) &&
                   (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps);
        }
    }

    // Windows power events may arrive on a different thread from MusicBee notifications.
    internal sealed class SleepTracker
    {
        private readonly object _gate = new object();
        private PlaybackSnapshot _latest = new PlaybackSnapshot(PlaybackStatus.Unknown, string.Empty);
        private PlaybackSnapshot _beforeSleep;
        private bool _suspended;

        internal bool IsSuspended
        {
            get
            {
                lock (_gate) return _suspended;
            }
        }

        internal void Update(PlaybackSnapshot snapshot)
        {
            lock (_gate)
                if (!_suspended)
                    _latest = snapshot;
        }

        internal void Suspend()
        {
            lock (_gate)
            {
                if (_suspended)
                    return;
                _beforeSleep = _latest;
                _suspended = true;
            }
        }

        internal PlaybackSnapshot Resume()
        {
            lock (_gate)
            {
                if (!_suspended)
                    return null;
                _suspended = false;
                var snapshot = _beforeSleep;
                _beforeSleep = null;
                return snapshot;
            }
        }
    }
}