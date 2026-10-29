using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.Config
{
    /// <summary>配置表文本来源。PC 走文件，以后上 Android 换成 UnityWebRequest 实现即可。</summary>
    public interface IConfigSource
    {
        bool TryRead(string fileName, out string text);
    }

    public sealed class FileConfigSource : IConfigSource
    {
        readonly string _root;

        public FileConfigSource(string root = null)
        {
            _root = root ?? Path.Combine(UnityEngine.Application.streamingAssetsPath, "Config");
        }

        public bool TryRead(string fileName, out string text)
        {
            string path = Path.Combine(_root, fileName);
            if (!File.Exists(path))
            {
                text = null;
                return false;
            }

            text = File.ReadAllText(path, Encoding.UTF8);
            return true;
        }
    }

    /// <summary>内存来源，测试用。</summary>
    public sealed class MemoryConfigSource : IConfigSource
    {
        readonly Dictionary<string, string> _files = new Dictionary<string, string>();

        public MemoryConfigSource Add(string fileName, string text)
        {
            _files[fileName] = text;
            return this;
        }

        public bool TryRead(string fileName, out string text)
        {
            return _files.TryGetValue(fileName, out text);
        }
    }

    public sealed class ConfigException : Exception
    {
        public ConfigException(string message) : base(message)
        {
        }
    }
}
