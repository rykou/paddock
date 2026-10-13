using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Xml.Serialization;

namespace Paddock
{
    public class FenceData
    {
        public Guid Id = Guid.NewGuid();
        public string Title = "New fence";
        public int X = 120, Y = 120, W = 360, H = 240;
        public int Argb = Color.FromArgb(32, 42, 58).ToArgb();
        public int Opacity = 85;
        public bool Collapsed;
        public bool Locked;
        public bool Rounded = true;
        public int IconSize = 48;
        public int TitleAlign; // 0 centre, 1 left
        public string Folder; // the real folder whose contents the fence shows
        public List<string> Items = new List<string>(); // pre-0.4 path references, converted to shortcuts on load
    }

    public class Store
    {
        public List<FenceData> Fences = new List<FenceData>();
        public bool QuickHide = true; // double-click empty desktop to hide or show every fence

        // PADDOCK_HOME points a second, independent instance at its own settings and fence folders (used for testing).
        public static readonly string Home = Environment.GetEnvironmentVariable("PADDOCK_HOME");

        static string FilePath => Home != null
            ? Path.Combine(Home, "fences.xml")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paddock", "fences.xml");

        public static string FenceRoot => Home != null
            ? Path.Combine(Home, "Fences")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Paddock");

        public static Store Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var s = File.OpenRead(FilePath))
                        return (Store)new XmlSerializer(typeof(Store)).Deserialize(s);
            }
            catch
            {
                // Keep the unreadable file aside instead of overwriting it on the next save.
                try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            }
            return null;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string tmp = FilePath + ".tmp";
            using (var s = File.Create(tmp))
                new XmlSerializer(typeof(Store)).Serialize(s, this);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }
}
