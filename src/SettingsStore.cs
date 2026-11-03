namespace MusicBeePlugin
{
    internal sealed class AutoresumeSettings
    {
        internal bool Autostart { get; set; } = true;
        internal bool AfterSleep { get; set; }
        internal bool AfterConnectionError { get; set; } = true;
    }

    internal sealed class SettingsStore
    {
        private readonly string _directory;
        private readonly string _filename;

        internal SettingsStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new System.ArgumentException("The settings folder is missing.", nameof(directory));
            _directory = directory;
            _filename = System.IO.Path.Combine(directory, "settings.xml");
        }

        internal AutoresumeSettings Load(out string warning)
        {
            warning = null;
            try
            {
                if (System.IO.File.Exists(_filename))
                    return Read(_filename);
                if (!System.IO.File.Exists(_filename + ".bak"))
                    return new AutoresumeSettings();
            }
            catch (System.Exception exception) when (IsStorageError(exception))
            {
                warning = "Settings could not be loaded. A backup or the default settings will be used.";
            }

            try
            {
                if (System.IO.File.Exists(_filename + ".bak"))
                {
                    var result = Read(_filename + ".bak");
                    warning = "Settings were restored from a backup.";
                    return result;
                }
            }
            catch (System.Exception exception) when (IsStorageError(exception))
            {
                warning = "Neither settings nor their backup could be loaded. The default settings will be used.";
            }

            return new AutoresumeSettings();
        }

        internal void Save(AutoresumeSettings settings)
        {
            System.IO.Directory.CreateDirectory(_directory);
            var temporary = _filename + ".new";
            try
            {
                using (var stream = new System.IO.FileStream(temporary, System.IO.FileMode.Create,
                           System.IO.FileAccess.Write, System.IO.FileShare.None))
                {
                    using (var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings
                               { Encoding = new System.Text.UTF8Encoding(false), Indent = true, CloseOutput = false }))
                    {
                        writer.WriteStartElement("Autoresume");
                        writer.WriteAttributeString("version", "1");
                        writer.WriteElementString("AutostartOnLaunch", System.Xml.XmlConvert.ToString(settings.Autostart));
                        writer.WriteElementString("ResumeAfterSleep", System.Xml.XmlConvert.ToString(settings.AfterSleep));
                        writer.WriteElementString("ResumeAfterConnectionError", System.Xml.XmlConvert.ToString(settings.AfterConnectionError));
                        writer.WriteEndElement();
                    }

                    stream.Flush(true);
                }

                // Same-volume replacement preserves the original file if writing is interrupted.
                if (System.IO.File.Exists(_filename))
                    System.IO.File.Replace(temporary, _filename, _filename + ".bak");
                else
                    System.IO.File.Move(temporary, _filename);
            }
            finally
            {
                if (System.IO.File.Exists(temporary))
                    System.IO.File.Delete(temporary);
            }
        }

        private static AutoresumeSettings Read(string path)
        {
            using (var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings
                       { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 }))
            {
                var document = new System.Xml.XmlDocument { XmlResolver = null };
                document.Load(reader);
                var root = document.DocumentElement;
                if (root == null || root.Name != "Autoresume")
                    throw new System.Xml.XmlException("Invalid settings format.");
                return new AutoresumeSettings
                {
                    Autostart = ReadBoolean(root, "AutostartOnLaunch"),
                    AfterSleep = ReadBoolean(root, "ResumeAfterSleep", false),
                    AfterConnectionError = ReadBoolean(root, "ResumeAfterConnectionError")
                };
            }
        }

        private static bool ReadBoolean(System.Xml.XmlElement root, string name, bool defaultValue = true)
        {
            var element = root[name];
            return element == null ? defaultValue : bool.TryParse(element.InnerText, out var value) && value;
        }

        internal static bool IsStorageError(System.Exception exception)
        {
            return exception is System.IO.IOException || exception is System.UnauthorizedAccessException ||
                   exception is System.Xml.XmlException || exception is System.Security.SecurityException;
        }
    }

    internal sealed class DiagnosticLog
    {
        private readonly object _gate = new object();
        private readonly string _directory;
        private readonly string _filename;

        internal DiagnosticLog(string directory)
        {
            _directory = directory;
            _filename = System.IO.Path.Combine(directory, "autoresume.log");
        }

        // Callers pass only fixed messages and states, never URLs or host exception messages.
        internal void Write(string message)
        {
            lock (_gate)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(_directory);
                    if (System.IO.File.Exists(_filename) && new System.IO.FileInfo(_filename).Length > 262144)
                    {
                        var previous = _filename + ".1";
                        if (System.IO.File.Exists(previous))
                            System.IO.File.Delete(previous);
                        System.IO.File.Move(_filename, previous);
                    }

                    System.IO.File.AppendAllText(_filename,
                        System.DateTimeOffset.Now.ToString("O") + " " + message + System.Environment.NewLine,
                        new System.Text.UTF8Encoding(false));
                }
                catch (System.Exception exception) when (SettingsStore.IsStorageError(exception))
                {
                    // Logging must not crash MusicBee or open a dialog during playback.
                }
            }
        }
    }
}