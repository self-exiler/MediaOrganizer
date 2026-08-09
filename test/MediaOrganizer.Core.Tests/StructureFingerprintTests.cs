using MediaOrganizer.Core.Patterns;

namespace MediaOrganizer.Core.Tests;

public class StructureFingerprintTests
{
    [Theory]
    [InlineData("IMG_20240115.jpg", "LLLSDDDDDDDDSLLL")]
    [InlineData("mm_export1718012345678.jpg", "LLSLLLLLLDDDDDDDDDDDDDSLLL")]
    [InlineData("scan001.tif", "LLLLDDDSLLL")]
    [InlineData("20240115", "DDDDDDDD")]
    public void 指纹分类正确(string input, string expected)
    {
        Assert.Equal(expected, StructureFingerprint.Compute(input));
    }
}
