namespace MusicBeePlugin
{
    internal sealed class ConnectionRecovery
    {
        private string[] _queue;
        private string _expectedItem;
        private string _candidate;
        private long _due;
        private int _failures;
        private bool _pending;
        internal int RetryDelaySeconds { get; private set; }

        internal bool IsDue(long now) => _pending && now >= _due;

        internal void ReportFailure(long now, PlaybackSnapshot current, string[] queue, int index)
        {
            if (queue == null || queue.Length == 0 || !ResumeCoordinator.IsWebStream(current.Item))
            {
                Cancel();
                return;
            }

            // The host may return an invalid index at queue end; require an exact URL match.
            if (index < 0 || index >= queue.Length ||
                !string.Equals(queue[index], current.Item, System.StringComparison.Ordinal))
                index = System.Array.IndexOf(queue, current.Item);
            if (index < 0)
            {
                Cancel();
                return;
            }

            if (_queue != null && !SameQueue(_queue, queue))
                _failures = 0;
            _failures++;
            _queue = (string[])queue.Clone();
            _expectedItem = current.Item;
            // Queue end does not stop recovery, regardless of the player's Repeat/Shuffle settings.
            _candidate = null;
            for (var offset = 1; offset <= queue.Length; offset++)
            {
                var candidate = queue[(index + offset) % queue.Length];
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                _candidate = candidate;
                break;
            }

            if (_candidate == null)
            {
                Cancel();
                return;
            }

            var round = (_failures - 1) / queue.Length;
            var seconds = System.Math.Min(60, 3 * System.Math.Pow(2, System.Math.Min(round, 5)));
            RetryDelaySeconds = (int)seconds;
            _due = now + (long)(seconds * 1000);
            _pending = true;
        }

        internal string TakeAttempt(long now, PlaybackSnapshot current, bool networkAvailable, string[] queue)
        {
            if (!_pending)
                return null;
            if (current.Status == PlaybackStatus.Playing ||
                !string.Equals(_expectedItem, current.Item, System.StringComparison.Ordinal) ||
                !SameQueue(_queue, queue))
            {
                Cancel();
                return null;
            }

            if (now < _due || current.Status == PlaybackStatus.Loading || current.Status == PlaybackStatus.Unknown ||
                (ResumeCoordinator.IsWebStream(_candidate) && !networkAvailable))
                return null;

            _pending = false;
            return _candidate;
        }

        internal void Cancel()
        {
            CancelPending();
            _failures = 0;
            RetryDelaySeconds = 0;
        }

        internal void CancelPending()
        {
            // Host notifications may arrive after the API returns and before the error dialog appears.
            // Cancel actions immediately, but retain failure history until playback succeeds or the session resets.
            _pending = false;
        }

        internal static bool SameQueue(string[] expected, string[] actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length)
                return false;
            return System.Linq.Enumerable.SequenceEqual(expected, actual, System.StringComparer.Ordinal);
        }
    }

    internal sealed class RadioErrorDialog
    {
        private string _templateText = RadioMessageTemplate.EnglishDefault;
        private RadioMessageTemplate _template = new RadioMessageTemplate(RadioMessageTemplate.EnglishDefault);
        private RadioMessageTemplate[] _additional = System.Array.Empty<RadioMessageTemplate>();
        private string _matchedKey;
        internal string LastObservation { get; private set; }

        internal RadioErrorDialog(string template = RadioMessageTemplate.EnglishDefault)
        {
            SetMessageTemplate(template);
        }

        internal void SetMessageTemplate(string template)
        {
            if (string.Equals(_templateText, template, System.StringComparison.Ordinal))
                return;
            _templateText = template;
            _template = new RadioMessageTemplate(template);
        }

        internal void SetAdditionalTemplates(string[] templates)
        {
            _additional = templates == null || templates.Length != 3 ? System.Array.Empty<RadioMessageTemplate>() : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(templates, template => new RadioMessageTemplate(template, false)));
        }

        private bool Matches(string message)
        {
            if (_template.Matches(message))
            {
                _matchedKey = "Main.msg.Rscnbo";
                return true;
            }

            var keys = new[] { "Main.msg.Ticto", "Main.msg.Utpts", "Main.msg.Trsinaatpi" };
            for (var index = 0; index < _additional.Length; index++)
            {
                if (!_additional[index].Matches(message))
                    continue;
                _matchedKey = keys[index];
                return true;
            }

            return false;
        }

        internal System.Windows.Forms.IButtonControl FindConfirmation(System.Windows.Forms.Control main)
        {
            LastObservation = null;
            if (main == null || main.InvokeRequired)
                return null;
            var hostAssembly = main.GetType().Assembly;
            // Snapshot the collection before a click can remove the dialog.
            var forms = System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Cast<System.Windows.Forms.Form>(System.Windows.Forms.Application.OpenForms));
            return System.Linq.Enumerable.FirstOrDefault(
                System.Linq.Enumerable.Select(forms, form => FindButton(form, main, hostAssembly)), button => button != null);
        }

        private System.Windows.Forms.IButtonControl FindButton(System.Windows.Forms.Form form,
            System.Windows.Forms.Control main, System.Reflection.Assembly hostAssembly)
        {
            if (form == null || form.IsDisposed || !form.Visible || !form.Modal || form.InvokeRequired ||
                form == main)
                return null;
            if (form.GetType().Assembly != hostAssembly)
            {
                LastObservation = "Modal dialog left unchanged: its assembly differs from the MusicBee main window.";
                return null;
            }

            var buttons = new System.Collections.Generic.List<System.Windows.Forms.IButtonControl>();
            _matchedKey = null;
            var recognized = Inspect(form, buttons);
            var valid = _template.IsValid || System.Linq.Enumerable.Any(_additional, template => template.IsValid);
            LastObservation = "MusicBee modal dialog: valid templates=" + valid +
                              "; visible buttons=" + buttons.Count + ".";
            if (buttons.Count != 1 || !valid)
                return null;
            var button = buttons[0];
            // MusicBee's skinned OK is an IButtonControl with DialogResult.Yes, not a standard Button.
            // Confirm only after the radio message was independently identified.
            var acceptable = form.AcceptButton == button && ((System.Windows.Forms.Control)button).Enabled &&
                             (button.DialogResult == System.Windows.Forms.DialogResult.OK ||
                              button.DialogResult == System.Windows.Forms.DialogResult.Yes);
            LastObservation += " Default OK available=" + acceptable + ".";
            if (!acceptable)
                return null;
            var source = "public controls";
            if (!recognized)
            {
                source = "isolated field reader";
                if (HiddenDialogMessage.TryRead(form, out var message, out var outcome))
                    recognized = Matches(message);
                LastObservation += " Field reader=" + outcome + ".";
            }

            LastObservation += " Message source=" + source + "; recognized radio message=" + recognized + "; key=" + (_matchedKey ?? "none") + ".";
            return recognized ? button : null;
        }

        private bool Inspect(System.Windows.Forms.Control control,
            System.Collections.Generic.List<System.Windows.Forms.IButtonControl> buttons)
        {
            var found = false;
            foreach (System.Windows.Forms.Control child in control.Controls)
            {
                if (!child.Visible)
                    continue;
                if (child is System.Windows.Forms.IButtonControl button)
                    buttons.Add(button);
                // Do not log message contents: they may contain a private station name.
                found |= Matches(child.Text);
                found |= Matches(child.AccessibleName) || Matches(child.AccessibleDescription);
                found |= Inspect(child, buttons);
            }

            return found;
        }
    }

    internal sealed class RadioMessageTemplate
    {
        internal const string EnglishDefault = "Radio station '{0}' is not responding and may no longer be a valid link";
        private readonly string _prefix;
        private readonly string _suffix;
        private readonly bool _stationName;
        internal bool IsValid { get; }

        internal RadioMessageTemplate(string template, bool stationName = true)
        {
            _stationName = stationName;
            if (string.IsNullOrEmpty(template) || template.Length > 8192)
                return;
            if (!stationName)
            {
                _prefix = template;
                IsValid = !string.IsNullOrWhiteSpace(template) && template.IndexOf('{') < 0 && template.IndexOf('}') < 0;
                return;
            }

            var placeholder = template.IndexOf("{0}", System.StringComparison.Ordinal);
            if (placeholder < 0 || template.IndexOf("{0}", placeholder + 3, System.StringComparison.Ordinal) >= 0)
                return;
            _prefix = template.Substring(0, placeholder);
            _suffix = template.Substring(placeholder + 3);
            var fixedText = _prefix + _suffix;
            // Reject incomplete/unknown formatting rather than treating it as a wildcard.
            IsValid = !string.IsNullOrWhiteSpace(fixedText) && fixedText.IndexOf('{') < 0 && fixedText.IndexOf('}') < 0;
        }

        internal bool Matches(string message)
        {
            return IsValid && message != null && message.Length <= 8192 &&
                   (!_stationName
                       ? string.Equals(message, _prefix, System.StringComparison.Ordinal)
                       : message.Length > _prefix.Length + _suffix.Length &&
                         message.StartsWith(_prefix, System.StringComparison.Ordinal) &&
                         message.EndsWith(_suffix, System.StringComparison.Ordinal));
        }
    }

    // Narrow compatibility fallback approved for the skinned host dialog.
    // Read only one unambiguous declared string field; never invoke or modify private host members.
    internal static class HiddenDialogMessage
    {
        internal static bool TryRead(System.Windows.Forms.Form dialog, out string message, out string outcome)
        {
            message = null;
            outcome = "unavailable";
            try
            {
                var fields = dialog.GetType().GetFields(System.Reflection.BindingFlags.Instance |
                                                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                                        System.Reflection.BindingFlags.DeclaredOnly);
                System.Reflection.FieldInfo candidate = null;
                foreach (var field in fields)
                {
                    if (field.FieldType != typeof(string))
                        continue;
                    if (candidate != null)
                    {
                        outcome = "ambiguous string fields";
                        return false;
                    }

                    candidate = field;
                }

                if (candidate == null)
                {
                    outcome = "no declared string field";
                    return false;
                }

                var value = candidate.GetValue(dialog) as string;
                if (string.IsNullOrEmpty(value) || value.Length > 8192)
                {
                    outcome = "empty or oversized message";
                    return false;
                }

                message = value;
                outcome = "one readable string field";
                return true;
            }
            catch (System.Exception exception)
            {
                outcome = "unavailable (" + exception.GetType().Name + ")";
                return false;
            }
        }
    }
}