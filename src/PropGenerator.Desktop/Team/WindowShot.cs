using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PropGenerator.Desktop.Team;

/// <summary>
/// A picture of the whole window as it is now, for a suggestion: the window's controls drawn to a bitmap, with the 3D
/// view's own last frame in its place (drawn off screen, an OpenGL view is black). The frame goes in as a picture laid
/// just over the view for this one render, so the controls that float over the view stay on top of it.
/// </summary>
internal static class WindowShot
{
    /// <summary>Takes the picture, on the UI thread.</summary>
    /// <param name="window">The window.</param>
    /// <param name="view">The 3D view in it (a child of a panel), or null.</param>
    /// <param name="frame">Reads the 3D view's frame (RGBA, top row first), or null when there is no view to read.</param>
    /// <returns>A PNG, or null when the window has no size.</returns>
    internal static async Task<byte[]?> TakeAsync(Window window, Control? view, Func<Task<(byte[] Rgba, int Width, int Height)>>? frame)
    {
        ArgumentNullException.ThrowIfNull(window);
        double scale = window.RenderScaling;
        int w = (int)Math.Round(window.Bounds.Width * scale);
        int h = (int)Math.Round(window.Bounds.Height * scale);
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        WriteableBitmap? picture = null;
        if (view?.Parent is Panel && frame is not null)
        {
            try
            {
                (byte[] f, int fw, int fh) = await frame();
                for (int i = 3; i < f.Length; i += 4)
                {
                    f[i] = 255;  // the framebuffer's alpha is not the picture's
                }

                picture = Bitmap(f, fw, fh);
            }
            catch (InvalidOperationException)
            {
                // No 3D view running, or it drew no frame: the picture keeps the window without it.
            }
        }

        byte[] rgba = new byte[w * h * 4];
        using (picture)
        using (var bitmap = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96 * scale, 96 * scale)))
        {
            Image? over = null;
            if (picture is not null && view!.Parent is Panel panel)
            {
                over = new Image { Source = picture, Stretch = Avalonia.Media.Stretch.Fill, IsHitTestVisible = false };
                Grid.SetRow(over, Grid.GetRow(view));
                Grid.SetColumn(over, Grid.GetColumn(view));
                Grid.SetRowSpan(over, Grid.GetRowSpan(view));
                Grid.SetColumnSpan(over, Grid.GetColumnSpan(view));
                panel.Children.Insert(panel.Children.IndexOf(view) + 1, over);
                over.Measure(view.Bounds.Size);
                over.Arrange(view.Bounds);
            }

            try
            {
                bitmap.Render(window);
            }
            finally
            {
                if (over is not null)
                {
                    ((Panel)view!.Parent!).Children.Remove(over);
                }
            }

            var pin = GCHandle.Alloc(rgba, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, w, h), pin.AddrOfPinnedObject(), rgba.Length, w * 4);
            }
            finally
            {
                pin.Free();
            }
        }

        // ponytail: assumes the bitmap's Bgra8888 (Avalonia's default on every platform we ship); to opaque RGBA.
        for (int i = 0; i < rgba.Length; i += 4)
        {
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
            rgba[i + 3] = 255;
        }

        return await Task.Run(() => PngEncoder.Encode(rgba, w, h));
    }

    /// <summary>A bitmap of RGBA pixels, top row first.</summary>
    /// <param name="rgba">The pixels.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The bitmap.</returns>
    internal static WriteableBitmap Bitmap(byte[] rgba, int width, int height)
    {
        var b = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer fb = b.Lock();
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(rgba, y * width * 4, fb.Address + (y * fb.RowBytes), width * 4);
        }

        return b;
    }
}
