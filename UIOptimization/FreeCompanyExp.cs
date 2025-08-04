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
    private static uint lastCurrentExp = 0;
    private static uint lastMaxExp = 0;

    protected override void Init()
    {
        FrameworkManager.Register(OnUpdate, throttleMS: 1_000);
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
        lastCurrentExp = 0;
        lastMaxExp = 0;
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (!DService.ClientState.IsLoggedIn) return;
        
        if (IsAddonAndNodesReady(FreeCompany) && ExpTextNode != null)
        {
            TryRefreshFCData();
            var (currentExp, maxExp, level) = GetCurrentFCData();
            if (currentExp != lastCurrentExp || maxExp != lastMaxExp)
            {
                lastCurrentExp = currentExp;
                lastMaxExp = maxExp;
                UpdateExpDisplay();
            }
        }
    }

    private (uint currentExp, uint maxExp, byte level) GetCurrentFCData()
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

    private unsafe void CreateExpNodes(AtkUnitBase* addon)
    {
        if (ExpTextNode != null) return;
        ExpTextNode?.Dispose();
        ExpTextNode = null;
        var rootNode = addon->RootNode;
        if (rootNode == null) return;
        
        var originalTextColor = new Vector4(204f/255f, 204f/255f, 204f/255f, 1f);
        byte originalFontSize = 14;
        
        var expLevelTextNode = addon->GetNodeById(13);
        if (expLevelTextNode != null && expLevelTextNode->Type == NodeType.Text)
        {
            var textNode = (AtkTextNode*)expLevelTextNode;
            originalTextColor = new Vector4(
                textNode->TextColor.R / 255f,
                textNode->TextColor.G / 255f,
                textNode->TextColor.B / 255f,
                textNode->TextColor.A / 255f
            );
            originalFontSize = textNode->FontSize;
        }
        ModifyExpBar(addon);
        
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
        Service.AddonController.AttachNode(ExpTextNode, rootNode);
    }
    
    private unsafe void ModifyExpBar(AtkUnitBase* addon)
    {
        var currentExpBarNode = addon->GetNodeById(14);
        var originalExpBarWidth = currentExpBarNode != null ? currentExpBarNode->Width : 0;
        var expBarOffset = originalExpBarWidth;
        
        // 进度条
        if (currentExpBarNode != null)
        {
            var originalWidth = currentExpBarNode->Width;
            var newWidth = (ushort)(originalWidth * 2f);
            var originalX = currentExpBarNode->X;
            var newX = (short)(originalX - originalWidth);
            currentExpBarNode->SetWidth(newWidth);
            currentExpBarNode->SetXFloat(newX);
            var nineGridNode = (AtkNineGridNode*)currentExpBarNode;
            nineGridNode->AtkResNode.SetWidth(newWidth);
            nineGridNode->AtkResNode.SetXFloat(newX);
            currentExpBarNode->DrawFlags |= 0x1;
        }
        
        // 背景条
        var maxExpBarNode = addon->GetNodeById(15);
        if (maxExpBarNode != null)
        {
            var originalWidth = maxExpBarNode->Width;
            var newWidth = (ushort)(originalWidth * 2f);
            var originalX = maxExpBarNode->X;
            var newX = (short)(originalX - originalWidth);
            maxExpBarNode->SetWidth(newWidth);
            maxExpBarNode->SetXFloat(newX);
            var nineGridNode = (AtkNineGridNode*)maxExpBarNode;
            nineGridNode->AtkResNode.SetWidth(newWidth);
            nineGridNode->AtkResNode.SetXFloat(newX);
            maxExpBarNode->DrawFlags |= 0x1;
        }
        
        // 等级
        var expLevelTextNode = addon->GetNodeById(13);
        if (expLevelTextNode != null && expLevelTextNode->Type == NodeType.Text)
        {
            var originalX = expLevelTextNode->X;
            var newX = (short)(originalX - expBarOffset);
            expLevelTextNode->SetXFloat(newX);
            expLevelTextNode->DrawFlags |= 0x1;
        }
    }
    
    private void UpdateExpDisplay()
    {
        if (ExpTextNode == null) return;
        
        var (currentExp, maxExp, level) = GetCurrentFCData();
        if (currentExp == 0 && maxExp == 0) return;
        
        var progress = maxExp > 0 ? (float)currentExp / maxExp * 100 : 0f;
        var newText = $"经验值 {currentExp:N0}/{maxExp:N0} ({progress:F1}%)";
        
        ExpTextNode.Text = newText;
    }

    private void TryRefreshFCData()
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
}
