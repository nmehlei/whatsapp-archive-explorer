namespace ArchiveExplorer.Demo;

/// <summary>Kinds of generated holiday "photos".</summary>
public enum Scene
{
    Sunset,
    Mountains,
    Sea,
    City,
    Forest,
    Night,
    Lighthouse,
}

/// <summary>
/// Paints simple, deterministic illustrations (landscapes, portrait silhouettes) as RGB pixels, so the showcase
/// archive has photos and profile pictures that look like something without shipping any real image.
/// </summary>
internal static class SceneRenderer
{
    private readonly record struct Rgb(double R, double G, double B)
    {
        public static Rgb Hex(int hex) => new((hex >> 16) & 0xFF, (hex >> 8) & 0xFF, hex & 0xFF);
        public Rgb Mix(Rgb o, double t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);
    }

    private sealed class Canvas(int width, int height)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public byte[] Pixels { get; } = new byte[width * height * 3];

        public void Set(int x, int y, Rgb c, double alpha = 1)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return;
            }
            var i = (y * Width + x) * 3;
            Pixels[i] = Blend(Pixels[i], c.R, alpha);
            Pixels[i + 1] = Blend(Pixels[i + 1], c.G, alpha);
            Pixels[i + 2] = Blend(Pixels[i + 2], c.B, alpha);
        }

        private static byte Blend(byte under, double over, double alpha) =>
            (byte)Math.Clamp(Math.Round(under + (over - under) * alpha), 0, 255);

        public void Disc(double cx, double cy, double r, Rgb c, double alpha = 1)
        {
            for (var y = (int)(cy - r - 1); y <= cy + r + 1; y++)
            {
                for (var x = (int)(cx - r - 1); x <= cx + r + 1; x++)
                {
                    var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    var edge = Math.Clamp(r - d + 0.5, 0, 1); // 1 px soft edge
                    if (edge > 0)
                    {
                        Set(x, y, c, alpha * edge);
                    }
                }
            }
        }

        public void Rect(double x0, double y0, double x1, double y1, Rgb c, double alpha = 1)
        {
            for (var y = (int)Math.Floor(y0); y < Math.Ceiling(y1); y++)
            {
                for (var x = (int)Math.Floor(x0); x < Math.Ceiling(x1); x++)
                {
                    Set(x, y, c, alpha);
                }
            }
        }
    }

    public static (int Width, int Height, byte[] Rgb) Landscape(Scene scene, int seed, int width = 512, int height = 288)
    {
        var random = new Random(seed);
        var canvas = new Canvas(width, height);
        var horizon = height * (0.55 + random.NextDouble() * 0.1);

        var (skyTop, skyLow) = scene switch
        {
            Scene.Sunset or Scene.Lighthouse => (Rgb.Hex(0x2B3A67), Rgb.Hex(0xF6A570)),
            Scene.Night => (Rgb.Hex(0x0B1026), Rgb.Hex(0x2E3B66)),
            Scene.City => (Rgb.Hex(0x4A5D8C), Rgb.Hex(0xE8B7A0)),
            Scene.Forest => (Rgb.Hex(0x7FB3D5), Rgb.Hex(0xDCEBF2)),
            _ => (Rgb.Hex(0x4F8FD6), Rgb.Hex(0xCFE6F7)),
        };
        for (var y = 0; y < height; y++)
        {
            var c = skyTop.Mix(skyLow, Math.Min(1, y / horizon));
            for (var x = 0; x < width; x++)
            {
                canvas.Set(x, y, c);
            }
        }

        if (scene == Scene.Night)
        {
            for (var s = 0; s < width / 3; s++)
            {
                canvas.Disc(random.NextDouble() * width, random.NextDouble() * horizon * 0.9, 0.6 + random.NextDouble(), Rgb.Hex(0xFFFFFF), 0.4 + random.NextDouble() * 0.6);
            }
            canvas.Disc(width * 0.78, height * 0.18, height * 0.07, Rgb.Hex(0xF4F1DE));
        }
        else
        {
            var sunX = width * (0.25 + random.NextDouble() * 0.5);
            var sunY = scene is Scene.Sunset or Scene.Lighthouse ? horizon - height * 0.06 : height * 0.2;
            var sun = scene is Scene.Sunset or Scene.Lighthouse ? Rgb.Hex(0xFFD58A) : Rgb.Hex(0xFFF6D5);
            for (var g = 24; g >= 1; g--)
            {
                canvas.Disc(sunX, sunY, height * 0.05 * (1 + g * 0.125), sun, 0.022);
            }
            canvas.Disc(sunX, sunY, height * 0.05, sun);
        }

        switch (scene)
        {
            case Scene.Mountains:
                Ridges(canvas, random, horizon, [0x8DA9C4, 0x5B7A99, 0x34495E], snow: true);
                Ground(canvas, horizon + height * 0.12, Rgb.Hex(0x4E7D4A), Rgb.Hex(0x2F4F2C));
                break;
            case Scene.Forest:
                Ridges(canvas, random, horizon, [0x9FC5B0, 0x6E9E83], snow: false);
                Ground(canvas, horizon + height * 0.05, Rgb.Hex(0x3E6B48), Rgb.Hex(0x1F3B26));
                Trees(canvas, random, horizon + height * 0.05);
                break;
            case Scene.City:
                Ground(canvas, horizon + height * 0.1, Rgb.Hex(0x3B3F55), Rgb.Hex(0x22243A));
                Skyline(canvas, random, horizon + height * 0.12);
                break;
            case Scene.Night:
                Ridges(canvas, random, horizon, [0x1E2A4A, 0x121A33], snow: false);
                Ground(canvas, horizon + height * 0.1, Rgb.Hex(0x0E1428), Rgb.Hex(0x070B18));
                break;
            default: // sea, sunset, lighthouse
                Sea(canvas, random, horizon, scene == Scene.Sea ? Rgb.Hex(0x2E86C1) : Rgb.Hex(0x3E4F7A));
                if (scene == Scene.Lighthouse)
                {
                    Lighthouse(canvas, width * 0.72, horizon, height);
                }
                break;
        }
        return (width, height, canvas.Pixels);
    }

    /// <summary>A head-and-shoulders silhouette on a soft background: a stand-in profile picture.</summary>
    public static (int Width, int Height, byte[] Rgb) Portrait(int seed, int size = 192)
    {
        var random = new Random(seed);
        var canvas = new Canvas(size, size);
        int[] backgrounds = [0xF4D6CC, 0xCDE7E1, 0xD9DCF5, 0xF6E7B9, 0xE3D3EE, 0xCFE3F6];
        int[] skins = [0xF1C7A5, 0xD9A27C, 0xB7795A, 0x8D5A3B, 0xF5D7BF];
        int[] hairs = [0x2E2018, 0x5B3A22, 0xA66B32, 0xD9B26A, 0x1C1C1C, 0x7A7A7A];
        int[] shirts = [0x2F6F8F, 0xC0504D, 0x4E8A5B, 0x6B4FA0, 0xE0A030, 0x34495E];
        var bg = Rgb.Hex(backgrounds[random.Next(backgrounds.Length)]);
        for (var y = 0; y < size; y++)
        {
            var c = bg.Mix(Rgb.Hex(0xFFFFFF), 0.35 * (1 - y / (double)size));
            for (var x = 0; x < size; x++)
            {
                canvas.Set(x, y, c);
            }
        }
        var skin = Rgb.Hex(skins[random.Next(skins.Length)]);
        var hair = Rgb.Hex(hairs[random.Next(hairs.Length)]);
        var shirt = Rgb.Hex(shirts[random.Next(shirts.Length)]);
        var cx = size / 2.0 + (random.NextDouble() - 0.5) * size * 0.06;
        // shoulders: a wide ellipse cut by the bottom edge
        for (var y = (int)(size * 0.68); y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = (x - cx) / (size * 0.42);
                var dy = (y - size * 1.02) / (size * 0.36);
                if (dx * dx + dy * dy <= 1)
                {
                    canvas.Set(x, y, shirt);
                }
            }
        }
        canvas.Rect(cx - size * 0.07, size * 0.56, cx + size * 0.07, size * 0.72, skin.Mix(Rgb.Hex(0x000000), 0.08));
        var longHair = random.Next(2) == 0;
        if (longHair)
        {
            canvas.Rect(cx - size * 0.2, size * 0.36, cx + size * 0.2, size * 0.66, hair);
        }
        canvas.Disc(cx, size * 0.42, size * 0.2, skin);
        // hair cap: the top part of a slightly larger disc
        for (var y = (int)(size * 0.2); y < size * 0.36; y++)
        {
            for (var x = (int)(cx - size * 0.23); x < cx + size * 0.23; x++)
            {
                var d = Math.Sqrt((x - cx) * (x - cx) + (y - size * 0.4) * (y - size * 0.4));
                if (d <= size * 0.215)
                {
                    canvas.Set(x, y, hair);
                }
            }
        }
        return (size, size, canvas.Pixels);
    }

    private static void Ridges(Canvas canvas, Random random, double horizon, int[] colors, bool snow)
    {
        for (var layer = 0; layer < colors.Length; layer++)
        {
            var color = Rgb.Hex(colors[layer]);
            var baseY = horizon - canvas.Height * (0.22 - layer * 0.06);
            var (p1, p2, p3) = (random.NextDouble() * 6, random.NextDouble() * 6, random.NextDouble() * 6);
            var amp = canvas.Height * (0.12 - layer * 0.025);
            for (var x = 0; x < canvas.Width; x++)
            {
                var u = x / (double)canvas.Width;
                var top = baseY - amp * (0.6 * Math.Sin(u * 5.3 + p1) + 0.3 * Math.Sin(u * 11.7 + p2) + 0.15 * Math.Sin(u * 23.1 + p3));
                for (var y = (int)top; y < canvas.Height; y++)
                {
                    // snow caps only the peaks: the higher the ridge, the deeper the snow
                    var cap = (baseY - amp * 0.35 - top) * 0.6;
                    var c = snow && layer == 0 && y < top + cap ? Rgb.Hex(0xF4F7FA) : color;
                    canvas.Set(x, y, c);
                }
            }
        }
    }

    private static void Ground(Canvas canvas, double top, Rgb near, Rgb far)
    {
        for (var y = (int)top; y < canvas.Height; y++)
        {
            var c = near.Mix(far, (y - top) / Math.Max(1, canvas.Height - top));
            for (var x = 0; x < canvas.Width; x++)
            {
                canvas.Set(x, y, c);
            }
        }
    }

    private static void Sea(Canvas canvas, Random random, double horizon, Rgb water)
    {
        for (var y = (int)horizon; y < canvas.Height; y++)
        {
            var c = water.Mix(Rgb.Hex(0x0E2238), (y - horizon) / (canvas.Height - horizon) * 0.7);
            for (var x = 0; x < canvas.Width; x++)
            {
                canvas.Set(x, y, c);
            }
        }
        for (var s = 0; s < canvas.Width / 4; s++)
        {
            var y = horizon + Math.Pow(random.NextDouble(), 1.6) * (canvas.Height - horizon);
            var x = random.NextDouble() * canvas.Width;
            var len = 4 + random.NextDouble() * 18;
            canvas.Rect(x, y, x + len, y + 1.2, Rgb.Hex(0xFFE2B8), 0.35);
        }
    }

    private static void Lighthouse(Canvas canvas, double x, double horizon, int height)
    {
        // a headland rising from the sea on the left, running off the right edge
        var cliff = Rgb.Hex(0x2A2F45);
        var plateau = horizon - height * 0.07;
        var foot = x - canvas.Width * 0.3;
        for (var px = (int)foot; px < canvas.Width; px++)
        {
            var t = Math.Clamp((px - foot) / (x - canvas.Width * 0.06 - foot), 0, 1);
            var top = horizon + (plateau - horizon) * (1 - (1 - t) * (1 - t)) + (1 - t) * height * 0.02;
            for (var y = (int)top; y < canvas.Height; y++)
            {
                canvas.Set(px, y, cliff);
            }
        }
        var baseY = horizon - height * 0.07;
        var towerH = height * 0.3;
        for (var y = (int)(baseY - towerH); y < baseY; y++)
        {
            var t = (baseY - y) / towerH;
            var half = height * (0.035 - 0.012 * t);
            var stripe = ((int)(t * 5)) % 2 == 0 ? Rgb.Hex(0xF2F2F2) : Rgb.Hex(0xC0392B);
            canvas.Rect(x - half, y, x + half, y + 1, stripe);
        }
        var lampY = baseY - towerH - height * 0.025;
        canvas.Rect(x - height * 0.03, lampY + height * 0.025, x + height * 0.03, lampY + height * 0.035, Rgb.Hex(0x222222));
        for (var g = 32; g >= 1; g--)
        {
            canvas.Disc(x, lampY, height * 0.0045 * g, Rgb.Hex(0xFFF2B0), 0.02);
        }
        canvas.Disc(x, lampY, height * 0.02, Rgb.Hex(0xFFF7D6));
    }

    private static void Trees(Canvas canvas, Random random, double groundY)
    {
        var count = canvas.Width / 14;
        for (var i = 0; i < count; i++)
        {
            var x = random.NextDouble() * canvas.Width;
            var depth = random.NextDouble();
            var baseY = groundY + depth * (canvas.Height - groundY) * 0.8;
            var h = canvas.Height * (0.12 + depth * 0.18);
            var color = Rgb.Hex(0x1E4D2B).Mix(Rgb.Hex(0x0F2A17), depth);
            for (var y = (int)(baseY - h); y < baseY; y++)
            {
                var half = (y - (baseY - h)) / h * h * 0.28;
                canvas.Rect(x - half, y, x + half, y + 1, color);
            }
        }
    }

    private static void Skyline(Canvas canvas, Random random, double groundY)
    {
        var x = 0.0;
        while (x < canvas.Width)
        {
            var w = canvas.Width * (0.05 + random.NextDouble() * 0.08);
            var h = canvas.Height * (0.12 + random.NextDouble() * 0.3);
            var body = Rgb.Hex(0x2C3048).Mix(Rgb.Hex(0x454B6E), random.NextDouble());
            canvas.Rect(x, groundY - h, x + w - 2, groundY, body);
            for (var wy = groundY - h + 6; wy < groundY - 6; wy += 9)
            {
                for (var wx = x + 4; wx < x + w - 8; wx += 8)
                {
                    if (random.Next(3) > 0)
                    {
                        canvas.Rect(wx, wy, wx + 3, wy + 4, Rgb.Hex(0xFFD98A), 0.85);
                    }
                }
            }
            x += w;
        }
    }
}
