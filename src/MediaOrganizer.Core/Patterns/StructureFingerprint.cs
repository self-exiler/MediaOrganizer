namespace MediaOrganizer.Core.Patterns;

/// <summary>结构指纹：文件名字符分类为 D(数字)/L(字母)/S(符号)，用于排序聚类（SRS FR-3.4）。</summary>
public static class StructureFingerprint
{
    public static string Compute(string fileName)
        => string.Concat(fileName.Select(Classify));

    private static char Classify(char c) =>
        c is >= '0' and <= '9' ? 'D' :
        char.IsLetter(c) ? 'L' : 'S';
}
