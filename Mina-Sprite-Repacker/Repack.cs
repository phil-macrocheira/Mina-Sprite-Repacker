using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Mina_Sprite_Repacker
{
    public static class Repack
    {
        public static void RepackAllSprites(string rootDirectory)
        {
            string spritesDirectory = Path.Combine(rootDirectory, "_my_sprites");
            var spritePaths = Directory.EnumerateFiles(spritesDirectory, "*.png", SearchOption.AllDirectories);

            foreach (string spritePath in spritePaths) {
                RepackSingleSprite(rootDirectory, spritePath);
            }

            return;
        }
        public static void RepackSingleSprite(string rootDirectory, string spritePath)
        {
            if (!File.Exists(spritePath)) {
                Console.WriteLine($"PNG not found: {spritePath}");
                return;
            }

            // temp fix
            string marker = Path.DirectorySeparatorChar + "_my_sprites" + Path.DirectorySeparatorChar;
            int markerIdx = spritePath.IndexOf(marker);
            if (markerIdx < 0) {
                Console.WriteLine($"Cannot find '_my_sprites' in path: {spritePath}");
                return;
            }
            rootDirectory = spritePath.Substring(0, markerIdx);

            string spritesRoot = Path.Combine(rootDirectory, "_my_sprites");
            string relativeFromSprites = Path.GetRelativePath(spritesRoot, spritePath);
            string relativeDir = Path.GetDirectoryName(relativeFromSprites) ?? "";
            string parentOfPng = Path.GetFileName(relativeDir);
            string aboveParent = Path.GetDirectoryName(relativeDir) ?? "";
            string anbPath = Path.Combine(rootDirectory, aboveParent, parentOfPng + ".anb.yc");

            if (!File.Exists(anbPath)) {
                Console.WriteLine($"Cannot find original file: {anbPath}");
                return;
            }

            string pngName = Path.GetFileNameWithoutExtension(spritePath);
            if (!int.TryParse(pngName, out int textureIndex)) {
                Console.WriteLine($"Cannot determine texture index from '{pngName}'");
                return;
            }

            // Read indexed pixels and PNG palette
            byte[] indexed = ReadIndexedPng(spritePath, out int w, out int h);
            var (pngRgb, pngAlpha, pngColorCount) = ReadPngPalette(spritePath);

            // Compress pixels
            byte[] compressed = WflzCompress(indexed);

            string content = File.ReadAllText(anbPath);

            // Update palette file using global palette lookup
            var palMatch = Regex.Match(content, @"m_paletteName:\s*""([^""]+)""");
            if (palMatch.Success) {
                string palettePath = Path.Combine(rootDirectory, palMatch.Groups[1].Value);
                string globalPalPath = FindGlobalPalette(rootDirectory);

                if (globalPalPath == null) {
                    Console.WriteLine("Could not find global.pal.yc");
                    return;
                }

                UpdatePaletteWithGlobal(palettePath, globalPalPath, pngRgb, pngAlpha, pngColorCount);
            }

            // Replace texture data, dimensions, and size
            string modified = ReplaceTextureData(content, textureIndex, compressed, w, h);
            if (modified == null) {
                Console.WriteLine($"Failed to find texture index {textureIndex} in '{anbPath}'");
                return;
            }

            File.WriteAllText(anbPath, modified);
        }
        static string FindGlobalPalette(string rootDirectory)
        {
            var candidates = Directory.EnumerateFiles(rootDirectory, "global.pal.yc", SearchOption.AllDirectories);
            return candidates.FirstOrDefault();
        }
        struct PaletteColor
        {
            public byte R, G, B, A;
            public bool IsEmpty;
        }
        static List<PaletteColor> ParsePaletteColors(string palettePath)
        {
            var colors = new List<PaletteColor>();
            if (!File.Exists(palettePath)) return colors;

            string content = File.ReadAllText(palettePath);
            int colorsPos = content.IndexOf("m_colors:");
            if (colorsPos < 0) return colors;

            int arrOpen = content.IndexOf('[', colorsPos);
            int arrClose = FindClosingBrace(content, arrOpen);
            if (arrClose < 0) return colors;

            string colorsSection = content.Substring(arrOpen + 1, arrClose - arrOpen - 1);

            int pos = 0;
            int reserveEnd = colorsSection.IndexOf(')');
            if (reserveEnd >= 0) pos = reserveEnd + 1;

            while (pos < colorsSection.Length) {
                while (pos < colorsSection.Length && char.IsWhiteSpace(colorsSection[pos])) pos++;
                if (pos >= colorsSection.Length) break;

                if (colorsSection[pos] == ',') {
                    pos++;
                    continue;
                }

                int ycPos = colorsSection.IndexOf("ycColor", pos);
                if (ycPos < 0) break;

                string between = colorsSection.Substring(pos, ycPos - pos);
                int bareCommas = CountBareCommas(between);
                for (int i = 0; i < bareCommas; i++)
                    colors.Add(new PaletteColor { IsEmpty = true });

                int bo = colorsSection.IndexOf('{', ycPos);
                int bc = colorsSection.IndexOf('}', bo);
                if (bc < 0) break;

                string block = colorsSection.Substring(bo, bc - bo + 1);
                var rm = Regex.Match(block, @"\br:\s*(\d+)");
                var gm = Regex.Match(block, @"\bg:\s*(\d+)");
                var bm = Regex.Match(block, @"\bb:\s*(\d+)");
                var am = Regex.Match(block, @"\ba:\s*(\d+)");

                colors.Add(new PaletteColor {
                    R = rm.Success ? byte.Parse(rm.Groups[1].Value) : (byte)0,
                    G = gm.Success ? byte.Parse(gm.Groups[1].Value) : (byte)0,
                    B = bm.Success ? byte.Parse(bm.Groups[1].Value) : (byte)0,
                    A = am.Success ? byte.Parse(am.Groups[1].Value) : (byte)0,
                    IsEmpty = false
                });

                pos = bc + 1;
            }

            return colors;
        }
        static int CountBareCommas(string s)
        {
            int count = 0;
            int depth = 0;
            foreach (char c in s) {
                if (c == '{') depth++;
                else if (c == '}') depth--;
                else if (c == ',' && depth == 0) count++;
            }
            return count;
        }
        static int FindGlobalIndex(List<PaletteColor> globalColors, byte r, byte g, byte b, byte a)
        {
            if (a == 0) return -1;

            int bestIdx = -1;
            int bestDist = int.MaxValue;

            for (int i = 0; i < globalColors.Count; i++) {
                var gc = globalColors[i];
                if (gc.IsEmpty) continue;
                if (gc.A == 0) continue;

                int dr = r - gc.R;
                int dg = g - gc.G;
                int db = b - gc.B;
                int dist = dr * dr + dg * dg + db * db;

                if (dist == 0) return i;
                if (dist < bestDist) { bestDist = dist; bestIdx = i; }
            }

            if (bestDist > 0 && bestIdx >= 0)
                Console.WriteLine($"Color {r},{g},{b} is outside global palette, using palette {bestIdx} instead");

            return bestIdx;
        }
        static string ReplaceTextureData(string content, int textureIndex, byte[] compressed, int newWidth, int newHeight)
        {
            string newBase64 = Convert.ToBase64String(compressed);
            int newSize = compressed.Length;

            int texPos = content.IndexOf("m_textures:");
            if (texPos < 0) return null;

            int arrOpen = content.IndexOf('[', texPos);
            int arrClose = FindClosingBrace(content, arrOpen);
            if (arrClose < 0) return null;

            string texSection = content.Substring(arrOpen, arrClose - arrOpen + 1);

            int count = 0;
            foreach (Match tm in Regex.Matches(texSection, @"ycCutter2Texture\b")) {
                if (count == textureIndex) {
                    int absPos = arrOpen + tm.Index;
                    int bo = content.IndexOf('{', absPos);
                    int bc = FindClosingBrace(content, bo);
                    if (bc < 0) return null;

                    string block = content.Substring(bo, bc - bo + 1);

                    // Replace m_width
                    string newBlock = Regex.Replace(block,
                        @"(m_width:\s*)\d+",
                        "${1}" + newWidth);

                    // Replace m_height
                    newBlock = Regex.Replace(newBlock,
                        @"(m_height:\s*)\d+",
                        "${1}" + newHeight);

                    // Replace data
                    newBlock = Regex.Replace(newBlock,
                        @"(\bdata:\s*"")[^""]*("")",
                        "${1}" + newBase64 + "${2}");

                    // Replace size
                    newBlock = Regex.Replace(newBlock,
                        @"(\bsize:\s*)\d+",
                        "${1}" + newSize);

                    return content.Substring(0, bo) + newBlock + content.Substring(bc + 1);
                }
                count++;
            }
            return null;
        }
        static byte[] ReadIndexedPng(string path, out int width, out int height)
        {
            byte[] file = File.ReadAllBytes(path);
            int pos = 8;
            width = 0;
            height = 0;

            var idatChunks = new MemoryStream();

            while (pos + 12 <= file.Length) {
                int chunkLen = (file[pos] << 24) | (file[pos + 1] << 16)
                             | (file[pos + 2] << 8) | file[pos + 3];
                string type = Encoding.ASCII.GetString(file, pos + 4, 4);

                if (type == "IHDR") {
                    width = (file[pos + 8] << 24) | (file[pos + 9] << 16)
                           | (file[pos + 10] << 8) | file[pos + 11];
                    height = (file[pos + 12] << 24) | (file[pos + 13] << 16)
                           | (file[pos + 14] << 8) | file[pos + 15];
                }
                else if (type == "IDAT") {
                    idatChunks.Write(file, pos + 8, chunkLen);
                }

                pos += 12 + chunkLen;
            }

            byte[] zlib = idatChunks.ToArray();
            using var ms = new MemoryStream(zlib, 2, zlib.Length - 2);
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            using var output = new MemoryStream();
            ds.CopyTo(output);
            byte[] raw = output.ToArray();

            int stride = width + 1;
            byte[] indices = new byte[width * height];
            byte[] prevRow = new byte[width];

            for (int y = 0; y < height; y++) {
                int filter = raw[y * stride];
                int src = y * stride + 1;
                int dst = y * width;

                for (int x = 0; x < width; x++) {
                    byte cur = raw[src + x];
                    byte a = x > 0 ? indices[dst + x - 1] : (byte)0;
                    byte b = prevRow[x];
                    byte c = x > 0 ? prevRow[x - 1] : (byte)0;

                    indices[dst + x] = filter switch {
                        1 => (byte)(cur + a),
                        2 => (byte)(cur + b),
                        3 => (byte)(cur + ((a + b) / 2)),
                        4 => (byte)(cur + Paeth(a, b, c)),
                        _ => cur
                    };
                }

                Array.Copy(indices, dst, prevRow, 0, width);
            }

            return indices;
        }
        static (byte[] rgb, byte[] alpha, int count) ReadPngPalette(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            int pos = 8;
            byte[] plte = null;
            byte[] trns = null;

            while (pos + 12 <= file.Length) {
                int chunkLen = (file[pos] << 24) | (file[pos + 1] << 16)
                             | (file[pos + 2] << 8) | file[pos + 3];
                string type = Encoding.ASCII.GetString(file, pos + 4, 4);

                if (type == "PLTE") {
                    plte = new byte[chunkLen];
                    Array.Copy(file, pos + 8, plte, 0, chunkLen);
                }
                else if (type == "tRNS") {
                    trns = new byte[chunkLen];
                    Array.Copy(file, pos + 8, trns, 0, chunkLen);
                }

                pos += 12 + chunkLen;
            }

            int count = plte != null ? plte.Length / 3 : 0;
            var rgb = new byte[count * 3];
            var alpha = new byte[count];

            if (plte != null) Array.Copy(plte, rgb, plte.Length);

            for (int i = 0; i < count; i++)
                alpha[i] = 255;
            if (trns != null)
                for (int i = 0; i < Math.Min(trns.Length, count); i++)
                    alpha[i] = trns[i];

            return (rgb, alpha, count);
        }
        static void UpdatePaletteWithGlobal(string palettePath, string globalPalPath, byte[] pngRgb, byte[] pngAlpha, int pngColorCount)
        {
            if (!File.Exists(palettePath)) {
                Console.WriteLine($"  Palette file not found: {palettePath}");
                return;
            }

            string content = File.ReadAllText(palettePath);
            string[] firstLines = content.Split('\n', 3);
            if (firstLines.Length < 2 || !firstLines[1].Trim().StartsWith("ycPaletteFormat")) {
                Console.WriteLine($"Not a palette file: {palettePath}");
                return;
            }

            var globalColors = ParsePaletteColors(globalPalPath);
            if (globalColors.Count == 0) {
                Console.WriteLine($"Could not parse global palette: {globalPalPath}");
                return;
            }

            var globalIndices = new int[pngColorCount];
            int paletteWidth = 0;

            for (int i = 0; i < pngColorCount; i++) {
                byte r = pngRgb[i * 3];
                byte g = pngRgb[i * 3 + 1];
                byte b = pngRgb[i * 3 + 2];
                byte a = pngAlpha[i];

                globalIndices[i] = FindGlobalIndex(globalColors, r, g, b, a);
                if (globalIndices[i] >= 0) paletteWidth = i + 1;
            }

            // Update m_paletteWidth
            content = Regex.Replace(content,
                @"(m_paletteWidth:\s*)\d+",
                "${1}" + paletteWidth);

            // Update m_colors
            int colorsPos = content.IndexOf("m_colors:");
            if (colorsPos >= 0) {
                int colorsOpen = content.IndexOf('[', colorsPos);
                int colorsClose = FindClosingBrace(content, colorsOpen);
                if (colorsClose >= 0) {
                    var reserveMatch = Regex.Match(
                        content.Substring(colorsOpen, colorsClose - colorsOpen + 1),
                        @"Reserve:\s*(\d+)");
                    int reserve = reserveMatch.Success ? int.Parse(reserveMatch.Groups[1].Value) : 255;

                    string newColors = BuildColorsSection(pngRgb, pngAlpha, pngColorCount, reserve);
                    content = content.Substring(0, colorsOpen) + newColors + content.Substring(colorsClose + 1);
                }
            }

            // Update m_globalIndexData
            int indexPos = content.IndexOf("m_globalIndexData:");
            if (indexPos >= 0) {
                int indexOpen = content.IndexOf('[', indexPos);
                int indexClose = FindClosingBrace(content, indexOpen);
                if (indexClose >= 0) {
                    var idxReserve = Regex.Match(
                        content.Substring(indexOpen, indexClose - indexOpen + 1),
                        @"Reserve:\s*(\d+)");
                    int reserve = idxReserve.Success ? int.Parse(idxReserve.Groups[1].Value) : 255;

                    string newIndex = BuildGlobalIndexSection(globalIndices, pngColorCount, reserve);
                    content = content.Substring(0, indexOpen) + newIndex + content.Substring(indexClose + 1);
                }
            }

            File.WriteAllText(palettePath, content);
        }
        static string BuildColorsSection(byte[] rgb, byte[] alpha, int colorCount, int reserve)
        {
            var sb = new StringBuilder();
            sb.Append("[ ( Reserve: " + reserve + " ) \n");

            bool needComma = false;
            for (int i = 0; i < reserve; i++) {
                if (needComma) sb.Append(", ");
                needComma = true;

                if (i >= colorCount)
                    continue;

                byte r = rgb[i * 3];
                byte g = rgb[i * 3 + 1];
                byte b = rgb[i * 3 + 2];
                byte a = alpha[i];

                // Transparent entry: write ycColor with RGB but no alpha
                if (a == 0) {
                    sb.Append("\t\tycColor\n\t\t{\n");
                    if (r != 0) sb.Append($"\t\t\tr: {r},\n");
                    if (g != 0) sb.Append($"\t\t\tg: {g},\n");
                    if (b != 0) sb.Append($"\t\t\tb: {b},\n");
                    sb.Append("\t\t}");
                    continue;
                }

                sb.Append("\t\tycColor\n\t\t{\n");
                if (r != 0) sb.Append($"\t\t\tr: {r},\n");
                if (g != 0) sb.Append($"\t\t\tg: {g},\n");
                if (b != 0) sb.Append($"\t\t\tb: {b},\n");
                if (a != 0) sb.Append($"\t\t\ta: {a},\n");
                sb.Append("\t\t}");
            }

            sb.Append(" ]");
            return sb.ToString();
        }
        static string BuildGlobalIndexSection(int[] globalIndices, int colorCount, int reserve)
        {
            var sb = new StringBuilder();
            sb.Append($"[ ( Reserve: {reserve} ) ");

            bool needComma = false;
            for (int i = 0; i < reserve; i++) {
                if (needComma) sb.Append(", ");
                needComma = true;

                if (i < colorCount && globalIndices[i] >= 0)
                    sb.Append(globalIndices[i]);
            }

            sb.Append(" ]");
            return sb.ToString();
        }
        static byte Paeth(byte a, byte b, byte c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }
        static byte[] WflzCompress(byte[] data)
        {
            using var payload = new MemoryStream();

            int pos = 0;
            int firstBatch = Math.Min(255, data.Length);

            if (firstBatch > 0)
                payload.Write(data, 0, firstBatch);
            pos = firstBatch;

            while (pos < data.Length) {
                int count = Math.Min(255, data.Length - pos);
                payload.WriteByte(0);
                payload.WriteByte(0);
                payload.WriteByte(0);
                payload.WriteByte((byte)count);
                payload.Write(data, pos, count);
                pos += count;
            }

            byte[] body = payload.ToArray();
            var result = new byte[16 + body.Length];

            result[0] = 0x57; result[1] = 0x46;
            result[2] = 0x4C; result[3] = 0x5A;
            WriteLE(result, 4, body.Length);
            WriteLE(result, 8, data.Length);
            result[15] = (byte)firstBatch;

            Array.Copy(body, 0, result, 16, body.Length);
            return result;
        }

        static void WriteLE(byte[] buf, int off, int val)
        {
            buf[off] = (byte)(val);
            buf[off + 1] = (byte)(val >> 8);
            buf[off + 2] = (byte)(val >> 16);
            buf[off + 3] = (byte)(val >> 24);
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
    }
}