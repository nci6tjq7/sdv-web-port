using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SdvWebPort.Vfs.Content;

namespace SdvWebPort.Runtime.Content;

/// <summary>
/// Renders text using a BMFont glyph atlas + SpriteBatch.
/// </summary>
public sealed class BmFontRenderer
{
    private readonly BmFontFile _font;
    private readonly Texture2D[] _atlases;

    public BmFontRenderer(BmFontFile font, params Texture2D[] atlases)
    {
        _font = font;
        _atlases = atlases;
    }

    public float DrawString(SpriteBatch spriteBatch, string text, Vector2 position, Color color)
    {
        float x = position.X;
        float y = position.Y;

        foreach (char c in text)
        {
            var glyph = _font.GetChar(c);
            if (glyph == null) { x += 8; continue; }

            if (glyph.Width > 0 && glyph.Height > 0)
            {
                int pageIdx = Math.Clamp(glyph.Page, 0, _atlases.Length - 1);
                var atlas = _atlases[pageIdx];
                var srcRect = new Rectangle(glyph.X, glyph.Y, glyph.Width, glyph.Height);
                var destPos = new Vector2(x + glyph.XOffset, y + glyph.YOffset);
                spriteBatch.Draw(atlas, destPos, srcRect, color);
            }
            x += glyph.XAdvance;
        }
        return x - position.X;
    }

    public void DrawStringCentered(SpriteBatch spriteBatch, string text, Vector2 center, Color color)
    {
        int width = _font.MeasureString(text);
        var pos = new Vector2(center.X - width / 2f, center.Y - _font.LineHeight / 2f);
        DrawString(spriteBatch, text, pos, color);
    }
}
