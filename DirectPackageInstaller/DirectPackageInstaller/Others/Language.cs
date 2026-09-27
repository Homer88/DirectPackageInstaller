using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DirectPackageInstaller
{
    public class LanguageInfo
    {
        public string Name = null!;
        public string NativeName = null!;
        public string File = null!;

        public override string ToString() => NativeName;
    }

    /// <summary>
    /// Plain INI based localization, the language packs are loose files inside the
    /// "language" folder next to the Settings.ini, this way the user can add, remove
    /// or edit any translation without recompiling the app.
    ///
    /// The file layout is:
    /// [Meta]
    /// Name=English
    /// NativeName=English
    ///
    /// [Language]
    /// Menu.Options=Options
    /// </summary>
    public static class Language
    {
        public const string Default = "English";
        public const string DirectoryName = "language";

        const string Group = "Language";
        const string Meta = "Meta";

        static readonly Dictionary<string, string> Fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string? Current { get; private set; }

        /// <summary>
        /// Raised after <see cref="Load"/> applied a new language, used to re-apply
        /// the strings that are already on the screen.
        /// </summary>
        public static event EventHandler? Loaded;

        public static string Directory
        {
            get
            {
                try
                {
                    return Path.Combine(App.WorkingDirectory, DirectoryName);
                }
                catch
                {
                    return Path.Combine(AppContext.BaseDirectory, DirectoryName);
                }
            }
        }

        public static string? LanguagePath => Current == null ? null : GetLanguageFile(Current);

        public static string GetLanguageFile(string? Name)
        {
            if (string.IsNullOrWhiteSpace(Name))
                Name = Default;

            return Path.Combine(Directory, $"{Name!.Trim()}.ini");
        }

        /// <summary>
        /// Every *.ini file inside the language folder, the folder is scanned on each
        /// call so new language packs are picked up without a restart.
        /// </summary>
        public static List<LanguageInfo> Available
        {
            get
            {
                List<LanguageInfo> Result = new List<LanguageInfo>();

                try
                {
                    if (!System.IO.Directory.Exists(Directory))
                        return Result;

                    foreach (string File in System.IO.Directory.GetFiles(Directory, "*.ini").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        var Ini = new Ini(File, Group);

                        Result.Add(new LanguageInfo()
                        {
                            Name = Ini.GetValue("Name", Meta) ?? Path.GetFileNameWithoutExtension(File),
                            NativeName = Ini.GetValue("NativeName", Meta) ?? Ini.GetValue("Name", Meta) ?? Path.GetFileNameWithoutExtension(File),
                            File = File
                        });
                    }
                }
                catch { }

                return Result;
            }
        }

        static Language()
        {
            Load(null, true);
        }

        /// <summary>
        /// Switches the app to the given language, a null/empty/invalid name falls
        /// back to the default english pack. Returns the name actually loaded.
        /// </summary>
        public static string? Load(string? Name, bool Silent = false)
        {
            if (string.IsNullOrWhiteSpace(Name))
                Name = Default;

            Name = Name!.Trim();

            if (!File.Exists(GetLanguageFile(Name)))
                Name = Default;

            var LanguageFile = GetLanguageFile(Name);

            try
            {
                var Ini = new Ini(LanguageFile, Group);
                var Entries = Ini.GetValues(Group) ?? new Dictionary<string, string>();

                lock (Strings)
                {
                    Strings.Clear();
                    foreach (var Entry in Entries)
                        Strings[Entry.Key] = Unescape(Entry.Value);
                }

                if (!Name.Equals(Default, StringComparison.OrdinalIgnoreCase))
                    LoadFallback();
                else
                    lock (Fallback)
                    {
                        Fallback.Clear();
                        foreach (var Entry in Strings)
                            Fallback[Entry.Key] = Entry.Value;
                    }

                Current = Name;

                if (!Silent)
                    Loaded?.Invoke(null, EventArgs.Empty);

                return Name;
            }
            catch
            {
                return Current;
            }
        }

        static void LoadFallback()
        {
            try
            {
                var Ini = new Ini(GetLanguageFile(Default), Group);
                var Entries = Ini.GetValues(Group) ?? new Dictionary<string, string>();

                lock (Fallback)
                {
                    Fallback.Clear();
                    foreach (var Entry in Entries)
                        Fallback[Entry.Key] = Unescape(Entry.Value);
                }
            }
            catch { }
        }

        /// <summary>
        /// Returns the string of the given key, missing keys fall back to english and,
        /// when the key is unknown everywhere, the key itself is returned so the missing
        /// translation is easy to spot.
        /// </summary>
        public static string L(string Key)
        {
            if (string.IsNullOrWhiteSpace(Key))
                return string.Empty;

            lock (Strings)
            {
                if (Strings.TryGetValue(Key, out string? Value) && Value.Length > 0)
                    return Value;
            }

            lock (Fallback)
            {
                if (Fallback.TryGetValue(Key, out string? Value) && Value.Length > 0)
                    return Value;
            }

            return Key;
        }

        /// <summary>
        /// Same as <see cref="L"/> but applies the {0}, {1}... placeholders of the
        /// translated string, so the argument order can be adapted per language.
        /// </summary>
        public static string F(string Key, params object?[] Args)
        {
            string Text = L(Key);

            if (Args.Length == 0)
                return Text;

            try
            {
                return string.Format(System.Globalization.CultureInfo.CurrentCulture, Text, Args);
            }
            catch
            {
                return Text;
            }
        }

        static string Unescape(string? Value)
        {
            if (string.IsNullOrEmpty(Value))
                return string.Empty;

            return Value!.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
        }
    }
}
