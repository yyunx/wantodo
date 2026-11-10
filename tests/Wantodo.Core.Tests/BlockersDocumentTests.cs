using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wantodo.Core;

namespace Wantodo.Core.Tests;

public class BlockersDocumentTests
{
    [Fact]
    public void Parse_Empty_Text_Gives_Empty_Document()
    {
        var doc = BlockersDocument.Parse(null);

        Assert.Empty(doc.Rows);
    }

    [Fact]
    public void Round_Trip_Keeps_Rows_And_Order()
    {
        var doc = new BlockersDocument();
        doc.Set("复现 StepFinder", "数据集标注不全，要先补标");
        doc.Set("写开题报告", "第三章的实验还没跑完");

        string markdown = doc.ToMarkdown();
        var back = BlockersDocument.Parse(markdown);

        Assert.Equal(2, back.Rows.Count);
        Assert.Equal("复现 StepFinder", back.Rows[0].Want);
        Assert.Equal("数据集标注不全，要先补标", back.Rows[0].Blocker);
        Assert.Equal("写开题报告", back.Rows[1].Want);
    }

    [Fact]
    public void Parse_Ignores_Text_Before_Table_Header()
    {
        const string md = """
            # 随便写点什么

            这是说明文字，不该变成一行。

            | 想做 | 卡在什么地方 |
            | --- | --- |
            | 学会 C# 源生成器 | 文档太少 |
            """;

        var doc = BlockersDocument.Parse(md);

        Assert.Single(doc.Rows);
        Assert.Equal("学会 C# 源生成器", doc.Rows[0].Want);
    }

    [Fact]
    public void Pipes_Inside_Cells_Are_Escaped_And_Restored()
    {
        var doc = new BlockersDocument();
        doc.Set("比较 a|b", "这里 | 那里");

        var back = BlockersDocument.Parse(doc.ToMarkdown());

        Assert.Equal("比较 a|b", back.Rows[0].Want);
        Assert.Equal("这里 | 那里", back.Rows[0].Blocker);
    }

    [Fact]
    public void Set_With_Empty_Blocker_Removes_Row()
    {
        var doc = new BlockersDocument();
        doc.Set("x", "y");
        doc.Set("x", "   ");

        Assert.Empty(doc.Rows);
    }

    [Fact]
    public void Set_Updates_Existing_Row_Instead_Of_Duplicating()
    {
        var doc = new BlockersDocument();
        doc.Set("x", "first");
        doc.Set("  X  ", "second");

        Assert.Single(doc.Rows);
        Assert.Equal("second", doc.Rows[0].Blocker);
    }

    [Fact]
    public void Find_Is_Case_And_Whitespace_Insensitive()
    {
        var doc = new BlockersDocument();
        doc.Set("Setup Steps", "缺少依赖说明");

        Assert.NotNull(doc.Find(" setup steps "));
    }

    [Fact]
    public void Store_Writes_Markdown_File_With_Utf8_No_Bom()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wantodo-blockers-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new BlockersStore(dir);
            var doc = new BlockersDocument();
            doc.Set("做个小实验", "显卡被占着");
            store.Save(doc);

            Assert.True(File.Exists(store.FilePath));
            var bytes = File.ReadAllBytes(store.FilePath);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

            var back = store.Load();
            Assert.Single(back.Rows);
            Assert.Equal("做个小实验", back.Rows[0].Want);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_On_Missing_File_Returns_Empty()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wantodo-blockers-" + Guid.NewGuid().ToString("N")[..8]);
        var store = new BlockersStore(dir);

        Assert.Empty(store.Load().Rows);
    }
}
