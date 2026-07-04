using SdvWebPort.Vfs.Content;
using Xunit;

namespace SdvWebPort.Content.Tests;

public class BmFontFileTests
{
    private const string SampleFnt = """
info face="Arial" size=32 bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=1 aa=1 padding=0,0,0,0 spacing=1,1 outline=0
common lineHeight=32 base=26 scaleW=256 scaleH=256 pages=1 packed=0
page id=0 file="font_0.png"
char id=32 x=0 y=0 width=0 height=0 xoffset=0 yoffset=0 xadvance=8 page=0 chnl=15
char id=65 x=0 y=0 width=15 height=18 xoffset=0 yoffset=0 xadvance=16 page=0 chnl=15
char id=66 x=16 y=0 width=14 height=18 xoffset=0 yoffset=0 xadvance=15 page=0 chnl=15
""";

    [Fact]
    public void Parse_CommonLine_SetsLineHeightAndBase()
    {
        var font = BmFontFile.Parse(SampleFnt);
        Assert.Equal(32, font.LineHeight);
        Assert.Equal(26, font.BaseHeight);
        Assert.Equal(256, font.ScaleWidth);
        Assert.Equal(256, font.ScaleHeight);
    }

    [Fact]
    public void Parse_PageLine_AddsPage()
    {
        var font = BmFontFile.Parse(SampleFnt);
        Assert.Single(font.Pages);
        Assert.Equal(0, font.Pages[0].Id);
        Assert.Equal("font_0.png", font.Pages[0].File);
    }

    [Fact]
    public void Parse_CharLines_AddsCharacters()
    {
        var font = BmFontFile.Parse(SampleFnt);
        Assert.Equal(3, font.Characters.Count);
        Assert.True(font.Characters.ContainsKey(32));  // space
        Assert.True(font.Characters.ContainsKey(65));  // 'A'
        Assert.True(font.Characters.ContainsKey(66));  // 'B'
    }

    [Fact]
    public void Parse_CharA_HasCorrectGlyphRect()
    {
        var font = BmFontFile.Parse(SampleFnt);
        var a = font.GetChar(65);
        Assert.NotNull(a);
        Assert.Equal(0, a!.X);
        Assert.Equal(0, a.Y);
        Assert.Equal(15, a.Width);
        Assert.Equal(18, a.Height);
        Assert.Equal(16, a.XAdvance);
    }

    [Fact]
    public void MeasureString_AB_ReturnsSumOfXAdvance()
    {
        var font = BmFontFile.Parse(SampleFnt);
        // A=16 + B=15 = 31
        Assert.Equal(31, font.MeasureString("AB"));
    }

    [Fact]
    public void MeasureString_WithSpace_IncludesSpaceAdvance()
    {
        var font = BmFontFile.Parse(SampleFnt);
        // A=16 + space=8 + B=15 = 39
        Assert.Equal(39, font.MeasureString("A B"));
    }

    [Fact]
    public void GetChar_MissingChar_ReturnsNull()
    {
        var font = BmFontFile.Parse(SampleFnt);
        Assert.Null(font.GetChar(9999));
    }
}
