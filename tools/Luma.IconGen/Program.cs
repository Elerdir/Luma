using Luma.IconGen;

// Regenerates the Luma app icons from the vector definition in IconRenderer.
// Run from the repo root:  dotnet run --project tools/Luma.IconGen
var assets = args.Length > 0
    ? args[0]
    : Path.Combine("src", "Luma.Presentation", "Assets");

Directory.CreateDirectory(assets);

int[] icoSizes = [16, 24, 32, 48, 64, 128, 256];
var frames = icoSizes
    .Select(size => (Size: size, Png: ImageWriter.EncodePng(IconRenderer.Render(size), size, size)))
    .ToList();

var icoPath = Path.Combine(assets, "luma.ico");
File.WriteAllBytes(icoPath, ImageWriter.EncodeIco(frames));
Console.WriteLine($"{icoPath}  ({icoSizes.Length} frames)");

// A standalone 256px PNG is what Avalonia's Window.Icon loads at runtime.
var pngPath = Path.Combine(assets, "luma.png");
File.WriteAllBytes(pngPath, frames.Single(f => f.Size == 256).Png);
Console.WriteLine($"{pngPath}");

// macOS .icns needs sizes up to 1024. The release workflow downscales this one with
// sips rather than upscaling the 256px file, which would come out soft.
var largePath = Path.Combine(assets, "luma-1024.png");
File.WriteAllBytes(largePath, ImageWriter.EncodePng(IconRenderer.Render(1024), 1024, 1024));
Console.WriteLine($"{largePath}");
