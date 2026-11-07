using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

internal static class IconGenerator
{
    private static void Main(string[] args)
    {
        string outIco = args.Length > 0 ? args[0] : "app.ico";
        string outPreview = args.Length > 1 ? args[1] : "app-preview.png";
        int[] sizes = { 256, 128, 64, 48, 32, 16 };
        var pngs = new List<byte[]>();

        foreach (int size in sizes)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    float pad = size * 0.04f;
                    float w = size - pad * 2;
                    float radius = size * 0.22f;
                    using (var path = RoundedRect(new RectangleF(pad, pad, w, w), radius))
                    {
                        using (var brush = new LinearGradientBrush(
                                   new PointF(pad, pad), new PointF(pad + w, pad + w),
                                   Color.FromArgb(91, 73, 214), Color.FromArgb(151, 112, 246)))
                        {
                            g.FillPath(brush, path);
                        }
                    }

                    float penWidth = Math.Max(1.8f, size * 0.028f);
                    using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), penWidth))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        float m = size * 0.20f;
                        float l = size * 0.12f;
                        g.DrawLine(pen, m, m + l, m, m);
                        g.DrawLine(pen, m, m, m + l, m);
                        g.DrawLine(pen, size - m - l, m, size - m, m);
                        g.DrawLine(pen, size - m, m, size - m, m + l);
                        g.DrawLine(pen, size - m, size - m - l, size - m, size - m);
                        g.DrawLine(pen, size - m, size - m, size - m - l, size - m);
                        g.DrawLine(pen, m + l, size - m, m, size - m);
                        g.DrawLine(pen, m, size - m, m, size - m - l);
                    }

                    if (size >= 32)
                    {
                        using (var font = new Font("Microsoft YaHei UI", size * 0.44f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var textBrush = new SolidBrush(Color.White))
                        using (var format = new StringFormat())
                        {
                            format.Alignment = StringAlignment.Center;
                            format.LineAlignment = StringAlignment.Center;
                            g.DrawString("字", font, textBrush, new RectangleF(0, size * 0.005f, size, size * 0.99f), format);
                        }
                    }
                    else
                    {
                        using (var textBrush = new SolidBrush(Color.White))
                        {
                            g.FillEllipse(textBrush, size * 0.36f, size * 0.36f, size * 0.28f, size * 0.28f);
                        }
                    }
                }

                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }

                if (size == 256)
                {
                    bmp.Save(outPreview, ImageFormat.Png);
                }
            }
        }

        using (var fs = File.Create(outIco))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                byte[] data = pngs[i];
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)data.Length);
                bw.Write((uint)offset);
                offset += data.Length;
            }

            foreach (byte[] data in pngs)
            {
                bw.Write(data);
            }
        }

        Console.WriteLine("Wrote " + outIco + " (" + new FileInfo(outIco).Length + " bytes)");
        Console.WriteLine("Wrote " + outPreview);
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
