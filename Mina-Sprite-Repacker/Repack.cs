using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Mina_Sprite_Repacker
{
    public static class Repack
    {
        public static void RepackAllSprites()
        {
            var spritePaths = Directory.EnumerateFiles(Constants.spritesRoot, "*.png", SearchOption.AllDirectories);

            foreach (string spritePath in spritePaths) {
                RepackSingleSprite(spritePath);
            }

            return;
        }
        public static void RepackSingleSprite(string spritePath)
        {
            if (!File.Exists(spritePath)) {
                Console.WriteLine($"PNG not found: {spritePath}");
                return;
            }

            string anbFilename = Path.GetFileName(Path.GetDirectoryName(spritePath)) + ".anb.yc";
            string anbPath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(spritePath)),anbFilename);

            string[] pathParts = anbPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathPartsFiltered = pathParts.Where(part => !string.Equals(part, Constants.spritesFolderName, StringComparison.OrdinalIgnoreCase));
            anbPath = string.Join(Path.DirectorySeparatorChar.ToString(), pathPartsFiltered);

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

            // Parse global palette
            string globalPalPath = FindGlobalPalette(Constants.currentDirectory);
            var globalColors = ParsePaletteColors(globalPalPath);
            if (globalColors.Count == 0)
            {
                Console.WriteLine($"Could not parse global palette: {globalPalPath}");
                return;
            }

            // Compress pixels
            byte[] compressed = WflzCompress(indexed);

            string content = File.ReadAllText(anbPath);

            var palMatch = Regex.Match(content, @"m_paletteName:\s*""([^""]+)""");
            if (palMatch.Success) {
                content = Regex.Replace(content, @"m_paletteName:\s*""([^""]+)""", "m_paletteName: " + Constants.globalPalettePath);
            }

            // Replace texture data
            string modified = ReplaceTextureData(content, textureIndex, compressed);
            if (modified == null) {
                Console.WriteLine($"Failed to find texture index {textureIndex} in '{anbPath}'");
                return;
            }

            File.WriteAllText(anbPath, modified);
        }
        static string FindGlobalPalette(string rootDirectory)
        {
            var candidates = Directory.EnumerateFiles(rootDirectory, Constants.globalPaletteFilename, SearchOption.AllDirectories);
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

            // Walk through the array, handling both ycColor blocks and bare commas
            int pos = 0;
            // Skip past the Reserve declaration
            int reserveEnd = colorsSection.IndexOf(')');
            if (reserveEnd >= 0) pos = reserveEnd + 1;

            while (pos < colorsSection.Length) {
                // Skip whitespace
                while (pos < colorsSection.Length && char.IsWhiteSpace(colorsSection[pos])) pos++;
                if (pos >= colorsSection.Length) break;

                if (colorsSection[pos] == ',') {
                    // Check if this is a bare comma (empty slot) or trailing comma after a block
                    // Look back to see if we just finished a ycColor block
                    pos++;
                    continue;
                }

                // Look for ycColor
                int ycPos = colorsSection.IndexOf("ycColor", pos);
                if (ycPos < 0) break;

                // Check if there are bare commas between current pos and this ycColor
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
        static string ReplaceTextureData(string content, int textureIndex, byte[] compressed)
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

                    // Replace data
                    string newBlock = Regex.Replace(block,
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

            int transIndex = -1;

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
                else if (type == "tRNS") {
                    byte minAlpha = 255;
                    for (int i = 0; i < chunkLen; i++) {
                        byte alpha = file[pos + 8 + i];
                        if (alpha < minAlpha) {
                            minAlpha = alpha;
                            transIndex = i;
                        }
                    }
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

            // Ensure transparency is 0
            if (transIndex > 0) {
                for (int i = 0; i < indices.Length; i++) {
                    if (indices[i] == 0) {
                        indices[i] = (byte)transIndex;
                    }
                    else if (indices[i] == transIndex) {
                        indices[i] = 0;
                    }
                }
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

            // Force transparency to be first
            if (count > 1) {
                int transIndex = 0;
                byte minAlpha = alpha[0];
                for (int i = 1; i < count; i++) {
                    if (alpha[i] < minAlpha) {
                        minAlpha = alpha[i];
                        transIndex = i;
                    }
                }
                if (transIndex > 0) {
                    alpha[transIndex] = alpha[0];
                    alpha[0] = minAlpha;
                    int t = transIndex * 3;
                    byte tempR = rgb[0], tempG = rgb[1], tempB = rgb[2];
                    rgb[0] = rgb[t];
                    rgb[1] = rgb[t + 1];
                    rgb[2] = rgb[t + 2];
                    rgb[t] = tempR;
                    rgb[t + 1] = tempG;
                    rgb[t + 2] = tempB;
                }
            }

            return (rgb, alpha, count);
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