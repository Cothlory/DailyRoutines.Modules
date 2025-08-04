using System;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;

namespace DailyRoutines.ModulesPublic;

public unsafe class FreeCompanyExp : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "更精确的部队经验",
        Description = "在部队界面显示部队当前经验和升级所需经验",
        Category = ModuleCategories.UIOptimization,
        Author = ["Cothlory"]
    };

    private Config ModuleConfig = null!;
    private Vector2 RelativePosition = new(10, 10);

    protected override void Init()
    {
        ModuleConfig = LoadConfig<Config>() ?? new();
        FrameworkManager.Register(OnUpdate, throttleMS: 30_000);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "FreeCompany", OnFreeCompanyAddonSetup);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "FreeCompany", OnFreeCompanyAddonFinalize);
        Overlay ??= new(this);
        Overlay.IsOpen = false;
        CheckFreeCompanyWindowStatus();
    }

    protected override void Uninit()
    {
        FrameworkManager.Unregister(OnUpdate);
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

            return (currentExp, maxExp, level);
        }
        catch (Exception ex)
        {
            DService.Log.Error($"获取部队数据失败: {ex.Message}");
            return (0, 0, 0);
        }
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

    private (Vector2 position, Vector2 size) GetFreeCompanyWindowInfo()
    {
        try
        {
            var stage = AtkStage.Instance();
            if (stage == null) return (Vector2.Zero, Vector2.Zero);

            var unitManager = stage->RaptureAtkUnitManager;
            if (unitManager == null) return (Vector2.Zero, Vector2.Zero);

            var fcAddon = unitManager->GetAddonByName("FreeCompany");
            if (fcAddon == null) return (Vector2.Zero, Vector2.Zero);

            var position = new Vector2(fcAddon->X, fcAddon->Y);
            var size = new Vector2(fcAddon->GetScaledWidth(true), fcAddon->GetScaledHeight(true));

            return (position, size);
        }
        catch (Exception ex)
        {
            DService.Log.Debug($"获取部队窗口信息失败: {ex.Message}");
            return (Vector2.Zero, Vector2.Zero);
        }
    }
    
    private void CheckFreeCompanyWindowStatus()
    {
        try
        {
            var stage = AtkStage.Instance();
            if (stage == null) return;

            var unitManager = stage->RaptureAtkUnitManager;
            if (unitManager == null) return;

            var fcAddon = unitManager->GetAddonByName("FreeCompany");
            if (fcAddon != null && fcAddon->IsVisible)
            {
                if (Overlay != null)
                    Overlay.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            DService.Log.Debug($"检查部队窗口状态失败: {ex.Message}");
        }
    }

    protected override void ConfigUI()
    {
        ImGui.Text("悬浮窗偏移:");
        ImGui.SetNextItemWidth(150f * GlobalFontScale);
        if (ImGui.DragFloat2("", ref RelativePosition, 1f, -500f, 500f))
        {
            ModuleConfig.RelativePositionX = RelativePosition.X;
            ModuleConfig.RelativePositionY = RelativePosition.Y;
            SaveConfig(ModuleConfig);
        }
        ImGui.SameLine();
        if (ImGui.Button("重置"))
        {
            RelativePosition = new(10, 10);
            ModuleConfig.RelativePositionX = RelativePosition.X;
            ModuleConfig.RelativePositionY = RelativePosition.Y;
            SaveConfig(ModuleConfig);
        }

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
        
        RelativePosition = new(ModuleConfig.RelativePositionX, ModuleConfig.RelativePositionY);
        var (fcWindowPos, fcWindowSize) = GetFreeCompanyWindowInfo();
        if (fcWindowPos == Vector2.Zero) return;
        
        var overlayPos = new Vector2(
            fcWindowPos.X + fcWindowSize.X + RelativePosition.X,
            fcWindowPos.Y + RelativePosition.Y
        );
        var overlaySize = new Vector2(280f * GlobalFontScale, 60f * GlobalFontScale);
        
        ImGui.SetNextWindowPos(overlayPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(overlaySize, ImGuiCond.Always);
        
        var flags = ImGuiWindowFlags.NoMove | 
                   ImGuiWindowFlags.NoResize | 
                   ImGuiWindowFlags.NoCollapse |
                   ImGuiWindowFlags.NoTitleBar |
                   ImGuiWindowFlags.AlwaysAutoResize;

        if (ImGui.Begin($"##FCExpOverlay_{GetHashCode()}", flags))
        {
            var progress = 0f;
            if (maxExp > 0)
                progress = (float)currentExp / maxExp;
                
            ImGui.Text($"{currentExp:N0} / {maxExp:N0} ({progress:P1}%)");
            if (maxExp > currentExp)
            {
                var remaining = maxExp - currentExp;
                ImGui.Text($"还需: {remaining:N0}");
            }
        }
        ImGui.End();
    }

    private class Config : ModuleConfiguration
    {
        public float RelativePositionX = 10f;
        public float RelativePositionY = 10f;
    }
}
