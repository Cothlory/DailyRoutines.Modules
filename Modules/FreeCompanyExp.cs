using System;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace DailyRoutines.ModulesPublic;

public unsafe class FreeCompanyExp : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "更精确的部队经验",
        Description = "在部队界面显示部队当前经验和升级所需经验，并在获得经验时进行提醒",
        Category = ModuleCategories.UIOptimization,
        Author = ["Cothlory"]
    };

    private Config ModuleConfig = null!;
    private uint LastFCExp;

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        FrameworkManager.Register(OnUpdate, throttleMS: 30_000);
        DService.Condition.ConditionChange += OnConditionChanged;
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "FreeCompany", OnFreeCompanyAddonSetup);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "FreeCompany", OnFreeCompanyAddonFinalize);
        Overlay ??= new(this);
        Overlay.IsOpen = false;
        UpdateFCExp();
    }

    protected override void Uninit()
    {
        FrameworkManager.Unregister(OnUpdate);
        DService.Condition.ConditionChange -= OnConditionChanged;
        DService.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "FreeCompany", OnFreeCompanyAddonSetup);
        DService.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "FreeCompany", OnFreeCompanyAddonFinalize);
        base.Uninit();
    }

    private void OnFreeCompanyAddonSetup(AddonEvent type, AddonArgs args)
    {
        if (Overlay != null)
            Overlay.IsOpen = true;
    }

    private void OnFreeCompanyAddonFinalize(AddonEvent type, AddonArgs args)
    {
        if (Overlay != null)
            Overlay.IsOpen = false;
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
            DService.Log.Error($"获取部队数据失败: {ex.Message}");
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
        if (ImGui.Checkbox("启用经验变化通知", ref ModuleConfig.IsEnabled))
            SaveConfig(ModuleConfig);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("监控部队经验变化并发送通知");
        ImGui.BeginDisabled(!ModuleConfig.IsEnabled);
        ImGui.SetNextItemWidth(200f * GlobalFontScale);
        if (ImGui.DragInt("通知阈值", ref ModuleConfig.MinExpGainThreshold, 10f, 1, 10000))
            SaveConfig(ModuleConfig);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("经验变化量超过此数值时发送通知");
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
        
        var progress = 0f;
        if (maxExp > 0)
            progress = (float)currentExp / maxExp;
        ImGui.Text($"{currentExp:N0}/{maxExp:N0} ({progress:P1}%)");
    }

    private class Config : ModuleConfiguration
    {
        public bool IsEnabled = true;
        public int MinExpGainThreshold = 1000;
    }
}
