using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SkiaSharp;

namespace Redungeon.Tools.AtlasDecoder;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("==========================================================");
        Console.WriteLine("  Redungeon Atlas Decoder & Sprite Splitter (SkiaSharp)   ");
        Console.WriteLine("==========================================================\n");

        string defaultBaseDir = FindProjectRoot();
        string xnbPath = Path.Combine(defaultBaseDir, "RedungeonPC_ResolutionManager", "Content", "Images", "atlas.xnb");
        string jsonPath = Path.Combine(defaultBaseDir, "RedungeonPC_ResolutionManager", "Content", "Images", "atlas.json");
        string outputDir = Path.Combine(defaultBaseDir, "extracted_sprites");
        string mode = "full"; // "full", "trimmed", "both"
        bool exportAtlasSheet = true;

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--xnb" || args[i] == "-x") && i + 1 < args.Length)
                xnbPath = Path.GetFullPath(args[++i]);
            else if ((args[i] == "--json" || args[i] == "-j") && i + 1 < args.Length)
                jsonPath = Path.GetFullPath(args[++i]);
            else if ((args[i] == "--out" || args[i] == "-o") && i + 1 < args.Length)
                outputDir = Path.GetFullPath(args[++i]);
            else if ((args[i] == "--mode" || args[i] == "-m") && i + 1 < args.Length)
                mode = args[++i].ToLowerInvariant();
            else if (args[i] == "--no-sheet")
                exportAtlasSheet = false;
            else if (args[i] == "--help" || args[i] == "-h")
            {
                PrintHelp();
                return 0;
            }
        }

        if (!File.Exists(xnbPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] XNB file not found: {xnbPath}");
            Console.ResetColor();
            return 1;
        }

        if (!File.Exists(jsonPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] JSON map file not found: {jsonPath}");
            Console.ResetColor();
            return 1;
        }

        Directory.CreateDirectory(outputDir);

        Console.WriteLine($"Input XNB : {xnbPath}");
        Console.WriteLine($"Input JSON: {jsonPath}");
        Console.WriteLine($"Output Dir: {outputDir}");
        Console.WriteLine($"Mode      : {mode} (trimmed bounding-box or canvas-restored full frame)");
        Console.WriteLine();

        var stopwatch = Stopwatch.StartNew();

        // 1. Decode XNB texture
        Console.WriteLine("[1/3] Decoding MonoGame XNB Texture2D...");
        SKBitmap atlasBitmap;
        try
        {
            atlasBitmap = DecodeXnbTexture(xnbPath);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"      ✓ Atlas decoded successfully: {atlasBitmap.Width}x{atlasBitmap.Height} ({atlasBitmap.ColorType})");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Failed to decode XNB: {ex.Message}");
            Console.ResetColor();
            return 2;
        }

        // Optional: Export full decoded atlas PNG
        if (exportAtlasSheet)
        {
            string atlasPngPath = Path.Combine(outputDir, "atlas.png");
            Console.WriteLine($"      Writing full decoded spritesheet to '{Path.GetFileName(atlasPngPath)}'...");
            using var image = SKImage.FromBitmap(atlasBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(atlasPngPath);
            data.SaveTo(stream);
        }

        // 2. Parse atlas.json
        Console.WriteLine("\n[2/3] Parsing sprite mappings from atlas.json...");
        JsonDocument doc;
        try
        {
            string jsonContent = File.ReadAllText(jsonPath);
            doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Failed to parse JSON: {ex.Message}");
            Console.ResetColor();
            return 3;
        }

        var root = doc.RootElement;
        if (!root.TryGetProperty("sprites", out var spritesElement) || spritesElement.ValueKind != JsonValueKind.Array)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] 'sprites' array not found in atlas.json.");
            Console.ResetColor();
            return 4;
        }

        int totalSprites = spritesElement.GetArrayLength();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"      ✓ Found {totalSprites} sprite entries to extract.");
        Console.ResetColor();

        // 3. Extract and split sprites
        Console.WriteLine("\n[3/3] Extracting and exporting sprites...");

        string fullSubdir = (mode == "both") ? Path.Combine(outputDir, "full") : outputDir;
        string trimmedSubdir = (mode == "both") ? Path.Combine(outputDir, "trimmed") : outputDir;

        if (mode == "both")
        {
            Directory.CreateDirectory(fullSubdir);
            Directory.CreateDirectory(trimmedSubdir);
        }

        int extracted = 0;
        int failed = 0;
        int lastPercent = -1;

        foreach (var sprite in spritesElement.EnumerateArray())
        {
            string name = sprite.GetProperty("name").GetString() ?? $"sprite_{extracted}";
            int x = ParseInt(sprite, "x");
            int y = ParseInt(sprite, "y");
            int width = ParseInt(sprite, "width");
            int height = ParseInt(sprite, "height");
            int fullWidth = ParseInt(sprite, "full-width", width);
            int fullHeight = ParseInt(sprite, "full-height", height);
            int offXL = ParseInt(sprite, "off-x-l", 0);
            int offYT = ParseInt(sprite, "off-y-t", 0);

            if (width <= 0 || height <= 0)
            {
                failed++;
                continue;
            }

            // Ensure coordinates are within atlas bounds
            int clampedWidth = Math.Min(width, atlasBitmap.Width - x);
            int clampedHeight = Math.Min(height, atlasBitmap.Height - y);

            if (clampedWidth <= 0 || clampedHeight <= 0)
            {
                failed++;
                continue;
            }

            var srcRect = SKRectI.Create(x, y, clampedWidth, clampedHeight);

            // Trimmed mode
            if (mode == "trimmed" || mode == "both")
            {
                string targetPath = Path.Combine(trimmedSubdir, $"{name}.png");
                ExportTrimmedSprite(atlasBitmap, srcRect, targetPath);
            }

            // Full canvas mode (with restored transparent margins for animations)
            if (mode == "full" || mode == "both")
            {
                string targetPath = Path.Combine(fullSubdir, $"{name}.png");
                ExportRestoredCanvasSprite(atlasBitmap, srcRect, fullWidth, fullHeight, offXL, offYT, targetPath);
            }

            extracted++;

            int percent = (extracted * 100) / totalSprites;
            if (percent != lastPercent && percent % 5 == 0)
            {
                lastPercent = percent;
                Console.Write($"\r      Progress: {percent}% ({extracted}/{totalSprites})");
            }
        }

        stopwatch.Stop();
        Console.WriteLine($"\r      Progress: 100% ({extracted}/{totalSprites})               \n");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Successfully extracted {extracted} sprites in {stopwatch.ElapsedMilliseconds} ms!");
        if (failed > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"! Skipped {failed} invalid/zero-sized sprites.");
        }
        Console.ResetColor();

        Console.WriteLine($"Output directory: {outputDir}");
        return 0;
    }

    private static void ExportTrimmedSprite(SKBitmap atlas, SKRectI srcRect, string outputPath)
    {
        var info = new SKImageInfo(srcRect.Width, srcRect.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var spriteBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(spriteBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(atlas, srcRect, SKRectI.Create(0, 0, srcRect.Width, srcRect.Height));
        }

        using var image = SKImage.FromBitmap(spriteBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write);
        data.SaveTo(stream);
    }

    private static void ExportRestoredCanvasSprite(
        SKBitmap atlas,
        SKRectI srcRect,
        int fullWidth,
        int fullHeight,
        int offXL,
        int offYT,
        string outputPath)
    {
        int targetW = Math.Max(fullWidth, srcRect.Width);
        int targetH = Math.Max(fullHeight, srcRect.Height);

        var info = new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var spriteBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(spriteBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            var destRect = SKRectI.Create(offXL, offYT, srcRect.Width, srcRect.Height);
            canvas.DrawBitmap(atlas, srcRect, destRect);
        }

        using var image = SKImage.FromBitmap(spriteBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write);
        data.SaveTo(stream);
    }

    private static SKBitmap DecodeXnbTexture(string xnbPath)
    {
        using var fileStream = File.OpenRead(xnbPath);
        using var reader = new BinaryReader(fileStream);

        // 1. Header Validation
        char[] magic = reader.ReadChars(3);
        if (magic[0] != 'X' || magic[1] != 'N' || magic[2] != 'B')
            throw new InvalidDataException("Not a valid XNA/MonoGame XNB file (missing 'XNB' magic signature).");

        char platform = reader.ReadChar(); // 'w', 'm', 'a', etc.
        byte version = reader.ReadByte();   // 4 or 5
        byte flags = reader.ReadByte();

        bool isCompressed = (flags & 0x80) != 0;
        if (isCompressed)
            throw new NotSupportedException("Compressed XNB (LZX/LZ4) decoding is not required for Redungeon uncompressed assets.");

        uint fileSize = reader.ReadUInt32();

        // 2. Type Readers
        int readerCount = reader.Read7BitEncodedInt();
        string[] readerTypeNames = new string[readerCount];
        for (int i = 0; i < readerCount; i++)
        {
            readerTypeNames[i] = reader.ReadString();
            int readerVersion = reader.ReadInt32();
        }

        // 3. Shared Resources
        int sharedResourceCount = reader.Read7BitEncodedInt();

        // 4. Primary Object
        int primaryTypeIndex = reader.Read7BitEncodedInt();
        if (primaryTypeIndex == 0)
            throw new InvalidDataException("XNB does not contain a primary object (null reference).");

        string typeName = readerTypeNames[primaryTypeIndex - 1];
        if (!typeName.Contains("Texture2DReader"))
            throw new InvalidDataException($"Expected Texture2DReader, but found: {typeName}");

        // Texture2D binary layout:
        int surfaceFormat = reader.ReadInt32(); // 0 = SurfaceFormat.Color (RGBA8888)
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        int mipCount = reader.ReadInt32();

        if (surfaceFormat != 0)
            throw new NotSupportedException($"SurfaceFormat '{surfaceFormat}' is not standard RGBA (Color). Only SurfaceFormat.Color (0) is currently supported.");

        int dataSize = reader.ReadInt32();
        byte[] pixelBytes = reader.ReadBytes(dataSize);

        if (pixelBytes.Length != width * height * 4)
            throw new InvalidDataException($"Pixel data size mismatch. Expected {width * height * 4} bytes, got {pixelBytes.Length} bytes.");

        // Create SkiaSharp bitmap and copy RGBA pixel buffer
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        IntPtr pixelsPtr = bitmap.GetPixels();
        Marshal.Copy(pixelBytes, 0, pixelsPtr, pixelBytes.Length);

        return bitmap;
    }

    private static int ParseInt(JsonElement element, string propertyName, int defaultValue = 0)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;

        if (prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();

        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out int result))
            return result;

        return defaultValue;
    }

    private static string FindProjectRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "RedungeonPC_ResolutionManager")))
                return current;
            current = Directory.GetParent(current)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: dotnet run --project tools/AtlasDecoder [OPTIONS]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -x, --xnb <path>       Path to input atlas.xnb file.");
        Console.WriteLine("  -j, --json <path>      Path to input atlas.json map file.");
        Console.WriteLine("  -o, --out <path>       Destination folder for extracted sprites.");
        Console.WriteLine("  -m, --mode <mode>      Extraction mode: 'full' (default), 'trimmed', or 'both'.");
        Console.WriteLine("                         'full': restores original transparent canvas & offsets.");
        Console.WriteLine("                         'trimmed': extracts tight pixel bounding-box only.");
        Console.WriteLine("      --no-sheet         Do not export the full 'atlas.png' texture sheet.");
        Console.WriteLine("  -h, --help             Show this help screen.");
    }
}

