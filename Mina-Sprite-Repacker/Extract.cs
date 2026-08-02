using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Mina_Sprite_Repacker
{
    public static class Extract
    {
        static readonly byte[] PNG_SIG = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        static uint U32(byte[] f, int o) => (uint)(f[o] | (f[o + 1] << 8) | (f[o + 2] << 16) | (f[o + 3] << 24));
        static uint[] _crcT;
        class TextureEntry
        {
            public int Width;
            public int Height;
            public int Size;
            public int Align;
            public byte[] Data;
        }
        public static void ExtractAllSprites()
        {
            var anbFiles = Directory.EnumerateFiles(Constants.currentDirectory, "*.anb.yc", SearchOption.AllDirectories);

            string outDirectory = Path.Combine(Constants.currentDirectory, "_my_sprites");
            if (Directory.Exists(outDirectory)) {
                Directory.Delete(outDirectory, true);
            }
            Directory.CreateDirectory(outDirectory);

            foreach (string anbFile in anbFiles) {
                string fileContent = "";

                try {
                    fileContent = File.ReadAllText(anbFile);
                }
                catch (UnauthorizedAccessException) {
                    Console.WriteLine($"Error: Permission denied while trying to read '{anbFile}'");
                    continue;
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error: {ex.Message}");
                    continue;
                }

                if (!TryParseAnimDef(fileContent, out string paletteName, out List<string> frameNames, out List<TextureEntry> textures)) {
                    continue;
                }

                for (int i = 0; i < textures.Count; i++) {
                    string frameName = (i < frameNames.Count) ? frameNames[i] : $"{i}";
                    ExtractSprite(anbFile, paletteName, frameName, textures[i]);
                }
            }
        }
        static void ExtractSprite(string anbFile, string paletteName, string frameName, TextureEntry texture)
        {
            string palettePath = Path.Combine(Constants.currentDirectory, paletteName);
            var palette = LoadPalette(palettePath);
            if (palette == null) {
                Console.WriteLine($"Could not load palette '{palettePath}'");
                return;
            }

            byte[] indexed;
            try { indexed = WflzDecompress(texture.Data, 0); }
            catch (Exception ex) {
                Console.WriteLine($"WFLZ decompress failed for '{frameName}': {ex.Message}");
                return;
            }

            string relativePath = Path.GetRelativePath(Constants.currentDirectory, anbFile);
            string relativeDir = Path.GetDirectoryName(relativePath) ?? "";
            string baseName = Path.GetFileName(relativePath);
            baseName = baseName.Replace(".anb.yc", "");

            string outDir = Path.Combine(Constants.currentDirectory, Constants.spritesFolderName, relativeDir, baseName);
            Directory.CreateDirectory(outDir);

            string outPath = Path.Combine(outDir, frameName + ".png");
            WriteIndexedPng(outPath, texture.Width, texture.Height, indexed,
                palette.Value.rgb, palette.Value.alpha);
        }
        static (byte[] rgb, byte[] alpha)? LoadPalette(string palettePath)
        {
            if (!File.Exists(palettePath)) return null;

            string content;
            try { content = File.ReadAllText(palettePath); }
            catch { return null; }

            // Check if file is ycPaletteFormat
            string[] firstLines = content.Split('\n', 3);
            if (firstLines.Length < 2 || !firstLines[1].Trim().StartsWith("ycPaletteFormat"))
                return null;

            // Get color data
            int colorsPos = content.IndexOf("m_colors:");
            if (colorsPos < 0) return null;

            int arrOpen = content.IndexOf('[', colorsPos);
            int arrClose = FindClosingBrace(content, arrOpen);
            if (arrClose < 0) return null;

            string colorsSection = content.Substring(arrOpen, arrClose - arrOpen + 1);

            var rgb = new byte[256 * 3];
            var alpha = new byte[256];
            int index = 0;

            foreach (Match cm in Regex.Matches(colorsSection, @"ycColor\s*\{([^}]*)\}")) {
                if (index >= 256) break;

                string block = cm.Groups[1].Value;

                var rm = Regex.Match(block, @"\br:\s*(\d+)");
                var gm = Regex.Match(block, @"\bg:\s*(\d+)");
                var bm = Regex.Match(block, @"\bb:\s*(\d+)");
                var am = Regex.Match(block, @"\ba:\s*(\d+)");

                rgb[index * 3] = rm.Success ? byte.Parse(rm.Groups[1].Value) : (byte)0;
                rgb[index * 3 + 1] = gm.Success ? byte.Parse(gm.Groups[1].Value) : (byte)0;
                rgb[index * 3 + 2] = bm.Success ? byte.Parse(bm.Groups[1].Value) : (byte)0;
                alpha[index] = am.Success ? byte.Parse(am.Groups[1].Value) : (byte)0;

                index++;
            }
            return (rgb, alpha);
        }
        static bool TryParseAnimDef(string fileContent, out string palettePath, out List<string> frameNames, out List<TextureEntry> textures)
        {
            palettePath = null;
            frameNames = null;
            textures = null;

            // Check if file is ycCutter2AnimDef
            string[] firstLines = fileContent.Split('\n', 3);
            if (firstLines.Length < 2
                || !firstLines[1].Trim().StartsWith("ycCutter2AnimDef"))
                return false;

            // Get palette path
            var palMatch = Regex.Match(fileContent, @"m_paletteName:\s*""([^""]+)""");
            palettePath = palMatch.Success ? palMatch.Groups[1].Value : "";

            // Get sequence data
            frameNames = new List<string>();
            int frameCounter = 0;

            int seqPos = fileContent.IndexOf("m_sequences:");
            if (seqPos >= 0) {
                int arrOpen = fileContent.IndexOf('[', seqPos);
                int arrClose = FindClosingBrace(fileContent, arrOpen);
                if (arrClose < 0) return false;
                string seqSection = fileContent.Substring(arrOpen, arrClose - arrOpen + 1);

                foreach (Match sm in Regex.Matches(seqSection, @"ycCutter2Sequence(?!Frame)")) {
                    int bo = seqSection.IndexOf('{', sm.Index);
                    int bc = FindClosingBrace(seqSection, bo);
                    if (bc < 0) continue;
                    string seqBlock = seqSection.Substring(bo, bc - bo + 1);

                    var nm = Regex.Match(seqBlock, @"m_name:\s*""([^""]+)""");
                    if (!nm.Success) {
                        Console.WriteLine("Error: Could not get m_name from sequence data");
                        return false;
                    }
                    string name = nm.Groups[1].Value;

                    // Name the exported sprites
                    foreach (Match fm in Regex.Matches(seqBlock, @"ycCutter2SequenceFrame")) {
                        string frameName = $"{frameCounter}_{name}";
                        frameNames.Add($"{frameCounter}");
                        frameCounter++;
                    }
                }
            }

            // Get texture data
            textures = new List<TextureEntry>();

            int texPos = fileContent.IndexOf("m_textures:");
            if (texPos >= 0) {
                int arrOpen = fileContent.IndexOf('[', texPos);
                int arrClose = FindClosingBrace(fileContent, arrOpen);
                if (arrClose < 0) return false;
                string texSection = fileContent.Substring(arrOpen, arrClose - arrOpen + 1);

                foreach (Match tm in Regex.Matches(texSection, @"ycCutter2Texture\b")) {
                    int bo = texSection.IndexOf('{', tm.Index);
                    int bc = FindClosingBrace(texSection, bo);
                    if (bc < 0) continue;
                    string block = texSection.Substring(bo, bc - bo + 1);

                    var entry = new TextureEntry();

                    var w = Regex.Match(block, @"m_width:\s*(\d+)");
                    if (w.Success) entry.Width = int.Parse(w.Groups[1].Value);

                    var h = Regex.Match(block, @"m_height:\s*(\d+)");
                    if (h.Success) entry.Height = int.Parse(h.Groups[1].Value);

                    var s = Regex.Match(block, @"\bsize:\s*(\d+)");
                    if (s.Success) entry.Size = int.Parse(s.Groups[1].Value);

                    var a = Regex.Match(block, @"\balign:\s*(\d+)");
                    if (a.Success) entry.Align = int.Parse(a.Groups[1].Value);

                    var d = Regex.Match(block, @"\bdata:\s*""([^""]+)""");
                    if (d.Success) entry.Data = Convert.FromBase64String(d.Groups[1].Value);

                    textures.Add(entry);
                }
            }
            return true;
        }
        static int FindClosingBrace(string text, int openPos)
        {
            char open = text[openPos];
            char close = open == '{' ? '}' : ']';
            int depth = 1;
            bool inStr = false;

            for (int i = openPos + 1; i < text.Length; i++) {
                char c = text[i];
                if (c == '"' && text[i - 1] != '\\')
                    inStr = !inStr;
                if (inStr) continue;
                if (c == open) depth++;
                if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }
        static byte[] WflzDecompress(byte[] f, int hdr)
        {
            int compSize = (int)U32(f, hdr + 4);
            int decompSize = (int)U32(f, hdr + 8);
            if (decompSize <= 0) return Array.Empty<byte>();

            var outBuf = new byte[decompSize];
            int o = 0;
            int src = hdr + 16;
            int end = hdr + 16 + compSize;

            int numLiterals = f[hdr + 15];
            for (int i = 0; i < numLiterals && o < decompSize && src < end; i++)
                outBuf[o++] = f[src++];

            while (o < decompSize && src + 4 <= end) {
                int dist = f[src] | (f[src + 1] << 8);
                int len = f[src + 2];
                numLiterals = f[src + 3];
                src += 4;

                if (len > 0) {
                    int matchLen = len + 4;
                    int cpy = o - dist;
                    if (cpy < 0) break;
                    for (int i = 0; i < matchLen && o < decompSize; i++)
                        outBuf[o++] = outBuf[cpy + i];
                }

                for (int i = 0; i < numLiterals && o < decompSize && src < end; i++)
                    outBuf[o++] = f[src++];
            }
            return outBuf;
        }
        static void WriteIndexedPng(string path, int w, int h, byte[] idx, byte[] rgb, byte[] alpha)
        {
            if (w <= 0) w = 1;
            if (h <= 0) h = 1;
            var raw = new byte[(w + 1) * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) {
                    int i = y * w + x;
                    raw[y * (w + 1) + 1 + x] = (byte)(i < idx.Length ? idx[i] : 0);
                }

            using var fs = File.Create(path);
            fs.Write(PNG_SIG, 0, 8);

            var ihdr = new byte[13];
            WriteBE(ihdr, 0, (uint)w);
            WriteBE(ihdr, 4, (uint)h);
            ihdr[8] = 8;
            ihdr[9] = 3;
            Chunk(fs, "IHDR", ihdr);
            Chunk(fs, "PLTE", rgb);
            Chunk(fs, "tRNS", alpha);
            Chunk(fs, "IDAT", Zlib(raw));
            Chunk(fs, "IEND", Array.Empty<byte>());
        }
        static void Chunk(Stream s, string type, byte[] data)
        {
            var t = Encoding.ASCII.GetBytes(type);
            var len = new byte[4]; WriteBE(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            s.Write(t, 0, 4);
            s.Write(data, 0, data.Length);

            EnsureCrcTable();
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < t.Length; i++)
                crc = _crcT[(crc ^ t[i]) & 0xFF] ^ (crc >> 8);
            for (int i = 0; i < data.Length; i++)
                crc = _crcT[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            crc ^= 0xFFFFFFFF;

            var c = new byte[4]; WriteBE(c, 0, crc);
            s.Write(c, 0, 4);
        }
        static void EnsureCrcTable()
        {
            if (_crcT != null) return;
            _crcT = new uint[256];
            for (uint n = 0; n < 256; n++) {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                _crcT[n] = c;
            }
        }
        static byte[] Zlib(byte[] raw)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x78); ms.WriteByte(0x9C);
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
                ds.Write(raw, 0, raw.Length);
            uint a = Adler32(raw);
            ms.WriteByte((byte)(a >> 24)); ms.WriteByte((byte)(a >> 16));
            ms.WriteByte((byte)(a >> 8)); ms.WriteByte((byte)a);
            return ms.ToArray();
        }
        static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }
        static void WriteBE(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }
    }
}
