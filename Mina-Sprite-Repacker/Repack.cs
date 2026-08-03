using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Mina_Sprite_Repacker
{
    public static class Repack
    {
        static readonly byte[] PNG_SIG = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        public static void RepackAllSprites()
        {
#if DEBUG
            File.Copy(@"C:\Users\Phil\Desktop\unpak\data\dialogue\charPortraitTrainAuthority.anb.yc", @"C:\Users\Phil\AppData\Roaming\Yacht Club Games\Mina the Hollower\mods\UFO50\data\dialogue\charPortraitTrainAuthority.anb.yc", true);
            File.Copy(@"C:\Users\Phil\Desktop\unpak\data\NPCs\trainAuthority.anb.yc", @"C:\Users\Phil\AppData\Roaming\Yacht Club Games\Mina the Hollower\mods\UFO50\data\NPCs\trainAuthority.anb.yc", true);
#endif
            var palette = LoadGlobalPalette(Constants.globalPalettePathLocal);
            if (palette == null) {
                Console.WriteLine($"Could not load this program's copy of the global palette file: {Constants.globalPalettePathLocal}");
                return;
            }

            var pngFiles = Directory.EnumerateFiles(Constants.spritesRoot, "*.png", SearchOption.AllDirectories).ToList();
            if (pngFiles.Count == 0) {
                Console.WriteLine("No PNG files found to repack.");
                return;
            }

            var folders = pngFiles.GroupBy(p => Path.GetDirectoryName(p));
            foreach (var folder in folders) {
                RepackFolder(folder.Key, folder.ToList(), palette.Value.rgb, palette.Value.alpha);
            }
        }
        static void RepackFolder(string folderPath, List<string> pngPaths, byte[] palRgb, byte[] palAlpha)
        {
            // Reconstruct the .anb.yc path from the sprite folder path
            string relativePath = Path.GetRelativePath(Constants.spritesRoot, folderPath);
            string anbPath = Path.Combine(Constants.currentDirectory, relativePath + ".anb.yc");

            if (!File.Exists(anbPath)) {
                Console.WriteLine($"Cannot find original file: {anbPath}");
                return;
            }

            // Read all PNGs into RGBA pixel data
            var sprites = new Dictionary<int, (int Width, int Height, byte[] Rgba)>();
            foreach (string pngPath in pngPaths) {
                string name = Path.GetFileNameWithoutExtension(pngPath);
                string indexStr = name.Contains('_') ? name.Split('_')[0] : name;
                if (!int.TryParse(indexStr, out int texIdx)) {
                    Console.WriteLine($"Cannot determine texture index from '{name}'");
                    continue;
                }
                var png = ReadPngToRgba(pngPath);
                if (png.Rgba == null) {
                    Console.WriteLine($"Failed to read PNG: {pngPath}");
                    continue;
                }
                sprites[texIdx] = (png.Width, png.Height, png.Rgba);
            }
            if (sprites.Count == 0) return;

            // Collect every unique color across all PNGs in this folder
            var uniqueColors = new HashSet<uint>();
            foreach (var s in sprites.Values) {
                for (int i = 0; i < s.Rgba.Length; i += 4) {
                    uint key = PackColor(s.Rgba[i], s.Rgba[i + 1], s.Rgba[i + 2], s.Rgba[i + 3]);
                    uniqueColors.Add(key);
                }
            }

            // Map each unique color to the closest global palette index
            var colorMap = new Dictionary<uint, byte>();
            foreach (uint key in uniqueColors) {
                byte r = (byte)(key >> 24);
                byte g = (byte)(key >> 16);
                byte b = (byte)(key >> 8);
                byte a = (byte)key;
                colorMap[key] = FindClosestPaletteIndex(r, g, b, a, palRgb, palAlpha);
            }

            // Convert each sprite to palette-indexed data, then WFLZ compress
            var compressed = new Dictionary<int, (int Width, int Height, byte[] Data)>();
            foreach (var kvp in sprites) {
                var (w, h, rgba) = kvp.Value;
                var indexed = new byte[w * h];
                for (int i = 0; i < w * h; i++) {
                    uint key = PackColor(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
                    indexed[i] = colorMap[key];
                }
                compressed[kvp.Key] = (w, h, WflzCompress(indexed));
            }

            // Patch the .anb.yc file
            string content = File.ReadAllText(anbPath);
            content = Regex.Replace(content, @"m_paletteName:\s*""[^""]*""", @"m_paletteName: " + Constants.globalPalettePath);
            content = ReplaceTextureData(content, compressed);
            File.WriteAllText(anbPath, content);

            Console.WriteLine($"Repacked: {anbPath} ({sprites.Count} sprites)");
        }
        static (byte[] rgb, byte[] alpha)? LoadGlobalPalette(string resourceName)
        {
            string content;

            var assembly = typeof(Constants).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(resourceName)) {
                if (stream == null) return null;
                using (StreamReader reader = new StreamReader(stream)) {
                    content = reader.ReadToEnd();
                }
            }

            string[] firstLines = content.Split('\n', 3);
            if (firstLines.Length < 2 || !firstLines[1].Trim().StartsWith("ycPaletteFormat"))
                return null;

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
        static uint PackColor(byte r, byte g, byte b, byte a)
            => (uint)((r << 24) | (g << 16) | (b << 8) | a);
        static byte FindClosestPaletteIndex(byte r, byte g, byte b, byte a,
            byte[] palRgb, byte[] palAlpha)
        {
            // Fully transparent pixels always map to index 0
            if (a == 0) return 0;

            int bestIndex = 0;
            int bestDist = int.MaxValue;

            for (int i = 0; i < 256; i++) {
                int pr = palRgb[i * 3];
                int pg = palRgb[i * 3 + 1];
                int pb = palRgb[i * 3 + 2];
                int pa = palAlpha[i];

                int dr = r - pr;
                int dg = g - pg;
                int db = b - pb;
                int da = a - pa;

                int dist = dr * dr + dg * dg + db * db + da * da;
                if (dist < bestDist) {
                    bestDist = dist;
                    bestIndex = i;
                    if (dist == 0) break;
                }
            }
            return (byte)bestIndex;
        }
        static (int Width, int Height, byte[] Rgba) ReadPngToRgba(string path)
        {
            byte[] file;
            try { file = File.ReadAllBytes(path); }
            catch { return (0, 0, null); }

            if (file.Length < 8) return (0, 0, null);
            for (int i = 0; i < 8; i++)
                if (file[i] != PNG_SIG[i]) return (0, 0, null);

            int pos = 8;
            int width = 0, height = 0, bitDepth = 0, colorType = 0;
            byte[] plteRgb = null;
            byte[] trnsData = null;
            var idatChunks = new List<byte[]>();

            while (pos + 8 <= file.Length) {
                uint chunkLen = ReadBE(file, pos);
                string type = Encoding.ASCII.GetString(file, pos + 4, 4);
                int dataStart = pos + 8;
                if (dataStart + (int)chunkLen > file.Length) break;

                byte[] chunkData = new byte[chunkLen];
                Buffer.BlockCopy(file, dataStart, chunkData, 0, (int)chunkLen);
                pos = dataStart + (int)chunkLen + 4;

                switch (type) {
                    case "IHDR":
                        width = (int)ReadBE(chunkData, 0);
                        height = (int)ReadBE(chunkData, 4);
                        bitDepth = chunkData[8];
                        colorType = chunkData[9];
                        break;
                    case "PLTE": plteRgb = chunkData; break;
                    case "tRNS": trnsData = chunkData; break;
                    case "IDAT": idatChunks.Add(chunkData); break;
                    case "IEND": goto done;
                }
            }
        done:

            if (width <= 0 || height <= 0 || idatChunks.Count == 0)
                return (0, 0, null);

            // Concatenate IDAT chunks and decompress
            int totalLen = 0;
            foreach (var c in idatChunks) totalLen += c.Length;
            var allIdat = new byte[totalLen];
            int off = 0;
            foreach (var c in idatChunks) {
                Buffer.BlockCopy(c, 0, allIdat, off, c.Length);
                off += c.Length;
            }

            byte[] rawData;
            try {
                using var ms = new MemoryStream(allIdat, 2, allIdat.Length - 2);
                using var ds = new DeflateStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                ds.CopyTo(outMs);
                rawData = outMs.ToArray();
            }
            catch { return (0, 0, null); }

            // Bytes per pixel (for filter reconstruction)
            int bpp = colorType switch {
                0 => Math.Max(1, bitDepth / 8),
                2 => 3 * (bitDepth / 8),
                3 => 1,
                4 => 2 * (bitDepth / 8),
                6 => 4 * (bitDepth / 8),
                _ => 0
            };
            if (bpp == 0) return (0, 0, null);

            // Raw bytes per scanline (excluding the filter byte)
            int stride = colorType switch {
                0 => (width * bitDepth + 7) / 8,
                2 => width * 3 * (bitDepth / 8),
                3 => width,
                4 => width * 2 * (bitDepth / 8),
                6 => width * 4 * (bitDepth / 8),
                _ => 0
            };

            // Unfilter scanlines
            var unfiltered = new byte[stride * height];
            int src = 0;
            for (int y = 0; y < height; y++) {
                if (src >= rawData.Length) break;
                byte filter = rawData[src++];
                int row = y * stride;
                int prevRow = (y - 1) * stride;

                for (int x = 0; x < stride && src < rawData.Length; x++, src++) {
                    byte raw = rawData[src];
                    byte left = (x >= bpp) ? unfiltered[row + x - bpp] : (byte)0;
                    byte up = (y > 0) ? unfiltered[prevRow + x] : (byte)0;
                    byte upLeft = (y > 0 && x >= bpp) ? unfiltered[prevRow + x - bpp] : (byte)0;

                    unfiltered[row + x] = filter switch {
                        1 => (byte)(raw + left),
                        2 => (byte)(raw + up),
                        3 => (byte)(raw + (left + up) / 2),
                        4 => (byte)(raw + PaethPredictor(left, up, upLeft)),
                        _ => raw
                    };
                }
            }

            // Convert to RGBA
            var rgba = new byte[width * height * 4];
            switch (colorType) {
                case 3: // Indexed
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++) {
                            int idx = unfiltered[y * stride + x];
                            int pi = (y * width + x) * 4;
                            if (plteRgb != null && idx * 3 + 2 < plteRgb.Length) {
                                rgba[pi] = plteRgb[idx * 3];
                                rgba[pi + 1] = plteRgb[idx * 3 + 1];
                                rgba[pi + 2] = plteRgb[idx * 3 + 2];
                            }
                            rgba[pi + 3] = (trnsData != null && idx < trnsData.Length)
                                ? trnsData[idx] : (byte)255;
                        }
                    break;

                case 2: // RGB
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++) {
                            int si = y * stride + x * 3;
                            int pi = (y * width + x) * 4;
                            rgba[pi] = unfiltered[si];
                            rgba[pi + 1] = unfiltered[si + 1];
                            rgba[pi + 2] = unfiltered[si + 2];
                            rgba[pi + 3] = 255;
                        }
                    break;

                case 6: // RGBA
                    Buffer.BlockCopy(unfiltered, 0, rgba, 0,
                        Math.Min(unfiltered.Length, rgba.Length));
                    break;

                case 0: // Grayscale
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++) {
                            byte v = unfiltered[y * stride + x];
                            int pi = (y * width + x) * 4;
                            rgba[pi] = v; rgba[pi + 1] = v; rgba[pi + 2] = v;
                            rgba[pi + 3] = 255;
                        }
                    break;

                case 4: // Grayscale + Alpha
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++) {
                            int si = y * stride + x * 2;
                            int pi = (y * width + x) * 4;
                            byte v = unfiltered[si];
                            rgba[pi] = v; rgba[pi + 1] = v; rgba[pi + 2] = v;
                            rgba[pi + 3] = unfiltered[si + 1];
                        }
                    break;
            }
            return (width, height, rgba);
        }
        static byte PaethPredictor(byte a, byte b, byte c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }
        static byte[] WflzCompress(byte[] input)
        {
            if (input.Length == 0) {
                var hdr = new byte[16];
                hdr[0] = 0x57; hdr[1] = 0x46; hdr[2] = 0x4C; hdr[3] = 0x5A;
                return hdr;
            }

            var matches = new List<(int Pos, int Dist, int Len)>();
            for (int s = 1; s < input.Length; s++) {
                FindBestMatch(input, s, out int dist, out int len);
                if (len >= 5) {
                    matches.Add((s, dist, len));
                    s += len - 1;
                }
            }

            var payload = new MemoryStream();

            int firstMatchStart = (matches.Count > 0) ? matches[0].Pos : input.Length;
            int initLitCount = Math.Min(firstMatchStart, 255);
            for (int i = 0; i < initLitCount; i++)
                payload.WriteByte(input[i]);

            EmitLiterals(payload, input, initLitCount, firstMatchStart - initLitCount);

            for (int mi = 0; mi < matches.Count; mi++) {
                var (mpos, mdist, mlen) = matches[mi];
                int litStart = mpos + mlen;
                int litEnd = (mi + 1 < matches.Count) ? matches[mi + 1].Pos : input.Length;
                int litCount = litEnd - litStart;

                int firstChunk = Math.Min(litCount, 255);
                payload.WriteByte((byte)(mdist & 0xFF));
                payload.WriteByte((byte)((mdist >> 8) & 0xFF));
                payload.WriteByte((byte)(mlen - 4));
                payload.WriteByte((byte)firstChunk);
                for (int i = 0; i < firstChunk; i++)
                    payload.WriteByte(input[litStart + i]);

                EmitLiterals(payload, input, litStart + firstChunk, litCount - firstChunk);
            }

            byte[] payloadBytes = payload.ToArray();
            var result = new byte[16 + payloadBytes.Length];
            result[0] = 0x57; result[1] = 0x46; result[2] = 0x4C; result[3] = 0x5A;
            WriteLE(result, 4, (uint)payloadBytes.Length);
            WriteLE(result, 8, (uint)input.Length);
            result[15] = (byte)initLitCount;
            Buffer.BlockCopy(payloadBytes, 0, result, 16, payloadBytes.Length);
            return result;
        }
        static void EmitLiterals(MemoryStream payload, byte[] input, int start, int count)
        {
            int pos = start;
            int remaining = count;
            while (remaining > 0) {
                int chunk = Math.Min(remaining, 255);
                payload.WriteByte(0); payload.WriteByte(0); // dist = 0
                payload.WriteByte(0);                       // len = 0
                payload.WriteByte((byte)chunk);
                for (int i = 0; i < chunk; i++)
                    payload.WriteByte(input[pos++]);
                remaining -= chunk;
            }
        }
        static void FindBestMatch(byte[] data, int pos, out int bestDist, out int bestLen)
        {
            bestDist = 0;
            bestLen = 0;

            int maxDist = Math.Min(pos, 65535);
            int maxLen = Math.Min(data.Length - pos, 259); // len byte max 255 → 255+4 = 259

            for (int dist = 1; dist <= maxDist; dist++) {
                int len = 0;
                while (len < maxLen && data[pos + len] == data[pos - dist + len])
                    len++;
                if (len >= 5 && len > bestLen) {
                    bestLen = len;
                    bestDist = dist;
                    if (bestLen >= maxLen) break;
                }
            }
        }
        static string ReplaceTextureData(string content, Dictionary<int, (int Width, int Height, byte[] Data)> textures)
        {
            int texPos = content.IndexOf("m_textures:");
            if (texPos < 0) return content;

            int arrOpen = content.IndexOf('[', texPos);
            int arrClose = FindClosingBrace(content, arrOpen);
            if (arrClose < 0) return content;

            string section = content.Substring(arrOpen, arrClose - arrOpen + 1);

            // Locate every ycCutter2Texture block
            var blocks = new List<(int BraceOpen, int BraceClose)>();
            foreach (Match m in Regex.Matches(section, @"ycCutter2Texture\b")) {
                int bo = section.IndexOf('{', m.Index);
                int bc = FindClosingBrace(section, bo);
                if (bc >= 0) blocks.Add((bo, bc));
            }

            // Patch in reverse order so earlier indices stay valid
            var sb = new StringBuilder(section);
            for (int i = blocks.Count - 1; i >= 0; i--) {
                if (!textures.ContainsKey(i)) continue;

                var (bo, bc) = blocks[i];
                var (w, h, data) = textures[i];
                string block = section.Substring(bo, bc - bo + 1);

                string newBlock = Regex.Replace(block, @"m_width:\s*\d+", $"m_width: {w}");
                newBlock = Regex.Replace(newBlock, @"m_height:\s*\d+", $"m_height: {h}");
                newBlock = Regex.Replace(newBlock, @"\bsize:\s*\d+", $"size: {data.Length}");
                newBlock = Regex.Replace(newBlock, @"\bdata:\s*""[^""]*""",
                    $@"data: ""{Convert.ToBase64String(data)}""");

                sb.Remove(bo, bc - bo + 1);
                sb.Insert(bo, newBlock);
            }

            return content.Substring(0, arrOpen) + sb.ToString() + content.Substring(arrClose + 1);
        }
        static int FindClosingBrace(string text, int openPos)
        {
            char open = text[openPos];
            char close = open == '{' ? '}' : ']';
            int depth = 1;
            bool inStr = false;
            for (int i = openPos + 1; i < text.Length; i++) {
                char c = text[i];
                if (c == '"' && text[i - 1] != '\\') inStr = !inStr;
                if (inStr) continue;
                if (c == open) depth++;
                if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }
        static uint ReadBE(byte[] b, int o)
            => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
        static void WriteLE(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }
    }
}