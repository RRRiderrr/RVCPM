using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RVCPM.Services
{
    internal static class HashUtil
    {
        private static readonly HashSet<string> IgnoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "dist", "build", "out", ".idea", ".vs"
        };

        public static string Sha256File(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
                return ToHex(sha.ComputeHash(stream));
        }

        public static string Sha256Path(string path)
        {
            if (File.Exists(path)) return Sha256File(path);
            if (!Directory.Exists(path)) return "";

            using (var sha = SHA256.Create())
            {
                var root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var file in EnumerateFiles(path).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var rel = Path.GetFullPath(file).Substring(root.Length).Replace('\\', '/');
                    var nameBytes = Encoding.UTF8.GetBytes(rel + "\n");
                    sha.TransformBlock(nameBytes, 0, nameBytes.Length, null, 0);
                    var bytes = File.ReadAllBytes(file);
                    sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return ToHex(sha.Hash);
            }
        }

        public static IEnumerable<string> EnumerateFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                foreach (var child in Directory.GetDirectories(dir))
                {
                    if (IgnoredDirectories.Contains(Path.GetFileName(child))) continue;
                    stack.Push(child);
                }
                foreach (var file in Directory.GetFiles(dir)) yield return file;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }
    }
}
