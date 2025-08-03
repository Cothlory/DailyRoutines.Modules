using DailyRoutines.Abstracts;

namespace DailyRoutines.ModulesPublic;

public class TestModule : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title       = "测试模块",
        Description = "简单的测试模块",
        Category    = ModuleCategories.General,
        Author      = ["Cothlory"]
    };

    protected override void Init()
    {
        NotificationInfo("测试模块已启动！");
    }

    protected override void Uninit()
    {
        NotificationInfo("测试模块已停止！");
        base.Uninit();
    }

    protected override void ConfigUI()
    {
        ImGui.Text("这是一个测试模块");
        if (ImGui.Button("测试按钮"))
            NotificationInfo("按钮被点击了！");
    }
}
