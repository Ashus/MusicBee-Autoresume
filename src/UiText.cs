namespace MusicBeePlugin
{
    internal sealed class UiText
    {
        private static readonly System.Resources.ResourceManager EnglishResources =
            new System.Resources.ResourceManager("MusicBeePlugin.Resources.UiEnglish", typeof(UiText).Assembly);

        private static readonly System.Resources.ResourceManager CzechResources =
            new System.Resources.ResourceManager("MusicBeePlugin.Resources.UiCzech", typeof(UiText).Assembly);

        private readonly bool _czech;

        private UiText(bool czech)
        {
            _czech = czech;
        }

        internal static UiText English { get; } = new UiText(false);

        internal static UiText ForHostLanguage(string language)
        {
            return new UiText(string.Equals(language?.Trim(), "Czech", System.StringComparison.OrdinalIgnoreCase));
        }

        internal static UiText FromSettingsFile(string path)
        {
            try
            {
                using (var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                           System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
                {
                    if (stream.Length > 4 * 1024 * 1024)
                        return English;
                    using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8, true))
                    {
                        // Read only the verified language tag; the host file can contain non-XML settings data.
                        const string opening = "<SystemLanguage>";
                        const string closing = "</SystemLanguage>";
                        for (var lines = 0; lines < 20000; lines++)
                        {
                            var line = reader.ReadLine();
                            if (line == null)
                                break;
                            var start = line.IndexOf(opening, System.StringComparison.Ordinal);
                            if (start < 0)
                                continue;
                            start += opening.Length;
                            var end = line.IndexOf(closing, start, System.StringComparison.Ordinal);
                            if (end >= start)
                                return ForHostLanguage(line.Substring(start, end - start));
                        }
                    }
                }
            }
            catch (System.Exception exception) when (exception is System.IO.IOException ||
                exception is System.UnauthorizedAccessException || exception is System.Security.SecurityException ||
                exception is System.ArgumentException)
            {
                // Unavailable or changed host settings must leave the UI in its default language.
            }
            return English;
        }

        internal string Get(string key)
        {
            return (_czech ? ReadCzech(key) : null) ??
                   EnglishResources.GetString(key, System.Globalization.CultureInfo.InvariantCulture) ?? key;
        }

        private static string ReadCzech(string key)
        {
            return CzechResources.GetString(key, System.Globalization.CultureInfo.InvariantCulture);
        }

        internal string Warning(string english)
        {
            if (english == null || !_czech)
                return english;
            foreach (var key in new[] { "InitFailure", "RuntimeFailure", "PowerWarning", "LoadFailure", "BackupRestored", "AllSettingsFailed" })
            {
                var original = EnglishResources.GetString(key, System.Globalization.CultureInfo.InvariantCulture);
                if (string.Equals(original, english, System.StringComparison.Ordinal))
                    return Get(key);
                var marker = original.IndexOf("{0}", System.StringComparison.Ordinal);
                if (marker < 0)
                    continue;
                var prefix = original.Substring(0, marker);
                var suffix = original.Substring(marker + 3);
                if (english.Length >= prefix.Length + suffix.Length &&
                    english.StartsWith(prefix, System.StringComparison.Ordinal) && english.EndsWith(suffix, System.StringComparison.Ordinal))
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture, Get(key),
                        english.Substring(prefix.Length, english.Length - prefix.Length - suffix.Length));
            }

            return english;
        }
    }
}