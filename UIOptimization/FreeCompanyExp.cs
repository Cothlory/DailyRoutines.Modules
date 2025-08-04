using System;
using System.Numerics;
using DailyRoutines.Abstracts;
using DailyRoutines.Managers;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using KamiToolKit.Nodes;

namespace DailyRoutines.ModulesPublic;

public unsafe class FreeCompanyExp : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title = "更精确的部队经验",
        Description = "在部队界面显示部队当前经验和升级所需经验的精确数值",
        Category = ModuleCategories.UIOptimization,
        Author = ["Cothlory"]
    };

    private static TextNode? ExpTextNode;

    protected override void Init()
    {
        FrameworkManager.Register(OnUpdate, throttleMS: 30_000);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "FreeCompany", OnFreeCompanyAddonSetup);
        DService.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "FreeCompany", OnFreeCompanyAddonFinalize);
        
        if (IsAddonAndNodesReady(FreeCompany))
            OnFreeCompanyAddonSetup(AddonEvent.PostSetup, null);
    }

    protected override void Uninit()
    {
        FrameworkManager.Unregister(OnUpdate);
        DService.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "FreeCompany", OnFreeCompanyAddonSetup);
        DService.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "FreeCompany", OnFreeCompanyAddonFinalize);
        ExpTextNode?.Dispose();
        ExpTextNode = null;
        
        base.Uninit();
    }

    private void OnFreeCompanyAddonSetup(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)FreeCompany;
        if (addon == null) 
        {
            DService.Log.Warning("FreeCompanyExp: Addon is null");
            return;
        }
        
        DService.Log.Info("FreeCompanyExp: Addon setup, creating nodes");
        CreateExpNodes(addon);
        UpdateExpDisplay();
    }

    private void OnFreeCompanyAddonFinalize(AddonEvent type, AddonArgs args)
    {
        if (ExpTextNode != null)
        {
            Service.AddonController.DetachNode(ExpTextNode);
            ExpTextNode.Dispose();
            ExpTextNode = null;
        }
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (!DService.ClientState.IsLoggedIn) return;
        
        TryRefreshFCData();
        UpdateExpDisplay();
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
                
            var currentExp = (uint)fcExchangeArray->IntArray[6];
            var maxExp = (uint)fcExchangeArray->IntArray[7];
            var level = (byte)fcExchangeArray->IntArray[4];

            return (currentExp, maxExp, level);
        }
        catch (Exception ex)
        {
            DService.Log.Error($"Failed to get FC data: {ex.Message}");
            return (0, 0, 0);
        }
    }

    private unsafe void CreateExpNodes(AtkUnitBase* addon)
    {
        if (ExpTextNode != null) return;
        ExpTextNode?.Dispose();
        ExpTextNode = null;
        var rootNode = addon->RootNode;
        if (rootNode == null) return;
        
        // 原生UI样式 - node ID 13: text color = RGBA(204,204,204,255) | font size = 14
        var originalTextColor = new Vector4(204f/255f, 204f/255f, 204f/255f, 1f);
        byte originalFontSize = 14;
        
        ExpTextNode = new TextNode
        {
            Text = "经验值 0/0 (0.0%)",
            FontSize = originalFontSize,
            IsVisible = true,
            TextColor = originalTextColor,
            TextFlags = TextFlags.AutoAdjustNodeSize,
            AlignmentType = AlignmentType.Left,
            Position = new Vector2(350f, 125f),
            NodeId = 999001
        };
        
        try
        {
            Service.AddonController.AttachNode(ExpTextNode, rootNode);
        }
        catch (Exception ex)
        {
            DService.Log.Error($"FreeCompanyExp: Failed to create nodes: {ex.Message}");
            ExpTextNode?.Dispose();
            ExpTextNode = null;
        }
    }
    
    private void UpdateExpDisplay()
    {
        if (ExpTextNode == null) return;
        
        var (currentExp, maxExp, level) = GetCurrentFCData();
        if (currentExp == 0 && maxExp == 0) return;
        var progress = maxExp > 0 ? (float)currentExp / maxExp * 100 : 0f;
        ExpTextNode.Text = $"经验值 {currentExp:N0}/{maxExp:N0} ({progress:F1}%)";
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
            DService.Log.Debug($"Failed to refresh FC data: {ex.Message}");
        }
    }
}
