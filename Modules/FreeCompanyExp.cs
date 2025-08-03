using System;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.ModulesPublic;

public unsafe class FreeCompanyExp : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "更精确的部队经验",
        Description = "显示部队当前经验和升级所需经验，并在获得经验时进行提醒",
        Category = ModuleCategories.General,
        Author = ["Cothlory"]
    };

    private Config ModuleConfig = null!;
    private uint LastFCExp;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        // 注册框架更新事件，每30秒检查一次
        FrameworkManager.Register(OnUpdate, throttleMS: 30_000);
        DService.Condition.ConditionChange += OnConditionChanged;
        Overlay ??= new(this);
        Overlay.IsOpen = ModuleConfig.ShowOverlay;
        UpdateFCExp();
    }

    protected override void Uninit()
    {
        FrameworkManager.Unregister(OnUpdate);
        DService.Condition.ConditionChange -= OnConditionChanged;
        
        base.Uninit();
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (!DService.ClientState.IsLoggedIn) return;
        TryRefreshFCData();
        if (ModuleConfig.IsEnabled)
            CheckFCExpGain();
    }

    private void OnConditionChanged(ConditionFlag flag, bool value)
    {
        // 当完成任务、副本等活动时检查FC经验变化（仅在启用模块时）
        if (ModuleConfig.IsEnabled && flag == ConditionFlag.BoundByDuty && !value)
            CheckFCExpGain();
    }

    private void CheckFCExpGain()
    {
        var (currentExp, maxExp, level) = GetCurrentFCData();
        if (currentExp > LastFCExp)
        {
            var gained = currentExp - LastFCExp;
            if (gained > ModuleConfig.MinExpGainThreshold)
                NotificationInfo($"获得部队经验: {gained:N0}");
        }
        LastFCExp = currentExp;
    }

    private (uint currentExp, uint maxExp, byte level) GetCurrentFCData()
    {
        try
        {
            var stage = AtkStage.Instance();
            if (stage == null) return (0, 0, 0);

            var fcExchangeArray = stage->GetNumberArrayData(NumberArrayType.FreeCompanyExchange);
            if (fcExchangeArray == null || fcExchangeArray->AtkArrayData.Size < 10)
                return (0, 0, 0);
            var currentExp = (uint)fcExchangeArray->IntArray[6];  // 当前经验
            var maxExp = (uint)fcExchangeArray->IntArray[7];      // 当前等级总经验
            var level = (byte)fcExchangeArray->IntArray[4];       // 部队等级
            var credits = (uint)fcExchangeArray->IntArray[9];     // 部队战绩

            return (currentExp, maxExp, level);
        }
        catch (Exception ex)
        {
            DService.Log.Error($"获取FC数据失败: {ex.Message}");
            return (0, 0, 0);
        }
    }

    private void UpdateFCExp()
    {
        var (currentExp, _, _) = GetCurrentFCData();
        LastFCExp = currentExp;
    }

    private void TryRefreshFCData()
    {
        try
        {
            var infoModule = InfoModule.Instance();
            if (infoModule == null) return;

            var fcInfoProxy = infoModule->GetInfoProxyById(InfoProxyId.FreeCompany);
            if (fcInfoProxy == null) return;
            var localPlayer = DService.ClientState.LocalPlayer;
            if (localPlayer != null && localPlayer.EntityId != 0)
            {
                var fcProxy = (InfoProxyFreeCompany*)fcInfoProxy;
                fcProxy->RequestDataForCharacter(localPlayer.EntityId);
            }
        }
        catch (Exception ex)
        {
            DService.Log.Debug($"刷新部队数据失败: {ex.Message}");
        }
    }

    protected override void ConfigUI()
    {
        if (ImGui.Checkbox("显示悬浮窗口", ref ModuleConfig.ShowOverlay))
        {
            Overlay.IsOpen = ModuleConfig.ShowOverlay;
            SaveConfig(ModuleConfig);
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("显示一个悬浮窗口实时显示部队经验信息");
        if (ImGui.Checkbox("启用经验变化通知", ref ModuleConfig.IsEnabled))
            SaveConfig(ModuleConfig);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("开启后会监控FC经验变化并发送通知\n关闭后仍可查看FC数据，但不会发送经验获得通知");
        ImGui.BeginDisabled(!ModuleConfig.IsEnabled);
        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        if (ImGui.DragInt("通知阈值", ref ModuleConfig.MinExpGainThreshold, 10f, 1, 10000))
            SaveConfig(ModuleConfig);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("只有经验变化量超过此数值时才会发送通知");
        ImGui.EndDisabled();

        ImGui.Separator();
        
        var (currentExp, maxExp, level) = GetCurrentFCData();
        if (currentExp > 0 || maxExp > 0)
        {
            ImGui.Text($"当前等级: {level}");
            ImGui.Text($"当前经验: {currentExp:N0} / {maxExp:N0}");
            
            if (maxExp > 0)
            {
                var progress = (float)currentExp / maxExp;
                ImGui.ProgressBar(progress, new(300f * GlobalFontScale, 20f * GlobalFontScale), 
                    $"{progress:P1}");
            }
            
            if (maxExp > currentExp)
            {
                var remaining = maxExp - currentExp;
                ImGui.Text($"升级还需: {remaining:N0} 经验");
            }
        }
        else
        {
            ImGui.TextColored(new(1f, 0.5f, 0.5f, 1f), "未检测到有效的部队数据");
            ImGui.Text("请确保已加入部队");
        }
    }

    protected override void OverlayUI()
    {
        var (currentExp, maxExp, level) = GetCurrentFCData();
        if (currentExp == 0 && maxExp == 0) return;

        ImGui.SetWindowSize(new(300f * GlobalFontScale, 120f * GlobalFontScale));
        
        ImGui.Text($"部队等级: {level}");
        ImGui.Text($"当前经验: {currentExp:N0}/{maxExp:N0}");
        
        if (maxExp > 0)
        {
            var progress = (float)currentExp / maxExp;
            ImGui.ProgressBar(progress, new(280f * GlobalFontScale, 20f * GlobalFontScale));
        }
        
        if (maxExp > currentExp)
        {
            var remaining = maxExp - currentExp;
            ImGui.Text($"升级还需: {remaining:N0}");
        }
        else if (maxExp > 0)
            ImGui.TextColored(new(0f, 1f, 0f, 1f), "已达到当前等级上限!");
    }

    private class Config : ModuleConfiguration
    {
        public bool IsEnabled = true;
        public bool ShowOverlay = true;
        public int MinExpGainThreshold = 1000;
    }
}
